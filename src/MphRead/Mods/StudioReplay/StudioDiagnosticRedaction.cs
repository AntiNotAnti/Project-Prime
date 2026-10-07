using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MphRead.Mods.StudioReplay;

/// <summary>Sanitizes only an explicitly allowlisted diagnostic snapshot, never reads credentials, environment or log files.</summary>
internal static class StudioDiagnosticRedaction
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    internal static string? Redact(string? value)
    {
        if (value == null) return null;
        if (value.Length > 4096) value = value[..4096] + "…";
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home)) value = value.Replace(home, "~", StringComparison.Ordinal);
        value = Regex.Replace(value, @"(?i)(https?://)[^/\s@]+@", "$1[redacted]@", RegexOptions.None, RegexTimeout);
        value = Regex.Replace(value, @"\b[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b", "[redacted]", RegexOptions.None, RegexTimeout);
        return Regex.Replace(value, @"(?i)(bearer\s+|(?:access[_-]?token|refresh[_-]?token|authorization|api[_-]?key|password|secret|credential|token)\s*[:=]\s*)(?:\""[^\""\r\n]*\""|'[^'\r\n]*'|[^\s,;&]+)",
            "$1[redacted]", RegexOptions.None, RegexTimeout);
    }
    internal static string Serialize(object data)
    {
        var options = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
        JsonNode node = JsonSerializer.SerializeToNode(data, options)!;
        Sanitize(node);
        return node.ToJsonString(options);
    }
    private static void Sanitize(JsonNode node)
    {
        if (node is JsonObject map)
        {
            foreach (string name in map.Select(p => p.Key).ToArray())
            {
                if (Regex.IsMatch(name, @"(?i)^(?:access[_-]?token|refresh[_-]?token|authorization|api[_-]?key|password|secret|credential|token)$",
                    RegexOptions.None, RegexTimeout)) map[name] = "[redacted]";
                else map[name] = Value(map[name]);
            }
        }
        else if (node is JsonArray values)
            for (int i = 0; i < values.Count; i++) values[i] = Value(values[i]);
    }
    private static JsonNode? Value(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out string? text)) return JsonValue.Create(Redact(text));
        if (node != null) Sanitize(node);
        return node;
    }
}
