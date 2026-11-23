using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

internal sealed class AppBubble : Control
{
    private readonly Timer animation=new Timer{Interval=16};
    private readonly ToolTip hint=new ToolTip();
    private readonly Bitmap icon;
    private readonly Shell shell;
    private bool hover,pressed,dragging;
    private Point start,original;
    private float emphasis;
    private Point slideStart,slideEnd;
    private readonly System.Diagnostics.Stopwatch slide=new System.Diagnostics.Stopwatch();
    internal void SetPosition(Point destination,bool animate)
    {
        if(dragging)return;
        if(!animate||!SystemInformation.IsMenuAnimationEnabled){slide.Reset();Location=destination;return;}
        if(destination==Location)return;
        slideStart=Location;slideEnd=destination;slide.Restart();animation.Start();
    }
    internal AppPin Pin;
    internal event Action<AppBubble,Point> Dropped;

    internal AppBubble(Shell shell,AppPin pin)
    {
        this.shell=shell;Pin=pin;
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|
            ControlStyles.SupportsTransparentBackColor|ControlStyles.Selectable,true);
        BackColor=Color.Transparent; Size=new Size(90,84);Cursor=Cursors.Hand;
        AccessibleRole=AccessibleRole.PushButton;AccessibleName=pin.Name;AccessibleDescription="Launch app or drag to move";
        TabStop=true;Font=new Font(Style.Family,9,FontStyle.Bold);
        if(pin.Name!="Calculator"&&pin.Name!="Notepad"&&pin.Name!="Paint")icon=AppLibrary.Picture(pin);
        hint.SetToolTip(this,pin.Name+" · Drag to move");
        ContextMenuStrip=new ContextMenuStrip();
        ContextMenuStrip.Items.Add("Open "+pin.Name,null,delegate{Launch();});
        ContextMenuStrip.Items.Add("Unpin",null,delegate{shell.Run(()=>shell.EditPins(()=>shell.Pins.Remove(Pin)));});
        animation.Tick+=delegate{
            float goal=hover||dragging?1f:0f;
            emphasis+=(goal-emphasis)*.24f;
            if(slide.IsRunning)
            {
                double time=Math.Min(1,slide.Elapsed.TotalMilliseconds/190);
                double ease=1-Math.Pow(1-time,3);
                Location=new Point(slideStart.X+(int)Math.Round((slideEnd.X-slideStart.X)*ease),
                    slideStart.Y+(int)Math.Round((slideEnd.Y-slideStart.Y)*ease));
                if(time>=1)slide.Reset();
            }
            if(Math.Abs(goal-emphasis)<.015f&&!slide.IsRunning){emphasis=goal;animation.Stop();}
            Invalidate();
        };
    }
    private void Motion()
    {
        if(SystemInformation.IsMenuAnimationEnabled)animation.Start();
        else {emphasis=hover||dragging?1:0;Invalidate();}
    }
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover=true;Motion();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=false;Motion();}
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
        slide.Reset();pressed=true;start=PointToScreen(e.Location);original=Location;Capture=true;BringToFront();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);if(!pressed)return;
        var p=PointToScreen(e.Location);var delta=new Size(p.X-start.X,p.Y-start.Y);
        if(!dragging && Math.Abs(delta.Width)<SystemInformation.DragSize.Width/2 && Math.Abs(delta.Height)<SystemInformation.DragSize.Height/2)return;
        dragging=true;Cursor=Cursors.SizeAll;
        Location=new Point(Math.Max(0,Math.Min(Parent.ClientSize.Width-Width,original.X+delta.Width)),
            Math.Max(0,Math.Min(Parent.ClientSize.Height-Height,original.Y+delta.Height)));
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);if(e.Button!=MouseButtons.Left||!pressed)return;
        bool moved=dragging;pressed=false;dragging=false;Capture=false;Cursor=Cursors.Hand;Motion();
        if(moved && Dropped!=null)Dropped(this,Location);
        else if(!moved&&ClientRectangle.Contains(e.Location))Launch();
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if(!Capture&&pressed){pressed=false;dragging=false;Location=original;Cursor=Cursors.Hand;Motion();}
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){e.Handled=true;Launch();}
    }
    private void Launch(){shell.Run(()=>AppLibrary.Launch(Pin,shell));}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        int d=45+(int)(emphasis*6),x=(Width-d)/2,y=6-(int)(emphasis*3);
        Style.Fill(g,new RectangleF(x,y+3,d,d),d/2,Color.FromArgb(86,5,16,22));
        Style.Fill(g,new RectangleF(x,y,d,d),d/2,Color.FromArgb(246,27,48,58));
        using(var line=new Pen(Focused||hover?shell.Accent:Color.FromArgb(141,175,194),Focused?2:1))g.DrawEllipse(line,x,y,d,d);
        var symbol=new Rectangle(x+(d-25)/2,y+(d-25)/2,25,25);
        if(icon!=null){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(icon,symbol);}
        else if(Pin.Target=="builtin:web")
        {
            using(var pen=new Pen(shell.Accent,1.8f))
            {
                g.DrawEllipse(pen,symbol);g.DrawEllipse(pen,symbol.X+6,symbol.Y,13,25);
                g.DrawLine(pen,symbol.Left,symbol.Top+12,symbol.Right,symbol.Top+12);
            }
        }
        else if(Pin.Name=="Calculator"||Pin.Name=="Notepad"||Pin.Name=="Paint")Style.Icon(g,Pin.Name,symbol,shell.Accent);
        else if(Pin.Target.StartsWith("builtin:"))Style.Icon(g,Pin.Target=="builtin:files"?"Files":"Discord",symbol,shell.Accent);
        else TextRenderer.DrawText(g,Pin.Name.Substring(0,1),Font,symbol,shell.Accent,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        int width=Math.Min(Width-3,TextRenderer.MeasureText(Pin.Name,Font).Width+12);
        var caption=new Rectangle((Width-width)/2,57,width,22);
        Style.Fill(g,caption,11,Color.FromArgb(230,18,35,42));
        TextRenderer.DrawText(g,Pin.Name,Font,caption,Ui.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){animation.Dispose();hint.Dispose();if(icon!=null)icon.Dispose();if(ContextMenuStrip!=null)ContextMenuStrip.Dispose();}
        base.Dispose(disposing);
    }
}

internal sealed class BubbleLayer
{
    private readonly Shell shell;
    private readonly Control host;
    private readonly bool desktop;
    private readonly Dictionary<string,AppBubble> views=new Dictionary<string,AppBubble>(StringComparer.OrdinalIgnoreCase);
    private bool updating,ready;
    internal BubbleLayer(Shell shell,Control host,bool desktop)
    {
        this.shell=shell;this.host=host;this.desktop=desktop;
        shell.PinsChanged+=Sync;host.Resize+=delegate{Position(false);};
        host.Disposed+=delegate{shell.PinsChanged-=Sync;};
        Sync();
    }
    private void Sync()
    {
        foreach(var key in views.Keys.Where(k=>!shell.Pins.Any(a=>a.Target==k)).ToArray())
        {views[key].Dispose();views.Remove(key);}
        foreach(var pin in shell.Pins)
        {
            AppBubble b;
            if(!views.TryGetValue(pin.Target,out b))
            {
                b=new AppBubble(shell,pin);views.Add(pin.Target,b);host.Controls.Add(b);b.Dropped+=Drop;
            }
            b.Pin=pin;
        }
        Position(ready);ready=true;
    }
    private Rectangle Area()
    {
        int margin=desktop?22:6,top=desktop?44:6;
        return new Rectangle(margin,top,Math.Max(1,host.ClientSize.Width-2*margin-90),Math.Max(1,host.ClientSize.Height-top-(desktop?110:6)-84));
    }
    private int Columns(){return Math.Max(1,(host.ClientSize.Width-(desktop?44:18))/94);}
    private void Position(bool animate)
    {
        if(updating||host.IsDisposed)return;
        updating=true;
        try
        {
            var area=Area();int cols=Columns();var panel=host as Panel;
            if(!desktop&&panel!=null)panel.AutoScrollMinSize=new Size(0,12+84*((shell.Pins.Count+cols-1)/cols));
            var scroll=!desktop&&panel!=null?panel.AutoScrollPosition:Point.Empty;
            for(int i=0;i<shell.Pins.Count;i++)
            {
                var pin=shell.Pins[i];AppBubble view;
                if(!views.TryGetValue(pin.Target,out view))continue;
                var pos=new Point(area.X+(i%cols)*94+scroll.X,area.Y+(i/cols)*84+scroll.Y);
                if(desktop && pin.X>=0&&pin.Y>=0)pos=new Point(area.X+(int)(area.Width*pin.X),area.Y+(int)(area.Height*pin.Y));
                view.SetPosition(pos,animate&&host.Visible);
            }
        }
        finally{updating=false;}
    }
    private void Drop(AppBubble b,Point location)
    {
        shell.Run(()=>shell.EditPins(()=>{
            if(desktop)
            {
                var area=Area();
                b.Pin.X=Math.Max(0,Math.Min(1,(location.X-area.X)/(double)area.Width));
                b.Pin.Y=Math.Max(0,Math.Min(1,(location.Y-area.Y)/(double)area.Height));
            }
            else
            {
                var scroll=((Panel)host).AutoScrollPosition;
                int idx=Math.Max(0,(location.Y-scroll.Y+36)/84)*Columns()+Math.Max(0,(location.X-scroll.X+40)/94);
                idx=Math.Min(shell.Pins.Count-1,idx);
                shell.Pins.Remove(b.Pin);shell.Pins.Insert(idx,b.Pin);
            }
        }));
    }
}




