using System;
using System.IO;
using System.Threading;

namespace MphRead.Mods.Replay;

/// <summary>Private process storage with a lifetime independent of export output paths.</summary>
internal sealed class ReplayExportWorkDirectory : IDisposable
{
    private readonly string _root;
    private int _disposed;
    internal string Path { get; }

    private ReplayExportWorkDirectory(string root)
    {_root=root;Path=System.IO.Path.Combine(root,"work");}

    internal static ReplayExportWorkDirectory Create()
    {
        string parent=System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        if(OperatingSystem.IsWindows()&&parent.Length>180)
            parent=System.IO.Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        string root=System.IO.Path.Combine(parent,"pp-export-"+Guid.NewGuid().ToString("N"));
        var owned=new ReplayExportWorkDirectory(root);
        if(OperatingSystem.IsWindows()&&owned.Path.Length>=240)
            throw new IOException("A short per-user temporary directory is required to launch an export worker.");
        if(OperatingSystem.IsWindows())Directory.CreateDirectory(root);
        else Directory.CreateDirectory(root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        try{Directory.CreateDirectory(owned.Path);return owned;}
        catch{Directory.Delete(root);throw;}
    }

    // Queue snapshots retain their existing retry/log lifetime. They are not
    // disposed when an editor closes or a successor attempt is preparing.
    internal static string CreateRetained()=>Create().Path;
    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0)return;
        try
        {
            // Only this generated root and its direct work child are owned.
            // Refuse recursive cleanup if either was replaced by a link.
            string root=System.IO.Path.GetFullPath(_root),work=System.IO.Path.GetFullPath(Path);
            if(work!=System.IO.Path.Combine(root,"work")||new DirectoryInfo(root).LinkTarget!=null
                ||new DirectoryInfo(work).LinkTarget!=null)return;
            Directory.Delete(root,recursive:true);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException)
        {System.Diagnostics.Debug.WriteLine("Export private-directory retirement: "+error.Message);}
    }

    internal static string RetryOutput(string originalOutput,Guid id)
        =>System.IO.Path.Combine(System.IO.Path.GetFullPath(originalOutput),"retry-"+id.ToString("N"));
}
