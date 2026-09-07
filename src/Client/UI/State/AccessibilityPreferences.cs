using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using MphRead.Mods.Launcher;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.State;

public sealed class AccessibilityPreferences : INotifyPropertyChanged
{
    private const string FileName = "ui-accessibility.txt";
    private double _uiScale = 1;
    private bool _largeText;
    private bool _reducedMotion;
    private bool _holdActions = true;
    private UiColorVisionMode _colorVisionMode;
    private double _safeArea = UiMetrics.SafeArea;

    public event PropertyChangedEventHandler? PropertyChanged;

    public double UiScale
    {
        get => _uiScale;
        set => Set(ref _uiScale, Math.Clamp(value, 0.8, 1.5));
    }

    public bool LargeText
    {
        get => _largeText;
        set => Set(ref _largeText, value);
    }

    public bool ReducedMotion
    {
        get => _reducedMotion;
        set => Set(ref _reducedMotion, value);
    }

    /// <summary>True uses press-and-hold actions; false uses toggle behavior.</summary>
    public bool HoldActions
    {
        get => _holdActions;
        set => Set(ref _holdActions, value);
    }

    public UiColorVisionMode ColorVisionMode
    {
        get => _colorVisionMode;
        set => Set(ref _colorVisionMode, value);
    }

    public double SafeArea
    {
        get => _safeArea;
        set => Set(ref _safeArea, Math.Clamp(value, 0, 64));
    }

    public double TextSize(double baseSize) => UiTypography.Scale(baseSize, UiScale, LargeText);

    public static AccessibilityPreferences Load()
    {
        var preferences = new AccessibilityPreferences();
        try
        {
            string path = Path.Combine(LauncherPrefs.Directory, FileName);
            if (!File.Exists(path)) return preferences;
            foreach (string line in File.ReadLines(path))
            {
                string[] pair = line.Split('=', 2);
                if (pair.Length != 2) continue;
                if (pair[0] == "ui_scale" && Double.TryParse(pair[1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double scale)) preferences.UiScale = scale;
                else if (pair[0] == "large_text" && Boolean.TryParse(pair[1], out bool large))
                    preferences.LargeText = large;
                else if (pair[0] == "reduced_motion" && Boolean.TryParse(pair[1], out bool reduced))
                    preferences.ReducedMotion = reduced;
                else if (pair[0] == "safe_area" && Double.TryParse(pair[1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double safe)) preferences.SafeArea = safe;
            }
        }
        catch (Exception) { }
        return preferences;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(LauncherPrefs.Directory);
            File.WriteAllLines(Path.Combine(LauncherPrefs.Directory, FileName),
            [
                $"ui_scale={UiScale.ToString(CultureInfo.InvariantCulture)}",
                $"large_text={LargeText.ToString().ToLowerInvariant()}",
                $"reduced_motion={ReducedMotion.ToString().ToLowerInvariant()}",
                $"safe_area={SafeArea.ToString(CultureInfo.InvariantCulture)}"
            ]);
        }
        catch (Exception) { }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
