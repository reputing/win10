using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal sealed class StyledCheckList : CheckedListBox
{
    public StyledCheckList()
    {
        Font=new Font(Style.Family,11);
        IntegralHeight=false;
        Style.DarkScroll(this);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if(e.Index<0||e.Index>=Items.Count)return;
        var g=e.Graphics;
        bool selected=(e.State&DrawItemState.Selected)!=0;
        using(var fill=new SolidBrush(selected?Ui.Raised:Ui.Surface))g.FillRectangle(fill,e.Bounds);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        int dim=Math.Min(15,e.Bounds.Height-4);
        var mark=new Rectangle(e.Bounds.X+9,e.Bounds.Y+(e.Bounds.Height-dim)/2,dim,dim);
        bool on=GetItemChecked(e.Index);
        var sky=Ui.Accent("Lavender");
        Style.Fill(g,mark,4,on?sky:Ui.Raised);
        using(var border=new Pen(on?sky:Ui.Muted))
        using(var path=Style.Round(mark,4))g.DrawPath(border,path);
        if(on)Style.Icon(g,"Cleanup",mark,Ui.Night);
        var label=new Rectangle(mark.Right+10,e.Bounds.Y,e.Bounds.Right-mark.Right-14,e.Bounds.Height);
        TextRenderer.DrawText(g,GetItemText(Items[e.Index]),Font,label,Ui.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        if(Focused&&ShowFocusCues&&selected)e.DrawFocusRectangle();
    }
}


