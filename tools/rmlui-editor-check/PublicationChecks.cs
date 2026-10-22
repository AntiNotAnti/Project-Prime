using System.Security.Cryptography;
using MphRead.Mods.Replay;
internal static class PublicationChecks
{
    internal static void Run()
    {
        int checks=0;void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        bool Reject(Action action){try{action();return false;}catch(IOException){return true;}}
        string directory=Path.Combine(Path.GetTempPath(),"prime-editor-publication-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"metadata.json");
            var missing=ReplayReviewedMetadata.Read(path);byte[] externalCreated="EXTERNAL CREATED AFTER FINAL VALIDATION"u8.ToArray();
            Check(Reject(()=>ReplayReviewedMetadata.Commit(missing,new{Value="proposed"},()=>{},()=>File.WriteAllBytes(path,externalCreated)))
                &&File.ReadAllBytes(path).SequenceEqual(externalCreated),"missing-target publication cannot overwrite new external destination after final validation");
            Check(!Directory.EnumerateFiles(directory,".reviewed-*").Any(),"failed non-overwriting publish removes its own temporary only");
            var existing=ReplayReviewedMetadata.Read(path);byte[] externalRewrite="EXTERNAL REWRITE AFTER FINAL VALIDATION"u8.ToArray();
            var raced=ReplayReviewedMetadata.Commit(existing,new{Value="published proposal"},()=>{},()=>File.WriteAllBytes(path,externalRewrite));
            Check(!raced.Verified&&raced.RecoveryPath!=null&&File.ReadAllBytes(raced.RecoveryPath).SequenceEqual(externalRewrite),
                "actual raced prior bytes remain recoverable after atomic replacement");
            Check(File.ReadAllBytes(path).SequenceEqual(raced.Written.Bytes!)&&raced.Written.Revision==Convert.ToHexString(SHA256.HashData(raced.Written.Bytes!))
                &&raced.Notice.Contains("published")&&raced.Notice.Contains("refresh"),"raced publication reports explicit unverified outcome with exact written receipt");
            byte[] racedRecovery=File.ReadAllBytes(raced.RecoveryPath!);
            existing=ReplayReviewedMetadata.Read(path);var verified=ReplayReviewedMetadata.Commit(existing,new{Value="verified publication"},()=>{});
            Check(verified.Verified&&verified.RecoveryPath==null&&File.ReadAllBytes(path).SequenceEqual(verified.Written.Bytes!),
                "verified replacement returns exact successful bytes without post-publication recapture");
            Check(File.ReadAllBytes(raced.RecoveryPath!).SequenceEqual(racedRecovery),"later verified save never overwrites earlier unverified recovery");
            var expected=ReplayReviewedMetadata.Read(path);int ownerChecks=0;
            Check(Reject(()=>ReplayReviewedMetadata.Commit(expected,new{Value="never published"},()=>{},()=>throw new IOException("injected before publication")))
                &&ReplayReviewedMetadata.Read(path).Revision==expected.Revision
                &&Directory.GetFiles(directory,"*.review-recovery-*").Length==1,
                "failure after final validation preserves original target and removes exclusive reservation");
            Check(Reject(()=>ReplayReviewedMetadata.Commit(expected,new{Value="never published"},()=>{if(++ownerChecks==2)throw new IOException("owner changed after temp flush");}))
                &&ReplayReviewedMetadata.Read(path).Revision==expected.Revision,"original pre-publication owner-failure invariant remains intact");
            for(int i=1;i<ReplayReviewedMetadata.MaximumRecoveries;i++)
            {
                var review=ReplayReviewedMetadata.Read(path);byte[] outside=System.Text.Encoding.UTF8.GetBytes("EXTERNAL RACE "+i);
                var result=ReplayReviewedMetadata.Commit(review,new{Value=i},()=>{},()=>File.WriteAllBytes(path,outside));
                Check(!result.Verified&&result.RecoveryPath!=null&&File.ReadAllBytes(result.RecoveryPath).SequenceEqual(outside),"each distinct race has its own recoverable prior bytes "+i);
            }
            var full=ReplayReviewedMetadata.Read(path);var copies=Directory.GetFiles(directory,"*.review-recovery-*").ToDictionary(p=>p,File.ReadAllBytes);
            Check(copies.Count==ReplayReviewedMetadata.MaximumRecoveries
                &&Reject(()=>ReplayReviewedMetadata.Commit(full,new{Value="must not publish"},()=>{}))
                &&ReplayReviewedMetadata.Read(path).Revision==full.Revision
                &&copies.All(pair=>File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)),
                "full bounded recovery slots reject before publication without discarding any prior or current bytes");
            string large=Path.Combine(directory,"large.json");File.WriteAllText(large,"REVIEWED");var small=ReplayReviewedMetadata.Read(large);
            var unverified=ReplayReviewedMetadata.Commit(small,new{Value="published"},()=>{},()=>File.WriteAllBytes(large,new byte[ReplayReviewedMetadata.MaximumBytes+1]));
            Check(!unverified.Verified&&unverified.RecoveryPath!=null&&new FileInfo(unverified.RecoveryPath).Length==ReplayReviewedMetadata.MaximumBytes+1,
                "oversize raced prior file is preserved without unbounded verification allocation");
            Check(!Directory.EnumerateFiles(directory,".reviewed-*").Any(),"all publication paths clean proposed temporary files only");
            Console.WriteLine($"PASS {checks} deterministic actual-file publication/race/recovery checks; temporary synthetic metadata only");
        }
        finally{Directory.Delete(directory,true);}
    }
}
