using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

public static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int L, T, R, B;
        public Rect(Rectangle rect) { L=rect.Left; T=rect.Top; R=rect.Right; B=rect.Bottom; }
        [System.Xml.Serialization.XmlIgnore] public Rectangle Rectangle { get { return Rectangle.FromLTRB(L,T,R,B); } }
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Placement
    {
        public int Size, Flags, Show;
        public Point Min, Max;
        public Rect Normal;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Bar
    {
        public int Size;
        public IntPtr Handle;
        public uint Notification, Edge;
        public Rect Bounds;
        public IntPtr Parameter;
    }
    internal delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd, StringBuilder title, int size);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder title, int size);
    [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] internal static extern bool GetWindowPlacement(IntPtr hwnd, ref Placement placement);
    [DllImport("user32.dll")] internal static extern bool SetWindowPlacement(IntPtr hwnd, ref Placement placement);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindow(string className,string windowName);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr hwnd, int state);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern bool SystemParametersInfo(uint act,uint param,string value,uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wp, string lp, uint flags, uint timeout, out IntPtr result);
    [DllImport("shell32.dll")] internal static extern UIntPtr SHAppBarMessage(uint message, ref Bar bar);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int bytes);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect bounds);
    [DllImport("dwmapi.dll", EntryPoint="DwmGetWindowAttribute")] private static extern int DwmGetRect(IntPtr hwnd, int attribute, out Rect bounds, int bytes);
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] internal static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("user32.dll")] internal static extern bool IsHungAppWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern int GetWindowRgnBox(IntPtr hwnd, out Rect box);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    internal delegate void EventProc(IntPtr hook, uint eventId, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, EventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);

    // Display scale of a window (1.0 at 96 dpi). Windows before 10 version 1607 have no per-window dpi.
    internal static double Scale(IntPtr hwnd)
    {
        try { uint dpi=GetDpiForWindow(hwnd); return dpi==0?1:dpi/96.0; }
        catch (EntryPointNotFoundException) { return 1; }
    }

    // The full window rectangle, and the rectangle a person actually sees. Windows 10 adds an invisible resize
    // border (about 7px on three sides) to the first and not the second.
    internal static bool Bounds(IntPtr hwnd, out Rectangle window, out Rectangle visible)
    {
        Rect raw, frame;
        window=visible=Rectangle.Empty;
        if (!GetWindowRect(hwnd,out raw)) return false;
        window=raw.Rectangle;
        visible=window;
        if (DwmGetRect(hwnd,9,out frame,16)==0 && frame.R>frame.L && frame.B>frame.T)
        {
            var shown=Rectangle.Intersect(frame.Rectangle,window);
            if (shown.Width>0 && shown.Height>0) visible=shown;
        }
        return true;
    }

    [DllImport("user32.dll")] internal static extern bool LockWorkStation();
    [DllImport("kernel32.dll")] internal static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [StructLayout(LayoutKind.Sequential)]
    internal struct Memory
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended;
    }
    [DllImport("kernel32.dll")] internal static extern bool GlobalMemoryStatusEx(ref Memory memory);

    [DllImport("user32.dll")] private static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    internal static void MediaKey(byte key)
    {
        keybd_event(key,0,1,UIntPtr.Zero);
        keybd_event(key,0,3,UIntPtr.Zero);
    }

    internal static Placement Snapshot(IntPtr handle)
    {
        var placement = new Placement { Size=Marshal.SizeOf(typeof(Placement)) };
        if (!GetWindowPlacement(handle, ref placement)) throw new InvalidOperationException("The window did not return its placement.");
        return placement;
    }

    internal static void RefreshTheme()
    {
        IntPtr result;
        SendMessageTimeout(new IntPtr(0xffff), 0x1a, IntPtr.Zero, "ImmersiveColorSet", 2, 80, out result);
    }
}

internal sealed class DesktopWindow
{
    public IntPtr Handle;
    public uint Pid, Thread;
    public long Started;
    public string Title, ClassName;

    public override string ToString() { return Title; }

    private static long Start(uint pid)
    {
        try { using (var process = Process.GetProcessById((int)pid)) return process.StartTime.ToUniversalTime().Ticks; }
        catch (Exception) { return 0; }
    }

    // A process start time tells apart two processes that reused an id, but asking for it costs a process handle and,
    // for protected processes, a thrown exception. Windows are listed several times a second, so remember it briefly.
    private static readonly Dictionary<uint,KeyValuePair<long,int>> starts=new Dictionary<uint,KeyValuePair<long,int>>();
    private static long CachedStart(uint pid)
    {
        int now=Environment.TickCount;
        lock (starts)
        {
            KeyValuePair<long,int> hit;
            if (starts.TryGetValue(pid,out hit) && unchecked(now-hit.Value)<10000) return hit.Key;
            long start=Start(pid);
            if (starts.Count>1024) starts.Clear();
            starts[pid]=new KeyValuePair<long,int>(start,now);
            return start;
        }
    }

    public bool Alive()
    {
        uint pid;
        var thread = Native.GetWindowThreadProcessId(Handle,out pid);
        var cls = new StringBuilder(256);
        Native.GetClassName(Handle,cls,cls.Capacity);
        return thread == Thread && pid == Pid && CachedStart(pid)==Started && cls.ToString()==ClassName;
    }

    internal static DesktopWindow FromHandle(IntPtr hwnd)
    {
        uint pid;
        var thread=Native.GetWindowThreadProcessId(hwnd,out pid);
        var title=new StringBuilder(512);
        var cls=new StringBuilder(256);
        Native.GetWindowText(hwnd,title,title.Capacity);
        Native.GetClassName(hwnd,cls,cls.Capacity);
        return new DesktopWindow {Handle=hwnd,Pid=pid,Thread=thread,Started=CachedStart(pid),Title=title.ToString(),ClassName=cls.ToString()};
    }

    public static List<DesktopWindow> List(Screen screen, bool includeMinimized)
    {
        var windows = new List<DesktopWindow>();
        var ownProcess = Process.GetCurrentProcess().Id;
        Native.EnumWindows(delegate(IntPtr hwnd, IntPtr unused) {
            if (!Native.IsWindowVisible(hwnd) || (!includeMinimized && Native.IsIconic(hwnd)) ||
                Native.GetWindow(hwnd, 4)!=IntPtr.Zero || (Native.GetWindowLong(hwnd,-20)&0x80)!=0) return true;
            uint pid;
            var thread = Native.GetWindowThreadProcessId(hwnd,out pid);
            if (pid==ownProcess || (screen!=null && Screen.FromHandle(hwnd).DeviceName!=screen.DeviceName)) return true;
            int cloaked;
            if (Native.DwmGetWindowAttribute(hwnd,14,out cloaked,4)==0 && cloaked!=0) return true;
            var title = new StringBuilder(512);
            var cls = new StringBuilder(256);
            if (Native.GetWindowText(hwnd,title,title.Capacity)==0 || Native.GetClassName(hwnd,cls,cls.Capacity)==0) return true;
            if (cls.ToString()=="Progman" || cls.ToString()=="WorkerW" || cls.ToString()=="Shell_TrayWnd" || cls.ToString()=="Shell_SecondaryTrayWnd") return true;
            var started = CachedStart(pid);
            if (started!=0) windows.Add(new DesktopWindow { Handle=hwnd,Pid=pid,Thread=thread,Started=started,Title=title.ToString(),ClassName=cls.ToString() });
            return true;
        },IntPtr.Zero);
        return windows;
    }

    public void Activate()
    {
        if (!Alive()) return;
        if (Native.IsIconic(Handle)) Native.ShowWindowAsync(Handle,9);
        Native.SetForegroundWindow(Handle);
    }
}

public sealed class WindowBackup
{
    public long Handle, Started;
    public uint Pid, Thread;
    public string ClassName, Monitor;
    public Point WorkOffset;
    public Native.Placement Original;

    internal DesktopWindow Window()
    {
        return new DesktopWindow {Handle=new IntPtr(Handle),Started=Started,Pid=Pid,Thread=Thread,ClassName=ClassName};
    }
}

internal sealed class WindowLayouts
{
    private readonly Recovery recovery;
    private readonly List<WindowBackup> undo;
    public int Count { get { return undo.Count; } }

    public WindowLayouts(Recovery recovery)
    {
        this.recovery=recovery;
        undo=recovery.State.Positions;
    }
    internal static Rectangle[] Plan(Rectangle work,int n,string mode,int gap=8)
    {
        if (n<=0) return new Rectangle[0];
        work.Inflate(-gap,-gap);
        if (work.Width<200 || work.Height<150) throw new InvalidOperationException("The available screen area is too small.");
        if (mode=="Cascade")
        {
            int step = Math.Min(34, Math.Min(work.Width/3,work.Height/3)/Math.Max(1,n-1));
            return Enumerable.Range(0,n).Select(i=>new Rectangle(work.X+i*step,work.Y+i*step,
                work.Width-step*(n-1),work.Height-step*(n-1))).ToArray();
        }
        if (mode=="Focus" && n>1)
        {
            var width=work.Width*3/5;
            return new[] {new Rectangle(work.X,work.Y,width-gap,work.Height)}.Concat(
                Plan(new Rectangle(work.X+width,work.Y-gap,work.Width-width+gap,work.Height+2*gap),n-1,work.Height/(n-1)>=120?"Rows":"Grid",gap)).ToArray();
        }
        int cols = mode=="Columns" ? n : mode=="Rows" ? 1 : (int)Math.Ceiling(Math.Sqrt(n));
        int rows = (n+cols-1)/cols;
        if (work.Width/cols<180 || work.Height/rows<110)
            throw new InvalidOperationException("Use fewer windows, a larger monitor, or Cascade.");
        return Enumerable.Range(0,n).Select(i=>{
            int row=i/cols, col=i%cols;
            int used=Math.Min(cols,n-row*cols);
            int x=work.Width*col/used, right=work.Width*(col+1)/used;
            int y=work.Height*row/rows, bottom=work.Height*(row+1)/rows;
            return new Rectangle(work.X+x,work.Y+y,right-x-(col+1<used?gap:0),bottom-y-(row+1<rows?gap:0));
        }).ToArray();
    }

    // Notes where a window was before Bluehour touched it, once, so Undo and Return to Windows can put it back.
    // Returns null when the window is already recorded. Nothing reaches the disk until Commit, so a batch of windows
    // shares one write: each write serializes the whole journal and flushes it to disk, which adds up quickly.
    internal WindowBackup Record(DesktopWindow window)
    {
        if (undo.Any(p=>p.Handle==window.Handle.ToInt64() && p.Window().Alive())) return null;
        var originalScreen=Screen.FromHandle(window.Handle);
        var backup=new WindowBackup { Handle=window.Handle.ToInt64(),Started=window.Started,Pid=window.Pid,
            Thread=window.Thread,ClassName=window.ClassName,Original=Native.Snapshot(window.Handle),Monitor=originalScreen.DeviceName,
            WorkOffset=Offset(originalScreen) };
        undo.Add(backup);
        return backup;
    }

    // Writes the journal. If that fails, the windows just recorded are forgotten so they can be left alone.
    internal void Commit(IEnumerable<WindowBackup> added)
    {
        try { recovery.Save(); }
        catch { foreach (var backup in added.ToArray()) undo.Remove(backup); throw; }
    }

    // Places a window immediately. `bounds` is the full window rectangle in screen coordinates.
    internal bool Place(DesktopWindow window,Screen screen,Rectangle bounds)
    {
        if (!window.Alive()) return false;
        var recorded=Record(window);
        if (recorded!=null) Commit(new[]{recorded});
        var target=Native.Snapshot(window.Handle);
        target.Flags=4;
        target.Show=4;
        // WINDOWPLACEMENT uses workspace coordinates for ordinary top-level windows.
        bounds.Offset(screen.Bounds.Left-screen.WorkingArea.Left,screen.Bounds.Top-screen.WorkingArea.Top);
        target.Normal=new Native.Rect(bounds);
        return Native.SetWindowPlacement(window.Handle,ref target);
    }

    public string Apply(Screen screen,IEnumerable<DesktopWindow> candidates,string mode)
    {
        var chosen=candidates.Where(w=>w.Alive()).ToArray();
        if (chosen.Length==0) return "No eligible windows.";
        var plan=Plan(screen.WorkingArea,chosen.Length,mode);
        int moved=0;
        for (int i=0;i<chosen.Length;i++)
        {
            try
            {
                if (Place(chosen[i],screen,plan[i])) moved++;
            }
            catch (InvalidOperationException) { }
        }
        return String.Format("Arranged {0} of {1} windows. Apps can enforce their own minimum size.",moved,chosen.Length);
    }

    private static Point Offset(Screen screen)
    {
        return new Point(screen.WorkingArea.Left-screen.Bounds.Left,screen.WorkingArea.Top-screen.Bounds.Top);
    }

    public int Revert()
    {
        int failed=0;
        foreach (var entry in undo.ToArray())
        {
            var window=entry.Window();
            var original=entry.Original;
            var restoredScreen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==entry.Monitor);
            if (restoredScreen!=null)
            {
                var offset=Offset(restoredScreen);
                var rect=original.Normal.Rectangle;
                rect.Offset(entry.WorkOffset.X-offset.X,entry.WorkOffset.Y-offset.Y);
                original.Normal=new Native.Rect(rect);
            }
            original.Flags|=4;
            if (!window.Alive() || Native.SetWindowPlacement(window.Handle,ref original))
            {
                undo.Remove(entry);
                try { recovery.Save(); }
                catch { undo.Add(entry); throw; }
            }
            else failed++;
        }
        return failed;
    }
}







