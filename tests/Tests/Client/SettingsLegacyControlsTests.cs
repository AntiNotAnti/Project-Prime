using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using MphRead.Entities;
using MphRead.Hud.Radar;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Theme;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class SettingsLegacyControlsTests
{
    [AvaloniaFact]
    public void LegacySettingsRowsKeepTouchTargetsAndAccessibleDescriptions()
    {
        var choice = new ChoiceRow("Preferred region", new[] { "Automatic", "North America" });
        var toggle = new ToggleRow("Reduce interface motion", false);
        var slider = new SliderRow("Sensitivity", 50);
        var key = new KeyRow("Chat key", () => new Keybind(PrimeKey.T),
            (_, _, _) => { });
        var pad = new PadRow(PadAction.Jump);
        var field = new FieldRow("Display name", "Player");

        Control[] controls = { choice, toggle, slider, key, pad, field };
        Assert.All(controls, control =>
        {
            Assert.True(control.Height >= PrimeTouchTargets.MinimumDip);
            Assert.False(String.IsNullOrWhiteSpace(
                AutomationProperties.GetName(control)));
        });
        Assert.Equal(PrimeTouchTargets.MinimumDip, field.Height);
        Assert.Equal("Enter a value.",
            AutomationProperties.GetHelpText(field.Box));
        Assert.All(controls.Take(5), control => Assert.False(
            String.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(control))));
    }

    [AvaloniaFact]
    public void ChoiceRowsMeasureTheirValueColumnForCompactLayouts()
    {
        var choice = new ChoiceRow("Preferred region", new[]
        {
            "Automatic", "North America", "A deliberately long region label"
        });
        Arrange(choice, 360);
        double longValueWidth = choice.RenderedValueWidth;

        choice.SetItems(new[] { "A", "B" });
        Arrange(choice, 360);

        Assert.InRange(longValueWidth, 0, PrimeLegacyControlVisuals.MaximumValueColumn);
        Assert.InRange(choice.RenderedValueWidth, 0,
            PrimeLegacyControlVisuals.MaximumValueColumn);
        Assert.True(choice.RenderedRightArrow.X >= choice.RenderedLeftArrow.Right);
    }

    [AvaloniaFact]
    public void MenuEntriesKeepSuppliedSentenceCaseAndFocusableStatus()
    {
        var entry = new MenuEntry("Save changes", "Apply this settings draft.");

        Assert.Equal("Save changes", AutomationProperties.GetName(entry));
        Assert.Equal("Apply this settings draft.",
            AutomationProperties.GetHelpText(entry));
        Assert.True(entry.Focusable);
        Assert.Equal(PrimeTouchTargets.MinimumDip + 10, entry.Height);

        entry.Primary = true;
        Assert.Contains("prime-primary", entry.Classes);
    }

    [AvaloniaFact]
    public void SettingsFooterUsesImmediateReducedMotionAndHidesWhenClean()
    {
        bool previousReducedMotion = LauncherPrefs.ReducedMotion;
        try
        {
            LauncherPrefs.ReducedMotion = true;
            using var settings = new SettingsView(new MenuSettings());
            SettingsActionBar footer = Assert.Single(Walk(settings)
                .OfType<SettingsActionBar>());
            SliderRow fieldOfView = GetPrivateField<SliderRow>(settings,
                "_fieldOfView");

            fieldOfView.Value++;

            Assert.True(footer.IsDirty);
            Assert.True(footer.IsVisible);
            Assert.Equal(1, footer.Opacity);
            Assert.Equal("Settings saved.",
                AutomationProperties.GetName(Assert.Single(Walk(footer)
                    .OfType<Note>(), note => note.Text == "Settings saved.")));

            fieldOfView.Value--;

            Assert.False(footer.IsDirty);
            Assert.False(footer.IsVisible);
            Assert.Equal(1, footer.Opacity);
        }
        finally
        {
            LauncherPrefs.ReducedMotion = previousReducedMotion;
        }
    }

    [AvaloniaFact]
    public void RadarLayoutPreviewSupportsKeyboardMovementAndResize()
    {
        Vector movement = default;
        float resize = 0;
        var preview = new RadarLayoutEditorPreview((_, _) => { },
            delta => movement += delta, delta => resize += delta);

        var right = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Right
        };
        preview.RaiseEvent(right);
        var grow = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.OemPlus
        };
        preview.RaiseEvent(grow);

        Assert.True(preview.Focusable);
        Assert.True(right.Handled);
        Assert.True(grow.Handled);
        Assert.Equal(new Vector(RadarLayoutEditor.SnapStep, 0), movement);
        Assert.Equal(.05f, resize, 3);
    }

    [AvaloniaFact]
    public void EveryRadarRowBuildsTheExpectedNormalizedProfile()
    {
        using var view = new SettingsView(new MenuSettings());
        RadarProfile baseline = RadarSettings.DefaultProfile;

        GetPrivateField<ChoiceRow>(view, "_radarStyleRow").Index = (int)RadarStyle.Enhanced;
        GetPrivateField<ChoiceRow>(view, "_radarOrientationRow").Index = (int)RadarOrientation.North;
        GetPrivateField<ChoiceRow>(view, "_radarPositionRow").Index = (int)RadarAnchor.Custom;
        GetPrivateField<SliderRow>(view, "_radarScaleRow").Value = 80;
        GetPrivateField<SliderRow>(view, "_radarOffsetXRow").Value = 28;
        GetPrivateField<SliderRow>(view, "_radarOffsetYRow").Value = -20;
        GetPrivateField<SliderRow>(view, "_radarRangeRow").Value = 65;
        GetPrivateField<SliderRow>(view, "_radarOpacityRow").Value = 75;
        GetPrivateField<ToggleRow>(view, "_radarElevationRow").On = false;
        GetPrivateField<ChoiceRow>(view, "_radarColorRow").Index
            = (int)RadarColorPreset.Protanopia;
        GetPrivateField<SliderRow>(view, "_radarMarkerScaleRow").Value = 125;
        GetPrivateField<SliderRow>(view, "_radarMarkerOpacityRow").Value = 65;
        GetPrivateField<ToggleRow>(view, "_radarMarkerOutlineRow").On = false;
        GetPrivateField<ToggleRow>(view, "_radarEdgeArrowsRow").On = false;
        GetPrivateField<ToggleRow>(view, "_radarLabelsRow").On = false;
        GetPrivateField<ToggleRow>(view, "_radarObjectiveEmphasisRow").On = false;
        GetPrivateField<ToggleRow>(view, "_radarEnemiesRow").On = false;
        GetPrivateField<ToggleRow>(view, "_radarTeammatesRow").On = false;
        GetPrivateField<ToggleRow>(view, "_radarObjectivesRow").On = false;
        GetPrivateField<ToggleRow>(view, "_radarFlagsRow").On = false;
        GetPrivateField<ToggleRow>(view, "_radarBasesRow").On = false;
        GetPrivateField<ToggleRow>(view, "_radarNodesRow").On = false;
        GetPrivateField<ToggleRow>(view, "_radarDefendersRow").On = false;
        GetPrivateField<ChoiceRow>(view, "_radarFloorRow").Index = (int)RadarFloorMode.All;
        GetPrivateField<SliderRow>(view, "_radarElevationThresholdRow").Value = 325;
        GetPrivateField<ToggleRow>(view, "_radarMapFillRow").On = false;
        GetPrivateField<ToggleRow>(view, "_radarMapOutlinesRow").On = false;
        GetPrivateField<SliderRow>(view, "_radarFloorBrightnessRow").Value = 125;
        GetPrivateField<SliderRow>(view, "_radarAdjacentOpacityRow").Value = 35;
        GetPrivateField<SliderRow>(view, "_radarBackgroundDimRow").Value = 45;
        GetPrivateField<SliderRow>(view, "_radarBackgroundBlurRow").Value = 30;
        GetPrivateField<ToggleRow>(view, "_radarGridRow").On = true;
        GetPrivateField<ToggleRow>(view, "_radarRingsRow").On = false;
        GetPrivateField<ToggleRow>(view, "_radarCompassRow").On = false;
        GetPrivateField<ChoiceRow>(view, "_radarZoomRow").Index
            = (int)RadarZoomMode.CombatSensitive;
        GetPrivateField<SliderRow>(view, "_radarAutoMinimumRow").Value = 35;
        GetPrivateField<SliderRow>(view, "_radarAutoMaximumRow").Value = 70;
        GetPrivateField<SliderRow>(view, "_radarZoomSmoothingRow").Value = 650;
        GetPrivateField<SliderRow>(view, "_radarPersistenceRow").Value = 1200;
        GetPrivateField<ToggleRow>(view, "_radarPulseRow").On = false;
        GetPrivateField<SliderRow>(view, "_radarEdgeScaleRow").Value = 140;
        GetPrivateField<ToggleRow>(view, "_radarPriorityRow").On = false;

        RadarProfile actual = InvokePrivate<RadarProfile>(view,
            "BuildRadarProfileFromRows");
        RadarProfile expected = (baseline with
        {
            Name = "Custom", Preset = RadarPreset.Custom,
            Style = RadarStyle.Enhanced, Orientation = RadarOrientation.North,
            Anchor = RadarAnchor.Custom, Scale = 1.33f, OffsetX = 28, OffsetY = -20,
            Range = 65, Opacity = .75f, ElevationIndicators = false,
            ColorPreset = RadarColorPreset.Protanopia,
            Colors = RadarColors.For(RadarColorPreset.Protanopia),
            MarkerScale = 1.25f, MarkerOpacity = .65f, MarkerOutline = false,
            EdgeArrows = false, Labels = false, ObjectiveEmphasis = false,
            ShowEnemies = false, ShowTeammates = false, ShowObjectives = false,
            ShowFlags = false, ShowBases = false, ShowNodes = false,
            ShowDefenders = false, FloorMode = RadarFloorMode.All,
            ElevationThreshold = 3.25f, MapFill = false, MapOutlines = false,
            FloorBrightness = 1.25f, AdjacentFloorOpacity = .35f,
            BackgroundDim = .45f, BackgroundBlur = .3f, Grid = true,
            DistanceRings = false, Compass = false,
            ZoomMode = RadarZoomMode.CombatSensitive,
            AutomaticMinimumRange = 35, AutomaticMaximumRange = 70,
            ZoomSmoothingSeconds = .65f, ContactPersistenceSeconds = 1.2f,
            ContactPulse = false, ClampedEdgeScale = 1.4f,
            PrioritizeObjectives = false
        }).Normalize();

        Assert.Equal(expected with { Scale = actual.Scale }, actual);
        Assert.Equal(expected.Scale, actual.Scale, 3);
    }

    [AvaloniaFact]
    public void RadarPresetAndDependentRowsStayCoherent()
    {
        using var view = new SettingsView(new MenuSettings());
        ChoiceRow preset = GetPrivateField<ChoiceRow>(view, "_radarPresetRow");
        SliderRow markerScale = GetPrivateField<SliderRow>(view, "_radarMarkerScaleRow");
        int originalMarkerScale = markerScale.Value;

        preset.Index = (int)RadarPreset.Custom;
        Assert.Equal(originalMarkerScale, markerScale.Value);

        preset.Index = (int)RadarPreset.Minimal;
        Assert.False(GetPrivateField<ToggleRow>(view, "_radarLabelsRow").On);
        Assert.Equal("Minimal", InvokePrivate<RadarProfile>(view,
            "BuildRadarProfileFromRows").Name);

        markerScale.Value += 5;
        Assert.Equal((int)RadarPreset.Custom, preset.Index);
        Assert.Equal("Custom", InvokePrivate<RadarProfile>(view,
            "BuildRadarProfileFromRows").Name);

        ChoiceRow zoom = GetPrivateField<ChoiceRow>(view, "_radarZoomRow");
        SliderRow fixedRange = GetPrivateField<SliderRow>(view, "_radarRangeRow");
        SliderRow automaticMinimum = GetPrivateField<SliderRow>(view,
            "_radarAutoMinimumRow");
        SliderRow automaticMaximum = GetPrivateField<SliderRow>(view,
            "_radarAutoMaximumRow");
        zoom.Index = (int)RadarZoomMode.Automatic;
        Assert.False(fixedRange.IsVisible);
        Assert.True(automaticMinimum.IsVisible);
        automaticMinimum.Value = 75;
        Assert.Equal(75, automaticMaximum.Value);
        automaticMaximum.Value = 30;
        Assert.Equal(30, automaticMinimum.Value);
        zoom.Index = (int)RadarZoomMode.Fixed;
        Assert.True(fixedRange.IsVisible);
        Assert.False(automaticMinimum.IsVisible);
    }

    private static void Arrange(Control control, double width)
    {
        control.Measure(new Size(width, PrimeTouchTargets.MinimumDip));
        control.Arrange(new Rect(0, 0, width, PrimeTouchTargets.MinimumDip));
    }

    private static T GetPrivateField<T>(SettingsView view, string name)
        => (T)(typeof(SettingsView).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view)
            ?? throw new InvalidOperationException($"Missing SettingsView field {name}."));

    private static T InvokePrivate<T>(SettingsView view, string name)
        => (T)(typeof(SettingsView).GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(view, null)
            ?? throw new InvalidOperationException($"Missing SettingsView method {name}."));

    private static IEnumerable<Control> Walk(Control control)
    {
        yield return control;
        if (control is Panel panel)
        {
            foreach (Control child in panel.Children)
            {
                foreach (Control descendant in Walk(child))
                    yield return descendant;
            }
        }
        else if (control is ContentControl content
            && content.Content is Control contentChild)
        {
            foreach (Control descendant in Walk(contentChild))
                yield return descendant;
        }
        else if (control is Decorator decorator && decorator.Child is Control child)
        {
            foreach (Control descendant in Walk(child))
                yield return descendant;
        }
    }
}
