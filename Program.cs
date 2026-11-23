using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer=System.Windows.Forms.Timer;

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--self-test")) return Test(args.Skip(1).FirstOrDefault());
        using (var mutex=new Mutex(false,@"Local\BluehourDesktop."+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value))
        {
            bool entered;
            try { entered=mutex.WaitOne(0); }
            catch (AbandonedMutexException) { entered=true; }
            if (!entered) { MessageBox.Show("Bluehour is already running. Open it from the system tray.","Bluehour"); return 0; }
            try { using (var form=new Shell(new Recovery(Recovery.Root),args.Contains("--background"))) Application.Run(form); }
            catch (Exception ex) { MessageBox.Show(ex.Message,"Bluehour",MessageBoxButtons.OK,MessageBoxIcon.Error); return 1; }
            finally { mutex.ReleaseMutex(); }
        }
        return 0;
    }

    private static void TestWindows(string dir)
    {
        var area=Screen.PrimaryScreen.WorkingArea;
        using (var first=new Form {Text="Bluehour test one",StartPosition=FormStartPosition.Manual,
            Bounds=new Rectangle(area.X+80,area.Y+60,380,280),Opacity=0,ShowInTaskbar=false})
        using (var second=new Form {Text="Bluehour test two",StartPosition=FormStartPosition.Manual,
            Bounds=new Rectangle(area.X+110,area.Y+90,390,290),Opacity=0,ShowInTaskbar=false})
        {
            first.Show();
            second.Show();
            first.WindowState=FormWindowState.Maximized;
            Application.DoEvents();
            var firstInitial=Native.Snapshot(first.Handle);
            var secondInitial=Native.Snapshot(second.Handle);
            var poses=new WindowLayouts(new Recovery(dir));
            string result=poses.Apply(Screen.PrimaryScreen,new[]{DesktopWindow.FromHandle(first.Handle),DesktopWindow.FromHandle(second.Handle)},"Columns");
            Application.DoEvents();
            if (!result.StartsWith("Arranged 2",StringComparison.Ordinal) || first.Bounds.IntersectsWith(second.Bounds) ||
                first.WindowState==FormWindowState.Maximized)
                throw new Exception("Window arrangement failed: "+result+" "+first.Bounds+" "+second.Bounds);
            if (new WindowLayouts(new Recovery(dir)).Revert()!=0) throw new Exception("Window restore returned an error.");
            Application.DoEvents();
            if (Native.Snapshot(first.Handle).Show!=firstInitial.Show || Native.Snapshot(second.Handle).Normal.Rectangle!=secondInitial.Normal.Rectangle)
                throw new Exception("Original rectangle or maximized state was not restored.");
        }
    }

    private static int Test(string log)
    {
        string dir=Path.Combine(Path.GetTempPath(),"Bluehour-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            var area=new Rectangle(-1920,32,1890,1048);
            foreach (string mode in Shell.Modes)
                for (int n=1;n<=5;n++)
                {
                    var rects=WindowLayouts.Plan(area,n,mode);
                    if (rects.Length!=n || rects.Any(r=>!area.Contains(r)) ||
                        (mode!="Cascade" && rects.Where((r,i)=>rects.Skip(i+1).Any(r.IntersectsWith)).Any()))
                        throw new Exception("Invalid layout: "+mode);
                }
            var state=new Recovery(dir);
            state.State.Layout="Focus";
            state.Save();
            state.State.Dock=true;
            state.Save();
            var loaded=new Recovery(dir);
            if (!loaded.State.Dock || loaded.State.Layout!="Focus") throw new Exception("Journal round trip failed.");
            if (!Recovery.ValidPoint("Bluehour "+Guid.NewGuid().ToString("N")) || Recovery.ValidPoint("Bluehour '; exit"))
                throw new Exception("Restore point name validation failed.");
            using (var shell=new Shell(loaded,false,true))
            {
                shell.Opacity=0;
                shell.Show();
                Application.DoEvents();
                foreach (var page in Shell.Pages) shell.Render(page); shell.Render("Cleanup");
                Application.DoEvents();
                if (log!=null)
                    using (var bitmap=new Bitmap(shell.Width,shell.Height))
                    {
                        shell.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                        bitmap.Save(Path.ChangeExtension(log,"png"));
                    }
                using (var surface=new DesktopSurface(shell,Screen.PrimaryScreen))
                {
                    surface.Opacity=0;
                    surface.Bounds=new Rectangle(20,20,1280,720);
                    surface.Show();
                    surface.BringDesktopForward();
                    Application.DoEvents();
                    if (!surface.Visible || surface.Width!=1280) throw new Exception("Desktop surface layout failed.");
                    foreach(var size in new[]{new Size(1024,680),new Size(1280,800),new Size(1920,1080)})
                    {
                        const int railWidth=54;
                        surface.ClientSize=new Size(size.Width-railWidth,size.Height);
                        surface.UpdateDisplay();
                        Application.DoEvents();
                        if(log==null)continue;
                        using(var bar=new DockBar(shell))
                        using(var bitmap=new Bitmap(size.Width,size.Height))
                        {
                            bar.Opacity=0; bar.Preview("Left",new Size(railWidth,size.Height)); bar.Show(); Application.DoEvents();
                            surface.DrawToBitmap(bitmap,new Rectangle(railWidth,0,surface.Width,surface.Height));
                            bar.DrawToBitmap(bitmap,new Rectangle(0,0,railWidth,size.Height));
                            bitmap.Save(Path.Combine(Path.GetDirectoryName(log),"desktop-"+size.Width+".png"));
                        }
                    }
                }
                TestWindows(dir);
                if (Assembly.GetExecutingAssembly().GetManifestResourceStream("Bluehour.Coast")==null)
                    throw new Exception("Missing embedded wallpaper.");
            }
            return 0;
        }
        catch (Exception ex) { if (log!=null) File.WriteAllText(log,ex.ToString()); return 1; }
        finally
        {
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir)) File.Delete(file);
                Directory.Delete(dir);
            }
        }
    }
}

internal static class Ui
{
    internal static readonly Color Night=Color.FromArgb(12,23,28), Surface=Color.FromArgb(23,37,44),
        Raised=Color.FromArgb(38,58,66), Text=Color.FromArgb(222,237,241), Muted=Color.FromArgb(162,187,194);
    internal static Color Accent(string name)
    {
        if (name=="Sea Glass") return Color.FromArgb(143,220,205);
        if (name=="Apricot") return Color.FromArgb(255,187,154);
        return Color.FromArgb(139,213,249);
    }
    internal static Label Label(string text,float size,Color color,bool bold=false)
    {
        return new Label {Text=text,AutoSize=true,Font=new Font(Style.Family,size,bold?FontStyle.Bold:FontStyle.Regular),
            ForeColor=color,BackColor=Color.Transparent,Margin=new Padding(0,3,0,3),UseMnemonic=false};
    }
    internal static Button Button(string text,Action action,Color? accent=null)
    {
        var b=new ShellButton {Text=text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MinimumSize=new Size(0,35),
            Padding=new Padding(10,1,10,1),Margin=new Padding(0,0,8,6),FlatStyle=FlatStyle.Flat,UseVisualStyleBackColor=false,
            UseMnemonic=false,ForeColor=accent.HasValue?Night:Text,BackColor=accent??Raised,Cursor=Cursors.Hand};
        b.FlatAppearance.BorderSize=0;
        b.FlatAppearance.MouseOverBackColor=accent.HasValue?Color.White:Color.FromArgb(62,67,100);
        b.Click+=delegate{action();};
        return b;
    }
    internal static FlowLayoutPanel Flow(bool vertical)
    {
        return new FlowLayoutPanel {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=!vertical,
            FlowDirection=vertical?FlowDirection.TopDown:FlowDirection.LeftToRight,Margin=new Padding(0,7,0,2)};
    }
}

internal sealed partial class Shell : Form
{
    internal static readonly string[] Modes={"Grid","Focus","Columns","Rows","Cascade"};
    internal static readonly string[] Pages={"Dashboard","Workspaces","Appearance","Cleanup","Recovery"};
    private readonly Recovery recovery;
    private readonly WindowLayouts layouts;
    private readonly Timer timer=new Timer {Interval=1000};
    private readonly ToolTip tips=new ToolTip();
    private readonly Panel body=new Panel {Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(24,18,24,14)};
    private readonly Label status=Ui.Label("",9,Ui.Muted);
    private readonly Dictionary<string,Button> tabs=new Dictionary<string,Button>();
    private readonly NotifyIcon tray;
    private readonly DockBar dock;
    private readonly bool test;
    private bool quitting,busy,hotkey,hoverOpened;
    private int hoverTicks;
    private string currentPage,signature="";
    private Label clock,date,metrics;
    private Launcher launcher;
    private long lastIdle,lastTotal;

    private DesktopState State { get { return recovery.State; } }
    internal string DesktopImage {get{return State.SurfaceImage;}}
    internal bool ShowSeconds {get{return State.Seconds;}}
    internal Color Accent { get { return Ui.Accent(State.Accent); } }
    private Screen Monitor { get { return Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==State.Monitor)??Screen.PrimaryScreen; } }

    internal Shell(Recovery recovery,bool background,bool test=false)
    {
        this.recovery=recovery;
        layouts=new WindowLayouts(recovery);
        this.test=test;
        Text="Bluehour Desktop";
        BackColor=Ui.Night;
        ForeColor=Ui.Text;
        Font=new Font(Style.Family,10);
        AutoScaleMode=AutoScaleMode.Dpi;
        StartPosition=FormStartPosition.CenterScreen;
        Size=new Size(1000,705);
        MinimumSize=new Size(710,525);
        var nav=Ui.Flow(true);
        nav.AutoSize=false;
        nav.Width=182;
        nav.Dock=DockStyle.Left;
        nav.BackColor=Ui.Surface;
        nav.Padding=new Padding(14,22,9,10);
        nav.Controls.Add(Ui.Label("Bluehour",16,Ui.Text,true));
        nav.Controls[0].Margin=new Padding(8,0,0,22);
        foreach (var page in Pages)
        {
            var destination=page;
            var button=B(page,()=>Render(destination));
            ((ShellButton)button).Glyph=page=="Workspaces"?"Layouts":page=="Dashboard"?"Desktop":page=="Appearance"?"Settings":page;
            button.AutoSize=false;
            button.Size=new Size(144,40);
            button.TextAlign=ContentAlignment.MiddleLeft;
            nav.Controls.Add(button);
            tabs[page]=button;
        }
        
        var right=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        right.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        var footer=new Panel {BackColor=Ui.Surface,Dock=DockStyle.Fill,Margin=new Padding(0)};
        status.AutoEllipsis=true;
        status.AutoSize=false;
        status.Dock=DockStyle.Fill;
        status.Padding=new Padding(18,0,6,0);
        status.TextAlign=ContentAlignment.MiddleLeft;
        footer.Controls.Add(status);
        right.Controls.Add(body,0,0);
        right.Controls.Add(footer,0,1);
        Controls.Add(right);
        Controls.Add(nav);
        WindowFrame.Attach(this,"Control center",true);
        dock=new DockBar(this);
        tray=new NotifyIcon {Icon=SystemIcons.Application,Visible=!test,Text="Bluehour Desktop",ContextMenuStrip=new ContextMenuStrip()};
        tray.DoubleClick+=delegate{ShowPage("Dashboard");};
        tray.ContextMenuStrip.Items.Add("Show Bluehour desktop",null,delegate{Run(RevealDesktop);});
        tray.ContextMenuStrip.Items.Add("Open control center",null,delegate{Run(()=>ShowPage("Dashboard"));});
        tray.ContextMenuStrip.Items.Add("Return to Windows",null,delegate{Run(ReturnToWindows);});
        tray.ContextMenuStrip.Items.Add("Launcher",null,delegate{OpenLauncher();});
        tray.ContextMenuStrip.Items.Add("Undo window positions",null,delegate{Run(Undo);});
        tray.ContextMenuStrip.Items.Add("Exit Bluehour",null,delegate{ExitShell();});
        timer.Tick+=delegate{Run(Pulse);};
        if (!test) timer.Start();
        Render("Dashboard");
        Resize+=delegate{ResizeContent();};
        Shown+=delegate{
            if (test) return;
            Say(hotkey?"Ctrl + Alt + K opens the launcher.":"Open the launcher from the system tray.");
            if (State.Desktop) Run(()=>ResumeDesktop(background));
            else { Run(RestoreTaskbar); Run(UpdateDock); }
            if (background) Hide();
        };
        FormClosing+=delegate(object sender,FormClosingEventArgs e){
            if (!quitting && !test && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); }
            else if (!quitting && !test) { Run(()=>dock.DisableBar()); Run(RestoreTaskbar); }
        };
    }


    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Style.WindowChrome(this);
        if (!test)
        {
            hotkey=Native.RegisterHotKey(Handle,1,0x4003,(uint)Keys.K);
            recoveryKey=Native.RegisterHotKey(Handle,2,0x4003,(uint)Keys.Back);
        }
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (hotkey) Native.UnregisterHotKey(Handle,1);
        if (recoveryKey) Native.UnregisterHotKey(Handle,2);
        base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message m)
    {
        if (WindowFrame.ResizeHit(this,ref m))return;
        if (m.Msg==0x0312)
        {
            if (m.WParam.ToInt32()==2) Run(ReturnToWindows);
            else if (m.WParam.ToInt32()==1) Run(OpenLauncher);
        }
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            CloseSurfaces();
            if (dock!=null) dock.Dispose();
            if (tray!=null) { tray.Visible=false; tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
            if (launcher!=null) launcher.Dispose();
            tips.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ExitShell()
    {
        if (busy) { Say("Wait for the current Windows action to finish."); return; }
        int remaining;
        try { dock.DisableBar(); RestoreTaskbar(); remaining=layouts.Revert(); }
        catch (Exception ex) { Run(UpdateDock); ShowPage("Recovery"); Say(ex.Message); return; }
        if (remaining>0 && MessageBox.Show(this,"Some window positions could not be restored. Exit anyway?","Bluehour",
            MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)
        {
            Run(()=>{if (State.Desktop) ResumeDesktop(false); else UpdateDock();});
            return;
        }
        quitting=true;
        Close();
    }

    private void Say(string text) { status.Text=text.Replace(Environment.NewLine,"  •  "); tips.SetToolTip(status,text); }
    internal void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { Say(ex.Message); }
    }
    internal void PrepareMaximize(){MaximizedBounds=Screen.FromHandle(Handle).WorkingArea;}
    internal void ShowPage(string page)
    {
        Render(page);
        if (!Visible) Show();
        if (WindowState==FormWindowState.Minimized) WindowState=FormWindowState.Normal;
        Activate();
    }

    private void ResizeContent()
    {
        if (body.Controls.Count==0) return;
        int width=Math.Max(390,body.ClientSize.Width-65);
        foreach (Control item in body.Controls[0].Controls)
            if (item is Hero || item is ListBox) item.Width=width;
            else item.MaximumSize=new Size(width,0);
    }

    internal void Render(string page)
    {
        currentPage=page;
        var old=body.Controls.Cast<Control>().ToArray();
        body.Controls.Clear();
        foreach (var control in old) control.Dispose();
        clock=date=metrics=null;
        foreach (var tab in tabs)
        {
            tab.Value.BackColor=tab.Key==page?Accent:Ui.Surface;
            tab.Value.ForeColor=tab.Key==page?Ui.Night:Ui.Muted;
        }
        var col=Ui.Flow(true);
        col.Location=new Point(23,18);
        body.Controls.Add(col);
        switch (page)
        {
            case "Workspaces": Workspaces(col); break;
            case "Appearance": Appearance(col); break;
            case "Cleanup": Cleanup(col); break;
            case "Recovery": RecoveryPage(col); break;
            default: Dashboard(col); break;
        }
        ResizeContent();
        Widgets();
    }

    private static void Title(Control panel,string title,string detail)
    {
        panel.Controls.Add(Ui.Label(title,25,Ui.Text,true));
        Line(panel,detail);
    }
    private static void Heading(Control panel,string title)
    {
        var label=Ui.Label(title,13,Ui.Text,true);
        label.Margin=new Padding(0,14,0,2);
        panel.Controls.Add(label);
    }
    private static void Line(Control panel,string text) { panel.Controls.Add(Ui.Label(text,10,Ui.Muted)); }
    private static void Row(Control panel,params Control[] items)
    {
        var row=Ui.Flow(false);
        row.Controls.AddRange(items);
        panel.Controls.Add(row);
    }
    private Button B(string label,Action action,bool primary=false) { return Ui.Button(label,()=>Run(action),primary?Accent:(Color?)null); }
    private CheckBox Check(string label,bool value,Action<bool> change)
    {
        var check=new CheckBox {Text=label,Checked=value,AutoSize=true,Margin=new Padding(0,5,16,6)};
        check.CheckedChanged+=delegate{Run(()=>change(check.Checked));};
        return check;
    }
    private ComboBox Options(string label,string[] values,string selected,Action<string> change)
    {
        var list=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=172,Margin=new Padding(0,3,12,6),
            BackColor=Ui.Raised,ForeColor=Ui.Text,AccessibleName=label};
        list.Items.AddRange(values);
        list.SelectedItem=values.Contains(selected)?selected:values[0];
        list.SelectedIndexChanged+=delegate{Run(()=>change((string)list.SelectedItem));};
        return list;
    }
    private static CheckedListBox List(string label,int height)
    {
        return new StyledCheckList {Height=height,Width=620,CheckOnClick=true,BorderStyle=BorderStyle.None,
            BackColor=Ui.Surface,ForeColor=Ui.Text,AccessibleName=label,Margin=new Padding(0,8,0,2)};
    }

    private void Dashboard(Control panel)
    {
        var hero=new Hero(Accent) {Width=665,Height=215,Margin=new Padding(0,0,0,6)};
        var heading=Ui.Label("Desktop control center",20,Color.White,true);
        heading.Location=new Point(24,19);
        clock=Ui.Label("",44,Color.White,true);
        clock.Location=new Point(20,98);
        date=Ui.Label("",11,Color.White,true);
        date.Location=new Point(27,182);
        hero.Controls.AddRange(new Control[]{heading,clock,date});
        panel.Controls.Add(hero);
        Row(panel,B(State.Desktop?"Show Bluehour desktop":"Switch to Bluehour desktop",RevealDesktop,true),
            B("Open Discord",OpenDiscord),B(State.Desktop?"Return to Windows":"Open launcher",State.Desktop?(Action)ReturnToWindows:OpenLauncher));
        if (recoveryKey) Line(panel,"Ctrl + Alt + Backspace returns to the Windows desktop.");
        Heading(panel,"Performance");
        metrics=Ui.Label("",11,Accent);
        panel.Controls.Add(metrics);
        Row(panel,B("Task Manager",()=>Open("taskmgr.exe")),B("Storage",()=>Open("ms-settings:storagesense")),B("Network",()=>Open("ms-settings:network")));
        Heading(panel,"Media");
        Row(panel,B("Previous track",()=>Native.MediaKey(0xb1)),B("Play / pause",()=>Native.MediaKey(0xb3)),B("Next track",()=>Native.MediaKey(0xb0)));
        Heading(panel,"Utilities");
        Row(panel,B("Calendar",Calendar),B("Sound",()=>Open("ms-settings:sound")),B("Display",()=>Open("ms-settings:display")),
            B("Weather",()=>Open("https://www.msn.com/weather")),B("Lock Windows",()=>Native.LockWorkStation()));
    }

    private void Workspaces(Control panel)
    {
        Title(panel,"Workspaces","Rearrange open Windows apps on the selected display.");
        Heading(panel,"Display and layout");
        var screens=Screen.AllScreens;
        string[] names=screens.Select((s,i)=>"Display "+(i+1)+" · "+s.Bounds.Width+"×"+s.Bounds.Height).ToArray();
        int selected=Array.FindIndex(screens,s=>s.DeviceName==Monitor.DeviceName);
        Row(panel,Options("Monitor",names,names[Math.Max(0,selected)],value=>{
            State.Monitor=screens[Array.IndexOf(names,value)].DeviceName; recovery.Save(); signature=""; UpdateDock(); Render("Workspaces");
        }),Options("Window layout",Modes,State.Layout,value=>{State.Layout=value; recovery.Save(); signature=""; dock.RefreshBar();}));
        var list=List("Windows to include",164);
        foreach (var window in DesktopWindow.List(Monitor,false)) list.Items.Add(window,true);
        panel.Controls.Add(list);
        Row(panel,B("Arrange checked windows",()=>Arrange(list.CheckedItems.Cast<DesktopWindow>(),State.Layout),true),
            B("Refresh windows",()=>Render("Workspaces")),B("Undo placement",Undo));
        Row(panel,Check("Automatically arrange visible windows when they open or close",State.Auto,value=>{
            State.Auto=value; recovery.Save(); signature="";
        }));
        Line(panel,"Win + Tab opens Windows Task View for virtual desktops.");
    }


    private void Appearance(Control panel)
    {
        Title(panel,"Appearance","Customize your dock, desktop background, and Windows color mode.");
        Heading(panel,"Dock");
        Row(panel,Check("Keep dock visible on Windows desktop",State.Dock,v=>{State.Dock=v; recovery.Save(); UpdateDock();}),
            Check("Clock seconds",State.Seconds,v=>{State.Seconds=v; recovery.Save(); Widgets();}),
            Check("Open dashboard on dock hover",State.Hover,v=>{State.Hover=v; recovery.Save();}));
        Row(panel,Check("Auto-hide Windows taskbar in desktop mode",State.AutoHideTaskbar,v=>{
            State.AutoHideTaskbar=v; recovery.Save();
            if (State.Desktop && v) AutoHideTaskbar();
            if (!v) RestoreTaskbar();
            if (State.Desktop) UpdateDock();
        }));
        Row(panel,Options("Dock edge",new[]{"Left","Right","Top","Bottom"},State.Edge,v=>{State.Edge=v; recovery.Save(); UpdateDock();}),
            Options("Accent",new[]{"Glacier","Sea Glass","Apricot"},State.Accent,SetAccent));
        Heading(panel,"Windows personalization");
        Row(panel,B("Apply palette to Windows",()=>{recovery.SetFramePalette(Accent);Say("Dark mode and Windows title-bar accent applied. Recovery keeps the previous colors.");},true));
        Row(panel,B("Dark mode",()=>Theme(true),true),B("Light mode",()=>Theme(false)),B("Windows settings",()=>Open("ms-settings:personalization")));
        Row(panel,B("Use Bluehour wallpaper",()=>Wallpaper(recovery.WallpaperPath())),B("Choose image…",ChooseWallpaper));
        Line(panel,"Wallpaper changes apply across connected displays with a static Windows background.");
        Heading(panel,"Sign-in");
        Row(panel,B("Start at sign-in",()=>{
            recovery.Set(Recovery.Run,"BluehourDesktop","\""+Application.ExecutablePath+"\" --background",RegistryValueKind.String);
            Say("Bluehour will start at sign-in from this EXE’s current location.");
        }),B("Disable sign-in launch",()=>{
            recovery.Set(Recovery.Run,"BluehourDesktop",null,RegistryValueKind.String);
            Say("Sign-in launch disabled.");
        }));
    }

    private void Theme(bool dark) { recovery.SetTheme(dark); Say(dark?"Windows dark mode applied.":"Windows light mode applied."); }
    private void Wallpaper(string path) { recovery.SetWallpaper(path); State.SurfaceImage=path; recovery.Save(); foreach(var surface in canvases)surface.UpdateDisplay(); Say("Wallpaper applied. The previous image can be restored in Recovery."); }
    private void ChooseWallpaper()
    {
        using (var file=new OpenFileDialog {Filter="Wallpaper images|*.jpg;*.jpeg;*.png;*.bmp",Title="Choose an image"})
            if (file.ShowDialog(this)==DialogResult.OK) Wallpaper(file.FileName);
    }

    private void Cleanup(Control panel)
    {
        Title(panel,"Cleanup","Review optional Windows apps and performance settings for this account.");
        Heading(panel,"Background activity");
        Row(panel,B("Disable Game DVR capture",()=>{
            recovery.SetQuiet(); Say("Game DVR capture preferences changed. Reopen games or sign in again if needed.");
        }),B("Windows Startup Apps",()=>Open("ms-settings:startupapps")));
        string[] names=new string[0];
        if (!test)
        {
            try { names=Recovery.Startup(); }
            catch (System.Security.SecurityException) { Line(panel,"Windows denied access to the current user’s sign-in entries."); }
        }
        if (names.Length>0)
        {
            var startup=List("Sign-in entries to disable",94);
            startup.Items.AddRange(names);
            panel.Controls.Add(startup);
            Row(panel,B("Disable checked sign-in entries",()=>{
                var chosen=startup.CheckedItems.Cast<string>().ToArray();
                foreach (var item in chosen) recovery.Set(Recovery.Run,item,null,RegistryValueKind.String);
                Say(chosen.Length+" sign-in entries disabled.");
                Render("Cleanup");
            }));
        }
        Heading(panel,"Optional Store apps");
        Line(panel,"A verified Windows restore point is required. Reinstallation may require Microsoft Store. Local app data can be lost.");
        var list=List("Optional apps to remove",126);
        list.Items.AddRange(Recovery.OptionalApps.Select(a=>a[0]).ToArray());
        panel.Controls.Add(list);
        Row(panel,B("Review removal",()=>RemoveApps(list.CheckedItems.Cast<string>().ToArray()),true),B("Restore removed apps",RestoreApps));
        Heading(panel,"Discord");
        Row(panel,B("Open Discord",OpenDiscord),B("Install Discord",InstallDiscord));
    }

    private void RecoveryPage(Control panel)
    {
        Title(panel,"Recovery","Restore saved settings, window positions, or removed apps.");
        Heading(panel,"Bluehour backup");
        Line(panel,State.Changes.Count+" saved Windows settings  ·  "+layouts.Count+" saved window positions  ·  "+State.Removed.Count+" app removal records");
        Row(panel,B("Revert Bluehour changes",Revert,true),B("Undo placement",Undo),B("Restore removed apps",RestoreApps));
        Heading(panel,"Windows System Restore");
        Line(panel,Recovery.ValidPoint(State.Point)?"Last verified point: "+State.Point:"Create a restore point before applying system changes.");
        Row(panel,B("Create and verify restore point",CreatePoint),B("Open System Restore",()=>Open(Path.Combine(Environment.SystemDirectory,"rstrui.exe"))));
        Line(panel,"Windows requires System Protection and administrator approval. Windows may throttle repeat restore points.");
    }

    private void Revert()
    {
        if (busy) { Say("Wait for the current Windows action."); return; }
        if (MessageBox.Show(this,"Restore saved settings and window positions and turn off the dock?",
            "Revert Bluehour changes",MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK) return;
        var issues=recovery.Restore();
        CloseSurfaces();
        UpdateDock();
        try { RestoreTaskbar(); }
        catch (Exception ex) { issues+=" "+ex.Message; }
        int failures=layouts.Revert();
        Say(issues=="" && failures==0?"Bluehour changes reverted.":issues+" "+(failures==0?"":failures+" positions could not be restored."));
        Render("Recovery");
    }

    private void Undo()
    {
        State.Auto=false; recovery.Save();
        int failed=layouts.Revert();
        Say(failed==0?"Window placement restored. Automatic tiling is off.":failed+" window positions could not be restored.");
        if (currentPage=="Workspaces") Render("Workspaces");
    }

    private async Task MakePoint()
    {
        var name="Bluehour "+Guid.NewGuid().ToString("N");
        Say("Creating Windows restore point. Approve the Windows prompt if shown.");
        if (!await Task.Run(()=>Recovery.MakePoint(name)))
            throw new InvalidOperationException("Windows did not verify a new point. Check System Protection or the restore point interval.");
        State.Point=name;
        recovery.Save();
        Say("Windows restore point verified.");
    }

    private async void CreatePoint()
    {
        if (busy || test) return;
        busy=true;
        try { await MakePoint(); if (currentPage=="Recovery") Render("Recovery"); }
        catch (Exception ex) { Say(ex.Message); }
        finally { busy=false; }
    }

    private async void RemoveApps(string[] selected)
    {
        if (busy || test) return;
        if (selected.Length==0) { Say("Select the optional apps you want to remove."); return; }
        if (MessageBox.Show(this,"Remove these apps from your Windows account?\n\n"+String.Join("\n",selected)+
            "\n\nBluehour first verifies a System Restore point. Reinstallation may require Microsoft Store and local app data can be lost.",
            "Confirm app removal",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes) return;
        busy=true;
        try
        {
            Say("Checking installed apps.");
            var installed=await Task.Run(()=>Recovery.OptionalApps.Where(a=>selected.Contains(a[0]) && Recovery.IsInstalled(a[1])).ToArray());
            if (installed.Length==0) { Say("None of the selected apps is installed."); return; }
            await MakePoint();
            var failed=new List<string>();
            foreach (var app in installed)
            {
                if (!State.Removed.Any(a=>a.Name==app[1]))
                {
                    var record=new RemovedApp {Name=app[1],StoreId=app[2]};
                    State.Removed.Add(record);
                    try { recovery.Save(); }
                    catch { State.Removed.Remove(record); throw; }
                }
                if (!await Task.Run(()=>Recovery.RemoveApp(app[1]))) failed.Add(app[0]);
            }
            Say(failed.Count==0?"Selected apps removed. Use Restore removed apps to reinstall them.":"Removal failed: "+String.Join(", ",failed));
        }
        catch (Exception ex) { Say(ex.Message); }
        finally { busy=false; }
    }

    private async void RestoreApps()
    {
        if (busy || test) return;
        if (State.Removed.Count==0) { Say("There are no recorded app removals."); return; }
        busy=true;
        try
        {
            foreach (var record in State.Removed.ToArray())
                if (await Task.Run(()=>Recovery.IsInstalled(record.Name))) { State.Removed.Remove(record); recovery.Save(); }
            if (State.Removed.Count==0) { Say("All recorded apps are installed."); return; }
            using (var form=new Form {Text="Restore removed apps",Size=new Size(480,270),StartPosition=FormStartPosition.CenterScreen,
                BackColor=Ui.Night,ForeColor=Ui.Text,Font=Font,MaximizeBox=false,MinimizeBox=false})
            {
                var flow=Ui.Flow(true);
                flow.Padding=new Padding(16);
                Line(flow,"Open each app in Microsoft Store to reinstall it.");
                foreach (var record in State.Removed)
                {
                    var app=Recovery.OptionalApps.FirstOrDefault(a=>a[1]==record.Name && a[2]==record.StoreId);
                    if (app!=null) flow.Controls.Add(B(app[0],()=>Open("ms-windows-store://pdp/?ProductId="+app[2])));
                }
                form.Controls.Add(flow);
                form.ShowDialog();
            }
        }
        catch (Exception ex) { Say(ex.Message); }
        finally { busy=false; }
    }


    internal static void Open(string file,string args="")
    {
        Process.Start(new ProcessStartInfo(file,args){UseShellExecute=true});
    }

    internal void OpenDiscord()
    {
        string updater=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"Discord\Update.exe");
        if (File.Exists(updater)) Open(updater,"--processStart Discord.exe");
        else Open("https://discord.com/app");
    }

    private void InstallDiscord()
    {
        string winget=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"Microsoft\WindowsApps\winget.exe");
        if (File.Exists(winget)) Open(winget,"install --id Discord.Discord --exact --source winget");
        else Open("https://discord.com/download");
    }

    internal void Calendar()
    {
        using (var calendar=new MonthCalendar {MaxSelectionCount=1,Dock=DockStyle.Top})
        using (var form=new Form {Text="Calendar",BackColor=Ui.Night,StartPosition=FormStartPosition.CenterScreen,
            ClientSize=calendar.SingleMonthSize,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false})
        {
            form.Controls.Add(calendar);
            form.ShowDialog();
        }
    }

    private void UpdateDock()
    {
        if (test) return;
        if (!State.Dock && !State.Desktop) { dock.DisableBar(); dockAvailable=false; return; }
        dockAvailable=dock.EnableBar(Monitor,State.Edge);
        if (!dockAvailable) Say("Windows could not reserve dock space.");
    }

    internal void OpenLauncher()
    {
        if (launcher!=null && !launcher.IsDisposed) { launcher.Activate(); return; }
        launcher=new Launcher(this);
        launcher.Show();
    }

    internal IEnumerable<LaunchItem> Items(bool commands)
    {
        if (commands)
        {
            yield return new LaunchItem("Settings","Open appearance controls",()=>ShowPage("Appearance"));
            foreach (var accent in new[]{"Glacier","Sea Glass","Apricot"})
            {
                var local=accent;
                yield return new LaunchItem("Scheme · "+local,"Change Bluehour accent",()=>SetAccent(local));
            }
            yield return new LaunchItem("Dark","Set Windows dark mode",()=>Theme(true));
            yield return new LaunchItem("Light","Set Windows light mode",()=>Theme(false));
            yield return new LaunchItem("Wallpaper","Choose an image",ChooseWallpaper);
            yield return new LaunchItem("Coast wallpaper","Apply the included background",()=>Wallpaper(recovery.WallpaperPath()));
            yield return new LaunchItem("Lock","Lock Windows",()=>Native.LockWorkStation());
            yield return new LaunchItem("Calculator","Open Windows Calculator",()=>Open("calc.exe"));
            yield return new LaunchItem("Undo","Restore window positions",Undo);
            foreach (var mode in Modes)
            {
                var local=mode;
                yield return new LaunchItem(mode,"Apply window layout",()=>ChooseLayout(local));
            }
            yield break;
        }
        yield return new LaunchItem("Workspaces","Arrange Windows apps",()=>ShowPage("Workspaces"));
        yield return new LaunchItem("Discord","Open Discord",OpenDiscord);
        yield return new LaunchItem("Dashboard","See calendar and performance",()=>ShowPage("Dashboard"));
        yield return new LaunchItem("Files","Open File Explorer",()=>Open("explorer.exe"));
        yield return new LaunchItem("Windows Settings","Open the Windows Settings app",()=>Open("ms-settings:"));
        yield return new LaunchItem("All Windows apps","Open Windows Apps folder",()=>Open("explorer.exe","shell:AppsFolder"));
        foreach (var window in DesktopWindow.List(null,true))
        {
            var active=window;
            yield return new LaunchItem(active.Title,"Switch to open window",active.Activate);
        }
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[]{Environment.SpecialFolder.Programs,Environment.SpecialFolder.CommonPrograms})
        {
            var dir=Environment.GetFolderPath(folder);
            string[] files;
            try { files=Directory.Exists(dir)?Directory.GetFiles(dir,"*.lnk",SearchOption.AllDirectories):new string[0]; }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var shortcut in files.OrderBy(Path.GetFileNameWithoutExtension))
            {
                string title=Path.GetFileNameWithoutExtension(shortcut), file=shortcut;
                if (seen.Add(title)) yield return new LaunchItem(title,"Launch installed app",()=>Open(file));
            }
        }
    }

    private void SetAccent(string name)
    {
        State.Accent=name; recovery.Save(); dock.RefreshBar(); Render(currentPage);
    }

    internal void ChooseLayout(string name)
    {
        State.Layout=name;
        recovery.Save();
        signature="";
        Arrange(DesktopWindow.List(Monitor,false),name);
        dock.RefreshBar();
        if (currentPage=="Workspaces") Render("Workspaces");
    }

    internal string ActiveLayout { get { return State.Layout; } }
    internal string DockTime { get { return DateTime.Now.ToString(State.Seconds?"HH:mm:ss":"HH:mm"); } }

    private void Pulse()
    {
        Widgets();
        DesktopPulse();
        if (dockAvailable)
        {
            dock.RefreshBar();
            if (!dock.Hovering) { hoverTicks=0; hoverOpened=false; }
            else if (State.Hover && !Visible && !busy && !hoverOpened && ++hoverTicks>=2)
            { hoverOpened=true; ShowPage("Dashboard"); }
        }
        if (State.Auto && !busy)
        {
            var windows=DesktopWindow.List(Monitor,false);
            string key=Monitor.DeviceName+":"+Monitor.WorkingArea+":"+String.Join(",",windows.Select(w=>w.Handle.ToInt64()).OrderBy(n=>n));
            if (signature!=key)
            {
                signature=key;
                Arrange(windows,State.Layout);
            }
        }
    }

    private void Widgets()
    {
        if (clock==null) return;
        clock.Text=DockTime;
        date.Text=DateTime.Now.ToString("dddd, MMMM d");
        var memory=new Native.Memory {Length=(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.Memory))};
        string ram=Native.GlobalMemoryStatusEx(ref memory)?memory.Load+"%":"—";
        long idle,kernel,user;
        string cpu="—";
        if (Native.GetSystemTimes(out idle,out kernel,out user))
        {
            long total=kernel+user;
            if (lastTotal>0 && total>lastTotal) cpu=Math.Max(0,Math.Min(100,100-(int)(100*(idle-lastIdle)/(total-lastTotal))))+"%";
            lastIdle=idle; lastTotal=total;
        }
        var power=SystemInformation.PowerStatus;
        string battery=(power.BatteryChargeStatus&BatteryChargeStatus.NoSystemBattery)==0 &&
            power.BatteryChargeStatus!=BatteryChargeStatus.Unknown?"    •    BATTERY  "+power.BatteryLifePercent.ToString("P0"):"";
        metrics.Text="CPU  "+cpu+"    •    MEMORY  "+ram+battery;
    }
}

internal sealed class Hero : Panel
{
    private readonly Image image;
    private readonly Color accent;
    public Hero(Color accent)
    {
        this.accent=accent;
        DoubleBuffered=true;
        using (var asset=Assembly.GetExecutingAssembly().GetManifestResourceStream("Bluehour.Coast"))
            if (asset!=null)
                using (var picture=Image.FromStream(asset)) image=new Bitmap(picture);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Width<=0 || Height<=0) return;
        using (var baseColor=new LinearGradientBrush(ClientRectangle,Color.FromArgb(38,42,78),Color.FromArgb(74,69,109),18f))
            e.Graphics.FillRectangle(baseColor,ClientRectangle);
        if (image!=null)
        {
            float zoom=Math.Max(Width/(float)image.Width,Height/(float)image.Height);
            var w=image.Width*zoom;
            var h=image.Height*zoom;
            e.Graphics.DrawImage(image,(Width-w)/2,(Height-h)/2,w,h);
        }
        using (var wash=new LinearGradientBrush(ClientRectangle,Color.FromArgb(230,17,19,36),Color.FromArgb(22,17,19,36),0f))
            e.Graphics.FillRectangle(wash,ClientRectangle);
        using (var edge=new SolidBrush(accent)) e.Graphics.FillRectangle(edge,0,0,4,Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && image!=null) image.Dispose();
        base.Dispose(disposing);
    }
}














