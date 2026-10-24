#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

public static partial class ReplayCamera
{
    internal static bool MemoryDocumentDirty { get; set; }
    private sealed record MemoryPreview(object Owner,object Scene,object Transport,long Generation,uint Frame,ReplayCameraTrack Track);
    private static MemoryPreview? _memoryPreview;
    internal static bool MemoryPreviewActive=>_memoryPreview!=null;
    internal static void SetMemoryPreview(object owner,object scene,object transport,long generation,uint frame,ReplayCameraTrack draft)
        => _memoryPreview=new(owner,scene,transport,generation,frame,draft);
    internal static void ClearMemoryPreview(){if(_memoryPreview!=null)Changed=true;_memoryPreview=null;}
    internal static bool TryMemoryPreview(Scene scene,out ReplayCameraKeyframe key)
    {
        key=default;var draft=_memoryPreview;
        if(draft==null)return false;
        if(!ReferenceEquals(draft.Owner,DemoPlayback.PlaybackOwner)||!ReferenceEquals(draft.Scene,scene)
            ||!ReferenceEquals(draft.Transport,DemoPlayback.Session.Transport)||draft.Generation!=DemoPlayback.Session.Transport.SeekGeneration
            ||draft.Frame!=ReplayController.CurrentFrame||!ReplayController.IsPaused||ReplayController.IsSeeking)
        {_memoryPreview=null;return false;}
        return draft.Track.Sample(draft.Frame,out key,TrackConstantSpeed);
    }

    internal static void ApplyReviewedToolsState(string logical,IReadOnlyList<ReplayCameraKeyframe> expected,byte[] state)
    {
        if(_trackPath==null||!ReplayPathComparer.Same(_trackPath,logical)||!Track.Keys.SequenceEqual(expected))
            throw new InvalidOperationException("The original camera track owner changed after publication.");
        if(!Track.ImportState(state))throw new InvalidDataException(Track.LastError);
        _restoreKey=null;ClearSelection();EditStatus="Reviewed camera track change saved.";Changed=true;
    }
    internal static void ApplyReviewedToolsKeys(string logical,IReadOnlyList<ReplayCameraKeyframe> expected,IReadOnlyList<ReplayCameraKeyframe> after)
    {
        if(_trackPath==null||!ReplayPathComparer.Same(_trackPath,logical)||!Track.Keys.SequenceEqual(expected))
            throw new InvalidOperationException("The original camera track owner changed after publication.");
        var valid=new ReplayCameraTrack();
        foreach(var key in after)if(!valid.Put(key))throw new InvalidDataException(valid.LastError);
        Track.Clear();foreach(var key in valid.Keys)Track.Put(key);
        _restoreKey=null;ClearSelection();EditStatus="Reviewed camera track change saved.";Changed=true;
    }
}
#endif
