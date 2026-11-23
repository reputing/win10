using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

internal sealed class DesktopSurface : Form
{
    private readonly Shell shell;
    private Image photograph;
    private string loadedPath;
    private Bitmap background;
    private readonly ShellButton search, workspace, settings;
    private readonly ToolTip hints=new ToolTip();
    private readonly ContextMenuStrip running=new ContextMenuStrip();
    private string renderedTime, renderedDate;
    private Color renderedAccent;
    internal const int TopBar=64;
    internal readonly string Display;

    internal DesktopSurface(Shell shell,Screen screen)
    {
        this.shell=shell; Display=screen.DeviceName;
        Text="Bluehour Desktop Surface"; ShowInTaskbar=false;
        FormBorderStyle=FormBorderStyle.None; StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None;
        BackColor=Ui.Night; ForeColor=Ui.Text; DoubleBuffered=true;
        loadedPath="";
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Bluehour.Coast"))
        using(var image=Image.FromStream(stream)) photograph=new Bitmap(image);
        search=ActionButton("Search apps     Ctrl + Alt + K",null,shell.OpenLauncher);
        workspace=ActionButton("Workspace layouts","GripH",()=>shell.ShowPage("Workspaces"));
        settings=ActionButton("Control center","GripV",()=>shell.ShowPage("Dashboard"));
        ContextMenuStrip=new ContextMenuStrip {BackColor=Ui.Surface,ForeColor=Ui.Text,ShowImageMargin=false};
        ContextMenuStrip.Items.Add("Open windows",null,delegate{shell.Run(ShowRunning);});
        ContextMenuStrip.Items.Add("Open files",null,delegate{shell.Run(()=>Shell.Open("explorer.exe"));});
        ContextMenuStrip.Items.Add("Appearance",null,delegate{shell.Run(()=>shell.ShowPage("Appearance"));});
        ContextMenuStrip.Items.Add("Return to Windows",null,delegate{shell.Run(shell.ReturnToWindows);});
        ContextMenuStrip.Items.Insert(0,new ToolStripMenuItem("Pin an app…",null,delegate{shell.Run(shell.PickApp);}));
        Bounds=screen.WorkingArea;new BubbleLayer(shell,this,true);
        Resize+=delegate{Arrange();};
        Arrange(); UpdateDisplay();
    }
    private ShellButton ActionButton(string label,string icon,Action action)
    {
        var b=new ShellButton {Text=label,AccessibleName=label,Glyph=icon,GlyphOnly=icon!=null,Pill=true,FlatStyle=FlatStyle.Flat,
            Font=new Font(Style.Family,9f),BackColor=Ui.Surface,ForeColor=Ui.Text,Cursor=Cursors.Hand,TabStop=true};
        b.FlatAppearance.BorderSize=0;b.Click+=delegate{shell.Run(action);};Controls.Add(b);hints.SetToolTip(b,label);return b;
    }
    private void Arrange()
    {
        if(background!=null){background.Dispose();background=null;}
        int margin=Math.Max(16,Math.Min(44,ClientSize.Width/36));
        search.SetBounds(margin,Math.Max(0,ClientSize.Height-margin-33),170,33);
        workspace.SetBounds((ClientSize.Width-56)/2,0,56,27);
        settings.SetBounds(Math.Max(0,ClientSize.Width-36),(ClientSize.Height-40)/2,36,40);
        Invalidate();
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var cp=base.CreateParams;cp.ExStyle|=0x08000080;return cp;}}
    protected override void WndProc(ref Message m){if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);}
    private void ShowRunning()
    {
        foreach(var item in running.Items.Cast<ToolStripItem>().ToArray())item.Dispose();
        var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==Display)??Screen.PrimaryScreen;
        foreach(var w in DesktopWindow.List(screen,true))
        {
            var window=w;running.Items.Add(w.Title.Replace("&","&&"),null,delegate{shell.Run(window.Activate);});
        }
        if(running.Items.Count==0)running.Items.Add("No open windows").Enabled=false;
        running.Show(Cursor.Position);
    }
    internal void UpdateDisplay()
    {
        var path=shell.DesktopImage??"";
        if(loadedPath!=path)
        {
            Image replacement=null;
            try
            {
                if(!String.IsNullOrEmpty(path))
                    using(var file=System.IO.File.OpenRead(path))
                    using(var img=Image.FromStream(file))replacement=new Bitmap(img);
            }
            catch(System.IO.IOException){}
            catch(ArgumentException){}
            catch(OutOfMemoryException){}
            if(replacement==null)
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Bluehour.Coast"))
                using(var img=Image.FromStream(stream))replacement=new Bitmap(img);
            photograph.Dispose();photograph=replacement;loadedPath=path;Arrange();
        }
        var now=DateTime.Now;
        var time=now.ToString(shell.ShowSeconds?"h:mm:ss":"h:mm");
        var day=now.ToString("tt MMMM d dddd");
        if(time==renderedTime&&day==renderedDate&&renderedAccent==shell.Accent)return;
        renderedTime=time;renderedDate=day;renderedAccent=shell.Accent;
        Invalidate(new Rectangle(0,Math.Max(0,Height-220),Width,220));
    }
    internal void BringDesktopForward(){Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x13);}
    private Bitmap Backdrop()
    {
        var buffer=new Bitmap(ClientSize.Width,ClientSize.Height);
        using(var g=Graphics.FromImage(buffer))
        {
            g.Clear(Ui.Night);
            var area=Rectangle.Inflate(ClientRectangle,-7,-7);
            using(var clip=Style.Round(area,25))
            {
                g.SetClip(clip);
                float scale=Math.Max(area.Width/(float)photograph.Width,area.Height/(float)photograph.Height);
                float w=photograph.Width*scale,h=photograph.Height*scale;
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.DrawImage(photograph,area.Left+(area.Width-w)/2,area.Top+(area.Height-h)/2,w,h);
                using(var wash=new LinearGradientBrush(area,Color.FromArgb(0,5,17,23),Color.FromArgb(100,5,17,23),90f))
                {
                    wash.Blend=new Blend {Positions=new[]{0f,.52f,1f},Factors=new[]{0f,0f,1f}};
                    wash.WrapMode=WrapMode.TileFlipXY;g.FillRectangle(wash,area);
                }
                g.ResetClip();
                g.SmoothingMode=SmoothingMode.AntiAlias;
                float x=workspace.Left, right=workspace.Right;
                using(var contour=new GraphicsPath())
                using(var ink=new SolidBrush(Ui.Night))
                {
                    contour.StartFigure();contour.AddLine(x-20,0,right+20,0);
                    contour.AddLine(right+20,0,right+20,7);
                    contour.AddBezier(right+20,7,right+5,7,right+7,26,right-8,26);
                    contour.AddLine(right-8,26,x+8,26);
                    contour.AddBezier(x+8,26,x-7,26,x-5,7,x-20,7);contour.CloseFigure();
                    g.FillPath(ink,contour);
                }            }
        }
        return buffer;
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if(ClientSize.Width<20||ClientSize.Height<20||photograph==null)return;
        if(background==null)background=Backdrop();
        e.Graphics.DrawImageUnscaled(background,0,0);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        var now=DateTime.Now;
        float em=Math.Min(118,Math.Min(Width*.081f,Height*.15f));
        if(shell.ShowSeconds)em*=.76f;
        float margin=Math.Max(18,Math.Min(54,Width*.038f));
        float y=Height-margin-em;
        using(var face=new Font(ShellFonts.Display,em,FontStyle.Regular,GraphicsUnit.Pixel))
        using(var month=new Font(Style.Family,Math.Max(11,em*.165f),FontStyle.Bold,GraphicsUnit.Pixel))
        using(var number=new Font(Style.Family,Math.Max(19,em*.34f),FontStyle.Bold,GraphicsUnit.Pixel))
        using(var fmt=(StringFormat)StringFormat.GenericTypographic.Clone())
        using(var pale=new SolidBrush(Color.FromArgb(222,242,246)))
        using(var accent=new SolidBrush(shell.Accent))
        using(var soft=new SolidBrush(Color.FromArgb(179,213,222)))
        {
            string hour=now.ToString("%h"), minutes=now.ToString(shell.ShowSeconds?":mm:ss":":mm");
            string caption=now.ToString("MMM").ToUpperInvariant()+"  /  "+now.ToString("ddd").ToUpperInvariant();
            float dateW=Math.Max(96,g.MeasureString(caption,month,1000,fmt).Width+6);
            float split=Width-margin-dateW-16;
            float hw=g.MeasureString(hour,face,1000,fmt).Width;
            float mw=g.MeasureString(minutes,face,1000,fmt).Width;
            float x=split-16-hw-mw;
            g.DrawString(hour,face,accent,x,y,fmt);
            g.DrawString(minutes,face,pale,x+hw,y,fmt);
            float top=y+em*.22f;
            g.FillRectangle(accent,split,top,2,em*.76f);
            g.DrawString(caption,month,soft,split+13,top,fmt);
            g.DrawString(now.ToString("dd"),number,pale,split+12,top+em*.21f,fmt);
            g.DrawString(now.ToString("tt"),month,accent,split+14,top+em*.61f,fmt);
        }
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){if(photograph!=null)photograph.Dispose();if(background!=null)background.Dispose();running.Dispose();hints.Dispose();if(ContextMenuStrip!=null)ContextMenuStrip.Dispose();}
        base.Dispose(disposing);
    }
}






