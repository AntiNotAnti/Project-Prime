using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MphRead.Mods.Render.Hud;

public sealed class HudProfileStore
{
    public const int MaximumBytes = 128 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        MaxDepth = 24, Converters = { new JsonStringEnumConverter() }
    };
    public string DirectoryPath { get; }
    public HudProfileStore(string directory) => DirectoryPath = directory;
    public static string Serialize(HudProfile profile) => JsonSerializer.Serialize(profile, Options);
    public static HudProfile Parse(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new FormatException("HUD profile exceeds 128 KiB.");
        var input = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 24 }) as JsonObject
            ?? throw new FormatException("Expected a HUD profile object.");
        // Merge recursively so partially specified elements inherit anchor/position as well as scale.
        string presetName = "Project Prime";
        if (input["basePreset"] is JsonValue value && value.TryGetValue<string>(out string? preset)
            && Array.IndexOf(HudProfileDefaults.Presets, preset) >= 0) presetName = preset;
        JsonObject baseline = JsonSerializer.SerializeToNode(HudProfileDefaults.Create(presetName), Options)!.AsObject();
        Merge(baseline, input);
        var profile = baseline.Deserialize<HudProfile>(Options) ?? throw new FormatException("Empty HUD profile.");
        profile.Validate();
        return profile;
    }
    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var pair in source)
        {
            if (target[pair.Key] is JsonObject child && pair.Value is JsonObject incoming) Merge(child, incoming);
            else target[pair.Key] = pair.Value?.DeepClone();
        }
    }
    private string PathFor(string name)
    {
        // Names are display labels, never paths, on every platform (including Unix).
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name is "." or ".."
            || name.IndexOfAny("/\\:*?\"<>|".ToCharArray()) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Use a profile name without path or filename characters.", nameof(name));
        foreach (char c in name) if (char.IsControl(c)) throw new ArgumentException("Profile names cannot contain control characters.", nameof(name));
        if (name.EndsWith('.') || name.EndsWith(' ')) throw new ArgumentException("Profile names cannot end with a dot or space.", nameof(name));
        string stem=name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length==4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9')
            throw new ArgumentException("This profile name is reserved by Windows.",nameof(name));
        return Path.Combine(DirectoryPath, name + ".json");
    }
    public HudProfile Load(string name)
    {
        string path = PathFor(name);
        try { return Read(path); }
        catch (Exception e) when (Recoverable(e)) { return Read(path + ".bak"); }
    }
    private static HudProfile Read(string path)
    {
        if (new FileInfo(path).Length > MaximumBytes) throw new FormatException("HUD profile exceeds 128 KiB.");
        return Parse(File.ReadAllText(path));
    }
    internal static bool Recoverable(Exception e) => e is IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException;
    public void Save(string name, HudProfile profile)
    {
        string path = PathFor(name);
        // Validate the detached serialized value before touching the last good file.
        string json = Serialize(Parse(Serialize(profile)));
        Directory.CreateDirectory(DirectoryPath);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(json); file.Write(data); file.Flush(true);
            }
            if (File.Exists(path))
            {
                // A broken active file must never overwrite a good recovery copy.
                try { Read(path); File.Copy(path, path + ".bak", true); }
                catch (Exception e) when (Recoverable(e)) { }
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
