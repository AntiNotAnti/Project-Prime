using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapViewport
{
    private FormattedText? _unavailablePreviewText;
    private void DrawUnavailablePreviewHud(DrawingContext context)
    {
        if(GpuActive || !(LightingPreview || ShadowPreview || FogPreview || DiagnosticMode!=MphRead.Mods.MapEditor.MapViewportDiagnosticMode.None))return;
#if MPHREAD_SHELL
        if(_capturingStudioOverlay)return;
#endif
        _unavailablePreviewText ??=new FormattedText("Preview requires the native GPU viewport · CPU authoring remains available",
            CultureInfo.InvariantCulture,FlowDirection.LeftToRight,GuiTheme.Face(bold:false),11,Brushes.Gold);
        var bounds=new Rect(10,10,Math.Max(1,Math.Min(_unavailablePreviewText.Width+16,Bounds.Width-20)),_unavailablePreviewText.Height+12);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(230,18,30,42)),null,bounds,4,4);
        using(context.PushClip(bounds))context.DrawText(_unavailablePreviewText,bounds.TopLeft+new Avalonia.Vector(8,6));
    }
    private string? _transformHudValue;
    private FormattedText? _transformHudText;
    private void DrawTransformHud(DrawingContext context)
    {
        string axis=_axis>=0 ? "XYZ"[_axis].ToString() : Axes;
        float snap=Tool=="Rotate" ? AngleSnap : Tool=="Scale" ? ScaleSnap : Snap;
        var origin=GizmoCenter;
        string value=FormattableString.Invariant($"{Tool} · {axis} · {(LocalAxes ? "Local" : "World")} · {PivotMode} pivot · Snap {snap:0.###}\nOrigin {origin.X:0.###}, {origin.Y:0.###}, {origin.Z:0.###}");
        if(_drag)
        {
            value+=Tool=="Rotate" ? FormattableString.Invariant($"\nAngle {_rotation:0.###}°")
                : Tool=="Scale" ? FormattableString.Invariant($"\nScale {_scale:0.###}")
                : FormattableString.Invariant($"\nDelta {_preview.X:0.###}, {_preview.Y:0.###}, {_preview.Z:0.###}");
            if(_keyboardTransform)value+=" · Input "+(_numericTransform.Length==0 ? "—" : _numericTransform)+" · Enter apply · Esc cancel";
        }
        if(_transformHudValue!=value)
        {
            _transformHudValue=value;
            _transformHudText=new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,GuiTheme.Face(bold:false),11,Brushes.White);
        }
        var text=_transformHudText!;
        var bounds=new Rect(10,Math.Max(10,Bounds.Height-text.Height-22),Math.Max(1,Math.Min(text.Width+16,Bounds.Width-20)),text.Height+12);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(220,18,30,42)),new Pen(Brushes.SlateGray,1),bounds,4,4);
        using(context.PushClip(bounds))context.DrawText(text,bounds.TopLeft+new Avalonia.Vector(8,6));
    }
}
