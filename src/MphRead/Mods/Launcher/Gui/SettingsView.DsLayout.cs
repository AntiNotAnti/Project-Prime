#if MPHREAD_AVALONIA
using System;
using Avalonia.Controls;
using MphRead.Mods.Input;

namespace MphRead.Mods.Launcher.Gui
{
    internal sealed partial class SettingsView
    {
        private ToggleRow? _dsStacked;
        private ChoiceRow? _dsAspect;
        private SliderRow? _dsRadarSize, _dsRadarX, _dsRadarY;

        private void BuildDsLayout(StackPanel page)
        {
            Heading(page, "Dual-screen presentation");
            _dsStacked = Add(page, new ToggleRow(
                "Stack two equal screens", DsLayout.Enabled));
            Explain(page, "Desktop stylus mode only. The lower LCD owns the weapon "
                + "controls and radar; the upper LCD shows gameplay and combat. "
                + "Other layouts remain unchanged.");
            _dsAspect = Add(page, new ChoiceRow("Each screen aspect",
                new[] { "4:3", "5:3" }, DsLayout.ScreenAspect == "5:3" ? 1 : 0));
            _dsRadarSize = Add(page, new SliderRow("Bottom radar size",
                (int)MathF.Round(DsLayout.RadarSize * 100),
                v => v + "%", min: 25, max: 300, keyStep: 5));
            _dsRadarX = Add(page, new SliderRow("Bottom radar horizontal",
                (int)MathF.Round(DsLayout.RadarX * 100),
                v => v + "%", min: 0, max: 100, keyStep: 1));
            _dsRadarY = Add(page, new SliderRow("Bottom radar vertical",
                (int)MathF.Round(DsLayout.RadarY * 100),
                v => v + "%", min: 0, max: 100, keyStep: 1));
            _dsStacked.Changed += (_, _) => ShowDsLayoutRows();
            ShowDsLayoutRows();
        }

        private void ShowDsLayoutRows()
        {
            bool active = _dsStacked?.On == true && _penTablet.On;
            if (_dsAspect != null) _dsAspect.IsVisible = active;
            if (_dsRadarSize != null) _dsRadarSize.IsVisible = active;
            if (_dsRadarX != null) _dsRadarX.IsVisible = active;
            if (_dsRadarY != null) _dsRadarY.IsVisible = active;
        }

        private void ApplyDsLayout()
        {
            if (_dsStacked == null) return;
            DsLayout.Enabled = _dsStacked.On;
            DsLayout.ScreenAspect = _dsAspect?.Index == 1 ? "5:3" : "4:3";
            if (_dsRadarSize != null) DsLayout.RadarSize = _dsRadarSize.Value / 100f;
            if (_dsRadarX != null) DsLayout.RadarX = _dsRadarX.Value / 100f;
            if (_dsRadarY != null) DsLayout.RadarY = _dsRadarY.Value / 100f;
        }

        private void ResetDsLayoutRows()
        {
            if (_dsStacked == null) return;
            _dsStacked.On = DsLayout.Enabled;
            if (_dsAspect != null) _dsAspect.Index = DsLayout.ScreenAspect == "5:3" ? 1 : 0;
            if (_dsRadarSize != null) _dsRadarSize.Value = (int)MathF.Round(DsLayout.RadarSize * 100);
            if (_dsRadarX != null) _dsRadarX.Value = (int)MathF.Round(DsLayout.RadarX * 100);
            if (_dsRadarY != null) _dsRadarY.Value = (int)MathF.Round(DsLayout.RadarY * 100);
            ShowDsLayoutRows();
        }
    }
}
#endif
