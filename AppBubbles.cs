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
        ContextMenuStrip.Items.Add("unpin",null,delegate{shell.Run(()=>shell.Unpin(Pin));});
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
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var surface=Parent as DesktopSurface;
        if(surface!=null)surface.PaintBackdropFor(this,e.Graphics);
        else base.OnPaintBackground(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        StickerArt.Draw(e.Graphics,ClientRectangle,Pin,icon,Font,shell.Accent,Focused||hover,emphasis,sink);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){animation.Dispose();hint.Dispose();if(icon!=null)icon.Dispose();if(ContextMenuStrip!=null)ContextMenuStrip.Dispose();}
        base.Dispose(disposing);
    }
}

// The pinned apps on the dashboard: a scrolling board of stickers in a grid. (The desktop draws its stickers itself; see
// StickerField.)
internal sealed class BubbleLayer
{
    private readonly Shell shell;
    private readonly Panel host;
    private readonly Dictionary<string,AppBubble> views=new Dictionary<string,AppBubble>(StringComparer.OrdinalIgnoreCase);
    private bool updating,ready;
    internal BubbleLayer(Shell shell,Panel host)
    {
        this.shell=shell;this.host=host;
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
    internal const int CellW=StickerField.CellW,CellH=StickerField.CellH;
    private int Columns(){return Math.Max(1,(host.ClientSize.Width-18)/CellW);}
    private void Position(bool animate)
    {
        if(updating||host.IsDisposed)return;
        updating=true;
        try
        {
            int cols=Columns();
            host.AutoScrollMinSize=new Size(0,12+CellH*((shell.Pins.Count+cols-1)/cols));
            var scroll=host.AutoScrollPosition;
            for(int i=0;i<shell.Pins.Count;i++)
            {
                AppBubble view;
                if(!views.TryGetValue(shell.Pins[i].Target,out view))continue;
                view.SetPosition(new Point(6+(i%cols)*CellW+scroll.X,6+(i/cols)*CellH+scroll.Y),animate&&host.Visible);
            }
        }
        finally{updating=false;}
    }
    private void Drop(AppBubble b,Point location)
    {
        shell.Run(()=>shell.EditPins(()=>{
            var scroll=host.AutoScrollPosition;
            int idx=Math.Max(0,(location.Y-scroll.Y+CellH/2)/CellH)*Columns()+Math.Max(0,(location.X-scroll.X+CellW/2)/CellW);
            idx=Math.Min(shell.Pins.Count-1,idx);
            shell.Pins.Remove(b.Pin);shell.Pins.Insert(idx,b.Pin);
        }));
    }
}
