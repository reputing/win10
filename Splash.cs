using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer=System.Windows.Forms.Timer;

// The loading screen. It appears immediately, keeps animating while the desktop is set up one step at a time, and
// fades out when everything is ready. Starting in steps (rather than in one long block) is what keeps Windows from
// deciding the app has stopped responding.
internal sealed class Splash : LayeredForm
{
    private const int CardW=440,CardH=290,Reach=30;
    private readonly Timer clock=new Timer {Interval=33};
    private readonly Stopwatch age=Stopwatch.StartNew();
    private readonly string title;
    private readonly Rectangle area;
    private readonly double scale;
    private readonly bool animated;
    private Stopwatch leaving;
    private TaskCompletionSource<bool> gone;
    private string step="waking up";
    private double goal,shown;
    private Color accent,partner;

    internal Splash(Screen screen,Color accent,Color partner,string title)
    {
        TopMost=true;
        Text="Bluehour";
        this.accent=accent;this.partner=partner;this.title=title;
        CreateHandle();
        scale=Native.Scale(Handle);
        animated=Motion.Enabled;
        int w=(int)Math.Round((CardW+2*Reach)*scale),h=(int)Math.Round((CardH+2*Reach)*scale);
        var bounds=(screen??Screen.PrimaryScreen).Bounds;
        area=new Rectangle(bounds.Left+(bounds.Width-w)/2,bounds.Top+(bounds.Height-h)/2,w,h);
        clock.Tick+=delegate{Frame();};
    }

    protected override bool PassThrough { get { return false; } }

    internal void Open()
    {
        Show();
        clock.Start();
        Frame();
    }

    internal void Recolor(Color primary,Color secondary){accent=primary;partner=secondary;}

    // Names the step that is starting and moves the bar towards `progress` (0 to 1).
    internal void Say(string text,double progress)
    {
        step=Ui.Voice(text);
        goal=Math.Max(goal,progress);
    }

    // Runs startup steps one at a time on the interface thread, yielding between them so the screen keeps moving
    // and Windows keeps seeing a responsive window.
    internal async Task Run(Shell shell,List<KeyValuePair<string,Action>> steps,double from,double to)
    {
        for(int i=0;i<steps.Count;i++)
        {
            Say(steps[i].Key,from+(to-from)*i/steps.Count);
            await Task.Delay(40);
            Diag.Mark("startup: "+steps[i].Key);
            var timer=Stopwatch.StartNew();
            shell.Run(steps[i].Value);
            if(timer.ElapsedMilliseconds>500)Diag.Log("Startup step \""+steps[i].Key+"\" took "+timer.ElapsedMilliseconds+" ms.");
        }
        Say("ready",to);
    }

    internal Task Finish()
    {
        goal=1;
        gone=new TaskCompletionSource<bool>();
        if(!animated){clock.Stop();Hide();gone.SetResult(true);return gone.Task;}
        leaving=Stopwatch.StartNew();
        return gone.Task;
    }

    private void Frame()
    {
        Diag.Beat();
        double ms=age.Elapsed.TotalMilliseconds;
        double alpha=animated?Motion.Effects.At(Motion.Progress(ms,240)):1;
        if(leaving!=null)
        {
            double t=Motion.Progress(leaving.Elapsed.TotalMilliseconds,260);
            alpha*=1-t;
            if(t>=1){clock.Stop();Hide();gone.TrySetResult(true);return;}
        }
        shown+=(goal-shown)*.14;
        Present(area,(int)Math.Round(255*alpha),delegate(Graphics g){Draw(g,ms);});
    }

    private float S(double value){return (float)(value*scale);}

    private void Draw(Graphics g,double ms)
    {
        float reach=S(Reach);
        var card=new RectangleF(reach,reach,S(CardW),S(CardH));
        float radius=S(38);
        for(float grow=reach-4;grow>=2;grow-=3)
        {
            var shade=RectangleF.Inflate(card,grow,grow);shade.Y+=S(9);
            Style.Fill(g,shade,radius+grow,Color.FromArgb(7,6,0,16));
            Style.Fill(g,RectangleF.Inflate(card,grow,grow),radius+grow,Color.FromArgb(4,accent));
        }
        Style.Fill(g,new RectangleF(card.X,card.Y+S(7),card.Width,card.Height),radius,Color.FromArgb(245,Ui.EdgeDeep));
        using(var path=Style.Round(card,radius))
        {
            using(var body=new LinearGradientBrush(card,Ui.SurfaceHigh,Ui.Surface,90f))g.FillPath(body,path);
        }
        using(var outline=Style.Round(RectangleF.Inflate(card,-S(1.5),-S(1.5)),radius-S(1.5)))
        using(var pen=new Pen(Ui.Edge,S(3)))g.DrawPath(pen,outline);

        // the three pastel dots
        var dots=new[]{WindowFrame.Pink,WindowFrame.Lavender,WindowFrame.Cream};
        for(int i=0;i<3;i++)
            using(var brush=new SolidBrush(dots[i]))g.FillEllipse(brush,card.X+S(30)+i*S(21),card.Y+S(26),S(13),S(13));

        // the mascot bobs; its shadow stays put
        float cx=card.X+card.Width/2,cy=card.Y+S(112);
        float bob=(float)(animated?Math.Sin(ms/380)*S(4):0),d=S(96);
        using(var brush=new SolidBrush(Color.FromArgb(70,0,0,0)))
            g.FillEllipse(brush,cx-d*.38f,cy+d/2+S(8),d*.76f,S(9));
        Style.Fill(g,new RectangleF(cx-d/2,cy-d/2+bob+S(6),d,d),d/2,Color.FromArgb(245,Style.Mix(accent,Ui.SurfaceLowest,.62)));
        Style.Fill(g,new RectangleF(cx-d/2,cy-d/2+bob,d,d),d/2,accent);
        Style.Icon(g,"Launch",new RectangleF(cx-S(29),cy-S(29)+bob,S(58),S(58)),Ui.Night);
        if(animated)
            for(int i=0;i<3;i++)
            {
                double angle=ms/1500+i*2.094;
                float sx=cx+(float)Math.Cos(angle)*d*.8f,sy=cy+bob+(float)Math.Sin(angle)*d*.56f;
                float size=S(5+3*Math.Sin(ms/260+i*1.7));
                Style.DrawSparkle(g,sx,sy,Math.Max(2,size),Color.FromArgb(225,i%2==0?accent:partner));
            }

        using(var face=new Font(ShellFonts.Display,S(30),FontStyle.Regular,GraphicsUnit.Pixel))
        using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
        {
            format.Alignment=StringAlignment.Center;
            Style.ShadowText(g,title,face,Ui.Text,Ui.EdgeDeep,new PointF(cx,card.Y+S(176)),S(2.5),format);
        }
        using(var small=new Font(Style.Family,S(14),FontStyle.Bold,GraphicsUnit.Pixel))
        using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
        {
            format.Alignment=StringAlignment.Center;
            using(var brush=new SolidBrush(Ui.Muted))g.DrawString(step+"…",small,brush,cx,card.Y+S(214),format);
            if(ms>7000)
                using(var note=new Font(Style.Family,S(12),FontStyle.Regular,GraphicsUnit.Pixel))
                using(var brush=new SolidBrush(Ui.Faint))
                    g.DrawString("still working. a big wallpaper or a busy pc takes longer",note,brush,cx,card.Y+S(262),format);
        }

        // progress pill with a moving shimmer and a sparkle at its tip
        float tw=S(300),th=S(16),tx=cx-tw/2,ty=card.Y+S(238);
        Style.Fill(g,new RectangleF(tx,ty+S(3),tw,th),th/2,Color.FromArgb(235,Ui.EdgeDeep));
        var track=new RectangleF(tx,ty,tw,th);
        Style.Fill(g,track,th/2,Ui.Raised);
        float fillW=Math.Max(th,(float)(tw*Math.Min(1,shown)));
        var fill=new RectangleF(tx,ty,fillW,th);
        using(var path=Style.Round(fill,th/2))
        {
            using(var brush=new LinearGradientBrush(new RectangleF(tx,ty,Math.Max(2,fillW),th),accent,partner,0f))g.FillPath(brush,path);
            if(animated)
            {
                var saved=g.Save();
                g.SetClip(path);
                float band=S(46),offset=(float)((ms/7)%(fillW+band))-band;
                using(var shimmer=new SolidBrush(Color.FromArgb(55,255,255,255)))
                    g.FillRectangle(shimmer,tx+offset,ty,band,th);
                g.Restore(saved);
            }
        }
        using(var outline=Style.Round(RectangleF.Inflate(track,-S(1),-S(1)),th/2-S(1)))
        using(var pen=new Pen(Ui.Edge,S(2)))g.DrawPath(pen,outline);
        Style.DrawSparkle(g,tx+fillW-th/2,ty+th/2,S(7)+(float)(animated?Math.Sin(ms/170)*S(1.5):0),Color.White);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)clock.Dispose();
        base.Dispose(disposing);
    }
}
