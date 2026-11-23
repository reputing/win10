using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed class SettingGroup : FlowLayoutPanel
{
    internal SettingGroup()
    {
        AutoSize=true;
        AutoSizeMode=AutoSizeMode.GrowAndShrink;
        FlowDirection=FlowDirection.TopDown;
        WrapContents=false;
        BackColor=Ui.Surface;
        Padding=new Padding(14,9,14,6);
        Margin=new Padding(0,3,0,4);
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
    }
    // A rounded card drawn with anti-aliasing. (A window region would leave a jagged, pointy edge.)
    protected override void OnPaintBackground(System.Windows.Forms.PaintEventArgs e)
    {
        var g=e.Graphics;
        g.Clear(Parent==null?Ui.Night:Parent.BackColor);
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Style.Fill(g,new System.Drawing.RectangleF(0,0,Width,Height),18,Ui.Surface);
    }
    private int fitted=-1;
    // Sets the card's width and its children's limits in one layout pass, and not at all when nothing changed.
    internal void Fit(int width)
    {
        if(fitted==width)return;
        fitted=width;
        SuspendLayout();
        MaximumSize=new Size(width,0);
        MinimumSize=new Size(width,0);
        int inner=width-Padding.Horizontal;
        foreach(Control child in Controls)
        {
            child.MaximumSize=new Size(inner,0);
            if(child is ListBox)child.Width=inner;
        }
        ResumeLayout(false);   // the page lays everything out once, after all the cards have their widths
    }
    // Puts each section's controls into a card. Every card is filled while its layout is suspended and laid out once;
    // filling it control by control would lay the whole card out again for each one.
    internal static void Wrap(FlowLayoutPanel page)
    {
        SettingGroup current=null;
        var groups=new System.Collections.Generic.List<SettingGroup>();
        page.SuspendLayout();
        foreach(var child in page.Controls.Cast<Control>().ToArray())
        {
            if(object.Equals(child.Tag,"Section"))
            {
                current=new SettingGroup();
                current.SuspendLayout();
                groups.Add(current);
                int index=page.Controls.GetChildIndex(child);
                page.Controls.Add(current);
                page.Controls.SetChildIndex(current,index+1);
            }
            else if(current!=null)current.Controls.Add(child);
        }
        foreach(var group in groups)group.ResumeLayout(false);
        page.ResumeLayout(false);
    }
}

// The page area. Double buffered; scrolling and painting are left to Windows' normal, cheap path. (Composing the whole
// page off screen on every change made each hover repaint all of it.)
internal sealed class SmoothPanel : Panel
{
    internal SmoothPanel(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);}

    // Windows Forms scrolls a control into view whenever it gets focus, so clicking a switch that is half off the bottom of
    // the page made the whole page jump. A control that is already (partly) visible is left where it is.
    protected override Point ScrollToControl(Control activeControl)
    {
        var bounds=RectangleToClient(activeControl.Parent.RectangleToScreen(activeControl.Bounds));
        return bounds.IntersectsWith(ClientRectangle)?DisplayRectangle.Location:base.ScrollToControl(activeControl);
    }
}
