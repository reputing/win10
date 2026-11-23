using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal sealed partial class Shell
{
    private readonly List<DesktopSurface> canvases=new List<DesktopSurface>();
    private bool wasOnExplorerDesktop;
    private bool recoveryKey;
    private bool dockAvailable;

    private bool starting;
    private bool fullScreen;      // a real full-screen app is in front (see FullScreen.Window)
    private int fullTicks;

    // Switching to the Bluehour desktop from the control center: the same steps as at launch, behind the loading
    // screen, one at a time so the app keeps responding while Windows and the dock are set up.
    private async void StartDesktop()
    {
        if (test || starting) return;
        starting=true;
        try
        {
            State.Desktop=true;
            recovery.Save();
            using (var splash=new Splash(Monitor,Accent,Highlight,"setting up your desktop"))
            {
                splash.Open();
                await splash.Run(this,DesktopSteps(true),.04,.96);
                await splash.Finish();
            }
        }
        catch (Exception ex) { Notify(ex.Message,true); }
        finally { starting=false; }
    }

    private static KeyValuePair<string,Action> Step(string label,Action run){return new KeyValuePair<string,Action>(label,run);}

    // What it takes to bring the desktop up, as named steps that can be run one after another.
    internal List<KeyValuePair<string,Action>> DesktopSteps(bool bringForward)
    {
        var steps=new List<KeyValuePair<string,Action>>();
        if (State.ReplaceTaskbar || State.AutoHideTaskbar)
            steps.Add(Step("tucking the taskbar away",delegate{
                try { TuckTaskbar(); }
                catch (Exception ex) { Say("Windows taskbar: "+ex.Message); }
            }));
        steps.Add(Step("setting up the dock",UpdateDock));
        steps.Add(Step("hanging your wallpaper",RefreshSurfaces));
        steps.Add(Step("finding your windows",workspace.Start));
        steps.Add(Step("binding the windows key",UpdateWinKey));
        if (bringForward) steps.Add(Step("almost there",RevealDesktop));
        return steps;
    }

    // The steps Bluehour runs behind the loading screen when it starts.
    internal List<KeyValuePair<string,Action>> BootSteps(bool background)
    {
        var steps=State.Desktop?DesktopSteps(background):new List<KeyValuePair<string,Action>>{
            Step("putting the taskbar back",RestoreTaskbar),Step("setting up the dock",UpdateDock)};
        steps.Add(Step("finding your apps",LoadLibrary));
        steps.Add(Step("picking up your desktop shortcuts",delegate{SyncDesktopApps(true);}));
        return steps;
    }

    // Creates the window handle without showing the window, so the hotkeys work even when it stays hidden.
    internal void Prepare(){ if (!IsHandleCreated) CreateHandle(); }

    private void ResumeDesktop(bool bringForward)
    {
        foreach (var step in DesktopSteps(bringForward)) step.Value();
    }

    // ---- Resting while you play ---------------------------------------------------------------------------------

    internal bool Resting { get { return resting; } }

    // A game, video or presentation in front (or Ctrl + Alt + G) puts Bluehour to rest: no work, no hooks, no windows
    // above the game, low priority. It wakes by itself when the full-screen app is gone.
    private void CheckRest()
    {
        if (test) return;
        // A game has to be in front for two checks in a row, so a window that merely flashes full-screen never trips it.
        bool game=State.GameMode && fullScreen;
        fullTicks=game?fullTicks+1:0;
        bool want=manualRest || (game && fullTicks>=2);
        if (want==resting) return;
        if (want) EnterRest(); else LeaveRest();
    }

    internal void ToggleRest()
    {
        manualRest=!manualRest;
        CheckRest();
        // A tray balloon rather than a toast: Bluehour puts no window of its own on top while it rests.
        if (!test) tray.ShowBalloonTip(1500,"Bluehour",manualRest?"Resting. Ctrl + Alt + G wakes it.":"Awake.",ToolTipIcon.None);
    }

    private void EnterRest()
    {
        resting=true;
        Diag.Log("Resting.");
        workspace.Pause();
        winKey.Stop();
        dock.Rest(true);
        Toast.Dismiss();
        LayeredForm.ReleaseCanvas();
        timer.Interval=2000;
        heartbeat.Interval=1000;
        Mem.Priority(ProcessPriorityClass.BelowNormal);
        Mem.Trim();
        BeginGame();
    }

    private void LeaveRest()
    {
        resting=false;
        EndGame();
        Mem.Priority(ProcessPriorityClass.Normal);
        timer.Interval=1000;
        heartbeat.Interval=1000;
        dock.Rest(false);
        workspace.Resume();
        UpdateWinKey();
        Diag.Log("Awake again.");
    }

    internal bool WinKeyOn { get { return State.Desktop && State.WinKey; } }

    // The Windows key hook runs only while the desktop is up and the setting is on.
    internal void UpdateWinKey()
    {
        if (test) return;
        if (WinKeyOn) winKey.Start(); else winKey.Stop();
    }

    private static Native.Bar SystemTaskbar()
    {
        var handle=Native.FindWindow("Shell_TrayWnd",null);
        if (handle==IntPtr.Zero) throw new InvalidOperationException("Windows Explorer’s taskbar is unavailable.");
        return new Native.Bar {Size=Marshal.SizeOf(typeof(Native.Bar)),Handle=handle};
    }

    private void AutoHideTaskbar()
    {
        if (test) return;
        var data=SystemTaskbar();
        int existing=(int)(Native.SHAppBarMessage(4,ref data).ToUInt32()&3);
        if ((existing&1)!=0) return;
        if (State.TaskbarBefore<0)
        {
            State.TaskbarBefore=existing;
            try { recovery.Save(); }
            catch { State.TaskbarBefore=-1; throw; }
        }
        data.Parameter=new IntPtr(3);
        Native.SHAppBarMessage(10,ref data);
        if ((Native.SHAppBarMessage(4,ref data).ToUInt32()&1)==0)
            throw new InvalidOperationException("Windows did not enable taskbar auto-hide.");
    }

    // The Bluehour dock takes the taskbar's place: Windows is told to auto-hide it (which frees the screen edge for
    // windows), and then its windows are hidden so it cannot pop up over the dock.
    private void TuckTaskbar()
    {
        if (test) return;
        string problem=null;
        try { AutoHideTaskbar(); }
        catch (Exception ex) { problem=ex.Message; }
        if (State.ReplaceTaskbar)
        {
            if (!State.TaskbarHidden)
            {
                State.TaskbarHidden=true;
                try { recovery.Save(); }
                catch { State.TaskbarHidden=false; throw; }
            }
            Taskbar.Hide();
        }
        if (problem!=null) throw new InvalidOperationException(problem);
    }

    // Windows brings its taskbar back for some events (an app flashing, Explorer restarting). Put it away again.
    private void KeepTaskbarAway()
    {
        if (!test && State.Desktop && State.ReplaceTaskbar && State.TaskbarHidden) Taskbar.Hide();
    }

    private void RestoreTaskbar()
    {
        if (test) return;
        if (State.TaskbarHidden)
        {
            Taskbar.Show();
            State.TaskbarHidden=false;
            try { recovery.Save(); }
            catch (IOException) { }
        }
        if (State.TaskbarBefore<0) return;
        var data=SystemTaskbar();
        int previous=State.TaskbarBefore;
        data.Parameter=new IntPtr((previous&1)|2);
        Native.SHAppBarMessage(10,ref data);
        if ((Native.SHAppBarMessage(4,ref data).ToUInt32()&1)!=(previous&1))
            throw new InvalidOperationException("Windows did not return the taskbar to its previous setting.");
        State.TaskbarBefore=-1;
        try { recovery.Save(); }
        catch { State.TaskbarBefore=previous; throw; }
    }

    private void RefreshSurfaces()
    {
        if (test || !State.Desktop) return;
        var screens=Screen.AllScreens;
        foreach (var orphan in canvases.Where(c=>screens.All(s=>s.DeviceName!=c.Display)).ToArray())
        {
            canvases.Remove(orphan);
            orphan.Dispose();
        }
        foreach (var screen in screens)
        {
            var canvas=canvases.FirstOrDefault(c=>c.Display==screen.DeviceName);
            if (canvas==null)
            {
                canvas=new DesktopSurface(this,screen);
                canvases.Add(canvas);
                canvas.Show();
                canvas.BringDesktopForward();
            }
            if (canvas.Bounds!=screen.Bounds) canvas.Bounds=screen.Bounds;
            canvas.UpdateDisplay();
        }
    }

    private void CloseSurfaces()
    {
        foreach (var canvas in canvases) canvas.Dispose();
        canvases.Clear();
    }

    internal void RevealDesktop()
    {
        if (!State.Desktop) { StartDesktop(); return; }
        Hide();
        if (launcher!=null && !launcher.IsDisposed) launcher.Close();
        foreach (var canvas in canvases) canvas.BringDesktopForward();
    }

    private readonly List<IntPtr> parked=new List<IntPtr>();

    // The dock's "show desktop": minimizes every app window, and brings the same ones back the next time.
    internal void ShowDesktop()
    {
        if (Visible) Hide();
        if (launcher!=null && !launcher.IsDisposed) launcher.Close();
        var open=DesktopWindow.List(null,false).Where(Workspace.IsAppWindow).ToList();
        if (open.Count>0)
        {
            parked.Clear();
            foreach (var window in open) { parked.Add(window.Handle); Native.ShowWindowAsync(window.Handle,6); }   // SW_MINIMIZE
        }
        else
        {
            foreach (var handle in parked) if (Native.IsWindow(handle) && Native.IsIconic(handle)) Native.ShowWindowAsync(handle,9);   // SW_RESTORE
            parked.Clear();
        }
        foreach (var canvas in canvases) canvas.BringDesktopForward();
    }

    internal void ReturnToWindows()
    {
        State.Desktop=false;
        State.Dock=false;
        workspace.Stop();
        UpdateWinKey();
        CloseSurfaces();
        UpdateDock();
        string error="";
        try { recovery.Save(); }
        catch (Exception ex) { error="Could not save desktop preferences. "+ex.Message; }
        try { RestoreTaskbar(); }
        catch (Exception ex) { error=ex.Message; }
        int failed=layouts.Revert();
        Say(error!=""?error:failed==0?"Returned to the Windows desktop.":failed+" window positions could not be restored.");
        ShowPage("Recovery");
    }

    private void DesktopPulse()
    {
        RefreshSurfaces();
        if (!State.Desktop) return;
        var foreground=Native.GetForegroundWindow();
        var name=new StringBuilder(256);
        Native.GetClassName(foreground,name,name.Capacity);
        bool explorer=name.ToString()=="Progman" || name.ToString()=="WorkerW";
        if (explorer && !wasOnExplorerDesktop)
            foreach (var canvas in canvases) canvas.BringDesktopForward();
        wasOnExplorerDesktop=explorer;
    }

    // Tiles windows. With the desktop up this goes through the workspace (frames and glide); without it the windows
    // are simply placed.
    private void Arrange(IEnumerable<DesktopWindow> chosen,IEnumerable<DesktopWindow> all)
    {
        if (State.Desktop) { workspace.Arrange(chosen,all); return; }
        Say(layouts.Apply(Monitor,chosen.ToArray(),State.Layout));
    }

    // The part of the display windows may occupy: the work area, minus the strip the top bar floats in.
    internal Rectangle TileArea
    {
        get
        {
            var work=Monitor.WorkingArea;
            int top=(int)Math.Round(DesktopSurface.TopBar*Native.Scale(Handle));
            return Rectangle.FromLTRB(work.Left,work.Top+top,work.Right,work.Bottom);
        }
    }
}



