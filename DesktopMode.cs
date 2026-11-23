using System;
using System.Collections.Generic;
using System.Drawing;
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

    private void StartDesktop()
    {
        if (test) return;
        State.Desktop=true;
        recovery.Save();
        ResumeDesktop(true);
    }

    private void ResumeDesktop(bool bringForward)
    {
        if (State.AutoHideTaskbar)
        {
            try { AutoHideTaskbar(); }
            catch (Exception ex) { Say("Windows taskbar: "+ex.Message); }
        }
        UpdateDock();
        RefreshSurfaces();
        workspace.Start();
        if (bringForward) RevealDesktop();
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

    private void RestoreTaskbar()
    {
        if (test || State.TaskbarBefore<0) return;
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
                // Inserting behind the lowest active app keeps a newly connected display ready.
                var windows=DesktopWindow.List(screen,false);
                if (windows.Count>0) Native.SetWindowPos(canvas.Handle,windows.Last().Handle,0,0,0,0,0x13);
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

    internal void ReturnToWindows()
    {
        State.Desktop=false;
        State.Dock=false;
        workspace.Stop();
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



