using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using Timer=System.Windows.Forms.Timer;

// How one sticker looks. The dashboard board and the desktop both draw it, so they cannot drift apart.
internal static class StickerArt
{
    // A sticker: the hard shadow stays where it is; hover lifts the tile away from it, press sinks it onto it.
    internal static void Draw(Graphics g,Rectangle bounds,AppPin pin,Bitmap icon,Font font,Color accent,bool lit,float emphasis,float sink)
    {
        g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAlias;
        const int tile=64;
        float x=bounds.X+(bounds.Width-tile)/2f,y=bounds.Y+6;
        float grow=1+emphasis*.07f-sink*.05f,dy=-emphasis*4f+sink*3.4f;
        Style.Fill(g,new RectangleF(x,y+5,tile,tile),22,Color.FromArgb(240,Ui.EdgeDeep));
        float size=tile*grow;
        var body=new RectangleF(x+(tile-size)/2,y+dy+(tile-size)/2,size,size);
        using(var path=Style.Round(body,22))
        {
            using(var fill=new LinearGradientBrush(body,Ui.SurfaceHigh,Ui.Surface,90f))g.FillPath(fill,path);
            using(var pen=new Pen(lit?accent:Ui.Edge,2.5f))g.DrawPath(pen,path);
        }
        using(var sheen=new Pen(Color.FromArgb(30,255,255,255),1f))
        using(var inner=Style.Round(RectangleF.Inflate(body,-4.5f,-4.5f),18))g.DrawPath(sheen,inner);
        var symbol=new RectangleF(body.X+(body.Width-30)/2,body.Y+(body.Height-30)/2,30,30);
        if(icon!=null){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(icon,symbol);}
        else if(pin.Target=="builtin:web")
        {
            using(var pen=new Pen(accent,2.1f))
            {
                g.DrawEllipse(pen,symbol);g.DrawEllipse(pen,symbol.X+7,symbol.Y,16,30);
                g.DrawLine(pen,symbol.Left,symbol.Top+15,symbol.Right,symbol.Top+15);
            }
        }
        else if(pin.Name=="Calculator"||pin.Name=="Notepad"||pin.Name=="Paint")Style.Icon(g,pin.Name,symbol,accent);
        else if(pin.Target.StartsWith("builtin:"))Style.Icon(g,pin.Target=="builtin:files"?"Files":"Discord",symbol,accent);
        else TextRenderer.DrawText(g,pin.Name.Substring(0,1).ToLowerInvariant(),font,Rectangle.Round(symbol),accent,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        string name=Ui.Voice(pin.Name);
        int width=Math.Min(bounds.Width-2,TextRenderer.MeasureText(name,font).Width+16);
        var caption=new Rectangle(bounds.X+(bounds.Width-width)/2,(int)y+tile+15,width,22);
        Style.Fill(g,caption,11,Color.FromArgb(222,Ui.Night));
        TextRenderer.DrawText(g,name,font,caption,Ui.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
    }
}

// All the stickers on the desktop, drawn and handled as one layer of the desktop surface instead of one window each.
// That makes them cheap (no window, timer, tooltip or menu per sticker), lets them overlap correctly while one is
// dragged, and lets them snap to a grid: dropping a sticker on another swaps the two.
internal sealed class StickerField : IDisposable
{
    internal const int CellW=98,CellH=118;

    private sealed class Item
    {
        internal AppPin Pin;
        internal Bitmap Icon;
        internal Rectangle Bounds;
        internal Point From,To;
        internal float Hover,Sink;
    }

    private readonly Shell shell;
    private readonly DesktopSurface host;
    private readonly List<Item> items=new List<Item>();
    private readonly Timer clock=new Timer{Interval=16};
    private readonly Stopwatch slide=new Stopwatch();
    private readonly Font font=new Font(Style.Family,9,FontStyle.Bold);
    private readonly ContextMenuStrip menu=Ui.Menu();
    private Item hot,held,dragging,menuFor;
    private Point grab,origin,dropCell=new Point(-1,-1);
    private bool ready,shown=true;

    internal StickerField(Shell shell,DesktopSurface host)
    {
        this.shell=shell;this.host=host;
        menu.Items.Add("open",null,delegate{if(menuFor!=null)Launch(menuFor);});
        menu.Items.Add("unpin",null,delegate{if(menuFor!=null){var pin=menuFor.Pin;shell.Run(()=>shell.Unpin(pin));}});
        clock.Tick+=delegate{Step();};
        shell.PinsChanged+=Sync;
        Sync();
    }

    internal ContextMenuStrip Menu{get{return menu;}}
    internal bool Shown
    {
        get{return shown;}
        set{if(shown==value)return;shown=value;hot=held=dragging=null;host.Invalidate(Area(true));}
    }

    // ---- Grid ---------------------------------------------------------------------------------------------------

    private Rectangle Area(bool whole=false)
    {
        var work=host.Work;
        if(whole)return work;
        int left=work.Left+Math.Max(18,Math.Min(34,work.Width/56));
        int top=work.Top+(int)Math.Round(DesktopSurface.TopBar*Native.Scale(host.IsHandleCreated?host.Handle:IntPtr.Zero))+10;
        return new Rectangle(left,top,Math.Max(1,work.Right-left*2-CellW),Math.Max(1,work.Bottom-top-28-CellH));
    }
    private int Columns(){return Math.Max(1,Area().Width/CellW+1);}
    private int Rows(){return Math.Max(1,Area().Height/CellH+1);}
    private static long Key(int column,int row){return ((long)column<<32)|(uint)row;}
    private Point Origin(Point cell){var area=Area();return new Point(area.X+cell.X*CellW,area.Y+cell.Y*CellH);}

    // The cell of every sticker: ones that were placed keep theirs, the rest fill the free cells down the columns.
    private Point[] Cells()
    {
        var area=Area();int columns=Columns(),rows=Rows();
        var result=new Point[items.Count];
        var used=new HashSet<long>();
        for(int i=0;i<items.Count;i++)
        {
            var pin=items[i].Pin;
            int column=pin.Col,row=pin.Row;
            if(column<0 && pin.X>=0 && pin.Y>=0)   // a free position saved by an earlier build
            {
                column=(int)Math.Round(area.Width*pin.X/CellW);
                row=(int)Math.Round(area.Height*pin.Y/CellH);
            }
            if(column>=0 && row>=0 && column<columns && row<rows && used.Add(Key(column,row)))result[i]=new Point(column,row);
            else result[i]=new Point(-1,-1);
        }
        int next=0;
        for(int i=0;i<items.Count;i++)
        {
            if(result[i].X>=0)continue;
            while(used.Contains(Key(next/rows,next%rows)))next++;
            result[i]=new Point(next/rows,next%rows);
            used.Add(Key(result[i].X,result[i].Y));
        }
        return result;
    }

    // ---- Keeping up with the pins -------------------------------------------------------------------------------

    private void Sync()
    {
        if(host.IsDisposed)return;
        foreach(var gone in items.Where(i=>!shell.Pins.Any(p=>p.Target==i.Pin.Target)).ToArray())
        {
            if(gone.Icon!=null)gone.Icon.Dispose();
            items.Remove(gone);
            if(hot==gone)hot=null;
        }
        var ordered=new List<Item>();
        foreach(var pin in shell.Pins)
        {
            var item=items.FirstOrDefault(i=>i.Pin.Target==pin.Target);
            if(item==null)
            {
                item=new Item{Pin=pin,Bounds=new Rectangle(0,0,CellW-6,CellH-6)};
                if(pin.Name!="Calculator"&&pin.Name!="Notepad"&&pin.Name!="Paint")item.Icon=AppLibrary.Picture(pin);
            }
            item.Pin=pin;
            ordered.Add(item);
        }
        items.Clear();items.AddRange(ordered);
        dragging=held=null;dropCell=new Point(-1,-1);
        Place(ready && host.Visible);
        ready=true;
        host.Invalidate(Area(true));
    }

    // Sends every sticker to its cell, gliding there when `animate`.
    internal void Place(bool animate)
    {
        var cells=Cells();
        bool moving=false;
        for(int i=0;i<items.Count;i++)
        {
            var item=items[i];
            item.To=Origin(cells[i]);
            if(!animate || !SystemInformation.IsMenuAnimationEnabled || !Motion.Enabled)item.Bounds.Location=item.To;
            else if(item.Bounds.Location!=item.To){item.From=item.Bounds.Location;moving=true;}
        }
        if(moving){slide.Restart();clock.Start();}
        else if(!animate)host.Invalidate(Area(true));
    }

    // ---- Painting -----------------------------------------------------------------------------------------------

    internal void Paint(Graphics g,Rectangle clip)
    {
        if(!shown)return;
        var accent=shell.Accent;
        if(dragging!=null && dropCell.X>=0)
        {
            var spot=new Rectangle(Origin(dropCell),new Size(CellW-6,CellH-6));
            if(spot.IntersectsWith(clip))
            {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                var box=new RectangleF(spot.X+(spot.Width-64)/2f,spot.Y+6,64,64);
                Style.Fill(g,box,22,Color.FromArgb(40,shell.Highlight));
                using(var path=Style.Round(box,22))
                using(var pen=new Pen(Color.FromArgb(150,shell.Highlight),2.5f){DashStyle=DashStyle.Dash})g.DrawPath(pen,path);
            }
        }
        foreach(var item in items)
            if(item!=dragging && Rectangle.Inflate(item.Bounds,6,6).IntersectsWith(clip))
                StickerArt.Draw(g,item.Bounds,item.Pin,item.Icon,font,accent,item==hot,item.Hover,item.Sink);
        if(dragging!=null)StickerArt.Draw(g,dragging.Bounds,dragging.Pin,dragging.Icon,font,accent,true,1f,0f);
    }

    private void Dirty(Item item){if(item!=null)host.Invalidate(Rectangle.Inflate(item.Bounds,8,8));}
    private void Dirty(Point cell){if(cell.X>=0)host.Invalidate(Rectangle.Inflate(new Rectangle(Origin(cell),new Size(CellW-6,CellH-6)),8,8));}

    // ---- Animation ----------------------------------------------------------------------------------------------

    private void Step()
    {
        bool busy=false;
        double t=slide.IsRunning?Math.Min(1,slide.Elapsed.TotalMilliseconds/190):1;
        double ease=1-Math.Pow(1-t,3);
        foreach(var item in items)
        {
            float goal=item==hot||item==dragging?1f:0f,hold=item==held&&item!=dragging?1f:0f;
            float h=item.Hover+(goal-item.Hover)*.3f,s=item.Sink+(hold-item.Sink)*.5f;
            if(Math.Abs(goal-h)<.015f)h=goal;
            if(Math.Abs(hold-s)<.02f)s=hold;
            bool glide=slide.IsRunning && item!=dragging && item.Bounds.Location!=item.To;
            if(h!=item.Hover || s!=item.Sink || glide)
            {
                Dirty(item);
                item.Hover=h;item.Sink=s;
                if(glide)
                    item.Bounds.Location=new Point(item.From.X+(int)Math.Round((item.To.X-item.From.X)*ease),
                        item.From.Y+(int)Math.Round((item.To.Y-item.From.Y)*ease));
                Dirty(item);
            }
            if(h!=goal || s!=hold || (glide && t<1))busy=true;
        }
        if(t>=1)slide.Reset();
        if(!busy && !slide.IsRunning)clock.Stop();
    }

    private void Wake()
    {
        if(Motion.Enabled){if(!clock.Enabled)clock.Start();}
        else
        {
            foreach(var item in items){item.Hover=item==hot||item==dragging?1:0;item.Sink=item==held&&item!=dragging?1:0;}
            host.Invalidate(Area(true));
        }
    }

    // ---- The mouse ----------------------------------------------------------------------------------------------

    private Item At(Point point)
    {
        if(!shown)return null;
        if(dragging!=null && dragging.Bounds.Contains(point))return dragging;
        for(int i=items.Count-1;i>=0;i--)
            if(items[i].Bounds.Contains(point))return items[i];
        return null;
    }

    internal void MouseMove(Point point)
    {
        if(held!=null)
        {
            var delta=new Size(point.X-grab.X,point.Y-grab.Y);
            if(dragging==null)
            {
                if(Math.Abs(delta.Width)<SystemInformation.DragSize.Width/2 && Math.Abs(delta.Height)<SystemInformation.DragSize.Height/2)return;
                dragging=held;slide.Reset();host.Cursor=Cursors.SizeAll;Wake();
            }
            Dirty(dragging);
            dragging.Bounds.Location=new Point(Math.Max(0,Math.Min(host.ClientSize.Width-dragging.Bounds.Width,origin.X+delta.Width)),
                Math.Max(0,Math.Min(host.ClientSize.Height-dragging.Bounds.Height,origin.Y+delta.Height)));
            Dirty(dragging);
            var cell=NearestCell(dragging.Bounds.Location);
            if(cell!=dropCell){Dirty(dropCell);dropCell=cell;Dirty(dropCell);}
            return;
        }
        var over=At(point);
        if(over==hot)return;
        hot=over;
        host.Cursor=over!=null?Cursors.Hand:Cursors.Default;
        Wake();
    }

    internal void MouseLeave()
    {
        if(held!=null || hot==null)return;
        hot=null;Wake();
    }

    internal void MouseDown(MouseButtons button,Point point)
    {
        var item=At(point);
        if(button==MouseButtons.Right){menuFor=item;host.ContextMenuStrip=item!=null?menu:host.DesktopMenu;return;}
        if(button!=MouseButtons.Left || item==null)return;
        slide.Reset();
        held=item;grab=point;origin=item.Bounds.Location;
        host.Capture=true;Wake();
    }

    internal void MouseUp(MouseButtons button,Point point)
    {
        if(button!=MouseButtons.Left || held==null)return;
        var item=held;bool moved=dragging!=null;
        held=null;dragging=null;host.Capture=false;
        host.Cursor=At(point)!=null?Cursors.Hand:Cursors.Default;
        Dirty(item);Dirty(dropCell);
        var target=dropCell;dropCell=new Point(-1,-1);
        if(moved)Drop(item,target);
        else if(item.Bounds.Contains(point))Launch(item);
        Wake();
    }

    // Capture was taken away mid-drag (another window grabbed the mouse): put the sticker back.
    internal void CaptureLost()
    {
        if(held==null)return;
        held=null;dragging=null;
        Dirty(dropCell);dropCell=new Point(-1,-1);
        Place(true);Wake();
    }

    private Point NearestCell(Point corner)
    {
        var area=Area();
        int column=Math.Max(0,Math.Min(Columns()-1,(int)Math.Round((corner.X-area.X)/(double)CellW)));
        int row=Math.Max(0,Math.Min(Rows()-1,(int)Math.Round((corner.Y-area.Y)/(double)CellH)));
        return new Point(column,row);
    }

    // Snaps the sticker into the nearest cell; if another sticker is there, the two trade places.
    private void Drop(Item item,Point target)
    {
        if(target.X<0)target=NearestCell(item.Bounds.Location);
        var cells=Cells();
        int own=items.IndexOf(item);
        // `Cells` was computed from the model, which still has the sticker where it began.
        var home=own>=0?cells[own]:target;
        Item other=null;
        for(int i=0;i<items.Count;i++)if(i!=own && cells[i]==target)other=items[i];
        var other0=other;
        shell.Run(()=>shell.EditPins(()=>
        {
            item.Pin.Col=target.X;item.Pin.Row=target.Y;item.Pin.X=item.Pin.Y=-1;
            if(other0!=null){other0.Pin.Col=home.X;other0.Pin.Row=home.Y;other0.Pin.X=other0.Pin.Y=-1;}
        }));
    }

    private void Launch(Item item){var pin=item.Pin;shell.Run(()=>shell.OpenApp(pin));}

    // For the self-test: a drag from one sticker to a point, through the same code a real mouse uses.
    internal bool Drag(string target,Point by)
    {
        var item=items.FirstOrDefault(i=>i.Pin.Target==target);
        if(item==null)return false;
        var start=new Point(item.Bounds.X+item.Bounds.Width/2,item.Bounds.Y+item.Bounds.Height/2);
        MouseDown(MouseButtons.Left,start);
        MouseMove(new Point(start.X+by.X/2,start.Y+by.Y/2));
        MouseMove(new Point(start.X+by.X,start.Y+by.Y));
        MouseUp(MouseButtons.Left,new Point(start.X+by.X,start.Y+by.Y));
        return true;
    }
    internal Point CellOf(string target)
    {
        var cells=Cells();
        for(int i=0;i<items.Count;i++)if(items[i].Pin.Target==target)return cells[i];
        return new Point(-1,-1);
    }

    public void Dispose()
    {
        clock.Dispose();shell.PinsChanged-=Sync;
        foreach(var item in items)if(item.Icon!=null)item.Icon.Dispose();
        items.Clear();font.Dispose();menu.Dispose();
    }
}
