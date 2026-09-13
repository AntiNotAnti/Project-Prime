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
using Avalonia.VisualTree;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Resources;
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
    public void ReducedMotionDoesNotAllocateRouteScan()
    {
        var control = new Border { Opacity = 0.12 };

        Assert.Null(PrimeMotion.AnimateScan(control, 480,
            reducedMotion: true));
        Assert.Equal(0, control.Opacity);
        Assert.Null(control.Transitions);
        Assert.Null(control.RenderTransform);
    }

    [AvaloniaFact]
    public void RouteScanIsOneShotBoundedAndLeaseOwned()
    {
        var control = new Border();
        PrimeScanAccentLease? lease = PrimeMotion.AnimateScan(control, 480,
            reducedMotion: false);

        Assert.NotNull(lease);
        Assert.InRange(control.Opacity, 0.11, 0.13);
        Assert.IsType<TranslateTransform>(control.RenderTransform);
        lease!.Dispose();
        Assert.True(lease.IsDisposed);
        Assert.Equal(1, control.Opacity);
        Assert.Null(control.RenderTransform);
    }

    [AvaloniaFact]
    public void NormalMotionCreatesOpacityAndTranslationTransitions()
    {
        Assert.Single(PrimeMotion.CreateTransitions(reducedMotion: false)!);
        Assert.Equal(2, PrimeMotion.CreateTranslationTransitions(
            reducedMotion: false)!.Count);
    }

    [Fact]
    public void DecorativeMotionHasNoRecurringScheduler()
    {
        string source = File.ReadAllText(FindRepositoryFile(
            "src/Client.Presentation/Launcher/Theme/PrimeMotion.cs"));

        // Route motion is a bounded entry/scan lease. A render-clock timer or
        // repeating animation would make captures nondeterministic and would
        // keep work alive after a route leaves the visual tree.
        Assert.DoesNotContain("DispatcherTimer", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("System.Threading.Timer", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("new Timer", source, StringComparison.Ordinal);
        Assert.Contains("AnimateScan", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsiveMetricsKeepTheSharedCompactBoundary()
    {
        Assert.Equal(720, PrimeLayoutMetrics.CompactWidth);
        Assert.Equal(PrimeContentLayout.Mobile,
            PrimePlayLayout.ResolveContentLayout(719.99));
        Assert.Equal(PrimeContentLayout.Compact,
            PrimePlayLayout.ResolveContentLayout(PrimeLayoutMetrics.CompactWidth));
        Assert.Equal(PrimeContentLayout.Compact,
            PrimePlayLayout.ResolveContentLayout(979.99));
        Assert.Equal(PrimeContentLayout.Wide,
            PrimePlayLayout.ResolveContentLayout(PrimePlayLayout.WideWidth));
        Assert.Equal(1440, PrimeLayoutMetrics.MaxContentWidth);
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
            new KeyRow("Chat", () => new MphRead.Entities.Keybind(
                    PrimeKey.T),
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
            "src/Client.Presentation/Launcher/Theme/PrimeColors.axaml", "SolidColorBrush");

        AssertBrushColor(palette, "PrimeBackgroundBrush", GuiTheme.Ink);
        AssertBrushColor(palette, "PrimeHeaderBrush", GuiTheme.Header);
        AssertBrushColor(palette, "PrimeFooterBrush", GuiTheme.Footer);
        AssertBrushColor(palette, "PrimeSurfaceBrush", GuiTheme.Surface);
        AssertBrushColor(palette, "PrimePrimarySurfaceBrush", GuiTheme.Panel);
        AssertBrushColor(palette, "PrimeSurfaceRaisedBrush", GuiTheme.PanelLight);
        AssertBrushColor(palette, "PrimeSurfaceMutedBrush", GuiTheme.SurfaceMuted);
        AssertBrushColor(palette, "PrimeControlBrush", GuiTheme.Control);
        AssertBrushColor(palette, "PrimeInteractiveSurfaceBrush",
            GuiTheme.InteractiveSurface);
        AssertBrushColor(palette, "PrimeSelectedSurfaceBrush",
            GuiTheme.SelectedSurface);
        AssertBrushColor(palette, "PrimeFocusSurfaceBrush", GuiTheme.FocusSurface);
        AssertBrushColor(palette, "PrimeDividerBrush", GuiTheme.Divider);
        AssertBrushColor(palette, "PrimeEdgeStrongBrush", GuiTheme.EdgeStrong);
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
            "src/Client.Presentation/Launcher/Theme/PrimeControls.axaml", "Style",
            "Selector");
        Dictionary<string, XElement> typography = LoadElements(
            "src/Client.Presentation/Launcher/Theme/PrimeTypography.axaml", "Style",
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
        AssertSetter(controls,
            ":is(Button).prime-tab.prime-selected:focus", "BorderBrush",
            "{DynamicResource PrimeBrandBrush}");
        AssertSetter(controls,
            ":is(Button).prime-tab.prime-selected:focus-visible", "BorderBrush",
            "{DynamicResource PrimeBrandBrush}");
        AssertSetter(controls,
            "#NavPanel :is(Button).prime-button.prime-primary:focus",
            "BorderBrush", "{DynamicResource PrimeBrandBrush}");
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
            "src/Client.Presentation/Launcher/Theme/PrimeControls.axaml")));
        Assert.DoesNotContain("PrimeAccent", File.ReadAllText(FindRepositoryFile(
            "src/Client.Presentation/Launcher/Theme/PrimeTypography.axaml")));
    }

    [Fact]
    public void SharedLayoutMetricsMatchTheCaptureAndRouteContract()
    {
        Assert.Equal(720, PrimeLayoutMetrics.CompactWidth);
        Assert.Equal(980, PrimeLayoutMetrics.MediumWidth);
        Assert.Equal(1200, PrimeLayoutMetrics.WideWidth);
        Assert.Equal(1440, PrimeLayoutMetrics.MaxContentWidth);
        Assert.Equal(900, PrimeLayoutMetrics.SimpleFormMaxWidth);
        Assert.Equal(1100, PrimeLayoutMetrics.ComplexFormMaxWidth);
    }

    [Fact]
    public void TypographyDeclaresCardHeadingWithoutUsingPageTitleScale()
    {
        Dictionary<string, XElement> typography = LoadElements(
            "src/Client.Presentation/Launcher/Theme/PrimeTypography.axaml", "Style",
            "Selector");

        Assert.Equal("32", typography["TextBlock.prime-title"]
            .Elements().Single(element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "FontSize")
            .Attribute("Value")?.Value);
        Assert.Equal("18", typography["TextBlock.prime-card-heading"]
            .Elements().Single(element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "FontSize")
            .Attribute("Value")?.Value);
        Assert.Equal("36", typography["TextBlock.prime-hero"]
            .Elements().Single(element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "FontSize")
            .Attribute("Value")?.Value);
    }

    [AvaloniaFact]
    public void PageHeadingUpdatesThroughExplicitPropertiesAndTrailingSlot()
    {
        var trailing = new PrimeStatusChip("Online", PrimeStatusKind.Success);
        var heading = new PrimePageHeading("Play", "ONLINE MULTIPLAYER",
            "Find a match.", trailing);

        Assert.Equal("Play", heading.Title);
        Assert.Equal("ONLINE MULTIPLAYER", heading.Kicker);
        Assert.Equal("Find a match.", heading.Subtitle);
        Assert.Same(trailing, heading.TrailingContent);

        heading.Title = "Maps";
        heading.Kicker = null;
        heading.Subtitle = "Browse installed maps.";
        heading.TrailingContent = null;

        Assert.Equal("Maps", heading.Title);
        Assert.Null(heading.Kicker);
        Assert.Equal("Browse installed maps.", heading.Subtitle);
        Assert.Null(heading.TrailingContent);
        Assert.Contains(heading.Children, child => child is TextBlock text
            && text.Classes.Contains("prime-title") && text.Text == "Maps");
    }

    [AvaloniaFact]
    public void TechFrameDecorationsAreStaticNonInteractiveAndAutomationHidden()
    {
        var frame = new PrimeTechFrame(new Border());

        Assert.Same(frame.Child, frame.Children[0]);
        Assert.NotEmpty(frame.Decorations);
        Assert.All(frame.Decorations, decoration =>
        {
            Assert.False(decoration.IsHitTestVisible);
            Assert.False(decoration.Focusable);
            Assert.Equal(AccessibilityView.Raw,
                AutomationProperties.GetAccessibilityView(decoration));
        });
    }

    [AvaloniaFact]
    public void StatRailOwnsACompactSetOfReusableStatTiles()
    {
        var first = new PrimeStatTile("ONLINE", "17");
        var second = new PrimeStatTile("LOBBIES", "3");
        var rail = new PrimeStatRail(new[] { first, second });

        Assert.Contains("prime-stat-rail", rail.Classes);
        Assert.Equal(new[] { first, second }, rail.Tiles);
        Assert.Equal(new[] { first, second }, rail.Children);
        Assert.Equal(8, rail.ItemSpacing);
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

    [AvaloniaFact]
    public void ReusableCardsEmptyStatesAndStatusChipsExposeSemanticState()
    {
        PrimeHeroCard hero = PrimeControlFactory.HeroCard(new TextBlock());
        PrimeActionCard action = PrimeControlFactory.ActionCard(new TextBlock(),
            selected: true);
        PrimeTechFrame frame = PrimeControlFactory.TechFrame(new TextBlock());
        PrimeStatRail rail = PrimeControlFactory.StatRail(new[]
        {
            ("STATUS", "READY"), ("PLAYERS", "6")
        });
        PrimeEmptyState empty = PrimeControlFactory.EmptyState("NO MATCHES",
            "Host a lobby to get started.",
            PrimeControlFactory.Button("Host lobby"));
        var status = new PrimeStatusChip("Online", PrimeStatusKind.Success);

        Assert.Contains("prime-hero-card", hero.Classes);
        Assert.True(action.IsSelected);
        Assert.NotNull(action.Transitions);
        Assert.Contains("prime-tech-frame", frame.Classes);
        Assert.Equal(2, rail.Tiles.Count);
        Assert.Contains("prime-stat-rail", rail.Classes);
        Assert.Equal("NO MATCHES", empty.Title);
        Assert.NotNull(empty.PrimaryAction);
        Assert.Equal("Online", AutomationProperties.GetItemStatus(status));
        Assert.Equal(AutomationLiveSetting.Polite,
            AutomationProperties.GetLiveSetting(status));
    }

    [AvaloniaFact]
    public void ProductionRoutesExposeSharedHeroAndActionSurfaces()
    {
        PrimeShellView play = Assert.IsType<PrimeShellView>(UiCapture.BuildFixture(
            "play-home", new MenuSettings(), Array.Empty<string>()));
        var playWindow = new Window { Width = 940, Height = 560, Content = play };
        try
        {
            playWindow.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(play.GetVisualDescendants().OfType<PrimeHeroCard>());
            Assert.NotEmpty(play.GetVisualDescendants().OfType<PrimeActionCard>());
            Assert.Contains(play.GetVisualDescendants().OfType<PrimeHeroCard>(),
                card => !String.IsNullOrWhiteSpace(AutomationProperties.GetName(card)));
            Assert.All(play.GetVisualDescendants().OfType<PrimeActionCard>(), card =>
                Assert.False(String.IsNullOrWhiteSpace(
                    AutomationProperties.GetName(card))));
        }
        finally
        {
            playWindow.Content = null;
            playWindow.Close();
            play.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        PrimeShellView rankings = Assert.IsType<PrimeShellView>(UiCapture.BuildFixture(
            "rankings-desktop", new MenuSettings(), Array.Empty<string>()));
        var rankingsWindow = new Window
        {
            Width = 1440,
            Height = 900,
            Content = rankings
        };
        try
        {
            rankingsWindow.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(rankings.GetVisualDescendants().OfType<PrimeHeroCard>());
        }
        finally
        {
            rankingsWindow.Content = null;
            rankingsWindow.Close();
            rankings.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void FocusAndSelectionRemainDistinctForTabs()
    {
        var tabs = new PrimeTabStrip(
        [
            new PrimeTabItem("Overview", true, () => { }),
            new PrimeTabItem("History", false, () => { })
        ]);
        var window = new Window { Width = 420, Height = 120, Content = tabs };
        try
        {
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            PrimeTabButton selected = tabs.SelectedTab;
            Assert.Contains("prime-selected", selected.Classes);
            Assert.Equal("Selected",
                AutomationProperties.GetItemStatus(selected));
            Assert.True(selected.Focus());
            Assert.True(selected.IsFocused);
            Assert.Contains("prime-selected", selected.Classes);
            Assert.Equal("Overview", AutomationProperties.GetName(selected));
            Assert.Equal("Not selected",
                AutomationProperties.GetItemStatus(tabs.Tabs[1]));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void PlayerFacingResourceCopyExcludesInternalTerminology()
    {
        XDocument resource = XDocument.Load(FindRepositoryFile(
            "src/Client.Presentation/Launcher/Resources/PrimeUiCopy.resx"));
        string copy = String.Join("\n", resource.Descendants()
            .Where(element => element.Name.LocalName == "value")
            .Select(element => element.Value));

        foreach (string forbidden in new[]
        {
            "Open Gateway",
            "authoritative session",
            "local builds are never replaced automatically",
            "exact region ID",
            "presentation only",
            "worker",
            "node directory"
        })
        {
            Assert.DoesNotContain(forbidden, copy,
                StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal("Quick Play", PrimeUiCopy.Play_QuickPlay_Title);
        Assert.Equal("Updates are unavailable for local builds.",
            PrimeUiCopy.Update_LocalBuild);
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
