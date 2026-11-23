using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Timer=System.Windows.Forms.Timer;

// Keeps real Windows apps in a tiled workspace: finds them, frames them, glides them into their tiles, and puts
// them back when the desktop is dismissed. Everything runs on the UI thread.
internal sealed class Workspace : IDisposable
{
    private sealed class Entry
    {
        internal DesktopWindow Window;
        internal WorkspaceFrame Frame;
        internal Rectangle Outer,Goal,From;   // tile including the ring, and the visible rectangle the app should end at
        internal Stopwatch Glide;
        internal int Duration;
        internal bool Tiled,Fresh,Moving;
    }

    private sealed class Claim
    {
        internal WorkspaceApp App;
        internal int Started;
    }

    internal const int Gap=12;
    private const int ClaimMs=30000;

    private static readonly string[] Ignored={"Windows.UI.Core.CoreWindow","MultitaskingViewFrame","XamlExplorerHostIslandWindow","ForegroundStaging"};

    private readonly Shell shell;
    private readonly WindowLayouts layouts;
    private readonly List<Entry> items=new List<Entry>();
    private readonly List<Claim> claims=new List<Claim>();
    private readonly HashSet<long> excluded=new HashSet<long>();
    private readonly List<IntPtr> hooks=new List<IntPtr>();
    private readonly Timer clock=new Timer {Interval=100};
    private readonly Stopwatch lastScan=Stopwatch.StartNew();
    private Native.EventProc callback;
    private Entry dragging;
    private bool running,scanSoon;
    private int failures;

    internal Workspace(Shell shell,WindowLayouts layouts)
    {
        this.shell=shell;this.layouts=layouts;
        clock.Tick+=delegate
        {
            try{Tick();failures=0;}
            catch(Exception ex){if(failures++==0)shell.Notify(ex.Message,true);}   // once per run of failures, not once per tick
        };
    }

    internal int Count { get { return items.Count; } }

    // ---- Lifecycle ----------------------------------------------------------------------------------------------

    internal void Start()
    {
        if(running)return;
        running=true;
        callback=new Native.EventProc(OnEvent);
        // foreground, move/size start+end, minimize start+end, create/destroy/show/hide, location change
        foreach(var range in new[]{new uint[]{0x0003,0x0003},new uint[]{0x000A,0x000B},new uint[]{0x0016,0x0017},new uint[]{0x8000,0x8003},new uint[]{0x800B,0x800B}})
        {
            // Out of context and skipping this process, so our own frames cannot feed back into the hook.
            var hook=Native.SetWinEventHook(range[0],range[1],IntPtr.Zero,callback,0,0,2);
            if(hook!=IntPtr.Zero)hooks.Add(hook);
        }
        clock.Start();
        scanSoon=true;
    }

    // Hands every window back: rounded corners cleared, frames closed. Positions are restored by WindowLayouts.Revert.
    internal void Stop()
    {
        running=false;
        clock.Stop();
        foreach(var hook in hooks)Native.UnhookWinEvent(hook);
        hooks.Clear();
        ReleaseAll();
        claims.Clear();
    }

    internal void ReleaseAll()
    {
        foreach(var entry in items)Free(entry);
        items.Clear();
        dragging=null;
    }

    public void Dispose()
    {
        Stop();
        clock.Dispose();
    }

    private static void Free(Entry entry)
    {
        try{entry.Frame.Release();}
        finally{entry.Frame.Dispose();}
    }

    // ---- Opening apps into the workspace ------------------------------------------------------------------------

    // Looks for a window of the app that is already open, including minimized ones.
    internal DesktopWindow Existing(WorkspaceApp app)
    {
        foreach(var window in DesktopWindow.List(null,true))
            if(Eligible(window,true) && app.Matches(window))return window;
        return null;
    }

    // Starts watching for the app's first window. It is framed and tiled even when automatic tiling is off.
    internal void Expect(WorkspaceApp app)
    {
        claims.RemoveAll(c=>c.App.Pin.Target==app.Pin.Target);
        claims.Add(new Claim {App=app,Started=Environment.TickCount});
        scanSoon=true;
    }

    // Brings an already open window into the workspace and to the front.
    internal void Bring(DesktopWindow window)
    {
        excluded.Remove(window.Handle.ToInt64());
        if(Find(window.Handle)==null)Adopt(window,shell.ActiveLayout=="Focus");
        window.Activate();
        Refresh();
    }

    // Manual arrange: the checked windows join, the unchecked ones are left alone until they close.
    internal void Arrange(IEnumerable<DesktopWindow> chosen,IEnumerable<DesktopWindow> all)
    {
        var picked=chosen.Where(w=>w.Alive()).ToArray();
        foreach(var window in all.Where(w=>picked.All(p=>p.Handle!=w.Handle)))
        {
            excluded.Add(window.Handle.ToInt64());
            var entry=Find(window.Handle);
            if(entry!=null){Free(entry);items.Remove(entry);}
        }
        foreach(var window in (shell.ActiveLayout=="Focus"?picked.Reverse():picked))
        {
            excluded.Remove(window.Handle.ToInt64());
            if(Find(window.Handle)==null)Adopt(window,false);
        }
        Scan();
        Relayout(true);
    }

    // ---- Tracking -----------------------------------------------------------------------------------------------

    private Entry Find(IntPtr handle){return items.FirstOrDefault(e=>e.Window.Handle==handle);}

    private static bool Eligible(DesktopWindow window,bool allowMinimized)
    {
        var handle=window.Handle;
        if(Ignored.Contains(window.ClassName))return false;
        // Fixed-size windows, dialogs and overlays keep their native presentation.
        if((Native.GetWindowLong(handle,-16)&0x40000)==0)return false;
        if((Native.GetWindowLong(handle,-20)&0x08000000)!=0)return false;
        if(Native.IsIconic(handle))return allowMinimized;
        Rectangle outer,visible;
        return Native.Bounds(handle,out outer,out visible) && visible.Width>=160 && visible.Height>=100;
    }

    private bool Tileable(Entry entry)
    {
        var handle=entry.Window.Handle;
        if(!Native.IsWindowVisible(handle) || Native.IsIconic(handle))return false;
        if(Native.IsZoomed(handle) && !entry.Fresh)return false;   // a window the person maximized stays maximized
        Rectangle outer,visible;
        if(!Native.Bounds(handle,out outer,out visible))return false;
        var screen=Screen.FromHandle(handle);
        if(screen.DeviceName!=shell.Monitor.DeviceName)return false;
        return Native.IsZoomed(handle) || !visible.Contains(screen.Bounds);
    }

    private void Adopt(DesktopWindow window,bool atFront)
    {
        try{layouts.Record(window);}
        catch(Exception ex){shell.Notify(ex.Message,true);return;}
        var entry=new Entry
        {
            Window=window,Fresh=true,
            Frame=new WorkspaceFrame(window,delegate{return shell.Accent;},delegate{return shell.Highlight;})
        };
        entry.Tiled=Tileable(entry);
        if(atFront)items.Insert(0,entry);else items.Add(entry);
        entry.Frame.Pop();
    }

    private void Scan()
    {
        lastScan.Restart();
        scanSoon=false;
        var windows=DesktopWindow.List(null,true);
        var live=new HashSet<long>(windows.Select(w=>w.Handle.ToInt64()));
        bool changed=false;
        foreach(var entry in items.ToArray())
            if(!live.Contains(entry.Window.Handle.ToInt64()) || !entry.Window.Alive())
            {
                Free(entry);items.Remove(entry);changed=true;
            }
        excluded.RemoveWhere(h=>!live.Contains(h));
        bool focus=shell.ActiveLayout=="Focus";
        foreach(var window in (focus?windows.AsEnumerable().Reverse():windows))
        {
            if(Find(window.Handle)!=null || !Eligible(window,false) || excluded.Contains(window.Handle.ToInt64()))continue;
            var claim=claims.FirstOrDefault(c=>c.App.Matches(window));
            bool onMonitor=Screen.FromHandle(window.Handle).DeviceName==shell.Monitor.DeviceName;
            if(claim==null && !(shell.AutoTile && onMonitor))continue;
            if(claim!=null)claims.Remove(claim);
            Adopt(window,focus);
            changed=true;
        }
        foreach(var entry in items)
        {
            bool tile=Tileable(entry);
            if(tile!=entry.Tiled){entry.Tiled=tile;changed=true;}
        }
        foreach(var claim in claims.Where(c=>Environment.TickCount-c.Started>ClaimMs).ToArray())
        {
            claims.Remove(claim);
            shell.Notify(claim.App.Name+" did not open a window."+(claim.App.IsDiscord?" Check that Discord is installed (Control center → Cleanup → Install Discord).":""),true);
        }
        if(changed)Relayout(true);
    }

    // Re-reads the world now instead of waiting for the next timer tick.
    internal void Refresh(){Scan();}

    // ---- Layout and motion --------------------------------------------------------------------------------------

    internal void Relayout(bool animate)
    {
        var tiled=items.Where(e=>e.Tiled).ToList();
        if(tiled.Count==0)return;
        string mode=shell.ActiveLayout;
        Rectangle[] plan;
        try{plan=WindowLayouts.Plan(shell.TileArea,tiled.Count,mode,Gap);}
        catch(InvalidOperationException)
        {
            plan=WindowLayouts.Plan(shell.TileArea,tiled.Count,"Cascade",Gap);
            shell.Notify("Too many windows for "+mode+", so they are cascaded instead.",false);
        }
        for(int i=0;i<tiled.Count;i++)Retarget(tiled[i],plan[i],animate && Motion.Enabled);
    }

    private void Retarget(Entry entry,Rectangle outer,bool animate)
    {
        var handle=entry.Window.Handle;
        int ring=(int)Math.Round(WorkspaceFrame.Ring*Native.Scale(handle));
        var goal=Rectangle.Inflate(outer,-ring,-ring);
        Rectangle window,shown;
        if(!Native.Bounds(handle,out window,out shown))return;
        entry.Outer=outer;
        entry.Goal=goal;
        entry.Moving=false;
        if(Native.IsZoomed(handle) || Native.IsIconic(handle))
        {
            // Restore first; the next tick lands the exact rectangle once Windows reports the real frame size.
            try{layouts.Place(entry.Window,shell.Monitor,goal);}
            catch(InvalidOperationException){}
            entry.From=goal;entry.Duration=0;
        }
        else if(entry.Fresh && animate)
        {
            // cl-pop-in: a new window grows from 94% of its tile.
            int dx=(int)Math.Round(goal.Width*(1-Motion.PopScale)/2),dy=(int)Math.Round(goal.Height*(1-Motion.PopScale)/2);
            entry.From=Rectangle.Inflate(goal,-dx,-dy);
            entry.Duration=Motion.PopIn;
        }
        else
        {
            entry.From=shown;
            entry.Duration=animate?Motion.Glide:0;
        }
        entry.Fresh=false;
        entry.Glide=Stopwatch.StartNew();
        clock.Interval=15;
    }

    private static void Apply(Entry entry,Rectangle visible)
    {
        Rectangle window,shown;
        if(!Native.Bounds(entry.Window.Handle,out window,out shown))return;
        // The window rectangle is larger than what is visible by Windows 10's invisible resize border.
        int left=shown.Left-window.Left,top=shown.Top-window.Top,right=window.Right-shown.Right,bottom=window.Bottom-shown.Bottom;
        Native.SetWindowPos(entry.Window.Handle,IntPtr.Zero,visible.Left-left,visible.Top-top,
            visible.Width+left+right,visible.Height+top+bottom,0x4014);
    }

    private bool StepGlides()
    {
        bool busy=false;
        foreach(var entry in items)
        {
            if(entry.Glide==null)continue;
            if(entry.Moving){entry.Glide=null;continue;}
            double t=Motion.Progress(entry.Glide.Elapsed.TotalMilliseconds,entry.Duration);
            // .cl-win moves with the emphasized-decel curve: quick off the mark, then a long settle.
            Apply(entry,t>=1?entry.Goal:Motion.Lerp(entry.From,entry.Goal,Motion.Decel.At(t)));
            if(t>=1)entry.Glide=null;else busy=true;
        }
        return busy;
    }

    private void Tick()
    {
        bool busy=StepGlides();
        foreach(var entry in items.ToArray())
        {
            if(!entry.Frame.Sync()){Free(entry);items.Remove(entry);scanSoon=true;continue;}
            if(entry.Frame.Busy)busy=true;
        }
        if(dragging!=null){Highlight();busy=true;}
        if(scanSoon || lastScan.ElapsedMilliseconds>=400)Scan();
        if(!busy)busy=items.Any(e=>e.Glide!=null || e.Frame.Busy);
        clock.Interval=busy?15:100;
    }

    // ---- Window events ------------------------------------------------------------------------------------------

    private void OnEvent(IntPtr hook,uint eventId,IntPtr hwnd,int objectId,int childId,uint thread,uint time)
    {
        if(objectId!=0 || childId!=0 || hwnd==IntPtr.Zero || !running)return;
        try
        {
            switch(eventId)
            {
                case 0x0003: // foreground changed: ring colours
                    foreach(var entry in items)entry.Frame.Sync();
                    break;
                case 0x000A: // a window started moving or resizing
                    dragging=Find(hwnd);
                    if(dragging!=null){dragging.Moving=true;dragging.Glide=null;clock.Interval=15;}
                    break;
                case 0x000B:
                    Drop();
                    break;
                case 0x800B: // location change
                    var moved=Find(hwnd);
                    if(moved!=null)moved.Frame.Sync();
                    break;
                default: // minimize, create, destroy, show, hide
                    if(Native.GetAncestor(hwnd,2)==hwnd || Find(hwnd)!=null)scanSoon=true;
                    break;
            }
        }
        catch(Exception ex){shell.Notify(ex.Message,true);}
    }

    // While a window is being dragged, the tile under the pointer lights up as the swap target.
    private void Highlight()
    {
        var at=Cursor.Position;
        foreach(var entry in items)
            entry.Frame.Highlight=entry!=dragging && entry.Tiled && !entry.Outer.IsEmpty && entry.Outer.Contains(at);
    }

    private void Drop()
    {
        var moved=dragging;
        dragging=null;
        if(moved==null)return;
        moved.Moving=false;
        var at=Cursor.Position;
        var target=items.FirstOrDefault(e=>e!=moved && e.Tiled && !e.Outer.IsEmpty && e.Outer.Contains(at));
        foreach(var entry in items)entry.Frame.Highlight=false;
        if(shell.ActiveLayout=="Cascade")return;   // overlapping windows stay where they were dropped
        if(target!=null && moved.Tiled)
        {
            int a=items.IndexOf(moved),b=items.IndexOf(target);
            items[a]=target;
            items[b]=moved;
        }
        Relayout(true);
    }
}
