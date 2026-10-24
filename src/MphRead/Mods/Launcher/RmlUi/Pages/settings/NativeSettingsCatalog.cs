#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Input;
namespace MphRead.Mods.Launcher.RmlUi.Pages.Settings;

// Presentation metadata only. The controller's canonical groups also define
// binding conflict domains and must remain stable when a control changes tabs.
internal static class NativeSettingsCatalog
{
    internal const int TabSlots = 8;
    private static readonly string[][] CategoryTabs =
    {
        new[] { "View", "Accessibility", "Campaign" },
        new[] { "Quality", "Textures", "Lighting", "Cosmetics" },
        new[] { "Mixer", "Notifications" },
        new[] { "Aim", "Movement", "Weapons", "Actions", "Interface", "Controller", "Touch", "Stylus" },
        new[] { "Recording", "Playback", "Keyboard", "Controller" },
        new[] { "Identity", "Servers", "Local assets" }
    };
    internal static string[] Tabs(int category) => (uint)category < CategoryTabs.Length ? CategoryTabs[category] : Array.Empty<string>();

    internal static string Summary(int category) => category switch
    {
        0 => "Window, field of view, weapon presentation, frame rate and campaign preferences",
        1 => "Quality presets, textures, lighting and cosmetic effects",
        2 => "Volume, language and combat notification sounds",
        3 => "Aim, movement, weapons, actions and input devices",
        4 => "Clip recording, storage, playback and replay bindings",
        5 => "Player identity, servers and local source assets",
        6 => "Updates, diagnostics and settings backups",
        7 => "Verify installation and clean generated caches",
        _ => "Contributors and project acknowledgements"
    };

    // The visual editors are the single UI for these exact same values.
    // The canonical rows remain available to persistence, imports and search.
    internal static bool HasVisualEditor(NativeSettingRow row) => row.Group is "Outline colors" or "Touch layout"
        || row.Group == "Stylus guide" && row.Label == "Guide position / width";

    internal static int Tab(NativeSettingRow row) => row.Category switch
    {
        "Display" => row.Group switch { "Accessibility" or "Outline colors" => 1, "Campaign map" or "Campaign ending" => 2, _ => 0 },
        "Graphics" => row.Group switch { "Textures" => 1, "Lighting and atmosphere" or "Cel shading" => 2, "Cosmetics" => 3, _ => 0 },
        "Audio" => row.Group is "Mixer" or "General" ? 0 : 1,
        "Controls" => ControlTab(row),
        "Replays" => row.Group switch { "Playback" => 1, "Playback keys" => 2, "Controller bindings" => 3, _ => 0 },
        "Profile" => row.Group == "Servers" ? 1 : 0,
        _ => 0
    };

    private static int ControlTab(NativeSettingRow row)
    {
        if (row.BindingAction is {} action)
            return action switch
            {
                PadAction.Jump or PadAction.Crouch or PadAction.Morph => 1,
                PadAction.Scan or PadAction.ScanVisor => 3,
                PadAction.Menu or PadAction.Chat or PadAction.Scoreboard or PadAction.SaveClip => 4,
                _ => 2
            };
        if (row.Group is "Controller" or "Controller calibration")
            return row.Label.StartsWith("Wheel ", StringComparison.Ordinal) ? 2 : 5;
        if (row.Group == "Weapon wheel") return 2;
        if (row.Group is "Touch buttons" or "Touch layout" or "Touch overlay") return 6;
        if (row.Group is "Stylus" or "Stylus guide") return 7;
        if (row.Group == "Custom scope (all aim devices)") return 0;
        if (row.Group == "Keyboard bindings")
        {
            if (row.Label.StartsWith("Move ", StringComparison.Ordinal)
                || row.Label.StartsWith("Roll ", StringComparison.Ordinal)
                || row.Label is "Jump" or "Crouch" or "Boost" or "Morph") return 1;
            if (row.Label.StartsWith("Aim ", StringComparison.Ordinal)) return 0;
            if (row.Label is "Scan" or "Scan visor") return 3;
            if (row.Label is "Scoreboard" or "Hud overlay") return 4;
            return 2;
        }
        return row.Label switch
        {
            "Chat Key" or "Clip Key" => 4,
            "Mouse Movement Boost" or "Mouse Alt Form Movement" or "Alt Swipe Sensitivity" => 1,
            "Classic Morph Ball Touch" => 6,
            "Stylus Movement Boost" => 7,
            "Scroll All Weapons" => 2,
            _ => 0
        };
    }

    internal static string Group(NativeSettingRow row) => row.Group switch
    {
        "Keyboard bindings" => "Keyboard and mouse bindings",
        "Controller bindings" => "Controller bindings",
        "Playback keys" => "Replay keyboard bindings",
        "Mouse and keyboard" when row.IsBinding => "Keyboard shortcuts",
        "Mouse and keyboard" => Tab(row) switch { 1 => "Movement behavior", 2 => "Weapon selection", 6 => "Touch movement", 7 => "Stylus movement", _ => "Mouse aiming" },
        "Controller" when Tab(row) == 2 => "Controller weapon wheel",
        _ => row.Group
    };
}
#endif
