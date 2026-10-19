#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    private sealed partial class CurrentReplayEditor:ITheatreWorkspaceTimelineBackend
    {
        public void SetReviewedTimelineRange(TheatreEditorContext context,uint start,uint end)
        {
            Validate(context,true);if(start>=end||end>context.Duration)throw new ArgumentException("Choose a range of at least one recorded frame.");
            SetRange((ReplayTransport)context.Transport,start,end);
        }
    }
}
#endif
