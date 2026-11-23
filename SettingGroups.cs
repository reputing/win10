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
    }
    protected override void OnSizeChanged(System.EventArgs e)
    {
        base.OnSizeChanged(e);
        if(Width<2||Height<2)return;
        using(var shape=Style.Round(ClientRectangle,18))
        {
            var previous=Region;
            Region=new Region(shape);
            if(previous!=null)previous.Dispose();
        }
    }
    internal void Fit(int width)
    {
        MaximumSize=new Size(width,0);
        MinimumSize=new Size(width,0);
        foreach(Control child in Controls)
        {
            int inner=width-Padding.Horizontal;
            child.MaximumSize=new Size(inner,0);
            if(child is ListBox)child.Width=inner;
        }
    }
    internal static void Wrap(FlowLayoutPanel page)
    {
        SettingGroup current=null;
        page.SuspendLayout();
        foreach(var child in page.Controls.Cast<Control>().ToArray())
        {
            if(object.Equals(child.Tag,"Section"))
            {
                current=new SettingGroup();
                int index=page.Controls.GetChildIndex(child);
                page.Controls.Add(current);
                page.Controls.SetChildIndex(current,index+1);
            }
            else if(current!=null)current.Controls.Add(child);
        }
        page.ResumeLayout(true);
    }
}

