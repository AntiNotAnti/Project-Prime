using MphRead;

namespace ProjectPrime.Editor.App;

/// <summary>
/// Adapts editor-owned HUD drawing to the renderer's fixed presentation-stage
/// contract without introducing a second rendering pipeline.
/// </summary>
internal static class EditorPresentationStages
{
    public static void BeginHudOverlay(RenderFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        frame.AddOverlayCommand(RenderOverlayCommand.StageMarker(RenderPresentationStage.HudScene));
        frame.AddOverlayCommand(RenderOverlayCommand.StageMarker(RenderPresentationStage.Cel));
        frame.AddOverlayCommand(RenderOverlayCommand.StageMarker(RenderPresentationStage.SceneComposite));
        frame.AddOverlayCommand(RenderOverlayCommand.StageMarker(RenderPresentationStage.HudOverlay));
    }

    public static void Complete(RenderFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        frame.AddOverlayCommand(RenderOverlayCommand.StageMarker(RenderPresentationStage.SpectatorOverlay));
        frame.AddOverlayCommand(RenderOverlayCommand.StageMarker(RenderPresentationStage.ReplayOverlay));
        frame.AddOverlayCommand(RenderOverlayCommand.StageMarker(RenderPresentationStage.Fade));
    }
}
