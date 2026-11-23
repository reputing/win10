using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Timer=System.Windows.Forms.Timer;

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
    // Fills only the band between an outer and an inner rounded shape (the parts of the outer that the inner does not
    // cover). Painting a frame as bands costs in proportion to its perimeter instead of its area.
    internal static void FillBand(Graphics g,RectangleF outer,float outerRadius,RectangleF inner,float innerRadius,Color color)
    {
        using(var band=new GraphicsPath(FillMode.Alternate))
        {
            using(var a=Round(outer,outerRadius))band.AddPath(a,false);
            using(var b=Round(inner,innerRadius))band.AddPath(b,false);
            using(var brush=new SolidBrush(color))g.FillPath(brush,band);
        }
    }

    internal static Color Mix(Color a,Color b,double t)
    {
        return Color.FromArgb(Motion.Lerp(a.A,b.A,t),Motion.Lerp(a.R,b.R,t),Motion.Lerp(a.G,b.G,t),Motion.Lerp(a.B,b.B,t));
    }

    // kuro.rip's four-point sparkle: concave sides, sharp tips.
    internal static GraphicsPath Sparkle(float cx,float cy,float r)
    {
        var path=new GraphicsPath();
        var tips=new[]{new PointF(cx,cy-r),new PointF(cx+r,cy),new PointF(cx,cy+r),new PointF(cx-r,cy)};
        for(int i=0;i<4;i++)
        {
            var a=tips[i];var b=tips[(i+1)%4];
            path.AddBezier(a,new PointF(cx+(a.X-cx)*.16f,cy+(a.Y-cy)*.16f),new PointF(cx+(b.X-cx)*.16f,cy+(b.Y-cy)*.16f),b);
        }
        path.CloseFigure();
        return path;
    }
    internal static void DrawSparkle(Graphics g,float cx,float cy,float r,Color color)
    {
        using(var path=Sparkle(cx,cy,r))
        using(var brush=new SolidBrush(color))g.FillPath(brush,path);
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h,int attribute,ref int value,int bytes);
    internal static void WindowChrome(Form f)
    {
        int on=1, round=2, caption=Ui.Night.R|(Ui.Night.G<<8)|(Ui.Night.B<<16);
        if(DwmSetWindowAttribute(f.Handle,20,ref on,4)!=0) DwmSetWindowAttribute(f.Handle,19,ref on,4);
        DwmSetWindowAttribute(f.Handle,33,ref round,4);
        DwmSetWindowAttribute(f.Handle,35,ref caption,4);
    }

    // Line icons on a 24x24 grid, drawn here so they match the theme's weight and need no font.
    internal static void Icon(Graphics g,string id,RectangleF box,Color color)
    {
        var state=g.Save();
        g.TranslateTransform(box.X,box.Y);g.ScaleTransform(box.Width/24f,box.Height/24f);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=new Pen(color,1.9f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
        using(var fill=new SolidBrush(color))
        {
            switch(id)
            {
                case "GripH": g.DrawLine(p,4,12,20,12);break;
                case "GripV": g.DrawLine(p,12,4,12,20);break;
                case "Calculator":
                    using(var outline=Round(new RectangleF(4,2,16,20),4))g.DrawPath(p,outline);
                    g.DrawLine(p,7,8,17,8);
                    for(int row=0;row<2;row++)for(int col=0;col<3;col++)g.DrawLine(p,7+col*4,12+row*4,8+col*4,12+row*4);
                    break;
                case "Notepad":
                    using(var outline=Round(new RectangleF(5,3,15,18),3))g.DrawPath(p,outline);
                    g.DrawLine(p,9,2,9,5);g.DrawLine(p,15,2,15,5);
                    g.DrawLine(p,8,10,17,10);g.DrawLine(p,8,14,17,14);g.DrawLine(p,8,17,13,17);break;
                case "Paint":
                    g.DrawEllipse(p,3,4,18,17);g.DrawEllipse(p,7,8,2,2);g.DrawEllipse(p,13,7,2,2);
                    g.DrawLine(p,11,17,21,5);break;
                case "Launch": // a small cat, for the little desktop you live in
                    using(var head=new GraphicsPath())
                    {
                        head.AddLine(5,10,6,3.5f);head.AddLine(6,3.5f,10.5f,6.4f);
                        head.AddBezier(10.5f,6.4f,11.6f,6.1f,12.4f,6.1f,13.5f,6.4f);
                        head.AddLine(13.5f,6.4f,18,3.5f);head.AddLine(18,3.5f,19,10);
                        head.AddBezier(19,10,21.6f,13.2f,20.6f,17.6f,16.6f,19.9f);
                        head.AddBezier(16.6f,19.9f,14.6f,21.1f,9.4f,21.1f,7.4f,19.9f);
                        head.AddBezier(7.4f,19.9f,3.4f,17.6f,2.4f,13.2f,5,10);
                        head.CloseFigure();
                        g.DrawPath(p,head);
                    }
                    g.FillEllipse(fill,8,12.2f,1.9f,2.6f);g.FillEllipse(fill,14.1f,12.2f,1.9f,2.6f);
                    g.DrawLines(p,new[]{new PointF(10.6f,16.6f),new PointF(12,17.7f),new PointF(13.4f,16.6f)});
                    break;
                case "Sparkle":
                    using(var star=Sparkle(12,12,10))g.FillPath(fill,star);
                    break;
                case "Search":
                    g.DrawEllipse(p,3.5f,3.5f,12.5f,12.5f);g.DrawLine(p,14.6f,14.6f,20.5f,20.5f);break;
                case "Layouts":
                    using(var path=Round(new RectangleF(3,4,18,16),4))g.DrawPath(p,path);
                    g.DrawLine(p,11,5,11,19);g.DrawLine(p,12,12,20,12);break;
                case "Grid":
                    foreach(var cell in new[]{new RectangleF(3.5f,3.5f,7.5f,7.5f),new RectangleF(13,3.5f,7.5f,7.5f),new RectangleF(3.5f,13,7.5f,7.5f),new RectangleF(13,13,7.5f,7.5f)})
                        using(var path=Round(cell,2.2f))g.DrawPath(p,path);
                    break;
                case "Focus":
                    using(var big=Round(new RectangleF(3.5f,3.5f,10.5f,17),2.4f))g.DrawPath(p,big);
                    using(var a=Round(new RectangleF(16.5f,3.5f,4,7.5f),1.8f))g.DrawPath(p,a);
                    using(var b=Round(new RectangleF(16.5f,13,4,7.5f),1.8f))g.DrawPath(p,b);
                    break;
                case "Columns":
                    foreach(float x in new[]{3.5f,9.8f,16.1f})using(var path=Round(new RectangleF(x,3.5f,4.4f,17),2f))g.DrawPath(p,path);
                    break;
                case "Rows":
                    foreach(float y in new[]{3.5f,9.8f,16.1f})using(var path=Round(new RectangleF(3.5f,y,17,4.4f),2f))g.DrawPath(p,path);
                    break;
                case "Cascade":
                    using(var back=Round(new RectangleF(3.5f,3.5f,12.5f,11.5f),2.6f))
                    using(var front=Round(new RectangleF(8,9,12.5f,11.5f),2.6f))
                    {
                        var clip=g.Save();
                        g.SetClip(front,CombineMode.Exclude);   // the back card disappears behind the front one
                        g.DrawPath(p,back);
                        g.Restore(clip);
                        g.DrawPath(p,front);
                    }
                    break;
                case "Desktop":
                    using(var path=Round(new RectangleF(2,4,20,13),3))g.DrawPath(p,path);
                    g.DrawLine(p,12,17,12,21);g.DrawLine(p,8,21,16,21);break;
                case "Discord":
                    using(var path=Round(new RectangleF(3,4,17,13),5))g.DrawPath(p,path);
                    g.DrawLines(p,new[]{new Point(7,16),new Point(6,21),new Point(12,17)});
                    g.DrawLine(p,8,10,9,10);g.DrawLine(p,14,10,15,10);break;
                case "Files":
                    g.DrawPolygon(p,new[]{new Point(3,6),new Point(9,6),new Point(11,9),new Point(21,9),new Point(20,19),new Point(3,19)});break;
                case "Calendar":
                    using(var path=Round(new RectangleF(4,5,16,16),4))g.DrawPath(p,path);
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
                case "Bolt":
                    g.DrawLines(p,new[]{new PointF(13.5f,2.5f),new PointF(5,13.5f),new PointF(11.5f,13.5f),new PointF(10.5f,21.5f),new PointF(19,10),new PointF(12.5f,10),new PointF(13.5f,2.5f)});break;
                case "Chevron":
                    g.DrawLines(p,new[]{new Point(6,9),new Point(12,15),new Point(18,9)});break;
                case "Close":
                    g.DrawLine(p,6,6,18,18);g.DrawLine(p,18,6,6,18);break;
                case "Minimize":
                    g.DrawLine(p,6,12,18,12);break;
                case "Maximize":
                    using(var path=Round(new RectangleF(6,6,12,12),2.5f))g.DrawPath(p,path);break;
                default: g.DrawEllipse(p,5,5,14,14);break;
            }
        }
        g.Restore(state);
    }

    // Draws text with kuro's chunky hard shadow.
    internal static void ShadowText(Graphics g,string text,Font font,Color color,Color shadow,PointF at,float drop,StringFormat format)
    {
        using(var back=new SolidBrush(shadow))g.DrawString(text,font,back,at.X,at.Y+drop,format);
        using(var front=new SolidBrush(color))g.DrawString(text,font,front,at.X,at.Y,format);
    }
}

// Buttons behave like kuro.rip's sticker buttons: they sit on a hard shadow, lift a little when hovered, and sink
// onto the shadow while pressed. A click is registered when the mouse is released over the button, or with Space/Enter.
internal sealed class ShellButton : Button
{
    public string Glyph;
    public bool GlyphOnly;
    public string Detail;
    public bool Pill;
    public bool Chevron;
    private readonly Timer animation=new Timer {Interval=15};
    private bool over,down;
    private float hover,press;

    public ShellButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint,true);
        animation.Tick+=delegate{Step();};
    }

    private void Step()
    {
        float h=over&&Enabled?1f:0f,p=down&&over&&Enabled?1f:0f;
        hover+=(h-hover)*.34f;press+=(p-press)*.5f;
        if(Math.Abs(h-hover)<.02f&&Math.Abs(p-press)<.02f){hover=h;press=p;animation.Stop();}
        Invalidate();
    }
    private void Go()
    {
        if(Motion.Enabled)animation.Start();
        else{hover=over&&Enabled?1f:0f;press=down&&over&&Enabled?1f:0f;Invalidate();}
    }

    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);over=true;Go();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);over=false;Go();}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){down=true;Go();}}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);down=false;Go();}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter){down=over=true;Go();}}
    protected override void OnKeyUp(KeyEventArgs e){base.OnKeyUp(e);if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter){down=over=false;Go();}}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);down=false;Go();Invalidate();}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Go();Invalidate();}

    private void PaintBackdrop(Graphics g)
    {
        if(Parent is DesktopSurface)((DesktopSurface)Parent).PaintBackdropFor(this,g);
        else g.Clear(Parent==null?Ui.Night:Parent.BackColor.A==255?Parent.BackColor:Ui.Night);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        PaintBackdrop(g);
        float alpha=Enabled?1f:.45f;
        if(Detail!=null){PaintCard(g,alpha);return;}
        // Sticker button: hard shadow underneath, body that lifts on hover and sinks on press.
        const float shadow=3f;
        float bodyHeight=Math.Max(1,Height-2-shadow);
        // At rest the body sits 1px from the top with the shadow 3px below it; hover lifts it a pixel, press sinks it.
        var body=new RectangleF(1,1-hover+press*2.4f,Math.Max(1,Width-2),bodyHeight);
        float radius=Pill?bodyHeight/2f:12f;
        Color baseColor=BackColor;
        Color fill=Style.Mix(baseColor,Color.White,hover*.09);
        if(press>0)fill=Style.Mix(fill,Color.Black,press*.12);
        Color ink=Style.Mix(baseColor,Ui.SurfaceLowest,.62);
        Style.Fill(g,new RectangleF(1,1+shadow,body.Width,bodyHeight),radius,Color.FromArgb((int)(235*alpha),ink));
        Style.Fill(g,body,radius,Color.FromArgb((int)(255*alpha),fill));
        var bounds=Rectangle.Round(body);
        Color text=Enabled?ForeColor:Ui.Faint;
        if(Glyph!=null)
        {
            int size=GlyphOnly?Math.Min(22,Math.Min(bounds.Width,bounds.Height)-8):18;
            int left=GlyphOnly?bounds.X+(bounds.Width-size)/2:bounds.X+12;
            Style.Icon(g,Glyph,new RectangleF(left,bounds.Y+(bounds.Height-size)/2f,size,size),text);
            if(!GlyphOnly){bounds.X+=34;bounds.Width-=38;}
        }
        if(Chevron){Style.Icon(g,"Chevron",new RectangleF(bounds.Right-24,bounds.Y+(bounds.Height-14)/2f,14,14),Ui.Muted);bounds.Width-=18;}
        if(!GlyphOnly)TextRenderer.DrawText(g,Text,Font,bounds,text,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|
            TextFormatFlags.EndEllipsis|(TextAlign==ContentAlignment.MiddleLeft?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter));
        if(Focused&&ShowFocusCues)
            using(var ring=Style.Round(RectangleF.Inflate(body,2.5f,2.5f),radius+2.5f))
            using(var pen=new Pen(Ui.Accent("Lavender"),2f))g.DrawPath(pen,ring);
    }

    // Navigation cards (settings pages): a flat rounded card with an icon disc.
    private void PaintCard(Graphics g,float alpha)
    {
        float scale=1f-press*.015f;
        var rect=new RectangleF(1+(Width-2)*(1-scale)/2,1+(Height-2)*(1-scale)/2,(Width-2)*scale,(Height-2)*scale);
        Color fill=Style.Mix(BackColor,Color.White,hover*.06);
        Style.Fill(g,rect,22,Color.FromArgb((int)(255*alpha),fill));
        using(var outline=Style.Round(RectangleF.Inflate(rect,-.75f,-.75f),22))
        using(var pen=new Pen(Color.FromArgb((int)(150*alpha),hover>.01f||BackColor!=Ui.Raised?Ui.Edge:Ui.Line),1.5f))g.DrawPath(pen,outline);
        Style.Fill(g,new RectangleF(rect.X+11,rect.Y+(rect.Height-36)/2,36,36),18,Ui.Night);
        Style.Icon(g,Glyph,new RectangleF(rect.X+18,rect.Y+(rect.Height-22)/2,22,22),ForeColor);
        var title=new Rectangle((int)rect.X+56,(int)rect.Y+7,(int)rect.Width-62,24);
        TextRenderer.DrawText(g,Ui.Voice(Text),Font,title,Ui.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        using(var small=new Font(Style.Family,8.5f))
            TextRenderer.DrawText(g,Detail,small,new Rectangle((int)rect.X+56,(int)rect.Y+32,(int)rect.Width-62,20),Ui.Muted,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        if(Focused&&ShowFocusCues)
            using(var ring=Style.Round(RectangleF.Inflate(rect,-2,-2),20))
            using(var pen=new Pen(Ui.Accent("Lavender"),2f))g.DrawPath(pen,ring);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)animation.Dispose();
        base.Dispose(disposing);
    }
}

// Context menus in the theme: plum card, pill-shaped hover, accent checks. The stock Windows menu is white with a blue bar.
internal sealed class KuroColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground { get { return Ui.Surface; } }
    public override Color MenuBorder { get { return Ui.Edge; } }
    public override Color MenuItemBorder { get { return Color.Transparent; } }
    public override Color MenuItemSelected { get { return Ui.Raised; } }
    public override Color ImageMarginGradientBegin { get { return Ui.Surface; } }
    public override Color ImageMarginGradientMiddle { get { return Ui.Surface; } }
    public override Color ImageMarginGradientEnd { get { return Ui.Surface; } }
    public override Color SeparatorDark { get { return Ui.Line; } }
    public override Color SeparatorLight { get { return Ui.Line; } }
}

internal sealed class KuroMenuRenderer : ToolStripProfessionalRenderer
{
    internal KuroMenuRenderer() : base(new KuroColors()) { RoundedEdges=false; }
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using(var brush=new SolidBrush(Ui.Surface))e.Graphics.FillRectangle(brush,e.AffectedBounds);
    }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        var area=new RectangleF(1,1,e.ToolStrip.Width-2.5f,e.ToolStrip.Height-2.5f);
        using(var path=Style.Round(area,16))
        using(var pen=new Pen(Ui.Edge,2f))e.Graphics.DrawPath(pen,path);
    }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if(!e.Item.Selected||!e.Item.Enabled)return;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Style.Fill(e.Graphics,new RectangleF(5,e.Item.Bounds.Y,e.Item.Width-10,e.Item.Height),12,Ui.Raised);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var item=e.Item as ToolStripMenuItem;
        e.TextColor=!e.Item.Enabled?Ui.Faint:item!=null&&item.Checked?Ui.Accent("Lavender"):e.Item.Selected?Color.White:Ui.Text;
        e.TextFormat|=TextFormatFlags.NoPrefix;
        base.OnRenderItemText(e);
    }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var box=new RectangleF(e.ImageRectangle.X+1,e.ImageRectangle.Y+2,e.ImageRectangle.Height-2,e.ImageRectangle.Height-2);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Style.Icon(e.Graphics,"Cleanup",box,Ui.Accent("Lavender"));
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using(var pen=new Pen(Ui.Line))e.Graphics.DrawLine(pen,16,e.Item.Height/2,e.Item.Width-16,e.Item.Height/2);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor=Ui.Muted;
        base.OnRenderArrow(e);
    }
}
