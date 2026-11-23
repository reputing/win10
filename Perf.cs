using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

// Things that keep Bluehour out of the way: noticing a game, giving memory back, and reporting its own cost.
internal static class FullScreen
{
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

    // The same question Windows asks before it shows a notification: is a full-screen app (a game, a video, a
    // presentation) in front? 2 = busy full-screen app, 3 = Direct3D full-screen game, 4 = presentation mode.
    private static readonly int Own=Process.GetCurrentProcess().Id;

    internal static bool AppRunning
    {
        get
        {
            // Bluehour's own desktop covers the whole screen, so Windows can report "full-screen app" while one of its
            // windows is in front. That is the person using Bluehour, not a game.
            uint front;
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out front);
            if(front==(uint)Own)return false;
            int state;
            return SHQueryUserNotificationState(out state)==0 && (state==2 || state==3 || state==4);
        }
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
