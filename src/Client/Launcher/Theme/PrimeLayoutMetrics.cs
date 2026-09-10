using System;
using Avalonia;

namespace MphRead.Mods.Launcher.Theme;

/// <summary>Touch sizing values in device-independent pixels.</summary>
public static class PrimeTouchTargets
{
    public const double MinimumDip = 44;
    public const double PreferredPrimaryMinimumDip = 48;
    public const double PreferredPrimaryMaximumDip = 52;
    public const double MinimumSpacingDip = 8;

    public static double EnsureMinimum(double value)
    {
        Validate(value);
        return Math.Max(MinimumDip, value);
    }

    public static double EnsurePrimary(double value)
    {
        Validate(value);
        return Math.Clamp(value, PreferredPrimaryMinimumDip,
            PreferredPrimaryMaximumDip);
    }

    private static void Validate(double value)
    {
        if (Double.IsNaN(value) || Double.IsInfinity(value) || value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}

/// <summary>
/// Conservative fallback insets plus a merge helper for Android system bars,
/// display cutouts, and gesture navigation. Values are DIP, not pixels.
/// </summary>
public static class PrimeSafeArea
{
    public const double MinimumHorizontalDip = 16;
    public const double MinimumTopDip = 24;
    public const double MinimumBottomDip = 24;

    public static Thickness Resolve(Thickness systemInsets)
    {
        Validate(systemInsets, nameof(systemInsets));
        return new Thickness(
            Math.Max(MinimumHorizontalDip, systemInsets.Left),
            Math.Max(MinimumTopDip, systemInsets.Top),
            Math.Max(MinimumHorizontalDip, systemInsets.Right),
            Math.Max(MinimumBottomDip, systemInsets.Bottom));
    }

    private static void Validate(Thickness value, string parameterName)
    {
        if (Double.IsNaN(value.Left) || Double.IsNaN(value.Top)
            || Double.IsNaN(value.Right) || Double.IsNaN(value.Bottom)
            || Double.IsInfinity(value.Left) || Double.IsInfinity(value.Top)
            || Double.IsInfinity(value.Right) || Double.IsInfinity(value.Bottom)
            || value.Left < 0 || value.Top < 0 || value.Right < 0
            || value.Bottom < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName,
                "System insets must be finite and non-negative.");
        }
    }
}

/// <summary>Single facade for later Android shell layout code.</summary>
public static class PrimeLayoutMetrics
{
    public const double MinimumTouchTargetDip = PrimeTouchTargets.MinimumDip;
    public const double PreferredPrimaryActionMinimumDip =
        PrimeTouchTargets.PreferredPrimaryMinimumDip;
    public const double PreferredPrimaryActionMaximumDip =
        PrimeTouchTargets.PreferredPrimaryMaximumDip;
    public const double SafeAreaMinimumHorizontalDip =
        PrimeSafeArea.MinimumHorizontalDip;
    public const double SafeAreaMinimumTopDip = PrimeSafeArea.MinimumTopDip;
    public const double SafeAreaMinimumBottomDip = PrimeSafeArea.MinimumBottomDip;

    public static Thickness ResolveSafeArea(Thickness systemInsets)
        => PrimeSafeArea.Resolve(systemInsets);
}
