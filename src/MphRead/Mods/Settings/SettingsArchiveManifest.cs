using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead.Mods.Settings;

internal sealed record SettingsArchiveFile(string Path, string Category);
internal sealed record SettingsArchiveManifest(int Format, string Product, string CreatedByVersion,
    List<SettingsArchiveFile> Files)
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        WriteIndented = true
    };
}
