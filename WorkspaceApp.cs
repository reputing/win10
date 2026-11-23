using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

// Knows which windows belong to a pinned app, and how to start it.
internal sealed class WorkspaceApp
{
    internal readonly AppPin Pin;
    private readonly string executable,appId;

    private static readonly string[] DiscordFolders={"Discord","DiscordPTB","DiscordCanary"};
    private static readonly string[] ChromiumNames={"chrome.exe","msedge.exe","brave.exe","vivaldi.exe","opera.exe"};

    [DllImport("shlwapi.dll",CharSet=CharSet.Unicode)]
    private static extern int AssocQueryString(uint flags,uint key,string association,string verb,StringBuilder result,ref uint length);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(IntPtr process,ref uint length,StringBuilder result);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref int length);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    internal WorkspaceApp(AppPin pin)
    {
        Pin=pin;
        if(pin.Target=="builtin:web")executable=DefaultBrowser();
        else if(pin.Target=="builtin:files")executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe");
        else if(pin.Target.StartsWith("file:",StringComparison.Ordinal))
        {
            string path=pin.Target.Substring(5);
            executable=path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)?LaunchPath(path,false):path;
        }
        else if(pin.Target.StartsWith("app:",StringComparison.Ordinal))
        {
            appId=pin.Target.Substring(4);
            executable=LaunchPath(appId,true);
        }
    }

    internal string Name { get { return Pin.Name; } }
    internal bool IsDiscord { get { return Pin.Target=="builtin:discord"; } }

    // ---- Finding the app's window -------------------------------------------------------------------------------

    private static string Association(string name,uint flags)
    {
        uint size=32768;
        var buffer=new StringBuilder((int)size);
        return AssocQueryString(flags,2,name,"open",buffer,ref size)==0?buffer.ToString():null;
    }

    private static string DefaultBrowser()
    {
        string path=Association("https",0x1000);
        if(String.IsNullOrEmpty(path))
        {
            var progId=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice","ProgId",null) as string;
            if(!String.IsNullOrEmpty(progId))path=Association(progId,0);
        }
        return path;
    }

    private static string LaunchPath(string id,bool appsFolder)
    {
        dynamic shell=null,folder=null,item=null;
        try
        {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application",true));
            folder=shell.NameSpace(appsFolder?"shell:AppsFolder":Path.GetDirectoryName(id));
            if(folder==null)return null;
            item=folder.ParseName(appsFolder?id:Path.GetFileName(id));
            if(item==null)return null;
            string file=item.ExtendedProperty("System.Link.TargetParsingPath") as string;
            return String.IsNullOrWhiteSpace(file)?null:Environment.ExpandEnvironmentVariables(file);
        }
        catch(COMException){return null;}
        catch(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException){return null;}
        finally
        {
            foreach(object obj in new object[]{item,folder,shell})
                if(obj!=null && Marshal.IsComObject(obj))Marshal.ReleaseComObject(obj);
        }
    }

    private static string ImagePath(uint pid)
    {
        var process=OpenProcess(0x1000,false,pid);
        if(process==IntPtr.Zero)return null;
        try
        {
            var value=new StringBuilder(32768);
            int length=value.Capacity;
            return QueryFullProcessImageName(process,0,value,ref length)?value.ToString():null;
        }
        finally{CloseHandle(process);}
    }

    // UWP windows are hosted by ApplicationFrameHost; the app's own process owns the CoreWindow inside.
    private static uint OwnerProcess(DesktopWindow window)
    {
        if(window.ClassName!="ApplicationFrameWindow")return window.Pid;
        uint found=window.Pid;
        Native.EnumChildWindows(window.Handle,delegate(IntPtr child,IntPtr unused)
        {
            var name=new StringBuilder(64);
            Native.GetClassName(child,name,name.Capacity);
            if(name.ToString()!="Windows.UI.Core.CoreWindow")return true;
            uint pid;
            Native.GetWindowThreadProcessId(child,out pid);
            if(pid==window.Pid)return true;
            found=pid;
            return false;
        },IntPtr.Zero);
        return found;
    }

    private static bool InDiscordFolder(string path)
    {
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string file=Path.GetFileName(path);
        return DiscordFolders.Any(folder=>path.StartsWith(Path.Combine(local,folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) &&
            file.StartsWith("Discord",StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChromium(string path)
    {
        return path!=null && ChromiumNames.Contains(Path.GetFileName(path),StringComparer.OrdinalIgnoreCase);
    }

    internal bool Matches(DesktopWindow window)
    {
        string path=ImagePath(window.Pid);
        if(IsDiscord)
        {
            if(path!=null && InDiscordFolder(path))return true;
            // Without the desktop client, Discord runs as a browser app window titled "Discord".
            return IsChromium(path) && window.Title.IndexOf("Discord",StringComparison.OrdinalIgnoreCase)>=0;
        }
        if(!String.IsNullOrEmpty(executable) && path!=null && String.Equals(path,executable,StringComparison.OrdinalIgnoreCase))
            return Pin.Target!="builtin:files" || window.ClassName=="CabinetWClass" || window.ClassName=="ExploreWClass";
        if(appId==null)return false;
        var process=OpenProcess(0x1000,false,OwnerProcess(window));
        if(process==IntPtr.Zero)return false;
        try
        {
            uint count=512;
            var id=new StringBuilder((int)count);
            return GetApplicationUserModelId(process,ref count,id)==0 && String.Equals(appId,id.ToString(),StringComparison.OrdinalIgnoreCase);
        }
        finally{CloseHandle(process);}
    }

    // ---- Starting the app ---------------------------------------------------------------------------------------

    // Starts the app and says what it did, for the toast. Throws with a readable reason when nothing could be started.
    internal string Open()
    {
        if(IsDiscord)return OpenDiscord();
        if(Pin.Target=="builtin:web")
        {
            Shell.Open("https://www.google.com/");
            return "Opening Browser";
        }
        if(Pin.Target=="builtin:files")
        {
            Shell.Open("explorer.exe");
            return "Opening Files";
        }
        if(appId!=null)
        {
            if(appId.IndexOf('"')>=0)throw new InvalidDataException("Invalid application identifier.");
            Shell.Open("explorer.exe","\"shell:AppsFolder\\"+appId+"\"");
            return "Opening "+Pin.Name;
        }
        if(Pin.Target.StartsWith("file:",StringComparison.Ordinal))
        {
            string path=Pin.Target.Substring(5);
            if(!File.Exists(path))throw new FileNotFoundException(Pin.Name+" has moved. Unpin it and pin it again.",path);
            Shell.Open(path);
            return "Opening "+Pin.Name;
        }
        throw new InvalidOperationException("Bluehour does not know how to open "+Pin.Name+".");
    }

    private static string DiscordUpdater()
    {
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach(var folder in DiscordFolders)
        {
            string updater=Path.Combine(local,folder,"Update.exe");
            if(File.Exists(updater))return updater;
        }
        return null;
    }

    private static string StartMenuShortcut(string prefix)
    {
        foreach(var root in new[]{Environment.SpecialFolder.Programs,Environment.SpecialFolder.CommonPrograms})
        {
            string dir=Environment.GetFolderPath(root);
            if(String.IsNullOrEmpty(dir) || !Directory.Exists(dir))continue;
            // Discord's installer puts its shortcut one folder down ("Discord Inc").
            var places=new System.Collections.Generic.List<string>{dir};
            try{places.AddRange(Directory.GetDirectories(dir));}
            catch(IOException){}
            catch(UnauthorizedAccessException){}
            foreach(var place in places)
            {
                try
                {
                    var link=Directory.GetFiles(place,prefix+"*.lnk").FirstOrDefault();
                    if(link!=null)return link;
                }
                catch(IOException){}
                catch(UnauthorizedAccessException){}
            }
        }
        return null;
    }

    private static string ChromiumBrowser()
    {
        string preferred=DefaultBrowser();
        if(IsChromium(preferred) && File.Exists(preferred))return preferred;
        foreach(var name in new[]{"msedge.exe","chrome.exe"})
            foreach(var hive in new[]{"HKEY_LOCAL_MACHINE","HKEY_CURRENT_USER"})
            {
                var registered=Registry.GetValue(hive+@"\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"+name,"",null) as string;
                if(!String.IsNullOrEmpty(registered))registered=registered.Trim('"');
                if(!String.IsNullOrEmpty(registered) && File.Exists(registered))return registered;
            }
        string edge=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),@"Microsoft\Edge\Application\msedge.exe");
        return File.Exists(edge)?edge:null;
    }

    private string OpenDiscord()
    {
        // 1. The installed client, through Discord's own updater/launcher (Stable, PTB or Canary).
        string updater=DiscordUpdater();
        if(updater!=null)
        {
            string folder=Path.GetDirectoryName(updater);
            var start=new ProcessStartInfo(updater,"--processStart "+Path.GetFileName(folder)+".exe"){UseShellExecute=true,WorkingDirectory=folder};
            using(Process.Start(start)){}
            return "Opening Discord";
        }
        // 2. An install somewhere else that still registers the discord:// protocol.
        using(var key=Registry.ClassesRoot.OpenSubKey(@"discord\shell\open\command"))
            if(key!=null){Shell.Open("discord://");return "Opening Discord";}
        // 3. A Start menu shortcut.
        string shortcut=StartMenuShortcut("Discord");
        if(shortcut!=null){Shell.Open(shortcut);return "Opening Discord";}
        // 4. No client: Discord's web app in its own window, which Bluehour can frame like any other app.
        string browser=ChromiumBrowser();
        if(browser!=null)
        {
            Shell.Open(browser,"--app=https://discord.com/app");
            return "Discord isn't installed, so this is the web app";
        }
        Shell.Open("https://discord.com/app");
        return "Discord isn't installed. Opening the web app in your browser";
    }
}
