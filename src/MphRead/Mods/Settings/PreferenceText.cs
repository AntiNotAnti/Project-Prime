using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Input;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Settings;

/// <summary>Pure text grammar shared by runtime loading and detached archive validation.</summary>
internal static class PreferenceText
{
    private static readonly HashSet<string> LauncherBool = Words("bright_skins host_on_master list_hosted window_maximized auto_update debug_logs reduce_motion combat_notifications_visible replay_auto_prune replay_delete_clips spectator_name_tags kill_cam final_kill_cam social_privacy_configured");
    private static readonly HashSet<string> LauncherInt = Words("prefs_schema player_outline_width master_port server_port last_role color bots bot_level host_port lobby_time_limit_seconds lobby_goal replay_storage_gb kill_cam_camera last_kind");
    private static readonly HashSet<string> ControlBool = Words("invert_y invert_x mouse_movement_boost mouse_alt_form_movement stylus_movement_boost pointer_jump_guard stylus_mode stylus_zone stylus_native_ui scroll_all_weapons gamepad_invert_y");
    private static readonly HashSet<string> ControlFloat = Words("sensitivity imperialist_zoom_sensitivity imperialist_zoom_amount alt_swipe_sensitivity stylus_native_ui_opacity stylus_zone_opacity stylus_zone_outline_opacity stylus_zone_button_opacity stylus_cursor_opacity gamepad_deadzone gamepad_look");
    private static readonly HashSet<string> ReplayKeys = Words("replay_play_pause replay_step_back replay_step_forward replay_seek_back replay_seek_forward replay_slower replay_faster replay_restart replay_camera_track replay_constant_speed replay_interpolation replay_easing");
    private static readonly Dictionary<string, string> PadOptions = DefaultPadOptions();
    private static HashSet<string> Words(string text) => new(text.Split(' '), StringComparer.Ordinal);
    private static Dictionary<string,string> DefaultPadOptions()
    {
        // The production writer enumerates every numeric, boolean and enum option,
        // including calibration/radial keys. New options automatically join the grammar.
        var lines = new List<string>(); new GamepadOptionState().Write(lines);
        return lines.ToDictionary(line => line[..line.IndexOf('=')], line => line[(line.IndexOf('=') + 1)..]);
    }
    internal static IReadOnlyDictionary<string,string> Parse(string text, bool launcher)
    {
        var values = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int split = line.IndexOf('=');
            if (split <= 0 || line.Any(char.IsControl)) throw new InvalidDataException("Invalid preference line.");
            string key = line[..split].Trim(), value = line[(split + 1)..].Trim();
            if (!IsValid(key, value, launcher)) throw new InvalidDataException("Invalid preference value: " + key);
            values[key] = value; // Production compatibility: final duplicate wins.
        }
        if (!launcher)
        {
            string[] lines = values.Select(pair => pair.Key + "=" + pair.Value).ToArray();
            // Exercise the normal detached controller migration and binding parsers.
            new GamepadOptionState().Load(lines);
            var bindings = new PadBindingState();
            foreach (var pair in values) bindings.TryLoad(pair.Key, pair.Value);
            bindings.LoadSlots(lines);
        }
        return values;
    }
    internal static bool IsValid(string key, string value, bool launcher)
    {
        if (launcher)
        {
            if (LauncherBool.Contains(key)) return bool.TryParse(value, out _);
            if (LauncherInt.Contains(key)) return Integer(value);
            return key switch
            {
                "combat_feedback_volume" => Number(value),
                "bright_skin_style" => Defined<PlayerSkinStyle>(value, true),
                "player_outline" => Defined<PlayerOutlineStyle>(value, true),
                "hunter" => Defined<Hunter>(value, true),
                "lobby_mode" => Defined<GameMode>(value, true),
                "social_presence_visibility" => Defined<MphRead.Mods.Launcher.SocialPresenceVisibility>(value, true),
                "social_activity_visibility" => Defined<MphRead.Mods.Launcher.SocialActivityVisibility>(value, true),
                "social_invite_policy" => Defined<MphRead.Mods.Launcher.SocialInvitePolicy>(value, true),
                "window_mode" => WindowMode.Parse(value, (WindowStartMode)(-1)) != (WindowStartMode)(-1),
                "window_size" or "window_pos" => Pair(value),
                "aim_trainer" => Trainer(value),
                _ => true // Retired/unknown keys remain forward/backward compatible.
            };
        }
        if (ControlBool.Contains(key)) return bool.TryParse(value, out _);
        if (ControlFloat.Contains(key)) return Number(value);
        if (key is "clip_postroll" or "clip_seconds") return Integer(value);
        if (key is "clip_key" or "chat_key" || ReplayKeys.Contains(key))
            return value.Equals("none", StringComparison.OrdinalIgnoreCase) || Defined<Keys>(value);
        if (key == "stylus_zone_rect") return Vector(value);
        if (key == "touch_buttons") return bool.TryParse(value, out _);
        if (key is "touch_button_scale" or "touch_stick_scale" or "touch_overlay_opacity") return Number(value);
        foreach ((TouchControl control, _) in TouchSettings.Order)
        {
            if (key == TouchSettings.SettingKey(control)) return bool.TryParse(value, out _);
            if (key == TouchSettings.LayoutKey(control)) return value.Equals("default", StringComparison.OrdinalIgnoreCase) || Vector(value);
        }
        if (key == "gamepad_preset") return new[] { "Default", "Bumper Jumper", "Southpaw", "Classic", "Custom" }.Contains(value);
        if (PadOptions.TryGetValue(key, out string? defaultValue))
        {
            if (key == "gamepad_curve") return Defined<GamepadCurve>(value);
            if (key == "gamepad_glyph_style") return Defined<GamepadFamily>(value);
            if (key == "gamepad_binding_modifier") return Buttons(value, single: true);
            if (key == "gamepad_wheel_order")
            {
                var parts = value.Split(',');
                return parts.Length == 6 && parts.All(Integer) && parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).Order().SequenceEqual(Enumerable.Range(0,6));
            }
            return bool.TryParse(defaultValue, out _) ? bool.TryParse(value, out _) : Number(value);
        }
        if (key.StartsWith("pad_", StringComparison.Ordinal))
        {
            string action = key[4..]; bool single = false;
            foreach (string suffix in new[] { "_primary_modifier", "_secondary_modifier", "_primary", "_secondary" })
                if (action.EndsWith(suffix, StringComparison.Ordinal)) { action = action[..^suffix.Length]; single = true; break; }
            if (Defined<PadAction>(action)) return Buttons(value, single);
        }
        if (InputSettings.Bindings.Any(property => property.Name == key))
        {
            string[] parts = value.Split(':', 2);
            string type = parts[0].Trim(), name = parts.Length == 2 ? parts[1].Trim() : "";
            return type switch { "ScrollUp" or "ScrollDown" => parts.Length == 1,
                "Key" => Defined<Keys>(name), "Mouse" => Defined<MouseButton>(name), _ => false };
        }
        return true;
    }
    private static bool Trainer(string value)
    {
        try { JsonSerializer.Deserialize<Training.AimTrainerDefinition>(value).Sanitize(); return true; }
        catch (JsonException) { return false; }
    }
    private static bool Buttons(string value, bool single)
        => Enum.TryParse<GamepadButtons>(value, out var buttons) && ((int)buttons & ~0xffff) == 0
            && (!single || ((int)buttons & ((int)buttons - 1)) == 0);
    private static bool Defined<T>(string value, bool ignoreCase = false) where T : struct, Enum
        => Enum.TryParse<T>(value, ignoreCase, out var parsed) && Enum.IsDefined(parsed);
    private static bool Integer(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    private static bool Number(string value) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) && float.IsFinite(number);
    private static bool Vector(string value) { string[] parts = value.Split(','); return parts.Length == 3 && parts.All(Number); }
    private static bool Pair(string value)
    {
        int separator = value.IndexOfAny(new[] { 'x', 'X', ',' });
        return separator > 0 && Integer(value[..separator]) && Integer(value[(separator + 1)..]);
    }
}
