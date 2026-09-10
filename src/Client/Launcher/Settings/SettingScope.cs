namespace MphRead.Mods.Launcher.Settings;

/// <summary>
/// The logical owner of a setting. This is deliberately not a flags enum: a
/// persisted value must have one owner and one authority boundary.
/// </summary>
public enum SettingScope
{
    ClientPreference,
    AccountPreference,
    MatchHostRule,
    PlatformPreference,
    Diagnostics,
    ServerOperator,
    Internal
}
