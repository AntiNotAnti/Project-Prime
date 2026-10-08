using System;
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

        internal void Reset() => _scaleX = _scaleY = 1;
    }
}
