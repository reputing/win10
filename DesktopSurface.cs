using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

// The desktop Bluehour draws over Explorer's: a full-bleed wallpaper, a layout switcher and search at the top, a clock
// at the bottom right, and app stickers down the left. The dock floats in the strip along the bottom edge.
internal sealed class DesktopSurface : Form
{
    internal const int TopBar=64;
    private static readonly double[][] Twinkles=
    {
        new[]{.62,.11,6},new[]{.79,.2,4},new[]{.91,.09,5},new[]{.43,.07,4},new[]{.27,.2,3},new[]{.7,.5,4},new[]{.5,.66,3},new[]{.36,.78,5},new[]{.88,.6,3}
    };

    private readonly Shell shell;
    private Image photograph;
    private string loadedPath;
    private Bitmap background;
    private readonly Size panelSize;
    private readonly ShellButton search, settings;
    private readonly LayoutPill layouts;
    private readonly ToolTip hints=Ui.Tip();
    private readonly ContextMenuStrip running=Ui.Menu();
    private readonly ContextMenuStrip blank;
    private readonly StickerField stickers;
    private string renderedTime, renderedDate, renderedLayout, renderedMeter="";
    private readonly SystemMeter meter=new SystemMeter();
    private Color renderedAccent;
    internal readonly string Display;

    internal DesktopSurface(Shell shell,Screen screen)
    {
        this.shell=shell; Display=screen.DeviceName; panelSize=screen.Bounds.Size;
        Text="Bluehour Desktop Surface"; ShowInTaskbar=false;
        FormBorderStyle=FormBorderStyle.None; StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None;
        BackColor=Ui.Night; ForeColor=Ui.Text; DoubleBuffered=true;
        loadedPath="";
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Bluehour.Coast"))
        using(var image=Image.FromStream(stream)) photograph=Fit(image);
        search=ActionButton("search   ctrl + alt + k","Search",shell.OpenLauncher,false);
        settings=ActionButton("Control center","Settings",()=>shell.ShowPage("Dashboard"),true);
        layouts=new LayoutPill(shell);
        Controls.Add(layouts);
        ContextMenuStrip=blank=Ui.Menu();
        ContextMenuStrip.Items.Add("pin an app…",null,delegate{shell.Run(shell.PickApp);});
        ContextMenuStrip.Items.Add("open windows",null,delegate{shell.Run(ShowRunning);});
        ContextMenuStrip.Items.Add("open files",null,delegate{shell.Run(()=>shell.OpenApp(AppLibrary.Builtins[1]));});
        ContextMenuStrip.Items.Add("appearance",null,delegate{shell.Run(()=>shell.ShowPage("Appearance"));});
        ContextMenuStrip.Items.Add(new ToolStripSeparator());
        ContextMenuStrip.Items.Add("return to windows",null,delegate{shell.Run(shell.ReturnToWindows);});
        Bounds=screen.Bounds;stickers=new StickerField(shell,this);
        Resize+=delegate{Arrange();};
        Arrange(); ApplyWidgets();
    }

    private ShellButton ActionButton(string label,string icon,Action action,bool glyphOnly)
    {
        var b=new ShellButton {Text=label,AccessibleName=label,Glyph=icon,GlyphOnly=glyphOnly,Pill=true,FlatStyle=FlatStyle.Flat,
            Font=new Font(Style.Family,9.5f,FontStyle.Bold),BackColor=Ui.Surface,ForeColor=Ui.Text,Cursor=Cursors.Hand,TabStop=true};
        b.FlatAppearance.BorderSize=0;b.Click+=delegate{Diag.Log("desktop button: "+label);shell.Run(action);};Controls.Add(b);hints.SetToolTip(b,Ui.Voice(label));return b;
    }

    // The part of the display that windows use, in this form's coordinates; the dock's strip lies outside it.
    internal Rectangle Work
    {
        get
        {
            var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==Display);
            if(screen==null)return ClientRectangle;
            var area=Rectangle.Intersect(new Rectangle(screen.WorkingArea.X-Bounds.X,screen.WorkingArea.Y-Bounds.Y,screen.WorkingArea.Width,screen.WorkingArea.Height),ClientRectangle);
            return area.IsEmpty?ClientRectangle:area;
        }
    }

    private void Arrange()
    {
        if(background!=null){background.Dispose();background=null;}
        int margin=Math.Max(16,Math.Min(40,Width/48));
        var work=Work;
        layouts.Location=new Point(work.Left+(work.Width-layouts.Width)/2,work.Top+2);
        settings.SetBounds(work.Right-margin-48,work.Top+10,48,48);
        search.SetBounds(settings.Left-10-224,work.Top+10,224,48);
        Invalidate(true);   // the buttons paint a slice of the wallpaper as their background, so they must repaint with it
    }
    internal StickerField Stickers{get{return stickers;}}
    internal ContextMenuStrip DesktopMenu{get{return blank;}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);stickers.MouseMove(e.Location);}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);stickers.MouseLeave();}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);stickers.MouseDown(e.Button,e.Location);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);stickers.MouseUp(e.Button,e.Location);}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)stickers.CaptureLost();}
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
        if(running.Items.Count==0)running.Items.Add("no open windows").Enabled=false;
        running.Show(Cursor.Position);
    }
    // The palette changed: the wallpaper's wash and everything painted on it is made again.
    internal void Retint()
    {
        BackColor=Ui.Night;
        if(background!=null){background.Dispose();background=null;}
        renderedTime=null;
        Invalidate(true);
    }

    // Shows or hides the top-bar buttons, the stickers and the clock group, as chosen in Appearance.
    internal void ApplyWidgets()
    {
        var options=shell.Prefs;
        layouts.Visible=!options.HideLayouts;search.Visible=!options.HideSearch;settings.Visible=!options.HideSettings;
        stickers.Shown=!options.HideStickers;
        renderedTime=null;
        UpdateDisplay();
        Invalidate(true);
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
                    using(var img=Image.FromStream(file))replacement=Fit(img);
            }
            catch(System.IO.IOException){}
            catch(ArgumentException){}
            catch(OutOfMemoryException){}
            if(replacement==null)
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Bluehour.Coast"))
                using(var img=Image.FromStream(stream))replacement=Fit(img);
            photograph.Dispose();photograph=replacement;loadedPath=path;Arrange();
        }
        layouts.Sync();
        string hint=shell.WinKeyOn?"search   win":"search   ctrl + alt + k";
        if(search.Text!=hint){search.Text=hint;search.AccessibleName=hint;search.Invalidate();}
        var now=DateTime.Now;
        var time=now.ToString(shell.ShowSeconds?"h:mm:ss":"h:mm");
        var day=now.ToString("tt MMMM d dddd");
        string reading=shell.Prefs.ShowMeter?meter.Read():"";
        if(time==renderedTime&&day==renderedDate&&renderedAccent==shell.Accent&&renderedLayout==shell.ActiveLayout&&reading==renderedMeter)return;
        renderedTime=time;renderedDate=day;renderedAccent=shell.Accent;renderedLayout=shell.ActiveLayout;renderedMeter=reading;
        var work=Work;
        Invalidate(new Rectangle(work.Left,Math.Max(0,work.Bottom-320),work.Width,320));
    }
    // Decodes a wallpaper at the display's size. A 4K original would otherwise stay in memory, uncompressed, for as long
    // as Bluehour runs.
    private Bitmap Fit(Image source)
    {
        double k=Math.Max(panelSize.Width/(double)source.Width,panelSize.Height/(double)source.Height);
        if(k>=1)return new Bitmap(source);
        var result=new Bitmap((int)Math.Ceiling(source.Width*k),(int)Math.Ceiling(source.Height*k));
        using(var g=Graphics.FromImage(result))
        {
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.DrawImage(source,0,0,result.Width,result.Height);
        }
        return result;
    }

    // Stickers and the layout switcher draw their own background straight from the cached wallpaper. Letting WinForms do
    // it for them repaints this whole form (clock included) behind every frame of every hover animation.
    internal void PaintBackdropFor(Control child,Graphics g)
    {
        if(photograph==null || child.Width<1 || child.Height<1)return;
        if(background==null)background=Backdrop();
        g.DrawImage(background,new Rectangle(0,0,child.Width,child.Height),child.Left,child.Top,child.Width,child.Height,GraphicsUnit.Pixel);
    }

    // Puts the desktop above Explorer's own but beneath every app window, so apps tiled on top of it stay visible.
    internal void BringDesktopForward()
    {
        IntPtr after=IntPtr.Zero;   // HWND_TOP when no app is open
        foreach(var window in DesktopWindow.List(null,false))
            if((Native.GetWindowLong(window.Handle,-20)&8)==0)after=window.Handle;   // the lowest window that is not topmost
        Native.SetWindowPos(Handle,after,0,0,0,0,0x13);
    }

    // The wallpaper, covering the whole display, with a violet wash for legibility and a few sparkles.
    private Bitmap Backdrop()
    {
        var buffer=new Bitmap(ClientSize.Width,ClientSize.Height);
        using(var g=Graphics.FromImage(buffer))
        {
            g.Clear(Ui.Night);
            var area=ClientRectangle;
            float scale=Math.Max(area.Width/(float)photograph.Width,area.Height/(float)photograph.Height);
            float w=photograph.Width*scale,h=photograph.Height*scale;
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.DrawImage(photograph,area.Left+(area.Width-w)/2,area.Top+(area.Height-h)/2,w,h);
            // Dark at the top (for the layout switcher) and bottom (for the clock), clear in between.
            using(var wash=new LinearGradientBrush(area,Color.Transparent,Color.Transparent,90f))
            {
                wash.InterpolationColors=new ColorBlend
                {
                    Positions=new[]{0f,.2f,.55f,1f},
                    Colors=new[]{Color.FromArgb(120,Ui.Night),Color.FromArgb(0,Ui.Night),Color.FromArgb(0,Ui.Night),Color.FromArgb(200,Ui.Night)}
                };
                g.FillRectangle(wash,area);
            }
            g.SmoothingMode=SmoothingMode.AntiAlias;
            foreach(var t in Twinkles)
                Style.DrawSparkle(g,(float)(area.Width*t[0]),(float)(area.Height*t[1]),(float)t[2]*Math.Max(1f,area.Width/1920f),Color.FromArgb(150,Ui.Tertiary(Ui.Current)));
        }
        return buffer;
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if(ClientSize.Width<20||ClientSize.Height<20||photograph==null)return;
        if(background==null)background=Backdrop();
        e.Graphics.DrawImageUnscaled(background,0,0);
    }

    private static string Greeting(DateTime now)
    {
        string who=Environment.UserName.ToLowerInvariant();
        string part=now.Hour<5?"still up":now.Hour<12?"good morning":now.Hour<18?"good afternoon":"good evening";
        return who.Length==0?part:part+", "+who;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        g.TextRenderingHint=TextRenderingHint.AntiAlias;
        var work=Work;
        stickers.Paint(g,e.ClipRectangle);
        // The clock and greeting are drawn here; skip the work when something else is being repainted.
        if(!e.ClipRectangle.IntersectsWith(new Rectangle(work.Left+work.Width/2,Math.Max(0,work.Bottom-360),work.Width/2,360)))return;
        var now=DateTime.Now;
        var accent=shell.Accent;
        var options=shell.Prefs;
        float margin=Math.Max(22,Math.Min(56,work.Width*.032f));
        float right=work.Right-margin;
        float em=Math.Min(150,Math.Min(work.Width*.092f,work.Height*.19f));
        if(shell.ShowSeconds)em*=.78f;
        float scale=Math.Max(.75f,work.Width/1920f);
        // chips along the bottom (left to right: the system reading, then day, date and am/pm), then the time above them,
        // then the greeting
        var chips=new System.Collections.Generic.List<string>();
        if(options.ShowMeter && renderedMeter!="")chips.Add(renderedMeter);
        int firstDate=chips.Count;
        if(!options.HideDate){chips.Add(now.ToString("ddd").ToLowerInvariant());chips.Add(now.ToString("MMM d").ToLowerInvariant());chips.Add(now.ToString("tt").ToLowerInvariant());}
        float chipH=Math.Max(28,32*scale),chipY=work.Bottom-margin-chipH;
        float chipTop=chips.Count>0?chipY:work.Bottom-margin;
        using(var chipFont=new Font(Style.Family,Math.Max(13,14*scale),FontStyle.Bold,GraphicsUnit.Pixel))
        using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
        {
            float x=right;
            for(int i=chips.Count-1;i>=0;i--)
            {
                float w=g.MeasureString(chips[i],chipFont,PointF.Empty,format).Width+chipH*.9f;
                x-=w;
                bool filled=!options.HideDate && i==chips.Count-1;
                var box=new RectangleF(x,chipY,w,chipH);
                Style.Fill(g,new RectangleF(box.X,box.Y+3,box.Width,box.Height),chipH/2,Color.FromArgb(245,Ui.EdgeDeep));
                Style.Fill(g,box,chipH/2,filled?accent:Ui.Surface);
                if(!filled)using(var outline=Style.Round(RectangleF.Inflate(box,-1,-1),chipH/2-1))using(var pen=new Pen(Ui.Edge,2f))g.DrawPath(pen,outline);
                using(var brush=new SolidBrush(filled?Ui.Night:Ui.Text))
                    g.DrawString(chips[i],chipFont,brush,box.X+chipH*.45f,box.Y+(chipH-chipFont.Size*1.25f)/2,format);
                x-=8;
            }
        }
        if(options.HideClock && options.HideGreeting)return;
        using(var face=new Font(ShellFonts.Display,em,FontStyle.Regular,GraphicsUnit.Pixel))
        using(var greet=new Font(Style.Family,Math.Max(14,17*scale),FontStyle.Bold,GraphicsUnit.Pixel))
        using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
        {
            string hour=now.ToString("%h"),minutes=now.ToString(shell.ShowSeconds?":mm:ss":":mm");
            float hw=g.MeasureString(hour,face,1000,format).Width,mw=g.MeasureString(minutes,face,1000,format).Width;
            float y=chipTop-em*1.08f;
            float x=right-hw-mw;
            float drop=Math.Max(3,em*.04f);
            if(!options.HideClock)
            {
                Style.ShadowText(g,hour,face,accent,Ui.EdgeDeep,new PointF(x,y),drop,format);
                Style.ShadowText(g,minutes,face,Ui.Text,Ui.EdgeDeep,new PointF(x+hw,y),drop,format);
            }
            else y=chipTop-greet.Size*1.2f+greet.Size*1.7f;
            if(!options.HideGreeting)
            {
                string line=Greeting(now);
                float gw=g.MeasureString(line,greet,1000,format).Width;
                using(var brush=new SolidBrush(Ui.Muted))g.DrawString(line,greet,brush,right-gw,y-greet.Size*1.7f,format);
                Style.DrawSparkle(g,right-gw-14,y-greet.Size*1.7f+greet.Size*.6f,5,Ui.Tertiary(shell.AccentName));
            }
        }
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){stickers.Dispose();if(photograph!=null)photograph.Dispose();if(background!=null)background.Dispose();running.Dispose();hints.Dispose();if(ContextMenuStrip!=null)ContextMenuStrip.Dispose();}
        base.Dispose(disposing);
    }
}
