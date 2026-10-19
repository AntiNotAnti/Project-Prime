#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    private sealed partial class CurrentReplayEditor
    {
        public Task<TheatreSpatialAnalysis> AnalyzeSpatial(TheatreEditorContext context,CancellationToken cancellation)
        {
            Validate(context,false);
            // Only immutable path/token values leave the engine thread. Completion must
            // revalidate the retained source/sidecar/scene owner before publication.
            string path=context.PlaybackPath;
            var geometry=ReplaySpatialAnalysis.Capture(MphRead.Mods.Network.DemoPlayback.ReviewedStudioWorld().Scene);
            return Task.Run(()=>ReplaySpatialAnalysis.Read(path,cancellation,geometry),cancellation);
        }
        public void ValidateSpatial(TheatreEditorContext context)=>Validate(context,false);
    }
}
#endif
