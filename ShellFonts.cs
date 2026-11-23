using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class ShellFonts
{
    private static readonly PrivateFontCollection Collection=new PrivateFontCollection();
    private static readonly PrivateFontCollection Numbers=new PrivateFontCollection();
    internal static readonly FontFamily Text, Display;
    [DllImport("gdi32.dll")] private static extern IntPtr AddFontMemResourceEx(IntPtr bytes,uint length,IntPtr reserved,out uint count);

    static ShellFonts()
    {
        foreach(var name in new[]{"Regular","Bold","Display"})
        {
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Bluehour."+name))
            using(var copy=new MemoryStream())
            {
                stream.CopyTo(copy);
                var bytes=copy.ToArray();
                // GDI+ needs this buffer for the lifetime of the process-wide collection.
                var storage=Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes,0,storage,bytes.Length);
                uint count;
                AddFontMemResourceEx(storage,(uint)bytes.Length,IntPtr.Zero,out count);
                (name=="Display"?Numbers:Collection).AddMemoryFont(storage,bytes.Length);
            }
        }
        Text=Collection.Families.FirstOrDefault(f=>f.Name=="Bluehour Sans");
        Display=Numbers.Families.First();
        if(Text==null || Display==null)throw new Exception("Loaded fonts: "+String.Join(", ",Collection.Families.Select(f=>f.Name)));
    }
}



