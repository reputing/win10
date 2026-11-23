using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

// What the dock shows besides the system buttons: a few pinned apps and everything that is running.
internal sealed partial class Shell
{
    private readonly Dictionary<string,Bitmap> dockIcons=new Dictionary<string,Bitmap>();

    private WorkspaceApp AppFor(AppPin pin)
    {
        WorkspaceApp app;
        if (!apps.TryGetValue(pin.Target,out app)) { app=new WorkspaceApp(pin); apps[pin.Target]=app; }
        return app;
    }

    // Rebuilds the dock's app section from the workspace's latest look at the windows. The dock only redraws when the
    // result differs from what it already shows.
    private void RefreshDock()
    {
        if (test || !dockAvailable || resting) return;
        Diag.Mark("dock: listing apps");
        var windows=workspace.Snapshot.Where(Workspace.IsAppWindow).ToList();
        var front=Native.GetAncestor(Native.GetForegroundWindow(),3);
        var list=new List<DockApp>();
        int pinned=0;
        foreach (var pin in Pins)
        {
            // Desktop shortcuts are not dock slots (there may be dozens); they show up here only while they run, as programs.
            if (pin.Imported || ++pinned>6) continue;
            var app=AppFor(pin);
            var mine=windows.Where(w=>app.Matches(w)).ToList();
            foreach (var w in mine) windows.Remove(w);
            list.Add(MakeDockApp(pin.Name,pin,mine,front,null));
        }
        foreach (var group in windows.GroupBy(w=>WorkspaceApp.ProgramOf(w)??"?"))
        {
            var mine=group.ToList();
            string name=group.Key=="?"?mine[0].Title:Path.GetFileNameWithoutExtension(group.Key);
            list.Add(MakeDockApp(name,null,mine,front,group.Key=="?"?null:group.Key));
        }
        int room=dock.Room;
        dock.SetApps(list.Count>room?list.Take(room).ToList():list);
    }

    private DockApp MakeDockApp(string name,AppPin pin,List<DesktopWindow> windows,IntPtr front,string program)
    {
        var app=new DockApp {Name=name,Pin=pin};
        app.Windows.AddRange(windows);
        app.Active=windows.Any(w=>w.Handle==front);
        if (pin!=null && pin.Target.StartsWith("builtin:",StringComparison.Ordinal))
            app.Glyph=pin.Target=="builtin:web"?"Browser":pin.Target=="builtin:files"?"Files":"Discord";
        else app.Icon=DockIcon(pin,windows,program);
        return app;
    }

    // Icons are looked up once per app and kept (they are small); a missing icon is looked for again next time.
    private Bitmap DockIcon(AppPin pin,List<DesktopWindow> windows,string program)
    {
        string key=pin!=null?pin.Target:(program??"?"+windows.Count);
        Bitmap icon;
        if (dockIcons.TryGetValue(key,out icon) && icon!=null) return icon;
        icon=pin!=null?AppLibrary.Picture(pin):null;
        if (icon==null && windows.Count>0) icon=Native.WindowIcon(windows[0].Handle);
        if (icon==null && program!=null && File.Exists(program))
        {
            try { using (var extracted=System.Drawing.Icon.ExtractAssociatedIcon(program)) icon=new Bitmap(extracted.ToBitmap(),32,32); }
            catch (ArgumentException) { }
            catch (IOException) { }
        }
        if (dockIcons.Count>80) { foreach (var old in dockIcons.Values) if (old!=null) old.Dispose(); dockIcons.Clear(); }
        dockIcons[key]=icon;
        return icon;
    }

    // Clicking an app in the dock: open it, switch to it, or (if it is the window you are using) minimize it.
    internal void ActivateApp(DockApp app)
    {
        var windows=app.Windows.Where(w=>w.Alive()).ToList();
        if (windows.Count==0) { if (app.Pin!=null) OpenApp(app.Pin); return; }
        var front=Native.GetAncestor(Native.GetForegroundWindow(),3);
        var current=windows.FirstOrDefault(w=>w.Handle==front);
        if (current!=null && windows.Count==1 && !Native.IsIconic(current.Handle)) { Native.ShowWindowAsync(current.Handle,6); return; }
        var next=current==null?windows[0]:windows[(windows.IndexOf(current)+1)%windows.Count];
        if (State.Desktop) workspace.Bring(next); else next.Activate();
    }

    // Right-click on an app in the dock: its windows, a new window, and close.
    internal void DockMenu(DockApp app,Point at)
    {
        var menu=Ui.Menu();
        var open=app.Windows.Where(w=>w.Alive()).ToList();
        foreach (var w in open)
        {
            var window=w;
            string title=window.Title.Length>46?window.Title.Substring(0,45)+"…":window.Title;
            menu.Items.Add(title.Replace("&","&&"),null,delegate{Run(()=>{ if (State.Desktop) workspace.Bring(window); else window.Activate(); });});
        }
        if (app.Pin!=null)
        {
            if (open.Count>0) menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("open "+Ui.Voice(app.Name),null,delegate{Run(()=>OpenApp(app.Pin));});
        }
        if (open.Count>0)
        {
            if (menu.Items.Count>0) menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("close",null,delegate{foreach (var w in open) Native.PostMessage(w.Handle,0x10,IntPtr.Zero,IntPtr.Zero);});
        }
        if (menu.Items.Count==0) { menu.Dispose(); return; }
        menu.Closed+=delegate{BeginInvoke((Action)(()=>menu.Dispose()));};
        menu.Show(at,ToolStripDropDownDirection.AboveLeft);
    }
}
