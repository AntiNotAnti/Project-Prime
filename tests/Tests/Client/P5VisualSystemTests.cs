using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Theme;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class P5VisualSystemTests
{
    [Fact]
    public void OptionalMotionUsesShortOpacityAndTranslationRecipe()
    {
        PrimeMotionSpec spec = PrimeMotion.Resolve(reducedMotion: false);

        Assert.InRange(spec.Duration.TotalMilliseconds, 160, 220);
        Assert.Equal(new Vector(0, PrimeMotion.DefaultTranslationDip),
            spec.Offset);
        Assert.Equal(0, spec.InitialOpacity);
        Assert.True(spec.IsEnabled);
    }

    [Fact]
    public void ReducedMotionResolvesToImmediateZeroOffset()
    {
        PrimeMotionSpec spec = PrimeMotion.Resolve(reducedMotion: true);

        Assert.Equal(TimeSpan.Zero, spec.Duration);
        Assert.Equal(Vector.Zero, spec.Offset);
        Assert.Equal(1, spec.InitialOpacity);
        Assert.True(spec.IsImmediate);
    }

    [AvaloniaFact]
    public void ReducedMotionDoesNotCreateTransitionsOrALease()
    {
        var control = new Border();

        Assert.Null(PrimeMotion.CreateTransitions(reducedMotion: true));
        Assert.Null(PrimeMotion.CreateTranslationTransitions(
            reducedMotion: true));
        Assert.Null(PrimeMotion.AnimateEntry(control,
            reducedMotion: true));
        Assert.Null(control.Transitions);
        Assert.Equal(1, control.Opacity);
    }

    [AvaloniaFact]
    public void NormalMotionCreatesOpacityAndTranslationTransitions()
    {
        Assert.Single(PrimeMotion.CreateTransitions(reducedMotion: false)!);
        Assert.Equal(2, PrimeMotion.CreateTranslationTransitions(
            reducedMotion: false)!.Count);
    }

    [AvaloniaFact]
    public void CustomSettingsControlsExposeNamesAndTouchSizedTargets()
    {
        Control[] controls =
        [
            new MenuEntry("Player"),
            new ChoiceRow("Input type", ["Mouse", "Stylus"]),
            new ToggleRow("Invert aim", false),
            new SliderRow("Sensitivity", 50),
            new KeyRow("Chat", () => new Keybind(
                    OpenTK.Windowing.GraphicsLibraryFramework.Keys.T),
                (_, _, _) => { }),
            new PadRow(PadAction.Jump)
        ];

        Assert.All(controls, control =>
        {
            Assert.True(control.Height >= PrimeTouchTargets.MinimumDip);
            Assert.False(String.IsNullOrWhiteSpace(
                AutomationProperties.GetName(control)));
        });
        Assert.All(controls.Skip(1), control => Assert.False(
            String.IsNullOrWhiteSpace(AutomationProperties.GetItemStatus(control))));
    }

    [AvaloniaFact]
    public void SliderUsesTouchSizedTrackLaneWithoutIncludingLabelOrValueGutter()
    {
        var slider = new SliderRow("Sensitivity", 50);
        slider.Measure(new Size(420, PrimeTouchTargets.MinimumDip));
        slider.Arrange(new Rect(0, 0, 420, PrimeTouchTargets.MinimumDip));

        Rect track = slider.RenderedTrack;
        Assert.True(slider.CanStartDrag(track.Center));
        Assert.True(slider.CanStartDrag(new Point(track.Center.X, 2)));
        Assert.True(slider.CanStartDrag(new Point(track.Center.X,
            PrimeTouchTargets.MinimumDip - 2)));
        Assert.False(slider.CanStartDrag(new Point(2, track.Center.Y)));
        Assert.False(slider.CanStartDrag(new Point(419, track.Center.Y)));
    }

    [AvaloniaFact]
    public void EntryLeaseOwnsUnattachedControlStateUntilDisposed()
    {
        var control = new Border();
        PrimeMotionLease? lease = PrimeMotion.AnimateEntry(control,
            reducedMotion: false);

        Assert.NotNull(lease);
        Assert.False(lease!.IsStarted);
        Assert.Equal(0, control.Opacity);
        Assert.NotNull(control.RenderTransform);

        lease.Dispose();
        Assert.True(lease.IsDisposed);
        Assert.Equal(1, control.Opacity);
        Assert.Null(control.RenderTransform);
    }

    [Fact]
    public void LauncherPreferenceControlsDefaultMotionResolution()
    {
        bool previous = LauncherPrefs.ReducedMotion;
        try
        {
            LauncherPrefs.ReducedMotion = true;
            Assert.True(PrimeMotion.Resolve().IsImmediate);
            LauncherPrefs.ReducedMotion = false;
            Assert.False(PrimeMotion.Resolve().IsImmediate);
        }
        finally
        {
            LauncherPrefs.ReducedMotion = previous;
        }
    }

    [Fact]
    public void PrimeThemeDeclaresHierarchyAndSemanticResourceKeys()
    {
        Assert.Contains(PrimeVisualTokens.BackgroundBrush,
            PrimeVisualTokens.HierarchyBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.PrimarySurfaceBrush,
            PrimeVisualTokens.HierarchyBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.RaisedSurfaceBrush,
            PrimeVisualTokens.HierarchyBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.SecondarySurfaceBrush,
            PrimeVisualTokens.HierarchyBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.InteractiveSurfaceBrush,
            PrimeVisualTokens.HierarchyBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.SelectedSurfaceBrush,
            PrimeVisualTokens.HierarchyBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.ModalSurfaceBrush,
            PrimeVisualTokens.HierarchyBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.BrandBrush,
            PrimeVisualTokens.SemanticBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.BrandStrongBrush,
            PrimeVisualTokens.SemanticBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.OnBrandBrush,
            PrimeVisualTokens.SemanticBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.TechBrush,
            PrimeVisualTokens.SemanticBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.TechStrongBrush,
            PrimeVisualTokens.SemanticBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.GunmetalBrush,
            PrimeVisualTokens.SemanticBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.InfoBrush,
            PrimeVisualTokens.SemanticBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.SuccessBrush,
            PrimeVisualTokens.SemanticBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.WarningBrush,
            PrimeVisualTokens.SemanticBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.ErrorBrush,
            PrimeVisualTokens.SemanticBrushResourceKeys);
        Assert.Contains(PrimeVisualTokens.DestructiveBrush,
            PrimeVisualTokens.SemanticBrushResourceKeys);
        Assert.Equal(PrimeVisualTokens.SuccessBrush,
            PrimeVisualTokens.BrushKey(PrimeSemanticColor.Success));
        Assert.Equal(PrimeVisualTokens.DestructiveBrush,
            PrimeVisualTokens.BrushKey(PrimeSemanticColor.Destructive));
        Assert.Equal(PrimeVisualTokens.BrandBrush,
            PrimeVisualTokens.BrushKey(PrimeSemanticColor.Brand));
        Assert.Equal(PrimeVisualTokens.TechBrush,
            PrimeVisualTokens.BrushKey(PrimeSemanticColor.Tech));
        Assert.Equal(PrimeVisualTokens.BrandSurfaceBrush,
            PrimeVisualTokens.BrushKey(PrimeSemanticColor.BrandSurface));
    }

    [Fact]
    public void XamlAndCodeBuiltPalettesStaySynchronized()
    {
        Dictionary<string, XElement> palette = LoadElements(
            "src/Client/Launcher/Theme/PrimeColors.axaml", "SolidColorBrush");

        AssertBrushColor(palette, "PrimeBackgroundBrush", GuiTheme.Ink);
        AssertBrushColor(palette, "PrimePrimarySurfaceBrush", GuiTheme.Panel);
        AssertBrushColor(palette, "PrimeSurfaceRaisedBrush", GuiTheme.PanelLight);
        AssertBrushColor(palette, "PrimeTextBrush", GuiTheme.Text);
        AssertBrushColor(palette, "PrimeTextMutedBrush", GuiTheme.TextDim);
        AssertBrushColor(palette, "PrimeBrandBrush", GuiTheme.Brand);
        AssertBrushColor(palette, "PrimeBrandStrongBrush", GuiTheme.BrandStrong);
        AssertBrushColor(palette, "PrimeBrandSurfaceBrush", GuiTheme.BrandSurface);
        AssertBrushColor(palette, "PrimeTechBrush", GuiTheme.Tech);
        AssertBrushColor(palette, "PrimeTechStrongBrush", GuiTheme.TechStrong);
        AssertBrushColor(palette, "PrimeGunmetalBrush", GuiTheme.Gunmetal);
        AssertBrushColor(palette, "PrimeSuccessBrush", GuiTheme.Success);
        AssertBrushColor(palette, "PrimeWarningBrush", GuiTheme.Warning);
        AssertBrushColor(palette, "PrimeErrorBrush", GuiTheme.Error);

        AssertBrushColor(palette, "PrimeAccentBrush", GuiTheme.Brand);
        AssertBrushColor(palette, "PrimeAccentStrongBrush", GuiTheme.BrandStrong);
        AssertBrushColor(palette, "PrimeOnBrandBrush", GuiTheme.Ink);
        AssertBrushColor(palette, "PrimeOnAccentBrush", GuiTheme.Ink);
        AssertBrushColor(palette, "PrimeInfoBrush", GuiTheme.Tech);
        AssertBrushColor(palette, "PrimeWarmBrush", GuiTheme.Warning);
        Assert.Equal(GuiTheme.Brand, GuiTheme.Accent);
        Assert.Same(GuiTheme.BrandBrush, GuiTheme.AccentBrush);

        double dotOpacity = Double.Parse(
            palette["PrimeDotBrush"].Attribute("Opacity")!.Value,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(dotOpacity, 0.20, 0.28);
    }

    [Fact]
    public void ThemeStylesKeepBrandTechSelectionAndFocusDistinct()
    {
        Dictionary<string, XElement> controls = LoadElements(
            "src/Client/Launcher/Theme/PrimeControls.axaml", "Style",
            "Selector");
        Dictionary<string, XElement> typography = LoadElements(
            "src/Client/Launcher/Theme/PrimeTypography.axaml", "Style",
            "Selector");

        AssertSetter(controls, ":is(Button).prime-button", "Background",
            "{DynamicResource PrimeInteractiveSurfaceBrush}");
        AssertSetter(controls, ":is(Button).prime-button", "BorderBrush",
            "{DynamicResource PrimeGunmetalBrush}");
        AssertSetter(controls, ":is(Button).prime-primary", "Background",
            "{DynamicResource PrimeBrandBrush}");
        AssertSetter(controls, ":is(Button).prime-primary", "BorderBrush",
            "{DynamicResource PrimeBrandStrongBrush}");
        AssertSetter(controls, ":is(Button).prime-button:focus", "BorderBrush",
            "{DynamicResource PrimeFocusBrush}");
        AssertSetter(controls,
            ":is(Border).prime-selected-row.prime-selected", "Background",
            "{DynamicResource PrimeBrandSurfaceBrush}");
        AssertSetter(controls, ":is(Border).prime-status-info", "BorderBrush",
            "{DynamicResource PrimeTechBrush}");
        AssertSetter(controls, ":is(Border).prime-tech", "BorderBrush",
            "{DynamicResource PrimeTechBrush}");
        AssertSetter(controls, ":is(Button).prime-tab.prime-selected",
            "BorderBrush", "{DynamicResource PrimeBrandBrush}");
        AssertSetter(controls, ":is(Button).prime-tab:focus", "BorderBrush",
            "{DynamicResource PrimeFocusBrush}");
        AssertSetter(controls, ":is(Button).prime-button:disabled", "Opacity",
            "0.58");
        AssertSetter(controls, ":is(Button).prime-primary:disabled", "Background",
            "{DynamicResource PrimeSurfaceMutedBrush}");
        AssertSetter(controls, ":is(Button).prime-tab:disabled", "Foreground",
            "{DynamicResource PrimeTextMutedBrush}");
        AssertSetter(controls,
            "TextBox.prime-input:disabled, ComboBox.prime-input:disabled",
            "BorderBrush", "{DynamicResource PrimeDividerBrush}");
        AssertSetter(controls, "CheckBox.prime-input:disabled", "Opacity", "0.58");
        AssertSetter(controls, "Slider.prime-input:disabled", "Opacity", "0.58");
        AssertSetter(controls, ":is(Border).prime-history-row", "BorderBrush",
            "{DynamicResource PrimeDividerBrush}");
        AssertSetter(controls, ":is(Border).prime-status-muted", "BorderBrush",
            "{DynamicResource PrimeDividerBrush}");

        AssertSetter(typography, "TextBlock.prime-brand", "Foreground",
            "{DynamicResource PrimeBrandStrongBrush}");
        AssertSetter(typography, "TextBlock.prime-status-text", "Foreground",
            "{DynamicResource PrimeTechBrush}");
        AssertSetter(typography, "TextBlock.prime-kicker", "Foreground",
            "{DynamicResource PrimeBrandBrush}");

        Assert.DoesNotContain("PrimeAccent", File.ReadAllText(FindRepositoryFile(
            "src/Client/Launcher/Theme/PrimeControls.axaml")));
        Assert.DoesNotContain("PrimeAccent", File.ReadAllText(FindRepositoryFile(
            "src/Client/Launcher/Theme/PrimeTypography.axaml")));
    }

    [AvaloniaFact]
    public void StatusSemanticsExposeNameStatusAndLivePriority()
    {
        var status = new TextBlock();
        PrimeAccessibility.SetStatus(status, "Connected",
            PrimeStatusKind.Success);

        Assert.Equal("Connected", AutomationProperties.GetName(status));
        Assert.Equal("Connected",
            AutomationProperties.GetItemStatus(status));
        Assert.Equal(AutomationLiveSetting.Polite,
            AutomationProperties.GetLiveSetting(status));
        Assert.Contains("prime-status-success", status.Classes);
        Assert.DoesNotContain("prime-status-info", status.Classes);

        PrimeAccessibility.SetStatus(status, "Connection failed",
            PrimeStatusKind.Error);
        Assert.Equal(AutomationLiveSetting.Assertive,
            AutomationProperties.GetLiveSetting(status));
        Assert.Contains("prime-status-error", status.Classes);
        Assert.DoesNotContain("prime-status-success", status.Classes);
    }

    [Fact]
    public void TouchTargetsAndSafeAreaHaveAndroidSafeMinimums()
    {
        Assert.Equal(44, PrimeTouchTargets.MinimumDip);
        Assert.Equal(48, PrimeTouchTargets.PreferredPrimaryMinimumDip);
        Assert.Equal(52, PrimeTouchTargets.PreferredPrimaryMaximumDip);
        Assert.Equal(44, PrimeTouchTargets.EnsureMinimum(32));
        Assert.Equal(52, PrimeTouchTargets.EnsurePrimary(64));

        Thickness safe = PrimeSafeArea.Resolve(new Thickness(0, 10, 8, 6));
        Assert.Equal(new Thickness(16, 24, 16, 24), safe);
        Assert.Equal(safe, PrimeLayoutMetrics.ResolveSafeArea(
            new Thickness(0, 10, 8, 6)));
    }

    [Theory]
    [InlineData(ControllerFamily.Generic, "A")]
    [InlineData(ControllerFamily.Xbox, "A")]
    [InlineData(ControllerFamily.PlayStation, "Cross")]
    [InlineData(ControllerFamily.Nintendo, "B")]
    public void ControllerGlyphsUseTheExistingNormalizedSource(
        ControllerFamily family, string expected)
    {
        string source = ControllerGlyphs.Label(GamepadButtons.A, family);

        Assert.Equal(expected, source);
        Assert.Equal(source, PrimeControllerGlyphs.Label(
            GamepadButtons.A, family));
        Assert.Equal($"Deploy ({expected})", PrimeControllerGlyphs.Prompt(
            "Deploy", GamepadButtons.A, family));
    }

    private static Dictionary<string, XElement> LoadElements(string relativePath,
        string elementName, string keyAttribute = "Key")
    {
        XDocument document = XDocument.Load(FindRepositoryFile(relativePath));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Descendants()
            .Where(element => element.Name.LocalName == elementName)
            .ToDictionary(element => keyAttribute == "Key"
                ? element.Attribute(xaml + keyAttribute)!.Value
                : element.Attribute(keyAttribute)!.Value,
                StringComparer.Ordinal);
    }

    private static void AssertBrushColor(IReadOnlyDictionary<string, XElement> palette,
        string key, Color expected)
        => Assert.Equal(expected, Color.Parse(palette[key].Attribute("Color")!.Value));

    private static void AssertSetter(IReadOnlyDictionary<string, XElement> styles,
        string selector, string property, string expected)
    {
        XElement setter = styles[selector].Elements()
            .Single(element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == property);
        Assert.Equal(expected, setter.Attribute("Value")?.Value);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}'.");
    }
}
