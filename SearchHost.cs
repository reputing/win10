using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

// Everything the launcher can find, and how it ranks it.
internal sealed partial class Shell
{
    private List<SearchItem> catalog;
    private int libraryTicks;

    private static readonly string[][] SettingsPages=
    {
        new[]{"Display","ms-settings:display","Resolution, scale and refresh rate"},
        new[]{"Sound","ms-settings:sound","Volume, devices and outputs"},
        new[]{"Bluetooth & devices","ms-settings:bluetooth","Pair and manage devices"},
        new[]{"Network & internet","ms-settings:network","Wi-Fi, Ethernet and VPN"},
        new[]{"Wi-Fi","ms-settings:network-wifi","Wireless networks"},
        new[]{"Personalization","ms-settings:personalization","Background, colors and themes"},
        new[]{"Taskbar settings","ms-settings:taskbar","Taskbar behavior"},
        new[]{"Installed apps","ms-settings:appsfeatures","Uninstall or change apps"},
        new[]{"Default apps","ms-settings:defaultapps","Choose what opens what"},
        new[]{"Startup apps","ms-settings:startupapps","What runs at sign-in"},
        new[]{"Storage","ms-settings:storagesense","Disk space and cleanup"},
        new[]{"Windows Update","ms-settings:windowsupdate","Check for updates"},
        new[]{"Power & battery","ms-settings:powersleep","Power mode and sleep"},
        new[]{"Notifications","ms-settings:notifications","Alerts and Focus"},
        new[]{"Gaming settings","ms-settings:gaming-gamemode","Game Mode"},
        new[]{"Xbox Game Bar","ms-settings:gaming-gamebar","Game Bar and captures"},
        new[]{"Mouse","ms-settings:mousetouchpad","Pointer speed and buttons"},
        new[]{"Keyboard","ms-settings:typing","Typing and input"},
        new[]{"Privacy & security","ms-settings:privacy","Permissions"},
        new[]{"Windows Security","windowsdefender:","Antivirus and firewall"},
        new[]{"Accounts","ms-settings:yourinfo","Your account"},
        new[]{"Date & time","ms-settings:dateandtime","Clock and time zone"},
        new[]{"Language & region","ms-settings:regionlanguage","Language and formats"},
        new[]{"Accessibility","ms-settings:easeofaccess","Vision, hearing and input"},
        new[]{"About this PC","ms-settings:about","Specs and Windows version"},
        new[]{"Recovery options","ms-settings:recovery","Reset this PC"},
        new[]{"Printers & scanners","ms-settings:printers","Add or remove printers"},
        new[]{"Task Manager","taskmgr","Processes, performance, startup"},
        new[]{"Device Manager","devmgmt.msc","Hardware and drivers"},
        new[]{"Disk Management","diskmgmt.msc","Partitions and drives"},
        new[]{"Services","services.msc","Windows services"},
        new[]{"Event Viewer","eventvwr.msc","System logs"},
        new[]{"Resource Monitor","resmon","Live CPU, memory, disk, network"},
        new[]{"System Information","msinfo32","Detailed hardware report"},
        new[]{"Control Panel","control","The classic control panel"},
        new[]{"Command Prompt","cmd","Command line"},
        new[]{"PowerShell","powershell","PowerShell"},
    };

    private SearchItem AppItem(AppPin pin)
    {
        return new SearchItem {Name=pin.Name,Lower=pin.Name.ToLowerInvariant(),Kind="app",Key="app:"+pin.Target,Pin=pin,
            Detail=pin.Target.StartsWith("builtin:",StringComparison.Ordinal)?"Bluehour app":"App",Run=()=>OpenApp(pin)};
    }

    // Apps, settings pages and commands. Built on first use and again whenever the app list or the stickers change.
    internal List<SearchItem> Catalog()
    {
        if (catalog!=null) return catalog;
        var items=new List<SearchItem>();
        var seen=new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var pin in AppLibrary.Builtins.Concat(Pins).Concat(library??new List<AppPin>()))
            if (seen.Add(pin.Name)) items.Add(AppItem(pin));
        foreach (var page in SettingsPages)
        {
            string target=page[1];
            items.Add(new SearchItem {Name=page[0],Lower=page[0].ToLowerInvariant(),Detail=page[2],Kind="setting",Key="setting:"+target,Glyph="Settings",
                Run=()=>Open(target)});
        }
        foreach (var command in Items(true))
        {
            var run=command;
            items.Add(new SearchItem {Name=command.Name,Lower=command.Name.ToLowerInvariant(),Detail=command.Detail,Kind="command",
                Key="command:"+command.Name,Glyph="Layouts",Run=run.Run});
        }
        catalog=items;
        return items;
    }

    // Windows that are open right now, to switch to.
    private List<SearchItem> OpenWindowItems()
    {
        var found=new List<SearchItem>();
        var windows=workspace.Snapshot.Count>0?workspace.Snapshot:DesktopWindow.List(null,true);
        foreach (var w in windows.Where(Workspace.IsAppWindow))
        {
            var window=w;
            found.Add(new SearchItem {Name=window.Title,Lower=window.Title.ToLowerInvariant(),Kind="window",Glyph="Desktop",Detail="Switch to this window",
                Run=()=>{ if (State.Desktop) workspace.Bring(window); else window.Activate(); }});
        }
        return found;
    }

    private static SearchItem Header(string text){return new SearchItem {Name=text,Kind="header"};}

    // What shows before anything is typed: what you use most, what is open, then every app A to Z. Nothing is hidden.
    private List<SearchItem> Overview()
    {
        var list=new List<SearchItem>();
        var usage=State.Usage.ToDictionary(u=>u.Key,u=>u,StringComparer.Ordinal);
        var all=Catalog();
        var recent=all.Where(c=>(c.Kind=="app"||c.Kind=="setting") && usage.ContainsKey(c.Key))
            .OrderByDescending(c=>UsageBonus(usage,c.Key)).Take(6).ToList();
        if (recent.Count>0) { list.Add(Header("recent")); list.AddRange(recent); }
        var windows=OpenWindowItems().Take(8).ToList();
        if (windows.Count>0) { list.Add(Header("open windows")); list.AddRange(windows); }
        list.Add(Header("all apps"));
        list.AddRange(all.Where(c=>c.Kind=="app").OrderBy(c=>c.Name,StringComparer.CurrentCultureIgnoreCase));
        return list;
    }

    private static double UsageBonus(Dictionary<string,UsageEntry> usage,string key)
    {
        UsageEntry entry;
        if (key==null || !usage.TryGetValue(key,out entry)) return 0;
        double bonus=Math.Min(250,60*Math.Log(1+entry.Count,2));
        if (DateTime.UtcNow.Ticks-entry.Last<TimeSpan.TicksPerDay*3) bonus+=40;   // used in the last three days
        return bonus;
    }

    private static double KindWeight(string kind)
    {
        return kind=="app"?60:kind=="window"?40:kind=="setting"?10:kind=="file"?-30:0;
    }

    // The results for what has been typed. "> text" searches commands only.
    internal List<SearchItem> Search(string input)
    {
        string query=(input??"").Trim();
        bool commandsOnly=query.StartsWith(">",StringComparison.Ordinal);
        if (commandsOnly) query=query.Substring(1).Trim();
        if (query.Length==0)
            return commandsOnly?Catalog().Where(c=>c.Kind=="command").OrderBy(c=>c.Name).ToList():Overview();
        var words=query.ToLowerInvariant().Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
        var usage=State.Usage.ToDictionary(u=>u.Key,u=>u,StringComparer.Ordinal);
        var found=new List<SearchItem>();
        foreach (var item in Catalog().Concat(commandsOnly?new List<SearchItem>():OpenWindowItems()))
        {
            if (commandsOnly && item.Kind!="command") continue;
            double score=Match.Score(words,item.Lower);
            if (score<0) continue;
            item.Score=score+KindWeight(item.Kind)+UsageBonus(usage,item.Key);
            found.Add(item);
        }
        found=found.OrderByDescending(i=>i.Score).ThenBy(i=>i.Name,StringComparer.CurrentCultureIgnoreCase).Take(50).ToList();
        if (commandsOnly) return found;
        string answer=Calc.Evaluate(query);
        if (answer!=null)
            found.Insert(0,new SearchItem {Name="= "+answer,Kind="calc",Glyph="Calculator",Detail="Enter copies the answer",
                Run=()=>{ Clipboard.SetText(answer); Notify("Copied "+answer,false); }});
        Uri url;
        if (Uri.TryCreate(query,UriKind.Absolute,out url) && (url.Scheme=="https" || url.Scheme=="http"))
            found.Insert(0,new SearchItem {Name="Open "+url.Host,Kind="web",Glyph="Browser",Detail="Open this address in your browser",Run=()=>Open(url.AbsoluteUri)});
        else
            found.Add(new SearchItem {Name="Search the web for “"+query+"”",Kind="web",Glyph="Browser",Detail="Google",
                Run=()=>Open("https://www.google.com/search?q="+Uri.EscapeDataString(query))});
        return found;
    }

    // Remembers what you open, so what you use most rises to the top.
    internal void RecordUse(string key)
    {
        if (String.IsNullOrEmpty(key)) return;
        var entry=State.Usage.FirstOrDefault(u=>u.Key==key);
        if (entry==null) { entry=new UsageEntry {Key=key}; State.Usage.Add(entry); }
        entry.Count++;
        entry.Last=DateTime.UtcNow.Ticks;
        if (State.Usage.Count>200) State.Usage=State.Usage.OrderByDescending(u=>u.Count).ThenByDescending(u=>u.Last).Take(150).ToList();
        try { recovery.Save(); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
