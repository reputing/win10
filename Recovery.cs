using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

public sealed class PreviousValue
{
    public string Key, Name, Text;
    public int Kind;
    public bool Existed;
}

public sealed class UsageEntry
{
    public string Key;
    public int Count;
    public long Last;
}

public sealed class RemovedApp
{
    public string Name, StoreId;
}

public sealed class WallpaperBackup
{
    public string Monitor, Original, Backup;
}

public sealed class DesktopState
{
    public bool Desktop, Dock, Seconds, Hover, Auto=true, WallpaperDisabled, AutoHideTaskbar=true, WinKey=true;
    public List<AppPin> Pins=new List<AppPin>();
    public bool PinsInitialized, SeededInstalled;
    public string SurfaceImage="";
    public string SurfaceVideo="";   // a live wallpaper (an MP4 or similar), or empty
    public int TaskbarBefore=-1;
    // Bluehour replaces the Windows taskbar with its dock: the taskbar windows are hidden while the desktop is up.
    public bool ReplaceTaskbar=true, TaskbarHidden;
    // What is shown. Each is "hide" so that older journals, which know nothing of them, keep everything.
    public bool HideClock, HideGreeting, HideDate, HideLayouts, HideSearch, HideSettings, HideStickers, HideDockSystem, HideDockApps, ShowMeter;
    public string DockSize="Medium", CustomAccent="#C9A4FF", WindowAnimation="Quick";
    // The game optimizer: what to do when a game comes to the front, and the games it knows.
    public bool AutoOptimize=true, OptPriority=true, OptBackground=true, OptMemory=true, OptPower=true;
    public List<GameProfile> Games=new List<GameProfile>();
    public List<WallpaperBackup> Wallpapers=new List<WallpaperBackup>();
    // Revision 4 introduced the Kuro desktop: tiling on by default, the island dock at the bottom, new accent names.
    public int Revision;
    // Window frames: "Native" lets Windows 11 draw the border and corners (no extra windows, no CPU), "Sticker" draws
    // Bluehour's own frame with an overlay window, "Off" leaves windows alone apart from tiling them.
    public string Frames="Native";
    public bool Animate=true, GameMode=true, ImportDesktop=true;
    public string PowerBefore="";
    public List<string> Dismissed=new List<string>();
    public List<UsageEntry> Usage=new List<UsageEntry>();
    public string Edge="Bottom", Monitor="", Layout="Grid", Accent="Lavender", Point="";
    public List<PreviousValue> Changes=new List<PreviousValue>();
    public List<WindowBackup> Positions=new List<WindowBackup>();
    public List<RemovedApp> Removed=new List<RemovedApp>();
}

internal sealed class Recovery
{
    public const string Run=@"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Theme=@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Game=@"System\GameConfigStore";
    private const string Desktop=@"Control Panel\Desktop";
    public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BluehourDesktop");
    private readonly string journal;
    private readonly XmlSerializer serializer=new XmlSerializer(typeof(DesktopState));
    public DesktopState State { get; private set; }

    public Recovery(string directory)
    {
        journal=Path.Combine(directory,"desktop.xml");
        State=new DesktopState();
        bool existing=File.Exists(journal);
        if (existing)
            using (var input=File.OpenRead(journal)) State=(DesktopState)serializer.Deserialize(input);
        if (State.Revision<5)
        {
            // Revision 5: frames default to the lightest style the system supports.
            State.Frames=Environment.OSVersion.Version.Build>=22000?"Native":"Off";
        }
        if (State.Revision<4)
        {
            if (existing)
            {
                State.Auto=true;
                State.Edge="Bottom";
                State.Accent=State.Accent=="Sea Glass"?"Mint":State.Accent=="Apricot"?"Sakura":"Lavender";
            }
            State.Revision=4;
        }
        State.Revision=5;
        Ui.UseCustom(State.CustomAccent);
        Ui.Theme(State.Accent);
        Motion.Speed=State.WindowAnimation;
    }

    // Whether Bluehour is set to start when you sign in.
    public bool RunsAtSignIn
    {
        get { using (var key=Registry.CurrentUser.OpenSubKey(Run)) return key!=null && key.GetValue("BluehourDesktop")!=null; }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(journal));
        string pending=journal+".tmp";
        using (var output=new FileStream(pending,FileMode.Create,FileAccess.Write,FileShare.None))
        {
            serializer.Serialize(output,State);
            output.Flush(true);
        }
        if (File.Exists(journal)) File.Replace(pending,journal,journal+".previous",true);
        else File.Move(pending,journal);
    }

    public void Remember(string path,string name)
    {
        if (State.Changes.Any(v=>v.Key==path && v.Name==name)) return;
        using (var key=Registry.CurrentUser.OpenSubKey(path))
        {
            var current=key==null ? null : key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (current!=null && !(current is string) && !(current is int))
                throw new InvalidOperationException("Unsupported existing registry value for "+name+".");
            State.Changes.Add(new PreviousValue {
                Key=path,Name=name,Existed=current!=null,
                Kind=current==null ? 0 : (int)key.GetValueKind(name),
                Text=current==null ? "" : Convert.ToString(current,System.Globalization.CultureInfo.InvariantCulture)
            });
        }
        try { Save(); }
        catch { State.Changes.RemoveAt(State.Changes.Count-1); throw; }
    }

    public void Set(string path,string name,object value,RegistryValueKind kind)
    {
        Remember(path,name);
        using (var key=Registry.CurrentUser.CreateSubKey(path))
        {
            if (value==null) key.DeleteValue(name,false);
            else key.SetValue(name,value,kind);
        }
    }

    public void SetTheme(bool dark)
    {
        Set(Theme,"AppsUseLightTheme",dark?0:1,RegistryValueKind.DWord);
        Set(Theme,"SystemUsesLightTheme",dark?0:1,RegistryValueKind.DWord);
        Native.RefreshTheme();
    }

    public void SetFramePalette(Color accent)
    {
        const string path=@"Software\Microsoft\Windows\DWM";
        int packed=unchecked((int)0xff000000)|accent.R|(accent.G<<8)|(accent.B<<16);
        Set(path,"AccentColor",packed,RegistryValueKind.DWord);
        Set(path,"AccentColorInactive",unchecked((int)0xff382921),RegistryValueKind.DWord);
        Set(path,"ColorPrevalence",1,RegistryValueKind.DWord);
        SetTheme(true);
    }

    public void SetQuiet()
    {
        Set(Game,"GameDVR_Enabled",0,RegistryValueKind.DWord);
        Set(@"Software\Microsoft\Windows\CurrentVersion\GameDVR","AppCaptureEnabled",0,RegistryValueKind.DWord);
    }

    // Standard Windows settings that lower background load while gaming. Each goes through Set, so the previous value is
    // journaled and "Revert Bluehour changes" puts it back.
    public void SetGaming()
    {
        const string bar=@"Software\Microsoft\GameBar";
        Set(bar,"AutoGameModeEnabled",1,RegistryValueKind.DWord);
        Set(bar,"AllowAutoGameMode",1,RegistryValueKind.DWord);
        Set(bar,"UseNexusForGameBarEnabled",0,RegistryValueKind.DWord);   // the controller button no longer opens Game Bar
        Set(bar,"ShowStartupPanel",0,RegistryValueKind.DWord);
        SetQuiet();                                                       // Game DVR background capture off
    }

    // Window and taskbar transparency makes Windows composite every translucent layer on every frame.
    public void SetTransparency(bool on)
    {
        Set(Theme,"EnableTransparency",on?1:0,RegistryValueKind.DWord);
        Native.RefreshTheme();
    }

    // "Enhance pointer precision" is mouse acceleration: the cursor moves further the faster the mouse moves.
    public void SetMouseAcceleration(bool on)
    {
        const string mouse=@"Control Panel\Mouse";
        string[] values=on?new[]{"1","6","10"}:new[]{"0","0","0"};
        Set(mouse,"MouseSpeed",values[0],RegistryValueKind.String);
        Set(mouse,"MouseThreshold1",values[1],RegistryValueKind.String);
        Set(mouse,"MouseThreshold2",values[2],RegistryValueKind.String);
        // SPI_SETMOUSE applies it now: threshold 1, threshold 2, acceleration on/off.
        Native.SetMouseParams(0x0004,0,new[]{int.Parse(values[1]),int.Parse(values[2]),on?1:0},3);
    }

    public string WallpaperPath()
    {
        string file=Path.Combine(Root,"bluehour-alpine.jpg");
        if (File.Exists(file)) return file;
        using (var asset=Assembly.GetExecutingAssembly().GetManifestResourceStream("Bluehour.Coast"))
        {
            if (asset==null) return null;
            Directory.CreateDirectory(Root);
            string pending=file+".tmp";
            using (var output=File.Create(pending)) asset.CopyTo(output);
            File.Move(pending,file);
        }
        return file;
    }

    public void SetWallpaper(string path)
    {
        if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("Choose a wallpaper image first.");
        var wall=DesktopWallpaper.Open();
        try
        {
            uint status;
            wall.GetStatus(out status);
            if ((status&2)!=0) throw new InvalidOperationException("Switch Windows Background from Slideshow to Picture first.");
            var monitors=MonitorPaths(wall);
            if (monitors.Length==0) throw new InvalidOperationException("Windows did not report a connected display.");
            var added=new List<WallpaperBackup>();
            foreach (var monitor in monitors)
            {
                if (State.Wallpapers.Any(w=>w.Monitor==monitor)) continue;
                string original;
                wall.GetWallpaper(monitor,out original);
                string backup="";
                if (!String.IsNullOrEmpty(original))
                {
                    if (!File.Exists(original)) throw new InvalidOperationException("Windows could not open the previous wallpaper for backup.");
                    Directory.CreateDirectory(Root);
                    string extension=Path.GetExtension(original);
                    if (String.IsNullOrEmpty(extension)) extension=".jpg";
                    backup=Path.Combine(Root,"wallpaper-"+Guid.NewGuid().ToString("N")+extension);
                    File.Copy(original,backup);
                }
                added.Add(new WallpaperBackup {Monitor=monitor,Original=original,Backup=backup});
            }
            bool previous=State.WallpaperDisabled;
            if (State.Wallpapers.Count==0) State.WallpaperDisabled=(status&1)==0;
            State.Wallpapers.AddRange(added);
            try { Save(); }
            catch
            {
                foreach (var item in added) State.Wallpapers.Remove(item);
                State.WallpaperDisabled=previous;
                throw;
            }
            wall.SetWallpaper(null,path);
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(wall); }
    }

    private static string[] MonitorPaths(IDesktopWallpaper wall)
    {
        uint count;
        wall.GetMonitorDevicePathCount(out count);
        var monitors=new string[count];
        for (uint i=0;i<count;i++) wall.GetMonitorDevicePathAt(i,out monitors[i]);
        return monitors;
    }

    private static string RestorePath(WallpaperBackup item)
    {
        if (String.IsNullOrEmpty(item.Backup)) return "";
        if (String.IsNullOrEmpty(item.Original) || !File.Exists(item.Original)) return item.Backup;
        using (var hash=System.Security.Cryptography.SHA256.Create())
        using (var original=File.OpenRead(item.Original))
        using (var backup=File.OpenRead(item.Backup))
            return hash.ComputeHash(original).SequenceEqual(hash.ComputeHash(backup))?item.Original:item.Backup;
    }

    public string Restore()
    {
        var warnings=new List<string>();
        foreach (var item in State.Changes.ToArray().Reverse())
        {
            try
            {
                using (var key=Registry.CurrentUser.CreateSubKey(item.Key))
                {
                    if (item.Existed)
                    {
                        var kind=(RegistryValueKind)item.Kind;
                        object value=kind==RegistryValueKind.DWord ? (object)Int32.Parse(item.Text,System.Globalization.CultureInfo.InvariantCulture) : item.Text;
                        key.SetValue(item.Name,value,kind);
                    }
                    else key.DeleteValue(item.Name,false);
                }
                State.Changes.Remove(item);
                Save();
            }
            catch (Exception ex) { warnings.Add(item.Name+" — "+ex.Message); }
        }
        if (State.Wallpapers.Count>0)
        {
            try
            {
                var wall=DesktopWallpaper.Open();
                try
                {
                    var monitors=MonitorPaths(wall);
                    foreach (var item in State.Wallpapers.ToArray())
                    {
                        try
                        {
                            if (!monitors.Contains(item.Monitor))
                                throw new InvalidOperationException("Reconnect the original display and retry.");
                            string file=RestorePath(item);
                            if (file!="") wall.SetWallpaper(item.Monitor,file);
                            if (State.WallpaperDisabled) wall.Enable(false);
                            State.Wallpapers.Remove(item);
                            try { Save(); }
                            catch { State.Wallpapers.Add(item); throw; }
                        }
                        catch (Exception ex) { warnings.Add("Wallpaper — "+ex.Message); }
                    }
                }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(wall); }
            }
            catch (Exception ex) { warnings.Add("Wallpaper — "+ex.Message); }
        }
        State.SurfaceImage="";
        State.Desktop=false;
        State.Dock=false;
        Save();
        Native.RefreshTheme();
        if (State.Removed.Count>0) warnings.Add("Use Cleanup → Restore removed apps for any removed Store app.");
        return String.Join(Environment.NewLine,warnings);
    }

    public static string[] Startup()
    {
        using (var key=Registry.CurrentUser.OpenSubKey(Run))
            return key==null ? new string[0] : key.GetValueNames().Where(name=>{
                var kind=key.GetValueKind(name);
                return name!="BluehourDesktop" && (kind==RegistryValueKind.String || kind==RegistryValueKind.ExpandString);
            }).OrderBy(name=>name).ToArray();
    }

    public static int PowerShell(string script,bool elevated)
    {
        string encoded=Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; try { "+script+
            " } catch { Write-Error -ErrorAction Continue $_; exit 1 }"));
        string systemPowerShell=Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe");
        var options=new ProcessStartInfo(systemPowerShell,"-NoProfile -NonInteractive -EncodedCommand "+encoded);
        options.UseShellExecute=elevated;
        options.WindowStyle=ProcessWindowStyle.Hidden;
        options.CreateNoWindow=!elevated;
        if (elevated) options.Verb="runas";
        using (var process=Process.Start(options))
        {
            process.WaitForExit();
            return process.ExitCode;
        }
    }

    public static bool MakePoint(string name)
    {
        if (!ValidPoint(name)) throw new ArgumentException("Invalid restore point name.");
        return PowerShell("Checkpoint-Computer -Description '"+name+"' -RestorePointType MODIFY_SETTINGS; "+
            "if (-not (Get-ComputerRestorePoint | Where-Object Description -eq '"+name+"')) { exit 3 }; exit 0",true)==0;
    }

    public static bool ValidPoint(string name)
    {
        Guid result;
        return name!=null && name.StartsWith("Bluehour ",StringComparison.Ordinal) && Guid.TryParseExact(name.Substring(9),"N",out result);
    }

    public static readonly string[][] OptionalApps={
        new [] {"Solitaire Collection","Microsoft.MicrosoftSolitaireCollection","9WZDNCRFHWD2"},
        new [] {"Clipchamp","Clipchamp.Clipchamp","9P1J8S7CCWWT"},
        new [] {"Maps","Microsoft.WindowsMaps","9WZDNCRDTBVB"},
        new [] {"Feedback Hub","Microsoft.WindowsFeedbackHub","9NBLGGH4R32N"}
    };

    public static bool IsInstalled(string name)
    {
        if (!OptionalApps.Any(a=>a[1]==name)) return false;
        int result=PowerShell("if (Get-AppxPackage -Name '"+name+"') { exit 0 }; exit 4",false);
        if (result==4) return false;
        if (result!=0) throw new InvalidOperationException("Windows could not check Appx packages.");
        return true;
    }

    public static bool RemoveApp(string name)
    {
        if (!OptionalApps.Any(a=>a[1]==name)) return false;
        return PowerShell("Get-AppxPackage -Name '"+name+"' | Remove-AppxPackage; "+
            "if (Get-AppxPackage -Name '"+name+"') { exit 3 }; exit 0",false)==0;
    }
}

[System.Runtime.InteropServices.ComImport]
[System.Runtime.InteropServices.Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    void SetWallpaper([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string monitor,
                      [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string file);
    void GetWallpaper([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string monitor,
                      [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] out string file);
    void GetMonitorDevicePathAt(uint index,[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] out string monitor);
    void GetMonitorDevicePathCount(out uint count);
    void GetMonitorRECT();
    void SetBackgroundColor();
    void GetBackgroundColor();
    void SetPosition();
    void GetPosition();
    void SetSlideshow();
    void GetSlideshow();
    void SetSlideshowOptions();
    void GetSlideshowOptions();
    void AdvanceSlideshow();
    void GetStatus(out uint status);
    void Enable([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool enabled);
}

internal static class DesktopWallpaper
{
    public static IDesktopWallpaper Open()
    {
        var cls=Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"),true);
        return (IDesktopWallpaper)Activator.CreateInstance(cls);
    }
}








