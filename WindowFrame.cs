using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Chrome for Bluehour's own windows, after kuro.rip's: a plum edge, rounded corners, and three pastel dots in place
// of the Windows caption buttons (pink closes, lavender minimizes, cream maximizes). The dots show their glyph on hover.
internal static class WindowFrame
{
    internal const int BarHeight=46,Corner=26;
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);

    internal static readonly Color Pink=Color.FromArgb(255,159,210),Lavender=Color.FromArgb(217,184,245),Cream=Color.FromArgb(255,230,241);

    internal static void Attach(Form form,string title,bool resize)
    {
        form.FormBorderStyle=FormBorderStyle.None;
        form.Padding=new Padding(resize?6:5);
        var bar=new TitleBar(form,title,resize){Dock=DockStyle.Top,Height=BarHeight};
        form.Controls.Add(bar);
        bar.SendToBack();
        form.Paint+=delegate(object sender,PaintEventArgs e){Ring(form,e.Graphics);};
        form.Resize+=delegate{Shape(form);};
        form.HandleCreated+=delegate{Shape(form);};
        form.Activated+=delegate{form.Invalidate();bar.Invalidate();};
        form.Deactivate+=delegate{form.Invalidate();bar.Invalidate();};
        Shape(form);
    }

    private static void Shape(Form form)
    {
        if(form.Width<2||form.Height<2)return;
        var old=form.Region;
        if(form.WindowState==FormWindowState.Maximized)form.Region=null;
        else using(var shape=Style.Round(new RectangleF(0,0,form.Width,form.Height),Corner))form.Region=new Region(shape);
        if(old!=null)old.Dispose();
        form.Invalidate();
    }

    private static void Ring(Form form,Graphics g)
    {
        if(form.WindowState==FormWindowState.Maximized)return;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        bool active=Form.ActiveForm==form;
        var color=active?Style.Mix(Ui.Edge,Ui.Accent("Lavender"),.4):Ui.Edge;
        using(var path=Style.Round(new RectangleF(1.25f,1.25f,form.Width-2.5f,form.Height-2.5f),Corner-1))
        using(var pen=new Pen(color,2.5f))g.DrawPath(pen,path);
    }

    private static void Maximize(Form f)
    {
        var shell=f as Shell; if(shell!=null)shell.PrepareMaximize();
        f.WindowState=f.WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;
    }

    internal static bool ResizeHit(Form f,ref Message m)
    {
        if(m.Msg!=0x84||f.WindowState==FormWindowState.Maximized)return false;
        long packed=m.LParam.ToInt64();
        var pt=f.PointToClient(new Point((short)packed,(short)(packed>>16)));
        const int grip=7;
        bool l=pt.X<grip,r=pt.X>=f.ClientSize.Width-grip,t=pt.Y<grip,b=pt.Y>=f.ClientSize.Height-grip;
        int hit=t?(l?13:r?14:12):b?(l?16:r?17:15):l?10:r?11:0;
        if(hit==0)return false;m.Result=new IntPtr(hit);return true;
    }

    private sealed class TitleBar : Control
    {
        private readonly Form form;
        private readonly string title;
        private readonly bool resize;
        private int hot=-1;
        private bool over;

        internal TitleBar(Form form,string title,bool resize)
        {
            this.form=form;this.title=Ui.Voice(title);this.resize=resize;
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint,true);
            BackColor=Ui.SurfaceLow;
            AccessibleName=title+" title bar";
        }

        private RectangleF Dot(int index)
        {
            const float size=15f,gap=10f;
            return new RectangleF(20+index*(size+gap),(Height-size)/2f,size,size);
        }
        private bool Usable(int index){return index==0||resize;}
        private int Hit(Point point)
        {
            for(int i=0;i<3;i++)if(RectangleF.Inflate(Dot(i),5,5).Contains(point))return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index=Hit(e.Location);
            bool near=index>=0||new RectangleF(10,0,100,Height).Contains(e.Location);
            if(index!=hot||near!=over){hot=index;over=near;Cursor=index>=0&&Usable(index)?Cursors.Hand:Cursors.Default;Invalidate();}
        }
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=-1;over=false;Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if(e.Button!=MouseButtons.Left)return;
            int index=Hit(e.Location);
            if(index>=0){return;}
            if(e.Clicks==2&&resize){Maximize(form);return;}
            ReleaseCapture();SendMessage(form.Handle,0xA1,new IntPtr(2),IntPtr.Zero);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if(e.Button!=MouseButtons.Left)return;
            int index=Hit(e.Location);
            if(index<0||!Usable(index))return;
            if(index==0)form.Close();
            else if(index==1)form.WindowState=FormWindowState.Minimized;
            else Maximize(form);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;
            g.SmoothingMode=SmoothingMode.AntiAlias;
            g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Parent==null?Ui.Night:Parent.BackColor);
            var bar=new RectangleF(0,0,Width,Height-4);
            Style.Fill(g,bar,20,Ui.SurfaceLow);
            using(var line=new Pen(Ui.Line,1.5f))g.DrawLine(line,18,Height-5,Width-18,Height-5);
            var colors=new[]{Pink,Lavender,Cream};
            string[] glyphs={"Close","Minimize","Maximize"};
            for(int i=0;i<3;i++)
            {
                var dot=Dot(i);
                bool on=Usable(i);
                float grow=i==hot&&on?1.18f:1f;
                var disc=new RectangleF(dot.X-(grow-1)*dot.Width/2,dot.Y-(grow-1)*dot.Height/2,dot.Width*grow,dot.Height*grow);
                using(var brush=new SolidBrush(on?colors[i]:Color.FromArgb(70,colors[i])))g.FillEllipse(brush,disc);
                if(over&&on)Style.Icon(g,glyphs[i],RectangleF.Inflate(disc,-2.6f,-2.6f),Color.FromArgb(210,Ui.EdgeDeep));
            }
            using(var font=new Font(ShellFonts.Display,15,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var format=new StringFormat(StringFormat.GenericTypographic){Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
            using(var brush=new SolidBrush(form==Form.ActiveForm?Ui.Text:Ui.Muted))
            {
                Style.DrawSparkle(g,Width/2f-g.MeasureString(title,font,PointF.Empty,format).Width/2-16,(Height-4)/2f,4.5f,Ui.Tertiary("Lavender"));
                g.DrawString(title,font,brush,new RectangleF(0,0,Width,Height-4),format);
            }
        }
    }
}
