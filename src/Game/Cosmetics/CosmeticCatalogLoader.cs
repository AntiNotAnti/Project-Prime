using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead.Cosmetics;

public sealed record CosmeticCatalogLoadResult(
    CosmeticCatalog Catalog,
    IReadOnlyList<CosmeticValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

/// <summary>
/// Strict, bounded reader for a data-only cosmetic directory. Catalog entries
/// are relative manifest paths; manifests cannot escape the supplied root.
/// Invalid entries are omitted so optional presentation content fails soft.
/// </summary>
public static class CosmeticCatalogLoader
{
    public const string CatalogFileName = "catalog.json";

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter() }
    };

    public static CosmeticCatalogLoadResult Load(string packRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packRoot);
        string root = Path.GetFullPath(packRoot);
        var issues = new List<CosmeticValidationIssue>();
        if (!TryRead(root, CatalogFileName, CosmeticPackageLimits.MaximumCatalogBytes,
            CosmeticValidationIssueKind.MissingCatalog, issues, out byte[] catalogBytes))
            return new CosmeticCatalogLoadResult(CosmeticCatalog.Empty, issues);

        CatalogManifest? manifest;
        try
        {
            RejectDuplicateProperties(catalogBytes);
            manifest = JsonSerializer.Deserialize<CatalogManifest>(catalogBytes, _json);
        }
        catch (JsonException error)
        {
            issues.Add(new(CosmeticValidationIssueKind.MalformedManifest,
                $"Malformed {CatalogFileName}: {error.Message}", Path: CatalogFileName));
            return new CosmeticCatalogLoadResult(CosmeticCatalog.Empty, issues);
        }
        if (manifest == null || manifest.Format != CosmeticCatalog.CurrentVersion)
        {
            issues.Add(new(CosmeticValidationIssueKind.UnsupportedFormat,
                $"{CatalogFileName} must use format {CosmeticCatalog.CurrentVersion}.", Path: CatalogFileName));
            return new CosmeticCatalogLoadResult(CosmeticCatalog.Empty, issues);
        }
        string[] skins = manifest.Skins ?? [];
        string[] armor = manifest.ArmorEffects ?? [];
        string[] deaths = manifest.DeathEffects ?? [];
        if (skins.Length > CosmeticPackageLimits.MaximumEntriesPerKind
            || armor.Length > CosmeticPackageLimits.MaximumEntriesPerKind
            || deaths.Length > CosmeticPackageLimits.MaximumEntriesPerKind)
        {
            issues.Add(new(CosmeticValidationIssueKind.TooManyEntries,
                $"Each catalog category is limited to {CosmeticPackageLimits.MaximumEntriesPerKind} entries."));
            return new CosmeticCatalogLoadResult(CosmeticCatalog.Empty, issues);
        }

        var skinDefinitions = new List<SkinDefinition>(skins.Length);
        var armorDefinitions = new List<ArmorEffectDefinition>(armor.Length);
        var deathDefinitions = new List<DeathEffectDefinition>(deaths.Length);
        foreach (string path in skins)
        {
            if (TryLoad<SkinManifest>(root, path, issues, out SkinManifest? value)
                && ValidateHeader(value!.Format, value.Id, value.Key, "prime.skin.", path, issues)
                && value.Hunter <= Hunter.Guardian && value.Materials is { Length: <= CosmeticPackageLimits.MaximumMaterialOverrides })
            {
                SkinMaterialOverride[] materials = value.Materials.Select(item => new SkinMaterialOverride(
                    item.Source ?? "", Asset(root, path, item.Albedo, issues), Asset(root, path, item.Normal, issues),
                    Asset(root, path, item.Emissive, issues), item.SpecularStrength, item.Smoothness,
                    item.ReflectionStrength, Color(item.EmissionTint), item.EmissionStrength)).ToArray();
                string? mask = Asset(root, path, value.TeamAccent?.Mask, issues);
                if (!HasPathIssue(path, issues))
                {
                    TeamAccentDefinition? accent = value.TeamAccent == null ? null
                        : new(value.TeamAccent.Enabled, mask, value.TeamAccent.Strength);
                    TryAdd(new SkinDefinition(value.Id, value.Key!, value.DisplayName ?? "", value.Hunter,
                        value.BaseRecolor, materials, accent), skinDefinitions, path, issues);
                }
            }
            else if (value != null && value.Hunter > Hunter.Guardian)
                issues.Add(new(CosmeticValidationIssueKind.InvalidDefinition, "Skin hunter is invalid.", value.Key, path));
        }
        foreach (string path in armor)
        {
            if (TryLoad<ArmorManifest>(root, path, issues, out ArmorManifest? value)
                && ValidateHeader(value!.Format, value.Id, value.Key, "prime.armor_fx.", path, issues))
            {
                var definition = new ArmorEffectDefinition(value.Id, value.Key!, value.DisplayName ?? "",
                    value.AltFormMode, Material(value.Material), Particles(value.Particles), Ribbons(value.Ribbons),
                    Attachments(value.Attachments), Distortion(value.Distortion));
                if (!HasPathIssue(path, issues)) TryAdd(definition, armorDefinitions, path, issues);
            }
        }
        foreach (string path in deaths)
        {
            if (TryLoad<DeathManifest>(root, path, issues, out DeathManifest? value)
                && ValidateHeader(value!.Format, value.Id, value.Key, "prime.death.", path, issues))
            {
                string? animation = Asset(root, path, value.Animation, issues);
                var definition = new DeathEffectDefinition(value.Id, value.Key!, value.DisplayName ?? "",
                    value.Duration, value.BodyMode, animation, Material(value.Material), Particles(value.Particles),
                    Distortion(value.Distortion), value.Hunter);
                if (!HasPathIssue(path, issues)) TryAdd(definition, deathDefinitions, path, issues);
            }
        }

        try
        {
            return new CosmeticCatalogLoadResult(new CosmeticCatalog(skinDefinitions, armorDefinitions,
                deathDefinitions, checked((ushort)manifest.Format)), issues);
        }
        catch (ArgumentException error)
        {
            issues.Add(new(CosmeticValidationIssueKind.InvalidDefinition, error.Message));
            return new CosmeticCatalogLoadResult(CosmeticCatalog.Empty, issues);
        }
    }

    private static bool TryLoad<T>(string root, string? path, List<CosmeticValidationIssue> issues, out T? value)
    {
        value = default;
        if (!CosmeticPath.IsValidRelative(path))
        {
            issues.Add(new(CosmeticValidationIssueKind.InvalidAssetPath, "Manifest path is not canonical and relative.", Path: path));
            return false;
        }
        if (!TryRead(root, path!, CosmeticPackageLimits.MaximumManifestBytes,
            CosmeticValidationIssueKind.MissingAsset, issues, out byte[] bytes)) return false;
        try
        {
            RejectDuplicateProperties(bytes);
            value = JsonSerializer.Deserialize<T>(bytes, _json);
            if (value == null) throw new JsonException("Manifest root is null.");
            return true;
        }
        catch (JsonException error)
        {
            issues.Add(new(CosmeticValidationIssueKind.MalformedManifest,
                $"Malformed cosmetic manifest: {error.Message}", Path: path));
            return false;
        }
    }

    private static bool TryRead(string root, string relativePath, int maximumBytes,
        CosmeticValidationIssueKind missingKind, List<CosmeticValidationIssue> issues, out byte[] bytes)
    {
        bytes = [];
        string? full = Resolve(root, relativePath);
        if (full == null)
        {
            issues.Add(new(CosmeticValidationIssueKind.InvalidAssetPath, "Path escapes the cosmetic pack.", Path: relativePath));
            return false;
        }
        try
        {
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.SequentialScan);
            if (stream.Length is <= 0 || stream.Length > maximumBytes)
            {
                issues.Add(new(stream.Length > maximumBytes ? CosmeticValidationIssueKind.ManifestTooLarge : missingKind,
                    $"File must be between 1 and {maximumBytes} bytes.", Path: relativePath));
                return false;
            }
            bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            return true;
        }
        catch (IOException)
        {
            issues.Add(new(missingKind, "Cosmetic pack file could not be read.", Path: relativePath));
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            issues.Add(new(missingKind, "Cosmetic pack file could not be read.", Path: relativePath));
            return false;
        }
    }

    private static string? Asset(string root, string manifestPath, string? authored,
        List<CosmeticValidationIssue> issues)
    {
        if (authored == null) return null;
        if (!CosmeticPath.IsValidRelative(authored))
        {
            issues.Add(new(CosmeticValidationIssueKind.InvalidAssetPath,
                "Asset path is not canonical and relative.", Path: manifestPath));
            return authored;
        }
        string directory = Path.GetDirectoryName(manifestPath)?.Replace('\\', '/') ?? "";
        string relative = directory.Length == 0 ? authored : directory + "/" + authored;
        string? full = Resolve(root, relative);
        if (full == null)
            issues.Add(new(CosmeticValidationIssueKind.InvalidAssetPath, "Asset path escapes the cosmetic pack.", Path: manifestPath));
        else if (!File.Exists(full))
            issues.Add(new(CosmeticValidationIssueKind.MissingAsset, "Referenced cosmetic asset is missing.", Path: relative));
        return relative;
    }

    private static string? Resolve(string root, string relative)
    {
        string full = Path.GetFullPath(relative.Replace('/', Path.DirectorySeparatorChar), root);
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal)) return null;

        // Lexical containment does not stop a pack-owned symlink from escaping the
        // root. Cosmetic packages are data-only, so reject every reparse point in
        // the authored path instead of following it.
        string current = root;
        foreach (string segment in relative.Split('/'))
        {
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return null;
            }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
        }
        return full;
    }

    private static bool ValidateHeader(int format, ushort id, string? key, string prefix,
        string path, List<CosmeticValidationIssue> issues)
    {
        if (format != CosmeticCatalog.CurrentVersion)
        {
            issues.Add(new(CosmeticValidationIssueKind.UnsupportedFormat, "Unsupported cosmetic manifest format.", key, path));
            return false;
        }
        if (id == 0)
        {
            issues.Add(new(CosmeticValidationIssueKind.InvalidId, "Cosmetic ID zero is reserved for fallback.", key, path));
            return false;
        }
        if (!CosmeticId.IsValid(key) || !key!.StartsWith(prefix, StringComparison.Ordinal))
        {
            issues.Add(new(CosmeticValidationIssueKind.InvalidKey, "Cosmetic key is invalid for its category.", key, path));
            return false;
        }
        return true;
    }

    private static void TryAdd<T>(T value, List<T> destination, string path,
        List<CosmeticValidationIssue> issues)
    {
        try
        {
            // Reuse the catalog's complete semantic validation for a single entry.
            if (value is SkinDefinition skin) _ = new CosmeticCatalog([skin], [], []);
            else if (value is ArmorEffectDefinition armor) _ = new CosmeticCatalog([], [armor], []);
            else if (value is DeathEffectDefinition death) _ = new CosmeticCatalog([], [], [death]);
            destination.Add(value);
        }
        catch (ArgumentException error)
        {
            issues.Add(new(CosmeticValidationIssueKind.InvalidDefinition, error.Message, Path: path));
        }
    }

    private static bool HasPathIssue(string manifestPath, List<CosmeticValidationIssue> issues)
        => issues.Any(issue => (issue.Kind is CosmeticValidationIssueKind.InvalidAssetPath
                or CosmeticValidationIssueKind.MissingAsset)
            && (issue.Path == manifestPath || issue.Path?.StartsWith(
                (Path.GetDirectoryName(manifestPath)?.Replace('\\', '/') ?? "") + "/", StringComparison.Ordinal) == true));

    private static CosmeticColor? Color(float[]? value)
        => value is { Length: 3 } ? new(value[0], value[1], value[2]) : null;

    private static CosmeticMaterialDefinition? Material(MaterialManifest? value)
        => value == null ? null : new(Color(value.EmissionTint), value.EmissionStrength,
            value.SpecularStrength, value.Smoothness, value.ReflectionStrength);

    private static CosmeticParticleDefinition[] Particles(ParticleManifest[]? values)
        => values?.Select(value => new CosmeticParticleDefinition(value.Kind ?? "", value.Anchor,
            value.Rate, value.Count)).ToArray() ?? [];

    private static CosmeticRibbonDefinition[] Ribbons(RibbonManifest[]? values)
        => values?.Select(value => new CosmeticRibbonDefinition(value.From, value.To,
            value.Segments, value.Rate)).ToArray() ?? [];

    private static CosmeticAttachmentDefinition[] Attachments(AttachmentManifest[]? values)
        => values?.Select(value => new CosmeticAttachmentDefinition(
            value.Mesh ?? "", value.Anchor,
            value.Scale, value.OffsetX, value.OffsetY, value.OffsetZ,
            value.PitchDegrees, value.YawDegrees, value.RollDegrees)).ToArray() ?? [];

    private static CosmeticDistortionDefinition? Distortion(DistortionManifest? value)
        => value == null ? null : new(value.Strength, value.Duration);

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> bytes)
    {
        using JsonDocument document = JsonDocument.Parse(bytes.ToArray(),
            new JsonDocumentOptions { MaxDepth = _json.MaxDepth });
        Inspect(document.RootElement);
        static void Inspect(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new JsonException($"Duplicate property '{property.Name}'.");
                    Inspect(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (JsonElement child in element.EnumerateArray()) Inspect(child);
        }
    }

    private sealed class CatalogManifest
    {
        public int Format { get; set; }
        public string[]? Skins { get; set; }
        public string[]? ArmorEffects { get; set; }
        public string[]? DeathEffects { get; set; }
    }

    private abstract class DefinitionManifest
    {
        public int Format { get; set; }
        public ushort Id { get; set; }
        public string? Key { get; set; }
        public string? DisplayName { get; set; }
    }
    private sealed class SkinManifest : DefinitionManifest
    {
        public Hunter Hunter { get; set; }
        public byte BaseRecolor { get; set; }
        public SkinMaterialManifest[]? Materials { get; set; }
        public TeamAccentManifest? TeamAccent { get; set; }
    }
    private sealed class SkinMaterialManifest : MaterialManifest
    {
        public string? Source { get; set; }
        public string? Albedo { get; set; }
        public string? Normal { get; set; }
        public string? Emissive { get; set; }
    }
    private class MaterialManifest
    {
        public float[]? EmissionTint { get; set; }
        public float? EmissionStrength { get; set; }
        public float? SpecularStrength { get; set; }
        public float? Smoothness { get; set; }
        public float? ReflectionStrength { get; set; }
    }
    private sealed class TeamAccentManifest
    {
        public bool Enabled { get; set; }
        public string? Mask { get; set; }
        public float Strength { get; set; }
    }
    private sealed class ArmorManifest : DefinitionManifest
    {
        public CosmeticAltFormMode AltFormMode { get; set; } = CosmeticAltFormMode.RootOnly;
        public MaterialManifest? Material { get; set; }
        public ParticleManifest[]? Particles { get; set; }
        public RibbonManifest[]? Ribbons { get; set; }
        public AttachmentManifest[]? Attachments { get; set; }
        public DistortionManifest? Distortion { get; set; }
    }
    private sealed class DeathManifest : DefinitionManifest
    {
        public Hunter? Hunter { get; set; }
        public float Duration { get; set; }
        public DeathBodyMode BodyMode { get; set; }
        public string? Animation { get; set; }
        public MaterialManifest? Material { get; set; }
        public ParticleManifest[]? Particles { get; set; }
        public DistortionManifest? Distortion { get; set; }
    }
    private sealed class ParticleManifest
    {
        public string? Kind { get; set; }
        public CosmeticAnchor Anchor { get; set; }
        public float Rate { get; set; }
        public ushort Count { get; set; }
    }
    private sealed class RibbonManifest
    {
        public CosmeticAnchor From { get; set; }
        public CosmeticAnchor To { get; set; }
        public byte Segments { get; set; }
        public float Rate { get; set; }
    }
    private sealed class AttachmentManifest
    {
        public string? Mesh { get; set; }
        public CosmeticAnchor Anchor { get; set; }
        public float Scale { get; set; } = 1;
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public float OffsetZ { get; set; }
        public float PitchDegrees { get; set; }
        public float YawDegrees { get; set; }
        public float RollDegrees { get; set; }
    }
    private sealed class DistortionManifest
    {
        public float Strength { get; set; }
        public float Duration { get; set; }
    }
}
