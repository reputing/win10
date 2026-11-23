using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class WindowFrame
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);

    internal static void Attach(Form form,string title,bool resize)
    {
        form.FormBorderStyle=FormBorderStyle.None;
        form.Padding=new Padding(resize?5:2);
        var bar=new Panel {Dock=DockStyle.Top,Height=38,BackColor=Ui.Night};
        var label=Ui.Label(title,10,Ui.Muted,true);
        label.Dock=DockStyle.Fill;label.AutoSize=false;label.Padding=new Padding(11,0,0,0);
        label.TextAlign=ContentAlignment.MiddleLeft;
        bar.Controls.Add(label);
        MouseEventHandler drag=delegate(object s,MouseEventArgs e){
            if(e.Button!=MouseButtons.Left)return;
            if(e.Clicks==2&&resize){Maximize(form);return;}
            ReleaseCapture();SendMessage(form.Handle,0xA1,new IntPtr(2),IntPtr.Zero);
        };
        label.MouseDown+=drag;bar.MouseDown+=drag;
        var actions=Ui.Flow(false);
        actions.Dock=DockStyle.Right;actions.WrapContents=false;actions.BackColor=Ui.Night;
        if(resize)
        {
            CaptionButton(actions,"−","Minimize",()=>form.WindowState=FormWindowState.Minimized);
            CaptionButton(actions,"□","Maximize or restore",()=>Maximize(form));
        }
        CaptionButton(actions,"×","Close",form.Close);
        bar.Controls.Add(actions);
        form.Controls.Add(bar);
        bar.SendToBack();
    }
    private static void Maximize(Form f)
    {
        var shell=f as Shell; if(shell!=null)shell.PrepareMaximize();
        f.WindowState=f.WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;
    }
    private static void CaptionButton(Control parent,string glyph,string hint,Action action)
    {
        var b=Ui.Button(glyph,action);b.AutoSize=false;b.MinimumSize=Size.Empty;b.Size=new Size(33,28);
        b.AccessibleName=hint;b.Margin=new Padding(1,3,3,2);b.Padding=Padding.Empty;
        b.Font=new Font("Segoe UI",12);parent.Controls.Add(b);
    }
    internal static bool ResizeHit(Form f,ref Message m)
    {
        if(m.Msg!=0x84||f.WindowState==FormWindowState.Maximized)return false;
        long packed=m.LParam.ToInt64();
        var pt=f.PointToClient(new Point((short)packed,(short)(packed>>16)));
        bool l=pt.X<5,r=pt.X>=f.ClientSize.Width-5,t=pt.Y<5,b=pt.Y>=f.ClientSize.Height-5;
        int hit=t?(l?13:r?14:12):b?(l?16:r?17:15):l?10:r?11:0;
        if(hit==0)return false;m.Result=new IntPtr(hit);return true;
    }
}


