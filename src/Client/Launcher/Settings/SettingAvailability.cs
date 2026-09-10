namespace MphRead.Mods.Launcher.Settings;

/// <summary>Whether a setting can be changed on the current build/device.</summary>
public enum SettingAvailability
{
    Available,
    /// <summary>Compatibility spelling for metadata consumers.</summary>
    Supported = Available,
    Unsupported,
    Unavailable
}
