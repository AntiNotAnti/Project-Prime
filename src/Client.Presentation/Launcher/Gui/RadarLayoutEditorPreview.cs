using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

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
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _draw(context, Bounds);
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
}
