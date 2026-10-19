using System;
using System.IO;
using System.Security.Cryptography;
using MphRead.Mods.Launcher;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public sealed record MapStudioAssetFileRead(NativeStudioFileVersion Version,byte[] Bytes);
public sealed record MapStudioAssetPublication(bool Verified,string Notice,string? RecoveryPath);

/// <summary>Explicit reviewed binary IO. No polling, document writer, watcher or automatic deletion is introduced.</summary>
public static class MapStudioReviewedAssetFiles
{
    private const int MaximumRecoveries=4;
    public static MapStudioAssetFileRead ReadSource(string path)
    {
        RequireLength(path);var version=NativeStudioFileVersion.Capture(path);RequireLength(version.Path);
        using var input=new FileStream(version.Path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(input.Length<0||input.Length>MapPackageReader.MaxEntryBytes)throw new InvalidDataException("Asset exceeds the 256 MiB entry limit.");
        byte[] bytes=new byte[checked((int)input.Length)];input.ReadExactly(bytes);
        if(input.ReadByte()!=-1)throw new IOException("Asset source changed while reading.");
        if(bytes.LongLength!=version.Length||bytes.LongLength>MapPackageReader.MaxEntryBytes
            ||Convert.ToHexString(SHA256.HashData(bytes))!=version.Sha256||!Matches(version))
            throw new IOException("Asset source changed while reading. Review it again.");
        return new(version,bytes);
    }
    public static NativeStudioFileVersion ReviewTarget(string path)
    {
        if(File.Exists(path))RequireLength(path);
        var version=NativeStudioFileVersion.Capture(path,allowMissing:true);
        if(version.Exists)RequireLength(version.Path);
        return version;
    }
    public static bool Matches(NativeStudioFileVersion version)
    {
        try
        {
            // A captured version path was already canonical. A newly introduced
            // link may not redirect even a same-hash verification read elsewhere.
            for(string? current=version.Path;current!=null;current=Path.GetDirectoryName(current))
                if(new FileInfo(current).LinkTarget!=null||(File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)return false;
            if(version.Exists)RequireLength(version.Path);return version.Matches();
        }
        catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException){return false;}
    }
    private static void RequireLength(string path)
    {
        var info=new FileInfo(path);
        if(!info.Exists)throw new FileNotFoundException("Asset source is missing.");
        if(info.Length<0||info.Length>MapPackageReader.MaxEntryBytes)throw new InvalidDataException("Asset exceeds the 256 MiB entry limit.");
    }
    public static MapStudioAssetPublication Publish(NativeStudioFileVersion reviewed,ReadOnlyMemory<byte> payload,Action verifyOwner,Action? beforePublication=null)
    {
        if(payload.Length>MapPackageReader.MaxEntryBytes)throw new InvalidDataException("Asset exceeds the 256 MiB entry limit.");
        return PublishCore(reviewed,Convert.ToHexString(SHA256.HashData(payload.Span)),
            output=>output.Write(payload.Span),verifyOwner,beforePublication);
    }
    /// <summary>Stream a reviewed immutable package without narrowing its archive limit to an individual asset's limit.</summary>
    public static MapStudioAssetPublication PublishFile(NativeStudioFileVersion reviewed,NativeStudioFileVersion source,
        Action verifyOwner,Action? beforePublication=null)
    {
        if(!source.Exists||source.Length<0||source.Length>MapPackageReader.MaxArchiveBytes)
            throw new InvalidDataException("Package exceeds the 512 MiB archive limit.");
        void Verify(){verifyOwner();if(!source.Matches())throw new IOException("Reviewed package bytes changed.");}
        return PublishCore(reviewed,source.Sha256,output=>{using var input=File.OpenRead(source.Path);input.CopyTo(output,65536);},Verify,beforePublication,archive:true);
    }
    private static MapStudioAssetPublication PublishCore(NativeStudioFileVersion reviewed,string writtenHash,
        Action<FileStream> write,Action verifyOwner,Action? beforePublication,bool archive=false)
    {
        ArgumentNullException.ThrowIfNull(verifyOwner);
        string directory=Path.GetDirectoryName(reviewed.Path)??throw new IOException("Export folder is unavailable.");
        string temporary=Path.Combine(directory,".prime-asset-"+Guid.NewGuid().ToString("N")+".tmp");
        string? recovery=null;bool displaced=false;
        try
        {
            verifyOwner();Validate(reviewed,archive);
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {write(output);output.Flush(flushToDisk:true);}
            verifyOwner();Validate(reviewed,archive);
            if(!reviewed.Exists)
            {
                beforePublication?.Invoke();verifyOwner();
                File.Move(temporary,reviewed.Path,overwrite:false);
                try
                {
                    if(NativeStudioFileVersion.Capture(reviewed.Path).Sha256!=writtenHash)return new(false,"Export target changed after publication. Inspect the actual file before reviewing again.",null);
                    return new(true,"ASSET EXPORTED // EXACT REVIEWED BYTES",null);
                }
                catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException)
                {return new(false,"Asset was published but its bytes could not be verified. Inspect the actual file. "+error.Message,null);}
            }
            recovery=AvailableRecovery(reviewed.Path);
            beforePublication?.Invoke();verifyOwner();
            try
            {
                // Neither move may overwrite an existing leaf. There is a short
                // missing-target window; this is intentionally not atomic whole
                // overwrite/CAS. No mutable recovery path is ever deleted here.
                File.Move(reviewed.Path,recovery,overwrite:false);displaced=true;
                if(NativeStudioFileVersion.Capture(recovery).Sha256!=reviewed.Sha256)
                    return new(false,"The source target changed after review. Export refused; target is absent and actual displaced bytes remain at "+recovery+". Inspect and restore them explicitly.",recovery);
                verifyOwner();
                File.Move(temporary,reviewed.Path,overwrite:false);
                if(NativeStudioFileVersion.Capture(reviewed.Path).Sha256!=writtenHash)
                    return new(false,"Exported target could not be verified. Actual prior bytes remain at "+recovery+". Inspect both files.",recovery);
                return new(true,"ASSET EXPORTED // EXACT BYTES; PRIOR FILE RETAINED AT "+recovery,recovery);
            }
            catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            {
                return new(false,displaced
                    ?"Export did not complete or verify. Target may be absent or externally replaced; actual displaced bytes remain at "+recovery+". No rollback was attempted. "+error.Message
                    :"Export refused before target displacement. Target and any externally created recovery file remain untouched. "+error.Message,
                    displaced?recovery:null);
            }
        }
        finally
        {
            try{if(File.Exists(temporary))File.Delete(temporary);}catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException){}
        }
    }
    private static void Validate(NativeStudioFileVersion reviewed,bool archive=false)
    {if(!(archive?reviewed.Matches():Matches(reviewed)))throw new InvalidOperationException("The reviewed file changed. Review the destination again.");}
    private static string AvailableRecovery(string target)
    {
        for(int i=0;i<MaximumRecoveries;i++)
        {
            string path=target+".prime-asset-recovery-"+i;
            if(!File.Exists(path)&&!Directory.Exists(path)&&new FileInfo(path).LinkTarget==null)return path;
        }
        throw new IOException("Four prior asset recoveries exist. Inspect and move them before another overwrite.");
    }
}
