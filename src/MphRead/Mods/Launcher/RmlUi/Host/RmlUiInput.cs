using System;
using System.Buffers;
using System.Text;

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    [Flags]
    public enum RmlUiInputModifiers { None = 0, Shift = 1, Control = 2, Alt = 4, Command = 8 }

    /// <summary>Coordinates reach RmlUi in framebuffer pixels exactly once.</summary>
    public sealed class RmlUiInput
    {
        private readonly RmlUiHost _host;
        private readonly IRmlUiNativeBridge _native;
        private float _scaleX = 1, _scaleY = 1;
        private ulong _sequence;
        private double _windowX, _windowY;
        private int _framebufferX, _framebufferY;
        public bool DiagnosticsEnabled { get; set; }
        public RmlUiInputDiagnostics Diagnostics { get; private set; }

        internal RmlUiInput(RmlUiHost host, IRmlUiNativeBridge native)
        {
            _host = host;
            _native = native;
        }

        public void SetFramebufferScale(float x, float y)
        {
            _host.VerifyNativeCallAllowed();
            _scaleX = Single.IsFinite(x) && x > 0 ? x : 1;
            _scaleY = Single.IsFinite(y) && y > 0 ? y : 1;
        }

        public void PointerMoved(double x, double y, RmlUiInputModifiers modifiers = default)
        {
            _host.VerifyNativeCallAllowed();
            if (!_host.Active) return;
            (int px, int py) = RmlUiPointerMapping.FromWindow(x, y, _scaleX, _scaleY);
            _windowX = x; _windowY = y; _framebufferX = px; _framebufferY = py;
            _native.MouseMove(px, py, (int)modifiers);
        }

        public void PointerButton(int button, double x, double y, bool down,
            RmlUiInputModifiers modifiers = default)
        {
            _host.VerifyNativeCallAllowed();
            if (!_host.Active || button is < 0 or > 2) return;
            PointerMoved(x, y, modifiers);
            _native.MouseButton(button, down ? 1 : 0, (int)modifiers);
        }

        public void PointerWheel(double deltaY, RmlUiInputModifiers modifiers = default)
        {
            _host.VerifyNativeCallAllowed();
            if (_host.Active && Double.IsFinite(deltaY))
                _native.MouseWheel((float)Math.Clamp(deltaY, -1000, 1000), (int)modifiers);
        }

        public void Key(int key, bool down, RmlUiInputModifiers modifiers = default)
        {
            _host.VerifyNativeCallAllowed();
            if (_host.Active && key != 0) _native.Key(key, down ? 1 : 0, (int)modifiers);
        }

        public void Text(string text)
        {
            _host.VerifyNativeCallAllowed();
            if (!_host.Active || String.IsNullOrEmpty(text)) return;
            foreach (Rune rune in text.EnumerateRunes()) _native.Text((uint)rune.Value);
        }

        public RmlUiInputResult Dispatch(in RmlUiPlatformInputEvent input)
        {
            _host.VerifyNativeCallAllowed();
            if (!_host.Active) return Record(input, RmlUiInputResult.Inactive);
            if (!_host.IsAlive(input.Document)) return Record(input, RmlUiInputResult.StaleDocument);
            if (!_host.IsVisible(input.Document)) return Record(input, RmlUiInputResult.HiddenDocument);
            if (((int)input.Modifiers & ~15) != 0) return Record(input, RmlUiInputResult.Invalid);
            bool textEvent = input.Kind is RmlUiPlatformInputKind.TextCommitted
                or RmlUiPlatformInputKind.CompositionBegin or RmlUiPlatformInputKind.CompositionUpdate
                or RmlUiPlatformInputKind.CompositionCommit or RmlUiPlatformInputKind.CompositionCancel;
            RmlUiTextInputState textState = default;
            if (input.Kind is RmlUiPlatformInputKind.KeyDown or RmlUiPlatformInputKind.KeyUp && input.FocusEpoch != 0)
            {
                // Input-method deletion/selection keys are queued alongside
                // text and must retain that same focus lifetime. Physical
                // keyboard keys keep epoch zero and follow current navigation.
                if (!_host.TryGetTextInputState(out var keyScope)
                    || input.Document != keyScope.Document || input.FocusEpoch != keyScope.FocusEpoch)
                    return Record(input, RmlUiInputResult.StaleFocus);
            }
            if (textEvent)
            {
                if (!_host.TryGetTextInputState(out textState)) return Record(input, RmlUiInputResult.Unsupported);
                if (input.FocusEpoch == 0 || input.FocusEpoch != textState.FocusEpoch || input.Document != textState.Document)
                    return Record(input, RmlUiInputResult.StaleFocus);
                if (!ValidText(input.Text, out int textLength) || input.Cursor < -1 || input.SelectionLength < 0
                    || input.Cursor > textLength || input.SelectionLength > textLength
                    || (input.Cursor >= 0 && input.SelectionLength > textLength - input.Cursor))
                    return Record(input, RmlUiInputResult.Invalid);
            }
            switch (input.Kind)
            {
                case RmlUiPlatformInputKind.PointerMove:
                case RmlUiPlatformInputKind.PointerDown:
                case RmlUiPlatformInputKind.PointerUp:
                    if (!Double.IsFinite(input.X) || !Double.IsFinite(input.Y)
                        || input.Code is < 0 or > 2) return Record(input, RmlUiInputResult.Invalid);
                    if (input.Kind == RmlUiPlatformInputKind.PointerMove) PointerMoved(input.X, input.Y, input.Modifiers);
                    else PointerButton(input.Code, input.X, input.Y, input.Kind == RmlUiPlatformInputKind.PointerDown, input.Modifiers);
                    break;
                case RmlUiPlatformInputKind.Wheel:
                    if (!Double.IsFinite(input.Delta)) return Record(input, RmlUiInputResult.Invalid);
                    PointerWheel(input.Delta, input.Modifiers);
                    break;
                case RmlUiPlatformInputKind.KeyDown:
                case RmlUiPlatformInputKind.KeyUp:
                    if (input.Code is not (>= 1 and <= 21 or >= 32 and <= 57)) return Record(input, RmlUiInputResult.Invalid);
                    Key(input.Code, input.Kind == RmlUiPlatformInputKind.KeyDown, input.Modifiers);
                    break;
                case RmlUiPlatformInputKind.TextCommitted:
                    // A platform with a preedit adapter commits through the
                    // composition ABI. Its later character callback must not
                    // append the same composition for a second time.
                    if (textState.Composing) return Record(input, RmlUiInputResult.Invalid);
                    Text(input.Text);
                    break;
                case RmlUiPlatformInputKind.FocusLost:
                    _host.ReleaseInput();
                    break;
                case RmlUiPlatformInputKind.CompositionBegin:
                case RmlUiPlatformInputKind.CompositionUpdate:
                case RmlUiPlatformInputKind.CompositionCommit:
                case RmlUiPlatformInputKind.CompositionCancel:
                    if ((textState.Capabilities & RmlUiTextInputCapabilities.Composition) == 0)
                        return Record(input, RmlUiInputResult.Unsupported);
                    int stage = (int)input.Kind - (int)RmlUiPlatformInputKind.CompositionBegin;
                    if (!_host.Compose(input, stage)) return Record(input, RmlUiInputResult.StaleFocus);
                    break;
                default: return Record(input, RmlUiInputResult.Invalid);
            }
            return Record(input, RmlUiInputResult.Accepted);
        }

        internal void RecordIntent(RmlUiIntent intent)
        {
            if (DiagnosticsEnabled) Diagnostics = Diagnostics with { Intent = intent.Kind, IntentArgument = intent.Argument };
        }

        private RmlUiInputResult Record(in RmlUiPlatformInputEvent input, RmlUiInputResult result)
        {
            if (DiagnosticsEnabled)
            {
                string hovered = _host.Active ? SafeElementId(_host.HoveredElement()) : "";
                string focused = _host.Active ? SafeElementId(_host.FocusedElement()) : "";
                Diagnostics = new(++_sequence, input.Device, input.Kind, result,
                    _windowX, _windowY, _framebufferX, _framebufferY, hovered, focused, null, 0);
            }
            return result;
        }

        private static string SafeElementId(string value)
        {
            if (value.Length > 64) return "";
            foreach (char character in value)
                if (!Char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-')) return "";
            return value;
        }

        private static bool ValidText(string text, out int count)
        {
            count = 0;
            if (text == null || text.Length > 65536 || text.Contains('\0')) return false;
            ReadOnlySpan<char> remaining = text.AsSpan();
            while (!remaining.IsEmpty)
            {
                if (Rune.DecodeFromUtf16(remaining, out _, out int consumed) != OperationStatus.Done) return false;
                remaining = remaining[consumed..];
                count++;
            }
            return true;
        }

        internal void Reset()
        {
            _scaleX = _scaleY = 1;
            Diagnostics = default;
            _sequence = 0;
        }
    }
}
