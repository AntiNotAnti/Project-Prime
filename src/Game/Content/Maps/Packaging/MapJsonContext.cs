using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead.Mods.MapGen;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = false,
    AllowTrailingCommas = false,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MapManifest))]
[JsonSerializable(typeof(MapProject))]
[JsonSerializable(typeof(MapEnvironment))]
[JsonSerializable(typeof(MapAuthoringScene))]
[JsonSerializable(typeof(MapBuildMetadata))]
[JsonSerializable(typeof(MapDefinition))]
public sealed partial class MapJsonContext : JsonSerializerContext
{
}
