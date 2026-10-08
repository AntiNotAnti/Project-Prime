#if MPHREAD_RMLUI_ANDROID
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Format = Android.Graphics.Format;
using Android.Opengl;
using Android.Views;
using Android.Views.InputMethods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Launcher.RmlUi.Components;
using AccessibilityNodeProvider = Android.Views.Accessibility.AccessibilityNodeProvider;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Droid;

// Android callbacks publish immutable snapshots. The dedicated owner is the
// only thread allowed to call native RmlUi, Core controllers, EGL or Vulkan.
internal sealed partial class AndroidRmlUiView : SurfaceView, ISurfaceHolderCallback
{
    private readonly object _gate = new();
    private readonly ConcurrentQueue<Action<AndroidRmlUiSession>> _commands = new();
    private AndroidRenderLifetime _lifetime = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _suspendCompleted;
    private volatile bool _matchSuspended;
    private readonly AndroidRmlUiInput _input;
    private readonly RmlUiAccessibilityService _accessibility = new();
    private readonly AndroidRmlUiAccessibility _accessibilityProvider;
    private readonly MenuSettings _settings;
    private readonly IReadOnlyList<string> _rooms;
    private readonly Action<LaunchPlan> _launch;
    private readonly Action<LaunchPlan> _connectedLobby;
    private readonly Action<RmlUiRouteArgument> _route;
    private readonly Action _quit;
    private readonly Func<Task<Stream?>>? _pickRom;
    private readonly Action<string> _failed;
    private readonly Action<string>? _presented;
    private readonly Thread _owner;
    private AndroidRmlUiInputSnapshot _snapshot = AndroidRmlUiInputSnapshot.Empty;
    private ISurfaceHolder? _wanted, _bound;
    private int _width, _height;
    private bool _paused;
    private long _surfaceGeneration;
    private EGLDisplay? _display;
    private EGLConfig? _config;
    private EGLContext? _context;
    private EGLSurface? _surface, _pbuffer;
    private nint _nativeWindow;
    private bool _modern;
    private AndroidRmlUiSession? _session;
    private AndroidRmlUiGlesRenderer? _gles;
    private int _frames;
#if MPHREAD_RMLUI_ANDROID_CHECK
    private bool _gpuCheck;
#endif
    private float _density;
    [DllImport("android")] private static extern nint ANativeWindow_fromSurface(nint env, nint surface);
    [DllImport("android")] private static extern void ANativeWindow_release(nint window);

    internal AndroidRmlUiView(Context context, MenuSettings settings, IReadOnlyList<string> rooms,
        Action<LaunchPlan> launch, Action<RmlUiRouteArgument> route, Action quit, Action<string> failed, Action<LaunchPlan> connectedLobby, Func<Task<Stream?>>? pickRom = null, Action<string>? presented = null) : base(context)
    {
        (_settings, _rooms, _launch, _route, _quit, _failed, _connectedLobby) = (settings, rooms, launch, route, quit, failed, connectedLobby);
        _pickRom = pickRom; _presented = presented;
        _density = Math.Max(1, context.Resources?.DisplayMetrics?.Density ?? 1);
        Focusable = true; FocusableInTouchMode = true;
        Holder!.AddCallback(this);
        _input = new(this, () => Volatile.Read(ref _snapshot), SendInput, Back,
            touch => Enqueue(s => s.DispatchTouch(touch)));
        _accessibilityProvider = new(this, command =>
        {
            if (!_accessibility.Enqueue(command)) return false;
            Enqueue(s => {
                int accepted = _accessibility.Drain(s.Host);
#if MPHREAD_RMLUI_ANDROID_CHECK
                Console.WriteLine($"[rmlui-android] semantic command {command.Action} revision={command.Revision} textLength={command.Text.Length} nativeAccepted={accepted}");
#endif
            }); return true;
        });
        _owner = new Thread(Run) { IsBackground = true, Name = "Project Prime RmlUi owner" }; _owner.Start();
    }
    internal Task StopAsync()
    {
        _input.Cancel(); _stop.Cancel(); _lifetime.RequestStop();
        lock (_gate) Monitor.PulseAll(_gate);
        return _completed.Task;
    }
    internal Task SuspendForMatchAsync()
    {
        _input.Cancel();
        lock (_gate)
        {
            if (_matchSuspended) return _suspendCompleted?.Task ?? Task.CompletedTask;
            _suspendCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _matchSuspended = true; Monitor.PulseAll(_gate); return _suspendCompleted.Task;
        }
    }
    internal void ResumeAfterMatch()
    {
        lock (_gate) { _matchSuspended = false; Monitor.PulseAll(_gate); }
    }
    internal void SetPaused(bool paused)
    {
        if (paused) _input.Cancel();
        lock (_gate) { _paused = paused; Monitor.PulseAll(_gate); }
    }
    internal void Back() => Enqueue(s => s.Back());
    internal void Navigate(RmlUiRouteArgument route) => Enqueue(s => s.Open(route));
    internal void Enqueue(Action<AndroidRmlUiSession> command)
    {
        if (_stop.IsCancellationRequested) return;
        _commands.Enqueue(command);
        lock (_gate) Monitor.PulseAll(_gate);
    }
    public override WindowInsets? OnApplyWindowInsets(WindowInsets? insets)
    {
        if (insets != null && LayoutParameters is Android.Widget.FrameLayout.LayoutParams layout)
        {
            int left = 0, top = 0, right = 0, bottom = 0;
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var safe = insets.GetInsets(WindowInsets.Type.DisplayCutout() | WindowInsets.Type.SystemBars());
                left = safe.Left; top = safe.Top; right = safe.Right; bottom = safe.Bottom;
            }
            else if (OperatingSystem.IsAndroidVersionAtLeast(28) && insets.DisplayCutout is { } cutout)
            { left = cutout.SafeInsetLeft; top = cutout.SafeInsetTop; right = cutout.SafeInsetRight; bottom = cutout.SafeInsetBottom; }
            if (layout.LeftMargin != left || layout.TopMargin != top || layout.RightMargin != right || layout.BottomMargin != bottom)
            { layout.SetMargins(left, top, right, bottom); LayoutParameters = layout; }
        }
        return base.OnApplyWindowInsets(insets);
    }
    public override bool OnTouchEvent(MotionEvent? e) => e != null && _input.Touch(e);
    public override AccessibilityNodeProvider? AccessibilityNodeProvider => _accessibilityProvider;
    protected override bool DispatchHoverEvent(MotionEvent? e) => e != null && _accessibilityProvider.Hover(e) || base.DispatchHoverEvent(e);
    public override bool OnGenericMotionEvent(MotionEvent? e) => e != null && _input.Generic(e) || base.OnGenericMotionEvent(e);
    public override bool OnKeyDown(Keycode code, KeyEvent? e) => _input.Key(code, e, true) || base.OnKeyDown(code, e);
    public override bool OnKeyUp(Keycode code, KeyEvent? e) => _input.Key(code, e, false) || base.OnKeyUp(code, e);
    public override bool OnCheckIsTextEditor() => Volatile.Read(ref _snapshot).TextState.HasValue;
    public override IInputConnection? OnCreateInputConnection(EditorInfo? info) => _input.Connection(info);
    public void SurfaceCreated(ISurfaceHolder holder) { }
    public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
    {
        lock (_gate) { _wanted = holder; _width = width; _height = height; _density = Math.Max(1, Resources?.DisplayMetrics?.Density ?? 1); _surfaceGeneration++; Monitor.PulseAll(_gate); }
    }
    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        _input.Cancel();
        lock (_gate)
        {
            if (ReferenceEquals(_wanted, holder)) _wanted = null;
            _surfaceGeneration++; Monitor.PulseAll(_gate);
            // One frame can be in flight. The owner acknowledges native-window
            // detach before Android returns this destroyed surface to its pool.
            while (ReferenceEquals(_bound, holder) && !_completed.Task.IsCompleted)
                Monitor.Wait(_gate, 100);
        }
    }
    private void Run()
    {
        IDisposable? ownership = null;
        Exception? failure = null;
        try
        {
            ownership = _lifetime.Enter();
            string root = AndroidRmlUiAssets.Extract(Context!);
#if MPHREAD_RMLUI_ANDROID_CHECK
            // Select the real GLES path explicitly for its pixel fixture;
            // no preference is written and ordinary builds ignore this extra.
            if ((Context as Android.App.Activity)?.Intent?.GetStringExtra("rmlui-renderer") is { } checkRenderer)
            {
                if (checkRenderer is not ("opengl" or "vulkan"))
                    throw new ArgumentException("The Android renderer check accepts opengl or vulkan.");
                GraphicsBackendPolicy.Configure(checkRenderer);
            }
#endif
            GraphicsBackendPolicy.LoadPreference(); _modern = GraphicsBackendPolicy.ModernGameplayRequested;
            long boundGeneration = -1;
            while (!_stop.IsCancellationRequested)
            {
                if (_matchSuspended)
                {
                    Exception? staleLaunch = null;
                    try { _session?.SuspendGraphics(); }
                    catch (Exception ex) { staleLaunch = ex; }
                    ReleaseGpu();
                    _lifetime.Complete(ownership); ownership = null;
                    PublishInput(false);
                    if (staleLaunch == null) _suspendCompleted?.TrySetResult();
                    else _suspendCompleted?.TrySetException(staleLaunch);
                    lock (_gate) while (_matchSuspended && !_stop.IsCancellationRequested) Monitor.Wait(_gate, 100);
                    boundGeneration = -1;
                    continue;
                }
                if (ownership == null)
                {
                    _lifetime = new(); ownership = _lifetime.Enter();
                    int resumeWidth, resumeHeight; float resumeDensity;
                    lock (_gate) { resumeWidth = Math.Max(1, _width); resumeHeight = Math.Max(1, _height); resumeDensity = _density; }
                    _session?.ResumeGraphics(root, resumeWidth, resumeHeight, resumeDensity);
                }
                lock (_gate)
                {
                    if (_wanted == null || _width <= 0 || _height <= 0 || _paused)
                    {
                        _session?.ReleaseInput(); PublishInput(false); DetachWindow();
                        Monitor.Wait(_gate, 100); continue;
                    }
                    if (_session == null)
                    {
                        int initialWidth = _width, initialHeight = _height; float initialDensity = _density;
                        Monitor.Exit(_gate);
                        try
                        {
                            _session = new(root, initialWidth, initialHeight, initialDensity, _settings, _rooms,
                                plan => PostUi(() => _launch(plan)), route => PostUi(() => _route(route)), () => PostUi(_quit), plan => PostUi(() => _connectedLobby(plan)), _pickRom);
                        }
                        finally { Monitor.Enter(_gate); }
                        Console.WriteLine($"[rmlui-android] native host active {_width}x{_height} density={_density} backend={(_modern ? "vulkan" : "gles3")}");
                        if (_wanted == null || _paused || _matchSuspended || _stop.IsCancellationRequested) continue;
                    }
                    if (_bound != _wanted || boundGeneration != _surfaceGeneration)
                    {
                        DetachWindow();
                        AttachWindow(_wanted, _width, _height); boundGeneration = _surfaceGeneration;
                    }
                    if (_bound == null) { Monitor.Wait(_gate, 100); continue; }
#if MPHREAD_RMLUI_ANDROID_CHECK
                    if (!_modern && !_gpuCheck)
                    {
                        _gpuCheck = true;
                        string check = AndroidRmlUiGlesCheck.Run();
                        Console.WriteLine("[rmlui-android] " + check);
                        AndroidRmlUiCheckReport.Write(Context!, "rmlui-android-gles-check.txt", check + "\n");
                    }
#endif
                    _session.Host.Resize(_width, _height, _density);
                    for (int i = 0; i < 256 && _commands.TryDequeue(out var command); i++) command(_session);
                    _session.Update();
                    if (_modern)
                    {
                        GraphicsApi.Viewport(0, 0, _width, _height);
                        GraphicsApi.ClearColor(10 / 255f, 12 / 255f, 16 / 255f, 1);
                        GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
                        _session.Host.Render(_width, _height);
                        RmlUiGpuCompositor.DrawNativeFrame(_width, _height);
                        ModernGraphicsCompat.Present();
                    }
                    else
                    {
                        GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, 0); GLES30.GlViewport(0, 0, _width, _height);
                        GLES30.GlClearColor(10 / 255f, 12 / 255f, 16 / 255f, 1); GLES30.GlClear(GLES30.GlColorBufferBit);
                        _session.Host.Render(_width, _height); (_gles ??= new()).Draw(_width, _height);
                        if (!EGL14.EglSwapBuffers(_display, _surface))
                        {
                            int error = EGL14.EglGetError();
                            if (error == 0x300E) { RecoverContext(); boundGeneration = -1; continue; }
                            throw new InvalidOperationException($"Android RmlUi swap failed (0x{error:X}).");
                        }
                    }
                    bool presented = !_modern || ModernGraphicsCompat.LastPresentationSucceeded;
                    _session.Presented(presented); PublishInput(presented);
                    if (presented && ++_frames == 1)
                    {
                        File.WriteAllText(Path.Combine(Context!.FilesDir!.AbsolutePath, "rmlui-android-runtime.txt"),
                            $"PRESENTED native page {_session.Pages.Manager.PageKey} {_width}x{_height} density={_density} backend={(_modern ? "vulkan" : "gles3")}\n");
                        Console.WriteLine("[rmlui-android] first native frame presented");
                        string actualRenderer = _modern ? "vulkan" : "opengl";
                        PostUi(() => _presented?.Invoke(actualRenderer));
#if MPHREAD_RMLUI_ANDROID_CHECK
                        _ = RunRequestedChecks();
#endif
                    }
                }
                _stop.Token.WaitHandle.WaitOne(8);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { Console.WriteLine("[rmlui-android] " + ex); PostUi(() => _failed(ex.Message)); }
        finally
        {
            try
            {
                // Core/documents retire before graphics and process-wide lease.
                _session?.Dispose(); _session = null;
                if (ownership != null) ReleaseGpu();
                PublishInput(false);
            }
            catch (Exception ex) { failure = ex; Console.WriteLine("[rmlui-android] retirement failed " + ex); }
            finally
            {
                _lifetime.Complete(ownership, failure);
                if (failure == null) _completed.TrySetResult(); else _completed.TrySetException(failure);
                _suspendCompleted?.TrySetException(new OperationCanceledException("The Android native menu owner retired."));
                lock (_gate) { _bound = null; Monitor.PulseAll(_gate); }
            }
        }
    }
    private void ReleaseGpu()
    {
        if (_modern) RmlUiGpuCompositor.ReleaseNativeFrame();
        else { MakePbufferCurrent(); _gles?.Dispose(); _gles = null; }
        lock (_gate) DetachWindow();
        if (_modern) ModernGraphicsCompat.Shutdown(); else DestroyContext();
    }
    private void PublishInput(bool active)
    {
        var value = AndroidRmlUiInputSnapshot.Empty;
        if (active && _session != null)
        {
            var host = _session.Host;
            if (host.TryGetTextInputState(out var state))
            {
                string id = host.FocusedElement();
                value = new(host.CurrentInputDocument, state, host.ReadField(state.Document, id),
                    id.Contains("password", StringComparison.OrdinalIgnoreCase), _session.CaptureHudCanvas());
            }
            else value = new(host.CurrentInputDocument, HudCanvas: _session.CaptureHudCanvas());
        }
        var previous = Interlocked.Exchange(ref _snapshot, value);
        var previousAccessibility = _accessibility.Snapshot;
        var accessibility = previousAccessibility;
        if (active && _session != null)
        {
            accessibility = _accessibility.Capture(_session.Host);
        }
        else { _accessibility.Retire(); accessibility = _accessibility.Snapshot; }
        if (accessibility.Document != previousAccessibility.Document || accessibility.Revision != previousAccessibility.Revision)
            PostUi(() => _accessibilityProvider.Publish(accessibility));
        if (previous.TextState?.FocusEpoch != value.TextState?.FocusEpoch) PostUi(_input.PublishKeyboard);
        else if (previous.TextState != value.TextState || previous.Value != value.Value) PostUi(_input.PublishSelection);
    }
    private void PostUi(Action action) => ((Android.App.Activity)Context!).RunOnUiThread(action);
    private void SendInput(RmlUiPlatformInputEvent input)
    {
        string? paste = AndroidRmlUiClipboard.ReadForPaste(Context, input);
        Enqueue(session =>
        {
            if (input.Document != session.Host.CurrentInputDocument) return;
            if (paste != null) session.Host.SetClipboard(paste);
            session.Dispatch(input);
            if (AndroidRmlUiClipboard.ReadAfterCopy(session.Host, input) is { } text)
                PostUi(() => AndroidRmlUiClipboard.Write(Context, text));
        });
    }
    private void AttachWindow(ISurfaceHolder holder, int width, int height)
    {
        if (holder.Surface?.IsValid != true) return;
        if (_modern)
        {
            _nativeWindow = ANativeWindow_fromSurface(Android.Runtime.JNIEnv.Handle, holder.Surface.Handle);
            if (_nativeWindow == 0) throw new InvalidOperationException("Android native window is unavailable.");
            try { ModernGraphicsCompat.AttachAndroidWindow(_nativeWindow, width, height); }
            catch (Exception ex) when (!ModernGraphicsCompat.Active)
            {
                ANativeWindow_release(_nativeWindow); _nativeWindow = 0;
                GraphicsBackendPolicy.UseCompatibilityFallback(ex.Message); _modern = false;
                AttachWindow(holder, width, height); return;
            }
        }
        else
        {
            EnsureContext();
            _surface = EGL14.EglCreateWindowSurface(_display, _config, holder.Surface, new[] { EGL14.EglNone }, 0);
            if (_surface == null || _surface.Equals(EGL14.EglNoSurface)) throw new InvalidOperationException("Android RmlUi EGL window creation failed.");
            if (!EGL14.EglMakeCurrent(_display, _surface, _surface, _context)) throw new InvalidOperationException("Android RmlUi EGL make-current failed.");
            EGL14.EglSwapInterval(_display, 1); EsBindings.Load();
        }
        _bound = holder;
    }
    private void EnsureContext()
    {
        if (_context != null) return;
        _display = EGL14.EglGetDisplay(EGL14.EglDefaultDisplay);
        int[] version = new int[2];
        if (!EGL14.EglInitialize(_display, version, 0, version, 1)) throw new InvalidOperationException("Android RmlUi EGL initialization failed.");
        int[] attributes = { EGL14.EglRenderableType, 0x40, EGL14.EglSurfaceType, EGL14.EglWindowBit | EGL14.EglPbufferBit,
            EGL14.EglRedSize, 8, EGL14.EglGreenSize, 8, EGL14.EglBlueSize, 8, EGL14.EglDepthSize, 24, EGL14.EglStencilSize, 8, EGL14.EglNone };
        var configs = new EGLConfig[1]; int[] count = new int[1];
        if (!EGL14.EglChooseConfig(_display, attributes, 0, configs, 0, 1, count, 0) || count[0] == 0)
            throw new InvalidOperationException("Android RmlUi needs an ES3 depth/stencil configuration.");
        _config = configs[0]; _context = EGL14.EglCreateContext(_display, _config, EGL14.EglNoContext,
            new[] { EGL14.EglContextClientVersion, 3, EGL14.EglNone }, 0);
        if (_context == null || _context.Equals(EGL14.EglNoContext)) throw new InvalidOperationException("Android RmlUi ES3 context creation failed.");
        _pbuffer = EGL14.EglCreatePbufferSurface(_display, _config,
            new[] { EGL14.EglWidth, 1, EGL14.EglHeight, 1, EGL14.EglNone }, 0);
        if (_pbuffer == null || _pbuffer.Equals(EGL14.EglNoSurface)) throw new InvalidOperationException("Android RmlUi retirement pbuffer creation failed.");
    }
    private void DetachWindow()
    {
        if (_nativeWindow != 0) { ModernGraphicsCompat.DetachAndroidWindow(); ANativeWindow_release(_nativeWindow); _nativeWindow = 0; }
        if (_surface != null)
        {
            MakePbufferCurrent();
            if (!EGL14.EglDestroySurface(_display, _surface)) throw new InvalidOperationException("Android RmlUi EGL surface teardown failed.");
            _surface = null;
        }
        _bound = null; Monitor.PulseAll(_gate);
    }
    private void MakePbufferCurrent()
    {
        if (_display != null && _context != null && _pbuffer != null)
            EGL14.EglMakeCurrent(_display, _pbuffer, _pbuffer, _context);
    }
    private void DestroyContext()
    {
        if (_display == null) return;
        EGL14.EglMakeCurrent(_display, EGL14.EglNoSurface, EGL14.EglNoSurface, EGL14.EglNoContext);
        if (_pbuffer != null) EGL14.EglDestroySurface(_display, _pbuffer);
        if (_context != null) EGL14.EglDestroyContext(_display, _context);
        if (!EGL14.EglTerminate(_display)) throw new InvalidOperationException("Android RmlUi EGL display teardown failed.");
        _display = null; _config = null; _context = null; _pbuffer = null;
    }
    private void RecoverContext()
    {
        // Native core does not own GL objects; resource copies are discarded,
        // and replacement GPU state uploads the live generation's atlases again.
        _gles?.Abandon(); _gles = null;
        _surface = null; _bound = null; DestroyContext();
        _session?.ReleaseInput(); PublishInput(false);
        Console.WriteLine("[rmlui-android] ES3 context lost; rebuilding presentation");
    }
}
#endif
