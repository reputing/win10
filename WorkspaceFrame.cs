using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// The "Sticker" frame: kuro.rip's window look (plum edge, hard offset shadow, accent glow when focused) drawn by an
// overlay window directly beneath the app, plus a corner cut on the app itself with SetWindowRgn.
//
// It is the heaviest frame style, so it is built to cost as little as it can:
//  * only the thin bands around the window are painted (what the app covers is never drawn);
//  * moving the window moves the overlay with SetWindowPos; it is only repainted when its size or colour changes;
//  * it is invisible while its window glides, and fades in afterwards with a bitmap-free opacity update;
//  * all overlays share one drawing buffer.
// Windows 11 users get the "Native" frame by default, which costs nothing at all (see Frames.cs).
internal sealed class WorkspaceFrame : LayeredForm, IFrame
{
    internal const int Ring=3,Radius=20,Reach=22;
    private const int Overlap=1;

    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateEllipticRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr destination,IntPtr first,IntPtr second,int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);

    internal readonly DesktopWindow Target;
    private readonly Func<Color> accent,tertiary;
    private readonly Stopwatch appear=new Stopwatch();
    private Rectangle placed,clip;
    private int clipRadius;
    private bool clipped,highlight,dirty,suspended;
    private Color current,goal;
    private float glow;
    private readonly Stopwatch settled=Stopwatch.StartNew(),applying=new Stopwatch();
    private Size seenSize;
    private int applies;
    private bool gaveUp;
    private double scale=1;

    internal WorkspaceFrame(DesktopWindow target,Func<Color> accent,Func<Color> tertiary)
    {
        Target=target;this.accent=accent;this.tertiary=tertiary;
        Text="Bluehour window frame";
        TopMost=false;
        CreateHandle();
    }

    public bool Polled { get { return true; } }

    public bool Highlight
    {
        get { return highlight; }
        set { if(highlight!=value){highlight=value;dirty=true;} }
    }

    public bool Suspended
    {
        get { return suspended; }
        set { if(suspended!=value){suspended=value;if(value)Conceal();else dirty=true;} }
    }

    // True while the fade-in runs, so the workspace keeps its fast timer going for it.
    public bool Busy { get { return appear.IsRunning; } }

    // The frame fades in over .3s as the window settles into its tile.
    public void Pop()
    {
        if(Motion.Enabled)appear.Restart();
        dirty=true;
    }

    private bool Foreground
    {
        get { return Native.GetAncestor(Native.GetForegroundWindow(),3)==Target.Handle; }
    }

    private Color Goal()
    {
        if(highlight)return tertiary();
        if(Foreground)return accent();
        return Ui.Edge;   // --kw-edge
    }

    // Focus changes the colour at once: one cheap repaint beats animating a large overlay.
    private void Recolor()
    {
        var wanted=Goal();
        if(wanted==goal)return;
        goal=current=wanted;
        glow=wanted==Ui.Edge?0:1;
        dirty=true;
    }

    private int Alpha()
    {
        if(!appear.IsRunning)return 255;
        double t=Motion.Progress(appear.Elapsed.TotalMilliseconds,Motion.PopIn);
        if(t>=1){appear.Reset();return 255;}
        return (int)Math.Round(255*Motion.Decel.At(t));
    }

    // Follows the app. Returns false once the window is gone. Window events call this with clipCorners false: they
    // only need the ring to keep up, and changing the app's window region from inside an event would let an app that
    // answers every change with a change of its own set off a loop.
    public bool Sync(bool clipCorners)
    {
        var handle=Target.Handle;
        uint pid;
        if(!Native.IsWindow(handle) || Native.GetWindowThreadProcessId(handle,out pid)!=Target.Thread || pid!=Target.Pid)return false;
        if(suspended){Conceal();return true;}
        Diag.Mark("sticker frame sync");
        Rectangle window,visible;
        int cloaked;
        if(!Native.IsWindowVisible(handle) || Native.IsIconic(handle) || Native.IsZoomed(handle) ||
           (Native.DwmGetWindowAttribute(handle,14,out cloaked,4)==0 && cloaked!=0) ||
           !Native.Bounds(handle,out window,out visible) || visible.Contains(Screen.FromHandle(handle).Bounds))
        {
            // Minimized, maximized, full screen or hidden: show the app as it is.
            Conceal();
            return true;
        }
        scale=Native.Scale(handle);
        int ring=(int)Math.Round(Ring*scale),radius=(int)Math.Round(Radius*scale),margin=(int)Math.Round(Reach*scale);
        if(clipCorners)Clip(window,visible,Math.Max(2,radius-ring));
        Recolor();
        var bounds=Rectangle.Inflate(visible,ring+margin,ring+margin);
        if(bounds.Size!=placed.Size || dirty || !Visible)
        {
            placed=bounds;dirty=false;
            Present(bounds,Alpha(),delegate(Graphics g){Draw(g,bounds.Size,margin,ring,radius);});
        }
        else if(bounds.Location!=placed.Location)
        {
            // Only moved: nothing needs repainting.
            placed=bounds;
            Native.SetWindowPos(Handle,IntPtr.Zero,bounds.X,bounds.Y,0,0,0x15);
        }
        if(appear.IsRunning)SetOpacity(Alpha());
        if(!Visible)Show();
        // Directly beneath the app, including when another app covers both.
        if(Native.GetWindow(handle,2)!=Handle)Native.SetWindowPos(Handle,handle,0,0,0,0,0x13);
        return true;
    }

    private void Draw(Graphics g,Size size,int margin,int ring,int radius)
    {
        var tile=new RectangleF(margin,margin,size.Width-2*margin,size.Height-2*margin);
        float drop=(float)(5*scale);
        // The app covers everything inside the tile, so only the bands around it are painted: the cost follows the
        // perimeter, not the area (a full-size window is millions of pixels, its edge is a few thousand).
        for(int grow=margin-6;grow>=2;grow-=3)
        {
            var shade=RectangleF.Inflate(tile,grow,grow);shade.Y+=drop*2;
            Style.FillBand(g,shade,radius+grow,tile,radius,Color.FromArgb(7,6,0,16));
        }
        if(glow>.01f)
            for(int grow=(int)Math.Round(16*scale);grow>=2;grow-=4)
                Style.FillBand(g,RectangleF.Inflate(tile,grow,grow),radius+grow,tile,radius,Color.FromArgb((int)Math.Round(10*glow),accent()));
        // The hard offset shadow of .kw-win: 0 6px 0 --kw-shadow.
        var hard=tile;hard.Y+=drop;
        Style.FillBand(g,hard,radius,tile,radius,Color.FromArgb(240,Ui.EdgeDeep));
        if(highlight)
        {
            float halo=(float)(5*scale);
            Style.FillBand(g,RectangleF.Inflate(tile,halo,halo),radius+halo,tile,radius,Color.FromArgb(110,goal));
        }
        // The inner edge runs one pixel under the app so no wallpaper peeks through the app's clipped corners.
        float inset=ring+Overlap;
        Style.FillBand(g,tile,radius,RectangleF.Inflate(tile,-inset,-inset),Math.Max(1,radius-inset),current);
    }

    // ---- Rounding the app's own corners -------------------------------------------------------------------------

    // The full window rectangle minus the four corner wedges outside the visible rounded corners (window coordinates).
    private static IntPtr CornerRegion(Size window,Rectangle shown,int radius)
    {
        int r=Math.Max(1,Math.Min(radius,Math.Min(shown.Width,shown.Height)/2));
        IntPtr region=CreateRectRgn(0,0,window.Width,window.Height);
        int[][] corners=
        {
            new[]{shown.Left,shown.Top,1,1},   new[]{shown.Right,shown.Top,-1,1},
            new[]{shown.Left,shown.Bottom,1,-1}, new[]{shown.Right,shown.Bottom,-1,-1}
        };
        foreach(var c in corners)
        {
            int x=c[0],y=c[1],dx=c[2],dy=c[3];
            // The square at the corner, minus the circle whose centre sits r inside both edges, is the wedge to remove.
            IntPtr wedge=CreateRectRgn(Math.Min(x,x+dx*r),Math.Min(y,y+dy*r),Math.Max(x,x+dx*r),Math.Max(y,y+dy*r));
            int cx=x+dx*r,cy=y+dy*r;
            IntPtr disc=CreateEllipticRgn(cx-r,cy-r,cx+r,cy+r);
            CombineRgn(wedge,wedge,disc,4);
            CombineRgn(region,region,wedge,4);
            DeleteObject(disc);
            DeleteObject(wedge);
        }
        return region;
    }

    private void Clip(Rectangle window,Rectangle visible,int radius)
    {
        // SetWindowRgn waits for the app to answer, so never ask an app that Windows already considers hung.
        if(gaveUp || Native.IsHungAppWindow(Target.Handle))return;
        if(window.Size!=seenSize){seenSize=window.Size;settled.Restart();}
        var shown=new Rectangle(visible.X-window.X,visible.Y-window.Y,visible.Width,visible.Height);
        Native.Rect box;
        // A window region of 0 means "none": some apps reset theirs when they resize or change state.
        int kind=Native.GetWindowRgnBox(Target.Handle,out box);
        if(clipped && kind!=0 && clip==shown && clipRadius==radius && box.Rectangle.Size==window.Size)return;
        // While the window is changing size, a region cut for the old size would clip its content: drop it and wait
        // until the size has been steady for a moment before rounding the corners again.
        if(settled.ElapsedMilliseconds<120)
        {
            if(clipped && box.Rectangle.Size!=window.Size)Unclip();
            return;
        }
        // Some apps (Chromium among them) put their own region back whenever they notice a change. If one keeps
        // doing that, stop competing for it instead of looping.
        if(!applying.IsRunning || applying.ElapsedMilliseconds>3000){applying.Restart();applies=0;}
        if(++applies>6)
        {
            gaveUp=true;
            Unclip();
            Diag.Log("Stopped rounding the corners of a "+Target.ClassName+" window: it keeps resetting its window region.");
            return;
        }
        Diag.Mark("rounding corners");
        IntPtr region=CornerRegion(window.Size,shown,radius);
        if(Native.SetWindowRgn(Target.Handle,region,true)==0){DeleteObject(region);return;}
        clip=shown;clipRadius=radius;clipped=true;
    }

    private void Unclip()
    {
        if(!clipped)return;
        clipped=false;
        clip=Rectangle.Empty;
        if(Native.IsWindow(Target.Handle) && !Native.IsHungAppWindow(Target.Handle))Native.SetWindowRgn(Target.Handle,IntPtr.Zero,true);
    }

    private void Conceal()
    {
        if(Visible)Hide();
        Unclip();
        placed=Rectangle.Empty;
    }

    // Hands the window back exactly as Bluehour found it.
    public void Release()
    {
        Unclip();
        if(Visible)Hide();
    }
}
