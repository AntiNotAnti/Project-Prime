using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MphRead.Entities;
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

    private static void Arrange(Control control, double width)
    {
        control.Measure(new Size(width, PrimeTouchTargets.MinimumDip));
        control.Arrange(new Rect(0, 0, width, PrimeTouchTargets.MinimumDip));
    }

    private static T GetPrivateField<T>(SettingsView view, string name)
        => (T)(typeof(SettingsView).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view)
            ?? throw new InvalidOperationException($"Missing SettingsView field {name}."));

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
