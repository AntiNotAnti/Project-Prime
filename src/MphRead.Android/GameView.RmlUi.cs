using System;
#if MPHREAD_RMLUI_ANDROID
using System.Collections.Concurrent;
using System.Threading;
using Android.Content;
using Android.Views;
using Android.Views.InputMethods;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Launcher.RmlUi.Components;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Training;
using AccessibilityNodeProvider = Android.Views.Accessibility.AccessibilityNodeProvider;
#endif

namespace MphRead.Droid;

internal sealed partial class GameView
{
#if MPHREAD_RMLUI_ANDROID
    private AndroidRmlUiInput? _nativeInput;
    private AndroidRmlUiAccessibility? _nativeAccessibilityProvider;
    private AndroidRmlUiInputSnapshot _nativeSnapshot = AndroidRmlUiInputSnapshot.Empty;
    internal bool NativeUiVisible => _loop.NativeUiVisible;
    internal void ConfigureNativeResults(Action<AimTrainerSession, AimResultsAction> action) => _loop.ConfigureNativeResults(action);
    private void InitializeNativeInput(Context context)
    {
        _nativeInput = new(this, () => Volatile.Read(ref _nativeSnapshot),
            SendNativeInput, () => _loop.EnqueueNative(s => s.Back()),
            touch => _loop.EnqueueNative(s => s.DispatchTouch(touch)));
        _nativeAccessibilityProvider = new(this, _loop.EnqueueNativeAccessibility);
        _loop.ConfigureNativeUi(context, snapshot =>
        {
            var previous = Interlocked.Exchange(ref _nativeSnapshot, snapshot);
            if (previous == snapshot) return;
            ((Android.App.Activity)context).RunOnUiThread(() =>
            {
                if (previous.TextState?.FocusEpoch != snapshot.TextState?.FocusEpoch) _nativeInput.PublishKeyboard();
                else if (previous.TextState != snapshot.TextState || previous.Value != snapshot.Value) _nativeInput.PublishSelection();
            });
        }, snapshot => ((Android.App.Activity)context).RunOnUiThread(() => _nativeAccessibilityProvider.Publish(snapshot)));
    }
    public override AccessibilityNodeProvider? AccessibilityNodeProvider => _nativeAccessibilityProvider;
    protected override bool DispatchHoverEvent(MotionEvent? e) => e != null && NativeUiVisible
        && _nativeAccessibilityProvider?.Hover(e) == true || base.DispatchHoverEvent(e);
    internal bool NativeHover(MotionEvent e) => NativeUiVisible && _nativeAccessibilityProvider?.Hover(e) == true;
    internal bool NativeTouch(MotionEvent e) => NativeUiVisible && _nativeInput?.Touch(e) == true;
    private void SendNativeInput(RmlUiPlatformInputEvent input)
    {
        string? paste = AndroidRmlUiClipboard.ReadForPaste(Context, input);
        _loop.EnqueueNative(session =>
        {
            if (input.Document != session.Host.CurrentInputDocument) return;
            if (paste != null) session.Host.SetClipboard(paste);
            session.Dispatch(input);
            if (AndroidRmlUiClipboard.ReadAfterCopy(session.Host, input) is { } text)
                ((Android.App.Activity)Context!).RunOnUiThread(() => AndroidRmlUiClipboard.Write(Context, text));
        });
    }
    internal void OpenNativeMenu(Action resume, Action settings, Action replay, Action leave, Action quit)
    {
        _nativeInput?.Cancel(); _loop.RequestNativeMenu(true, resume, settings, replay, leave, quit); RequestFocus();
    }
    internal void CloseNativeMenu() { _nativeInput?.Cancel(); _loop.RequestNativeMenu(false); }
    internal void OpenNativeReplay(Action back)
    { _nativeInput?.Cancel(); _loop.RequestNativeReplay(true, back); RequestFocus(); }
    internal void CloseNativeReplay() { _nativeInput?.Cancel(); _loop.RequestNativeReplay(false); }
    internal void BackNativeMenu() => _loop.EnqueueNative(s => s.Back());
    private bool NativeKey(Keycode code, KeyEvent? e, bool down) => NativeUiVisible && _nativeInput?.Key(code, e, down) == true;
    private bool NativeGeneric(MotionEvent? e) => e != null && NativeUiVisible && _nativeInput?.Generic(e) == true;
    private IInputConnection? NativeConnection(EditorInfo? info) => NativeUiVisible ? _nativeInput?.Connection(info) : null;
    private bool NativeTextEditor => NativeUiVisible && Volatile.Read(ref _nativeSnapshot).TextState.HasValue;
#else
    internal bool NativeUiVisible => false;
    private void InitializeNativeInput(Android.Content.Context context) { }
    private bool NativeKey(Android.Views.Keycode code, Android.Views.KeyEvent? e, bool down) => false;
    private bool NativeGeneric(Android.Views.MotionEvent? e) => false;
    private Android.Views.InputMethods.IInputConnection? NativeConnection(Android.Views.InputMethods.EditorInfo? info) => null;
    private bool NativeTextEditor => false;
#endif

    private sealed partial class RenderLoop
    {
#if MPHREAD_RMLUI_ANDROID
        private readonly ConcurrentQueue<Action<AndroidRmlUiMatchSession>> _nativeCommands = new();
        private Context? _nativeContext;
        private Action<AndroidRmlUiInputSnapshot>? _nativePublish;
        private Action<RmlUiAccessibilitySnapshot>? _nativePublishAccessibility;
        private readonly RmlUiAccessibilityService _nativeAccessibility = new();
        private AndroidRmlUiMatchSession? _nativeSession;
        private AndroidRmlUiGlesRenderer? _nativeGles;
        private bool _nativeRequested, _nativeReplayRequested, _nativeFocusLost, _nativeVisible;
        private Action? _nativeReplayBack;
        private Action<AimTrainerSession, AimResultsAction>? _nativeTrainingAction;
        private Action? _nativeResume, _nativeSettings, _nativeReplay, _nativeLeave, _nativeQuit;
        internal bool NativeUiVisible => Volatile.Read(ref _nativeRequested) || Volatile.Read(ref _nativeReplayRequested) || Volatile.Read(ref _nativeVisible);
        internal void ConfigureNativeResults(Action<AimTrainerSession, AimResultsAction> action)
        { lock (_lock) _nativeTrainingAction = action; }
        internal void ConfigureNativeUi(Context context, Action<AndroidRmlUiInputSnapshot> publish,
            Action<RmlUiAccessibilitySnapshot> publishAccessibility)
        { lock (_lock) { _nativeContext = context; _nativePublish = publish; _nativePublishAccessibility = publishAccessibility; } }
        internal bool EnqueueNativeAccessibility(RmlUiAccessibilityCommand command)
        {
            if (!NativeUiVisible || !_nativeAccessibility.Enqueue(command)) return false;
            EnqueueNative(s => _nativeAccessibility.Drain(s.Host)); return true;
        }
        internal void RequestNativeMenu(bool open, Action? resume = null, Action? settings = null,
            Action? replay = null, Action? leave = null, Action? quit = null)
        {
            lock (_lock)
            {
                _nativeRequested = open; _nativeFocusLost = !open;
                if (open) _nativeReplayRequested = false;
                if (open) (_nativeResume, _nativeSettings, _nativeReplay, _nativeLeave, _nativeQuit) = (resume, settings, replay, leave, quit);
                Monitor.PulseAll(_lock);
            }
        }
        internal void RequestNativeReplay(bool open, Action? back = null)
        {
            lock (_lock)
            {
                _nativeReplayRequested = open; _nativeFocusLost = !open;
                if (open) { _nativeRequested = false; _nativeReplayBack = back; }
                Monitor.PulseAll(_lock);
            }
        }
        internal void EnqueueNative(Action<AndroidRmlUiMatchSession> command)
        { if (NativeUiVisible) _nativeCommands.Enqueue(command); }
        private void UpdateNativeUi()
        {
            bool open, replay, lost; Context? context;
            lock (_lock) { open = _nativeRequested; replay = _nativeReplayRequested; lost = _nativeFocusLost; _nativeFocusLost = false; context = _nativeContext; }
            if (lost) { _nativeSession?.ReleaseInput(); PublishNativeInput(false); }
            if (context == null || Scene == null) return;
            if (_nativeSession == null && !open && !replay && Scene.AimTrainer?.Completed != true && !Mods.EndScreen.PanelAvailable) return;
            void Ui(Action? action) { if (action != null) ((Android.App.Activity)context).RunOnUiThread(action); }
            _nativeSession ??= new(AndroidRmlUiAssets.Extract(context), _size.X, _size.Y,
                context.Resources?.DisplayMetrics?.Density ?? 1, Scene.GameState, Scene.Players,
                () => { RequestNativeMenu(false); Ui(_nativeResume); }, () => Ui(_nativeSettings),
                () => { RequestNativeMenu(false); Ui(_nativeReplay); }, () => Ui(_nativeLeave), () => Ui(_nativeQuit),
                _onPauseMenu, (training, action) => Ui(() => _nativeTrainingAction?.Invoke(training, action)),
                () => Ui(_nativeReplayBack), () => RequestNativeReplay(false));
            _nativeSession.Host.Resize(_size.X, _size.Y, context.Resources?.DisplayMetrics?.Density ?? 1);
            for (int i = 0; i < 256 && _nativeCommands.TryDequeue(out var command); i++) command(_nativeSession);
            _nativeSession.Update(Scene, open, replay);
            Volatile.Write(ref _nativeVisible, _nativeSession.Visible);
            PublishNativeInput(_nativeSession.Visible);
        }
        private void PublishNativeInput(bool active)
        {
            var snapshot = AndroidRmlUiInputSnapshot.Empty;
            if (active && _nativeSession != null)
            {
                var host = _nativeSession.Host;
                if (host.TryGetTextInputState(out var state))
                {
                    string id = host.FocusedElement();
                    snapshot = new(host.CurrentInputDocument, state, host.ReadField(state.Document, id),
                        id.Contains("password", StringComparison.OrdinalIgnoreCase), _nativeSession.CaptureHudCanvas());
                }
                else snapshot = new(host.CurrentInputDocument, HudCanvas: _nativeSession.CaptureHudCanvas());
            }
            _nativePublish?.Invoke(snapshot);
            var previous = _nativeAccessibility.Snapshot;
            var accessibility = previous;
            if (active && _nativeSession != null)
            {
                accessibility = _nativeAccessibility.Capture(_nativeSession.Host);
            }
            else { _nativeAccessibility.Retire(); accessibility = _nativeAccessibility.Snapshot; }
            if (accessibility.Document != previous.Document || accessibility.Revision != previous.Revision)
                _nativePublishAccessibility?.Invoke(accessibility);
        }
        private bool DrawNativeUi()
        {
            if (_nativeSession?.Visible != true) return false;
            _nativeSession.Host.Render(_size.X, _size.Y);
            if (_modern) RmlUiGpuCompositor.DrawNativeFrame(_size.X, _size.Y);
            else (_nativeGles ??= new()).Draw(_size.X, _size.Y);
            _nativeSession.Presented(true);
            return true;
        }
        private void ReleaseNativeUi(bool nativeAvailable)
        {
            try { _nativeSession?.Dispose(); }
            finally
            {
                _nativeSession = null;
                Volatile.Write(ref _nativeVisible, false);
                if (_modern) RmlUiGpuCompositor.ReleaseNativeFrame();
                else if (nativeAvailable) _nativeGles?.Dispose(); else _nativeGles?.Abandon();
                _nativeGles = null; PublishNativeInput(false);
            }
        }
        private void LoseNativeFocus() { lock (_lock) _nativeFocusLost = true; }
#else
        private void UpdateNativeUi() { }
        private bool DrawNativeUi() => false;
        private void ReleaseNativeUi(bool nativeAvailable) { }
        private void LoseNativeFocus() { }
#endif
    }
}
