using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

internal sealed class AppBubble : Control
{
    private readonly Timer animation=new Timer{Interval=16};
    private readonly ToolTip hint=Ui.Tip();
    private readonly Bitmap icon;
    private readonly Shell shell;
    private bool hover,pressed,dragging;
    private Point start,original;
    private float emphasis,sink;
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
        BackColor=Color.Transparent; Size=new Size(BubbleLayer.CellW-6,BubbleLayer.CellH-6);Cursor=Cursors.Hand;
        AccessibleRole=AccessibleRole.PushButton;AccessibleName=pin.Name;AccessibleDescription="Launch app or drag to move";
        TabStop=true;Font=new Font(Style.Family,9,FontStyle.Bold);
        if(pin.Name!="Calculator"&&pin.Name!="Notepad"&&pin.Name!="Paint")icon=AppLibrary.Picture(pin);
        hint.SetToolTip(this,Ui.Voice(pin.Name)+" · drag to move");
        ContextMenuStrip=Ui.Menu();
        ContextMenuStrip.Items.Add("open "+Ui.Voice(pin.Name),null,delegate{Launch();});
        ContextMenuStrip.Items.Add("unpin",null,delegate{shell.Run(()=>shell.EditPins(()=>shell.Pins.Remove(Pin)));});
        animation.Tick+=delegate{
            float goal=hover||dragging?1f:0f,hold=pressed&&!dragging?1f:0f;
            emphasis+=(goal-emphasis)*.3f;
            sink+=(hold-sink)*.5f;
            if(slide.IsRunning)
            {
                double time=Math.Min(1,slide.Elapsed.TotalMilliseconds/190);
                double ease=1-Math.Pow(1-time,3);
                Location=new Point(slideStart.X+(int)Math.Round((slideEnd.X-slideStart.X)*ease),
                    slideStart.Y+(int)Math.Round((slideEnd.Y-slideStart.Y)*ease));
                if(time>=1)slide.Reset();
            }
            if(Math.Abs(goal-emphasis)<.015f&&Math.Abs(hold-sink)<.02f&&!slide.IsRunning){emphasis=goal;sink=hold;animation.Stop();}
            Invalidate();
        };
    }
    private void Motion()
    {
        if(SystemInformation.IsMenuAnimationEnabled)animation.Start();
        else {emphasis=hover||dragging?1:0;sink=pressed&&!dragging?1:0;Invalidate();}
    }
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover=true;Motion();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=false;Motion();}
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
        slide.Reset();pressed=true;start=PointToScreen(e.Location);original=Location;Capture=true;BringToFront();Motion();
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
    private void Launch(){shell.Run(()=>shell.OpenApp(Pin));}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAlias;
        var accent=shell.Accent;
        const int tile=64;
        float x=(Width-tile)/2f,y=6;
        // A sticker: the hard shadow stays where it is; hover lifts the tile away from it, press sinks it onto it.
        float grow=1+emphasis*.07f-sink*.05f,dy=-emphasis*4f+sink*3.4f;
        Style.Fill(g,new RectangleF(x,y+5,tile,tile),22,Color.FromArgb(240,Ui.EdgeDeep));
        float size=tile*grow;
        var body=new RectangleF(x+(tile-size)/2,y+dy+(tile-size)/2,size,size);
        using(var path=Style.Round(body,22))
        {
            using(var fill=new LinearGradientBrush(body,Ui.SurfaceHigh,Ui.Surface,90f))g.FillPath(fill,path);
            using(var pen=new Pen(Focused||hover?accent:Ui.Edge,2.5f))g.DrawPath(pen,path);
        }
        using(var sheen=new Pen(Color.FromArgb(30,255,255,255),1f))
        using(var inner=Style.Round(RectangleF.Inflate(body,-4.5f,-4.5f),18))g.DrawPath(sheen,inner);
        var symbol=new RectangleF(body.X+(body.Width-30)/2,body.Y+(body.Height-30)/2,30,30);
        if(icon!=null){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(icon,symbol);}
        else if(Pin.Target=="builtin:web")
        {
            using(var pen=new Pen(accent,2.1f))
            {
                g.DrawEllipse(pen,symbol);g.DrawEllipse(pen,symbol.X+7,symbol.Y,16,30);
                g.DrawLine(pen,symbol.Left,symbol.Top+15,symbol.Right,symbol.Top+15);
            }
        }
        else if(Pin.Name=="Calculator"||Pin.Name=="Notepad"||Pin.Name=="Paint")Style.Icon(g,Pin.Name,symbol,accent);
        else if(Pin.Target.StartsWith("builtin:"))Style.Icon(g,Pin.Target=="builtin:files"?"Files":"Discord",symbol,accent);
        else TextRenderer.DrawText(g,Pin.Name.Substring(0,1).ToLowerInvariant(),Font,Rectangle.Round(symbol),accent,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        string name=Ui.Voice(Pin.Name);
        int width=Math.Min(Width-2,TextRenderer.MeasureText(name,Font).Width+16);
        var caption=new Rectangle((Width-width)/2,(int)y+tile+15,width,22);
        Style.Fill(g,caption,11,Color.FromArgb(222,Ui.Night));
        TextRenderer.DrawText(g,name,Font,caption,Ui.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
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
    internal const int CellW=98,CellH=118;
    private Rectangle Area()
    {
        if(desktop)
        {
            var surface=host as DesktopSurface;
            var work=surface!=null?surface.Work:host.ClientRectangle;
            int left=work.Left+Math.Max(18,Math.Min(34,work.Width/56));
            int top=work.Top+(int)Math.Round(DesktopSurface.TopBar*Native.Scale(host.IsHandleCreated?host.Handle:IntPtr.Zero))+10;
            return new Rectangle(left,top,Math.Max(1,work.Right-left*2-CellW),Math.Max(1,work.Bottom-top-28-CellH));
        }
        return new Rectangle(6,6,Math.Max(1,host.ClientSize.Width-12-CellW),Math.Max(1,host.ClientSize.Height-12-CellH));
    }
    private int Columns(){return Math.Max(1,(host.ClientSize.Width-18)/CellW);}
    // On the desktop the stickers stack in columns down the left edge.
    private int Rows(){return Math.Max(1,Area().Height/CellH+1);}
    private void Position(bool animate)
    {
        if(updating||host.IsDisposed)return;
        updating=true;
        try
        {
            var area=Area();int cols=Columns(),rows=Rows();var panel=host as Panel;
            if(!desktop&&panel!=null)panel.AutoScrollMinSize=new Size(0,12+CellH*((shell.Pins.Count+cols-1)/cols));
            var scroll=!desktop&&panel!=null?panel.AutoScrollPosition:Point.Empty;
            for(int i=0;i<shell.Pins.Count;i++)
            {
                var pin=shell.Pins[i];AppBubble view;
                if(!views.TryGetValue(pin.Target,out view))continue;
                var pos=desktop?new Point(area.X+(i/rows)*CellW,area.Y+(i%rows)*CellH)
                    :new Point(area.X+(i%cols)*CellW+scroll.X,area.Y+(i/cols)*CellH+scroll.Y);
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
                int idx=Math.Max(0,(location.Y-scroll.Y+CellH/2)/CellH)*Columns()+Math.Max(0,(location.X-scroll.X+CellW/2)/CellW);
                idx=Math.Min(shell.Pins.Count-1,idx);
                shell.Pins.Remove(b.Pin);shell.Pins.Insert(idx,b.Pin);
            }
        }));
    }
}




