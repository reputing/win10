using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

// Things that keep Bluehour out of the way: noticing a game, giving memory back, and reporting its own cost.
internal static class FullScreen
{
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

    private static readonly int Own=Process.GetCurrentProcess().Id;
    private static readonly string[] Shell={"Progman","WorkerW","Shell_TrayWnd","Shell_SecondaryTrayWnd","Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow","ForegroundStaging","MultitaskingViewFrame"};

    // The window of the full-screen app in front, or Zero. Two things must agree: Windows says a full-screen app is
    // running (the question it asks before showing a notification: 2 busy, 3 Direct3D game, 4 presentation), and the
    // window in front really covers its whole display. Bluehour's own desktop covers every display, so Windows alone
    // can report "full-screen app" while a person is just using the desktop; that must never count as a game.
    internal static IntPtr Window(){int state;return Window(out state);}

    // `state` is Windows' own answer: 2 a busy full-screen app, 3 a Direct3D game, 4 presentation mode.
    internal static IntPtr Window(out int state)
    {
        state=0;
        var front=Native.GetForegroundWindow();
        if(front==IntPtr.Zero)return IntPtr.Zero;
        uint pid;
        Native.GetWindowThreadProcessId(front,out pid);
        if(pid==(uint)Own)return IntPtr.Zero;
        if(SHQueryUserNotificationState(out state)!=0)return IntPtr.Zero;
        if(state==4)return front;   // presentation mode is something the person switched on
        if(state!=2 && state!=3)return IntPtr.Zero;
        return Covers(front)?front:IntPtr.Zero;
    }

    internal static bool AppRunning { get { return Window()!=IntPtr.Zero; } }

    private static bool Covers(IntPtr window)
    {
        if(!Native.IsWindowVisible(window) || Native.IsIconic(window))return false;
        var name=new System.Text.StringBuilder(64);
        Native.GetClassName(window,name,name.Capacity);
        if(Array.IndexOf(Shell,name.ToString())>=0)return false;
        Native.Rect box;
        if(!Native.GetWindowRect(window,out box))return false;
        var screen=System.Windows.Forms.Screen.FromHandle(window).Bounds;
        return box.L<=screen.Left && box.T<=screen.Top && box.R>=screen.Right && box.B>=screen.Bottom;
    }

    // The program file behind a window, e.g. a game's .exe, or null when Windows will not say.
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process,uint flags,System.Text.StringBuilder name,ref int size);
    internal static string ProgramOf(uint pid)
    {
        var process=OpenProcess(0x1000,false,pid);   // PROCESS_QUERY_LIMITED_INFORMATION
        if(process==IntPtr.Zero)return null;
        try
        {
            var text=new System.Text.StringBuilder(520);
            int size=text.Capacity;
            return QueryFullProcessImageName(process,0,text,ref size)?text.ToString():null;
        }
        finally{CloseHandle(process);}
    }
}

internal static class Mem
{
    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr process,IntPtr minimum,IntPtr maximum);

    // Collects garbage and hands the pages Bluehour is not using straight back to Windows.
    internal static void Trim()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using(var self=Process.GetCurrentProcess())
                SetProcessWorkingSetSize(self.Handle,new IntPtr(-1),new IntPtr(-1));
        }
        catch(InvalidOperationException){}
        catch(System.ComponentModel.Win32Exception){}
    }

    internal static void Priority(ProcessPriorityClass level)
    {
        try
        {
            using(var self=Process.GetCurrentProcess())self.PriorityClass=level;
        }
        catch(InvalidOperationException){}
        catch(System.ComponentModel.Win32Exception){}
    }
}

// Bluehour's own CPU and memory, so the cost of running it is never a guess.
internal sealed class OwnStats
{
    private TimeSpan lastCpu;
    private long lastTicks;

    internal string Read()
    {
        using(var self=Process.GetCurrentProcess())
        {
            long now=Stopwatch.GetTimestamp();
            var cpu=self.TotalProcessorTime;
            double percent=0;
            if(lastTicks!=0 && now>lastTicks)
            {
                double wall=(now-lastTicks)/(double)Stopwatch.Frequency;
                percent=(cpu-lastCpu).TotalSeconds/(wall*Environment.ProcessorCount)*100;
            }
            lastCpu=cpu;lastTicks=now;
            long megabytes=self.WorkingSet64/(1024*1024);
            return "CPU "+percent.ToString("0.0")+"%    •    MEMORY "+megabytes+" MB";
        }
    }
}

// Windows power plans, through powercfg. Used only when asked, and the previous plan is always remembered.
internal static class PowerPlan
{
    internal const string HighPerformance="8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private static readonly Regex GuidPattern=new Regex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    private static string Run(string arguments,out int exit)
    {
        var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"powercfg.exe"),arguments)
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};
        using(var process=Process.Start(start))
        {
            string text=process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            exit=process.HasExited?process.ExitCode:-1;
            return text;
        }
    }

    internal static string Active()
    {
        int exit;
        var match=GuidPattern.Match(Run("/getactivescheme",out exit));
        return exit==0 && match.Success?match.Value:null;
    }

    internal static bool Activate(string guid)
    {
        if(!GuidPattern.IsMatch(guid))return false;
        int exit;
        Run("/setactive "+guid,out exit);
        return exit==0;
    }

    // Switches to the High performance plan, creating it first if this PC does not list it. Returns the plan that was
    // active before, or null if nothing could be changed.
    internal static string UseHighPerformance()
    {
        string before=Active();
        if(before==null)return null;
        if(Activate(HighPerformance))return before;
        int exit;
        var match=GuidPattern.Match(Run("/duplicatescheme "+HighPerformance,out exit));
        return exit==0 && match.Success && Activate(match.Value)?before:null;
    }
}

// The CPU and memory reading for the desktop's system widget.
internal sealed class SystemMeter
{
    private long lastIdle,lastTotal;
    internal string Read()
    {
        long idle,kernel,user;
        string cpu="–";
        if(Native.GetSystemTimes(out idle,out kernel,out user))
        {
            long total=kernel+user;
            if(lastTotal>0 && total>lastTotal)cpu=Math.Max(0,Math.Min(100,100-(int)(100*(idle-lastIdle)/(total-lastTotal))))+"%";
            lastIdle=idle;lastTotal=total;
        }
        var memory=new Native.Memory {Length=(uint)Marshal.SizeOf(typeof(Native.Memory))};
        string ram=Native.GlobalMemoryStatusEx(ref memory)?memory.Load+"%":"–";
        return "cpu "+cpu+"  ·  ram "+ram;
    }
}
