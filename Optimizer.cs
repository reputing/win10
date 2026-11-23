using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

// One game Bluehour knows about, and how hard it should work for it.
public sealed class GameProfile
{
    public string Name="", Exe="", Folder="", Level="Max", Last="";
    public bool HighGpu, NoFullscreenOpt;
    public int Sessions;

    // True when `program` (a full path) is this game: its exe, or anything inside its install folder.
    internal bool Matches(string program)
    {
        if(String.IsNullOrEmpty(program))return false;
        if(Exe!="" && String.Equals(Exe,program,StringComparison.OrdinalIgnoreCase))return true;
        return Folder!="" && program.StartsWith(Folder.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
    }
}

// Finding games, and the registry details of per-game settings. The parsing is plain string work so it can be tested.
internal static class GameLibrary
{
    internal const string GpuKey=@"Software\Microsoft\DirectX\UserGpuPreferences";
    internal const string CompatKey=@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    internal const string NoFullscreenFlag="DISABLEDXMAXIMIZEDWINDOWEDMODE";
    private static readonly string[] GameFolders={@"\steamapps\",@"\epic games\",@"\xboxgames\",@"\riot games\",@"\gog galaxy\games\",
        @"\gog games\",@"\ea games\",@"\origin games\",@"\ubisoft game launcher\games\",@"\battle.net\",@"\games\"};

    // A full-screen program is treated as a game when Windows says it is a Direct3D game, or it lives in a game library.
    internal static bool LooksLikeGame(string program,int state)
    {
        if(state==3)return true;
        string lower=(program??"").ToLowerInvariant();
        return GameFolders.Any(f=>lower.Contains(f));
    }

    internal static string NameOf(string program)
    {
        string name=Path.GetFileNameWithoutExtension(program??"");
        name=Regex.Replace(name,@"([-_](win(64|32)?|shipping|x64|x86))+$","",RegexOptions.IgnoreCase);
        name=Regex.Replace(name,@"([a-z])([A-Z])","$1 $2");
        return name.Length==0?"Game":name;
    }

    // ---- Steam and Epic ---------------------------------------------------------------------------------------

    internal static List<string> ParseSteamLibraries(string vdf)
    {
        var list=new List<string>();
        foreach(System.Text.RegularExpressions.Match match in Regex.Matches(vdf??"","\"path\"\\s+\"([^\"]+)\""))
            list.Add(match.Groups[1].Value.Replace(@"\\",@"\"));
        return list;
    }

    internal static bool ParseSteamManifest(string acf,out string name,out string installDir)
    {
        var n=Regex.Match(acf??"","\"name\"\\s+\"([^\"]*)\"");
        var d=Regex.Match(acf??"","\"installdir\"\\s+\"([^\"]*)\"");
        name=n.Success?n.Groups[1].Value:"";installDir=d.Success?d.Groups[1].Value:"";
        return name!="" && installDir!="";
    }

    internal static bool ParseEpicItem(string json,out string name,out string location,out string executable)
    {
        Func<string,string> field=key=>{var m=Regex.Match(json??"","\""+key+"\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");return m.Success?m.Groups[1].Value.Replace(@"\\",@"\").Replace(@"\/","/"):"";};
        name=field("DisplayName");location=field("InstallLocation");executable=field("LaunchExecutable");
        return name!="" && location!="";
    }

    private static readonly string[] SkipNames={"unins","setup","crash","report","redist","vc_redist","dxsetup","prereq","launcher","helper","updater","install","eac","battleye"};

    // The program most likely to be the game itself: the biggest .exe within a few folders of the install folder.
    internal static string BestExe(string folder)
    {
        string best=null;long size=0;
        Action<string,int> walk=null;
        walk=(dir,depth)=>
        {
            try
            {
                foreach(var file in Directory.GetFiles(dir,"*.exe"))
                {
                    string lower=Path.GetFileName(file).ToLowerInvariant();
                    if(SkipNames.Any(lower.Contains))continue;
                    long length=new FileInfo(file).Length;
                    if(length>size){size=length;best=file;}
                }
                if(depth>=3)return;
                foreach(var sub in Directory.GetDirectories(dir))
                {
                    string lower=Path.GetFileName(sub).ToLowerInvariant();
                    if(lower.Contains("redist")||lower.Contains("directx")||lower.Contains("anticheat")||lower.Contains("battleye")||lower=="_commonredist")continue;
                    walk(sub,depth+1);
                }
            }
            catch(IOException){}
            catch(UnauthorizedAccessException){}
        };
        if(Directory.Exists(folder))walk(folder,0);
        return best;
    }

    // Looks in the Steam and Epic libraries for installed games. Slow on a big library, so call it off the UI thread.
    internal static List<GameProfile> Scan()
    {
        var found=new List<GameProfile>();
        try
        {
            var roots=new List<string>();
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                var path=key==null?null:key.GetValue("SteamPath") as string;
                if(!String.IsNullOrEmpty(path))roots.Add(path.Replace('/','\\'));
            }
            string x86=Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            if(!String.IsNullOrEmpty(x86))roots.Add(Path.Combine(x86,"Steam"));
            foreach(var root in roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            {
                string vdf=Path.Combine(root,@"steamapps\libraryfolders.vdf");
                if(File.Exists(vdf))roots.AddRange(ParseSteamLibraries(File.ReadAllText(vdf)));
            }
            foreach(var library in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string apps=Path.Combine(library,"steamapps");
                if(!Directory.Exists(apps))continue;
                foreach(var manifest in Directory.GetFiles(apps,"appmanifest_*.acf"))
                {
                    string name,dir;
                    if(!ParseSteamManifest(File.ReadAllText(manifest),out name,out dir))continue;
                    if(name.StartsWith("Steamworks",StringComparison.OrdinalIgnoreCase) || name.IndexOf("Redistributable",StringComparison.OrdinalIgnoreCase)>=0)continue;
                    string folder=Path.Combine(apps,"common",dir);
                    if(Directory.Exists(folder))found.Add(new GameProfile{Name=name,Folder=folder,Exe=BestExe(folder)??""});
                }
            }
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
        catch(System.Security.SecurityException){}
        try
        {
            string epic=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"Epic\EpicGamesLauncher\Data\Manifests");
            if(Directory.Exists(epic))
                foreach(var item in Directory.GetFiles(epic,"*.item"))
                {
                    string name,location,executable;
                    if(!ParseEpicItem(File.ReadAllText(item),out name,out location,out executable))continue;
                    found.Add(new GameProfile{Name=name,Folder=location,Exe=executable==""?(BestExe(location)??""):Path.Combine(location,executable)});
                }
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
        return found;
    }

    // ---- Per-game Windows settings ----------------------------------------------------------------------------

    internal static string AddFlag(string existing,string flag)
    {
        existing=(existing??"").Trim();
        if(existing.Split(' ').Contains(flag))return existing;
        if(existing=="")return "~ "+flag;
        return existing.StartsWith("~")?existing+" "+flag:"~ "+existing+" "+flag;
    }

    // Returns the value without the flag, or null when nothing meaningful is left.
    internal static string RemoveFlag(string existing,string flag)
    {
        var parts=(existing??"").Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries).Where(p=>p!=flag).ToList();
        return parts.Count==0||(parts.Count==1&&parts[0]=="~")?null:String.Join(" ",parts);
    }

    internal static string Read(string path,string name)
    {
        using(var key=Registry.CurrentUser.OpenSubKey(path))
            return key==null?null:key.GetValue(name) as string;
    }
}

// What happens around a game: its priority goes up, background apps step back, memory is handed over, and all of it is
// put back when the game ends. Nothing here is permanent, and nothing is closed or suspended.
internal sealed class Optimizer
{
    private sealed class Lowered { internal int Pid; internal long Start; internal ProcessPriorityClass Before; }

    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr process);

    // Names that are never touched: Windows itself, audio, anti-cheat, the stores a game launches through, voice and capture.
    private static readonly string[] Always={"system","idle","registry","memory compression","csrss","smss","wininit","winlogon","services","lsass",
        "svchost","dwm","explorer","fontdrvhost","audiodg","ctfmon","sihost","taskhostw","runtimebroker","shellexperiencehost","startmenuexperiencehost",
        "searchhost","searchapp","textinputhost","applicationframehost","securityhealthsystray","securityhealthservice","msmpeng","nissrv","bluehourdesktop",
        "discord","obs","obs64","obs32","teamspeak","ts3client_win64","mumble","voicemeeter","voicemeeterpro","nvcontainer","nvdisplay.container",
        "nvspcaps64","gamebar","gamebarftserver","gamebarpresencewriter"};
    private static readonly string[] Contains={"steam","epicgames","anticheat","battleye","vanguard","vgc","vgtray","riot","battle.net","ubisoft","upc","galaxyclient","playnite","xbox","gaming"};

    private readonly object gate=new object();
    private readonly List<Lowered> lowered=new List<Lowered>();
    private Process game;
    private ProcessPriorityClass gameBefore;
    private bool raised;
    internal bool Active { get; private set; }

    internal static bool IsProtected(string name)
    {
        string lower=(name??"").ToLowerInvariant();
        return Always.Contains(lower) || Contains.Any(lower.Contains);
    }

    private static long Available()
    {
        var memory=new Native.Memory {Length=(uint)Marshal.SizeOf(typeof(Native.Memory))};
        return Native.GlobalMemoryStatusEx(ref memory)?(long)memory.AvailablePhysical:0;
    }

    private static bool Trim(int pid)
    {
        var handle=OpenProcess(0x0100|0x0400,false,pid);   // PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION
        if(handle==IntPtr.Zero)return false;
        try{return EmptyWorkingSet(handle);}
        finally{CloseHandle(handle);}
    }

    // Starts a session for the game with this process id. Takes a moment: call it off the UI thread. Returns what it did.
    internal string Begin(int pid,string level,bool priority,bool background,bool memory)
    {
        lock(gate)
        {
            EndLocked();
            Active=true;
            var notes=new List<string>();
            int own=Process.GetCurrentProcess().Id,session=Process.GetCurrentProcess().SessionId;
            string path=FullScreen.ProgramOf((uint)pid);
            string folder=path==null?null:Path.GetDirectoryName(path);
            long before=Available();
            try
            {
                game=Process.GetProcessById(pid);
                if(priority)
                {
                    gameBefore=game.PriorityClass;
                    game.PriorityClass=level=="Max"?ProcessPriorityClass.High:ProcessPriorityClass.AboveNormal;
                    raised=true;
                    notes.Add("game priority up");
                }
            }
            catch(ArgumentException){}
            catch(InvalidOperationException){}
            catch(Win32Exception){}
            int stepped=0,trimmed=0;
            foreach(var process in Process.GetProcesses())
            {
                try
                {
                    if(process.Id==pid || process.Id==own || process.Id<=4 || process.SessionId!=session || IsProtected(process.ProcessName))continue;
                    string where=FullScreen.ProgramOf((uint)process.Id);
                    // The game's own helpers, launcher and anti-cheat live beside it: leave them alone.
                    if(folder!=null && where!=null && where.StartsWith(folder,StringComparison.OrdinalIgnoreCase))continue;
                    if(background && level=="Max" && process.PriorityClass==ProcessPriorityClass.Normal)
                    {
                        lowered.Add(new Lowered{Pid=process.Id,Start=process.StartTime.ToUniversalTime().Ticks,Before=ProcessPriorityClass.Normal});
                        process.PriorityClass=ProcessPriorityClass.BelowNormal;
                        stepped++;
                    }
                    if(memory && Trim(process.Id))trimmed++;
                }
                catch(InvalidOperationException){}
                catch(Win32Exception){}
                catch(NotSupportedException){}
                finally{process.Dispose();}
            }
            if(memory)Mem.Trim();
            if(stepped>0)notes.Add(stepped+" background apps stepped back");
            long freed=(Available()-before)/(1024*1024);
            if(memory && trimmed>0)notes.Add(freed>50?freed+" MB freed":"memory handed over");
            return notes.Count==0?"nothing to change":String.Join(", ",notes);
        }
    }

    internal void End(){lock(gate)EndLocked();}

    private void EndLocked()
    {
        foreach(var item in lowered)
        {
            try
            {
                using(var process=Process.GetProcessById(item.Pid))
                    if(process.StartTime.ToUniversalTime().Ticks==item.Start)process.PriorityClass=item.Before;
            }
            catch(ArgumentException){}
            catch(InvalidOperationException){}
            catch(Win32Exception){}
        }
        lowered.Clear();
        if(game!=null)
        {
            try{if(raised && !game.HasExited)game.PriorityClass=gameBefore;}
            catch(InvalidOperationException){}
            catch(Win32Exception){}
            game.Dispose();
            game=null;
        }
        raised=false;Active=false;
    }

    // Frees memory from every other app right now, without waiting for a game. Returns how many megabytes came back.
    internal static long BoostNow()
    {
        int own=Process.GetCurrentProcess().Id,session=Process.GetCurrentProcess().SessionId;
        long before=Available();
        foreach(var process in Process.GetProcesses())
        {
            try
            {
                if(process.Id==own || process.Id<=4 || process.SessionId!=session)continue;
                Trim(process.Id);
            }
            catch(InvalidOperationException){}
            catch(Win32Exception){}
            catch(NotSupportedException){}
            finally{process.Dispose();}
        }
        Mem.Trim();
        return Math.Max(0,(Available()-before)/(1024*1024));
    }
}
