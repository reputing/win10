using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public sealed class AppPin
{
    public string Target, Name;
    public double X=-1,Y=-1;      // a free position from earlier builds; replaced by Col and Row
    public int Col=-1,Row=-1;      // the cell on the desktop grid, or -1 to flow into the next free cell
    public bool Imported;   // came from a shortcut on the Windows desktop
    public override string ToString(){return Name;}
}

internal static class AppLibrary
{
    internal static AppPin Pin(string name,string target){return new AppPin{Name=name,Target=target};}
    internal static readonly AppPin[] Builtins={ Pin("Browser","builtin:web"),Pin("Files","builtin:files"),Pin("Discord","builtin:discord") };
    internal static List<AppPin> Installed()
    {
        var apps=new List<AppPin>();
        dynamic automation=null,folder=null,items=null;
        try
        {
            automation=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application",true));
            folder=automation.NameSpace("shell:AppsFolder");
            if(folder!=null)
            {
                items=folder.Items();
                int count=items.Count;
                for(int i=0;i<count;i++)
                {
                    dynamic entry=items.Item(i);
                    try
                    {
                        string name=entry.Name;
                        string id=entry.ExtendedProperty("System.AppUserModel.ID") as string;
                        if(!String.IsNullOrWhiteSpace(id)&&!String.IsNullOrWhiteSpace(name))apps.Add(Pin(name,"app:"+id));
                    }
                    finally{Release(entry);}
                }
            }
        }
        catch(COMException){}
        catch(System.Security.SecurityException){}
        catch(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException){}
        catch(UnauthorizedAccessException){}
        finally{Release(items);Release(folder);Release(automation);}
        foreach(var root in new[]{Environment.SpecialFolder.Programs,Environment.SpecialFolder.CommonPrograms})
        {
            var folders=new Stack<string>();
            folders.Push(Environment.GetFolderPath(root));
            while(folders.Count!=0)
            {
                string dir=folders.Pop();
                try
                {
                    foreach(var sub in Directory.GetDirectories(dir))
                        if((File.GetAttributes(sub)&FileAttributes.ReparsePoint)==0)folders.Push(sub);
                    foreach(var link in Directory.GetFiles(dir,"*.lnk"))
                        apps.Add(Pin(Path.GetFileNameWithoutExtension(link),"file:"+link));
                }
                catch(IOException){}
                catch(UnauthorizedAccessException){}
                catch(ArgumentException){}
            }
        }
        return apps.GroupBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase).Select(g=>g.First()).OrderBy(a=>a.Name).ToList();
    }
    // Shortcuts and programs sitting on the Windows desktop, for the current user and for everyone.
    internal static List<AppPin> DesktopShortcuts()
    {
        var found=new List<AppPin>();
        foreach(var folder in new[]{Environment.SpecialFolder.DesktopDirectory,Environment.SpecialFolder.CommonDesktopDirectory})
        {
            string dir=Environment.GetFolderPath(folder);
            if(String.IsNullOrEmpty(dir) || !Directory.Exists(dir))continue;
            try
            {
                foreach(var file in Directory.GetFiles(dir))
                {
                    string extension=Path.GetExtension(file).ToLowerInvariant();
                    if(extension!=".lnk" && extension!=".url" && extension!=".exe" && extension!=".appref-ms")continue;
                    if((File.GetAttributes(file)&FileAttributes.Hidden)!=0)continue;
                    var pin=Pin(Path.GetFileNameWithoutExtension(file),"file:"+file);
                    pin.Imported=true;
                    found.Add(pin);
                }
            }
            catch(IOException){}
            catch(UnauthorizedAccessException){}
        }
        return found.GroupBy(p=>p.Target,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).Take(60).ToList();
    }

    // When either desktop folder last changed, so the sticker list is only rebuilt when something actually happened.
    internal static DateTime DesktopStamp()
    {
        DateTime latest=DateTime.MinValue;
        foreach(var folder in new[]{Environment.SpecialFolder.DesktopDirectory,Environment.SpecialFolder.CommonDesktopDirectory})
        {
            try
            {
                string dir=Environment.GetFolderPath(folder);
                if(!String.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    var stamp=Directory.GetLastWriteTimeUtc(dir);
                    if(stamp>latest)latest=stamp;
                }
            }
            catch(IOException){}
            catch(UnauthorizedAccessException){}
        }
        return latest;
    }

    private static void Release(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
    internal static void Launch(AppPin pin,Shell shell){shell.OpenApp(pin);}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct FileInfo { public IntPtr Icon;public int Index;public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Display;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string Type; }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern int SHParseDisplayName(string path,IntPtr context,out IntPtr pidl,uint flags,out uint attributes);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr SHGetFileInfo(IntPtr pidl,uint attr,ref FileInfo info,uint size,uint flags);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr icon);
    private static readonly Dictionary<string,Bitmap> icons=new Dictionary<string,Bitmap>(StringComparer.OrdinalIgnoreCase);

    // An app's icon, looked up once for the whole session. The bitmap belongs to this cache: callers must not dispose it.
    internal static Bitmap Cached(AppPin pin)
    {
        if(pin==null || pin.Target==null || pin.Name=="Calculator" || pin.Name=="Notepad" || pin.Name=="Paint")return null;   // drawn in code
        Bitmap bitmap;
        if(icons.TryGetValue(pin.Target,out bitmap))return bitmap;
        bitmap=Picture(pin);
        icons[pin.Target]=bitmap;
        return bitmap;
    }

    internal static Bitmap Picture(AppPin pin)
    {
        string path=pin.Target.StartsWith("app:")?"shell:AppsFolder\\"+pin.Target.Substring(4):
            pin.Target.StartsWith("file:")?pin.Target.Substring(5):null;
        if(path==null)return null;

        IntPtr id;uint attr;
        if(SHParseDisplayName(path,IntPtr.Zero,out id,0,out attr)!=0||id==IntPtr.Zero)return null;
        var data=new FileInfo();
        try
        {
            SHGetFileInfo(id,0,ref data,(uint)Marshal.SizeOf(data),0x108);
            return data.Icon==IntPtr.Zero?null:Icon.FromHandle(data.Icon).ToBitmap();
        }
        finally{if(data.Icon!=IntPtr.Zero)DestroyIcon(data.Icon);Marshal.FreeCoTaskMem(id);}
    }
}

internal sealed partial class Shell
{
    private List<AppPin> library;
    internal event Action PinsChanged;
    internal List<AppPin> Pins {get{return State.Pins;}}
    internal void InitializePins()
    {
        if(State.PinsInitialized)return;
        if(State.Pins.Count==0)
            foreach(var app in AppLibrary.Builtins)State.Pins.Add(AppLibrary.Pin(app.Name,app.Target));
        State.PinsInitialized=true;
    }
    internal void EditPins(Action change)
    {
        var before=Pins.Select(a=>new AppPin{Name=a.Name,Target=a.Target,X=a.X,Y=a.Y,Col=a.Col,Row=a.Row,Imported=a.Imported}).ToList();
        try{change();recovery.Save();}
        catch{State.Pins=before;if(PinsChanged!=null)PinsChanged();throw;}
        if(PinsChanged!=null)PinsChanged();
    }
    // Removes a sticker. A shortcut that came from the desktop is remembered so it is not brought back next time.
    internal void Unpin(AppPin pin)
    {
        EditPins(()=>{
            Pins.Remove(pin);
            if(pin.Imported && !State.Dismissed.Contains(pin.Target))State.Dismissed.Add(pin.Target);
        });
    }

    private DateTime desktopStamp;
    private int desktopTicks;

    // Brings the shortcuts on the Windows desktop in as stickers, and drops ones whose shortcut is gone. The folders are
    // read on a worker thread; nothing happens unless a folder changed.
    internal void SyncDesktopApps(bool force)
    {
        if(test || !State.ImportDesktop)return;
        var stamp=AppLibrary.DesktopStamp();
        if(!force && stamp==desktopStamp)return;
        desktopStamp=stamp;
        var worker=new System.Threading.Thread(delegate()
        {
            List<AppPin> found;
            try{found=AppLibrary.DesktopShortcuts();}
            catch(Exception){return;}
            if(IsDisposed||!IsHandleCreated)return;
            try{BeginInvoke((Action)delegate{ApplyDesktopApps(found);});}
            catch(InvalidOperationException){}
        });
        worker.IsBackground=true;
        worker.Start();
    }

    private void ApplyDesktopApps(List<AppPin> found)
    {
        var add=found.Where(f=>!State.Dismissed.Contains(f.Target,StringComparer.OrdinalIgnoreCase) &&
            !Pins.Any(p=>String.Equals(p.Target,f.Target,StringComparison.OrdinalIgnoreCase) || String.Equals(p.Name,f.Name,StringComparison.CurrentCultureIgnoreCase))).ToArray();
        var remove=Pins.Where(p=>p.Imported && !found.Any(f=>String.Equals(f.Target,p.Target,StringComparison.OrdinalIgnoreCase))).ToArray();
        if(add.Length==0 && remove.Length==0)return;   // nothing changed: no journal write
        Run(()=>EditPins(()=>{
            foreach(var pin in remove)Pins.Remove(pin);
            foreach(var pin in add)Pins.Add(pin);
        }));
    }

    internal void AddPin(AppPin pin)
    {
        if(Pins.Any(p=>String.Equals(p.Target,pin.Target,StringComparison.OrdinalIgnoreCase)))return;
        EditPins(()=>Pins.Add(AppLibrary.Pin(pin.Name,pin.Target)));
    }
    private event Action LibraryReady;
    private bool loadingApps,reload;
    internal List<AppPin> Library {get{return library;}}
    // Looks for installed apps again (new installs). The old list stays in use until the new one is ready.
    internal void ReloadLibrary(){reload=true;LoadLibrary();}
    internal void LoadLibrary()
    {
        if(test||loadingApps||(library!=null&&!reload))return;
        loadingApps=true;reload=false;
        var worker=new System.Threading.Thread(delegate()
        {
            List<AppPin> list;
            try{list=AppLibrary.Installed();}
            catch(Exception){list=new List<AppPin>();}
            if(IsDisposed||!IsHandleCreated)return;
            try{BeginInvoke((Action)delegate{
                loadingApps=false; library=list; catalog=null;
                if(!State.SeededInstalled && list.Count>0)
                {
                    var defaults=list.Where(a=>new[]{"Notepad","Calculator","Paint","Spotify","Steam","Visual Studio Code"}.Contains(a.Name,StringComparer.OrdinalIgnoreCase)).Take(2).ToArray();
                    Run(()=>EditPins(()=>{
                        foreach(var a in defaults)if(!Pins.Any(p=>p.Target==a.Target))Pins.Add(AppLibrary.Pin(a.Name,a.Target));
                        State.SeededInstalled=true;
                    }));
                }
                if(LibraryReady!=null)LibraryReady();
            });}catch(InvalidOperationException){}
        });
        worker.IsBackground=true;worker.SetApartmentState(System.Threading.ApartmentState.STA);worker.Start();
    }
    internal void PickApp()
    {
        using(var picker=new Form {Text="Pin an app",StartPosition=FormStartPosition.CenterScreen,ClientSize=new Size(470,466),
            BackColor=Ui.Night,ForeColor=Ui.Text,Font=new Font(Style.Family,11),MinimizeBox=false,MaximizeBox=false})
        {
            var frame=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(16)};
            frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(var h in new[]{32,40,-1,46})frame.RowStyles.Add(h<0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,h));
            frame.Controls.Add(Ui.Label("Pin an installed app",16,Ui.Text,true),0,0);
            var search=new TextBox {Dock=DockStyle.Fill,AccessibleName="Search installed apps",BackColor=Ui.Raised,ForeColor=Ui.Text,Font=picker.Font};
            var list=new ListBox {Dock=DockStyle.Fill,BackColor=Ui.Surface,ForeColor=Ui.Text,BorderStyle=BorderStyle.None,AccessibleName="Installed apps"};
            frame.Controls.Add(search,0,1);frame.Controls.Add(list,0,2);
            Action filter=delegate{
                list.BeginUpdate();list.Items.Clear();
                var candidates=AppLibrary.Builtins.Concat(library??new List<AppPin>()).Where(a=>a.Name.IndexOf(search.Text,StringComparison.CurrentCultureIgnoreCase)>=0);
                list.Items.AddRange(candidates.ToArray());if(list.Items.Count>0)list.SelectedIndex=0;list.EndUpdate();
            };
            Action accept=delegate{var app=list.SelectedItem as AppPin;if(app!=null){AddPin(app);picker.Close();}};
            var row=Ui.Flow(false);
            row.Controls.Add(Ui.Button("Pin selected",()=>Run(accept),Accent));
            row.Controls.Add(Ui.Button("Browse…",()=>Run(()=>{
                using(var file=new OpenFileDialog{Filter="Apps and shortcuts|*.exe;*.lnk",DereferenceLinks=false})
                    if(file.ShowDialog(picker)==DialogResult.OK){AddPin(AppLibrary.Pin(Path.GetFileNameWithoutExtension(file.FileName),"file:"+file.FileName));picker.Close();}
            })));
            frame.Controls.Add(row,0,3);picker.Controls.Add(frame);
            WindowFrame.Attach(picker,"App library",false);
            search.TextChanged+=delegate{filter();};list.DoubleClick+=delegate{Run(accept);};
            picker.AcceptButton=(Button)row.Controls[0];
            LibraryReady+=filter; picker.FormClosed+=delegate{LibraryReady-=filter;};
            picker.Shown+=delegate{LoadLibrary();search.Focus();};
            filter();picker.ShowDialog();
        }
    }
}




