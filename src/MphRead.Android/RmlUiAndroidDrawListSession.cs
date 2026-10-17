#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;

namespace MphRead.Droid;

/// <summary>
/// Native RmlUi -> copied draw list -> GLES terminal pass. Must be constructed,
/// rendered, resized and retired by the *existing* GameView EGL owner thread.
/// RML-20 supplies the Android launcher surface, extracted asset root and input.
/// </summary>
internal sealed class RmlUiAndroidDrawListSession : IDisposable
{
    private readonly RmlUiHost _host = new();
    private readonly RmlUiDrawListReader _reader = new();
    private readonly RmlUiAndroidGlesCompositor _compositor = new();
    private int _ownerThread;
    private int _width, _height;
    private float _density;

    internal RmlUiHost Host => _host;
    internal bool Active => _host.Active;
    internal (long Captured, long Reused, long UploadedGeometry, long UploadedTextures) Metrics =>
        (_reader.CapturedFrames, _reader.ReusedFrames,
         _compositor.UploadedGeometry, _compositor.UploadedTextures);

    internal bool Initialize(int width, int height, float density, string extractedAssetRoot)
    {
        VerifyOwner();
        if (string.IsNullOrWhiteSpace(extractedAssetRoot))
            throw new ArgumentException("The native bridge requires an extracted, verified asset directory.", nameof(extractedAssetRoot));
        if (_host.Active) throw new InvalidOperationException("RmlUi draw-list session is already active.");
        if (!_host.Initialize(width, height, density, extractedAssetRoot, RmlUiRenderBackend.DrawList))
            return false;
        _width = width; _height = height; _density = density;
        return true;
    }

    internal void Render(int width, int height, float density)
    {
        VerifyOwner();
        if (!_host.Active) return;
        if (width <= 0 || height <= 0) return;
        if (_width != width || _height != height || _density != density)
        {
            _host.Resize(width, height, density);
            _width = width; _height = height; _density = density;
        }
        _host.Update();
        // Native Render populates the draw-list, never issues GLES calls.
        _host.Render(width, height);
        _compositor.Draw(_reader.Capture(), width, height);
    }

    internal void Shutdown()
    {
        VerifyOwner();
        try
        {
            _compositor.Release();
        }
        finally
        {
            _reader.Forget();
            _host.Shutdown();
            _width = _height = 0;
        }
    }

    private void VerifyOwner()
    {
        int current = Environment.CurrentManagedThreadId;
        if (_ownerThread == 0) _ownerThread = current;
        else if (_ownerThread != current)
            throw new InvalidOperationException("RmlUi session must remain on the EGL owner thread.");
    }

    public void Dispose() => Shutdown();
}
#endif
