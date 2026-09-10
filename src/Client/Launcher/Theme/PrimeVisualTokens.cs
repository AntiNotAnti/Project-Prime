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
    Focus
}

/// <summary>
/// Names for the shared Prime resources. Keeping the key vocabulary in one
/// place lets code-native controls and XAML use the same visual contract.
/// </summary>
public static class PrimeVisualTokens
{
    public const string BackgroundBrush = "PrimeBackgroundBrush";
    public const string PrimarySurfaceBrush = "PrimePrimarySurfaceBrush";
    public const string RaisedSurfaceBrush = "PrimeSurfaceRaisedBrush";
    public const string SecondarySurfaceBrush = "PrimeSecondarySurfaceBrush";
    public const string InteractiveSurfaceBrush = "PrimeInteractiveSurfaceBrush";
    public const string SelectedSurfaceBrush = "PrimeSelectedSurfaceBrush";
    public const string ModalSurfaceBrush = "PrimeModalSurfaceBrush";
    public const string PanelBrush = "PrimePanelBrush";

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

    public static IReadOnlyList<string> HierarchyBrushResourceKeys { get; } =
        new[]
        {
            BackgroundBrush,
            PrimarySurfaceBrush,
            RaisedSurfaceBrush,
            SecondarySurfaceBrush,
            InteractiveSurfaceBrush,
            SelectedSurfaceBrush,
            ModalSurfaceBrush,
            PanelBrush
        };

    public static IReadOnlyList<string> SemanticBrushResourceKeys { get; } =
        new[]
        {
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
