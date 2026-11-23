using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class DockBar : Form
{
    private readonly Shell shell;
    private readonly Panel controls=new Panel();
    private readonly ToolTip tips=new ToolTip();
    private readonly Label clock=Ui.Label("",10,Ui.Muted,true);
    private readonly uint callback=Native.RegisterWindowMessage("Bluehour.AppBar");
    private readonly uint taskbarCreated=Native.RegisterWindowMessage("TaskbarCreated");
    private readonly ContextMenuStrip layoutMenu=new ContextMenuStrip();
    private readonly WorkspaceBadge badge=new WorkspaceBadge();
    private Button workspace;
    private bool vertical;
    private Screen monitor;
    private string edge;
    private bool registered;
    private Rectangle last;

    internal DockBar(Shell shell)
    {
        this.shell=shell;
        FormBorderStyle=FormBorderStyle.None;
        StartPosition=FormStartPosition.Manual;
        ShowInTaskbar=false;
        TopMost=true;
        BackColor=Ui.Surface;
        Font=new Font(Style.Family,9);
        badge.Click+=delegate{shell.Run(()=>shell.ShowPage("Workspaces"));};
        controls.Dock=DockStyle.Fill;
        controls.BackColor=Ui.Night;
        controls.Resize+=delegate{ArrangeRail();};
        Controls.Add(controls);
        foreach (var item in Shell.Modes)
        {
            string mode=item;
            layoutMenu.Items.Add(mode,null,delegate{shell.Run(()=>shell.ChooseLayout(mode));});
        }
        layoutMenu.Items.Add("Manage windows…",null,delegate{shell.ShowPage("Workspaces");});
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { var cp=base.CreateParams; cp.ExStyle|=0x80; return cp; }
    }

    public bool Hovering { get { return Visible && Bounds.Contains(Cursor.Position); } }

    internal bool EnableBar(Screen screen,string alignment)
    {
        DisableBar();
        monitor=screen;
        edge=alignment;
        var data=Data();
        registered=Native.SHAppBarMessage(0,ref data)!=UIntPtr.Zero;
        if (!registered) return false;
        Populate();
        if (!Position()) { DisableBar(); return false; }
        Show();
        return true;
    }

    internal void DisableBar()
    {
        if (registered)
        {
            registered=false;
            var data=Data();
            Native.SHAppBarMessage(1,ref data);
        }
        Hide();
        last=Rectangle.Empty;
    }

    private Native.Bar Data()
    {
        return new Native.Bar {Size=Marshal.SizeOf(typeof(Native.Bar)),Handle=Handle,Notification=callback};
    }

    private bool Position()
    {
        var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==monitor.DeviceName)??Screen.PrimaryScreen;
        var data=Data();
        data.Edge=edge=="Top"?1u:edge=="Right"?2u:edge=="Bottom"?3u:0u;
        data.Bounds=new Native.Rect(screen.Bounds);
        Native.SHAppBarMessage(2,ref data);

        int thickness=54;
        switch (data.Edge)
        {
            case 0: data.Bounds.R=data.Bounds.L+thickness; break;
            case 1: data.Bounds.B=data.Bounds.T+thickness; break;
            case 2: data.Bounds.L=data.Bounds.R-thickness; break;
            default: data.Bounds.T=data.Bounds.B-thickness; break;
        }
        if (last==data.Bounds.Rectangle) return true;
        if (Native.SHAppBarMessage(3,ref data)==UIntPtr.Zero) return false;
        last=data.Bounds.Rectangle;
        Bounds=last;
        return true;
    }

    private void Populate()
    {
        vertical=edge=="Left" || edge=="Right";
        var old=controls.Controls.Cast<Control>().Where(c=>c!=clock&&c!=badge).ToArray();
        controls.Controls.Clear();
        foreach(var c in old)c.Dispose();
        Add("Launch","Search apps and commands",shell.OpenLauncher,true);
        workspace=Add("Layouts","Window layouts",()=>layoutMenu.Show(workspace,vertical?new Point(workspace.Width,0):new Point(0,workspace.Height)));
        Add("Desktop","Show desktop",shell.RevealDesktop);
        Add("Files","File Explorer",()=>Shell.Open("explorer.exe"));
        Add("Discord","Discord",shell.OpenDiscord);
        Add("Calendar","Calendar",shell.Calendar);
        Add("Sound","Volume and audio",()=>Shell.Open("ms-settings:sound"));
        Add("Network","Network settings",()=>Shell.Open("ms-settings:network"));
        Add("Settings","Control center",()=>shell.ShowPage("Dashboard"));
        clock.AutoSize=false;
        clock.TextAlign=ContentAlignment.MiddleCenter;
        clock.ForeColor=Ui.Muted;
        controls.Controls.Add(clock);controls.Controls.Add(badge);
        ArrangeRail();
        RefreshBar();
    }
    private Button Add(string icon,string hint,Action action,bool primary=false)
    {
        var b=new ShellButton {Glyph=icon,GlyphOnly=true,Pill=true,AccessibleName=hint,Text=hint,FlatStyle=FlatStyle.Flat,
            ForeColor=primary?Ui.Night:Ui.Text,BackColor=primary?shell.Accent:Ui.Surface,Cursor=Cursors.Hand};
        b.FlatAppearance.BorderSize=0;b.Click+=delegate{shell.Run(action);};tips.SetToolTip(b,hint);controls.Controls.Add(b);
        return b;
    }
    private void ArrangeRail()
    {
        if(controls.Controls.Count<10)return;
        int length=vertical?controls.Height:controls.Width;
        int extent=vertical?controls.Width:controls.Height;
        int cell=Math.Min(39,Math.Max(14,(length-82)/9));
        int inset=(extent-cell)/2;
        int tail=length-7-4*cell-12;
        for(int i=0;i<9;i++)
        {
            int pos=i<5?9+i*(cell+2):tail+(i-5)*(cell+3);
            controls.Controls[i].Bounds=vertical?new Rectangle(inset,pos,cell,cell):new Rectangle(pos,inset,cell,cell);
        }
        int middle=(tail+9+5*(cell+2))/2;
        bool room=tail-5*(cell+2)>142;
        badge.Visible=room;
        badge.Vertical=vertical;
        badge.Bounds=vertical?new Rectangle(0,middle-60,extent,108):new Rectangle(middle-64,0,128,extent);
        clock.Bounds=vertical?new Rectangle(0,tail-46,extent,44):new Rectangle(tail-68,0,66,extent);
        clock.Visible=tail-5*(cell+2)>62;
    }
    internal void Preview(string alignment,Size size)
    {
        edge=alignment;ClientSize=size;Populate();
    }
    internal void RefreshBar()
    {
        string value=DateTime.Now.ToString(vertical?"hh\nmm":"HH:mm");
        if(clock.Text!=value)clock.Text=value;
        if(badge.Text!=shell.ActiveLayout)badge.Text=shell.ActiveLayout;
        if(workspace!=null)tips.SetToolTip(workspace,"Layout · "+shell.ActiveLayout);
        if(controls.Controls.Count>0)controls.Controls[0].BackColor=shell.Accent;
    }
    protected override void WndProc(ref Message m)
    {
        if (registered && (m.Msg==callback || m.Msg==taskbarCreated || m.Msg==0x007e))
        {
            if (m.Msg==callback && m.WParam.ToInt32()==2)
            {
                TopMost=m.LParam==IntPtr.Zero;
                if (!TopMost) Native.SetWindowPos(Handle,new IntPtr(1),0,0,0,0,0x13);
            }
            else if (m.Msg==taskbarCreated)
                BeginInvoke((Action)(()=>shell.Run(()=>EnableBar(monitor,edge))));
            else if (m.Msg!=callback || m.WParam==IntPtr.Zero)
                BeginInvoke((Action)(()=>shell.Run(()=>{if (registered) Position();})));
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { if (registered) DisableBar(); tips.Dispose(); layoutMenu.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class LaunchItem
{
    public readonly string Name,Detail;
    public readonly Action Run;
    public LaunchItem(string name,string detail,Action run) { Name=name; Detail=detail; Run=run; }
    public override string ToString() { return Name+"  ·  "+Detail; }
}

internal sealed class Launcher : Form
{
    private readonly Shell shell;
    private readonly ListBox results;
    private readonly TextBox search;
    private readonly LaunchItem[] apps,commands;
    public Launcher(Shell shell)
    {
        this.shell=shell;
        apps=shell.Items(false).ToArray();
        commands=shell.Items(true).ToArray();
        Text="Bluehour Launcher";
        StartPosition=FormStartPosition.CenterScreen;
        FormBorderStyle=FormBorderStyle.FixedToolWindow;
        ShowInTaskbar=false;
        TopMost=true;
        BackColor=Ui.Night;
        ForeColor=Ui.Text;
        Font=new Font(Style.Family,10);
        KeyPreview=true;
        ClientSize=new Size(570,410);
        var frame=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(19,14,19,12)};
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        frame.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute,24));
        frame.Controls.Add(Ui.Label("Launcher",20,Ui.Text,true),0,0);
        search=new TextBox {Dock=DockStyle.Fill,BackColor=Ui.Raised,ForeColor=Ui.Text,AccessibleName="Search apps or type > for commands",Font=Font};
        frame.Controls.Add(search,0,1);
        results=new ListBox {Dock=DockStyle.Fill,BackColor=Ui.Surface,ForeColor=Ui.Text,BorderStyle=BorderStyle.None,
            AccessibleName="Launcher results",IntegralHeight=false,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=28};
        results.DrawItem+=DrawItem;
        frame.Controls.Add(results,0,2);
        frame.Controls.Add(Ui.Label("↑ ↓  Navigate      Enter  Open      >  Commands      Esc  Close",9,Ui.Muted),0,3);
        Controls.Add(frame);
        WindowFrame.Attach(this,"Command palette",false);
        search.TextChanged+=delegate{Filter();};
        results.DoubleClick+=delegate{ActivateResult();};
        KeyDown+=delegate(object sender,KeyEventArgs e)
        {
            if (e.KeyCode==Keys.Escape) { e.SuppressKeyPress=true; Close(); }
            else if (e.KeyCode==Keys.Enter) { e.SuppressKeyPress=true; ActivateResult(); }
            else if (search.Focused && results.Items.Count>0 && (e.KeyCode==Keys.Up || e.KeyCode==Keys.Down))
            {
                e.SuppressKeyPress=true;
                results.SelectedIndex=Math.Max(0,Math.Min(results.Items.Count-1,results.SelectedIndex+(e.KeyCode==Keys.Up?-1:1)));
            }
        };
        Shown+=delegate{search.Focus();};
        Filter();
    }

    private void Filter()
    {
        string term=search.Text.Trim();
        bool command=term.StartsWith(">",StringComparison.Ordinal);
        if (command) term=term.Substring(1).Trim();
        results.BeginUpdate();
        results.Items.Clear();
        results.Items.AddRange((command?commands:apps).Where(item=>item.Name.IndexOf(term,StringComparison.CurrentCultureIgnoreCase)>=0).ToArray());
        if (results.Items.Count>0) results.SelectedIndex=0;
        results.EndUpdate();
    }

    private void DrawItem(object sender,DrawItemEventArgs e)
    {
        if (e.Index<0) return;
        var selected=(e.State&DrawItemState.Selected)!=0;
        using (var paint=new SolidBrush(selected?shell.Accent:Ui.Surface)) e.Graphics.FillRectangle(paint,e.Bounds);
        var bounds=Rectangle.Inflate(e.Bounds,-8,0);
        TextRenderer.DrawText(e.Graphics,results.Items[e.Index].ToString(),Font,bounds,selected?Ui.Night:Ui.Text,
            TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        e.DrawFocusRectangle();
    }

    private void ActivateResult()
    {
        var item=results.SelectedItem as LaunchItem;
        if (item==null) return;
        Close();
        shell.Run(item.Run);
    }
}






