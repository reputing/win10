using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

// The game optimizer: what Bluehour does when a game comes to the front, the games it knows, and per-game settings.
internal sealed partial class Shell
{
    private readonly Optimizer optimizer=new Optimizer();
    private bool powerForGame;

    // A game came to the front (Bluehour has just gone to rest): find its profile and make the PC work for it.
    private async void BeginGame()
    {
        if (test || !State.AutoOptimize) return;
        int state;
        var window=FullScreen.Window(out state);
        if (window==IntPtr.Zero && manualRest) { window=Native.GetForegroundWindow(); state=0; }
        if (window==IntPtr.Zero) return;
        uint pid;
        Native.GetWindowThreadProcessId(window,out pid);
        if (pid==(uint)Process.GetCurrentProcess().Id) return;
        string exe=FullScreen.ProgramOf(pid);
        if (exe==null) return;
        var profile=State.Games.FirstOrDefault(g=>g.Matches(exe));
        if (profile==null)
        {
            // Only programs that look like games are learned on their own; a full-screen video is just left alone.
            if (!manualRest && !GameLibrary.LooksLikeGame(exe,state)) return;
            profile=new GameProfile {Name=GameLibrary.NameOf(exe),Exe=exe};
            State.Games.Add(profile);
        }
        if (profile.Level=="Off") return;
        if (!String.Equals(profile.Exe,exe,StringComparison.OrdinalIgnoreCase)) { profile.Exe=exe; ApplyGameSettings(profile); }
        profile.Sessions++;
        string level=profile.Level;
        bool priority=State.OptPriority,background=State.OptBackground,memory=State.OptMemory;
        var chosen=profile;
        try { recovery.Save(); } catch (IOException) { }
        try
        {
            if (State.OptPower && level=="Max" && State.PowerBefore=="")
            {
                string previous=await Task.Run(()=>PowerPlan.UseHighPerformance());
                if (previous!=null) { State.PowerBefore=previous; powerForGame=true; try { recovery.Save(); } catch (IOException) { } }
            }
            string summary=await Task.Run(()=>optimizer.Begin((int)pid,level,priority,background,memory));
            chosen.Last=summary;
            try { recovery.Save(); } catch (IOException) { }
            Diag.Log("Game session, "+chosen.Name+": "+summary);
            // The game may have ended while this was running.
            if (!resting) { await Task.Run(()=>optimizer.End()); return; }
            tray.ShowBalloonTip(2500,chosen.Name,Ui.Voice(summary),ToolTipIcon.None);
        }
        catch (Exception ex) { Diag.Log("Game optimizer: "+ex.Message); }
    }

    // The game is gone (or Bluehour was woken by hand): everything goes back the way it was.
    private void EndGame()
    {
        if (test) return;
        if (optimizer.Active) Task.Run(()=>optimizer.End());
        if (powerForGame) { powerForGame=false; RestorePower(); }
    }

    // Writes a game's Windows settings: the high-performance GPU choice and the full-screen optimizations flag. Both go
    // through the journal, so Recovery can put them back.
    internal void ApplyGameSettings(GameProfile game)
    {
        if (test || String.IsNullOrEmpty(game.Exe)) return;
        string gpu=game.HighGpu?"GpuPreference=2;":null;
        if (GameLibrary.Read(GameLibrary.GpuKey,game.Exe)!=gpu)
            recovery.Set(GameLibrary.GpuKey,game.Exe,gpu,RegistryValueKind.String);
        string current=GameLibrary.Read(GameLibrary.CompatKey,game.Exe);
        string wanted=game.NoFullscreenOpt?GameLibrary.AddFlag(current,GameLibrary.NoFullscreenFlag):GameLibrary.RemoveFlag(current,GameLibrary.NoFullscreenFlag);
        if (wanted!=current) recovery.Set(GameLibrary.CompatKey,game.Exe,wanted,RegistryValueKind.String);
    }

    private async void BoostNow()
    {
        if (busy || test) return;
        busy=true;
        try
        {
            Say("Freeing memory.");
            long freed=await Task.Run(()=>Optimizer.BoostNow());
            Notify(freed>50?freed+" MB handed back to Windows.":"Memory is already in good shape.",false);
        }
        catch (Exception ex) { Notify(ex.Message,true); }
        finally { busy=false; }
    }

    private async void FindGames()
    {
        if (busy || test) return;
        busy=true;
        try
        {
            Say("Looking through your Steam and Epic libraries.");
            var found=await Task.Run(()=>GameLibrary.Scan());
            int added=0;
            foreach (var game in found)
            {
                if (State.Games.Any(g=>String.Equals(g.Folder,game.Folder,StringComparison.OrdinalIgnoreCase) || (g.Exe!="" && String.Equals(g.Exe,game.Exe,StringComparison.OrdinalIgnoreCase)))) continue;
                State.Games.Add(game);
                added++;
            }
            recovery.Save();
            Notify(added==0?"No new games found.":added+" games added.",false);
            if (currentPage=="Performance") Render("Performance");
        }
        catch (Exception ex) { Notify(ex.Message,true); }
        finally { busy=false; }
    }

    private void AddGame()
    {
        using (var file=new OpenFileDialog {Filter="Programs|*.exe",Title="Choose the game's program"})
        {
            if (file.ShowDialog(this)!=DialogResult.OK) return;
            if (State.Games.Any(g=>g.Matches(file.FileName))) { Say("Bluehour already knows that game."); return; }
            State.Games.Add(new GameProfile {Name=GameLibrary.NameOf(file.FileName),Exe=file.FileName});
            recovery.Save();
            Render("Performance");
        }
    }

    private void GameSection(Control panel)
    {
        Heading(panel,"Game optimizer");
        Row(panel,Check("Optimize automatically when a game starts",State.AutoOptimize,v=>{State.AutoOptimize=v; recovery.Save();}));
        Row(panel,Check("Raise the game's priority",State.OptPriority,v=>{State.OptPriority=v; recovery.Save();}),
            Check("Step background apps back",State.OptBackground,v=>{State.OptBackground=v; recovery.Save();}));
        Row(panel,Check("Hand memory to the game",State.OptMemory,v=>{State.OptMemory=v; recovery.Save();}),
            Check("High performance power plan while playing",State.OptPower,v=>{State.OptPower=v; recovery.Save();}));
        Line(panel,"When a game comes to the front, Bluehour rests, the game gets a higher priority, other apps run a step lower, their memory is handed back and the power plan goes to High performance. When the game ends all of it is put back. Nothing is closed or paused. Games are recognized as they run, or you can add them.");
        Row(panel,B("Free memory now",BoostNow,true),B("Find my games",FindGames),B("Add a game…",AddGame));
        Heading(panel,"Your games");
        if (State.Games.Count==0) Line(panel,"No games yet. Start one and Bluehour learns it, or use Find my games.");
        if (State.Games.Count>0)
        {
            Line(panel,"Click the level to change it (Max, Balanced, Off); “fast gpu” and “no fs opt” turn the two Windows settings on or off for that game; × forgets it.");
            panel.Controls.Add(new GameList(State.Games,
                game=>Run(()=>{ ApplyGameSettings(game); recovery.Save(); }),
                game=>BeginInvoke((Action)(()=>Run(()=>{
                    game.HighGpu=game.NoFullscreenOpt=false;
                    ApplyGameSettings(game);
                    State.Games.Remove(game); recovery.Save(); Render("Performance");
                })))) {Width=620});
        }
    }
}

// Every game in one control: a list of rows that draws itself and handles its own mouse, so a library of a hundred games
// costs one window instead of several hundred.
internal sealed class GameList : Control
{
    private const int Row=56;
    private readonly List<GameProfile> games;
    private readonly Action<GameProfile> changed,removed;
    private int scroll,hotRow=-1,hotPart=-1;

    internal GameList(List<GameProfile> games,Action<GameProfile> changed,Action<GameProfile> removed)
    {
        this.games=games;this.changed=changed;this.removed=removed;
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);
        Name="Games list";AccessibleName="Games";AccessibleRole=AccessibleRole.List;
        Height=Math.Min(6,games.Count)*Row+4;Margin=new Padding(0,6,0,4);
    }

    private int Extent(){return games.Count*Row+4;}
    private int MaxScroll(){return Math.Max(0,Extent()-Height);}

    // The buttons of a row, from the right edge: remove, no-full-screen-optimizations, fast gpu, level.
    private Rectangle Part(int part,int row)
    {
        int y=row*Row-scroll+13;
        var remove=new Rectangle(Width-12-30,y,30,30);
        var fso=new Rectangle(remove.X-8-84,y,84,30);
        var gpu=new Rectangle(fso.X-8-76,y,76,30);
        var level=new Rectangle(gpu.X-8-88,y,88,30);
        return part==0?level:part==1?gpu:part==2?fso:remove;
    }
    private bool Hit(Point point,out int row,out int part)
    {
        row=(point.Y+scroll-2)/Row;part=-1;
        if(point.Y<0 || row<0 || row>=games.Count)return false;
        for(int p=0;p<4;p++)if(Part(p,row).Contains(point)){part=p;break;}
        return true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int row,part;
        if(!Hit(e.Location,out row,out part)){row=-1;part=-1;}
        if(row!=hotRow || part!=hotPart){hotRow=row;hotPart=part;Cursor=part>=0?Cursors.Hand:Cursors.Default;Invalidate();}
    }
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hotRow=hotPart=-1;Invalidate();}
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int row,part;
        if(e.Button!=MouseButtons.Left || !Hit(e.Location,out row,out part) || part<0)return;
        var game=games[row];
        if(part==0)game.Level=game.Level=="Max"?"Balanced":game.Level=="Balanced"?"Off":"Max";
        else if(part==1)game.HighGpu=!game.HighGpu;
        else if(part==2)game.NoFullscreenOpt=!game.NoFullscreenOpt;
        else{removed(game);return;}
        Invalidate();
        changed(game);
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int goal=Math.Max(0,Math.Min(MaxScroll(),scroll-Math.Sign(e.Delta)*Row));
        if(goal!=scroll){scroll=goal;Invalidate();}
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent==null?Ui.Night:Parent.BackColor.A==255?Parent.BackColor:Ui.Night);
    }

    private void Chip(Graphics g,Rectangle box,string text,bool on,bool hot,Color onColor)
    {
        Style.Fill(g,box,box.Height/2f,on?onColor:(hot?Ui.Raised:Ui.SurfaceHigh));
        TextRenderer.DrawText(g,text,Style.Cached(8.5f,FontStyle.Bold),box,on?Ui.Night:Ui.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var accent=Ui.Accent(Ui.Current);
        int first=Math.Max(0,scroll/Row),last=Math.Min(games.Count-1,(scroll+Height)/Row);
        for(int row=first;row<=last;row++)
        {
            var game=games[row];
            var card=new RectangleF(0,row*Row-scroll+2,Width-(MaxScroll()>0?12:0),Row-6);
            Style.Fill(g,card,18,row==hotRow?Ui.Raised:Ui.SurfaceHigh);
            int x=16,right=Part(0,row).X-12;
            var title=new Rectangle(x,(int)card.Y+7,Math.Max(40,right-x),22);
            TextRenderer.DrawText(g,games[row].Name,Style.Cached(10.5f,FontStyle.Bold),title,Ui.Text,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter);
            string detail=(game.Sessions>0?game.Sessions+(game.Sessions==1?" session":" sessions"):"not played yet")+(game.Last!=""?"  ·  "+game.Last:"");
            var sub=new Rectangle(x,(int)card.Y+28,Math.Max(40,right-x),18);
            TextRenderer.DrawText(g,detail,Style.Cached(8.5f,FontStyle.Regular),sub,Ui.Muted,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            bool h=row==hotRow;
            Chip(g,Part(0,row),Ui.Voice(game.Level),game.Level!="Off",h&&hotPart==0,game.Level=="Max"?accent:Ui.AccentContainer(Ui.Current));
            Chip(g,Part(1,row),"fast gpu",game.HighGpu,h&&hotPart==1,accent);
            Chip(g,Part(2,row),"no fs opt",game.NoFullscreenOpt,h&&hotPart==2,accent);
            var cross=Part(3,row);
            Style.Fill(g,cross,15,h&&hotPart==3?Ui.Raised:Ui.SurfaceHigh);
            Style.Icon(g,"Close",new RectangleF(cross.X+7,cross.Y+7,16,16),h&&hotPart==3?Ui.Text:Ui.Muted);
        }
        if(MaxScroll()>0)
        {
            float track=Height-12,thumb=Math.Max(28,track*Height/Extent()),top=6+(track-thumb)*scroll/Math.Max(1,Extent()-Height);
            Style.Fill(g,new RectangleF(Width-8,top,4,thumb),2,Color.FromArgb(130,Ui.Muted));
        }
    }
}
