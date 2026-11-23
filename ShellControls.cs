using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Timer=System.Windows.Forms.Timer;

// The bottom "island": a floating pill of icons with a violet glow and sparkles, in the spirit of kuro.rip's goo dock.
// It is a per-pixel-alpha window drawn by hand so it can glow and animate, and it registers as an AppBar so that
// windows tile around the strip it floats in.
internal sealed class DockBar : LayeredForm
{
    private sealed class Slot
    {
        internal string Glyph,Label;
        internal Action Run;
        internal bool Primary,GroupStart,Active;
        internal RectangleF Rect;
        internal float Hover,Press;
        internal Image Image;     // an app's own icon, when it has one
        internal int Count;       // how many of its windows are open
        internal DockApp App;
    }

    private enum Side { Top,Left,Right }

    private readonly Shell shell;
    private readonly List<Slot> slots=new List<Slot>(),head=new List<Slot>(),tail=new List<Slot>();
    private string appsSignature="";
    private int gMargin,gThick,gRoom,gReach;
    private readonly Timer animation=new Timer {Interval=15};
    private readonly ContextMenuStrip layoutMenu=Ui.Menu();
    private readonly uint callback=Native.RegisterWindowMessage("Bluehour.AppBar");
    private readonly uint taskbarCreated=Native.RegisterWindowMessage("TaskbarCreated");
    private Screen monitor;
    private string edge="Bottom";
    private bool registered,vertical;
    private Rectangle reserved,placed;
    private RectangleF pill;
    private Size extent;
    private Side labelSide=Side.Top;
    private double scale=1;
    private int hot=-1,held=-1;
    private Color shownAccent;
    private string shownLayout="";

    internal DockBar(Shell shell)
    {
        this.shell=shell;
        TopMost=true;
        Text="Bluehour dock";
        Font=new Font(Style.Family,9);
        animation.Tick+=delegate{Tick();};
        foreach (var item in Shell.Modes)
        {
            string mode=item;
            layoutMenu.Items.Add(Ui.Voice(mode),null,delegate{shell.Run(()=>shell.ChooseLayout(mode));}).Tag=mode;
        }
        layoutMenu.Items.Add(new ToolStripSeparator());
        layoutMenu.Items.Add("manage windows…",null,delegate{shell.ShowPage("Workspaces");});
        Add("Launch","search",shell.OpenLauncher,true,false);
        Add("Layouts","layouts",ShowLayouts,false,false);
        Add("Desktop","show desktop",shell.RevealDesktop,false,false);
        Add("Files","files",()=>shell.OpenApp(AppLibrary.Builtins[1]),false,false);
        Add("Discord","discord",()=>shell.OpenApp(AppLibrary.Builtins[2]),false,false);
        Add("Calendar","calendar",shell.Calendar,false,true);
        Add("Sound","volume",()=>Shell.Open("ms-settings:sound"),false,false);
        Add("Network","network",()=>Shell.Open("ms-settings:network"),false,false);
        Add("Settings","control center",()=>shell.ShowPage("Dashboard"),false,false);
        head.AddRange(slots.Take(5));
        tail.AddRange(slots.Skip(5));
    }

    protected override bool PassThrough { get { return false; } }

    private void Add(string glyph,string label,Action run,bool primary,bool group)
    {
        slots.Add(new Slot {Glyph=glyph,Label=label,Run=run,Primary=primary,GroupStart=group});
    }

    public bool Hovering { get { return Visible && !pill.IsEmpty && Rectangle.Round(Offset(pill)).Contains(Cursor.Position); } }
    private RectangleF Offset(RectangleF local){return new RectangleF(local.X+placed.X,local.Y+placed.Y,local.Width,local.Height);}

    // ---- Geometry -----------------------------------------------------------------------------------------------

    private int Px(double value){return (int)Math.Round(value*scale);}

    // Lays the island out for a monitor and returns the window rectangle. Local coordinates are relative to that window.
    private Rectangle Compose(Rectangle display,string alignment)
    {
        vertical=alignment=="Left"||alignment=="Right";
        int cell=Px(42),gap=Px(6),pad=Px(9),group=Px(16),reach=Px(30),spark=Px(30),room=Px(40),margin=Px(12);
        int length=2*pad+slots.Count*cell+(slots.Count-1)*gap+slots.Count(s=>s.GroupStart)*(group-gap);
        int thick=cell+2*pad;
        labelSide=alignment=="Left"?Side.Right:alignment=="Right"?Side.Left:Side.Top;
        // 'near' is the strip between the screen edge and the island.
        if(!vertical)
        {
            extent=new Size(length+2*(reach+spark),room+thick+reach);
            pill=new RectangleF(reach+spark,room,length,thick);
        }
        else
        {
            extent=new Size(room+thick+reach,length+2*(reach+spark));
            pill=new RectangleF(alignment=="Left"?reach:room,reach+spark,thick,length);
        }
        float at=pad;
        foreach(var slot in slots)
        {
            if(slot.GroupStart)at+=group-gap;
            slot.Rect=vertical?new RectangleF(pill.X+pad,pill.Y+at,cell,cell):new RectangleF(pill.X+at,pill.Y+pad,cell,cell);
            at+=cell+gap;
        }
        reserveThickness=thick+2*margin;
        gMargin=margin;gThick=thick;gRoom=room;gReach=reach;
        if(alignment=="Left")return new Rectangle(display.Left+margin-reach,display.Top+(display.Height-extent.Height)/2,extent.Width,extent.Height);
        if(alignment=="Right")return new Rectangle(display.Right-margin+reach-extent.Width,display.Top+(display.Height-extent.Height)/2,extent.Width,extent.Height);
        return new Rectangle(display.Left+(display.Width-extent.Width)/2,display.Bottom-margin-thick-room,extent.Width,extent.Height);
    }
    private int reserveThickness;

    // ---- AppBar -------------------------------------------------------------------------------------------------

    internal bool EnableBar(Screen screen,string alignment)
    {
        DisableBar();
        monitor=screen;
        edge=alignment=="Left"||alignment=="Right"?alignment:"Bottom";
        scale=Native.Scale(Handle);
        var data=Data();
        registered=Native.SHAppBarMessage(0,ref data)!=UIntPtr.Zero;
        if (!registered) return false;
        if (!Position()) { DisableBar(); return false; }
        Show();
        Render();
        return true;
    }

    internal void DisableBar()
    {
        if (registered)
        {
            registered=false;
            var data=Data();
            Native.SHAppBarMessage(1,ref data);
        }
        animation.Stop();
        Hide();
        reserved=Rectangle.Empty;
    }

    private Native.Bar Data()
    {
        return new Native.Bar {Size=Marshal.SizeOf(typeof(Native.Bar)),Handle=Handle,Notification=callback};
    }

    // While Bluehour rests the island is hidden: a topmost window above a game costs it frames.
    internal void Rest(bool on)
    {
        if(on){animation.Stop();Hide();}
        else if(registered){Show();Render();}
    }

    private bool Position()
    {
        var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==monitor.DeviceName)??Screen.PrimaryScreen;
        scale=Native.Scale(Handle);
        placed=Compose(screen.Bounds,edge);
        var data=Data();
        data.Edge=edge=="Right"?2u:edge=="Left"?0u:3u;
        data.Bounds=new Native.Rect(screen.Bounds);
        Native.SHAppBarMessage(2,ref data);
        // Reserve the strip the island floats in, so windows tile above (or beside) it.
        switch (data.Edge)
        {
            case 0: data.Bounds.R=data.Bounds.L+reserveThickness; break;
            case 2: data.Bounds.L=data.Bounds.R-reserveThickness; break;
            default: data.Bounds.T=data.Bounds.B-reserveThickness; break;
        }
        if (reserved!=data.Bounds.Rectangle)
        {
            if (Native.SHAppBarMessage(3,ref data)==UIntPtr.Zero) return false;
            reserved=data.Bounds.Rectangle;
        }
        // Float inside the strip Windows approved, not at the edge of the screen: when the taskbar is showing, the
        // approved strip sits above it.
        placed=Place(reserved);
        Render();
        return true;
    }

    private Rectangle Place(Rectangle strip)
    {
        if(edge=="Left")return new Rectangle(strip.Left+gMargin-gReach,strip.Top+(strip.Height-extent.Height)/2,extent.Width,extent.Height);
        if(edge=="Right")return new Rectangle(strip.Right-gMargin+gReach-extent.Width,strip.Top+(strip.Height-extent.Height)/2,extent.Width,extent.Height);
        return new Rectangle(strip.Left+(strip.Width-extent.Width)/2,strip.Bottom-gMargin-gThick-gRoom,extent.Width,extent.Height);
    }

    // How many app icons fit before the island would be wider than most of the screen.
    internal int Room
    {
        get
        {
            var screen=monitor==null?Screen.PrimaryScreen:monitor;
            double width=vertical?screen.Bounds.Height:screen.Bounds.Width;
            int cell=(int)Math.Round(48*Math.Max(1,scale));
            return Math.Max(0,(int)((width*.82)/cell)-head.Count-tail.Count);
        }
    }

    // Replaces the app section of the island. Nothing is redrawn unless the apps, their windows or the active one changed.
    internal void SetApps(List<DockApp> apps)
    {
        string signature=String.Join("|",apps.Select(a=>a.Name+":"+a.Windows.Count+(a.Active?"*":"")));
        if(signature==appsSignature)return;
        appsSignature=signature;
        var list=new List<Slot>(head);
        bool first=true;
        foreach(var app in apps)
        {
            var owner=app;
            list.Add(new Slot {Glyph=app.Glyph,Image=app.Icon,Label=Ui.Voice(app.Name),Count=app.Windows.Count,Active=app.Active,App=app,
                GroupStart=first,Run=()=>shell.ActivateApp(owner)});
            first=false;
        }
        list.AddRange(tail);
        slots.Clear();slots.AddRange(list);
        hot=held=-1;
        if(!registered||monitor==null)return;
        var screen2=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==monitor.DeviceName)??Screen.PrimaryScreen;
        Compose(screen2.Bounds,edge);
        placed=Place(reserved.IsEmpty?screen2.Bounds:reserved);
        Render();
    }

    protected override void WndProc(ref Message m)
    {
        if (registered && (m.Msg==callback || m.Msg==taskbarCreated || m.Msg==0x007e))
        {
            if (m.Msg==callback && m.WParam.ToInt32()==2)
            {
                TopMost=m.LParam==IntPtr.Zero;
                if (!TopMost) Native.SetWindowPos(Handle,new IntPtr(1),0,0,0,0,0x13);
            }
            else if (m.Msg==taskbarCreated)
                BeginInvoke((Action)(()=>shell.Run(()=>EnableBar(monitor,edge))));
            else if (m.Msg!=callback || m.WParam==IntPtr.Zero)
                BeginInvoke((Action)(()=>shell.Run(()=>{if (registered) Position();})));
        }
        base.WndProc(ref m);
    }

    // ---- Interaction --------------------------------------------------------------------------------------------

    private int SlotAt(Point point)
    {
        for(int i=0;i<slots.Count;i++)
            if(RectangleF.Inflate(slots[i].Rect,Px(3),Px(3)).Contains(point))return i;
        return -1;
    }

    private void ShowLayouts()
    {
        foreach(ToolStripItem item in layoutMenu.Items)
        {
            var entry=item as ToolStripMenuItem;
            if(entry!=null&&entry.Tag!=null)entry.Checked=(string)entry.Tag==shell.ActiveLayout;
        }
        var slot=slots[1];
        var at=new Point((int)(placed.X+slot.Rect.X),(int)(placed.Y+slot.Rect.Y));
        var direction=edge=="Left"?ToolStripDropDownDirection.AboveRight:edge=="Right"?ToolStripDropDownDirection.AboveLeft:ToolStripDropDownDirection.AboveRight;
        layoutMenu.Show(new Point(at.X,at.Y-Px(8)),direction);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index=SlotAt(e.Location);
        if(index==hot)return;
        hot=index;
        Cursor=index>=0?Cursors.Hand:Cursors.Default;
        Wake();
    }
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=-1;if(held<0)Wake();}
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if(e.Button!=MouseButtons.Left||hot<0)return;
        held=hot;Capture=true;Wake();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if(e.Button==MouseButtons.Right)
        {
            int index=SlotAt(e.Location);
            if(index>=0 && slots[index].App!=null){var app=slots[index].App;shell.Run(()=>shell.DockMenu(app,Cursor.Position));}
            return;
        }
        if(held<0)return;
        int pressed=held;
        held=-1;Capture=false;
        hot=SlotAt(e.Location);
        Wake();
        // Like every button here, a click lands when the mouse is released over the thing that was pressed.
        if(hot==pressed)shell.Run(slots[pressed].Run);
    }

    private void Wake()
    {
        if(!Motion.Enabled)
        {
            for(int i=0;i<slots.Count;i++){slots[i].Hover=i==hot?1:0;slots[i].Press=i==held&&i==hot?1:0;}
            Render();
            return;
        }
        if(!animation.Enabled)animation.Start();
    }

    private void Tick()
    {
        bool moving=false;
        for(int i=0;i<slots.Count;i++)
        {
            var slot=slots[i];
            float h=i==hot?1f:0f,p=i==held&&i==hot?1f:0f;
            slot.Hover+=(h-slot.Hover)*.3f;slot.Press+=(p-slot.Press)*.5f;
            if(Math.Abs(h-slot.Hover)<.02f&&Math.Abs(p-slot.Press)<.02f){slot.Hover=h;slot.Press=p;}
            else moving=true;
        }
        Render();
        if(!moving)animation.Stop();
    }

    // Called every second by the shell: repaint when the accent or the active layout changed.
    internal void RefreshBar()
    {
        if(!Visible)return;
        if(shownAccent!=shell.Accent||shownLayout!=shell.ActiveLayout)Render();
    }

    // ---- Drawing ------------------------------------------------------------------------------------------------

    private void Render()
    {
        if(!IsHandleCreated||placed.IsEmpty)return;
        shownAccent=shell.Accent;shownLayout=shell.ActiveLayout;
        Present(placed,255,delegate(Graphics g){Draw(g);});
    }

    // Draws the whole island into a bitmap the size of the window; used by the screenshots in the self-test too.
    internal Bitmap Snapshot(Rectangle display,string alignment,double dpiScale,out Point origin)
    {
        scale=dpiScale;
        var window=Compose(display,alignment);
        placed=window;
        origin=window.Location;
        var bitmap=new Bitmap(window.Width,window.Height);
        using(var g=Graphics.FromImage(bitmap))
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAlias;
            Draw(g);
        }
        return bitmap;
    }

    private void Draw(Graphics g)
    {
        var accent=shell.Accent;
        float radius=Math.Min(pill.Width,pill.Height)/2f;
        // Violet glow, like the drop-shadow on kuro's dock.
        for(int grow=Px(26);grow>=2;grow-=2)
        {
            var halo=RectangleF.Inflate(pill,grow,grow);
            Style.Fill(g,halo,radius+grow,Color.FromArgb(5,170,90,255));
        }
        // Hard shadow and body.
        float drop=Px(4);
        Style.Fill(g,new RectangleF(pill.X,pill.Y+(vertical?0:drop),pill.Width+(vertical?drop:0),pill.Height),radius,Color.FromArgb(245,Ui.EdgeDeep));
        using(var path=Style.Round(pill,radius))
        {
            using(var body=new LinearGradientBrush(pill,Ui.SurfaceHigh,Ui.Surface,vertical?0f:90f))g.FillPath(body,path);
            using(var sheen=new Pen(Color.FromArgb(34,255,255,255),1f))
            using(var inner=Style.Round(RectangleF.Inflate(pill,-2.5f,-2.5f),radius-2.5f))g.DrawPath(sheen,inner);
            using(var edgePen=new Pen(Ui.Edge,2f))
            using(var outline=Style.Round(RectangleF.Inflate(pill,-1,-1),radius-1))g.DrawPath(edgePen,outline);
        }
        // Sparkles at both ends.
        float r1=Px(9),r2=Px(5);
        float endA=Px(15),endB=Px(15);
        PointF a,b;
        if(vertical){a=new PointF(pill.X+pill.Width/2,pill.Y-endA);b=new PointF(pill.X+pill.Width/2,pill.Bottom+endB);}
        else{a=new PointF(pill.X-endA,pill.Y+pill.Height/2);b=new PointF(pill.Right+endB,pill.Y+pill.Height/2);}
        Style.DrawSparkle(g,a.X,a.Y,r1,Ui.Tertiary(shell.AccentName));
        Style.DrawSparkle(g,a.X+(vertical?r1:-r1)*.9f,a.Y+(vertical?-r1:-r1)*.9f,r2,Color.FromArgb(200,accent));
        Style.DrawSparkle(g,b.X,b.Y,r1,accent);
        Style.DrawSparkle(g,b.X+(vertical?-r1:r1)*.9f,b.Y+(vertical?r1:-r1)*.9f,r2,Color.FromArgb(200,Ui.Tertiary(shell.AccentName)));
        // Group divider: a small diamond.
        foreach(var slot in slots)
        {
            if(!slot.GroupStart)continue;
            var mid=vertical?new PointF(pill.X+pill.Width/2,slot.Rect.Y-Px(8)):new PointF(slot.Rect.X-Px(8),pill.Y+pill.Height/2);
            using(var brush=new SolidBrush(Color.FromArgb(150,230,190,255)))
                g.FillPolygon(brush,new[]{new PointF(mid.X,mid.Y-Px(3)),new PointF(mid.X+Px(3),mid.Y),new PointF(mid.X,mid.Y+Px(3)),new PointF(mid.X-Px(3),mid.Y)});
        }
        for(int i=0;i<slots.Count;i++)DrawSlot(g,slots[i],accent);
        for(int i=0;i<slots.Count;i++)if(slots[i].Hover>.02f)DrawLabel(g,slots[i]);
    }

    private void DrawSlot(Graphics g,Slot slot,Color accent)
    {
        float h=slot.Hover,p=slot.Press;
        float grow=1+.09f*h-.12f*p;
        var center=new PointF(slot.Rect.X+slot.Rect.Width/2,slot.Rect.Y+slot.Rect.Height/2-Px(3)*h+Px(2)*p);
        float d=slot.Rect.Width*grow;
        var disc=new RectangleF(center.X-d/2,center.Y-d/2,d,d);
        if(slot.Primary)
        {
            Style.Fill(g,new RectangleF(disc.X,disc.Y+Px(3)-Px(2)*p,disc.Width,disc.Height),d/2,Color.FromArgb(230,Style.Mix(accent,Ui.SurfaceLowest,.6)));
            Style.Fill(g,disc,d/2,Style.Mix(accent,Color.White,h*.12));
        }
        else if(h>.02f)Style.Fill(g,disc,d/2,Color.FromArgb((int)(255*Math.Min(1,h)),Ui.Raised));
        float icon=Px(22)*grow;
        Color ink=slot.Primary?Ui.Night:Style.Mix(Ui.Muted,Ui.Text,h);
        if(slot.Image!=null)
        {
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.DrawImage(slot.Image,new RectangleF(center.X-icon/2,center.Y-icon/2,icon,icon));
        }
        else Style.Icon(g,slot.Glyph,new RectangleF(center.X-icon/2,center.Y-icon/2,icon,icon),ink);
        // Open windows: a dot under the icon, longer and in the accent colour for the one you are using.
        if(slot.Count>0)
        {
            float w=slot.Active?Px(16):Px(5),t=Px(5);
            var at=vertical?new RectangleF(slot.Rect.Right+Px(2),center.Y-(slot.Active?w:t)/2,t,slot.Active?w:t)
                :new RectangleF(center.X-w/2,slot.Rect.Bottom+Px(1),w,t);
            Style.Fill(g,at,t/2,slot.Active?accent:Ui.Muted);
        }
        // The layouts button shows which layout is active as a small accent dot.
        if(slot.Glyph=="Layouts")
            using(var brush=new SolidBrush(accent))g.FillEllipse(brush,center.X+d*.22f,center.Y+d*.22f,Px(7),Px(7));
    }

    private void DrawLabel(Graphics g,Slot slot)
    {
        float t=Math.Min(1,slot.Hover);
        using(var font=new Font(Style.Family,(float)(12*scale),FontStyle.Bold,GraphicsUnit.Pixel))
        using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
        {
            var size=g.MeasureString(slot.Label,font,PointF.Empty,format);
            float w=size.Width+Px(26),h=Px(30);
            float x,y;
            var c=new PointF(slot.Rect.X+slot.Rect.Width/2,slot.Rect.Y+slot.Rect.Height/2);
            if(labelSide==Side.Top){x=c.X-w/2;y=pill.Y-h-Px(6)-Px(4)*(1-t);}
            else if(labelSide==Side.Right){x=pill.Right+Px(10)+Px(4)*(1-t);y=c.Y-h/2;}
            else{x=pill.X-w-Px(10)-Px(4)*(1-t);y=c.Y-h/2;}
            var box=new RectangleF(x,y,w,h);
            Style.Fill(g,new RectangleF(box.X,box.Y+Px(3),box.Width,box.Height),h/2,Color.FromArgb((int)(235*t),Ui.EdgeDeep));
            Style.Fill(g,box,h/2,Color.FromArgb((int)(255*t),Ui.Surface));
            using(var outline=Style.Round(RectangleF.Inflate(box,-1,-1),h/2-1))
            using(var pen=new Pen(Color.FromArgb((int)(255*t),Ui.Edge),2f))g.DrawPath(pen,outline);
            using(var brush=new SolidBrush(Color.FromArgb((int)(255*t),Ui.Text)))
                g.DrawString(slot.Label,font,brush,box.X+Px(13),box.Y+(h-size.Height)/2f,format);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { if (registered) DisableBar(); animation.Dispose(); layoutMenu.Dispose(); }
        base.Dispose(disposing);
    }
}

// One app in the dock: a pinned app, or a program that is running, with its open windows.
internal sealed class DockApp
{
    internal string Name,Glyph;
    internal AppPin Pin;
    internal readonly List<DesktopWindow> Windows=new List<DesktopWindow>();
    internal Bitmap Icon;
    internal bool Active;
}

internal sealed class LaunchItem
{
    public readonly string Name,Detail;
    public readonly Action Run;
    public LaunchItem(string name,string detail,Action run) { Name=name; Detail=detail; Run=run; }
    public override string ToString() { return Name+"  ·  "+Detail; }
}
