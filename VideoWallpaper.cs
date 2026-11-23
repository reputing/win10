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
    private int checks,bad,lastState=-1;
    private bool playing=true,started;
    private string description="";
    internal event Action Started;
    internal event Action<string> Failed;

    // Null when the player is available, otherwise what to tell the person.
    internal static string Problem()
    {
        return Type.GetTypeFromProgID("WMPlayer.OCX")==null
            ? "Live wallpapers need Windows Media Player. Turn on “Windows Media Player Legacy” under Settings → System → Optional features."
            : null;
    }

    // The video and sound formats named in an MP4's headers (they sit near the start or the end of the file), e.g.
    // "H.264 video, AAC sound". Null when they cannot be read.
    internal static string Describe(string file)
    {
        string video=null,audio=null;
        try
        {
            using(var input=System.IO.File.OpenRead(file))
            {
                int size=(int)Math.Min(6*1024*1024,input.Length);
                var buffer=new byte[size];
                foreach(long start in new[]{0L,Math.Max(0,input.Length-size)})
                {
                    input.Seek(start,System.IO.SeekOrigin.Begin);
                    int read=0;
                    while(read<size){int n=input.Read(buffer,read,size-read);if(n<=0)break;read+=n;}
                    string text=System.Text.Encoding.ASCII.GetString(buffer,0,read);
                    if(video==null)video=Find(text,new[]{"avc1","H.264","avc3","H.264","hvc1","HEVC (H.265)","hev1","HEVC (H.265)","dvh1","HEVC (H.265)","av01","AV1","vp09","VP9","mp4v","MPEG-4"});
                    if(audio==null)audio=Find(text,new[]{"mp4a","AAC","ac-3","Dolby Digital","ec-3","Dolby Digital Plus","Opus","Opus","alac","Apple Lossless","fLaC","FLAC"});
                    if(video!=null && audio!=null)break;
                }
            }
        }
        catch(System.IO.IOException){}
        catch(UnauthorizedAccessException){}
        if(video==null && audio==null)return null;
        return (video??"unknown")+" video, "+(audio??"no")+" sound";
    }

    private static string Find(string text,string[] pairs)
    {
        for(int i=0;i<pairs.Length;i+=2)if(text.IndexOf(pairs[i],StringComparison.Ordinal)>=0)return pairs[i+1];
        return null;
    }

    // What to say when a video would not play: the reason when it is a format Windows lacks, otherwise what Windows said.
    private string Reason(string detail)
    {
        if(description.Contains("HEVC"))return "It is H.265 (HEVC), which Windows only plays with Microsoft's “HEVC Video Extensions”. Re-save it as H.264 (HandBrake does this for free).";
        if(description.Contains("AV1") || description.Contains("VP9"))return "It is "+(description.Contains("AV1")?"AV1":"VP9")+", which Windows Media Player cannot play. Re-save it as H.264 MP4 (HandBrake does this for free).";
        if(description.Contains("Dolby") || description.Contains("Opus") || description.Contains("FLAC") || description.Contains("Lossless"))
            return "Its sound track format is not one Windows Media Player supports. Re-save it with AAC sound.";
        return detail==""?"Windows Media Player did not say why.":detail;
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
        description=Describe(file)??"";
        Diag.Log("Live wallpaper: "+System.IO.Path.GetFileName(file)+" ("+new System.IO.FileInfo(file).Length/(1024*1024)+" MB"+(description==""?"":", "+description)+")");
        Show();
        player=host.GetOcx();
        player.uiMode="none";
        player.enableContextMenu=false;
        player.stretchToFit=true;
        player.settings.mute=true;
        player.settings.volume=0;
        player.settings.setMode("loop",true);
        player.URL=file;
        player.controls.play();
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
        if(player!=null && started)Fit();
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
        }
        catch(Exception){}
    }

    private void Watch()
    {
        checks++;
        int state;
        try{state=(int)player.playState;bad=0;}   // 3 playing, 2 paused, 1 stopped, 6 buffering, 9 changing, 10 ready, 8 ended
        catch(Exception ex)
        {
            Diag.Log("Live wallpaper: could not read the player ("+ex.Message+")");
            if(++bad>=6)Fail(ex.Message);
            return;
        }
        if(state!=lastState){Diag.Log("Live wallpaper: player state "+state);lastState=state;}
        try
        {
            if(state==3 && !started)
            {
                started=true;
                host.Enabled=false;   // clicks on the video do nothing
                Fit();
                watch.Interval=2000;
                if(Started!=null)Started();
            }
            if(playing && (state==1 || state==8 || state==10 || state==0))player.controls.play();
            // A big file can take a while to open; give it half a minute before giving up.
            if(!started && checks>=60 && state!=3 && state!=2)
            {
                string detail="";
                try{if((int)player.error.errorCount>0)detail=(string)player.error.item(0).errorDescription;}catch(Exception){}
                Fail(detail);
            }
        }
        catch(Exception ex){Diag.Log("Live wallpaper: "+ex.Message);}
    }

    private void Fail(string detail)
    {
        watch.Stop();
        string reason=Reason(detail);
        Diag.Log("Live wallpaper failed: "+reason);
        if(Failed!=null)Failed(reason);
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
