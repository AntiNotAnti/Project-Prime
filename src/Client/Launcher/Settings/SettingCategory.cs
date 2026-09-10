namespace MphRead.Mods.Launcher.Settings;

/// <summary>Player-facing settings pages.</summary>
public enum SettingCategory
{
    Player,
    /// <summary>Compatibility spelling for metadata consumers.</summary>
    Gameplay = Player,
    Graphics,
    Audio,
    Controls,
    Hud,
    /// <summary>Compatibility spelling for metadata consumers.</summary>
    HUD = Hud,
    Network,
    Accessibility,
    System,
    About
}
