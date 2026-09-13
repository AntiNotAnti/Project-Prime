using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Theme;

/// <summary>Semantic roles shared by the Prime launcher resource dictionary.</summary>
public enum PrimeSemanticColor
{
    Accent,
    Info,
    Warm,
    Success,
    Warning,
    Error,
    Destructive,
    Focus,
    Brand,
    BrandStrong,
    BrandSurface,
    BrandEdge,
    OnBrand,
    Tech,
    TechStrong,
    Gunmetal
}

/// <summary>
/// Names for the shared Prime resources. Keeping the key vocabulary in one
/// place lets code-native controls and XAML use the same visual contract.
/// </summary>
public static class PrimeVisualTokens
{
    public const string BackgroundBrush = "PrimeBackgroundBrush";
    public const string HeaderBrush = "PrimeHeaderBrush";
    public const string FooterBrush = "PrimeFooterBrush";
    public const string SurfaceBrush = "PrimeSurfaceBrush";
    public const string PrimarySurfaceBrush = "PrimePrimarySurfaceBrush";
    public const string RaisedSurfaceBrush = "PrimeSurfaceRaisedBrush";
    public const string SurfaceMutedBrush = "PrimeSurfaceMutedBrush";
    public const string SecondarySurfaceBrush = "PrimeSecondarySurfaceBrush";
    public const string ControlBrush = "PrimeControlBrush";
    public const string InteractiveSurfaceBrush = "PrimeInteractiveSurfaceBrush";
    public const string SelectedBrush = "PrimeSelectedBrush";
    public const string SelectedSurfaceBrush = "PrimeSelectedSurfaceBrush";
    public const string NavSelectedBrush = "PrimeNavSelectedBrush";
    public const string ModalSurfaceBrush = "PrimeModalSurfaceBrush";
    public const string PanelBrush = "PrimePanelBrush";
    public const string PanelRaisedBrush = "PrimePanelRaisedBrush";
    public const string FocusSurfaceBrush = "PrimeFocusSurfaceBrush";

    public const string BrandBrush = "PrimeBrandBrush";
    public const string BrandStrongBrush = "PrimeBrandStrongBrush";
    public const string BrandSurfaceBrush = "PrimeBrandSurfaceBrush";
    public const string BrandEdgeBrush = "PrimeBrandEdgeBrush";
    public const string OnBrandBrush = "PrimeOnBrandBrush";
    public const string TechBrush = "PrimeTechBrush";
    public const string TechStrongBrush = "PrimeTechStrongBrush";
    public const string GunmetalBrush = "PrimeGunmetalBrush";

    /// <summary>Compatibility alias for older views; new code uses Brand or Tech.</summary>
    public const string AccentBrush = "PrimeAccentBrush";
    public const string InfoBrush = "PrimeInfoBrush";
    public const string WarmBrush = "PrimeWarmBrush";
    public const string SuccessBrush = "PrimeSuccessBrush";
    public const string WarningBrush = "PrimeWarningBrush";
    public const string ErrorBrush = "PrimeErrorBrush";
    public const string DestructiveBrush = "PrimeDestructiveBrush";
    public const string FocusBrush = "PrimeFocusBrush";

    public const string TextBrush = "PrimeTextBrush";
    public const string MutedTextBrush = "PrimeTextMutedBrush";
    public const string DimTextBrush = "PrimeTextDimBrush";
    public const string EdgeBrush = "PrimeEdgeBrush";
    public const string StrongEdgeBrush = "PrimeEdgeStrongBrush";
    public const string DividerBrush = "PrimeDividerBrush";
    public const string DotBrush = "PrimeDotBrush";
    public const string ScrimBrush = "PrimeScrimBrush";

    public static IReadOnlyList<string> HierarchyBrushResourceKeys { get; } =
        new[]
        {
            BackgroundBrush,
            HeaderBrush,
            FooterBrush,
            SurfaceBrush,
            PrimarySurfaceBrush,
            RaisedSurfaceBrush,
            SurfaceMutedBrush,
            SecondarySurfaceBrush,
            ControlBrush,
            InteractiveSurfaceBrush,
            SelectedBrush,
            SelectedSurfaceBrush,
            NavSelectedBrush,
            ModalSurfaceBrush,
            PanelBrush,
            PanelRaisedBrush,
            FocusSurfaceBrush,
            DividerBrush,
            EdgeBrush,
            StrongEdgeBrush
        };

    public static IReadOnlyList<string> SemanticBrushResourceKeys { get; } =
        new[]
        {
            BrandBrush,
            BrandStrongBrush,
            BrandSurfaceBrush,
            BrandEdgeBrush,
            OnBrandBrush,
            TechBrush,
            TechStrongBrush,
            GunmetalBrush,
            AccentBrush,
            InfoBrush,
            WarmBrush,
            SuccessBrush,
            WarningBrush,
            ErrorBrush,
            DestructiveBrush,
            FocusBrush
        };

    public static string BrushKey(PrimeSemanticColor color)
        => color switch
        {
            PrimeSemanticColor.Brand => BrandBrush,
            PrimeSemanticColor.BrandStrong => BrandStrongBrush,
            PrimeSemanticColor.BrandSurface => BrandSurfaceBrush,
            PrimeSemanticColor.BrandEdge => BrandEdgeBrush,
            PrimeSemanticColor.OnBrand => OnBrandBrush,
            PrimeSemanticColor.Tech => TechBrush,
            PrimeSemanticColor.TechStrong => TechStrongBrush,
            PrimeSemanticColor.Gunmetal => GunmetalBrush,
            PrimeSemanticColor.Accent => AccentBrush,
            PrimeSemanticColor.Info => InfoBrush,
            PrimeSemanticColor.Warm => WarmBrush,
            PrimeSemanticColor.Success => SuccessBrush,
            PrimeSemanticColor.Warning => WarningBrush,
            PrimeSemanticColor.Error => ErrorBrush,
            PrimeSemanticColor.Destructive => DestructiveBrush,
            PrimeSemanticColor.Focus => FocusBrush,
            _ => throw new ArgumentOutOfRangeException(nameof(color))
        };
}
