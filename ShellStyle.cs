using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Style
{
    internal static FontFamily Family { get { return ShellFonts.Text; } }
    internal static GraphicsPath Round(RectangleF r,float radius)
    {
        var p=new GraphicsPath();
        float d=Math.Min(Math.Min(r.Width,r.Height),radius*2);
        if (d<=1) {p.AddRectangle(r);return p;}
        p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90);
        p.CloseFigure();
        return p;
    }
    internal static void Fill(Graphics g,RectangleF r,float radius,Color color)
    {
        using(var path=Round(r,radius))
        using(var brush=new SolidBrush(color)) g.FillPath(brush,path);
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h,int attribute,ref int value,int bytes);
    internal static void WindowChrome(Form f)
    {
        int on=1, round=2, caption=Ui.Surface.R|(Ui.Surface.G<<8)|(Ui.Surface.B<<16);
        if(DwmSetWindowAttribute(f.Handle,20,ref on,4)!=0) DwmSetWindowAttribute(f.Handle,19,ref on,4);
        DwmSetWindowAttribute(f.Handle,33,ref round,4);
        DwmSetWindowAttribute(f.Handle,35,ref caption,4);
    }

    internal static void Icon(Graphics g,string id,RectangleF box,Color color)
    {
        var state=g.Save();
        g.TranslateTransform(box.X,box.Y);g.ScaleTransform(box.Width/24f,box.Height/24f);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=new Pen(color,1.8f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
        {
            switch(id)
            {
                case "GripH": g.DrawLine(p,4,12,20,12);break;
                case "GripV": g.DrawLine(p,12,4,12,20);break;
                case "Launch":
                    g.DrawLines(p,new[]{new Point(3,17),new Point(9,6),new Point(13,13),new Point(16,8),new Point(22,17)});
                    g.DrawLine(p,5,19,20,19); break;
                case "Layouts":
                    using(var path=Round(new RectangleF(3,4,18,16),3))g.DrawPath(p,path);
                    g.DrawLine(p,11,5,11,19);g.DrawLine(p,12,12,20,12);break;
                case "Desktop":
                    using(var path=Round(new RectangleF(2,4,20,13),2))g.DrawPath(p,path);
                    g.DrawLine(p,12,17,12,21);g.DrawLine(p,8,21,16,21);break;
                case "Discord":
                    using(var path=Round(new RectangleF(3,4,17,13),5))g.DrawPath(p,path);
                    g.DrawLines(p,new[]{new Point(7,16),new Point(6,21),new Point(12,17)});
                    g.DrawLine(p,8,10,9,10);g.DrawLine(p,14,10,15,10);break;
                case "Files":
                    g.DrawPolygon(p,new[]{new Point(3,6),new Point(9,6),new Point(11,9),new Point(21,9),new Point(20,19),new Point(3,19)});break;
                case "Calendar":
                    using(var path=Round(new RectangleF(4,5,16,16),3))g.DrawPath(p,path);
                    g.DrawLine(p,4,10,20,10);g.DrawLine(p,8,3,8,7);g.DrawLine(p,16,3,16,7);g.DrawLine(p,8,15,12,15);break;
                case "Sound":
                    g.DrawPolygon(p,new[]{new Point(3,10),new Point(7,10),new Point(12,6),new Point(12,18),new Point(7,14),new Point(3,14)});
                    g.DrawArc(p,9,6,10,12,-50,100);g.DrawArc(p,9,3,14,18,-50,100);break;
                case "Network":
                    g.DrawArc(p,0,6,24,22,227,86);g.DrawArc(p,4,11,16,15,227,86);g.DrawEllipse(p,11,17,2,2);break;
                case "Settings":
                    g.DrawLine(p,3,7,21,7);g.DrawLine(p,3,17,21,17);
                    g.DrawEllipse(p,7,4,6,6);g.DrawEllipse(p,13,14,6,6);break;
                case "Recovery":
                    g.DrawArc(p,4,4,16,16,190,309);g.DrawLines(p,new[]{new Point(2,5),new Point(3,11),new Point(9,10)});break;
                case "Cleanup":
                    g.DrawLines(p,new[]{new Point(5,12),new Point(9,17),new Point(20,6)});break;
                default: g.DrawEllipse(p,5,5,14,14);break;
            }
        }
        g.Restore(state);
    }
}

internal sealed class ShellButton : Button
{
    public string Glyph;
    public bool GlyphOnly;
    public bool Pill;
    private bool hover;
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if(!Pill||Width<2||Height<2)return;
        using(var outline=Style.Round(ClientRectangle,Height/2f))
        {
            var old=Region;Region=new Region(outline);if(old!=null)old.Dispose();
        }
    }
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover=true;Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=false;Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        if(Parent is DesktopSurface)
        {
            var saved=g.Save();
            g.TranslateTransform(-Left,-Top);
            using(var paint=new PaintEventArgs(g,Bounds))InvokePaintBackground(Parent,paint);
            g.Restore(saved);
        }
        else g.Clear(Parent==null?Ui.Night:Parent.BackColor.A==255?Parent.BackColor:Ui.Night);
        Color fill=hover?ControlPaint.Light(BackColor,.12f):BackColor;
        Style.Fill(g,new RectangleF(1,1,Math.Max(1,Width-2),Math.Max(1,Height-2)),Pill?Height/2:10,fill);
        var bounds=ClientRectangle;
        if(Glyph!=null)
        {
            int size=GlyphOnly?Math.Min(22,Math.Min(Width,Height)-8):18;
            int left=GlyphOnly?(Width-size)/2:12;
            Style.Icon(g,Glyph,new RectangleF(left,(Height-size)/2,size,size),ForeColor);
            if(!GlyphOnly){bounds.X+=36;bounds.Width-=40;}
        }
        if(!GlyphOnly)TextRenderer.DrawText(g,Text,Font,bounds,ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|
            TextFormatFlags.EndEllipsis|(TextAlign==ContentAlignment.MiddleLeft?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter));
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-5,-4),ForeColor,fill);
    }
}



internal sealed class WorkspaceBadge : Control
{
    internal bool Vertical;
    public WorkspaceBadge()
    {
        Cursor=Cursors.Hand;Font=new Font(Style.Family,10,FontStyle.Bold);
        ForeColor=Ui.Accent("Glacier");BackColor=Ui.Night;AccessibleName="Workspace layout";
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;var saved=g.Save();g.TranslateTransform(Width/2f,Height/2f);
        if(Vertical)g.RotateTransform(90);
        using(var brush=new SolidBrush(ForeColor))
        using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
            g.DrawString(Text,Font,brush,PointF.Empty,format);
        g.Restore(saved);
    }
    protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
}
