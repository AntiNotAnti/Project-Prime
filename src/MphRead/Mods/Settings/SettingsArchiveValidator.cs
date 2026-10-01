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
    internal static void ValidatePreferences(string text, bool launcher)
    {
        PreferenceText.Parse(text, launcher);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim(); if (line.Length == 0 || line.StartsWith('#')) continue;
            int split = line.IndexOf('=');
            string key = line[..split].Trim(), value = line[(split+1)..].Trim();
            CheckKey(key);
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                && !double.IsFinite(number)) throw new InvalidDataException("Non-finite preference: " + key);
            if (launcher && key == "aim_trainer")
            { using var json = JsonDocument.Parse(value); CheckJson(json.RootElement); }
        }
    }
    internal static void ValidateMappings(string text)
    {
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.Length > 4096 || line.Any(char.IsControl)) throw new InvalidDataException("Invalid controller mapping.");
            // Reuse the runtime's accepted GUID/platform mapping grammar without installing it.
            Input.GamepadMappings.ValidateMapping(line);
            if (!line.Split(',')[0].All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid controller GUID.");
        }
    }
}
