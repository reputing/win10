using System;
using System.Drawing;
using System.Runtime.InteropServices;

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

    internal const int Glide=340;       // .cl-win: left/top/width/height .34s
    internal const int PopIn=300;       // cl-pop-in .3s
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
