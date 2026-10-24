#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

public sealed partial class TheatrePlaybackPagePresenter
{
    internal bool AuxiliaryPreviewInteracting => _timeline?.Dragging == true
        || _curveDrag != null || _curveBox != null || _workspaceScrub != null || Viewport.Interacting;
}
#endif
