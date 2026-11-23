using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Timer=System.Windows.Forms.Timer;

// A switch in place of the stock checkbox, shaped like the toggles on kuro.rip: a pill track and a knob that springs across.
internal sealed class KuroSwitch : Control
{
    private readonly Timer animation=new Timer {Interval=15};
    private bool on,over,down;
    private float knob;

    internal KuroSwitch(string label,bool value)
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.Selectable,true);
        Text=label;on=value;knob=value?1:0;
        Font=new Font(Style.Family,10);ForeColor=Ui.Text;Cursor=Cursors.Hand;TabStop=true;
        AccessibleRole=AccessibleRole.CheckButton;AccessibleName=label;
        Margin=new Padding(0,5,18,6);
        Size=Measure();
        animation.Tick+=delegate{Step();};
    }

    internal event EventHandler CheckedChanged;
    internal bool Checked
    {
        get { return on; }
        set { if(on==value)return; on=value; Wake(); Invalidate(); if(CheckedChanged!=null)CheckedChanged(this,EventArgs.Empty); }
    }

    private Size Measure(){return new Size(60+TextRenderer.MeasureText(Text,Font).Width+8,32);}

    private void Wake()
    {
        if(Motion.Enabled){if(!animation.Enabled)animation.Start();}
        else{knob=on?1:0;Invalidate();}
    }

    // The knob overshoots a little before it settles, like kuro's spring easing.
    private float velocity;
    private void Step()
    {
        float goal=on?1f:0f;
        velocity+=(goal-knob)*.22f;velocity*=.6f;knob+=velocity;
        if(Math.Abs(goal-knob)<.003f&&Math.Abs(velocity)<.003f){knob=goal;velocity=0;animation.Stop();}
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);over=true;Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);over=false;down=false;Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){down=true;Focus();Invalidate();}}
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        bool fire=down&&over&&e.Button==MouseButtons.Left;
        down=false;
        if(fire)Checked=!Checked;else Invalidate();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter){e.Handled=true;Checked=!Checked;}
    }
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Size=Measure();Invalidate();}

    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        g.Clear(Parent==null?Ui.Night:Parent.BackColor.A==255?Parent.BackColor:Ui.Night);
        var accent=Ui.Accent("Lavender");
        var track=new RectangleF(1,3,50,26);
        float t=Math.Max(0,Math.Min(1,knob));
        Style.Fill(g,new RectangleF(track.X,track.Y+2.5f,track.Width,track.Height),13,Color.FromArgb(235,Ui.EdgeDeep));
        Style.Fill(g,track,13,Style.Mix(Ui.Raised,accent,t));
        using(var outline=Style.Round(RectangleF.Inflate(track,-1,-1),12))
        using(var pen=new Pen(Style.Mix(Ui.Edge,accent,t),2f))g.DrawPath(pen,outline);
        float d=down?22:20;
        float x=track.X+5+knob*(track.Width-10-20)-(d-20)/2;
        Style.Fill(g,new RectangleF(x,track.Y+(track.Height-d)/2+1.5f,d,d),d/2,Color.FromArgb(120,Ui.EdgeDeep));
        Style.Fill(g,new RectangleF(x,track.Y+(track.Height-d)/2,d,d),d/2,Style.Mix(Ui.Muted,Ui.Night,t));
        if(t>.5f)Style.DrawSparkle(g,x+d/2,track.Y+track.Height/2,5.2f,accent);
        TextRenderer.DrawText(g,Text,Font,new Rectangle(62,0,Width-62,Height),ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
        if(Focused&&ShowFocusCues)
            using(var ring=Style.Round(RectangleF.Inflate(track,3,3),16))
            using(var pen=new Pen(accent,2f))g.DrawPath(pen,ring);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)animation.Dispose();
        base.Dispose(disposing);
    }
}

// A rounded field that holds a text box, with a search glyph at the left.
internal sealed class SearchField : Panel
{
    internal readonly TextBox Input;
    internal SearchField(string hint)
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint,true);
        BackColor=Ui.Night;Padding=new Padding(48,14,22,8);Height=52;
        Input=new TextBox {Dock=DockStyle.Fill,BackColor=Ui.Surface,ForeColor=Ui.Text,BorderStyle=BorderStyle.None,
            Font=new Font(Style.Family,11.5f),AccessibleName=hint};
        Input.HandleCreated+=delegate{SetHint(Input.Handle,0x1501,new IntPtr(1),hint);};
        Input.GotFocus+=delegate{Invalidate();};
        Input.LostFocus+=delegate{Invalidate();};
        Controls.Add(Input);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SendMessageW",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SetHint(IntPtr handle,int message,IntPtr param,string text);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        g.Clear(Parent==null?Ui.Night:Parent.BackColor);
        var body=new RectangleF(1,1,Width-2,Height-6);
        Style.Fill(g,new RectangleF(1,4,Width-2,Height-6),body.Height/2,Color.FromArgb(235,Ui.EdgeDeep));
        Style.Fill(g,body,body.Height/2,Ui.Surface);
        using(var outline=Style.Round(RectangleF.Inflate(body,-1,-1),body.Height/2-1))
        using(var pen=new Pen(Input.Focused?Ui.Accent("Lavender"):Ui.Edge,2f))g.DrawPath(pen,outline);
        Style.Icon(g,"Search",new RectangleF(18,(Height-6-20)/2f+1,20,20),Input.Focused?Ui.Accent("Lavender"):Ui.Muted);
    }
}

// The calendar: a month grid with a round "today" marker and springy month buttons.
internal sealed class CalendarView : Form
{
    private readonly Shell shell;
    private DateTime month=new DateTime(DateTime.Today.Year,DateTime.Today.Month,1);
    private DateTime? picked;
    private Point mouse=new Point(-1,-1);
    private const int Cell=46,Row=40,Pad=22;

    internal CalendarView(Shell shell)
    {
        this.shell=shell;
        Text="Calendar";StartPosition=FormStartPosition.CenterScreen;ShowInTaskbar=false;TopMost=true;
        BackColor=Ui.Night;ForeColor=Ui.Text;Font=new Font(Style.Family,10,FontStyle.Bold);
        DoubleBuffered=true;KeyPreview=true;
        ClientSize=new Size(Cell*7+Pad*2+10,WindowFrame.BarHeight+Pad+64+34+Row*6+Pad+10);
        WindowFrame.Attach(this,"calendar",false);
        KeyDown+=delegate(object sender,KeyEventArgs e)
        {
            if(e.KeyCode==Keys.Escape)Close();
            else if(e.KeyCode==Keys.Left||e.KeyCode==Keys.PageUp){Shift(-1);e.Handled=true;}
            else if(e.KeyCode==Keys.Right||e.KeyCode==Keys.PageDown){Shift(1);e.Handled=true;}
        };
    }

    private void Shift(int months){month=month.AddMonths(months);Invalidate();}
    private RectangleF Previous(){return new RectangleF(ClientSize.Width-Pad-5-2*44-8,WindowFrame.BarHeight+Pad+8,44,44);}
    private RectangleF Next(){return new RectangleF(ClientSize.Width-Pad-5-44,WindowFrame.BarHeight+Pad+8,44,44);}
    private RectangleF Grid(int index)
    {
        var first=new DateTime(month.Year,month.Month,1);
        int offset=(int)first.DayOfWeek;   // weeks start on Sunday
        int slot=index+offset;
        return new RectangleF(Pad+5+(slot%7)*Cell,WindowFrame.BarHeight+Pad+64+34+(slot/7)*Row,Cell,Row);
    }

    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);mouse=e.Location;Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);mouse=new Point(-1,-1);Invalidate();}
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if(Previous().Contains(e.Location)){Shift(-1);return;}
        if(Next().Contains(e.Location)){Shift(1);return;}
        for(int d=0;d<DateTime.DaysInMonth(month.Year,month.Month);d++)
            if(Grid(d).Contains(e.Location)){picked=new DateTime(month.Year,month.Month,d+1);Invalidate();return;}
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAlias;
        var accent=shell.Accent;
        using(var head=new Font(ShellFonts.Display,28,FontStyle.Regular,GraphicsUnit.Pixel))
        using(var format=new StringFormat(StringFormat.GenericTypographic){LineAlignment=StringAlignment.Center})
        {
            var title=month.ToString("MMMM yyyy").ToLowerInvariant();
            Style.ShadowText(g,title,head,Ui.Text,Ui.EdgeDeep,new PointF(Pad+5,WindowFrame.BarHeight+Pad+18),2.5f,format);
        }
        foreach(var arrow in new[]{Previous(),Next()})
        {
            bool hot=arrow.Contains(mouse);
            Style.Fill(g,new RectangleF(arrow.X,arrow.Y+3,arrow.Width,arrow.Height-4),(arrow.Height-4)/2,Color.FromArgb(235,Ui.EdgeDeep));
            Style.Fill(g,new RectangleF(arrow.X,arrow.Y-(hot?1:0),arrow.Width,arrow.Height-4),(arrow.Height-4)/2,hot?Ui.Raised:Ui.Surface);
        }
        // chevrons: left and right
        using(var pen=new Pen(Ui.Text,2.2f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
        {
            var p=Previous();var n=Next();
            float cy=p.Y+(p.Height-4)/2;
            g.DrawLines(pen,new[]{new PointF(p.X+p.Width/2+3,cy-6),new PointF(p.X+p.Width/2-3,cy),new PointF(p.X+p.Width/2+3,cy+6)});
            g.DrawLines(pen,new[]{new PointF(n.X+n.Width/2-3,cy-6),new PointF(n.X+n.Width/2+3,cy),new PointF(n.X+n.Width/2-3,cy+6)});
        }
        string[] days={"su","mo","tu","we","th","fr","sa"};
        using(var small=new Font(Style.Family,12,FontStyle.Bold,GraphicsUnit.Pixel))
            for(int i=0;i<7;i++)
                TextRenderer.DrawText(g,days[i],small,new Rectangle(Pad+5+i*Cell,WindowFrame.BarHeight+Pad+64,Cell,28),Ui.Faint,
                    TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        int count=DateTime.DaysInMonth(month.Year,month.Month);
        for(int d=0;d<count;d++)
        {
            var cell=Grid(d);
            var date=new DateTime(month.Year,month.Month,d+1);
            bool today=date==DateTime.Today,chosen=picked.HasValue&&picked.Value==date,hot=cell.Contains(mouse);
            var disc=new RectangleF(cell.X+(cell.Width-Row+4)/2,cell.Y+2,Row-4,Row-4);
            if(today)
            {
                Style.Fill(g,new RectangleF(disc.X,disc.Y+3,disc.Width,disc.Height),disc.Height/2,Color.FromArgb(235,Style.Mix(accent,Ui.SurfaceLowest,.6)));
                Style.Fill(g,disc,disc.Height/2,accent);
            }
            else if(chosen||hot)
            {
                Style.Fill(g,disc,disc.Height/2,chosen?Ui.AccentContainer(shell.AccentName):Ui.Surface);
                if(chosen)using(var outline=Style.Round(RectangleF.Inflate(disc,-1,-1),disc.Height/2-1))using(var pen=new Pen(accent,2f))g.DrawPath(pen,outline);
            }
            TextRenderer.DrawText(g,(d+1).ToString(),Font,Rectangle.Round(cell),today?Ui.Night:Ui.Text,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        }
    }
}
