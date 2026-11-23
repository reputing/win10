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
        // Plain blocking collections: no background GC thread, which means fewer wake-ups and less memory held back.
        System.Runtime.GCSettings.LatencyMode=System.Runtime.GCLatencyMode.Batch;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--self-test")) return Test(args.Skip(1).FirstOrDefault());
        int exit=0;
        using (var mutex=new Mutex(false,@"Local\BluehourDesktop."+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value))
        {
            bool entered;
            try { entered=mutex.WaitOne(0); }
            catch (AbandonedMutexException) { entered=true; }
            if (!entered) { MessageBox.Show("Bluehour is already running. Open it from the system tray.","Bluehour"); return 0; }
            try
            {
                // The loading screen appears first; settings and the desktop are then set up behind it (see Boot).
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                Diag.Watch();
                using (var boot=new Boot(args.Contains("--background")))
                {
                    Application.Run(boot);
                    exit=boot.ExitCode;
                }
            }
            catch (Exception ex) { Diag.Log("Fatal: "+ex); MessageBox.Show(ex.Message,"Bluehour",MessageBoxButtons.OK,MessageBoxIcon.Error); exit=1; }
            finally { mutex.ReleaseMutex(); }
        }
        return exit;
    }

    // Ivy's emphasized-decel curve: starts at 0, ends at 1, never goes backwards, and is front-loaded.
    private static void TestMotion()
    {
        var curve=Motion.Decel;
        if (curve.At(0)!=0 || curve.At(1)!=1) throw new Exception("The window glide curve does not start at 0 and end at 1.");
        double previous=0;
        for (int i=1;i<=100;i++)
        {
            double value=curve.At(i/100.0);
            if (value<previous-0.0001) throw new Exception("The window glide curve goes backwards at "+i+"%.");
            previous=value;
        }
        if (curve.At(.25)<.5) throw new Exception("The window glide curve is not front-loaded.");
        var work=new Rectangle(0,64,1856,950);
        foreach (string mode in Shell.Modes)
            for (int n=1;n<=6;n++)
            {
                var rects=WindowLayouts.Plan(work,n,mode,Workspace.Gap);
                if (rects.Length!=n || rects.Any(r=>!work.Contains(r)) ||
                    (mode!="Cascade" && rects.Where((r,i)=>rects.Skip(i+1).Any(r.IntersectsWith)).Any()))
                    throw new Exception("Invalid tiling for "+mode+" with "+n+" windows.");
            }
    }

    // A lone tap of the Windows key opens the launcher; Windows shortcuts that use the key must not.
    private static void TestWinKey()
    {
        var tap=new WinTap();
        if (tap.Feed(0x5B,true) || !tap.Feed(0x5B,false)) throw new Exception("A lone Windows key tap was not recognised.");
        if (tap.Feed(0x5B,true) || tap.Feed(0x45,true) || tap.Feed(0x45,false) || tap.Feed(0x5B,false)) throw new Exception("Win + E was mistaken for a tap.");
        if (tap.Feed(0x5B,true) || tap.Feed(0x5B,true) || tap.Feed(0x5B,true) || !tap.Feed(0x5B,false)) throw new Exception("Holding the Windows key was not treated as one press.");
        if (tap.Feed(0x41,true) || tap.Feed(0x41,false)) throw new Exception("A key without the Windows key counted as a tap.");
        if (tap.Feed(0x5C,true) || !tap.Feed(0x5C,false)) throw new Exception("The right Windows key was not recognised.");
        if (tap.Feed(0x5B,true) || tap.Feed(0x09,true) || tap.Feed(0x5B,false) || tap.Feed(0x09,false)) throw new Exception("Win + Tab was mistaken for a tap.");
        if (tap.Feed(0x5B,true) || !tap.Feed(0x5B,false)) throw new Exception("A tap after a shortcut was not recognised.");
    }

    // A frame rounds the app's corners while it is around, and gives the window back untouched afterwards.
    private static void TestFrame()
    {
        var area=Screen.PrimaryScreen.WorkingArea;
        using (var host=new Form {Text="Bluehour frame test",StartPosition=FormStartPosition.Manual,
            Bounds=new Rectangle(area.X+140,area.Y+110,420,300),Opacity=0,ShowInTaskbar=false})
        {
            host.Show();
            Application.DoEvents();
            var window=DesktopWindow.FromHandle(host.Handle);
            using (var frame=new WorkspaceFrame(window,()=>Ui.Accent("Lavender"),()=>Ui.Tertiary("Lavender")))
            {
                if (!frame.Sync(true)) throw new Exception("The window frame lost its window.");
                // Corners are rounded once the window's size has been steady for a moment.
                System.Threading.Thread.Sleep(180);
                Application.DoEvents();
                if (!frame.Sync(true)) throw new Exception("The window frame lost its window.");
                Native.Rect box;
                if (Native.GetWindowRgnBox(host.Handle,out box)==0) throw new Exception("The app window's corners were not rounded.");
                frame.Release();
                if (Native.GetWindowRgnBox(host.Handle,out box)!=0) throw new Exception("Release did not give the window its square corners back.");
            }
            if (NativeFrame.Supported)
            {
                using (var native=new NativeFrame(window,()=>Ui.Accent("Lavender"),()=>Ui.Tertiary("Lavender")))
                {
                    if (!native.Sync(false)) throw new Exception("The native frame lost its window.");
                    native.Release();
                }
            }
        }
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

    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SendMessageW")]
    private static extern IntPtr InputMessage(IntPtr handle,int message,IntPtr keys,IntPtr coordinates);
    private static void TestDrag(AppBubble bubble,int dx,int dy)
    {
        int down=(12<<16)|12;
        int move=((12+dy)<<16)|(12+dx);
        InputMessage(bubble.Handle,0x201,new IntPtr(1),new IntPtr(down));
        InputMessage(bubble.Handle,0x200,new IntPtr(1),new IntPtr(move));
        InputMessage(bubble.Handle,0x202,IntPtr.Zero,new IntPtr(down));
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
                if(log!=null)
                    foreach(var app in AppLibrary.Installed().Where(a=>new[]{"Calculator","Paint","Notepad"}.Contains(a.Name,StringComparer.OrdinalIgnoreCase)).Take(2))
                        shell.AddPin(app);
                foreach (var page in Shell.Pages) shell.Render(page);
                if(log!=null)
                    using(var preview=new Bitmap(shell.Width,shell.Height))
                    {
                        shell.Render("Appearance");Application.DoEvents();shell.DrawToBitmap(preview,new Rectangle(Point.Empty,preview.Size));
                        preview.Save(Path.Combine(Path.GetDirectoryName(log),"settings-v3.png"));
                    }
                shell.Render("Dashboard");
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
                        surface.ClientSize=size;
                        surface.UpdateDisplay();
                        Application.DoEvents();
                        if(log==null)continue;
                        using(var bar=new DockBar(shell))
                        using(var bitmap=new Bitmap(size.Width,size.Height))
                        {
                            surface.DrawToBitmap(bitmap,new Rectangle(0,0,size.Width,size.Height));
                            Point origin;
                            using(var island=bar.Snapshot(new Rectangle(0,0,size.Width,size.Height),"Bottom",1.0,out origin))
                            using(var g=Graphics.FromImage(bitmap))g.DrawImage(island,origin);
                            bitmap.Save(Path.Combine(Path.GetDirectoryName(log),"desktop-"+size.Width+".png"));
                        }
                    }
                }
                var board=shell.Controls.Find("App board",true).Single();
                var browser=board.Controls.OfType<AppBubble>().Single(b=>b.Pin.Target=="builtin:web");
                TestDrag(browser,100,0);
                if(new Recovery(dir).State.Pins[1].Target!="builtin:web")
                    throw new Exception("Dragging a dashboard bubble did not persist the new order.");
                using(var moved=new DesktopSurface(shell,Screen.PrimaryScreen))
                {
                    moved.Opacity=0;moved.Show();Application.DoEvents();
                    var files=moved.Controls.OfType<AppBubble>().Single(b=>b.Pin.Target=="builtin:files");
                    TestDrag(files,75,90);
                    var placed=new Recovery(dir).State.Pins.Single(p=>p.Target=="builtin:files");
                    if(placed.X<=0 || placed.Y<=0)throw new Exception("The desktop pin position did not survive a journal reload.");
                }
                TestMotion();
                TestWinKey();
                TestFrame();
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
    // Palette taken from kuro.rip: a near-black violet ground, lavender and pink accents, and plum edges. The original
    // Bluehour names stay (Night = ground, Surface = card, Raised = hover) so existing controls pick the theme up.
    internal static readonly Color SurfaceLowest=Color.FromArgb(8,6,14), Night=Color.FromArgb(13,10,26),
        SurfaceLow=Color.FromArgb(18,15,36), Surface=Color.FromArgb(24,20,52),
        SurfaceHigh=Color.FromArgb(33,27,70), Raised=Color.FromArgb(45,38,92),
        Text=Color.FromArgb(245,236,255), Muted=Color.FromArgb(176,166,192), Faint=Color.FromArgb(122,113,136),
        Edge=Color.FromArgb(106,74,122),          // --kw-edge: the plum border on kuro's windows
        EdgeDeep=Color.FromArgb(14,4,18),         // --kw-shadow: the hard offset shadow under them
        Line=Color.FromArgb(52,43,92),
        Error=Color.FromArgb(255,138,138), OnError=Color.FromArgb(70,0,6);
    internal static readonly string[] Accents={"Lavender","Sakura","Mint"};
    internal static Color Accent(string name)
    {
        if (name=="Sakura") return Color.FromArgb(245,139,200);
        if (name=="Mint") return Color.FromArgb(159,217,185);
        return Color.FromArgb(201,164,255);
    }
    internal static Color AccentContainer(string name)
    {
        if (name=="Sakura") return Color.FromArgb(92,31,70);
        if (name=="Mint") return Color.FromArgb(31,74,55);
        return Color.FromArgb(61,42,120);
    }
    internal static Color OnAccentContainer(string name)
    {
        if (name=="Sakura") return Color.FromArgb(255,208,237);
        if (name=="Mint") return Color.FromArgb(200,236,214);
        return Color.FromArgb(236,220,255);
    }
    // The accent's partner: used for drop targets and small flourishes.
    internal static Color Tertiary(string name)
    {
        if (name=="Sakura") return Color.FromArgb(201,164,255);
        if (name=="Mint") return Color.FromArgb(255,181,223);
        return Color.FromArgb(245,139,200);
    }
    // kuro.rip writes everything in lowercase; short interface text follows suit.
    internal static string Voice(string text) { return text==null?"":text.ToLowerInvariant(); }

    // A context menu in the theme (the stock Windows one is white with a blue highlight).
    internal static ContextMenuStrip Menu()
    {
        var menu=new ContextMenuStrip {Renderer=new KuroMenuRenderer(),ShowImageMargin=false,ShowCheckMargin=false,
            BackColor=Surface,ForeColor=Text,Font=new Font(Style.Family,10),Padding=new Padding(7),DropShadowEnabled=false};
        menu.ItemAdded+=delegate(object sender,ToolStripItemEventArgs e){ if(!(e.Item is ToolStripSeparator)) e.Item.Padding=new Padding(8,7,8,7); };
        menu.Opened+=delegate
        {
            if(menu.Width<2||menu.Height<2)return;
            using(var shape=Style.Round(new RectangleF(0,0,menu.Width,menu.Height),16))
            {
                var old=menu.Region;
                menu.Region=new Region(shape);
                if(old!=null)old.Dispose();
            }
        };
        return menu;
    }

    // Tooltips drawn in the theme.
    internal static ToolTip Tip()
    {
        var tip=new ToolTip {OwnerDraw=true,InitialDelay=350,ReshowDelay=120,AutoPopDelay=7000};
        tip.Popup+=delegate(object sender,PopupEventArgs e)
        {
            using(var font=new Font(Style.Family,9.5f))
            {
                var size=TextRenderer.MeasureText(((ToolTip)sender).GetToolTip(e.AssociatedControl),font);
                e.ToolTipSize=new Size(size.Width+24,size.Height+14);
            }
        };
        tip.Draw+=delegate(object sender,DrawToolTipEventArgs e)
        {
            var g=e.Graphics;
            g.Clear(SurfaceHigh);
            using(var pen=new Pen(Edge,2f))g.DrawRectangle(pen,1,1,e.Bounds.Width-3,e.Bounds.Height-3);
            using(var font=new Font(Style.Family,9.5f))
                TextRenderer.DrawText(g,e.ToolTipText,font,new Rectangle(0,0,e.Bounds.Width,e.Bounds.Height),Text,
                    TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        };
        return tip;
    }
    // Large text in Fredoka, the display face (page titles, the hero card).
    internal static Label Heading(string text,float size,Color color)
    {
        return new Label {Text=text,AutoSize=true,Font=new Font(ShellFonts.Display,size,FontStyle.Regular),
            ForeColor=color,BackColor=Color.Transparent,Margin=new Padding(0,3,0,3),UseMnemonic=false};
    }
    internal static Label Label(string text,float size,Color color,bool bold=false)
    {
        return new Label {Text=text,AutoSize=true,Font=new Font(Style.Family,size,bold?FontStyle.Bold:FontStyle.Regular),
            ForeColor=color,BackColor=Color.Transparent,Margin=new Padding(0,3,0,3),UseMnemonic=false};
    }
    internal static Button Button(string text,Action action,Color? accent=null)
    {
        // Ivy buttons are fully rounded (--r-full); filled when they carry the accent, tonal otherwise.
        var b=new ShellButton {Text=text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MinimumSize=new Size(0,36),
            Padding=new Padding(12,1,12,1),Margin=new Padding(0,0,8,6),FlatStyle=FlatStyle.Flat,UseVisualStyleBackColor=false,Pill=true,
            UseMnemonic=false,ForeColor=accent.HasValue?Night:Text,BackColor=accent??Raised,Cursor=Cursors.Hand};
        b.FlatAppearance.BorderSize=0;
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
    internal static readonly string[] Pages={"Dashboard","Workspaces","Performance","Appearance","Cleanup","Recovery"};
    private readonly Recovery recovery;
    private readonly WindowLayouts layouts;
    private readonly Workspace workspace;
    private readonly WinKeyHook winKey;
    private bool resting,manualRest,gameKey;
    private OwnStats own;
    private Label ownLabel;
    private readonly Timer heartbeat=new Timer {Interval=500};
    private readonly Dictionary<string,WorkspaceApp> apps=new Dictionary<string,WorkspaceApp>();
    private readonly Timer timer=new Timer {Interval=1000};
    private readonly ToolTip tips=Ui.Tip();
    private readonly Timer transition=new Timer{Interval=16};
    private readonly System.Diagnostics.Stopwatch pageMotion=new System.Diagnostics.Stopwatch();
    private readonly Panel body=new Panel {Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(24,18,24,14)};
    private readonly Label status=Ui.Label("",9,Ui.Muted);
    private readonly Dictionary<string,Button> tabs=new Dictionary<string,Button>();
    private readonly NotifyIcon tray;
    private readonly DockBar dock;
    private readonly bool test;
    private bool quitting,busy,hotkey,hoverOpened;
    private int hoverTicks;
    private string currentPage;
    private Label clock,date,metrics;
    private Launcher launcher;
    private long lastIdle,lastTotal;

    private DesktopState State { get { return recovery.State; } }
    internal string DesktopImage {get{return State.SurfaceImage;}}
    internal bool ShowSeconds {get{return State.Seconds;}}
    internal Color Accent { get { return Ui.Accent(State.Accent); } }
    internal string AccentName { get { return State.Accent; } }
    internal Color Highlight { get { return Ui.Tertiary(State.Accent); } }
    internal bool AutoTile { get { return State.Desktop && State.Auto; } }
    internal Screen Monitor { get { return Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==State.Monitor)??Screen.PrimaryScreen; } }

    internal Shell(Recovery recovery,bool background,bool test=false)
    {
        this.recovery=recovery;
        layouts=new WindowLayouts(recovery);
        workspace=new Workspace(this,layouts);
        // A tap of the Windows key opens the launcher. The hook only posts a message; the work happens on the next turn of the loop.
        winKey=new WinKeyHook(delegate{ if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(()=>Run(ToggleLauncher))); });
        this.test=test; InitializePins();
        Motion.Allowed=State.Animate;
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
        nav.Width=248;
        nav.Dock=DockStyle.Left;
        nav.BackColor=Ui.Surface;
        nav.Padding=new Padding(14,22,9,10);
        var searchBox=new SearchField("Search settings") {Width=216,Margin=new Padding(0,0,0,10),BackColor=Ui.SurfaceLow};
        var filter=searchBox.Input;
        nav.Controls.Add(searchBox);
        filter.TextChanged+=delegate{
            foreach(var pair in tabs)pair.Value.Visible=(pair.Key+" "+((ShellButton)pair.Value).Detail).IndexOf(filter.Text,StringComparison.CurrentCultureIgnoreCase)>=0;
        };
        filter.KeyDown+=delegate(object sender,KeyEventArgs e){
            if(e.KeyCode==Keys.Enter){var first=tabs.FirstOrDefault(t=>t.Value.Visible);if(first.Value!=null)Render(first.Key);e.SuppressKeyPress=true;}
            if(e.KeyCode==Keys.Escape){filter.Clear();e.SuppressKeyPress=true;}
        };
        nav.AutoScroll=true;
        foreach (var page in Pages)
        {
            var destination=page;
            var button=B(page,()=>Render(destination));
            ((ShellButton)button).Glyph=page=="Workspaces"?"Layouts":page=="Dashboard"?"Desktop":page=="Appearance"?"Settings":page=="Performance"?"Bolt":page;
            button.AutoSize=false;
            button.Size=new Size(216,62);
            ((ShellButton)button).Detail=page=="Dashboard"?"Apps, clock and media":page=="Workspaces"?"Displays and tiling":page=="Performance"?"Games, memory and speed":page=="Appearance"?"Wallpaper and colors":page=="Cleanup"?"Startup and apps":"Backups and restore";
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
        tray=new NotifyIcon {Icon=SystemIcons.Application,Visible=!test,Text="Bluehour Desktop",ContextMenuStrip=Ui.Menu()};
        tray.DoubleClick+=delegate{ShowPage("Dashboard");};
        tray.ContextMenuStrip.Items.Add("Show Bluehour desktop",null,delegate{Run(RevealDesktop);});
        tray.ContextMenuStrip.Items.Add("Open control center",null,delegate{Run(()=>ShowPage("Dashboard"));});
        tray.ContextMenuStrip.Items.Add("Return to Windows",null,delegate{Run(ReturnToWindows);});
        tray.ContextMenuStrip.Items.Add("Launcher",null,delegate{OpenLauncher();});
        tray.ContextMenuStrip.Items.Add("Undo window positions",null,delegate{Run(Undo);});
        tray.ContextMenuStrip.Items.Add("Exit Bluehour",null,delegate{ExitShell();});
        timer.Tick+=delegate{Run(Pulse);};
        heartbeat.Tick+=delegate{Diag.Beat();};
        if (!test) { timer.Start(); heartbeat.Start(); }
        // The pages are built when the control center is shown and released when it is hidden, so it costs no memory
        // while you are not looking at it.
        if (test) Render("Dashboard");
        VisibleChanged+=delegate
        {
            if (test) return;
            if (!Visible) { ReleasePage(); Mem.Trim(); }
            else if (body.Controls.Count==0) Render(currentPage??"Dashboard");
        };
        transition.Tick+=delegate{
            if(body.Controls.Count==0){transition.Stop();return;}
            double t=Math.Min(1,pageMotion.Elapsed.TotalMilliseconds/180);
            body.Controls[0].Left=body.AutoScrollPosition.X+23+(int)Math.Round(18*Math.Pow(1-t,3));
            if(t>=1){transition.Stop();pageMotion.Reset();}
        };
        Resize+=delegate{ResizeContent();};
        Shown+=delegate{
            if (test) return; LoadLibrary();
            Say(hotkey?"Ctrl + Alt + K opens the launcher.":"Open the launcher from the system tray.");
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
            gameKey=Native.RegisterHotKey(Handle,3,0x4003,(uint)Keys.G);
        }
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (hotkey) Native.UnregisterHotKey(Handle,1);
        if (recoveryKey) Native.UnregisterHotKey(Handle,2);
        if (gameKey) Native.UnregisterHotKey(Handle,3);
        base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message m)
    {
        if (WindowFrame.ResizeHit(this,ref m))return;
        if (m.Msg==0x0312)
        {
            if (m.WParam.ToInt32()==2) Run(ReturnToWindows);
            else if (m.WParam.ToInt32()==3) Run(ToggleRest);
            else if (m.WParam.ToInt32()==1) Run(OpenLauncher);
        }
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            transition.Dispose(); timer.Dispose();
            winKey.Dispose(); heartbeat.Dispose();
            workspace.Dispose();
            Toast.Dismiss();
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
        try { winKey.Stop(); workspace.Stop(); dock.DisableBar(); RestoreTaskbar(); remaining=layouts.Revert(); }
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

    // The frame style actually in use: "Native" needs Windows 11, so on anything older it falls back to "Off".
    internal string FrameStyle
    {
        get
        {
            if (State.Frames=="Sticker") return "Sticker";
            return State.Frames=="Native" && NativeFrame.Supported ? "Native" : "Off";
        }
    }

    private void ReleasePage()
    {
        transition.Stop();
        var old=body.Controls.Cast<Control>().ToArray();
        body.Controls.Clear();
        foreach (var control in old) control.Dispose();
        clock=date=metrics=null;
        ownLabel=null;
    }

    private void Say(string text) { status.Text=text.Replace(Environment.NewLine,"  •  "); tips.SetToolTip(status,text); }
    // Messages reach the person on the desktop as well as in the control center footer, which is usually hidden.
    internal void Notify(string text,bool error)
    {
        Say(text);
        if (!test) Toast.Post(Monitor,error?text:Ui.Voice(text),error,Accent);
    }
    internal void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { Notify(ex.Message,true); }
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
        int width=Math.Max(260,body.ClientSize.Width-55);
        foreach (Control item in body.Controls[0].Controls)
            if(item is SettingGroup)((SettingGroup)item).Fit(width);
            else if (item is Hero || item is ListBox || item.Name=="App board") item.Width=width;
            else item.MaximumSize=new Size(width,0);
    }

    internal void Render(string page)
    {
        bool switching=Visible && currentPage!=page && !test && SystemInformation.IsMenuAnimationEnabled;
        transition.Stop();currentPage=page;
        var old=body.Controls.Cast<Control>().ToArray();
        body.Controls.Clear();
        foreach (var control in old) control.Dispose();
        clock=date=metrics=null;
        foreach (var tab in tabs)
        {
            tab.Value.BackColor=tab.Key==page?Ui.AccentContainer(State.Accent):Ui.Raised;
            tab.Value.ForeColor=tab.Key==page?Ui.OnAccentContainer(State.Accent):Ui.Text;
        }
        var col=Ui.Flow(true);
        body.AutoScrollPosition=Point.Empty;col.Location=new Point(switching?41:23,18);
        body.Controls.Add(col);
        switch (page)
        {
            case "Workspaces": Workspaces(col); break;
            case "Performance": PerformancePage(col); break;
            case "Appearance": Appearance(col); break;
            case "Cleanup": Cleanup(col); break;
            case "Recovery": RecoveryPage(col); break;
            default: Dashboard(col); break;
        }
        if(page!="Dashboard")SettingGroup.Wrap(col);
        ResizeContent(); Widgets(); if(switching){pageMotion.Restart();transition.Start();}
    }

    private static void Title(Control panel,string title,string detail)
    {
        panel.Controls.Add(Ui.Heading(Ui.Voice(title),27,Ui.Text));
        Line(panel,detail);
    }
    private static void Heading(Control panel,string title)
    {
        var label=Ui.Label(Ui.Voice(title),13,Ui.Text,true);
        label.Tag="Section";label.Margin=new Padding(2,14,0,2);
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
    private Control Check(string label,bool value,Action<bool> change)
    {
        var check=new KuroSwitch(label,value);
        check.CheckedChanged+=delegate{Run(()=>change(check.Checked));};
        return check;
    }
    private Button Options(string label,string[] values,string selected,Action<string> change)
    {
        string value=values.Contains(selected)?selected:values[0];
        var menu=Ui.Menu();
        var button=(ShellButton)Ui.Button(value,delegate{});
        button.Chevron=true;button.TextAlign=ContentAlignment.MiddleLeft;
        button.AccessibleName=label;button.AutoSize=false;button.Width=184;
        button.Click+=delegate{menu.Show(button,new Point(0,button.Height));};
        foreach(var item in values)
        {
            string choice=item;
            var option=new ToolStripMenuItem(choice){Checked=value==choice};
            option.Click+=delegate{Run(()=>{
                foreach(ToolStripMenuItem entry in menu.Items)entry.Checked=entry==option;
                button.Text=choice;change(choice);
            });};
            menu.Items.Add(option);
        }
        button.Disposed+=delegate{menu.Dispose();};
        return button;
    }    private static CheckedListBox List(string label,int height)
    {
        return new StyledCheckList {Height=height,Width=620,CheckOnClick=true,BorderStyle=BorderStyle.None,
            BackColor=Ui.Surface,ForeColor=Ui.Text,AccessibleName=label,Margin=new Padding(0,8,0,2)};
    }

    private void PerformancePage(Control panel)
    {
        Title(panel,"Performance","What Bluehour costs, and the settings that make games and apps run lighter.");
        Heading(panel,"Bluehour right now");
        ownLabel=Ui.Label(new OwnStats().Read(),11,Accent);
        panel.Controls.Add(ownLabel);
        Line(panel,"Bluehour sleeps between events. It has no polling loop and no animation timers while nothing is happening.");
        Row(panel,B("Trim Bluehour's memory now",()=>{Mem.Trim();Say("Memory handed back to Windows.");}));
        Heading(panel,"While you play");
        Row(panel,Check("Rest Bluehour automatically while a game or full-screen app is open",State.GameMode,v=>{State.GameMode=v; recovery.Save(); CheckRest();}));
        Line(panel,"Resting stops window tracking, removes the Windows key hook, hides the dock and runs Bluehour at low priority. Ctrl + Alt + G rests or wakes it by hand.");
        Heading(panel,"How Bluehour looks");
        Row(panel,Options("Window frames",new[]{"Native","Sticker","Off"},State.Frames,v=>{State.Frames=v; recovery.Save(); workspace.ChangeFrames();}),
            Check("Animations",State.Animate,v=>{State.Animate=v; Motion.Allowed=v; recovery.Save();}));
        Line(panel,"Native uses Windows 11's own window borders and costs nothing. Sticker draws Bluehour's frame with an extra window per app, which is heavier. Off only tiles windows.");
        Heading(panel,"Windows settings for gaming");
        Row(panel,B("Apply gaming settings",()=>{recovery.SetGaming();Say("Game Mode on, Game Bar overlay and Game DVR capture off.");},true),
            B("High performance power plan",UsePower));
        Row(panel,B("Turn off transparency",()=>{recovery.SetTransparency(false);Say("Transparency effects are off.");}),
            B("Turn off mouse acceleration",()=>{recovery.SetMouseAcceleration(false);Say("Mouse acceleration is off.");}));
        Line(panel,"These are ordinary Windows settings. Recovery → Revert Bluehour changes puts every one of them back.");
    }

    private async void UsePower()
    {
        if (busy || test) return;
        busy=true;
        try
        {
            Say("Changing the power plan.");
            string before=await Task.Run(()=>PowerPlan.UseHighPerformance());
            if (before==null) { Notify("Windows would not switch the power plan.",true); return; }
            if (State.PowerBefore=="") State.PowerBefore=before;
            recovery.Save();
            Notify("High performance power plan is on. Revert Bluehour changes brings back the previous one.",false);
        }
        catch (Exception ex) { Notify(ex.Message,true); }
        finally { busy=false; }
    }

    private void RestorePower()
    {
        if (State.PowerBefore=="") return;
        string plan=State.PowerBefore;
        State.PowerBefore="";
        try { recovery.Save(); } catch (IOException) { }
        Task.Run(()=>PowerPlan.Activate(plan));
    }

    private void Dashboard(Control panel)
    {
        var hero=new Hero(Accent) {Width=665,Height=215,Margin=new Padding(0,0,0,6)};
        var heading=Ui.Heading("control center",22,Color.White);
        heading.Location=new Point(30,22);
        clock=Ui.Heading("",50,Color.White);
        clock.Location=new Point(24,88);
        date=Ui.Label("",11,Ui.Text,true);
        date.Location=new Point(32,176);
        hero.Controls.AddRange(new Control[]{heading,clock,date});
        panel.Controls.Add(hero);
        Row(panel,B(State.Desktop?"Show Bluehour desktop":"Switch to Bluehour desktop",RevealDesktop,true),
            B("Open Discord",OpenDiscord),B(State.Desktop?"Return to Windows":"Open launcher",State.Desktop?(Action)ReturnToWindows:OpenLauncher));
        if (recoveryKey) Line(panel,"Ctrl + Alt + Backspace returns to the Windows desktop.");
        Heading(panel,"Your apps");
        var board=new Panel {Name="App board",AccessibleName="Pinned apps",Width=600,Height=118,AutoScroll=true,BackColor=Ui.Surface};
        board.SizeChanged+=delegate
        {
            if (board.Width<2||board.Height<2) return;
            using (var shape=Style.Round(board.ClientRectangle,26)) { var previous=board.Region; board.Region=new Region(shape); if (previous!=null) previous.Dispose(); }
        };
        panel.Controls.Add(board);
        new BubbleLayer(this,board,false);
        Row(panel,B("Pin an app…",PickApp));
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
            State.Monitor=screens[Array.IndexOf(names,value)].DeviceName; recovery.Save(); UpdateDock(); RefreshSurfaces(); workspace.Refresh(); Render("Workspaces");
        }),Options("Window layout",Modes,State.Layout,value=>ChooseLayout(value)));
        var list=List("Windows to include",164);
        foreach (var window in DesktopWindow.List(Monitor,false)) list.Items.Add(window,true);
        panel.Controls.Add(list);
        Row(panel,B("Arrange checked windows",()=>Arrange(list.CheckedItems.Cast<DesktopWindow>(),list.Items.Cast<DesktopWindow>()),true),
            B("Refresh windows",()=>Render("Workspaces")),B("Undo placement",Undo));
        Row(panel,Check("Tile windows automatically when they open or close",State.Auto,value=>{
            State.Auto=value; recovery.Save(); if (value && State.Desktop) workspace.Refresh();
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
        Row(panel,Options("Dock edge",new[]{"Bottom","Left","Right"},State.Edge,v=>{State.Edge=v; recovery.Save(); UpdateDock();}),
            Options("Accent",Ui.Accents,State.Accent,SetAccent));
        Heading(panel,"Shortcuts");
        Row(panel,Check("Windows key opens the Bluehour launcher instead of Start",State.WinKey,v=>{State.WinKey=v; recovery.Save(); UpdateWinKey();}));
        Line(panel,"Win + E, Win + Tab and other Windows shortcuts keep working. Ctrl + Alt + K always opens the launcher.");
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
        workspace.Stop();
        var issues=recovery.Restore();
        RestorePower();
        UpdateWinKey();
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
        workspace.ReleaseAll();
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

    internal void OpenDiscord() { OpenApp(AppLibrary.Builtins[2]); }

    // Every way of opening an app goes through here: a window that is already open is brought forward, otherwise the
    // app is started and its first window is framed and tiled into the workspace.
    internal void OpenApp(AppPin pin)
    {
        WorkspaceApp app;
        if (!apps.TryGetValue(pin.Target,out app)) { app=new WorkspaceApp(pin); apps[pin.Target]=app; }
        if (State.Desktop)
        {
            var existing=workspace.Existing(app);
            if (existing!=null) { workspace.Bring(existing); return; }
        }
        string message=app.Open();
        if (State.Desktop) workspace.Expect(app);
        Notify(message,false);
    }

    private void InstallDiscord()
    {
        string winget=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"Microsoft\WindowsApps\winget.exe");
        if (File.Exists(winget)) Open(winget,"install --id Discord.Discord --exact --source winget");
        else Open("https://discord.com/download");
    }

    internal void Calendar()
    {
        using (var form=new CalendarView(this)) form.ShowDialog();
    }

    private void UpdateDock()
    {
        if (test) return;
        if (!State.Dock && !State.Desktop) { dock.DisableBar(); dockAvailable=false; return; }
        dockAvailable=dock.EnableBar(Monitor,State.Edge);
        if (!dockAvailable) Say("Windows could not reserve dock space.");
    }

    // The Windows key: open the launcher, or close it again if it is already up.
    internal void ToggleLauncher()
    {
        if (launcher!=null && !launcher.IsDisposed && launcher.Visible) { launcher.Close(); return; }
        OpenLauncher();
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
            foreach (var accent in Ui.Accents)
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
        yield return new LaunchItem("Browser","Open your default web browser",()=>OpenApp(AppLibrary.Builtins[0]));
        yield return new LaunchItem("Workspaces","Arrange Windows apps",()=>ShowPage("Workspaces"));
        yield return new LaunchItem("Discord","Open Discord",OpenDiscord);
        yield return new LaunchItem("Dashboard","See calendar and performance",()=>ShowPage("Dashboard"));
        yield return new LaunchItem("Files","Open File Explorer",()=>OpenApp(AppLibrary.Builtins[1]));
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
                if (seen.Add(title)) yield return new LaunchItem(title,"Launch installed app",()=>OpenApp(AppLibrary.Pin(title,"file:"+file)));
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
        var windows=DesktopWindow.List(Monitor,false);
        if (!State.Desktop) Arrange(windows,windows);
        else if (workspace.Count==0) workspace.Arrange(windows,windows);
        else workspace.Relayout(true);
        dock.RefreshBar();
        if (currentPage=="Workspaces") Render("Workspaces");
    }

    internal string ActiveLayout { get { return State.Layout; } }
    internal string DockTime { get { return DateTime.Now.ToString(State.Seconds?"HH:mm:ss":"HH:mm"); } }

    private void Pulse()
    {
        CheckRest();
        if (resting) return;   // a game is in front: do nothing at all
        if (ownLabel!=null && ownLabel.IsHandleCreated)
        {
            if (own==null) own=new OwnStats();
            ownLabel.Text=own.Read();
        }
        Widgets();
        DesktopPulse();
        if (dockAvailable)
        {
            dock.RefreshBar();
            if (!dock.Hovering) { hoverTicks=0; hoverOpened=false; }
            else if (State.Hover && !Visible && !busy && !hoverOpened && ++hoverTicks>=2)
            { hoverOpened=true; ShowPage("Dashboard"); }
        }
    }

    private void Widgets()
    {
        if (clock==null) return;
        clock.Text=DockTime;
        date.Text=Ui.Voice(DateTime.Now.ToString("dddd, MMMM d"));
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
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        g.Clear(Parent==null?Ui.Night:Parent.BackColor);
        var card=new RectangleF(1,1,Width-2,Height-6);
        Style.Fill(g,new RectangleF(1,5,Width-2,Height-6),28,Color.FromArgb(245,Ui.EdgeDeep));
        using (var clip=Style.Round(card,28))
        {
            var saved=g.Save();
            g.SetClip(clip);
            using (var baseColor=new LinearGradientBrush(card,Ui.SurfaceHigh,Ui.Surface,18f))g.FillPath(baseColor,clip);
            if (image!=null)
            {
                float zoom=Math.Max(card.Width/image.Width,card.Height/image.Height);
                float w=image.Width*zoom,h=image.Height*zoom;
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.DrawImage(image,card.X+(card.Width-w)/2,card.Y+(card.Height-h)/2,w,h);
            }
            using (var wash=new LinearGradientBrush(card,Color.FromArgb(238,Ui.Night),Color.FromArgb(30,Ui.Night),0f))g.FillRectangle(wash,card);
            using (var glow=new LinearGradientBrush(card,Color.FromArgb(0,accent),Color.FromArgb(70,accent),90f))g.FillRectangle(glow,card);
            g.Restore(saved);
        }
        using (var outline=Style.Round(RectangleF.Inflate(card,-1.25f,-1.25f),27))
        using (var pen=new Pen(Ui.Edge,2.5f))g.DrawPath(pen,outline);
        Style.DrawSparkle(g,Width-44,38,11,accent);
        Style.DrawSparkle(g,Width-72,62,6,Color.FromArgb(210,Ui.Tertiary("Lavender")));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && image!=null) image.Dispose();
        base.Dispose(disposing);
    }
}




















