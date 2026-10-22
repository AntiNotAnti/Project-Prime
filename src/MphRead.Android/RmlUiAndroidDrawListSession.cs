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
    private readonly RmlUiHost _host;
    private readonly RmlUiDrawListReader _reader = new();
    private readonly RmlUiAndroidGlesCompositor _compositor = new();
    private int _ownerThread;
    private int _width, _height;
    private float _density;

    internal RmlUiAndroidDrawListSession() : this(new RmlUiHost()) { }
    internal RmlUiAndroidDrawListSession(RmlUiHost host) => _host = host ?? throw new ArgumentNullException(nameof(host));
    internal RmlUiHost Host => _host;
    internal RmlUiAndroidInputQueue Input { get; } = new();
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
        Input.Resume(_host);
        return true;
    }

    internal void Render(int width, int height, float density)
    {
        VerifyOwner();
        if (!_host.Active) return;
        if (width <= 0 || height <= 0) return;
        if (_width != width || _height != height || _density != density)
        {
            // A reflow changes hit targets even without SurfaceDestroyed.
            // Retire old contacts/IME before Resize and do not override a UI
            // suspend which may arrive while native layout is being changed.
            var layoutInput = Input.SuspendForLayout();
            Input.Drain(_host);
            _host.Resize(width, height, density);
            _width = width; _height = height; _density = density;
            if (layoutInput.Resume) Input.Resume(_host, layoutInput.Scope);
        }
        Input.Drain(_host);
        _host.Update();
        Input.Refresh(_host);
        // Native Render populates the draw-list, never issues GLES calls.
        _host.Render(width, height);
        _compositor.Draw(_reader.Capture(), width, height);
    }

    internal void Shutdown()
    {
        VerifyOwner();
        try
        {
            SuspendInput();
        }
        finally
        {
            try { _compositor.Release(); }
            finally
            {
                _reader.Forget();
                _host.Shutdown();
                _width = _height = 0;
            }
        }
    }

    // The UI thread may call Input.Suspend immediately. The EGL owner must
    // call this before releasing its current surface to flush native holds.
    internal void SuspendInput()
    {
        VerifyOwner();
        Input.Suspend();
        Input.Drain(_host);
    }

    internal void ResumeInput()
    {
        VerifyOwner();
        if (_host.Active) Input.Resume(_host);
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
