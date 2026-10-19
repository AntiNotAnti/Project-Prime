#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

public static partial class ReplayCamera
{
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
