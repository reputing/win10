using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer=System.Windows.Forms.Timer;

// The search and command palette: one box for apps, open windows, Windows settings, files, a calculator, and Bluehour's
// own commands. Empty, it shows what you use most, what is open, and every app A to Z, so nothing is ever hard to find.
internal sealed class Launcher : Form
{
    private readonly Shell shell;
    private readonly ListBox results;
    private readonly SearchField field;
    private readonly Timer fade=new Timer {Interval=15},filesTimer=new Timer {Interval=220};
    private readonly Stopwatch age=Stopwatch.StartNew();
    private readonly Dictionary<string,Bitmap> icons=new Dictionary<string,Bitmap>();
    private string filesFor="";
    private int generation;

    public Launcher(Shell shell)
    {
        this.shell=shell;
        Text="Bluehour Launcher";
        StartPosition=FormStartPosition.Manual;
        ShowInTaskbar=false;
        TopMost=true;
        BackColor=Ui.Night;
        ForeColor=Ui.Text;
        Font=new Font(Style.Family,10);
        KeyPreview=true;
        ClientSize=new Size(680,540);
        var area=shell.Monitor.WorkingArea;
        Location=new Point(area.Left+(area.Width-Width)/2,area.Top+Math.Max(24,(int)(area.Height*.14)));
        var frame=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(14,12,14,8),BackColor=Ui.Night};
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
        frame.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute,30));
        field=new SearchField("Search apps, files, settings and windows, or type a sum") {Dock=DockStyle.Fill};
        frame.Controls.Add(field,0,0);
        results=new ListBox {Dock=DockStyle.Fill,BackColor=Ui.Night,ForeColor=Ui.Text,BorderStyle=BorderStyle.None,
            AccessibleName="Search results",IntegralHeight=false,DrawMode=DrawMode.OwnerDrawVariable,Margin=new Padding(0,6,0,0)};
        Style.DarkScroll(results);
        results.MeasureItem+=delegate(object sender,MeasureItemEventArgs e){e.ItemHeight=Kind(e.Index)=="header"?32:48;};
        results.DrawItem+=DrawItem;
        frame.Controls.Add(results,0,1);
        var hint=Ui.Label("↑ ↓ choose    enter open    > commands    esc close",9,Ui.Faint);
        hint.Anchor=AnchorStyles.Left;hint.Margin=new Padding(8,4,0,0);
        frame.Controls.Add(hint,0,2);
        Controls.Add(frame);
        WindowFrame.Attach(this,"search",false);
        field.Input.TextChanged+=delegate{Query();};
        results.DoubleClick+=delegate{Choose();};
        results.MouseMove+=delegate(object sender,MouseEventArgs e)
        {
            int index=results.IndexFromPoint(e.Location);
            if(index>=0 && index!=results.SelectedIndex && Kind(index)!="header")results.SelectedIndex=index;
        };
        results.MouseClick+=delegate(object sender,MouseEventArgs e)
        {
            int index=results.IndexFromPoint(e.Location);
            if(index>=0 && Kind(index)!="header")Choose();
        };
        KeyDown+=OnKey;
        filesTimer.Tick+=delegate{filesTimer.Stop();FindFiles();};
        // Like any launcher it goes away when you click elsewhere (after a moment, so showing it cannot close it).
        Deactivate+=delegate{if(age.ElapsedMilliseconds>600 && !IsDisposed)Close();};
        // cl-fade-up: the palette fades in while rising a few pixels.
        Opacity=Motion.Enabled?0:1;
        fade.Tick+=delegate
        {
            double t=Motion.Effects.At(Motion.Progress(age.Elapsed.TotalMilliseconds,220));
            Opacity=t;
            if(t>=1){fade.Stop();Opacity=1;}
        };
        Shown+=delegate
        {
            Native.ForceForeground(Handle);
            field.Input.Focus();
            if(Motion.Enabled){age.Restart();fade.Start();}
        };
        Query();
    }

    private string Kind(int index)
    {
        var item=index>=0 && index<results.Items.Count?results.Items[index] as SearchItem:null;
        return item==null?"":item.Kind;
    }

    private void OnKey(object sender,KeyEventArgs e)
    {
        if(e.KeyCode==Keys.Escape){e.SuppressKeyPress=true;Close();return;}
        if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Choose();return;}
        int step=e.KeyCode==Keys.Up?-1:e.KeyCode==Keys.Down?1:e.KeyCode==Keys.PageUp?-8:e.KeyCode==Keys.PageDown?8:0;
        if(step!=0 && results.Items.Count>0)
        {
            e.SuppressKeyPress=true;
            Nudge(step);
        }
        else if(e.KeyCode==Keys.Home && field.Input.Text.Length==0){e.SuppressKeyPress=true;Select(0);}
    }

    // Moves the highlight, stepping over the section headings.
    private void Nudge(int step)
    {
        int target=Math.Max(0,Math.Min(results.Items.Count-1,results.SelectedIndex+step));
        int direction=step<0?-1:1;
        while(target>=0 && target<results.Items.Count && Kind(target)=="header")target+=direction;
        if(target<0 || target>=results.Items.Count)return;
        results.SelectedIndex=target;
    }

    private void Select(int from)
    {
        for(int i=from;i<results.Items.Count;i++)
            if(Kind(i)!="header"){results.SelectedIndex=i;return;}
    }

    private void Query()
    {
        generation++;
        filesTimer.Stop();
        string text=field.Input.Text;
        Fill(shell.Search(text));
        filesFor=text.Trim();
        if(filesFor.Length>=2 && !filesFor.StartsWith(">",StringComparison.Ordinal))filesTimer.Start();
    }

    private void Fill(List<SearchItem> items)
    {
        results.BeginUpdate();
        results.Items.Clear();
        results.Items.AddRange(items.ToArray());
        Select(0);
        results.EndUpdate();
    }

    // Files come from the Windows Search index on a worker thread and are added when they arrive, if the question has not
    // changed in the meantime.
    private void FindFiles()
    {
        string term=filesFor;
        int mine=generation;
        Task.Run(()=>FileSearch.Query(term,8)).ContinueWith(t=>
        {
            if(IsDisposed || !IsHandleCreated || t.IsFaulted)return;
            var files=t.Result;
            try{BeginInvoke((Action)(()=>AddFiles(mine,files)));}
            catch(InvalidOperationException){}
        });
    }

    private void AddFiles(int mine,List<SearchItem> files)
    {
        if(IsDisposed || mine!=generation || files.Count==0)return;
        var chosen=results.SelectedItem;
        var items=results.Items.Cast<object>().ToList();
        items.Add(new SearchItem {Name="files",Kind="header"});
        items.AddRange(files.Cast<object>());
        results.BeginUpdate();
        results.Items.Clear();
        results.Items.AddRange(items.ToArray());
        int again=chosen==null?-1:results.Items.IndexOf(chosen);
        if(again>=0)results.SelectedIndex=again;else Select(0);
        results.EndUpdate();
    }

    private Bitmap IconFor(SearchItem item)
    {
        if(item.Pin==null || item.Pin.Target.StartsWith("builtin:",StringComparison.Ordinal))return null;
        Bitmap bitmap;
        if(icons.TryGetValue(item.Pin.Target,out bitmap))return bitmap;
        bitmap=AppLibrary.Picture(item.Pin);
        icons[item.Pin.Target]=bitmap;
        return bitmap;
    }

    private static string GlyphFor(SearchItem item)
    {
        if(item.Pin!=null && item.Pin.Target.StartsWith("builtin:",StringComparison.Ordinal))
            return item.Pin.Target=="builtin:web"?"Browser":item.Pin.Target=="builtin:files"?"Files":"Discord";
        return item.Name=="Calculator"||item.Name=="Notepad"||item.Name=="Paint"?item.Name:item.Glyph;
    }

    private void DrawItem(object sender,DrawItemEventArgs e)
    {
        if(e.Index<0)return;
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var paint=new SolidBrush(Ui.Night))g.FillRectangle(paint,e.Bounds);
        var item=results.Items[e.Index] as SearchItem;
        if(item==null)return;
        if(item.Kind=="header")
        {
            TextRenderer.DrawText(g,Ui.Voice(item.Name),Font,new Rectangle(e.Bounds.X+18,e.Bounds.Y+6,e.Bounds.Width-36,e.Bounds.Height-6),Ui.Faint,
                TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
            return;
        }
        bool selected=(e.State&DrawItemState.Selected)!=0;
        var row=new RectangleF(e.Bounds.X+2,e.Bounds.Y+3,e.Bounds.Width-4,e.Bounds.Height-6);
        if(selected)
        {
            Style.Fill(g,row,16,Ui.AccentContainer(shell.AccentName));
            using(var outline=Style.Round(RectangleF.Inflate(row,-1,-1),15))
            using(var pen=new Pen(shell.Accent,2f))g.DrawPath(pen,outline);
        }
        var box=new RectangleF(row.X+14,row.Y+(row.Height-28)/2,28,28);
        var picture=IconFor(item);
        var tint=selected?Ui.OnAccentContainer(shell.AccentName):shell.Accent;
        if(picture!=null){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(picture,box);}
        else if(item.Kind=="app" && item.Pin!=null && !item.Pin.Target.StartsWith("builtin:",StringComparison.Ordinal) &&
            item.Name!="Calculator" && item.Name!="Notepad" && item.Name!="Paint")
        {
            // No icon to show: a tile with the app's first letter.
            Style.Fill(g,box,8,selected?Ui.Raised:Ui.AccentContainer(shell.AccentName));
            using(var letter=new Font(Style.Family,13,FontStyle.Bold,GraphicsUnit.Pixel))
                TextRenderer.DrawText(g,item.Name.Substring(0,1).ToUpperInvariant(),letter,Rectangle.Round(box),tint,
                    TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        }
        else Style.Icon(g,GlyphFor(item),box,tint);
        var ink=selected?Ui.OnAccentContainer(shell.AccentName):Ui.Text;
        TextRenderer.DrawText(g,item.Name,Font,new Rectangle((int)row.X+56,(int)row.Y,(int)(row.Width*.55f)-56,(int)row.Height),ink,
            TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
        using(var small=new Font(Style.Family,9))
            TextRenderer.DrawText(g,item.Detail,small,new Rectangle((int)(row.X+row.Width*.55f),(int)row.Y,(int)(row.Width*.45f)-16,(int)row.Height),
                selected?Ui.OnAccentContainer(shell.AccentName):Ui.Muted,
                TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.Right|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
    }

    private void Choose()
    {
        var item=results.SelectedItem as SearchItem;
        if(item==null || item.Kind=="header" || item.Run==null)return;
        Close();
        shell.RecordUse(item.Key);
        shell.Run(item.Run);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            fade.Dispose();filesTimer.Dispose();
            foreach(var icon in icons.Values)if(icon!=null)icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
