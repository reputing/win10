using System;
using System.Drawing;
using System.Runtime.InteropServices;

// How a tiled window is decorated. The workspace talks to all of these the same way.
internal interface IFrame : IDisposable
{
    // True when the frame has to follow its window around (the overlay does; the others do not).
    bool Polled { get; }
    // Brings the frame up to date. Returns false once the window is gone.
    bool Sync(bool clipCorners);
    bool Busy { get; }
    // Drop-target look while another window is dragged over this one.
    bool Highlight { get; set; }
    // While true (during a glide) nothing is drawn, so moving a window costs nothing extra.
    bool Suspended { get; set; }
    void Pop();
    // Hands the window back exactly as it was found.
    void Release();
}

// Lets Windows 11 draw the frame: DWM already knows how to give any window a coloured border and round corners, for free.
// There is no overlay window, no window region and no polling, so this style costs essentially nothing.
internal sealed class NativeFrame : IFrame
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);

    private readonly DesktopWindow target;
    private readonly Func<Color> accent,tertiary;
    private int applied=int.MinValue;
    private bool rounded,highlight;

    internal NativeFrame(DesktopWindow target,Func<Color> accent,Func<Color> tertiary)
    {
        this.target=target;this.accent=accent;this.tertiary=tertiary;
    }

    // DWMWA_BORDER_COLOR and DWMWA_WINDOW_CORNER_PREFERENCE arrived with Windows 11 (build 22000).
    internal static bool Supported { get { return Environment.OSVersion.Version.Build>=22000; } }

    public bool Polled { get { return false; } }
    public bool Busy { get { return false; } }
    public bool Suspended { get; set; }
    public bool Highlight
    {
        get { return highlight; }
        set { if(highlight!=value){highlight=value;Sync(false);} }
    }

    public bool Sync(bool clipCorners)
    {
        var handle=target.Handle;
        uint pid;
        if(!Native.IsWindow(handle) || Native.GetWindowThreadProcessId(handle,out pid)!=target.Thread || pid!=target.Pid)return false;
        if(!rounded)
        {
            int round=2;   // DWMWCP_ROUND
            DwmSetWindowAttribute(handle,33,ref round,4);
            rounded=true;
        }
        Color color=highlight?tertiary():Native.GetAncestor(Native.GetForegroundWindow(),3)==handle?accent():Ui.Edge;
        int value=color.R|(color.G<<8)|(color.B<<16);
        if(value!=applied)
        {
            DwmSetWindowAttribute(handle,34,ref value,4);   // DWMWA_BORDER_COLOR
            applied=value;
        }
        return true;
    }

    public void Pop(){Sync(false);}

    public void Release()
    {
        if(!rounded && applied==int.MinValue)return;
        if(!Native.IsWindow(target.Handle))return;
        int none=unchecked((int)0xFFFFFFFF);   // DWMWA_COLOR_DEFAULT
        DwmSetWindowAttribute(target.Handle,34,ref none,4);
        int normal=0;                          // DWMWCP_DEFAULT
        DwmSetWindowAttribute(target.Handle,33,ref normal,4);
        rounded=false;applied=int.MinValue;
    }

    public void Dispose(){}
}

// No decoration at all: windows are only tiled.
internal sealed class NullFrame : IFrame
{
    private readonly DesktopWindow target;
    internal NullFrame(DesktopWindow target){this.target=target;}
    public bool Polled { get { return false; } }
    public bool Busy { get { return false; } }
    public bool Suspended { get; set; }
    public bool Highlight { get; set; }
    public bool Sync(bool clipCorners){return Native.IsWindow(target.Handle);}
    public void Pop(){}
    public void Release(){}
    public void Dispose(){}
}

// The preview shown while a window is dragged: a soft accent-coloured tile over the place it will snap into.
internal sealed class DropGhost : LayeredForm
{
    private Rectangle shown;
    private Color tint;

    internal DropGhost(){TopMost=true;Text="Bluehour snap preview";}

    internal void ShowAt(Rectangle bounds,Color accent)
    {
        if(bounds==shown && accent==tint && Visible)return;
        shown=bounds;tint=accent;
        if(!Visible)Show();
        Present(bounds,255,delegate(System.Drawing.Graphics g)
        {
            var box=new RectangleF(1.5f,1.5f,bounds.Width-3,bounds.Height-3);
            Style.Fill(g,box,18,Color.FromArgb(46,accent));
            using(var path=Style.Round(box,18))
            using(var pen=new Pen(Color.FromArgb(210,accent),3f))g.DrawPath(pen,path);
        });
        Native.SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x13);   // above the dragged window, without taking focus
    }

    internal void Clear()
    {
        shown=Rectangle.Empty;
        if(Visible)Hide();
    }
}
