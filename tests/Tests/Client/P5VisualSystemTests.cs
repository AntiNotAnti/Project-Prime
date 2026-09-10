using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
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
}
