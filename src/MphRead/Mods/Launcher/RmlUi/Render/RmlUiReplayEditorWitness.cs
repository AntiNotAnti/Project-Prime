#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Security.Cryptography;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    // Shared by explicit native editor review and export acceptance. Never called by readonly frame polling.
    private static class ReplayNativeFileWitness
    {
        public static string Source(string logical,string playback)
        {
            string before=Facts(logical,playback);
            var identity=ReplayCameraTrackIdentity.Create(logical);
            string sourceHash=Convert.ToHexString(identity.SourceContentHash),playbackHash;
            if(ReplayPathComparer.Same(logical,playback))playbackHash=sourceHash;
            else {using var input=new FileStream(playback,FileMode.Open,FileAccess.Read,FileShare.Read);playbackHash=Convert.ToHexString(SHA256.HashData(input));}
            if(Facts(logical,playback)!=before)throw new IOException("The replay changed while reviewing its source identity.");
            return before+":SOURCE-SHA:"+sourceHash+":PLAYBACK-SHA:"+playbackHash+$":RANGE:{identity.StartFrame}:{identity.EndFrame}";
        }
        public static string Facts(string logical,string playback)
        {
            string result=ReplayAnnotationWitness.RecordingRevision(logical)+":"+ReplayAnnotationWitness.RecordingRevision(playback);
            if(logical.EndsWith(ReplayVirtualClips.Extension,StringComparison.OrdinalIgnoreCase))
            {
                if(!ReplayVirtualClips.TryLoad(logical,out var clip)||clip==null)throw new InvalidDataException("The virtual clip is invalid.");
                result+=":"+Path.GetFullPath(clip.SourceReplay)+":"+ReplayAnnotationWitness.RecordingRevision(clip.SourceReplay);
            }
            return result;
        }
    }
}
#endif
