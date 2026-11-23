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
        foreach (var game in State.Games.ToArray()) GameRow(panel,game);
    }

    private void GameRow(Control panel,GameProfile game)
    {
        var name=Ui.Label(game.Name,10.5f,Ui.Text,true);
        name.AutoSize=false;name.Width=210;name.Height=34;name.TextAlign=ContentAlignment.MiddleLeft;name.AutoEllipsis=true;
        var shown=game;
        var level=Options("Optimize level",new[]{"Max","Balanced","Off"},shown.Level,v=>{shown.Level=v; recovery.Save();});
        level.Width=130;
        var gpu=Check("High performance GPU",shown.HighGpu,v=>{shown.HighGpu=v; ApplyGameSettings(shown); recovery.Save();});
        var fso=Check("No full-screen optimizations",shown.NoFullscreenOpt,v=>{shown.NoFullscreenOpt=v; ApplyGameSettings(shown); recovery.Save();});
        var remove=B("Remove",()=>{
            shown.HighGpu=shown.NoFullscreenOpt=false;
            ApplyGameSettings(shown);
            State.Games.Remove(shown); recovery.Save(); Render("Performance");
        });
        Row(panel,name,level,gpu,fso,remove);
        string detail=(shown.Sessions>0?shown.Sessions+" sessions":"not played yet")+(shown.Last!=""?"  ·  last: "+shown.Last:"");
        Line(panel,detail);
    }
}
