using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Timer=System.Windows.Forms.Timer;

// The top-centre layout switcher: five little glyphs in a pill with a sliding highlight. It replaces a dropdown menu,
// so changing how windows are arranged is one click that you can watch happen.
internal sealed class LayoutPill : Control
{
    private const int Segment=50,Height0=72;
    private readonly Shell shell;
    private readonly Timer animation=new Timer {Interval=15};
    private float slider,velocity;
    private int hot=-1,held=-1,target;

    internal LayoutPill(Shell shell)
    {
        this.shell=shell;
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|
            ControlStyles.SupportsTransparentBackColor|ControlStyles.Selectable,true);
        BackColor=Color.Transparent;
        Size=new Size(Segment*Shell.Modes.Length+18,Height0);
        Cursor=Cursors.Hand;Font=new Font(Style.Family,9,FontStyle.Bold);
        AccessibleName="Window layout";AccessibleRole=AccessibleRole.ToolBar;
        target=Math.Max(0,Array.IndexOf(Shell.Modes,shell.ActiveLayout));
        slider=target;
        animation.Tick+=delegate{Step();};
    }

    // Called by the surface every second and after a click: follow the active layout.
    internal void Sync()
    {
        int wanted=Math.Max(0,Array.IndexOf(Shell.Modes,shell.ActiveLayout));
        if(wanted==target)return;
        target=wanted;
        Wake();
    }

    private void Wake()
    {
        if(Motion.Enabled){if(!animation.Enabled)animation.Start();}
        else{slider=target;Invalidate();}
    }

    // A lightly damped spring, so the highlight settles with a little overshoot like kuro's buttons.
    private void Step()
    {
        velocity+=(target-slider)*.2f;
        velocity*=.62f;
        slider+=velocity;
        if(Math.Abs(target-slider)<.002f&&Math.Abs(velocity)<.002f){slider=target;velocity=0;animation.Stop();}
        Invalidate();
    }

    private int Hit(Point point)
    {
        var track=Track();
        if(!track.Contains(point))return -1;
        return Math.Max(0,Math.Min(Shell.Modes.Length-1,(int)((point.X-track.X)/Segment)));
    }
    private RectangleF Track(){return new RectangleF(9,4,Segment*Shell.Modes.Length,40);}

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index=Hit(e.Location);
        if(index!=hot){hot=index;Invalidate();}
    }
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=-1;Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if(e.Button!=MouseButtons.Left)return;
        held=Hit(e.Location);Capture=true;Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int pressed=held;
        held=-1;Capture=false;Invalidate();
        if(pressed>=0&&Hit(e.Location)==pressed)
        {
            string mode=Shell.Modes[pressed];
            shell.Run(()=>shell.ChooseLayout(mode));
            Sync();
        }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int step=e.KeyCode==Keys.Left?-1:e.KeyCode==Keys.Right?1:0;
        if(step==0)return;
        e.Handled=true;
        string mode=Shell.Modes[Math.Max(0,Math.Min(Shell.Modes.Length-1,target+step))];
        shell.Run(()=>shell.ChooseLayout(mode));
        Sync();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var surface=Parent as DesktopSurface;
        if(surface!=null)surface.PaintBackdropFor(this,e.Graphics);
        else base.OnPaintBackground(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAlias;
        var track=Track();
        var accent=shell.Accent;
        Style.Fill(g,new RectangleF(track.X,track.Y+4,track.Width,track.Height),track.Height/2,Color.FromArgb(245,Ui.EdgeDeep));
        Style.Fill(g,track,track.Height/2,Ui.Surface);
        using(var outline=Style.Round(RectangleF.Inflate(track,-1,-1),track.Height/2-1))
        using(var pen=new Pen(Ui.Edge,2f))g.DrawPath(pen,outline);
        // The sliding highlight.
        var knob=new RectangleF(track.X+4+slider*Segment,track.Y+4,Segment-8,track.Height-8);
        Style.Fill(g,new RectangleF(knob.X,knob.Y+2.5f,knob.Width,knob.Height),knob.Height/2,Color.FromArgb(230,Style.Mix(accent,Ui.SurfaceLowest,.6)));
        Style.Fill(g,knob,knob.Height/2,accent);
        for(int i=0;i<Shell.Modes.Length;i++)
        {
            float closeness=Math.Max(0,1-Math.Abs(slider-i));      // how much the highlight is under this glyph
            float rise=i==hot?1.5f:0,squash=i==held?.88f:1f;
            float size=21*squash;
            float cx=track.X+i*Segment+Segment/2f,cy=track.Y+track.Height/2f-rise;
            Color ink=Style.Mix(i==hot?Ui.Text:Ui.Muted,Ui.Night,closeness);
            Style.Icon(g,Shell.Modes[i],new RectangleF(cx-size/2,cy-size/2,size,size),ink);
        }
        // Caption: the layout under the pointer, otherwise the active one.
        string caption=Ui.Voice(hot>=0?Shell.Modes[hot]:Shell.Modes[target]);
        using(var font=new Font(Style.Family,12,FontStyle.Bold,GraphicsUnit.Pixel))
        using(var format=new StringFormat(StringFormat.GenericTypographic){Alignment=StringAlignment.Center})
        {
            float y=track.Bottom+10;
            var measured=g.MeasureString(caption,font,PointF.Empty,format);
            var backing=new RectangleF(Width/2f-measured.Width/2-12,y-3,measured.Width+24,19);
            Style.Fill(g,backing,9.5f,Color.FromArgb(205,Ui.Night));
            using(var brush=new SolidBrush(hot>=0?Ui.Text:Ui.Muted))
                g.DrawString(caption,font,brush,Width/2f,y,format);
            Style.DrawSparkle(g,backing.X-8,y+6.5f,4.2f,Color.FromArgb(220,Ui.Tertiary(shell.AccentName)));
            Style.DrawSparkle(g,backing.Right+8,y+6.5f,4.2f,Color.FromArgb(220,accent));
        }
        if(Focused&&ShowFocusCues)
            using(var ring=Style.Round(RectangleF.Inflate(track,3,3),track.Height/2+3))
            using(var pen=new Pen(accent,2f))g.DrawPath(pen,ring);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)animation.Dispose();
        base.Dispose(disposing);
    }
}
