using OpenTK.Windowing.Common;

sealed partial class CheckWindow
{
    private readonly NativeInputIsolation _nativeInputIsolation = new();
    protected override void OnKeyDown(KeyboardKeyEventArgs e)
        => _nativeInputIsolation?.Observe(NativeInputKind.KeyDown, (int)e.Key);
    protected override void OnKeyUp(KeyboardKeyEventArgs e)
        => _nativeInputIsolation?.Observe(NativeInputKind.KeyUp, (int)e.Key);
    protected override void OnTextInput(TextInputEventArgs e)
        => _nativeInputIsolation?.Observe(NativeInputKind.Text, e.Unicode);
    protected override void OnMouseDown(MouseButtonEventArgs e)
        => _nativeInputIsolation?.Observe(NativeInputKind.MouseDown, (int)e.Button);
    protected override void OnMouseUp(MouseButtonEventArgs e)
        => _nativeInputIsolation?.Observe(NativeInputKind.MouseUp, (int)e.Button);
    protected override void OnMouseWheel(MouseWheelEventArgs e)
        => _nativeInputIsolation?.Observe(NativeInputKind.MouseWheel, x: e.OffsetX, y: e.OffsetY);
    protected override void OnMouseMove(MouseMoveEventArgs e)
        => _nativeInputIsolation?.Observe(NativeInputKind.MouseMove);
}
