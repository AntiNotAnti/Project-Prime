using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MphRead.Hud.Radar;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Direct-manipulation preview used only to edit local radar layout.</summary>
internal sealed class RadarLayoutEditorPreview : Control
{
    private readonly Action<DrawingContext, Rect> _draw;
    private readonly Action<Vector> _drag;
    private readonly Action<float> _resize;
    private Point _last;

    public RadarLayoutEditorPreview(Action<DrawingContext, Rect> draw,
        Action<Vector> drag, Action<float> resize)
    {
        _draw = draw ?? throw new ArgumentNullException(nameof(draw));
        _drag = drag ?? throw new ArgumentNullException(nameof(drag));
        _resize = resize ?? throw new ArgumentNullException(nameof(resize));
        Height = 150;
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _draw(context, Bounds);
        if (IsFocused)
            context.DrawRectangle(null, new Pen(GuiTheme.AccentBrush, 2),
                Bounds.Deflate(1));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _last = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured != this || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Point current = e.GetPosition(this);
        _drag(current - _last);
        _last = current;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this) e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0) return;
        _resize(e.Delta.Y > 0 ? .05f : -.05f);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        Vector? movement = e.Key switch
        {
            Key.Left => new Vector(-RadarLayoutEditor.SnapStep, 0),
            Key.Right => new Vector(RadarLayoutEditor.SnapStep, 0),
            Key.Up => new Vector(0, -RadarLayoutEditor.SnapStep),
            Key.Down => new Vector(0, RadarLayoutEditor.SnapStep),
            _ => null
        };
        if (movement is { } delta)
        {
            _drag(delta);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Add or Key.OemPlus)
        {
            _resize(.05f);
            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.Key is Key.Subtract or Key.OemMinus)
        {
            _resize(-.05f);
            InvalidateVisual();
            e.Handled = true;
        }
    }
}
