#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
namespace MphRead.Mods.Launcher;
/// <summary>Native project dialogs; cancellation never turns into an implicit path selection.</summary>
internal static class NativeReplayProjectPicker
{
    internal static Task<string?> Open(CancellationToken token)=>Task.Run(()=>
    {
        token.ThrowIfCancellationRequested();
        if(!NativeFilePicker.Available)throw new InvalidOperationException("The native file picker is unavailable. Open a recording/project from the replay library.");
        if(OperatingSystem.IsWindows())return WindowsDialog(false);
        if(OperatingSystem.IsMacOS())return Run("osascript",token,"-e","POSIX path of (choose file with prompt \"Open Replay Studio recording or project\" of type {\"ppdemo\",\"ppclip\"})");
        return Exists("zenity")?Run("zenity",token,"--file-selection","--title=Open Replay Studio recording or project","--file-filter=Replay Studio | *.ppdemo *.ppclip")
            :Run("kdialog",token,"--title","Open Replay Studio recording or project","--getopenfilename",Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"*.ppdemo *.ppclip|Replay Studio");
    },token);
    internal static Task<string?> OpenFile(string title,string description,string extension,CancellationToken token)
    {
        if(String.IsNullOrWhiteSpace(title)||title.Length>120||title.IndexOfAny(new[]{'\0','\r','\n'})>=0
            ||String.IsNullOrWhiteSpace(description)||description.Length>80||description.IndexOfAny(new[]{'\0','\r','\n'})>=0
            ||extension!="wav")throw new ArgumentException("Choose a bounded WAV picker title/filter.");
        return Task.Run(()=>
        {
            token.ThrowIfCancellationRequested();
            if(!NativeFilePicker.Available)throw new InvalidOperationException("The native WAV picker is unavailable. Enter an absolute WAV path instead.");
            string? selected;
            if(OperatingSystem.IsWindows())selected=WindowsDialog(false,title,description,extension);
            else if(OperatingSystem.IsMacOS())
            {
                string prompt=title.Replace("\\","\\\\").Replace("\"","\\\"");
                selected=Run("osascript",token,"-e",$"POSIX path of (choose file with prompt \"{prompt}\" of type {{\"wav\"}})");
            }
            else selected=Exists("zenity")?Run("zenity",token,"--file-selection","--title="+title,"--file-filter="+description+" | *.wav")
                :Run("kdialog",token,"--title",title,"--getopenfilename",Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"*.wav|"+description);
            token.ThrowIfCancellationRequested();
            if(String.IsNullOrWhiteSpace(selected))return null;
            if(selected.Length>4096||selected.Contains('\0')||!Path.IsPathFullyQualified(selected)||!selected.EndsWith(".wav",StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose an absolute WAV file path.");
            return Path.GetFullPath(selected);
        },token).WaitAsync(token);
    }
    internal static Task<string?> SaveAs(CancellationToken token)=>Task.Run(()=>
    {
        token.ThrowIfCancellationRequested();
        if(!NativeFilePicker.Available)throw new InvalidOperationException("The native save picker is unavailable.");
        string? selected;
        if(OperatingSystem.IsWindows())selected=WindowsDialog(true);
        else if(OperatingSystem.IsMacOS())selected=Run("osascript",token,"-e","POSIX path of (choose file name with prompt \"Save Replay Studio project as\" default name \"selection.ppclip\")");
        else
        {
            selected=Exists("zenity") ? Run("zenity",token,"--file-selection","--save","--confirm-overwrite","--title=Save Replay Studio project as","--filename=selection.ppclip","--file-filter=Replay Studio project | *.ppclip")
                : Run("kdialog",token,"--title","Save Replay Studio project as","--getsavefilename",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"selection.ppclip"),"*.ppclip|Replay Studio project");
        }
        token.ThrowIfCancellationRequested();
        return String.IsNullOrWhiteSpace(selected)?null:selected.EndsWith(".ppclip",StringComparison.OrdinalIgnoreCase)?selected:selected+".ppclip";
    },token);
    private static bool Exists(string command)
    {
        foreach(string dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))if(File.Exists(Path.Combine(dir,command)))return true;
        return false;
    }
    private static string? Run(string file,CancellationToken token,params string[] args)
    {
        var start=new ProcessStartInfo(file){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        foreach(string arg in args)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("The native file picker did not start.");
        using var cancel=token.Register(()=>{try{process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}});
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();process.WaitForExit();
        token.ThrowIfCancellationRequested();_ = error.GetAwaiter().GetResult();
        return process.ExitCode==0?output.GetAwaiter().GetResult().Trim():null;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct FileName
    {
        public int Size;public IntPtr Owner,Instance;public string? Filter,CustomFilter;public int MaxCustomFilter,FilterIndex;
        public IntPtr File;public int MaxFile;public IntPtr FileTitle;public int MaxFileTitle;public string? InitialDirectory,Title;
        public int Flags;public short FileOffset,FileExtension;public string? DefaultExtension;public IntPtr CustomData,Hook;public string? Template;
        public IntPtr Reserved;public int ReservedValue,FlagsEx;
    }
    [DllImport("comdlg32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool GetSaveFileNameW(ref FileName name);
    [DllImport("comdlg32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool GetOpenFileNameW(ref FileName name);
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? WindowsDialog(bool save,string? title=null,string? description=null,string? extension=null)
    {
        var result=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            try{result.SetResult(WindowsDialogCore(save,title,description,extension));}catch(Exception error){result.SetException(error);}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();return result.Task.GetAwaiter().GetResult();
    }
    private static string? WindowsDialogCore(bool save,string? title,string? description,string? extension)
    {
        IntPtr buffer=Marshal.AllocHGlobal(32768*2);
        try
        {
            for(int i=0;i<32768;i++)Marshal.WriteInt16(buffer,i*2,0);
            string suggested=save?"selection.ppclip":"";for(int i=0;i<suggested.Length;i++)Marshal.WriteInt16(buffer,i*2,(short)suggested[i]);
            var name=new FileName{Size=Marshal.SizeOf<FileName>(),Owner=NativeFilePicker.Owner,Filter=extension!=null?description+"\0*."+extension+"\0\0":save?"Replay Studio project\0*.ppclip\0\0":"Replay Studio recording/project\0*.ppdemo;*.ppclip\0\0",File=buffer,MaxFile=32768,Title=title??(save?"Save Replay Studio project as":"Open Replay Studio recording or project"),DefaultExtension=extension??(save?"ppclip":"ppdemo"),Flags=0x800|0x8|0x80000|(save?0x2:0x1000)};
            return (save?GetSaveFileNameW(ref name):GetOpenFileNameW(ref name))?Marshal.PtrToStringUni(buffer):null;
        }
        finally{Marshal.FreeHGlobal(buffer);}
    }
}
#endif
