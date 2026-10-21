using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.CustomWeapons.Packages;

/// <summary>CPU-validated immutable package. Installation and activation are separate operations.</summary>
public sealed class WeaponPackage
{
    private readonly byte[] _archive;
    private readonly SortedDictionary<string, byte[]> _resources;
    public WeaponProject Project { get; }
    public WeaponDefinition Definition { get; }
    public string PackageHash { get; }
    public string ContentHash { get; }
    public string DependencyHash { get; }
    public IReadOnlyList<string> ResourcePaths { get; }
    private WeaponPackage(byte[] archive, WeaponProject project, WeaponDefinition definition, SortedDictionary<string, byte[]> resources)
    {
        _archive = archive; Project = project; Definition = definition; _resources = resources;
        ResourcePaths = Array.AsReadOnly(resources.Keys.ToArray()); PackageHash = Hash(archive);
        DependencyHash = Hash(WeaponProject.EmptyLock);
        byte[] canonical = Manifest(project, definition, Normalized(project, definition, resources));
        byte[] framed = new byte[canonical.Length + 5]; "WPD1\0"u8.CopyTo(framed); canonical.CopyTo(framed, 5);
        ContentHash = Hash(framed);
    }
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public byte[] CopyArchive() => (byte[])_archive.Clone();
    public byte[] CopyResource(string path) => _resources.TryGetValue(path, out var bytes)
        ? (byte[])bytes.Clone() : throw WeaponPackageJson.Bad("Resource is not present.");

    public static WeaponPackage Parse(ReadOnlyMemory<byte> archive)
    {
        if (archive.Length > WeaponPackageLimits.MaximumArchiveBytes) throw WeaponPackageJson.Bad("Archive byte budget.");
        try
        {
            byte[] snapshot = archive.ToArray(); // Never retain caller-owned mutable bytes.
            var entries = WeaponPackageArchive.Read(snapshot);
            if (!entries.Remove("manifest.json", out var manifest) || !entries.TryGetValue("weapon.json", out var projectBytes))
                throw WeaponPackageJson.Bad("Missing manifest/project.");
            var project = WeaponProject.Parse(projectBytes);
            var definition = Validate(project, entries);
            ValidateManifest(manifest, project, definition, entries);
            return new(snapshot, project, definition, entries);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException
            or KeyNotFoundException or OverflowException or DecoderFallbackException)
        { throw WeaponPackageJson.Bad("Invalid package: " + e.Message); }
    }

    internal static WeaponDefinition Validate(WeaponProject project, SortedDictionary<string, byte[]> resources)
    {
        if (resources.Count != project.Required.Count + 1 || !resources.ContainsKey("weapon.json")) throw WeaponPackageJson.Bad("Resource set mismatch.");
        foreach (var resource in resources)
        {
            if (resource.Key == "weapon.json") continue;
            if (!project.Required.TryGetValue(resource.Key, out var media)) throw WeaponPackageJson.Bad("Undeclared resource.");
            int cap = media switch
            {
                "audio/wav" => 44 + WeaponPackageLimits.MaximumWaveFrames * 2,
                "text/plain" => WeaponPackageLimits.MaximumTextBytes,
                "application/vnd.projectprime.weapon-mechanics+json" => WeaponMechanics.MaxJsonBytes,
                "application/vnd.projectprime.rigid-boxes+json" => CustomRifleModel.MaximumBytes,
                _ => WeaponPackageLimits.MaximumJsonBytes
            };
            if (resource.Value.Length is <= 0 || resource.Value.Length > cap) throw WeaponPackageJson.Bad("Resource byte budget: " + resource.Key);
        }
        foreach (var path in project.Required.Keys) if (!resources.ContainsKey(path)) throw WeaponPackageJson.Bad("Missing required resource.");
        var mechanics = WeaponMechanics.Parse(resources[project.MechanicsPath]);
        using (var model = WeaponPackageJson.Parse(resources[project.ModelPath])) { } // Full recursive UTF-8/duplicate/number preflight.
        _ = CustomRifleModel.Decode(resources[project.ModelPath]); // Actual bounded CPU decoder, never GL.
        foreach (string path in project.AudioPaths) _ = DecodeWave(resources[path]);
        ValidateText(resources[project.LicensePath]); ValidateText(resources[project.ReadmePath]);
        using (var document = WeaponPackageJson.Parse(resources[WeaponProject.LockPath]))
        {
            var root = document.RootElement; WeaponPackageJson.Keys(root, "schema", "dependencies"); WeaponPackageJson.One(root.GetProperty("schema"));
            var dependencies = root.GetProperty("dependencies");
            if (dependencies.ValueKind != JsonValueKind.Array || dependencies.GetArrayLength() != 0)
                throw WeaponPackageJson.Bad("This version supports self-contained packages only; external dependencies are unsupported.");
        }
        return WeaponDefinition.Create(project.ContentId, project.Name, mechanics);
    }

    private static void ValidateText(ReadOnlySpan<byte> bytes)
    {
        string text = WeaponPackageJson.Utf8.GetString(bytes);
        if (string.IsNullOrWhiteSpace(text) || text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw WeaponPackageJson.Bad("Invalid license/readme text.");
    }

    internal static float[] DecodeWave(ReadOnlySpan<byte> bytes)
    {
        // A complete narrow decoder, not a file-extension or header-only check.
        if (bytes.Length is < 46 or > 44 + WeaponPackageLimits.MaximumWaveFrames * 2 || (bytes.Length & 1) != 0
            || !bytes[..4].SequenceEqual("RIFF"u8) || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != bytes.Length - 8
            || !bytes.Slice(8, 8).SequenceEqual("WAVEfmt "u8) || BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]) != 16
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[20..]) != 1 || BinaryPrimitives.ReadUInt16LittleEndian(bytes[22..]) != 1
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]) != 22050 || BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..]) != 44100
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[32..]) != 2 || BinaryPrimitives.ReadUInt16LittleEndian(bytes[34..]) != 16
            || !bytes.Slice(36, 4).SequenceEqual("data"u8) || BinaryPrimitives.ReadUInt32LittleEndian(bytes[40..]) != bytes.Length - 44)
            throw WeaponPackageJson.Bad("Unsupported/corrupt PCM16 mono22050 WAV.");
        var samples = new float[(bytes.Length - 44) / 2];
        for (int i = 0; i < samples.Length; i++) samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(44 + i * 2)..]) / 32768f;
        return samples;
    }

    private static SortedDictionary<string, byte[]> Normalized(WeaponProject project, WeaponDefinition definition, SortedDictionary<string, byte[]> resources)
    {
        var normalized = new SortedDictionary<string, byte[]>(resources, StringComparer.Ordinal)
        {
            ["weapon.json"] = project.ToCanonicalBytes(),
            [project.MechanicsPath] = definition.Mechanics.ToCanonicalBytes(),
            [WeaponProject.LockPath] = (byte[])WeaponProject.EmptyLock.Clone()
        };
        return normalized;
    }

    internal static byte[] Manifest(WeaponProject project, WeaponDefinition definition, SortedDictionary<string, byte[]> resources)
        => WeaponPackageJson.Write(w =>
        {
            w.WriteStartObject(); w.WriteNumber("schema", 1); w.WriteString("kind", "weapon");
            w.WriteString("contentId", project.ContentId.ToString("D")); w.WriteString("mechanicsHash", definition.Mechanics.DefinitionHash);
            w.WriteStartArray("resources");
            foreach (var resource in resources)
            {
                w.WriteStartObject(); w.WriteString("path", resource.Key);
                w.WriteString("mediaType", resource.Key == "weapon.json" ? "application/vnd.projectprime.weapon-project+json" : project.Required[resource.Key]);
                w.WriteNumber("length", resource.Value.Length); w.WriteString("sha256", Hash(resource.Value)); w.WriteEndObject();
            }
            w.WriteEndArray(); w.WriteEndObject();
        });

    private static void ValidateManifest(ReadOnlyMemory<byte> bytes, WeaponProject project, WeaponDefinition definition, SortedDictionary<string, byte[]> resources)
    {
        using var document = WeaponPackageJson.Parse(bytes); var root = document.RootElement;
        WeaponPackageJson.Keys(root, "schema", "kind", "contentId", "mechanicsHash", "resources");
        WeaponPackageJson.One(root.GetProperty("schema")); WeaponPackageJson.Is(root.GetProperty("kind"), "weapon");
        WeaponPackageJson.Is(root.GetProperty("contentId"), project.ContentId.ToString("D"));
        WeaponPackageJson.Is(root.GetProperty("mechanicsHash"), definition.Mechanics.DefinitionHash);
        var rows = root.GetProperty("resources");
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != resources.Count) throw WeaponPackageJson.Bad("Manifest resource count.");
        using var expected = resources.GetEnumerator();
        foreach (var row in rows.EnumerateArray())
        {
            WeaponPackageJson.Keys(row, "path", "mediaType", "length", "sha256");
            if (!expected.MoveNext()) throw WeaponPackageJson.Bad("Manifest count.");
            var actual = expected.Current;
            WeaponPackageJson.Is(row.GetProperty("path"), actual.Key);
            WeaponPackageJson.Is(row.GetProperty("mediaType"), actual.Key == "weapon.json" ? "application/vnd.projectprime.weapon-project+json" : project.Required[actual.Key]);
            if (!row.GetProperty("length").TryGetInt32(out int length) || length != actual.Value.Length) throw WeaponPackageJson.Bad("Manifest length.");
            WeaponPackageJson.Is(row.GetProperty("sha256"), Hash(actual.Value));
        }
    }

    internal static byte[] Build(WeaponProject project, IReadOnlyDictionary<string, byte[]> input)
    {
        ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(input);
        var resources = new SortedDictionary<string, byte[]>(StringComparer.Ordinal) { ["weapon.json"] = project.ToCanonicalBytes() };
        long total = 0;
        foreach (var resource in input)
        {
            if (resources.Count >= WeaponPackageLimits.MaximumEntries || !project.Required.ContainsKey(resource.Key)
                || resource.Value == null || resource.Value.Length > WeaponPackageLimits.MaximumArchiveBytes
                || !resources.TryAdd(resource.Key, (byte[])resource.Value.Clone())) throw WeaponPackageJson.Bad("Invalid source resource set.");
            total += resource.Value.Length;
            if (total > WeaponPackageLimits.MaximumExpandedBytes) throw WeaponPackageJson.Bad("Source resource byte budget.");
        }
        var definition = Validate(project, resources);
        resources = Normalized(project, definition, resources);
        resources.Add("manifest.json", Manifest(project, definition, resources));
        byte[] archive = WeaponPackageArchive.Write(resources);
        _ = Parse(archive); // Validate actual final bytes, including our container writer.
        return archive;
    }
}

public static class WeaponPackageBuilder
{
    public static byte[] Build(WeaponProject project, IReadOnlyDictionary<string, byte[]> resources)
    {
        try { return WeaponPackage.Build(project, resources); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException
            or KeyNotFoundException or OverflowException or DecoderFallbackException)
        { throw WeaponPackageJson.Bad("Invalid source: " + e.Message); }
    }
}
