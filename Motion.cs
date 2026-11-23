using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Motion language copied from Ivy v2's stylesheet (--e-* easing curves, --d-* durations and the
// .cl-win / cl-pop-in rules), so windows move and appear exactly like they do on the site.
internal static class Motion
{
    internal sealed class Curve
    {
        private readonly double x1,y1,x2,y2;
        internal Curve(double x1,double y1,double x2,double y2){this.x1=x1;this.y1=y1;this.x2=x2;this.y2=y2;}
        private static double Axis(double u,double a,double b)
        {
            double v=1-u;
            return 3*v*v*u*a+3*v*u*u*b+u*u*u;
        }
        // CSS cubic-bezier(): solve x(u)=t by bisection (x is monotonic for these curves), then read y(u).
        internal double At(double t)
        {
            if(t<=0)return 0;
            if(t>=1)return 1;
            double lo=0,hi=1,u=t;
            for(int i=0;i<28;i++)
            {
                double x=Axis(u,x1,x2);
                if(Math.Abs(x-t)<0.00001)break;
                if(x<t)lo=u;else hi=u;
                u=(lo+hi)/2;
            }
            return Axis(u,y1,y2);
        }
    }

    internal static readonly Curve Decel=new Curve(.05,.7,.1,1);       // --e-emphasized-decel, used by .cl-win geometry
    internal static readonly Curve Standard=new Curve(.2,0,0,1);       // --e-standard
    internal static readonly Curve Effects=new Curve(.34,.8,.34,1);    // --e-effects
    internal static readonly Curve Spatial=new Curve(.38,1.21,.22,1);  // --e-spatial (slight overshoot)

    // How windows move into their tiles: "Smooth" is Ivy's 0.34 s glide, "Quick" is shorter and asks the apps to redraw
    // fewer times (resizing a heavy app every frame is the costly part), "Off" snaps.
    internal static string Speed="Quick";
    internal static int Glide { get { return Speed=="Smooth"?340:Speed=="Off"?0:190; } }      // .cl-win: left/top/width/height .34s
    internal static int PopIn { get { return Speed=="Smooth"?300:Speed=="Off"?0:170; } }      // cl-pop-in .3s
    internal const int Recolor=200;     // .cl-win border-color .2s
    internal const int ToastIn=200;     // cl-fade-up
    internal const double PopScale=.94; // cl-pop-in starts at scale(.94)

    [DllImport("user32.dll",EntryPoint="SystemParametersInfoW")]
    private static extern bool ReadSetting(uint action,uint param,out int value,uint flags);

    // "Show animations in Windows". When it is off every transition snaps, like prefers-reduced-motion on the site.
    // Bluehour's own Animations setting; the Windows setting below is always respected as well.
    internal static bool Allowed=true;

    internal static bool Enabled
    {
        get
        {
            if(!Allowed)return false;
            int on;
            return !ReadSetting(0x1042,0,out on,0) || on!=0;
        }
    }

    internal static int Lerp(int from,int to,double t){return from+(int)Math.Round((to-from)*t);}
    internal static Rectangle Lerp(Rectangle from,Rectangle to,double t)
    {
        return Rectangle.FromLTRB(Lerp(from.Left,to.Left,t),Lerp(from.Top,to.Top,t),Lerp(from.Right,to.Right,t),Lerp(from.Bottom,to.Bottom,t));
    }
    internal static Color Lerp(Color from,Color to,double t)
    {
        return Color.FromArgb(Lerp(from.A,to.A,t),Lerp(from.R,to.R,t),Lerp(from.G,to.G,t),Lerp(from.B,to.B,t));
    }
    // Progress 0..1 of a transition that started `elapsedMs` ago.
    internal static double Progress(double elapsedMs,int durationMs)
    {
        return durationMs<=0?1:Math.Max(0,Math.Min(1,elapsedMs/durationMs));
    }
}

// Calls `tick` on the interface thread once per display refresh, for as long as it is running. A Windows timer ticks at
// about 64 Hz at best and drifts, which shows as judder on a 120 or 144 Hz monitor; this waits for the desktop
// compositor's own frame (DwmFlush), so each step lands in its own refresh.
internal sealed class FramePump
{
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    private readonly Control owner;
    private readonly Action tick;
    private int generation,pending;
    private volatile bool running;

    internal FramePump(Control owner,Action tick){this.owner=owner;this.tick=tick;}
    internal bool Running { get { return running; } }

    internal void Start()
    {
        if(running)return;
        running=true;
        int mine=Interlocked.Increment(ref generation);
        var thread=new Thread(delegate(){Loop(mine);}){IsBackground=true,Name="Bluehour frames"};
        thread.Start();
    }

    // The thread ends at the next refresh.
    internal void Stop(){running=false;}

    private void Loop(int mine)
    {
        while(running && mine==Volatile.Read(ref generation))
        {
            long began=Stopwatch.GetTimestamp();
            try{DwmFlush();}
            catch(Exception){Thread.Sleep(8);}
            // With composition off DwmFlush returns at once; do not spin.
            if(Stopwatch.GetTimestamp()-began<Stopwatch.Frequency/2000)Thread.Sleep(4);
            if(!running || mine!=Volatile.Read(ref generation))break;
            // One step at a time: if the interface thread is still busy with the last one, skip this refresh.
            if(Interlocked.Exchange(ref pending,1)!=0)continue;
            try
            {
                owner.BeginInvoke((Action)delegate
                {
                    try{if(running)tick();}
                    finally{Interlocked.Exchange(ref pending,0);}
                });
            }
            catch(InvalidOperationException){running=false;Interlocked.Exchange(ref pending,0);}
        }
    }
}

// One shared clock for every animation in the interface. Each button, switch and sticker used to run its own 15 ms
// timer (a Windows timer, which ticks at about 64 Hz at best and drifts); now they all register a step here, and a single
// pump calls them once per display refresh, with the time that passed. It stops itself when nothing is animating, so
// an idle interface does no work at all.
internal static class Anim
{
    private static readonly System.Collections.Generic.List<Func<double,bool>> steps=new System.Collections.Generic.List<Func<double,bool>>();
    private static Control owner;
    private static FramePump pump;
    private static long last;

    // Registers a step: it gets the seconds since the last one and returns true while it still has work to do.
    internal static void Add(Func<double,bool> step)
    {
        if(!steps.Contains(step))steps.Add(step);
        if(owner==null){owner=new Control();var handle=owner.Handle;}   // somewhere to hand the work to the interface thread
        if(pump==null)pump=new FramePump(owner,Tick);
        if(!pump.Running){last=Stopwatch.GetTimestamp();pump.Start();}
    }

    internal static void Remove(Func<double,bool> step){steps.Remove(step);}

    private static void Tick()
    {
        long now=Stopwatch.GetTimestamp();
        double dt=Math.Min(.05,(now-last)/(double)Stopwatch.Frequency);
        last=now;
        for(int i=steps.Count-1;i>=0;i--)
        {
            bool more;
            try{more=steps[i](dt);}
            catch(Exception){more=false;}   // one broken animation must not stop the others
            if(!more && i<steps.Count)steps.RemoveAt(i);
        }
        if(steps.Count==0)pump.Stop();
    }

    // Moves `value` towards `goal` the same way at any frame rate. `rate` is the fraction covered per frame at 60 Hz.
    internal static float Follow(float value,float goal,float rate,double dt)
    {
        float k=1-(float)Math.Pow(1-rate,dt*60);
        return value+(goal-value)*k;
    }
}
