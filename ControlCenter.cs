using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

// Accent colours as round swatches: one click, and you can see what you are choosing.
internal sealed class SwatchRow : Control
{
    private static readonly string[] Names={"Lavender","Sakura","Mint","Custom"};
    private readonly Func<string> current;
    private readonly Action<string> choose;
    private int hot=-1;

    internal SwatchRow(Func<string> current,Action<string> choose)
    {
        this.current=current;this.choose=choose;
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.Selectable,true);
        Size=new Size(Names.Length*74+8,74);Margin=new Padding(0,6,0,6);Cursor=Cursors.Hand;TabStop=true;
        AccessibleName="Accent color";AccessibleRole=AccessibleRole.RadioButton;
        Font=new Font(Style.Family,9,FontStyle.Bold);
    }

    private RectangleF Disc(int index){return new RectangleF(10+index*74,4,40,40);}
    private int Hit(Point point){for(int i=0;i<Names.Length;i++)if(RectangleF.Inflate(Disc(i),12,10).Contains(point))return i;return -1;}

    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int i=Hit(e.Location);if(i!=hot){hot=i;Invalidate();}}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=-1;Invalidate();}
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int i=Hit(e.Location);
        if(e.Button==MouseButtons.Left && i>=0)choose(Names[i]);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent==null?Ui.Night:Parent.BackColor.A==255?Parent.BackColor:Ui.Night);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAlias;
        string chosen=current();
        for(int i=0;i<Names.Length;i++)
        {
            var disc=Disc(i);
            float grow=i==hot?3:0;
            var box=RectangleF.Inflate(disc,grow,grow);
            Color color=Ui.Accent(Names[i]);
            bool selected=Names[i]==chosen;
            Style.Fill(g,new RectangleF(box.X,box.Y+3,box.Width,box.Height),box.Width/2,Color.FromArgb(235,Ui.EdgeDeep));
            Style.Fill(g,box,box.Width/2,color);
            if(selected)
                using(var ring=new Pen(Ui.Text,3f))g.DrawEllipse(ring,RectangleF.Inflate(box,5,5).X,RectangleF.Inflate(box,5,5).Y,box.Width+10,box.Height+10);
            if(Names[i]=="Custom")Style.Icon(g,"Settings",RectangleF.Inflate(box,-9,-9),Ui.Night);
            else if(selected)Style.Icon(g,"Cleanup",RectangleF.Inflate(box,-9,-9),Ui.Night);
            string label=Ui.Voice(Names[i]=="Custom"?"your color":Names[i]);
            using(var brush=new SolidBrush(selected?Ui.Text:Ui.Muted))
            using(var format=new StringFormat{Alignment=StringAlignment.Center})
                g.DrawString(label,Font,brush,new RectangleF(disc.X-14,disc.Bottom+10,disc.Width+28,18),format);
        }
        if(Focused&&ShowFocusCues)using(var pen=new Pen(Ui.Accent(Ui.Current),2f))g.DrawRectangle(pen,1,1,Width-3,Height-3);
    }
}

// Sends the mouse wheel to whatever is under the pointer. Windows Forms only gives the wheel to the control that has
// focus, so scrolling a settings page with the pointer over a switch or button did nothing.
internal sealed class WheelRouter : IMessageFilter
{
    private readonly Shell shell;
    private readonly ScrollableControl body;
    private readonly Func<double,bool> pace;
    private float current,goal;
    internal WheelRouter(Shell shell,ScrollableControl body){this.shell=shell;this.body=body;pace=Glide;}

    // The page eases to where the wheel sent it instead of jumping, on the shared display-synchronised clock.
    private bool Glide(double dt)
    {
        if(!body.IsHandleCreated || body.IsDisposed)return false;
        current=Anim.Follow(current,goal,.3f,dt);
        bool done=Math.Abs(goal-current)<.6f;
        if(done)current=goal;
        int y=(int)Math.Round(current);
        if(-body.AutoScrollPosition.Y!=y)body.AutoScrollPosition=new Point(-body.AutoScrollPosition.X,y);
        return !done;
    }

    public bool PreFilterMessage(ref Message m)
    {
        if(m.Msg!=0x020A || !shell.Visible || !body.IsHandleCreated)return false;   // WM_MOUSEWHEEL
        var under=Control.FromHandle(Native.WindowFromPoint(Cursor.Position));
        if(under==null || (under!=body && !body.Contains(under)))return false;
        // Controls that scroll themselves keep the wheel.
        if(under is PinBoard || under is GameList || under is ListBox || under is TextBoxBase)return false;
        int delta=(short)((long)m.WParam>>16);
        int limit=Math.Max(0,body.VerticalScroll.Maximum-body.VerticalScroll.LargeChange+1);
        if(Math.Abs(goal-current)<.6f)goal=current=-body.AutoScrollPosition.Y;   // start from where the page really is
        goal=Math.Max(0,Math.Min(limit,goal-delta/120f*90));
        if(!Motion.Enabled){current=goal;body.AutoScrollPosition=new Point(-body.AutoScrollPosition.X,(int)goal);}
        else Anim.Add(pace);
        return true;
    }
}

internal sealed partial class Shell
{
    private WheelRouter wheel;
    private TextBox settingsFilter;
    private FlowLayoutPanel navPanel;
    private readonly List<Control> found=new List<Control>();

    // Every setting, so one search box finds them all: the page it lives on, its exact label, and extra words.
    internal static readonly string[][] Index=
    {
        new[]{"Dashboard","Switch to Bluehour desktop","start desktop"},new[]{"Dashboard","Return to Windows","exit stop"},
        new[]{"Dashboard","Open Discord","chat"},new[]{"Dashboard","Pin an app…","add sticker"},new[]{"Dashboard","Import desktop shortcuts","icons"},
        new[]{"Dashboard","Free memory now","ram boost"},new[]{"Dashboard","Task Manager","processes"},new[]{"Dashboard","Calendar","date"},
        new[]{"Dashboard","Lock Windows","lock"},new[]{"Dashboard","Play / pause","music media"},
        new[]{"Workspaces","Monitor","display screen"},new[]{"Workspaces","Window layout","grid focus columns rows cascade tile"},
        new[]{"Workspaces","Arrange checked windows","tile"},new[]{"Workspaces","Undo placement","restore"},
        new[]{"Workspaces","Tile windows automatically when they open or close","auto"},
        new[]{"Performance","Rest Bluehour automatically while a game or full-screen app is open","game mode"},
        new[]{"Performance","Optimize automatically when a game starts","game optimizer fps"},
        new[]{"Performance","Raise the game's priority","cpu"},new[]{"Performance","Step background apps back","priority"},
        new[]{"Performance","Hand memory to the game","ram"},new[]{"Performance","High performance power plan while playing","power"},
        new[]{"Performance","Free memory now","ram boost"},new[]{"Performance","Find my games","steam epic"},
        new[]{"Performance","Add a game…","exe"},new[]{"Performance","Window frames","border native sticker"},
        new[]{"Performance","Animations","motion"},new[]{"Performance","Window movement","smooth quick glide"},
        new[]{"Performance","Apply gaming settings","game bar dvr"},new[]{"Performance","Turn off transparency","effects"},
        new[]{"Performance","Turn off mouse acceleration","mouse"},
        new[]{"Appearance","Keep dock visible on Windows desktop","dock"},new[]{"Appearance","Clock seconds","time"},
        new[]{"Appearance","Open dashboard on dock hover","hover"},new[]{"Appearance","Replace the Windows taskbar with the Bluehour dock","taskbar hide"},
        new[]{"Appearance","Dock edge","left right bottom"},new[]{"Appearance","Dock size","small large"},
        new[]{"Appearance","Show running apps in the dock","dock apps"},new[]{"Appearance","Show calendar, volume and network buttons","dock system"},
        new[]{"Appearance","Accent color","colors theme tint swatch"},new[]{"Appearance","Pick any color…","custom"},
        new[]{"Appearance","Clock","widget"},new[]{"Appearance","Greeting","widget"},new[]{"Appearance","Date","widget"},
        new[]{"Appearance","CPU and memory meter","widget system"},new[]{"Appearance","App stickers","icons widget"},
        new[]{"Appearance","Layout switcher","widget top"},new[]{"Appearance","Search button","widget top"},
        new[]{"Appearance","Control center button","widget top"},
        new[]{"Appearance","Windows key opens the Bluehour launcher instead of Start","win key start menu"},
        new[]{"Appearance","Apply palette to Windows","dark mode accent"},new[]{"Appearance","Dark mode","theme"},
        new[]{"Appearance","Light mode","theme"},new[]{"Appearance","Use Bluehour wallpaper","background"},
        new[]{"Appearance","Choose image or video…","wallpaper live mp4"},new[]{"Appearance","Stop live wallpaper","video"},
        new[]{"Appearance","Start Bluehour when I sign in","startup login"},new[]{"Appearance","Reset appearance","defaults"},
        new[]{"Cleanup","Disable Game DVR capture","recording"},new[]{"Cleanup","Windows Startup Apps","boot"},
        new[]{"Cleanup","Review removal","uninstall store apps"},new[]{"Cleanup","Install Discord","download"},
        new[]{"Recovery","Revert Bluehour changes","undo restore"},new[]{"Recovery","Create and verify restore point","backup"},
        new[]{"Recovery","Open System Restore","rstrui"},new[]{"Recovery","Open diagnostics log","debug problems"},
        new[]{"Recovery","Open data folder","journal"},
    };

    // The best matches for what was typed: (page, label), best first.
    internal static List<string[]> FindSettings(string query,int limit)
    {
        var words=(query??"").ToLowerInvariant().Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
        var hits=new List<KeyValuePair<double,string[]>>();
        if(words.Length==0)return new List<string[]>();
        foreach(var entry in Index)
        {
            double label=Match.Score(words,entry[1].ToLowerInvariant());
            double extra=Match.Score(words,entry[2])*.7,page=Match.Score(words,entry[0].ToLowerInvariant())*.4;
            double best=Math.Max(label,Math.Max(extra,page));
            if(best>=0)hits.Add(new KeyValuePair<double,string[]>(best,entry));
        }
        return hits.OrderByDescending(h=>h.Key).Take(limit).Select(h=>h.Value).ToList();
    }

    // Sets up what the control center needs beyond its pages: the settings search, the wheel, the shortcuts.
    private void ConfigureControlCenter(FlowLayoutPanel nav,TextBox filter)
    {
        navPanel=nav;settingsFilter=filter;
        wheel=new WheelRouter(this,body);
        Application.AddMessageFilter(wheel);
    }

    private void ShowSettingResults(string text)
    {
        navPanel.SuspendLayout();
        foreach(var control in found){navPanel.Controls.Remove(control);control.Dispose();}
        found.Clear();
        text=(text??"").Trim();
        bool searching=text.Length>0;
        foreach(var tab in tabs.Values)tab.Visible=!searching;
        if(searching)
        {
            var hits=FindSettings(text,7);
            foreach(var hit in hits)
            {
                string page=hit[0],label=hit[1];
                var result=new ShellButton {Text=label,AccessibleName=label,Glyph=page=="Workspaces"?"Layouts":page=="Dashboard"?"Desktop":page=="Appearance"?"Settings":page=="Performance"?"Bolt":page,
                    Detail=Ui.Voice(page),AutoSize=false,Size=new Size(216,62),Margin=new Padding(0,0,0,6),FlatStyle=FlatStyle.Flat,
                    Font=new Font(Style.Family,10),BackColor=Ui.Raised,ForeColor=Ui.Text,TextAlign=ContentAlignment.MiddleLeft,Cursor=Cursors.Hand};
                result.FlatAppearance.BorderSize=0;
                result.Click+=delegate{Run(()=>GoToSetting(page,label));};
                navPanel.Controls.Add(result);found.Add(result);
            }
            if(hits.Count==0)
            {
                var none=Ui.Label("nothing matches that",10,Ui.Muted);
                none.Margin=new Padding(8,10,0,0);
                navPanel.Controls.Add(none);found.Add(none);
            }
        }
        navPanel.ResumeLayout(true);
    }

    // Opens the page a setting lives on, scrolls it into view and gives it focus.
    internal void GoToSetting(string page,string label)
    {
        settingsFilter.Clear();
        ShowPage(page);
        var target=FindControl(body,label);
        if(target!=null)
        {
            body.ScrollControlIntoView(target);
            if(target.CanSelect)target.Focus();
        }
    }

    // For the self-test: does the page really have a control with this label?
    internal bool HasSetting(string page,string label){Render(page);return FindControl(body,label)!=null;}

    private static Control FindControl(Control root,string label)
    {
        foreach(Control child in root.Controls)
        {
            if(child.AccessibleName==label || child.Text==label)return child;
            var inner=FindControl(child,label);
            if(inner!=null)return inner;
        }
        return null;
    }

    // Keyboard: Ctrl + 1..6 pick a page, Ctrl + F searches the settings, Esc clears the search and then closes the window.
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(keyData==(Keys.Control|Keys.F)){settingsFilter.Focus();settingsFilter.SelectAll();return true;}
        if(keyData>=(Keys.Control|Keys.D1) && keyData<=(Keys.Control|Keys.D6))
        {
            int index=(int)(keyData&Keys.KeyCode)-(int)Keys.D1;
            if(index<Pages.Length){settingsFilter.Clear();ShowPage(Pages[index]);return true;}
        }
        if(keyData==Keys.Escape)
        {
            if(settingsFilter.Text.Length>0){settingsFilter.Clear();return true;}
            Hide();return true;
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }

    // ---- Smaller things ------------------------------------------------------------------------------------------

    private void ResetAppearance()
    {
        if(MessageBox.Show(this,"Put the colors, desktop widgets and dock options back to their defaults?","Reset appearance",
            MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK)return;
        var before=Ui.Palette();
        State.HideClock=State.HideGreeting=State.HideDate=State.HideLayouts=State.HideSearch=State.HideSettings=State.HideStickers=false;
        State.HideDockSystem=State.HideDockApps=State.ShowMeter=false;
        State.DockSize="Medium";State.Accent="Lavender";State.CustomAccent="#C9A4FF";
        Ui.UseCustom(State.CustomAccent);
        recovery.Save();
        Ui.Theme("Lavender");
        UpdateDock();
        Retheme(before);
        Say("Appearance reset.");
    }

    private void OpenDiagnostics()
    {
        string log=Path.Combine(Recovery.Root,"diagnostics.log");
        if(File.Exists(log))Open(log);
        else Say("There is no diagnostics log yet; Bluehour writes one when something goes wrong.");
    }

    // The five layouts as buttons, the current one highlighted.
    private Control LayoutButtons()
    {
        var row=Ui.Flow(false);
        row.SuspendLayout();
        foreach(var mode in Modes)
        {
            string name=mode;
            var button=(ShellButton)B(Ui.Voice(mode),()=>{ChooseLayout(name);Render("Dashboard");});
            button.Glyph=mode;
            if(State.Layout==mode){button.BackColor=Ui.AccentContainer(State.Accent);button.ForeColor=Ui.OnAccentContainer(State.Accent);}
            row.Controls.Add(button);
        }
        row.ResumeLayout(false);
        return row;
    }
}
