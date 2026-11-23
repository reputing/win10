using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class DesktopSurface : Form
{
    private readonly Shell shell;
    private readonly Image photograph;
    private readonly Label day=Ui.Label("",13,Color.White,true);
    private readonly Label clock=Ui.Label("",48,Color.White,true);
    private readonly Label desk=Ui.Label("Bluehour",27,Color.White,true);
    private readonly Label layout=Ui.Label("",11,Color.White);
    private readonly FlowLayoutPanel actions=Ui.Flow(false);
    private readonly ContextMenuStrip running=new ContextMenuStrip();
    internal readonly string Display;

    internal DesktopSurface(Shell shell,Screen screen)
    {
        this.shell=shell;
        Display=screen.DeviceName;
        Text="Bluehour Desktop Surface";
        ShowInTaskbar=false;
        FormBorderStyle=FormBorderStyle.None;
        StartPosition=FormStartPosition.Manual;
        BackColor=Ui.Night;
        ForeColor=Ui.Text;
        Font=new Font("Segoe UI",11);
        Bounds=screen.WorkingArea;
        DoubleBuffered=true;
        using (var asset=Assembly.GetExecutingAssembly().GetManifestResourceStream("Bluehour.Coast"))
            if (asset!=null)
                using (var image=Image.FromStream(asset)) photograph=new Bitmap(image);

        desk.Parent=this;
        layout.Parent=this;
        day.Parent=this;
        clock.Parent=this;
        actions.Parent=this;
        actions.BackColor=Color.Transparent;
        actions.WrapContents=true;
        var mode=Ui.Button("Open windows",()=>shell.Run(ShowRunning),shell.Accent);
        actions.Controls.Add(mode);
        actions.Controls.Add(Ui.Button("Workspaces",()=>shell.Run(()=>shell.ShowPage("Workspaces"))));
        actions.Controls.Add(Ui.Button("Discord",()=>shell.Run(shell.OpenDiscord)));
        actions.Controls.Add(Ui.Button("Files",()=>shell.Run(()=>Shell.Open("explorer.exe"))));
        actions.Controls.Add(Ui.Button("Control center",()=>shell.Run(()=>shell.ShowPage("Dashboard"))));
        actions.Controls.Add(Ui.Button("Return to Windows",()=>shell.Run(shell.ReturnToWindows)));
        ContextMenuStrip=new ContextMenuStrip();
        ContextMenuStrip.Items.Add("Search apps and commands",null,delegate{shell.Run(shell.OpenLauncher);});
        ContextMenuStrip.Items.Add("Appearance",null,delegate{shell.Run(()=>shell.ShowPage("Appearance"));});
        ContextMenuStrip.Items.Add("Recovery",null,delegate{shell.Run(()=>shell.ShowPage("Recovery"));});
        ContextMenuStrip.Items.Add("Return to Windows",null,delegate{shell.Run(shell.ReturnToWindows);});
        Resize+=delegate{UpdateDisplay();};
        UpdateDisplay();
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { var cp=base.CreateParams; cp.ExStyle|=0x08000080; return cp; } // No activation or task-switcher entry.
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg==0x21) { message.Result=new IntPtr(3); return; }
        base.WndProc(ref message);
    }

    private void ShowRunning()
    {
        foreach (var item in running.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
        running.Items.Clear();
        var screen=Screen.AllScreens.FirstOrDefault(display=>display.DeviceName==Display)??Screen.PrimaryScreen;
        var windows=DesktopWindow.List(screen,true);
        foreach (var window in windows)
        {
            var target=window;
            var title=window.Title.Length>72?window.Title.Substring(0,71)+"…":window.Title;
            running.Items.Add(title.Replace("&","&&"),null,delegate{shell.Run(target.Activate);});
        }
        if (windows.Count==0) running.Items.Add("No open windows").Enabled=false;
        running.Show(actions,new Point(0,actions.Height));
    }

    internal void UpdateDisplay()
    {
        clock.Text=shell.DockTime;
        day.Text=DateTime.Now.ToString("dddd, MMMM d");
        layout.Text="Workspace  /  "+shell.ActiveLayout;
        int inset=Math.Max(24,Width/30);
        desk.Location=new Point(inset,30);
        layout.Location=new Point(inset+2,desk.Bottom+1);
        actions.MaximumSize=new Size(Math.Max(160,Width-inset*2),0);
        actions.Location=new Point(inset,layout.Bottom+14);
        clock.Location=new Point(Math.Max(18,Width-clock.Width-inset),Math.Max(160,Height-157));
        day.Location=new Point(Math.Max(18,Width-day.Width-inset),clock.Bottom+4);
        if (actions.Controls.Count>0) actions.Controls[0].BackColor=shell.Accent;
    }

    internal void BringDesktopForward()
    {
        Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x13);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Width<=0 || Height<=0) return;
        e.Graphics.Clear(Ui.Night);
        if (photograph!=null)
        {
            float zoom=Math.Max(Width/(float)photograph.Width,Height/(float)photograph.Height);
            float width=photograph.Width*zoom,height=photograph.Height*zoom;
            e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(photograph,(Width-width)/2,(Height-height)/2,width,height);
        }
        using (var shade=new LinearGradientBrush(ClientRectangle,Color.FromArgb(166,13,16,35),Color.FromArgb(69,13,16,35),90f))
            e.Graphics.FillRectangle(shade,ClientRectangle);
        using (var bottom=new LinearGradientBrush(ClientRectangle,Color.Transparent,Color.FromArgb(160,9,18,40),90f))
            e.Graphics.FillRectangle(bottom,ClientRectangle);
        using (var edge=new SolidBrush(shell.Accent)) e.Graphics.FillRectangle(edge,0,0,Width,3);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (photograph!=null) photograph.Dispose();
            running.Dispose();
            if (ContextMenuStrip!=null) ContextMenuStrip.Dispose();
        }
        base.Dispose(disposing);
    }
}


