#if MPHREAD_RMLUI_ANDROID
using System;
using System.Text;
using Android.Content;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using ICharSequence = Java.Lang.ICharSequence;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Droid;

internal sealed record AndroidRmlUiInputSnapshot(RmlUiDocumentToken Document,
    RmlUiTextInputState? TextState = null, string Value = "", bool Password = false,
    RmlUiTextInputBounds? HudCanvas = null)
{
    internal static readonly AndroidRmlUiInputSnapshot Empty = new(default(RmlUiDocumentToken));
    public override string ToString() => $"Android RmlUi document {Document.DocumentId}, text focus {TextState?.FocusEpoch}";
}

internal readonly record struct AndroidRmlUiTouchEvent(RmlUiDocumentToken Document, int PointerId,
    RmlUiPlatformInputKind Kind, float X, float Y);

internal sealed class AndroidRmlUiInput
{
    private readonly View _view;
    private readonly Func<AndroidRmlUiInputSnapshot> _snapshot;
    private readonly Action<RmlUiPlatformInputEvent> _send;
    private readonly Action _back;
    private readonly Action<AndroidRmlUiTouchEvent>? _touch;
    private bool _hudGesture;
    private int _primary = -1;
    private RmlUiDocumentToken _primaryDocument;
    private double _x, _y;
    private double _startX, _startY;
    private bool _scrolling;
    private ulong _keyboardEpoch;
    internal AndroidRmlUiInput(View view, Func<AndroidRmlUiInputSnapshot> snapshot,
        Action<RmlUiPlatformInputEvent> send, Action back, Action<AndroidRmlUiTouchEvent>? touch = null)
        => (_view, _snapshot, _send, _back, _touch) = (view, snapshot, send, back, touch);

    internal bool Touch(MotionEvent e)
    {
        var snapshot = _snapshot();
        if (snapshot.Document == default) return false;
        int index = e.ActionIndex;
        if (_hudGesture && snapshot.Document != _primaryDocument) { Cancel(); return true; }
        if (e.ActionMasked == MotionEventActions.Down && _touch != null && snapshot.HudCanvas is { } canvas
            && e.GetX(index) >= canvas.X && e.GetY(index) >= canvas.Y
            && e.GetX(index) < canvas.X + canvas.Width && e.GetY(index) < canvas.Y + canvas.Height)
        { _hudGesture = true; _primaryDocument = snapshot.Document; }
        if (_hudGesture)
        {
            void Send(int pointer, RmlUiPlatformInputKind kind) => _touch!(new(_primaryDocument,
                e.GetPointerId(pointer), kind, e.GetX(pointer), e.GetY(pointer)));
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down: case MotionEventActions.PointerDown: Send(index, RmlUiPlatformInputKind.PointerDown); break;
                case MotionEventActions.Move:
                    for (int pointer = 0; pointer < e.PointerCount; pointer++) Send(pointer, RmlUiPlatformInputKind.PointerMove);
                    break;
                case MotionEventActions.PointerUp: Send(index, RmlUiPlatformInputKind.PointerUp); break;
                case MotionEventActions.Up: Send(index, RmlUiPlatformInputKind.PointerUp); _hudGesture = false; break;
                case MotionEventActions.Cancel: Cancel(); break;
            }
            return true;
        }
        if (e.ActionMasked == MotionEventActions.Down)
        {
            _primary = e.GetPointerId(index); _primaryDocument = snapshot.Document; _x = _startX = e.GetX(index); _y = _startY = e.GetY(index); _scrolling = false;
            _send(new(snapshot.Document, RmlUiPlatformInputKind.PointerDown, RmlUiInputDevice.Touch, X: _x, Y: _y));
        }
        else if (e.ActionMasked == MotionEventActions.Move && _primary >= 0)
        {
            index = e.FindPointerIndex(_primary);
            if (index < 0) { Cancel(); return true; }
            double nextX = e.GetX(index), nextY = e.GetY(index), dy = nextY - _y;
            float density = _view.Resources?.DisplayMetrics?.Density ?? 1;
            if (!_scrolling && snapshot.TextState == null && Math.Abs(nextY - _startY) > 8 * density
                && Math.Abs(nextY - _startY) > Math.Abs(nextX - _startX))
            {
                _scrolling = true;
                // Retire the pressed button before beginning a scroll so its
                // eventual release cannot activate a row crossed by a swipe.
                _send(new(_primaryDocument, RmlUiPlatformInputKind.FocusLost, RmlUiInputDevice.Touch));
            }
            _x = nextX; _y = nextY;
            _send(new(_primaryDocument, RmlUiPlatformInputKind.PointerMove, RmlUiInputDevice.Touch, X: _x, Y: _y));
            if (_scrolling && dy != 0)
                _send(new(_primaryDocument, RmlUiPlatformInputKind.Wheel, RmlUiInputDevice.Touch, Delta: dy / (48 * density)));
        }
        else if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.PointerUp && e.GetPointerId(index) == _primary)
        {
            _x = e.GetX(index); _y = e.GetY(index); _primary = -1;
            if (!_scrolling) _send(new(_primaryDocument, RmlUiPlatformInputKind.PointerUp, RmlUiInputDevice.Touch, X: _x, Y: _y));
            _scrolling = false;
        }
        else if (e.ActionMasked == MotionEventActions.Cancel) Cancel();
        // Secondary pointers never become the primary during a gesture.
        return true;
    }
    internal void Cancel()
    {
        if (_hudGesture) _touch?.Invoke(new(_primaryDocument, -1, RmlUiPlatformInputKind.FocusLost, 0, 0));
        _hudGesture = false;
        _primary = -1; _primaryDocument = default; _scrolling = false;
        var snapshot = _snapshot();
        if (snapshot.Document != default) _send(new(snapshot.Document, RmlUiPlatformInputKind.FocusLost, RmlUiInputDevice.Touch));
        Keyboard(false);
    }
    internal bool Generic(MotionEvent e)
    {
        var snapshot = _snapshot(); if (snapshot.Document == default) return false;
        var device = e.GetToolType(0) == MotionEventToolType.Stylus ? RmlUiInputDevice.Stylus : RmlUiInputDevice.Pointer;
        if (e.ActionMasked is MotionEventActions.HoverMove or MotionEventActions.HoverEnter)
            _send(new(snapshot.Document, RmlUiPlatformInputKind.PointerMove, device, X: e.GetX(), Y: e.GetY()));
        else if (e.ActionMasked == MotionEventActions.Scroll)
            _send(new(snapshot.Document, RmlUiPlatformInputKind.Wheel, device, Delta: e.GetAxisValue(Axis.Vscroll)));
        else return false;
        return true;
    }
    internal bool Key(Keycode code, KeyEvent? e, bool down)
    {
        var snapshot = _snapshot(); if (snapshot.Document == default) return false;
        if (code is Keycode.Back or Keycode.Escape) { if (down && e?.RepeatCount == 0) _back(); return true; }
        var modifiers = Modifiers(e);
        int key = Map(code);
        if (key != 0 && snapshot.TextState?.Composing != true)
            _send(new(snapshot.Document, down ? RmlUiPlatformInputKind.KeyDown : RmlUiPlatformInputKind.KeyUp,
                RmlUiInputDevice.Keyboard, Code: key, Modifiers: modifiers));
        if (down && snapshot.TextState is { } focus && !focus.Composing
            && (modifiers & (RmlUiInputModifiers.Control | RmlUiInputModifiers.Alt | RmlUiInputModifiers.Command)) == 0)
        {
            int unicode = e?.GetUnicodeChar(e.MetaState) ?? 0;
            if (unicode >= 32 && Rune.IsValid(unicode))
                _send(new(snapshot.Document, RmlUiPlatformInputKind.TextCommitted, RmlUiInputDevice.Keyboard,
                    focus.FocusEpoch, Text: new Rune(unicode).ToString()));
        }
        return true;
    }
    internal void PublishKeyboard()
    {
        var snapshot = _snapshot(); ulong epoch = snapshot.TextState?.FocusEpoch ?? 0;
        if (epoch == _keyboardEpoch) return;
        _keyboardEpoch = epoch;
        Keyboard(epoch != 0);
    }
    internal void PublishSelection()
    {
        var snapshot = _snapshot();
        if (snapshot.TextState is not { } focus || _view.Context?.GetSystemService(Context.InputMethodService) is not InputMethodManager ime) return;
        ime.UpdateSelection(_view, Utf16(snapshot.Value, focus.SelectionStart), Utf16(snapshot.Value, focus.SelectionEnd), -1, -1);
    }
    private void Keyboard(bool show)
    {
        if (_view.Context?.GetSystemService(Context.InputMethodService) is not InputMethodManager ime) return;
        if (show) { _view.RequestFocus(); ime.RestartInput(_view); ime.ShowSoftInput(_view, ShowFlags.Implicit); }
        else { _keyboardEpoch = 0; ime.HideSoftInputFromWindow(_view.WindowToken, HideSoftInputFlags.None); }
    }
    internal IInputConnection? Connection(EditorInfo? info)
    {
        var snapshot = _snapshot(); if (snapshot.TextState is not { } focus) return null;
        if (info != null)
        {
            info.InputType = InputTypes.ClassText | (snapshot.Password ? InputTypes.TextVariationPassword : InputTypes.TextFlagCapSentences);
            info.ImeOptions = (ImeFlags)((int)ImeAction.Done | (int)ImeFlags.NoFullscreen | (int)ImeFlags.NoExtractUi);
            info.InitialSelStart = Utf16(snapshot.Value, focus.SelectionStart);
            info.InitialSelEnd = Utf16(snapshot.Value, focus.SelectionEnd);
        }
        return new ConnectionImpl(_view, snapshot, _snapshot, _send);
    }
    private static int Map(Keycode code) => code switch
    {
        Keycode.Tab => 1, Keycode.Enter or Keycode.NumpadEnter or Keycode.ButtonA or Keycode.DpadCenter => 2,
        Keycode.Space => 4, Keycode.DpadUp => 5, Keycode.DpadDown => 6, Keycode.DpadLeft => 7, Keycode.DpadRight => 8,
        Keycode.MoveHome => 9, Keycode.MoveEnd => 10, Keycode.PageUp => 11, Keycode.PageDown => 12,
        Keycode.Del => 13, Keycode.ForwardDel => 14,
        >= Keycode.A and <= Keycode.Z => 32 + (int)code - (int)Keycode.A, _ => 0
    };
    private static RmlUiInputModifiers Modifiers(KeyEvent? e)
    {
        RmlUiInputModifiers result = default;
        if (e?.IsShiftPressed == true) result |= RmlUiInputModifiers.Shift;
        if (e?.IsCtrlPressed == true) result |= RmlUiInputModifiers.Control;
        if (e?.IsAltPressed == true) result |= RmlUiInputModifiers.Alt;
        if (e?.IsMetaPressed == true) result |= RmlUiInputModifiers.Command;
        return result;
    }
    internal static int Scalars(string text) { int count = 0; foreach (var rune in text.EnumerateRunes()) count++; return count; }
    private static int Utf16(string value, int scalar)
    {
        int result = 0; foreach (Rune rune in value.EnumerateRunes()) { if (scalar-- <= 0) break; result += rune.Utf16SequenceLength; } return result;
    }
    private sealed class ConnectionImpl : BaseInputConnection
    {
        private readonly AndroidRmlUiInputSnapshot _target;
        private readonly Func<AndroidRmlUiInputSnapshot> _snapshot;
        private readonly Action<RmlUiPlatformInputEvent> _send;
        private string _preedit = "";
        private bool _composing, _closed;
        internal ConnectionImpl(View view, AndroidRmlUiInputSnapshot target, Func<AndroidRmlUiInputSnapshot> snapshot,
            Action<RmlUiPlatformInputEvent> send) : base(view, fullEditor: false)
            => (_target, _snapshot, _send) = (target, snapshot, send);
        private bool Alive => !_closed && _snapshot().TextState is { } current && _target.TextState is { } target
            && current.Document == target.Document && current.FocusEpoch == target.FocusEpoch;
        private bool Composition(RmlUiPlatformInputKind kind, string text = "", int cursor = -1)
        {
            if (!Alive) return false;
            _send(new(_target.Document, kind, RmlUiInputDevice.InputMethod, _target.TextState!.Value.FocusEpoch,
                Text: text, Cursor: cursor)); return true;
        }
        public override bool SetComposingText(ICharSequence? text, int newCursorPosition)
        {
            if (!Alive) return false;
            if (!_composing && !Composition(RmlUiPlatformInputKind.CompositionBegin)) return false;
            _composing = true; _preedit = text?.ToString() ?? "";
            int length = Scalars(_preedit);
            return Composition(RmlUiPlatformInputKind.CompositionUpdate, _preedit,
                Math.Clamp(newCursorPosition > 0 ? length + newCursorPosition - 1 : newCursorPosition, 0, length));
        }
        public override bool CommitText(ICharSequence? text, int newCursorPosition)
        {
            if (!Alive) return false;
            if (!_composing && !Composition(RmlUiPlatformInputKind.CompositionBegin)) return false;
            _composing = false; _preedit = "";
            string value = text?.ToString() ?? "";
            return Composition(RmlUiPlatformInputKind.CompositionCommit, value, Scalars(value));
        }
        public override bool FinishComposingText()
        {
            if (!_composing) return Alive;
            _composing = false; string value = _preedit; _preedit = "";
            return Composition(RmlUiPlatformInputKind.CompositionCommit, value, Scalars(value));
        }
        public override bool DeleteSurroundingText(int beforeLength, int afterLength)
            => DeleteSurrounding(beforeLength, afterLength, codePoints: false);
        public override bool DeleteSurroundingTextInCodePoints(int beforeLength, int afterLength)
            => DeleteSurrounding(beforeLength, afterLength, codePoints: true);
        private bool DeleteSurrounding(int beforeLength, int afterLength, bool codePoints)
        {
            if (!Alive || beforeLength < 0 || afterLength < 0 || beforeLength + (long)afterLength > 4096) return false;
            if (_composing) { Composition(RmlUiPlatformInputKind.CompositionCancel); _composing = false; _preedit = ""; }
            var s = _snapshot(); int cursor = Utf16(s.Value, s.TextState!.Value.SelectionStart);
            int before = codePoints ? Math.Min(beforeLength, Scalars(s.Value.Substring(0, cursor)))
                : Scalars(s.Value.Substring(Math.Max(0, cursor - beforeLength), Math.Min(beforeLength, cursor)));
            int after = codePoints ? Math.Min(afterLength, Scalars(s.Value.Substring(cursor)))
                : Scalars(s.Value.Substring(cursor, Math.Min(afterLength, s.Value.Length - cursor)));
            for (int i = 0; i < before; i++) SendKey(13);
            for (int i = 0; i < after; i++) SendKey(14);
            return true;
        }
        private void SendKey(int key)
        {
            _send(new(_target.Document, RmlUiPlatformInputKind.KeyDown, RmlUiInputDevice.InputMethod,
                _target.TextState!.Value.FocusEpoch, Code: key));
            _send(new(_target.Document, RmlUiPlatformInputKind.KeyUp, RmlUiInputDevice.InputMethod,
                _target.TextState!.Value.FocusEpoch, Code: key));
        }
        public override ICharSequence? GetTextBeforeCursorFormatted(int length, GetTextFlags flags)
        {
            var s = _snapshot(); if (!Alive || s.Password) return new Java.Lang.String("");
            int end = Utf16(s.Value, s.TextState!.Value.SelectionStart);
            return new Java.Lang.String(s.Value.Substring(Math.Max(0, end - Math.Max(0, length)), Math.Min(Math.Max(0, length), end)));
        }
        public override ICharSequence? GetTextAfterCursorFormatted(int length, GetTextFlags flags)
        {
            var s = _snapshot(); if (!Alive || s.Password) return new Java.Lang.String("");
            int start = Utf16(s.Value, s.TextState!.Value.SelectionEnd);
            return new Java.Lang.String(s.Value.Substring(start, Math.Min(Math.Max(0, length), s.Value.Length - start)));
        }
        public override bool PerformEditorAction(ImeAction action)
        { if (!FinishComposingText()) return false; SendKey(2); return true; }
        public override bool SendKeyEvent(KeyEvent? e)
        {
            if (!Alive || e == null) return false;
            int key = Map(e.KeyCode); if (key == 0) return false;
            _send(new(_target.Document, e.Action == KeyEventActions.Down ? RmlUiPlatformInputKind.KeyDown : RmlUiPlatformInputKind.KeyUp,
                RmlUiInputDevice.InputMethod, _target.TextState!.Value.FocusEpoch, Code: key, Modifiers: Modifiers(e))); return true;
        }
        public override void CloseConnection()
        { if (_composing) Composition(RmlUiPlatformInputKind.CompositionCancel); _closed = true; base.CloseConnection(); }
    }
}
#endif
