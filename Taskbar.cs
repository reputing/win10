using System;
using System.Collections.Generic;
using System.Text;

// Takes the Windows taskbar out of the way while the Bluehour desktop is up, and gives it back. The taskbar windows are
// only hidden, never closed or changed, so showing them again returns exactly what was there. Explorer restarting
// brings its own taskbar back, which is why the desktop calls Hide again every second or so.
internal static class Taskbar
{
    private static bool hidden;

    // True while this process is the reason the taskbar is hidden.
    internal static bool Hid { get { return hidden; } }

    // The main taskbar and the one Windows adds to every other display.
    private static List<IntPtr> Windows()
    {
        var list=new List<IntPtr>();
        var main=Native.FindWindow("Shell_TrayWnd",null);
        if(main!=IntPtr.Zero)list.Add(main);
        Native.EnumWindows(delegate(IntPtr hwnd,IntPtr unused)
        {
            var name=new StringBuilder(64);
            if(Native.GetClassName(hwnd,name,name.Capacity)>0 && name.ToString()=="Shell_SecondaryTrayWnd")list.Add(hwnd);
            return true;
        },IntPtr.Zero);
        return list;
    }

    internal static void Hide()
    {
        // This runs every second or so, so the common case (already hidden, one display) is a single check.
        var main=Native.FindWindow("Shell_TrayWnd",null);
        if(main!=IntPtr.Zero && Native.IsWindowVisible(main))Native.ShowWindowAsync(main,0);   // SW_HIDE
        if(System.Windows.Forms.Screen.AllScreens.Length>1)
            foreach(var window in Windows())
                if(Native.IsWindowVisible(window))Native.ShowWindowAsync(window,0);
        hidden=true;
    }

    internal static void Show()
    {
        foreach(var window in Windows())
            if(!Native.IsWindowVisible(window))Native.ShowWindowAsync(window,4);   // SW_SHOWNOACTIVATE
        hidden=false;
    }
}
