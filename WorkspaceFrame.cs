using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Gives a real Windows app the frame kuro.rip gives its windows (.kw-win): rounded corners, a plum edge, and a hard
// offset shadow underneath that makes it look like a sticker. The edge turns accent-coloured and glows when focused.
//
// Two pieces work together:
//  * this click-through, per-pixel-alpha form sits directly beneath the app in z-order and draws the ring and shadow;
//  * the app window itself gets its four visible corners cut to quarter circles with SetWindowRgn, so its square
//    corners do not poke out of the ring. The invisible resize border Windows 10 adds is left in the region, so
//    edges and corners can still be grabbed.
// The app keeps its own title bar: Chrome, Discord and UWP apps draw their own, and a second bar would double up.
internal sealed class WorkspaceFrame : LayeredForm
{
    internal const int Ring=3,Radius=20,Reach=22;
    private const int Overlap=1;

    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateEllipticRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr destination,IntPtr first,IntPtr second,int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);

    internal readonly DesktopWindow Target;
    private readonly Func<Color> accent,tertiary;
    private readonly Stopwatch appear=new Stopwatch(),recolor=new Stopwatch();
    private Rectangle placed,clip;
    private int clipRadius;
    private bool clipped,highlight,ready,dirty;
    private Color from,current,goal;
    private float glow,glowFrom,glowGoal;
    private double scale=1;

    internal WorkspaceFrame(DesktopWindow target,Func<Color> accent,Func<Color> tertiary)
    {
        Target=target;this.accent=accent;this.tertiary=tertiary;
        Text="Bluehour window frame";
        TopMost=false;
        CreateHandle();
    }

    // Ivy's drop-target look (.cl-win-target): tertiary border with a glow.
    internal bool Highlight
    {
        get { return highlight; }
        set { if(highlight!=value){highlight=value;dirty=true;} }
    }

    // True while something on the frame is still animating, so the workspace keeps its fast timer running.
    internal bool Busy { get { return appear.IsRunning || recolor.IsRunning; } }

    // cl-pop-in: the frame fades in over .3s as the window settles into its tile.
    internal void Pop()
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

    private void Recolor()
    {
        var wanted=Goal();
        if(wanted!=goal)
        {
            from=current;goal=wanted;dirty=true;
            glowFrom=glow;glowGoal=wanted==Ui.Edge?0:1;
            if(ready && Motion.Enabled)recolor.Restart();
            else{current=goal;glow=glowGoal;recolor.Reset();}
        }
        if(!recolor.IsRunning)return;
        double t=Motion.Progress(recolor.Elapsed.TotalMilliseconds,Motion.Recolor);
        double eased=Motion.Standard.At(t);
        current=Motion.Lerp(from,goal,eased);
        glow=(float)(glowFrom+(glowGoal-glowFrom)*eased);
        if(t>=1){recolor.Reset();current=goal;glow=glowGoal;}
        dirty=true;
    }

    private int Alpha()
    {
        if(!appear.IsRunning)return 255;
        double t=Motion.Progress(appear.Elapsed.TotalMilliseconds,Motion.PopIn);
        if(t>=1){appear.Reset();return 255;}
        return (int)Math.Round(255*Motion.Decel.At(t));
    }

    // Follows the app. Returns false once the window is gone.
    internal bool Sync()
    {
        var handle=Target.Handle;
        uint pid;
        if(!Native.IsWindow(handle) || Native.GetWindowThreadProcessId(handle,out pid)!=Target.Thread || pid!=Target.Pid)return false;
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
        Clip(window,visible,Math.Max(2,radius-ring));
        Recolor();
        var bounds=Rectangle.Inflate(visible,ring+margin,ring+margin);
        if(bounds!=placed || dirty || !Visible)
        {
            placed=bounds;dirty=false;
            Present(bounds,Alpha(),delegate(Graphics g){Draw(g,bounds.Size,margin,ring,radius);});
            ready=true;
        }
        if(!Visible)Show();
        // Directly beneath the app, including when another app covers both.
        if(Native.GetWindow(handle,2)!=Handle)Native.SetWindowPos(Handle,handle,0,0,0,0,0x13);
        return true;
    }

    private void Draw(Graphics g,Size size,int margin,int ring,int radius)
    {
        var tile=new RectangleF(margin,margin,size.Width-2*margin,size.Height-2*margin);
        float drop=(float)(5*scale);
        // Soft ambient shadow, violet-black.
        for(int grow=margin-6;grow>=2;grow-=3)
        {
            var shade=new RectangleF(tile.X-grow,tile.Y-grow+drop*2,tile.Width+2*grow,tile.Height+2*grow);
            Style.Fill(g,shade,radius+grow,Color.FromArgb(7,6,0,16));
        }
        // Focus glow (kuro's drag glow: 0 0 30px accent at a quarter strength).
        if(glow>.01f)
            for(int grow=(int)Math.Round(16*scale);grow>=2;grow-=2)
            {
                var halo=RectangleF.Inflate(tile,grow,grow);
                Style.Fill(g,halo,radius+grow,Color.FromArgb((int)Math.Round(6*glow),accent()));
            }
        // The hard offset shadow of .kw-win: 0 6px 0 --kw-shadow.
        Style.Fill(g,new RectangleF(tile.X,tile.Y+drop,tile.Width,tile.Height),radius,Color.FromArgb(240,Ui.EdgeDeep));
        if(highlight)
        {
            float halo=(float)(5*scale);
            using(var path=new GraphicsPath(FillMode.Alternate))
            {
                using(var outer=Style.Round(RectangleF.Inflate(tile,halo,halo),radius+halo))path.AddPath(outer,false);
                using(var inner=Style.Round(tile,radius))path.AddPath(inner,false);
                using(var brush=new SolidBrush(Color.FromArgb(110,goal)))g.FillPath(brush,path);
            }
        }
        // The inner edge runs one pixel under the app so no wallpaper peeks through the app's clipped corners.
        float inset=ring+Overlap;
        using(var band=new GraphicsPath(FillMode.Alternate))
        {
            using(var outer=Style.Round(tile,radius))band.AddPath(outer,false);
            using(var inner=Style.Round(RectangleF.Inflate(tile,-inset,-inset),Math.Max(1,radius-inset)))band.AddPath(inner,false);
            using(var brush=new SolidBrush(current))g.FillPath(brush,band);
        }
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
        var shown=new Rectangle(visible.X-window.X,visible.Y-window.Y,visible.Width,visible.Height);
        Native.Rect box;
        // A window region of 0 means "none": some apps reset theirs when they resize or change state.
        int kind=Native.GetWindowRgnBox(Target.Handle,out box);
        if(clipped && kind!=0 && clip==shown && clipRadius==radius && box.Rectangle.Size==window.Size)return;
        IntPtr region=CornerRegion(window.Size,shown,radius);
        if(Native.SetWindowRgn(Target.Handle,region,true)==0){DeleteObject(region);return;}
        clip=shown;clipRadius=radius;clipped=true;
    }

    private void Unclip()
    {
        if(!clipped)return;
        clipped=false;
        clip=Rectangle.Empty;
        if(Native.IsWindow(Target.Handle))Native.SetWindowRgn(Target.Handle,IntPtr.Zero,true);
    }

    private void Conceal()
    {
        if(Visible)Hide();
        Unclip();
        placed=Rectangle.Empty;
    }

    // Hands the window back exactly as Bluehour found it.
    internal void Release()
    {
        Unclip();
        if(Visible)Hide();
    }
}
