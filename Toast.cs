using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Timer=System.Windows.Forms.Timer;

// Short messages on the desktop. Launch and recovery messages used to land only in the control center's footer,
// which is hidden while the desktop is in use, so a failed launch looked like nothing happening at all.
// Motion follows the site's cl-fade-up: 8px rise and fade over .2s.
internal sealed class Toast : LayeredForm
{
    private const int Shadow=18;
    private static Toast current;
    private readonly Timer clock=new Timer {Interval=15};
    private readonly Stopwatch age=new Stopwatch();
    private string message="";
    private bool failed,animated=true;
    private Color accent;
    private Point origin;
    private Size pill;
    private int holdMs;
    private double scale=1;
    private Font font;

    private Toast()
    {
        TopMost=true;
        clock.Tick+=delegate{Step();};
    }

    internal static void Post(Screen screen,string text,bool error,Color accent)
    {
        if(String.IsNullOrWhiteSpace(text))return;
        if(current==null || current.IsDisposed)current=new Toast();
        current.Display(screen,text,error,accent);
    }

    internal static void Dismiss()
    {
        if(current!=null && !current.IsDisposed)current.Dispose();
        current=null;
    }

    private void Display(Screen screen,string text,bool error,Color color)
    {
        message=text.Replace(Environment.NewLine,"  •  ");failed=error;accent=color;
        if(!IsHandleCreated)CreateHandle();
        scale=Native.Scale(Handle);
        if(font!=null)font.Dispose();
        font=new Font(Style.Family,(float)(14*scale),FontStyle.Regular,GraphicsUnit.Pixel);
        SizeF measured;
        using(var probe=new Bitmap(1,1))
        using(var g=Graphics.FromImage(probe))
        using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
            measured=g.MeasureString(message,font,PointF.Empty,format);
        int maxText=(int)Math.Min(560*scale,screen.WorkingArea.Width*.7);
        int textWidth=(int)Math.Min(maxText,Math.Ceiling(measured.Width)+2);
        pill=new Size((int)(textWidth+52*scale),(int)Math.Round(44*scale));
        var work=screen.WorkingArea;
        origin=new Point(work.Left+(work.Width-pill.Width)/2,work.Bottom-(int)Math.Round(30*scale)-pill.Height);
        animated=Motion.Enabled;
        holdMs=failed?6500:3200;
        if(!Visible)Show();
        age.Restart();
        clock.Start();
        Step();
    }

    private void Step()
    {
        double ms=age.Elapsed.TotalMilliseconds;
        int enterMs=animated?Motion.ToastIn:0,exitMs=animated?150:0,exitAt=enterMs+holdMs;
        if(ms>=exitAt+exitMs){clock.Stop();age.Reset();Hide();return;}
        double enter=Motion.Effects.At(Motion.Progress(ms,enterMs));
        double leave=ms>exitAt?Motion.Progress(ms-exitAt,exitMs):0;
        int lift=(int)Math.Round(8*scale*(1-enter));
        int margin=(int)Math.Round(Shadow*scale);
        var bounds=new Rectangle(origin.X-margin,origin.Y-margin+lift,pill.Width+2*margin,pill.Height+2*margin);
        Present(bounds,(int)Math.Round(255*enter*(1-leave)),delegate(Graphics g){Draw(g,margin);});
    }

    private void Draw(Graphics g,int margin)
    {
        float drop=(float)(4*scale);
        float step=Math.Max(1,margin/9f);
        for(float grow=margin*.6f;grow>=1;grow-=step)
        {
            var shade=new RectangleF(margin-grow,margin-grow+drop*2,pill.Width+2*grow,pill.Height+2*grow);
            Style.Fill(g,shade,shade.Height/2f,Color.FromArgb(7,6,0,16));
        }
        // Hard offset shadow, then the body: the same sticker treatment as kuro's windows.
        Style.Fill(g,new RectangleF(margin,margin+drop,pill.Width,pill.Height),pill.Height/2f,Color.FromArgb(240,Ui.EdgeDeep));
        var body=new RectangleF(margin,margin,pill.Width,pill.Height);
        Style.Fill(g,body,body.Height/2f,Ui.Surface);
        using(var outline=Style.Round(new RectangleF(margin+1,margin+1,pill.Width-2,pill.Height-2),(pill.Height-2)/2f))
        using(var pen=new Pen(failed?Ui.Error:Ui.Edge,2f))g.DrawPath(pen,outline);
        float dot=(float)(10*scale);
        using(var brush=new SolidBrush(failed?Ui.Error:accent))
            g.FillEllipse(brush,margin+(float)(20*scale),margin+(pill.Height-dot)/2f,dot,dot);
        using(var brush=new SolidBrush(Ui.Text))
        using(var format=new StringFormat {LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})
            g.DrawString(message,font,brush,new RectangleF(margin+(float)(40*scale),margin,pill.Width-(float)(58*scale),pill.Height),format);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing){clock.Dispose();if(font!=null)font.Dispose();}
        base.Dispose(disposing);
    }
}
