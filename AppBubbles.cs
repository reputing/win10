using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

// The pinned apps on the dashboard: one control that draws every sticker and handles the mouse itself, so opening the
// dashboard does not build a window, timer, tooltip and menu per sticker. Click to open, drag to reorder, right-click to
// unpin, wheel to scroll. (The desktop draws its stickers the same way; see StickerField.)
internal sealed class PinBoard : Control
{
    private const int CellW=StickerField.CellW,CellH=StickerField.CellH;
    private readonly Shell shell;
    private readonly Font font=new Font(Style.Family,9,FontStyle.Bold);
    private int hot=-1,held=-1,scroll;
    private bool dragging;
    private Point grab,at;
    private ContextMenuStrip menu;
    private AppPin menuFor;

    internal PinBoard(Shell shell)
    {
        this.shell=shell;
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);
        Name="App board";AccessibleName="Pinned apps";AccessibleRole=AccessibleRole.List;
        BackColor=Ui.Surface;
        shell.PinsChanged+=Changed;
    }

    private int Columns(){return Math.Max(1,(Width-18)/CellW);}
    private int Rows(){return (shell.Pins.Count+Columns()-1)/Columns();}
    private int Extent(){return 12+Rows()*CellH;}
    private int MaxScroll(){return Math.Max(0,Extent()-Height);}
    private Rectangle Cell(int index)
    {
        int columns=Columns();
        return new Rectangle(6+(index%columns)*CellW,6+(index/columns)*CellH-scroll,CellW-6,CellH-6);
    }
    private int IndexAt(Point point)
    {
        for(int i=0;i<shell.Pins.Count;i++)if(Cell(i).Contains(point))return i;
        return -1;
    }
    // The place a dragged sticker would be dropped into.
    private int TargetIndex(Point point)
    {
        int columns=Columns();
        int row=Math.Max(0,(point.Y+scroll-6+CellH/2)/CellH),column=Math.Max(0,Math.Min(columns-1,(point.X-6+CellW/2)/CellW));
        return Math.Max(0,Math.Min(shell.Pins.Count-1,row*columns+column));
    }

    private void Changed()
    {
        if(IsDisposed)return;
        hot=held=-1;dragging=false;
        scroll=Math.Max(0,Math.Min(MaxScroll(),scroll));
        Invalidate();
    }

    // ---- Mouse --------------------------------------------------------------------------------------------------

    internal void Press(MouseButtons button,Point point)
    {
        int index=IndexAt(point);
        if(button==MouseButtons.Right){if(index>=0)ShowMenu(shell.Pins[index],point);return;}
        if(button!=MouseButtons.Left || index<0)return;
        held=index;grab=point;at=point;Capture=true;Invalidate();
    }

    internal void Hover(Point point)
    {
        if(held>=0)
        {
            if(!dragging && Math.Abs(point.X-grab.X)<SystemInformation.DragSize.Width/2 && Math.Abs(point.Y-grab.Y)<SystemInformation.DragSize.Height/2)return;
            dragging=true;at=point;Cursor=Cursors.SizeAll;Invalidate();
            return;
        }
        int over=IndexAt(point);
        if(over==hot)return;
        hot=over;Cursor=over>=0?Cursors.Hand:Cursors.Default;
        Invalidate();
    }

    internal void Release(MouseButtons button,Point point)
    {
        if(button!=MouseButtons.Left || held<0)return;
        int index=held;bool moved=dragging;
        held=-1;dragging=false;Capture=false;Cursor=Cursors.Default;
        if(index>=shell.Pins.Count){Invalidate();return;}
        var pin=shell.Pins[index];
        if(moved)
        {
            int target=TargetIndex(point);
            shell.Run(()=>shell.EditPins(()=>{shell.Pins.Remove(pin);shell.Pins.Insert(Math.Min(target,shell.Pins.Count),pin);}));
        }
        else if(Cell(index).Contains(point))shell.Run(()=>shell.OpenApp(pin));
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);Hover(e.Location);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Press(e.Button,e.Location);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);Release(e.Button,e.Location);}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(held<0 && hot>=0){hot=-1;Invalidate();}}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture && held>=0){held=-1;dragging=false;Invalidate();}}
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int goal=Math.Max(0,Math.Min(MaxScroll(),scroll-Math.Sign(e.Delta)*CellH/2));
        if(goal!=scroll){scroll=goal;Invalidate();}
    }

    private void ShowMenu(AppPin pin,Point point)
    {
        if(menu==null)
        {
            menu=Ui.Menu();
            menu.Items.Add("open",null,delegate{if(menuFor!=null){var app=menuFor;shell.Run(()=>shell.OpenApp(app));}});
            menu.Items.Add("unpin",null,delegate{if(menuFor!=null){var app=menuFor;shell.Run(()=>shell.Unpin(app));}});
        }
        menuFor=pin;
        menu.Show(this,point);
    }

    // For the self-test: drags a sticker by an offset through the same code the mouse uses.
    internal bool Drag(string target,Point by)
    {
        int index=shell.Pins.FindIndex(p=>p.Target==target);
        if(index<0)return false;
        var cell=Cell(index);
        var start=new Point(cell.X+cell.Width/2,cell.Y+cell.Height/2);
        Press(MouseButtons.Left,start);
        Hover(new Point(start.X+by.X/2,start.Y+by.Y/2));
        Hover(new Point(start.X+by.X,start.Y+by.Y));
        Release(MouseButtons.Left,new Point(start.X+by.X,start.Y+by.Y));
        return true;
    }

    // ---- Drawing ------------------------------------------------------------------------------------------------

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g=e.Graphics;
        g.Clear(Parent==null?Ui.Night:Parent.BackColor);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        Style.Fill(g,new RectangleF(0,0,Width,Height),26,Ui.Surface);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        var accent=shell.Accent;
        var saved=g.Save();
        using(var shape=Style.Round(new RectangleF(0,0,Width,Height),26))g.SetClip(shape,CombineMode.Intersect);
        for(int i=0;i<shell.Pins.Count;i++)
        {
            if(dragging && i==held)continue;
            var cell=Cell(i);
            if(!cell.IntersectsWith(e.ClipRectangle))continue;
            var pin=shell.Pins[i];
            StickerArt.Draw(g,cell,pin,AppLibrary.Cached(pin),font,accent,i==hot,i==hot&&held<0?1f:0f,i==held?1f:0f);
        }
        if(dragging && held>=0 && held<shell.Pins.Count)
        {
            // Where it would land, then the sticker itself under the pointer.
            int target=TargetIndex(at);
            var spot=Cell(target);
            using(var path=Style.Round(new RectangleF(spot.X+(spot.Width-64)/2f,spot.Y+6,64,64),22))
            using(var pen=new Pen(Color.FromArgb(150,shell.Highlight),2.5f){DashStyle=DashStyle.Dash})g.DrawPath(pen,path);
            var pin=shell.Pins[held];
            StickerArt.Draw(g,new Rectangle(at.X-(CellW-6)/2,at.Y-(CellH-6)/2,CellW-6,CellH-6),pin,AppLibrary.Cached(pin),font,accent,true,1f,0f);
        }
        g.Restore(saved);
        // A slim scroll bar when there is more below.
        int extent=Extent();
        if(extent>Height)
        {
            float track=Height-24,thumb=Math.Max(28,track*Height/extent),top=12+(track-thumb)*scroll/Math.Max(1,extent-Height);
            Style.Fill(g,new RectangleF(Width-9,top,4,thumb),2,Color.FromArgb(120,Ui.Muted));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing){shell.PinsChanged-=Changed;font.Dispose();if(menu!=null)menu.Dispose();}
        base.Dispose(disposing);
    }
}
