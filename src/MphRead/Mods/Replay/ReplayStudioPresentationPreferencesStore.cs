#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

internal sealed record ReplayStudioPresentationBinding(string SourceSha256,uint StartFrame,uint EndFrame);
internal sealed record ReplayStudioPreferencePose(float X,float Y,float Z,float Qx,float Qy,float Qz,float Qw,float Fov,float Roll);
internal sealed record ReplayStudioPresentationPreferences(ReplayCameraMode Camera,int PlayerSlot,float Fov,
    bool GameHud,bool ReplayOverlay,bool ConstantSpeed,bool CollisionAvoidance,bool WhatShooterSaw,
    ReplayStudioPreferencePose? FreePose=null);
internal sealed record ReplayStudioPresentationPreferencesReview(ReplayReviewedSidecar Reviewed,string SourceRevision,
    ReplayStudioPresentationPreferences? Preferences);

/// <summary>Small source-bound UI preferences. Never stores a replay world, advances playback or writes from a frame poll.</summary>
internal static class ReplayStudioPresentationPreferencesStore
{
    internal const string Extension=".studio-presentation.json";
    private const int MaximumBytes=64*1024;
    private sealed record Document(int Version,string SourceRevision,ReplayStudioPresentationPreferences Preferences);
    internal static ReplayStudioPresentationPreferences Freeze(ReplayStudioPresentationPreferences value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(!Enum.IsDefined(value.Camera)||value.PlayerSlot is <0 or >7
            ||!float.IsFinite(value.Fov)||value.Fov is <10 or >150)
            throw new InvalidDataException("Saved Studio presentation values are outside supported bounds.");
        if(value.FreePose is{} pose)
        {
            foreach(float n in new[]{pose.X,pose.Y,pose.Z,pose.Qx,pose.Qy,pose.Qz,pose.Qw,pose.Fov,pose.Roll})
                if(!float.IsFinite(n))throw new InvalidDataException("Saved Studio camera pose contains non-finite values.");
            double length=(double)pose.Qx*pose.Qx+(double)pose.Qy*pose.Qy+(double)pose.Qz*pose.Qz+(double)pose.Qw*pose.Qw;
            if(Math.Abs(pose.X)>100000||Math.Abs(pose.Y)>100000||Math.Abs(pose.Z)>100000||length<.00001||length>1000000
                ||pose.Fov<MathF.PI/18-.00001f||pose.Fov>MathF.PI*5/6+.00001f||Math.Abs(pose.Roll)>MathF.PI*2)
                throw new InvalidDataException("Saved Studio camera pose is outside supported bounds.");
            float divisor=(float)Math.Sqrt(length);
            value=value with{FreePose=pose with{Qx=pose.Qx/divisor,Qy=pose.Qy/divisor,Qz=pose.Qz/divisor,Qw=pose.Qw/divisor}};
        }
        if(value.Camera==ReplayCameraMode.Free&&value.FreePose==null)
            throw new InvalidDataException("A saved free camera requires its reviewed pose.");
        return value;
    }
    internal static Task<ReplayStudioPresentationPreferencesReview> ReadAsync(string logicalPath,string sourceRevision,
        CancellationToken cancellation=default)
        =>ReplayStorageJobs.Run(()=>Read(logicalPath,sourceRevision,cancellation),cancellation);
    internal static ReplayStudioPresentationPreferencesReview Read(string logicalPath,string sourceRevision,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        if(string.IsNullOrWhiteSpace(sourceRevision)||sourceRevision.Length>32768)throw new ArgumentException("Review this recording source before reading presentation preferences.");
        var reviewed=ReplayReviewedMetadata.Read(logicalPath+Extension,MaximumBytes);
        if(reviewed.Bytes==null)return new(reviewed,sourceRevision,null);
        var document=ReplayReviewedMetadata.Parse<Document>(reviewed,()=>throw new InvalidDataException("Studio preferences are missing."));
        if(document.Version is not(1 or 2)||BindingFromRevision(document.SourceRevision) is not{} saved
            ||BindingFromRevision(sourceRevision) is not{} current||saved!=current)
            throw new InvalidDataException("Studio presentation preferences belong to a different recording source or clip range.");
        var frozen=Freeze(document.Preferences);cancellation.ThrowIfCancellationRequested();
        return new(reviewed,sourceRevision,frozen);
    }
    internal static ReplayStudioPresentationBinding? BindingFromRevision(string revision)
    {
        if(string.IsNullOrWhiteSpace(revision))return null;
        const string marker="SOURCE-SHA:";
        int source=revision.LastIndexOf(marker,StringComparison.Ordinal),range=revision.LastIndexOf(":RANGE:",StringComparison.Ordinal);
        if(source<0||range<source+marker.Length+64)return null;
        int hashStart=source+marker.Length;
        string hash=revision.Substring(hashStart,64);
        if(!hash.All(char.IsAsciiHexDigit)||revision[hashStart+64]!=':')return null;
        string[] frames=revision[(range+7)..].Split(':');
        if(frames.Length!=2||!uint.TryParse(frames[0],NumberStyles.None,CultureInfo.InvariantCulture,out uint start)
            ||!uint.TryParse(frames[1],NumberStyles.None,CultureInfo.InvariantCulture,out uint end)||end<start)return null;
        return new(hash.ToUpperInvariant(),start,end);
    }
    internal static ReplayStudioPresentationBinding CreateBinding(byte[] sourceHash,uint start,uint end)
    {
        if(sourceHash==null||sourceHash.Length!=32||end<start)throw new ArgumentException("Review a SHA-256 source and valid absolute clip range.");
        return new(Convert.ToHexString(sourceHash),start,end);
    }
    private static ReplayStudioPresentationBinding RequireBinding(string revision)
        =>BindingFromRevision(revision)??throw new InvalidDataException("The recording source binding is missing or malformed.");
    private static string BindingText(ReplayStudioPresentationBinding binding)
        =>FormattableString.Invariant($"SOURCE-SHA:{binding.SourceSha256}:RANGE:{binding.StartFrame}:{binding.EndFrame}");
    internal static ReplayStudioPresentationPreferencesReview ReadForCopy(string logical,ReplayStudioPresentationBinding binding,CancellationToken token=default)
        =>Read(logical,BindingText(binding),token);
    internal static bool StageRebound(string stagedLogical,ReplayStudioPresentationPreferencesReview review,
        ReplayStudioPresentationBinding binding,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();ReplayReviewedMetadata.Validate(review.Reviewed);
        if(review.Preferences==null)return false;
        var original=RequireBinding(review.SourceRevision);
        // Full recordings use the existing (0,0) identity sentinel. A saved
        // clip may only be copied unchanged or narrowed within its source range.
        if(original.SourceSha256!=binding.SourceSha256
            ||(!(original.StartFrame==0&&original.EndFrame==0)&&(binding.StartFrame<original.StartFrame||binding.EndFrame>original.EndFrame)))
            throw new InvalidDataException("The copied view must retain the same recording source and a contained clip range.");
        var frozen=Freeze(review.Preferences);
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(new Document(2,BindingText(binding),frozen));
        if(bytes.Length>MaximumBytes)throw new InvalidDataException("The copied view preferences exceed their size bound.");
        token.ThrowIfCancellationRequested();
        using(var file=new FileStream(stagedLogical+Extension,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {file.Write(bytes);file.Flush(flushToDisk:true);}
        token.ThrowIfCancellationRequested();return true;
    }
    internal static Task<ReplayStudioPresentationPreferencesReview> SaveAsync(ReplayStudioPresentationPreferencesReview review,
        ReplayStudioPresentationPreferences preferences,Action verifyOwner,CancellationToken cancellation=default)
    {
        var frozen=Freeze(preferences);
        return ReplayStorageJobs.Run(()=>
        {
            void Verify(){cancellation.ThrowIfCancellationRequested();verifyOwner();}
            Verify();
            var result=ReplayReviewedMetadata.Commit(review.Reviewed,new Document(2,BindingText(RequireBinding(review.SourceRevision)),frozen),Verify);
            if(!result.Verified)throw new IOException(result.Notice);
            return new ReplayStudioPresentationPreferencesReview(result.Written,review.SourceRevision,frozen);
        },cancellation);
    }
}
#endif
