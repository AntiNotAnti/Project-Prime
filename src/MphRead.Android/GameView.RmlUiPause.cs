#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.Threading;
using System.Threading.Tasks;
using Android.Views;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Droid;

internal sealed record RmlUiAndroidPauseOptions(Task<string> AssetRoot, float Density,
    Action<GameView, RmlUiAndroidPauseSnapshot> Notified);

internal sealed partial class GameView
{
    private RmlUiAndroidPauseRoute? _nativePause => _loop.NativePause;
    private int _nativePointer = -1;
    private RmlUiAndroidInputSnapshot? _nativePointerWitness;
    internal bool TryNativePause() => _nativePause?.RequestToggle() == true;
    internal bool NativePauseCurrent(RmlUiAndroidPauseSnapshot notification) => _nativePause?.IsCurrent(notification) == true;
    internal void RetireNativePauseForUiHandoff() => _nativePause?.RetireForUiHandoff();

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null || !NativeInputScope(out var queue, out int nativeWidth, out int nativeHeight)) return base.OnTouchEvent(e);
        if (queue == null) return true;
        var action = e.ActionMasked;
        if (action == MotionEventActions.Down)
        {
            _nativePointer = e.GetPointerId(0);
            _nativePointerWitness = queue.Current;
        }
        if (action == MotionEventActions.Cancel)
        {
            var witness = _nativePointerWitness ?? queue.Current;
            queue.TryEnqueue(witness, new(witness.Document, RmlUiPlatformInputKind.FocusLost, RmlUiInputDevice.Touch));
            _nativePointer = -1; _nativePointerWitness = null;
            return true;
        }
        int index = _nativePointer < 0 ? -1 : e.FindPointerIndex(_nativePointer);
        if (index < 0 || _nativePointerWitness is not { } captured) return true;
        if (action is MotionEventActions.PointerUp && e.ActionIndex != index) return true;
        RmlUiPlatformInputKind kind;
        if (action == MotionEventActions.Down) kind = RmlUiPlatformInputKind.PointerDown;
        else if (action == MotionEventActions.Move) kind = RmlUiPlatformInputKind.PointerMove;
        else if (action is MotionEventActions.Up or MotionEventActions.PointerUp) kind = RmlUiPlatformInputKind.PointerUp;
        else return true;
        double x = e.GetX(index) * nativeWidth / Math.Max(1.0, Width);
        double y = e.GetY(index) * nativeHeight / Math.Max(1.0, Height);
        queue.TryEnqueue(captured, new(captured.Document, kind, RmlUiInputDevice.Touch, X: x, Y: y, Code: 0));
        if (kind == RmlUiPlatformInputKind.PointerUp) { _nativePointer = -1; _nativePointerWitness = null; }
        return true;
    }

    internal bool TryNativePauseKey(KeyEvent? e)
    {
        if (e == null || !NativeInputScope(out var queue, out _, out _)) return false;
        bool down = e.Action == KeyEventActions.Down;
        if (!down && e.Action != KeyEventActions.Up) return true;
        if (e.KeyCode is Keycode.Back or Keycode.Escape)
        {
            if (down && e.RepeatCount == 0)
            {
                if (_loop.NativeLocalState?.Phase == RmlUiAndroidLocalPhase.Loading) _loop.CancelNativeLoad();
                else if (_nativeLauncher?.OwnsInput == true) _nativeLauncher.RequestBack();
                else _nativePause?.RequestToggle();
            }
            return true;
        }
        int code = e.KeyCode switch
        {
            Keycode.Tab => 1, Keycode.Enter or Keycode.NumpadEnter or Keycode.DpadCenter or Keycode.ButtonA => 2,
            Keycode.Space => 4, Keycode.DpadUp => 5, Keycode.DpadDown => 6,
            Keycode.DpadLeft => 7, Keycode.DpadRight => 8, Keycode.MoveHome => 9, Keycode.MoveEnd => 10,
            Keycode.PageUp => 11, Keycode.PageDown => 12, _ => 0
        };
        if (code != 0 && queue != null)
        {
            var witness = queue.Current;
            var modifiers = (e.IsShiftPressed ? RmlUiInputModifiers.Shift : 0)
                | (e.IsCtrlPressed ? RmlUiInputModifiers.Control : 0)
                | (e.IsAltPressed ? RmlUiInputModifiers.Alt : 0);
            queue.TryEnqueue(witness, new(witness.Document, down ? RmlUiPlatformInputKind.KeyDown : RmlUiPlatformInputKind.KeyUp,
                RmlUiInputDevice.Keyboard, Code: code, Modifiers: modifiers));
        }
        // Native Pause/Journal owns foreground keys; never leak them to chat
        // or gameplay while the page is opening, suspended or drawing.
        return true;
    }

    public override void OnWindowFocusChanged(bool hasWindowFocus)
    {
        base.OnWindowFocusChanged(hasWindowFocus);
        _loop.SetNativeFocused(hasWindowFocus);
        if (!hasWindowFocus) { _nativePointer = -1; _nativePointerWitness = null; }
    }

    private bool NativeInputScope(out RmlUiAndroidInputQueue? queue, out int width, out int height)
    {
        if (_nativeLauncher?.OwnsInput == true)
        {
            queue = _nativeLauncher.Input; var geometry = _nativeLauncher.Geometry;
            width = geometry.Width; height = geometry.Height; return true;
        }
        if (_nativePause?.OwnsInput == true)
        {
            queue = _nativePause.Input; var snapshot = _nativePause.Current;
            width = snapshot.Width; height = snapshot.Height; return true;
        }
        queue = null; width = height = 0;
        return _loop.NativeTransitionOwnsInput;
    }

    private sealed partial class RenderLoop
    {
        private RmlUiAndroidPauseRoute? _nativePause;
        private readonly Func<RmlUiAndroidPauseRoute?> _nativePauseFactory;
        internal RmlUiAndroidPauseRoute? NativePause => Volatile.Read(ref _nativePause);
        private float _nativeDensity = 1;
        internal void SetNativePauseDensity(float density) => Volatile.Write(ref _nativeDensity,
            float.IsFinite(density) && density > 0 ? density : 1);
        private void ProcessNativePause()
        {
            if (Scene != null) _nativePause?.Process(Scene, _size.X, _size.Y, Volatile.Read(ref _nativeDensity));
        }
        private bool DrawNativePause() => Scene != null
            && _nativePause?.Draw(Scene, _size.X, _size.Y, Volatile.Read(ref _nativeDensity)) == true;
    }
}
#endif
