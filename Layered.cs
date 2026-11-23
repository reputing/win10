using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// A 32-bit top-down DIB that GDI+ draws into and UpdateLayeredWindow presents. It only grows, so animating a
// window does not allocate a bitmap per frame.
internal sealed class Canvas : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Header
    {
        public int Size,Width,Height;
        public short Planes,BitCount;
        public int Compression,ImageSize,XPels,YPels,Used,Important;
    }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc,ref Header info,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

    private IntPtr dc,bitmap,stock,bits;
    private int capacityWidth,capacityHeight;
    internal IntPtr Dc { get { return dc; } }

    // Returns a bitmap view of the top-left width x height pixels. Dispose it before calling Begin again.
    internal Bitmap Begin(int width,int height)
    {
        if(dc==IntPtr.Zero)dc=CreateCompatibleDC(IntPtr.Zero);
        if(width>capacityWidth || height>capacityHeight)
            Allocate(Round(Math.Max(width,capacityWidth)),Round(Math.Max(height,capacityHeight)));
        return new Bitmap(width,height,capacityWidth*4,PixelFormat.Format32bppPArgb,bits);
    }

    private static int Round(int value){return (value+255)/256*256;}

    private void Allocate(int width,int height)
    {
        var info=new Header {Size=Marshal.SizeOf(typeof(Header)),Width=width,Height=-height,Planes=1,BitCount=32};
        IntPtr pixels;
        IntPtr created=CreateDIBSection(dc,ref info,0,out pixels,IntPtr.Zero,0);
        if(created==IntPtr.Zero)throw new OutOfMemoryException("Windows could not allocate a frame buffer.");
        IntPtr replaced=SelectObject(dc,created);
        if(stock==IntPtr.Zero)stock=replaced;
        if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);
        bitmap=created;bits=pixels;capacityWidth=width;capacityHeight=height;
    }

    public void Dispose()
    {
        if(dc!=IntPtr.Zero)
        {
            if(stock!=IntPtr.Zero)SelectObject(dc,stock);
            DeleteDC(dc);
            dc=IntPtr.Zero;
        }
        if(bitmap!=IntPtr.Zero){DeleteObject(bitmap);bitmap=IntPtr.Zero;}
        stock=IntPtr.Zero;bits=IntPtr.Zero;capacityWidth=capacityHeight=0;
    }
}

// A borderless, click-through, never-activating window drawn with per-pixel alpha. Window rings, shadows and
// toasts use it so their rounded edges are anti-aliased like the site's, instead of the jagged edge of a Region.
internal abstract class LayeredForm : Form
{
    [StructLayout(LayoutKind.Sequential,Pack=1)]
    private struct Blend { public byte Operation,Flags,Alpha,Format; }
    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr destination,ref Point position,ref Size size,
        IntPtr source,ref Point origin,int key,ref Blend blend,int flags);

    // One drawing buffer for every overlay window (they are drawn one at a time), sized to the largest of them. Each
    // window used to keep its own, which for a dozen full-size frames was tens of megabytes.
    private static readonly Canvas canvas=new Canvas();
    internal static void ReleaseCanvas(){canvas.Dispose();}

    protected LayeredForm()
    {
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None;
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    // Frames and toasts let every click through to what is beneath; the dock overrides this to receive them.
    protected virtual bool PassThrough { get { return true; } }
    protected override CreateParams CreateParams
    {
        // layered | no-activate | tool window, and transparent to the mouse unless a subclass wants clicks
        get { var cp=base.CreateParams; cp.ExStyle|=0x80000|0x08000000|0x80|(PassThrough?0x20:0); return cp; }
    }

    // Draws into a fresh transparent bitmap and puts it on screen at `bounds` with a constant opacity (0-255).
    protected bool Present(Rectangle bounds,int opacity,Action<Graphics> paint)
    {
        if(bounds.Width<=0 || bounds.Height<=0 || !IsHandleCreated)return false;
        using(var bitmap=canvas.Begin(bounds.Width,bounds.Height))
        {
            using(var g=Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode=SmoothingMode.AntiAlias;
                g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAlias;
                paint(g);
            }
        }
        var position=bounds.Location;var size=bounds.Size;var origin=Point.Empty;
        var blend=new Blend {Operation=0,Flags=0,Alpha=(byte)Math.Max(0,Math.Min(255,opacity)),Format=1};
        return UpdateLayeredWindow(Handle,IntPtr.Zero,ref position,ref size,canvas.Dc,ref origin,0,ref blend,2);
    }

    [DllImport("user32.dll",EntryPoint="UpdateLayeredWindow")]
    private static extern bool UpdateOpacity(IntPtr hwnd,IntPtr destination,IntPtr position,IntPtr size,IntPtr source,IntPtr origin,
        int key,ref Blend blend,int flags);

    // Changes only the opacity: no bitmap is drawn or copied, so fades are nearly free.
    protected bool SetOpacity(int opacity)
    {
        if(!IsHandleCreated)return false;
        var blend=new Blend {Operation=0,Flags=0,Alpha=(byte)Math.Max(0,Math.Min(255,opacity)),Format=1};
        return UpdateOpacity(Handle,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,0,ref blend,2);
    }
}
