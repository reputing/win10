using System;
using System.Drawing;
using System.Windows.Forms;
using Timer=System.Windows.Forms.Timer;

// A live wallpaper: a muted, looping video that plays in a window directly beneath the Bluehour desktop. The desktop
// surface is drawn with a colour key, so wherever it has no wallpaper of its own the video shows through, while the
// stickers, clock and buttons are drawn over it. The player is the Windows Media Player component (the same decoder
// Windows uses for MP4 / H.264), which plays on the graphics card, so it costs little processor time.
internal sealed class VideoWallpaper : Form
{
    private sealed class Player : AxHost
    {
        internal Player() : base("6BF52A52-394A-11d3-B153-00C04F79FAA6") { }   // WMPlayer.OCX
    }

    private readonly Player host=new Player();
    private readonly Timer watch=new Timer {Interval=500};
    private dynamic player;
    private int checks;
    private bool fitted,playing=true;
    internal event Action Failed;

    // Null when the player is available, otherwise what to tell the person.
    internal static string Problem()
    {
        return Type.GetTypeFromProgID("WMPlayer.OCX")==null
            ? "Live wallpapers need Windows Media Player. Turn on “Windows Media Player Legacy” under Settings → System → Optional features."
            : null;
    }

    internal VideoWallpaper(Rectangle bounds)
    {
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None;BackColor=Color.Black;Text="Bluehour live wallpaper";
        Bounds=bounds;
        ((System.ComponentModel.ISupportInitialize)host).BeginInit();
        host.SetBounds(0,0,bounds.Width,bounds.Height);
        Controls.Add(host);
        ((System.ComponentModel.ISupportInitialize)host).EndInit();
        watch.Tick+=delegate{Watch();};
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { var cp=base.CreateParams; cp.ExStyle|=0x80|0x08000000; return cp; }   // tool window, never activated
    }

    // Starts playing `file`. The player only exists once the window is on screen.
    internal void Begin(string file)
    {
        Show();
        player=host.GetOcx();
        player.uiMode="none";
        player.enableContextMenu=false;
        player.stretchToFit=true;
        player.settings.mute=true;
        player.settings.volume=0;
        player.settings.setMode("loop",true);
        player.URL=file;
        host.Enabled=false;   // clicks on the video do nothing
        watch.Start();
    }

    internal bool Playing
    {
        get { return playing; }
        set
        {
            if(playing==value || player==null)return;
            playing=value;
            try{if(value)player.controls.play();else player.controls.pause();}
            catch(Exception){}
        }
    }

    internal void Reframe(Rectangle bounds)
    {
        if(Bounds!=bounds)Bounds=bounds;
        fitted=false;
        if(player!=null)Fit();
    }

    // Puts the window just beneath `surface` in the stack.
    internal void Sink(IntPtr surface){Native.SetWindowPos(Handle,surface,0,0,0,0,0x13);}

    // The video covers the display: scaled up to fill it and centred, with the overhang cropped by the window.
    private void Fit()
    {
        try
        {
            int width=(int)player.currentMedia.imageSourceWidth,height=(int)player.currentMedia.imageSourceHeight;
            if(width<=0 || height<=0)return;
            double scale=Math.Max(ClientSize.Width/(double)width,ClientSize.Height/(double)height);
            int w=(int)Math.Ceiling(width*scale),h=(int)Math.Ceiling(height*scale);
            host.SetBounds((ClientSize.Width-w)/2,(ClientSize.Height-h)/2,w,h);
            fitted=true;
        }
        catch(Exception){}
    }

    private void Watch()
    {
        checks++;
        try
        {
            int state=(int)player.playState;   // 3 playing, 2 paused, 1 stopped, 8 media ended
            if(!fitted && state==3){Fit();watch.Interval=2000;}
            if(playing && (state==1 || state==8))player.controls.play();
            if(!fitted && checks>=16 && state!=3 && state!=2)Fail();   // 8 seconds and it never started
        }
        catch(Exception){Fail();}
    }

    private void Fail()
    {
        watch.Stop();
        if(Failed!=null)Failed();
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            watch.Stop();watch.Dispose();
            try{if(player!=null)player.close();}catch(Exception){}
            player=null;
            host.Dispose();
        }
        base.Dispose(disposing);
    }
}
