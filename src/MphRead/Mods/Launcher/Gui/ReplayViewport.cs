#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.Mods.Launcher.Gui;

internal sealed class ReplayViewport : Border
{
    private readonly HashSet<Key> _held = new();
    private Point? _drag;
    private float _yaw, _pitch;
    private static ReplayViewport? _focused;
    public ReplayViewport()
    {
        Focusable = true;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(1);
        BorderBrush = Brushes.Gray;
        ToolTip.SetTip(this, "Click to focus. WASD move, E/V up/down, Shift faster. Drag to look. "
            + "F free camera, B add keyframe, N next keyframe. Replay camera/timing shortcuts use your Replay Keyboard bindings.");
        LostFocus += (_, _) => Release();
        DetachedFromVisualTree += (_, _) => Release();
    }
    private void Release()
    {
        _held.Clear(); _drag = null; _yaw = _pitch = 0;
        if (_focused == this) _focused = null;
        BorderBrush = Brushes.Gray;
    }
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e); _focused = this; BorderBrush = Brushes.Cyan;
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus(); _focused = this; BorderBrush = Brushes.Cyan;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ReplayCamera.SetMode(ReplayCameraMode.Free);
            ReplayCamera.Director = false;
            _drag = e.GetPosition(this); e.Pointer.Capture(this);
        }
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag is not { } previous) return;
        Point next = e.GetPosition(this);
        _yaw += (float)(next.X - previous.X) * .003f;
        _pitch -= (float)(next.Y - previous.Y) * .003f;
        _drag = next; e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { _drag = null; e.Pointer.Capture(null); e.Handled = true; }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { _drag = null; base.OnPointerCaptureLost(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Release(); e.Handled = true; return; }
        bool first = _held.Add(e.Key);
        string name = e.Key switch
        {
            Key.OemOpenBrackets => "LeftBracket", Key.OemCloseBrackets => "RightBracket",
            Key.OemSemicolon => "Semicolon", Key.OemQuotes => "Apostrophe",
            Key.OemComma => "Comma", Key.OemPeriod => "Period",
            Key.OemPlus => "Equal", Key.OemMinus => "Minus",
            _ => e.Key.ToString()
        };
        if (first && Enum.TryParse<OpenTK.Windowing.GraphicsLibraryFramework.Keys>(name, out var key))
            ReplayInput.HandleKey(key, editor: true);
        e.Handled = true;
    }
    protected override void OnKeyUp(KeyEventArgs e) { _held.Remove(e.Key); e.Handled = true; }
    internal static void Poll(Scene scene)
    {
        var view = _focused;
        if (view == null || !view.IsEffectivelyVisible || !view.IsFocused || ReplayVideoExporter.Active) return;
        float Axis(Key positive, Key negative) => (view._held.Contains(positive) ? 1 : 0) - (view._held.Contains(negative) ? 1 : 0);
        float speed = view._held.Contains(Key.LeftShift) || view._held.Contains(Key.RightShift) ? .5f : .1f;
        scene.MoveReplayEditorCamera(new Vector3(Axis(Key.D, Key.A), Axis(Key.E, Key.V), Axis(Key.W, Key.S)) * speed,
            view._yaw, view._pitch);
        view._yaw = view._pitch = 0;
    }
}
#endif
