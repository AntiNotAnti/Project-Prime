using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.Settings;

internal static class SettingsArchiveValidator
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static string Validate(SettingsArchiveStore store, byte[] bytes)
    {
        if (bytes.Length > store.MaximumBytes) throw new InvalidDataException("Settings store is too large: " + store.Path);
        string text = Utf8.GetString(bytes);
        if (text.StartsWith('\uFEFF')) text = text[1..];
        if (store.Path.EndsWith(".json", StringComparison.Ordinal))
        {
            using var json = JsonDocument.Parse(text);
            CheckJson(json.RootElement);
        }
        store.Validate(text);
        return text;
    }
    private static void CheckJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new InvalidDataException("Duplicate settings key: " + property.Name);
                CheckKey(property.Name); CheckJson(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckJson(item);
    }
    private static void CheckKey(string key)
    {
        string lower = key.ToLowerInvariant().Replace("_", "").Replace("-", "");
        if (new[] { "accesstoken", "refreshtoken", "password", "credential", "secret", "authstate" }.Any(lower.Contains))
            throw new InvalidDataException("Credential fields cannot be archived.");
    }
    internal static void ValidatePreferences(string text)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int split = line.IndexOf('=');
            if (split <= 0 || line.Any(char.IsControl)) throw new InvalidDataException("Invalid preference line.");
            string key = line[..split].Trim(), value = line[(split + 1)..].Trim();
            CheckKey(key);
            // Existing controls writers repeat a few compatibility keys; normal loading is last-wins.
            seen.Add(key);
            ValidateKnownPreference(key, value);
            // The legacy loaders intentionally ignore retired/unknown keys and normalize ranges.
            // Preserve that compatibility, but never accept non-finite numeric state.
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                && !double.IsFinite(number)) throw new InvalidDataException("Non-finite preference: " + key);
            if (key == "aim_trainer") JsonSerializer.Deserialize<Training.AimTrainerDefinition>(value).Sanitize();
        }
    }
    private static void ValidateKnownPreference(string key, string value)
    {
        const string booleans = " invert_y invert_x mouse_movement_boost mouse_alt_form_movement stylus_movement_boost scroll_all_weapons stylus_mode pointer_jump_guard stylus_zone stylus_native_ui gamepad_invert_y list_hosted host_on_master auto_update debug_logs bright_skins reduce_motion combat_notifications_visible replay_auto_prune replay_delete_clips spectator_name_tags kill_cam final_kill_cam window_maximized touch_buttons ";
        const string numbers = " sensitivity imperialist_zoom_sensitivity imperialist_zoom_amount alt_swipe_sensitivity stylus_native_ui_opacity stylus_cursor_opacity stylus_zone_outline_opacity stylus_zone_button_opacity stylus_zone_opacity clip_seconds clip_postroll gamepad_deadzone gamepad_look touch_button_scale touch_stick_scale touch_overlay_opacity prefs_schema server_port master_port last_role color bots bot_level host_port lobby_time_limit_seconds lobby_goal last_kind player_outline_width combat_feedback_volume replay_storage_gb kill_cam_camera ";
        bool valid = true;
        if (booleans.Contains(" " + key + " ", StringComparison.Ordinal)) valid = bool.TryParse(value, out _);
        else if (numbers.Contains(" " + key + " ", StringComparison.Ordinal)) valid = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) && float.IsFinite(number);
        else if (key == "stylus_zone_rect") valid = Vector(value, ',');
        else if (key.StartsWith("touch_layout_", StringComparison.Ordinal)) valid = value.Equals("default", StringComparison.OrdinalIgnoreCase) || Vector(value, ',');
        else if (key == "hunter") valid = Enum.TryParse<Hunter>(value, true, out var hunter) && Enum.IsDefined(hunter);
        else if (key == "lobby_mode") valid = Enum.TryParse<GameMode>(value, true, out var mode) && Enum.IsDefined(mode);
        if (!valid) throw new InvalidDataException("Invalid preference value: " + key);
    }
    private static bool Vector(string value, char separator)
    {
        string[] parts = value.Split(separator);
        return parts.Length == 3 && parts.All(p => float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) && float.IsFinite(number));
    }
    internal static void ValidateMappings(string text)
    {
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.Length > 4096 || line.Any(char.IsControl)) throw new InvalidDataException("Invalid controller mapping.");
            // Reuse the runtime's accepted GUID/platform mapping grammar without installing it.
            Input.GamepadMappings.ReplaceOverride("", line);
            if (!line.Split(',')[0].All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid controller GUID.");
        }
    }
}
