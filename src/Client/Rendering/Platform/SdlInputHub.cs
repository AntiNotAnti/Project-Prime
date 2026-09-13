using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SDL;

namespace MphRead;

/// <summary>
/// Owns SDL's translated keyboard/mouse/text state.  It stores only neutral
/// compatibility values; SDL event polling remains exclusively on the host
/// thread and snapshots are consumed synchronously by that thread.
/// </summary>
internal sealed class SdlInputHub
{
    private readonly HashSet<int> _keys = new();
    private readonly HashSet<int> _mouseButtons = new();
    private readonly List<WindowKeyEvent> _keyEvents = new();
    private readonly List<WindowMouseButtonEvent> _mouseButtonEvents = new();
    private readonly StringBuilder _text = new();
    private Vector2 _mousePosition;
    private Vector2 _relativeMouse;
    private Vector2 _wheel;

    internal Vector2 RelativeMouse => _relativeMouse;
    internal Vector2 Wheel => _wheel;
    internal Vector2 MousePosition => _mousePosition;

    internal void BeginFrame()
    {
        _keyEvents.Clear();
        _mouseButtonEvents.Clear();
        _text.Clear();
        _relativeMouse = Vector2.Zero;
        _wheel = Vector2.Zero;
    }

    internal void ClearHeld()
    {
        _keys.Clear();
        _mouseButtons.Clear();
        _relativeMouse = Vector2.Zero;
        _wheel = Vector2.Zero;
    }

    internal WindowInputSnapshot Snapshot(bool focused, bool frameAdvanceMode)
        => new(_keys, _mouseButtons, _mousePosition, _relativeMouse, _wheel,
            _text.ToString(), focused, _keyEvents, _mouseButtonEvents,
            frameAdvanceMode);

    internal void HandleKey(SDL_KeyboardEvent evt)
    {
        Keys key = SdlGameHost.TranslateKey(evt.scancode);
        if (key == Keys.Unknown) return;
        bool down = evt.down;
        if (down) _keys.Add((int)key);
        else _keys.Remove((int)key);
        _keyEvents.Add(new WindowKeyEvent(key, down, evt.repeat,
            SdlGameHost.TranslateModifiers(evt.mod)));
    }

    internal void HandleMouseMotion(SDL_MouseMotionEvent evt)
    {
        _mousePosition = new Vector2(evt.x, evt.y);
        _relativeMouse += new Vector2(evt.xrel, evt.yrel);
    }

    internal void HandleMouseButton(SDL_MouseButtonEvent evt)
    {
        MouseButton button = SdlGameHost.TranslateMouseButton(evt.button);
        if (button == MouseButton.Last) return;
        bool down = evt.down;
        if (down) _mouseButtons.Add((int)button);
        else _mouseButtons.Remove((int)button);
        _mouseButtonEvents.Add(new WindowMouseButtonEvent(button, down));
    }

    internal void AddWheel(float x, float y)
        => _wheel += new Vector2(x, y);

    internal void AppendText(IntPtr text)
    {
        string? value = Marshal.PtrToStringUTF8(text);
        if (!string.IsNullOrEmpty(value)) _text.Append(value);
    }
}
