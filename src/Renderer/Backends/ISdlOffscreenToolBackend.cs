namespace MphRead;

/// <summary>
/// SDL GPU's deterministic offscreen boundary. It lives with the backend so
/// reusable hosts do not depend on the client-only ScenePresentation tool.
/// </summary>
internal interface ISdlOffscreenToolBackend
{
    bool TryBeginOffscreenFrame(out RenderBackendFrame frame);
    void RenderOffscreen(RenderBackendFrame frame, RenderFrame snapshot);
    bool TrySubmitOffscreenFrame(RenderBackendFrame frame);
    void FlushCaptures();
}
