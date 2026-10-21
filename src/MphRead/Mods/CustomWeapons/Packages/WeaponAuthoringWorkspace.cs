using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MphRead.Mods.CustomWeapons.Packages;

public sealed record WeaponLicenseAttestation(string ArchiveHash, string Attestor, DateTimeOffset AttestedUtc);

/// <summary>
/// CPU-only creator workspace shared by Studio and other desktop front ends.
/// A saved workspace is one atomic JSON document containing the exact validated
/// package snapshot. It never grants inventory, starts a Scene or activates a weapon.
/// Files and their ancestors must remain privately owned during authoring.
/// </summary>
public sealed class WeaponAuthoringWorkspace
{
    public const string Extension = ".ppweaponproject";
    public const int MaximumWorkspaceBytes = 12 * 1024 * 1024;
    private readonly object _gate = new();
    private WeaponPackage _package;
    private WeaponLicenseAttestation? _attestation;
    private string? _savedPath, _savedHash;
    private WeaponAuthoringWorkspace(WeaponPackage package) => _package = package;

    public WeaponProject Project { get { lock (_gate) return _package.Project; } }
    public WeaponLicenseAttestation? LicenseAttestation { get { lock (_gate) return _attestation; } }
    public bool CanDistribute { get { lock (_gate) return _attestation?.ArchiveHash == _package.PackageHash; } }
    public string? SavedPath { get { lock (_gate) return _savedPath; } }

    /// <summary>Starts a new identity using only the original bundled reference assets.</summary>
    public static WeaponAuthoringWorkspace CreateReference(string name = "My custom weapon", string author = "Creator")
    {
        var reference = WeaponPackage.Parse(WeaponPackageReference.BuildRiftlineM7());
        Guid id = Guid.NewGuid();
        var json = JsonNode.Parse(reference.Project.ToCanonicalBytes())!.AsObject();
        json["contentId"] = id.ToString("D"); json["productId"] = "creator.weapon-" + id.ToString("N");
        json["name"] = name; json["author"] = author;
        var project = WeaponProject.Parse(JsonSerializer.SerializeToUtf8Bytes(json));
        return new(WeaponPackage.Parse(WeaponPackageBuilder.Build(project, Resources(reference))));
    }

    /// <summary>Imports an exact package as an editable draft, without inheriting a license attestation.</summary>
    public static WeaponAuthoringWorkspace ImportArchive(string path)
        => new(WeaponPackage.Parse(Read(path, WeaponPackageLimits.MaximumArchiveBytes)));

    /// <summary>Opens the editable source-directory layout produced by the existing CLI.</summary>
    public static WeaponAuthoringWorkspace ImportSourceDirectory(string directory)
    {
        directory = SafePath(directory);
        var project = WeaponProject.Parse(Read(Path.Combine(directory, "weapon.json"), WeaponPackageLimits.MaximumJsonBytes));
        var resources = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in project.Required)
        {
            byte[] bytes = Read(Path.Combine(directory, entry.Key.Replace('/', Path.DirectorySeparatorChar)), ResourceLimit(entry.Value));
            if ((total += bytes.Length) > WeaponPackageLimits.MaximumExpandedBytes) throw new InvalidDataException("Source resource byte budget exceeded.");
            resources.Add(entry.Key, bytes);
        }
        return new(WeaponPackage.Parse(WeaponPackageBuilder.Build(project, resources)));
    }

    public static WeaponAuthoringWorkspace Open(string path)
    {
        try { return OpenCore(path); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or OverflowException or ArgumentException)
        { throw new InvalidDataException("Invalid weapon workspace JSON: " + error.Message, error); }
    }
    private static WeaponAuthoringWorkspace OpenCore(string path)
    {
        path = SafePath(path);
        byte[] bytes = Read(path, MaximumWorkspaceBytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        Keys(root, "schema", "archive", "licenseAttestation");
        WeaponPackageJson.One(root.GetProperty("schema"));
        var encoded = root.GetProperty("archive");
        if (encoded.ValueKind != JsonValueKind.String || encoded.GetString()!.Length > (WeaponPackageLimits.MaximumArchiveBytes + 2) / 3 * 4)
            throw new InvalidDataException("Workspace package byte budget exceeded.");
        byte[] archive;
        try { archive = encoded.GetBytesFromBase64(); }
        catch (FormatException error) { throw new InvalidDataException("Workspace package is not valid base64.", error); }
        var workspace = new WeaponAuthoringWorkspace(WeaponPackage.Parse(archive));
        var attestation = root.GetProperty("licenseAttestation");
        if (attestation.ValueKind != JsonValueKind.Null)
        {
            Keys(attestation, "archiveHash", "attestor", "attestedUtc");
            string hash = attestation.GetProperty("archiveHash").GetString() ?? "";
            string attestor = Attestor(attestation.GetProperty("attestor").GetString() ?? "");
            if (hash != workspace._package.PackageHash || !DateTimeOffset.TryParseExact(
                attestation.GetProperty("attestedUtc").GetString(), "O", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var when) || when.Offset != TimeSpan.Zero)
                throw new InvalidDataException("Workspace license attestation does not match its exact package.");
            workspace._attestation = new(hash, attestor, when);
        }
        workspace._savedPath = path; workspace._savedHash = Digest(bytes);
        return workspace;
    }

    public byte[] CopyProjectJson() { lock (_gate) return _package.Project.ToCanonicalBytes(); }
    public byte[] CopyMechanicsJson() { lock (_gate) return _package.Definition.Mechanics.ToCanonicalBytes(); }
    public IReadOnlyList<string> ResourcePaths { get { lock (_gate) return _package.Project.Required.Keys.ToArray(); } }
    public byte[] CopyResource(string path) { lock (_gate) return _package.CopyResource(path); }

    /// <summary>Invalid drafts are rejected before replacing the last validated snapshot.</summary>
    public void EditProjectJson(ReadOnlyMemory<byte> json)
    {
        var project = WeaponProject.Parse(json);
        lock (_gate) Replace(WeaponPackageBuilder.Build(project, Resources(_package)));
    }
    public void EditMechanicsJson(ReadOnlySpan<byte> json)
    {
        byte[] canonical = WeaponMechanics.Parse(json).ToCanonicalBytes();
        lock (_gate) SetResourceCore(_package.Project.MechanicsPath, canonical);
    }
    public void SetResource(string path, ReadOnlyMemory<byte> bytes)
    { lock (_gate) SetResourceCore(path, bytes); }
    private void SetResourceCore(string path, ReadOnlyMemory<byte> bytes)
    {
        if (!_package.Project.Required.TryGetValue(path, out string? media) || bytes.Length > ResourceLimit(media))
            throw new InvalidDataException("Unknown resource or resource byte budget exceeded.");
        var resources = Resources(_package); resources[path] = bytes.ToArray();
        Replace(WeaponPackageBuilder.Build(_package.Project, resources));
    }
    private void Replace(byte[] archive)
    {
        var package = WeaponPackage.Parse(archive);
        if (package.PackageHash != _package.PackageHash) _attestation = null;
        _package = package;
    }
    public WeaponPackage Validate() { lock (_gate) return WeaponPackage.Parse(_package.CopyArchive()); }
    public byte[] BuildArchive() { lock (_gate) return _package.CopyArchive(); }

    /// <summary>Explicit creator confirmation, bound to every byte of this revision.</summary>
    public void AttestLicense(string attestor, bool ownsOrHasPermission)
    {
        if (!ownsOrHasPermission) throw new InvalidOperationException("Confirm that you own or have permission to distribute every included asset.");
        attestor = Attestor(attestor);
        lock (_gate) _attestation = new(_package.PackageHash, attestor, DateTimeOffset.UtcNow);
    }
    public void ClearLicenseAttestation() { lock (_gate) _attestation = null; }
    public byte[] PreparePublish()
    { lock (_gate) { RequireAttestation(); return _package.CopyArchive(); } }
    public void ExportArchive(string destination)
    { lock (_gate) { RequireAttestation(); AtomicWrite(SafePath(destination), _package.CopyArchive(), overwrite: false); } }
    public WeaponPackage Install(string storeDirectory)
    { lock (_gate) return new WeaponPackageStore(SafePath(storeDirectory)).Install(_package.CopyArchive()); }
    private void RequireAttestation()
    {
        if (_attestation?.ArchiveHash != _package.PackageHash)
            throw new InvalidOperationException("Attest the asset licenses for this exact revision before export or publication.");
    }

    /// <summary>Atomic save, with an exclusive cross-process gate and stale-document refusal.</summary>
    public void Save(string path)
    {
        path = SafePath(path);
        if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a " + Extension + " workspace file.");
        lock (_gate)
        {
            string gatePath = SafePath(path + ".lock");
            using var diskGate = new FileStream(gatePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            bool same = _savedPath != null && string.Equals(path, _savedPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (same)
            {
                if (!File.Exists(path) || Digest(Read(path, MaximumWorkspaceBytes)) != _savedHash)
                    throw new IOException("The workspace changed on disk. Reopen it or save to a new filename.");
            }
            else if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Workspace destination already exists.");
            byte[] bytes = CanonicalWorkspace();
            AtomicWrite(path, bytes, overwrite: same);
            _savedPath = path; _savedHash = Digest(bytes);
        }
    }
    public byte[] CopyWorkspaceJson() { lock (_gate) return CanonicalWorkspace(); }
    private byte[] CanonicalWorkspace() => WeaponPackageJson.Write(writer =>
    {
        writer.WriteStartObject(); writer.WriteNumber("schema", 1);
        writer.WriteBase64String("archive", _package.CopyArchive());
        writer.WritePropertyName("licenseAttestation");
        if (_attestation is not { } attestation) writer.WriteNullValue();
        else
        {
            writer.WriteStartObject(); writer.WriteString("archiveHash", attestation.ArchiveHash);
            writer.WriteString("attestor", attestation.Attestor);
            writer.WriteString("attestedUtc", attestation.AttestedUtc.ToString("O", CultureInfo.InvariantCulture)); writer.WriteEndObject();
        }
        writer.WriteEndObject();
    });
    private static void Keys(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Workspace object required.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name) || !expected.Contains(property.Name, StringComparer.Ordinal))
                throw new InvalidDataException("Duplicate or unsupported workspace field.");
        if (names.Count != expected.Length) throw new InvalidDataException("Missing workspace field.");
    }
    private static string Attestor(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value = value.Trim();
        if (value.Length is < 1 or > 120 || value.Any(char.IsControl)) throw new ArgumentException("A bounded creator name is required.");
        return value;
    }
    private static Dictionary<string, byte[]> Resources(WeaponPackage package)
        => package.Project.Required.Keys.ToDictionary(path => path, package.CopyResource, StringComparer.Ordinal);
    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static int ResourceLimit(string media) => media switch
    {
        "audio/wav" => 44 + WeaponPackageLimits.MaximumWaveFrames * 2,
        "text/plain" => WeaponPackageLimits.MaximumTextBytes,
        "application/vnd.projectprime.weapon-mechanics+json" => WeaponMechanics.MaxJsonBytes,
        "application/vnd.projectprime.rigid-boxes+json" => CustomRifleModel.MaximumBytes,
        _ => WeaponPackageLimits.MaximumJsonBytes
    };
    private static byte[] Read(string path, int maximum)
    {
        path = SafePath(path);
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.Device)) != 0) throw new IOException("Input must be a regular file.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 || stream.Length > maximum) throw new InvalidDataException("Input byte budget exceeded.");
        byte[] bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new IOException("Input changed during read.");
        return bytes;
    }
    private static void AtomicWrite(string path, byte[] bytes, bool overwrite)
    {
        string parent = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(parent)) throw new IOException("Output parent directory must exist.");
        string stage = Path.Combine(parent, ".weapon-authoring-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool owned = false;
        try
        {
            using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { owned = true; stream.Write(bytes); stream.Flush(true); }
            SafePath(path); File.Move(stage, path, overwrite);
        }
        finally { if (owned && File.Exists(stage)) { SafePath(stage); File.Delete(stage); } }
    }
    private static string SafePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 4096 || input.Any(char.IsControl)) throw new IOException("Invalid disk path.");
        foreach (string part in input.Split('/', '\\'))
            if (part is not ("." or "..") && (part.EndsWith(' ') || part.EndsWith('.'))) throw new IOException("Ambiguous disk path.");
        string full = Path.GetFullPath(input);
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException("Network/device paths are prohibited.");
        string volume = Path.GetPathRoot(full)!;
        foreach (string part in full[volume.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            string stem = part.Split('.')[0].ToLowerInvariant();
            if (part.Contains(':') || part.EndsWith(' ') || part.EndsWith('.') || stem is "con" or "prn" or "aux" or "nul"
                || stem.Length == 4 && (stem.StartsWith("com", StringComparison.Ordinal) || stem.StartsWith("lpt", StringComparison.Ordinal)) && stem[3] is >= '0' and <= '9')
                throw new IOException("Unsafe disk path.");
        }
        for (string? current = full; current != null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Symlink/reparse paths are prohibited."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return full;
    }
}
