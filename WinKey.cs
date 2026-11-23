using System;
using System.Runtime.InteropServices;

// Tells a lone tap of the Windows key from the Windows key used in a shortcut. A tap is the key going down and up with
// nothing else pressed in between; Win+E, Win+Tab and the like are not taps, so those shortcuts keep working.
internal sealed class WinTap
{
    private bool held,chorded;

    // Feeds one key event and returns true when it completes a lone tap.
    internal bool Feed(uint key,bool down)
    {
        bool windows=key==0x5B || key==0x5C;
        if(windows)
        {
            // Holding the key sends repeated "down" events; they belong to the same press.
            if(down){if(!held){held=true;chorded=false;}return false;}
            bool tap=held && !chorded;
            held=chorded=false;
            return tap;
        }
        if(down && held)chorded=true;
        return false;
    }
}

// Makes a tap of the Windows key call `tapped` instead of opening the Start menu. It is installed only while the
// Bluehour desktop is up and removed when the desktop is dismissed.
internal sealed class WinKeyHook : IDisposable
{
    private delegate IntPtr HookProc(int code,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)]
    private struct Key { public uint Code,Scan,Flags,Time; public IntPtr Extra; }
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetWindowsHookEx(int id,HookProc proc,IntPtr module,uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wParam,IntPtr lParam);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);

    private readonly Action tapped;
    private WinTap tap=new WinTap();
    private HookProc procedure;   // held in a field so the delegate is not collected while Windows can call it
    private IntPtr hook;

    internal WinKeyHook(Action tapped){this.tapped=tapped;}

    internal bool Active { get { return hook!=IntPtr.Zero; } }

    internal void Start()
    {
        if(hook!=IntPtr.Zero)return;
        tap=new WinTap();
        procedure=OnKey;
        hook=SetWindowsHookEx(13,procedure,GetModuleHandle(null),0);
        if(hook==IntPtr.Zero)Diag.Log("Windows would not install the Windows key hook (error "+Marshal.GetLastWin32Error()+").");
    }

    internal void Stop()
    {
        if(hook==IntPtr.Zero)return;
        UnhookWindowsHookEx(hook);
        hook=IntPtr.Zero;
    }

    private IntPtr OnKey(int code,IntPtr wParam,IntPtr lParam)
    {
        if(code>=0)
        {
            try
            {
                var key=(Key)Marshal.PtrToStructure(lParam,typeof(Key));
                // Keys injected by this hook (the mask below) are not the person pressing something.
                if((key.Flags&0x10)==0)
                {
                    int message=wParam.ToInt32();
                    bool down=message==0x100 || message==0x104,up=message==0x101 || message==0x105;
                    if((down||up) && tap.Feed(key.Code,down))
                    {
                        // Windows opens Start when the key is released with nothing pressed in between. Pressing an
                        // unassigned key first makes the release look like part of a shortcut instead.
                        keybd_event(0xE8,0,0,UIntPtr.Zero);
                        keybd_event(0xE8,0,2,UIntPtr.Zero);
                        tapped();
                    }
                }
            }
            catch(Exception ex){Diag.Log("Windows key hook: "+ex.Message);}   // nothing may escape a hook
        }
        return CallNextHookEx(hook,code,wParam,lParam);
    }

    public void Dispose(){Stop();}
}
