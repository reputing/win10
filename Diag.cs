using System;
using System.IO;
using System.Threading;

// A breadcrumb trail for hangs. Code marks what it is about to do; a watchdog thread notices when the interface has
// stopped pumping messages and writes down the last mark, so a "not responding" window can be traced to a step.
// The log is %LOCALAPPDATA%\BluehourDesktop\diagnostics.log and is trimmed when it grows large.
internal static class Diag
{
    private static readonly object gate=new object();
    private static int beat=Environment.TickCount;
    private static bool watching,reported;
    internal static volatile string Where="starting";

    internal static void Mark(string where){Where=where;}

    // The interface calls this regularly while it is alive.
    internal static void Beat(){Interlocked.Exchange(ref beat,Environment.TickCount);}

    internal static void Log(string text)
    {
        try
        {
            lock(gate)
            {
                string path=Path.Combine(Recovery.Root,"diagnostics.log");
                Directory.CreateDirectory(Recovery.Root);
                var info=new FileInfo(path);
                if(info.Exists && info.Length>256*1024)
                {
                    string old=path+".old";
                    if(File.Exists(old))File.Delete(old);
                    File.Move(path,old);
                }
                File.AppendAllText(path,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+"  "+text+Environment.NewLine);
            }
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
    }

    // Starts the watchdog once. It only reads two fields, so it cannot stall along with the interface.
    internal static void Watch()
    {
        if(watching)return;
        watching=true;
        Log("---- Bluehour started");
        var thread=new Thread(delegate()
        {
            while(true)
            {
                Thread.Sleep(1000);
                int stalled=unchecked(Environment.TickCount-Volatile.Read(ref beat));
                if(stalled>2000 && !reported)
                {
                    reported=true;
                    Log("The interface has not responded for "+stalled+" ms. Last step: "+Where);
                }
                else if(stalled<1000 && reported)
                {
                    reported=false;
                    Log("The interface is responding again.");
                }
            }
        });
        thread.IsBackground=true;
        thread.Name="Bluehour watchdog";
        thread.Start();
    }
}
