using System;
using System.Threading.Tasks;
using System.Windows.Forms;

// Starts Bluehour behind a loading screen. The settings are read on a worker thread (the first XML serializer use
// compiles code, which takes a while), and the desktop is then set up one step at a time so the loading screen keeps
// moving and the app never looks frozen.
internal sealed class Boot : ApplicationContext
{
    private readonly bool background;
    private readonly Splash splash;
    private Shell shell;
    internal int ExitCode;

    internal Boot(bool background)
    {
        this.background=background;
        Diag.Mark("boot: showing the loading screen");
        splash=new Splash(Screen.PrimaryScreen,Ui.Accent(Ui.Current),Ui.Tertiary(Ui.Current),"waking up your desktop");
        splash.Open();
        EventHandler idle=null;
        idle=delegate{Application.Idle-=idle;Start();};
        Application.Idle+=idle;
    }

    private async void Start()
    {
        try
        {
            splash.Say("reading your settings",.06);
            Diag.Mark("boot: reading the journal");
            var recovery=await Task.Run(()=>new Recovery(Recovery.Root));
            splash.Recolor(Ui.Accent(recovery.State.Accent),Ui.Tertiary(recovery.State.Accent));
            splash.Say("dressing up the control center",.3);
            await Task.Delay(40);
            Diag.Mark("boot: creating the shell");
            shell=new Shell(recovery,background);
            MainForm=shell;
            shell.Prepare();
            var steps=shell.BootSteps(background);
            shell.Loading=true;
            await splash.Run(shell,steps,.38,.96);
            shell.Loading=false;
            if(!background)shell.Show();
            Diag.Mark("boot: done");
            await splash.Finish();
        }
        catch(Exception ex)
        {
            Diag.Log("Startup failed: "+ex);
            MessageBox.Show(Diag.Describe(ex),"Bluehour",MessageBoxButtons.OK,MessageBoxIcon.Error);
            ExitCode=1;
            ExitThread();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            splash.Dispose();
            if(shell!=null)shell.Dispose();
        }
        base.Dispose(disposing);
    }
}
