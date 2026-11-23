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
        internal IFrame Frame;
        internal Rectangle Outer,Goal,From;   // tile including the ring, and the visible rectangle the app should end at
        internal Stopwatch Glide;
        internal int Duration;
        internal bool Tiled,Fresh,Moving,Slide;
        internal int InsetL,InsetT;      // the invisible resize border Windows adds to the left and top
        internal Rectangle Placed;       // where the last slide step put the window
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
    private readonly FramePump pump;
    private readonly Stopwatch lastScan=Stopwatch.StartNew(),slowLog=Stopwatch.StartNew(),syncDue=Stopwatch.StartNew();
    private Native.EventProc callback;
    private Entry dragging;
    private DropGhost ghost;
    private bool running,paused,scanSoon;
    private int failures;

    internal Workspace(Shell shell,WindowLayouts layouts)
    {
        this.shell=shell;this.layouts=layouts;
        pump=new FramePump(shell,Beat);
        clock.Tick+=delegate{Beat();};
    }

    private void Beat()
    {
        try{Tick();failures=0;}
        catch(Exception ex){if(failures++==0)shell.Notify(ex.Message,true);}   // once per run of failures, not once per tick
    }

    // While windows are moving the work is paced by the display's refresh; otherwise by a slow timer.
    private void Pace()
    {
        if(!running || paused || pump.Running)return;
        clock.Stop();
        pump.Start();
    }

    internal int Count { get { return items.Count; } }

    // The windows seen by the latest scan, for the dock; and a signal that a fresh one is ready.
    internal List<DesktopWindow> Snapshot=new List<DesktopWindow>();
    internal event Action Scanned;
    internal static bool IsAppWindow(DesktopWindow window){return Eligible(window,true);}

    // ---- Lifecycle ----------------------------------------------------------------------------------------------

    internal void Start()
    {
        if(running)return;
        running=true;paused=false;
        Hook();
        clock.Start();
        scanSoon=true;
    }

    // Only the quiet events are watched: foreground changes, move/resize start and end, and minimize/restore. Events
    // for every window move or UI object on the system would cost a wake-up each, and Bluehour finds new and closed
    // windows by looking every second or so instead. (Foreground changes also trigger a look straight away.)
    private void Hook()
    {
        callback=new Native.EventProc(OnEvent);
        foreach(var range in new[]{new uint[]{0x0003,0x0003},new uint[]{0x000A,0x000B},new uint[]{0x0016,0x0017}})
        {
            // Out of context and skipping this process, so our own windows cannot feed back into the hook.
            var hook=Native.SetWinEventHook(range[0],range[1],IntPtr.Zero,callback,0,0,2);
            if(hook!=IntPtr.Zero)hooks.Add(hook);
        }
    }

    private void Unhook()
    {
        foreach(var hook in hooks)Native.UnhookWinEvent(hook);
        hooks.Clear();
    }

    // While a game or full-screen app is in front the workspace does nothing at all: no timer, no hooks.
    internal bool Paused { get { return paused; } }
    internal void Pause()
    {
        if(!running||paused)return;
        paused=true;
        clock.Stop();pump.Stop();
        Unhook();
        HideGhost();
    }
    internal void Resume()
    {
        if(!running||!paused)return;
        paused=false;
        Hook();
        clock.Interval=100;
        clock.Start();
        scanSoon=true;
    }

    // Hands every window back: rounded corners cleared, frames closed. Positions are restored by WindowLayouts.Revert.
    internal void Stop()
    {
        running=false;paused=false;
        HideGhost();
        clock.Stop();pump.Stop();
        Unhook();
        ReleaseAll();
        claims.Clear();
    }

    // The frame style setting changed: give every window the new kind of frame.
    internal void ChangeFrames()
    {
        foreach(var entry in items)
        {
            Free(entry);
            entry.Frame=MakeFrame(entry.Window);
            entry.Frame.Pop();
        }
        Relayout(false);
    }

    private IFrame MakeFrame(DesktopWindow window)
    {
        string style=shell.FrameStyle;
        Func<Color> accent=delegate{return shell.Accent;},partner=delegate{return shell.Highlight;};
        if(style=="Sticker")return new WorkspaceFrame(window,accent,partner);
        if(style=="Native")return new NativeFrame(window,accent,partner);
        return new NullFrame(window);
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
        pump.Stop();
        clock.Dispose();
        if(ghost!=null){ghost.Dispose();ghost=null;}
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
        if(Find(window.Handle)==null)
        {
            var recorded=new List<WindowBackup>();
            var adopted=new List<Entry>();
            var entry=Adopt(window,shell.ActiveLayout=="Focus",recorded);
            if(entry!=null)adopted.Add(entry);
            Settle(recorded,adopted);
        }
        window.Activate();
        Refresh();
    }

    // A layout was chosen: every window on this display joins it, including ones that were maximized.
    internal void Retile()
    {
        foreach(var entry in items)
        {
            if(Native.IsZoomed(entry.Window.Handle))entry.Fresh=true;
            entry.Tiled=Tileable(entry);
        }
        Relayout(true);
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
        var batch=new List<WindowBackup>();
        var joined=new List<Entry>();
        foreach(var window in (shell.ActiveLayout=="Focus"?picked.Reverse():picked))
        {
            excluded.Remove(window.Handle.ToInt64());
            if(Find(window.Handle)!=null)continue;
            var entry=Adopt(window,false,batch);
            if(entry!=null)joined.Add(entry);
        }
        Settle(batch,joined);
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

    // Starts managing a window. The journal is not written here: callers adopt a batch and then call Settle once.
    private Entry Adopt(DesktopWindow window,bool atFront,List<WindowBackup> recorded)
    {
        try
        {
            var backup=layouts.Record(window);
            if(backup!=null)recorded.Add(backup);
        }
        catch(Exception ex)
        {
            excluded.Add(window.Handle.ToInt64());   // a window that cannot be recorded is not retried every few hundred ms
            shell.Notify(ex.Message,true);
            return null;
        }
        var entry=new Entry
        {
            Window=window,Fresh=true,
            Frame=MakeFrame(window)
        };
        entry.Tiled=Tileable(entry);
        if(atFront)items.Insert(0,entry);else items.Add(entry);
        entry.Frame.Pop();
        return entry;
    }

    // Writes the journal for a batch of adopted windows in one go. They have not been moved yet, so if the write
    // fails they are simply let go.
    private bool Settle(List<WindowBackup> recorded,List<Entry> adopted)
    {
        if(recorded.Count==0)return true;
        try
        {
            Diag.Mark("workspace: saving the journal");
            layouts.Commit(recorded);
            return true;
        }
        catch(Exception ex)
        {
            foreach(var entry in adopted){excluded.Add(entry.Window.Handle.ToInt64());Free(entry);items.Remove(entry);}
            shell.Notify("Could not save the window journal, so no windows were moved. "+ex.Message,true);
            return false;
        }
    }

    private void Scan()
    {
        lastScan.Restart();
        scanSoon=false;
        Diag.Mark("workspace: listing windows");
        var windows=DesktopWindow.List(null,true);
        Snapshot=windows;
        var live=new HashSet<long>(windows.Select(w=>w.Handle.ToInt64()));
        bool changed=false;
        foreach(var entry in items.ToArray())
            if(!live.Contains(entry.Window.Handle.ToInt64()) || !entry.Window.Alive())
            {
                Free(entry);items.Remove(entry);changed=true;
            }
        excluded.RemoveWhere(h=>!live.Contains(h));
        bool focus=shell.ActiveLayout=="Focus";
        var recorded=new List<WindowBackup>();
        var adopted=new List<Entry>();
        Diag.Mark("workspace: adopting windows");
        foreach(var window in (focus?windows.AsEnumerable().Reverse():windows))
        {
            if(Find(window.Handle)!=null || !Eligible(window,false) || excluded.Contains(window.Handle.ToInt64()))continue;
            var claim=claims.FirstOrDefault(c=>c.App.Matches(window));
            bool onMonitor=Screen.FromHandle(window.Handle).DeviceName==shell.Monitor.DeviceName;
            if(claim==null && !(shell.AutoTile && onMonitor))continue;
            if(claim!=null)claims.Remove(claim);
            var entry=Adopt(window,focus,recorded);
            if(entry==null)continue;
            adopted.Add(entry);
            changed=true;
        }
        if(!Settle(recorded,adopted))changed=true;
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
        // Frames that are not drawn by an overlay (the native border) are brought up to date here, so a window that has just
        // gone full screen or come back is noticed within a scan.
        foreach(var entry in items)if(!entry.Frame.Polled)entry.Frame.Sync(false);
        Diag.Mark("workspace: laying out");
        if(changed)Relayout(true);
        if(Scanned!=null)Scanned();
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
        int ring=shell.FrameStyle=="Sticker"?(int)Math.Round(WorkspaceFrame.Ring*Native.Scale(handle)):0;
        var goal=Rectangle.Inflate(outer,-ring,-ring);
        Rectangle window,shown;
        if(!Native.Bounds(handle,out window,out shown))return;
        entry.Outer=outer;
        entry.Goal=goal;
        entry.Moving=false;
        entry.Slide=false;
        if(Native.IsZoomed(handle) || Native.IsIconic(handle))
        {
            // Restore first; the next tick lands the exact rectangle once Windows reports the real frame size.
            try{layouts.Place(entry.Window,shell.Monitor,goal);}
            catch(InvalidOperationException){}
            entry.From=goal;entry.Duration=0;
        }
        else if(entry.Fresh && animate)
        {
            if(Motion.Speed=="Smooth")
            {
                // cl-pop-in: a new window grows from 94% of its tile.
                int dx=(int)Math.Round(goal.Width*(1-Motion.PopScale)/2),dy=(int)Math.Round(goal.Height*(1-Motion.PopScale)/2);
                entry.From=Rectangle.Inflate(goal,-dx,-dy);
                entry.Duration=Motion.PopIn;
            }
            else
            {
                // A new app opens straight into its tile, and Windows' own open animation (done by the compositor, at
                // the display's refresh rate) is the only one that plays.
                entry.From=goal;
                entry.Duration=0;
            }
        }
        else
        {
            entry.From=shown;
            entry.Duration=animate?Motion.Glide:0;
            if(entry.Duration>0 && Motion.Speed!="Smooth" && shown!=goal)
            {
                // Resize once, now, and from then on only move. Windows moves a window's picture without the app doing
                // anything, so a slide is smooth at any refresh rate; resizing every frame makes the app redraw every
                // frame, and that is the stutter and tearing.
                entry.Slide=true;
                entry.InsetL=shown.Left-window.Left;entry.InsetT=shown.Top-window.Top;
                entry.Placed=Rectangle.Empty;
                if(shown.Size!=goal.Size)
                    Native.SetWindowPos(handle,IntPtr.Zero,window.Left,window.Top,goal.Width+(window.Width-shown.Width),
                        goal.Height+(window.Height-shown.Height),0x4014);
                entry.From=new Rectangle(shown.Location,goal.Size);
            }
        }
        entry.Fresh=false;
        entry.Glide=Stopwatch.StartNew();
        if(entry.Duration>0)entry.Frame.Suspended=true;
        Pace();
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

    // One step of a slide: position only (SWP_NOSIZE), and nothing at all if the window would not move.
    private static void Slide(Entry entry,Rectangle visible)
    {
        if(!entry.Placed.IsEmpty && entry.Placed.Location==visible.Location)return;
        entry.Placed=visible;
        Native.SetWindowPos(entry.Window.Handle,IntPtr.Zero,visible.Left-entry.InsetL,visible.Top-entry.InsetT,0,0,0x4015);
    }

    private bool StepGlides()
    {
        bool busy=false;
        foreach(var entry in items)
        {
            if(entry.Glide==null)continue;
            if(entry.Moving){entry.Glide=null;entry.Frame.Suspended=false;continue;}
            double t=Motion.Progress(entry.Glide.Elapsed.TotalMilliseconds,entry.Duration);
            // .cl-win moves with the emphasized-decel curve: quick off the mark, then a long settle.
            if(t>=1)Apply(entry,entry.Goal);
            else if(entry.Slide)Slide(entry,Motion.Lerp(entry.From,entry.Goal,Motion.Decel.At(t)));
            else Apply(entry,Motion.Lerp(entry.From,entry.Goal,Motion.Decel.At(t)));
            if(t>=1){entry.Glide=null;entry.Slide=false;if(entry.Frame.Suspended){entry.Frame.Suspended=false;entry.Frame.Pop();}}
            else busy=true;
        }
        return busy;
    }

    private void Tick()
    {
        var timer=Stopwatch.StartNew();
        Diag.Mark("workspace: gliding windows");
        bool busy=StepGlides();
        // Only overlay frames have to follow their window; the others are set once and left alone.
        if(busy || dragging!=null || syncDue.ElapsedMilliseconds>=250)
        {
            syncDue.Restart();
            foreach(var entry in items.ToArray())
            {
                if(!entry.Frame.Polled)continue;
                if(!entry.Frame.Sync(true)){Free(entry);items.Remove(entry);scanSoon=true;continue;}
                if(entry.Frame.Busy)busy=true;
            }
        }
        if(dragging!=null){Highlight();busy=true;}
        if(scanSoon || lastScan.ElapsedMilliseconds>=(claims.Count>0?200:2000))Scan();
        else
        {
            // The window in front is checked on every tick (about once a second when idle): full-screen video starts there.
            var front=Find(Native.GetAncestor(Native.GetForegroundWindow(),3));
            if(front!=null && !front.Frame.Polled)front.Frame.Sync(false);
        }
        if(!busy)busy=items.Any(e=>e.Glide!=null || e.Frame.Busy);
        if(busy)Pace();   // once per display refresh while something moves
        else
        {
            pump.Stop();
            int idle=claims.Count>0?150:1000;
            if(!clock.Enabled || clock.Interval!=idle){clock.Interval=idle;clock.Start();}
        }
        if(timer.ElapsedMilliseconds>150 && slowLog.ElapsedMilliseconds>2000)
        {
            slowLog.Restart();
            Diag.Log("A workspace update took "+timer.ElapsedMilliseconds+" ms. Last step: "+Diag.Where);
        }
    }

    // ---- Window events ------------------------------------------------------------------------------------------

    private void OnEvent(IntPtr hook,uint eventId,IntPtr hwnd,int objectId,int childId,uint thread,uint time)
    {
        if(objectId!=0 || childId!=0 || hwnd==IntPtr.Zero || !running)return;
        try
        {
            switch(eventId)
            {
                case 0x0003: // foreground changed: border colours, and a new window has probably appeared
                    foreach(var entry in items)entry.Frame.Sync(false);
                    scanSoon=true;clock.Interval=30;   // look soon: the idle tick is slow
                    break;
                case 0x000A: // a window started moving or resizing
                    dragging=Find(hwnd);
                    if(dragging!=null){dragging.Moving=true;dragging.Glide=null;dragging.Frame.Suspended=false;Pace();}
                    break;
                case 0x000B:
                    Drop();
                    break;
                default: // minimized or restored
                    if(Find(hwnd)!=null){scanSoon=true;clock.Interval=30;}
                    break;
            }
        }
        catch(Exception ex){shell.Notify(ex.Message,true);}
    }

    // While a window is being dragged, the tile under the pointer lights up as the swap target.
    private void Highlight()
    {
        var target=SnapTarget(Cursor.Position,dragging);
        foreach(var entry in items)entry.Frame.Highlight=entry==target;
        if(target==null || shell.ActiveLayout=="Cascade"){HideGhost();return;}
        if(ghost==null)ghost=new DropGhost();
        ghost.ShowAt(target.Outer,shell.Highlight);
    }

    private void HideGhost(){if(ghost!=null)ghost.Clear();}

    // The tile a dragged window would snap to: the one under the pointer, or the nearest one when the pointer is in a
    // gap between tiles. Over its own tile, or off the tiled area altogether, it goes back where it was.
    private Entry SnapTarget(Point at,Entry moved)
    {
        if(moved==null || moved.Outer.Contains(at) || !shell.TileArea.Contains(at))return null;
        Entry best=null;
        long bestDistance=long.MaxValue;
        foreach(var entry in items)
        {
            if(entry==moved || !entry.Tiled || entry.Outer.IsEmpty)continue;
            var tile=entry.Outer;
            if(tile.Contains(at))return entry;
            long dx=Math.Max(Math.Max(tile.Left-at.X,0),at.X-tile.Right),dy=Math.Max(Math.Max(tile.Top-at.Y,0),at.Y-tile.Bottom);
            long distance=dx*dx+dy*dy;
            if(distance<bestDistance){bestDistance=distance;best=entry;}
        }
        return best;
    }

    private void Drop()
    {
        var moved=dragging;
        dragging=null;
        if(moved==null)return;
        moved.Moving=false;
        var target=SnapTarget(Cursor.Position,moved);
        foreach(var entry in items)entry.Frame.Highlight=false;
        HideGhost();
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
