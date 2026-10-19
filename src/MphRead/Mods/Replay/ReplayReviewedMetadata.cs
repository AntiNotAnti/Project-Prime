using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

internal sealed record ReplayReviewedSidecar(string Path, string Revision, byte[]? Bytes);
internal sealed record ReplayReviewedPublication(ReplayReviewedSidecar Written,bool Verified,string Notice,string? RecoveryPath);

/// <summary>Bounded explicit-review I/O. Readonly UI polling never calls this helper.</summary>
internal static class ReplayReviewedMetadata
{
    internal const int MaximumBytes=1024*1024;
    internal static ReplayReviewedSidecar Read(string path,int maximum=MaximumBytes)
    {
        var file=new FileInfo(path);
        if(file.LinkTarget!=null)throw new IOException("The reviewed sidecar is a symbolic link.");
        if(!file.Exists)return new(path,"missing",null);
        long length=file.Length,stamp=file.LastWriteTimeUtc.Ticks;
        if(length<0 || length>maximum)throw new InvalidDataException("The reviewed sidecar is too large.");
        byte[] bytes=new byte[(int)length];
        using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {input.ReadExactly(bytes);if(input.ReadByte()!=-1)throw new IOException("The sidecar changed while reading.");}
        file.Refresh();
        if(!file.Exists || file.LinkTarget!=null || file.Length!=length || file.LastWriteTimeUtc.Ticks!=stamp)
            throw new IOException("The sidecar changed while reading.");
        return new(path,Convert.ToHexString(SHA256.HashData(bytes)),bytes);
    }
    internal static void Validate(ReplayReviewedSidecar reviewed)
    {if(Read(reviewed.Path).Revision!=reviewed.Revision)throw new InvalidOperationException("The reviewed sidecar changed. Refresh before saving.");}
    internal static T Parse<T>(ReplayReviewedSidecar reviewed,Func<T> missing)
    {
        if(reviewed.Bytes==null)return missing();
        try{return JsonSerializer.Deserialize<T>(reviewed.Bytes)??throw new InvalidDataException("The reviewed sidecar is empty.");}
        catch(JsonException error){throw new InvalidDataException("The reviewed sidecar is unreadable. Repair it before editing.",error);}
    }
    internal const int MaximumRecoveries=4;
    internal static ReplayReviewedPublication Commit<T>(ReplayReviewedSidecar reviewed,T document,Action verifyOwner,
        Action? beforePublication=null)
    {
        return CommitBytes(reviewed,JsonSerializer.SerializeToUtf8Bytes(document,new JsonSerializerOptions{WriteIndented=true}),verifyOwner,beforePublication);
    }
    internal static ReplayReviewedPublication CommitBytes(ReplayReviewedSidecar reviewed,byte[] proposed,Action verifyOwner,
        Action? beforePublication=null,int maximum=MaximumBytes)
    {
        if(maximum<=0||maximum>MaximumBytes||proposed.Length>maximum)throw new InvalidDataException("The edited sidecar is too large.");
        byte[] bytes=(byte[])proposed.Clone();
        var written=new ReplayReviewedSidecar(reviewed.Path,Convert.ToHexString(SHA256.HashData(bytes)),bytes);
        string directory=System.IO.Path.GetDirectoryName(reviewed.Path)??throw new IOException("The sidecar folder is unavailable.");
        string temporary=System.IO.Path.Combine(directory,".reviewed-"+Guid.NewGuid().ToString("N")+".tmp");
        string? recovery=null;byte[]? reservation=null;bool publicationAttempted=false;
        try
        {
            verifyOwner();Validate(reviewed);
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {output.Write(bytes);output.Flush(flushToDisk:true);}
            verifyOwner();Validate(reviewed);
            if(reviewed.Bytes==null)
            {
                beforePublication?.Invoke();
                // An external file created after review is never replaced by this missing-target publication.
                File.Move(temporary,reviewed.Path,overwrite:false);
                return new(written,true,"",null);
            }
            (recovery,reservation)=ReserveRecovery(reviewed.Path);
            beforePublication?.Invoke();
            publicationAttempted=true;
            try
            {
                // This is atomic replacement with recovery, not a filesystem compare-and-swap:
                // the backup contains the ACTUAL displaced bytes, including a raced external rewrite.
                File.Replace(temporary,reviewed.Path,recovery,ignoreMetadataErrors:false);
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException)
            {
                bool markerOnly=DeleteReservation(recovery,reservation);
                return new(written,false,"Metadata publication could not be verified. Inspect "+reviewed.Path
                    +(markerOnly?" before reviewing another change.":" and the preserved recovery at "+recovery+" before reviewing another change.")
                    +" "+error.Message,markerOnly?null:recovery);
            }
            try
            {
                if(Read(recovery).Revision!=reviewed.Revision)
                    return new(written,false,"Metadata was published, but the displaced file differed from the review. Its actual bytes are preserved at "
                        +recovery+". Inspect both files and refresh before editing again.",recovery);
            }
            catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return new(written,false,"Metadata was published, but the displaced file could not be verified. Its recovery is preserved at "
                    +recovery+". Inspect both files and refresh before editing again. "+error.Message,recovery);
            }
            try{File.Delete(recovery);return new(written,true,"",null);}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException)
            {return new(written,true,"Reviewed metadata saved. The verified prior copy remains at "+recovery+".",recovery);}
        }
        finally
        {
            try{if(File.Exists(temporary))File.Delete(temporary);}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
            // Before publication only the exclusively-created reservation is ours to delete.
            if(!publicationAttempted&&recovery!=null&&reservation!=null)DeleteReservation(recovery,reservation);
        }
    }
    private static (string,byte[]) ReserveRecovery(string target)
    {
        for(int i=0;i<MaximumRecoveries;i++)
        {
            string path=target+".review-recovery-"+i;byte[] marker=Guid.NewGuid().ToByteArray();
            try
            {
                using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {file.Write(marker);file.Flush(flushToDisk:true);}
                return(path,marker);
            }
            catch(IOException)when(File.Exists(path)||Directory.Exists(path)||new FileInfo(path).LinkTarget!=null){}
        }
        throw new IOException("Four prior metadata recoveries already exist. Inspect and move them before saving another change.");
    }
    private static bool DeleteReservation(string path,byte[] marker)
    {
        try
        {
            var copy=Read(path,marker.Length);
            if(copy.Bytes==null)return true;
            if(!copy.Bytes.AsSpan().SequenceEqual(marker))return false;
            File.Delete(path);return true;
        }
        catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException){return false;}
    }

}

internal static partial class ReplayReels
{
    internal static ReplayReelDocument ReadReviewed(ReplayReviewedSidecar sidecar,string logical,uint duration)
    {
        var document=ReplayReviewedMetadata.Parse(sidecar,()=>new ReplayReelDocument{SourceReplay=logical});
        if(document.Version!=1 || document.Segments==null || document.Segments.Count>128
            || !ReplayPathComparer.Same(document.SourceReplay,logical)
            || document.Segments.Any(row=>row==null || row.Id==Guid.Empty || row.StartFrame>=row.EndFrame
                || row.EndFrame>duration || string.IsNullOrWhiteSpace(row.Name) || row.Name.Length>80 || !Enum.IsDefined(row.Camera))
            || document.Segments.Select(row=>row.Id).Distinct().Count()!=document.Segments.Count)
            throw new InvalidDataException("The saved reel is invalid. Repair it before editing.");
        return document;
    }
    internal static ReplayReviewedPublication SaveReviewed(ReplayReviewedSidecar sidecar,ReplayReelDocument document,Action verifyOwner)
        => ReplayReviewedMetadata.Commit(sidecar,document,verifyOwner);
}

internal static partial class ReplayAnnotations
{
    internal static ReplayAnnotationDocument ReadReviewed(ReplayReviewedSidecar sidecar,uint duration)
    {
        var document=ReplayReviewedMetadata.Parse(sidecar,()=>new ReplayAnnotationDocument());
        if(document.Version!=1 || document.Bookmarks==null || document.Highlights==null || document.Tags==null || document.Collections==null
            || document.Bookmarks.Count>8192 || document.Highlights.Count>8192
            || document.Bookmarks.Any(row=>row==null || row.Id==Guid.Empty || row.Frame>duration || string.IsNullOrWhiteSpace(row.Name))
            || document.Highlights.Any(row=>row==null || row.Id==Guid.Empty || row.StartFrame>=row.EndFrame || row.EndFrame>duration || string.IsNullOrWhiteSpace(row.Name))
            || document.Bookmarks.Select(row=>row.Id).Distinct().Count()!=document.Bookmarks.Count
            || document.Highlights.Select(row=>row.Id).Distinct().Count()!=document.Highlights.Count)
            throw new InvalidDataException("The annotation sidecar is invalid. Repair it before editing.");
        return document;
    }
    internal static ReplayReviewedPublication SaveReviewed(ReplayReviewedSidecar sidecar,ReplayAnnotationDocument document,Action verifyOwner)
        => ReplayReviewedMetadata.Commit(sidecar,document,verifyOwner);
}
