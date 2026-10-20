using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Import;

public enum ImportBundleState { NotInstalled, Checking, Ready, Outdated, Incomplete, UnsupportedSource, Failed, Updating }
public enum ImportPublishStage { Checking, Staging, Verifying, BeforeCommit }
public sealed record ImportBundleSpec(string Id, int Version, string RecipeHash, string SourceFingerprint, string[] RequiredComponents);
public sealed record ImportBundleComponent(string Id, long Length, string Sha256);
public sealed record ImportBundleReceipt(int Schema, string Id, int Version, string RecipeHash,
    string SourceFingerprint, string Generation, string[] RequiredComponents, ImportBundleComponent[] Components);
public sealed record ImportBundleInspection(ImportBundleState State, string Message,
    ImportBundleReceipt? Receipt, IReadOnlyList<string> Files,
    IReadOnlyList<ImportBundleComponentInspection>? ComponentStates = null);
public sealed record ImportBundleComponentInspection(string Id, ImportBundleState State, string Message);

/// <summary>
/// Private local imports are immutable generations. Only the small current
/// receipt is replaced, after every component verifies. Old generations remain
/// available to running scenes/Studio; this owner never uploads or packages them.
/// </summary>
public static partial class LocalImportBundle
{
    public const int MaxComponents = 64;
    public const long MaxComponentBytes = 64 * 1024 * 1024;
    public const long MaxBundleBytes = 256 * 1024 * 1024;
    private const int MaxReceiptBytes = 64 * 1024;
    public const string StorageDirectory = ".local-imports";

    private static bool Hex(string? text, int length) => text?.Length == length && text.All(Uri.IsHexDigit);
    private static bool Id(string? text) => text is { Length: > 0 and <= 80 }
        && text.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    private static bool ComponentId(string? text)
    {
        if (text is not { Length: > 0 and <= 100 }) return false;
        string[] segments = text.Split('.');
        if (segments.Length < 2 || segments[^1] is not ("json" or "obj" or "bin" or "glb")) return false;
        foreach (string segment in segments)
            if (segment.Length == 0 || !segment.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
                return false;
        // These flat names are device paths on Windows even with an extension.
        string name = segments[0];
        return name is not ("con" or "prn" or "aux" or "nul")
            && !(name.Length == 4 && (name.StartsWith("com", StringComparison.Ordinal)
                || name.StartsWith("lpt", StringComparison.Ordinal)) && name[3] is >= '1' and <= '9');
    }
    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void CheckSpec(ImportBundleSpec spec)
    {
        if (!Id(spec.Id) || spec.Version <= 0 || !Hex(spec.RecipeHash, 64)
            || !Hex(spec.SourceFingerprint, 40) || spec.RequiredComponents is not { Length: <= MaxComponents }
            || spec.RequiredComponents.Any(p => !ComponentId(p))
            || spec.RequiredComponents.Distinct(StringComparer.OrdinalIgnoreCase).Count() != spec.RequiredComponents.Length) throw new InvalidDataException("Invalid private import recipe identity.");
    }

    private static string NormalizePath(string path)
    {
        string full = Path.GetFullPath(path);
        if (!OperatingSystem.IsMacOS()) return full;
        // macOS exposes these system directories through fixed aliases. Do
        // not resolve arbitrary user aliases or a retargeted system alias.
        foreach (string alias in new[] { "/var", "/tmp", "/etc" })
        {
            if (full != alias && !full.StartsWith(alias + "/", StringComparison.Ordinal)) continue;
            string? target = new DirectoryInfo(alias).LinkTarget;
            string expected = "/private" + alias;
            if (target != null && Path.GetFullPath(Path.Combine("/", target)) == expected)
                return expected + full[alias.Length..];
        }
        return full;
    }

    // Follow no existing symlink, including one above the chosen root. Lock
    // files are permanent: unlinking a held lock would permit a second writer.
    private static void CheckPath(string path, string root)
    {
        string boundary = Path.TrimEndingDirectorySeparator(NormalizePath(root));
        string candidate = NormalizePath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string prefix = Path.EndsInDirectorySeparator(boundary) ? boundary : boundary + Path.DirectorySeparatorChar;
        if (!candidate.Equals(boundary, comparison) && !candidate.StartsWith(prefix, comparison))
            throw new InvalidDataException("Private import path escaped its root.");
        for (string? part = candidate; part != null; part = Path.GetDirectoryName(part))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(part); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Private import paths cannot contain symbolic links.");
        }
    }

    private static string BundleRoot(string root, string id)
    {
        if (!Id(id)) throw new InvalidDataException("Invalid private bundle ID.");
        string result = Path.Combine(NormalizePath(root), StorageDirectory, id);
        CheckPath(result, root);
        return result;
    }

    // Discovery checks presence only. Even an unsafe/dangling pointer owns
    // this inventory so catalog discovery can fail closed via Inspect instead
    // of throwing or resurrecting unmanaged legacy Echoes definitions.
    public static bool HasReceipt(string root, string id)
    {
        if (!Id(id)) throw new InvalidDataException("Invalid private bundle ID.");
        string pointer = Path.Combine(NormalizePath(root), StorageDirectory, id, "current.json");
        try { return File.Exists(pointer) || new FileInfo(pointer).LinkTarget != null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    public static ImportBundleInspection Inspect(string root, string id, ImportBundleSpec? required = null)
    {
        try
        {
            if (required != null) { CheckSpec(required); if (required.Id != id) throw new InvalidDataException("Bundle ID mismatch."); }
            string folder = BundleRoot(root, id);
            string pointer = Path.Combine(folder, "current.json");
            CheckPath(pointer, root);
            if (!File.Exists(pointer)) return new(ImportBundleState.NotInstalled, "Not installed", null, Array.Empty<string>());
            byte[] pointerBytes;
            using (var input = new FileStream(pointer, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (input.Length is <= 0 or > MaxReceiptBytes) throw new InvalidDataException("Private receipt size exceeds its limit.");
                pointerBytes = new byte[(int)input.Length];
                input.ReadExactly(pointerBytes);
                if (input.ReadByte() != -1) throw new InvalidDataException("Private receipt changed during verification.");
            }
            var receipt = JsonSerializer.Deserialize<ImportBundleReceipt>(pointerBytes)
                ?? throw new InvalidDataException("Empty private receipt.");
            if (receipt.Schema != 1 || receipt.Id != id || receipt.Version <= 0
                || !Hex(receipt.RecipeHash, 64) || !Hex(receipt.SourceFingerprint, 40)
                || !Hex(receipt.Generation, 64) || receipt.RequiredComponents is not { Length: > 0 and <= MaxComponents }
                || receipt.RequiredComponents.Any(p => !ComponentId(p))
                || receipt.RequiredComponents.Distinct(StringComparer.OrdinalIgnoreCase).Count() != receipt.RequiredComponents.Length
                || receipt.Components is not { Length: > 0 and <= MaxComponents })
                throw new InvalidDataException("Invalid private receipt contract.");
            if (required != null && receipt.SourceFingerprint != required.SourceFingerprint)
                return new(ImportBundleState.UnsupportedSource, "Source edition differs from this recipe", receipt, Array.Empty<string>());
            if (required != null && (receipt.Version != required.Version || receipt.RecipeHash != required.RecipeHash))
                return new(ImportBundleState.Outdated, "Local import recipe has changed; reimport from your source", receipt, Array.Empty<string>());
            var files = new List<string>();
            var componentStates = new List<ImportBundleComponentInspection>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            ImportBundleState state = ImportBundleState.Ready;
            string message = "All local components verified";
            void Failure(string id, ImportBundleState failure, string detail)
            {
                componentStates.Add(new(id, failure, detail));
                if (state == ImportBundleState.Ready || failure == ImportBundleState.Failed)
                { state = failure; message = detail; }
            }
            foreach (var component in receipt.Components)
            {
                if (component == null || !ComponentId(component.Id) || !ids.Add(component.Id)
                    || component.Length is <= 0 or > MaxComponentBytes || !Hex(component.Sha256, 64)
                    || (total += component.Length) > MaxBundleBytes)
                    throw new InvalidDataException("Invalid private component inventory.");
                string path = Path.Combine(folder, receipt.Generation, component.Id);
                CheckPath(path, root);
                if (!File.Exists(path))
                { Failure(component.Id, ImportBundleState.Incomplete, "Missing local component: " + component.Id); continue; }
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (stream.Length != component.Length || Convert.ToHexStringLower(SHA256.HashData(stream)) != component.Sha256)
                    { Failure(component.Id, ImportBundleState.Failed, "Local component integrity failed: " + component.Id); continue; }
                    files.Add(path);
                    componentStates.Add(new(component.Id, ImportBundleState.Ready, "Local component verified"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Failure(component.Id, ImportBundleState.Failed, ex.Message); }
            }
            foreach (string missing in receipt.RequiredComponents.Concat(required?.RequiredComponents ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).Where(name => !ids.Contains(name)))
                Failure(missing, ImportBundleState.Incomplete, "Required local bundle component is missing: " + missing);
            return new(state, message, state == ImportBundleState.Failed ? null : receipt,
                state == ImportBundleState.Ready ? files.AsReadOnly() : Array.Empty<string>(),
                componentStates.AsReadOnly());
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return new(ImportBundleState.Failed, ex.Message, null, Array.Empty<string>()); }
    }

    /// <summary>
    /// Withdraw discovery atomically while retaining the receipt and immutable
    /// generation bytes for existing consumers. A later Publish can reinstall.
    /// Cancellation after the rename is a completed removal, never a rollback.
    /// </summary>
    public static bool Remove(string root, string id, CancellationToken cancellation = default)
    {
        string folder = BundleRoot(root, id);
        cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(folder);
        string lockPath = Path.Combine(folder, "writer.lock");
        CheckPath(lockPath, root);
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1);
        string pointer = Path.Combine(folder, "current.json");
        CheckPath(pointer, root);
        cancellation.ThrowIfCancellationRequested();
        if (!File.Exists(pointer)) return false;
        string retired = Path.Combine(folder, "retired-" + Guid.NewGuid().ToString("N") + ".json");
        CheckPath(retired, root);
        cancellation.ThrowIfCancellationRequested();
        // Same-directory rename is the sole commit. Keep the permanent lock
        // and all generations so open scene/Studio paths remain valid.
        File.Move(pointer, retired);
        return true;
    }

    public static IReadOnlyList<string> Publish(string root, ImportBundleSpec spec,
        IReadOnlyDictionary<string, byte[]> replacements, CancellationToken cancellation = default,
        IProgress<ImportPublishStage>? progress = null, bool replaceAllComponents = false)
    {
        CheckSpec(spec);
        if (replacements.Count is < 1 or > MaxComponents) throw new InvalidDataException("Invalid private component count.");
        string folder = BundleRoot(root, spec.Id);
        Directory.CreateDirectory(folder);
        string lockPath = Path.Combine(folder, "writer.lock");
        CheckPath(lockPath, root);
        cancellation.ThrowIfCancellationRequested();
        // Fail promptly with a clear busy error; no render/UI wait and no
        // second process can overwrite the first process's generation.
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1);
        progress?.Report(ImportPublishStage.Checking);
        var old = Inspect(root, spec.Id, spec with { RequiredComponents = Array.Empty<string>() });
        var bytes = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        if (!replaceAllComponents && old.State == ImportBundleState.Ready)
            foreach (string file in old.Files)
            {
                cancellation.ThrowIfCancellationRequested();
                string name = Path.GetFileName(file);
                var component = old.Receipt!.Components.Single(c => c.Id == name);
                byte[] content;
                using (var input = File.OpenRead(file))
                {
                    if (input.Length != component.Length) throw new InvalidDataException("Previous bundle changed while preparing update.");
                    content = new byte[(int)component.Length];
                    input.ReadExactly(content);
                    if (input.ReadByte() != -1 || Digest(content) != component.Sha256)
                        throw new InvalidDataException("Previous bundle changed while preparing update.");
                }
                bytes.Add(name, content);
            }
        long pendingBytes = bytes.Values.Sum(b => b.LongLength);
        var changedNames = new List<string>(replacements.Count);
        foreach (var part in replacements)
        {
            if (!ComponentId(part.Key) || part.Value == null || part.Value.LongLength is <= 0 or > MaxComponentBytes)
                throw new InvalidDataException("Invalid private component name or size.");
            pendingBytes += part.Value.LongLength - (bytes.TryGetValue(part.Key, out var previous) ? previous.LongLength : 0);
            if (pendingBytes > MaxBundleBytes) throw new InvalidDataException("Private bundle exceeds its budget.");
            // Detach from the caller before validation/publication.
            bytes[part.Key] = (byte[])part.Value.Clone();
            changedNames.Add(part.Key);
        }
        if (spec.RequiredComponents.Any(name => !bytes.ContainsKey(name)))
            throw new InvalidDataException("Required private bundle component is missing.");
        if (bytes.Count > MaxComponents || bytes.Values.Sum(b => b.LongLength) > MaxBundleBytes)
            throw new InvalidDataException("Private bundle exceeds its budget.");
        var components = bytes.Select(p => new ImportBundleComponent(p.Key, p.Value.LongLength, Digest(p.Value))).ToArray();
        string generation = Digest(JsonSerializer.SerializeToUtf8Bytes(new { spec, components }));
        var receipt = new ImportBundleReceipt(1, spec.Id, spec.Version, spec.RecipeHash, spec.SourceFingerprint, generation, bytes.Keys.ToArray(), components);
        byte[] pointerBytes = JsonSerializer.SerializeToUtf8Bytes(receipt);
        if (pointerBytes.Length > MaxReceiptBytes) throw new InvalidDataException("Private receipt exceeds its budget.");
        string staging = Path.Combine(folder, ".stage-" + Guid.NewGuid().ToString("N"));
        string destination = Path.Combine(folder, generation);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(staging);
            progress?.Report(ImportPublishStage.Staging);
            foreach (var part in bytes)
            {
                cancellation.ThrowIfCancellationRequested();
                using var output = new FileStream(Path.Combine(staging, part.Key), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                output.Write(part.Value);
                output.Flush(flushToDisk: true);
            }
            progress?.Report(ImportPublishStage.Verifying);
            foreach (var part in components)
            {
                cancellation.ThrowIfCancellationRequested();
                using var input = File.OpenRead(Path.Combine(staging, part.Id));
                if (input.Length != part.Length || Convert.ToHexStringLower(SHA256.HashData(input)) != part.Sha256)
                    throw new InvalidDataException("Staged private component verification failed.");
            }
            CheckPath(destination, root);
            if (Directory.Exists(destination))
            {
                bool reusable = true;
                try
                {
                    foreach (var part in components)
                    {
                        string file = Path.Combine(destination, part.Id);
                        CheckPath(file, root);
                        using var input = File.OpenRead(file);
                        if (input.Length != part.Length || Convert.ToHexStringLower(SHA256.HashData(input)) != part.Sha256)
                        { reusable = false; break; }
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                { reusable = false; }
                if (!reusable)
                {
                    // Repair into a fresh generation, never overwrite bytes a
                    // live scene might still have open (even a damaged copy).
                    generation = Digest(Encoding.UTF8.GetBytes(generation + Guid.NewGuid().ToString("N")));
                    destination = Path.Combine(folder, generation);
                    receipt = receipt with { Generation = generation };
                    pointerBytes = JsonSerializer.SerializeToUtf8Bytes(receipt);
                }
            }
            if (!Directory.Exists(destination)) Directory.Move(staging, destination);
            // Remove a coalesced staging copy before the commit. Cleanup must
            // never turn a successfully activated bundle into a reported failure.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            progress?.Report(ImportPublishStage.BeforeCommit);
            cancellation.ThrowIfCancellationRequested();
            CheckPath(Path.Combine(folder, "current.json"), root);
            // This is the sole publication point. Cancellation after it is a
            // completed import; do not roll back or mutate an old generation.
            AtomicFile.Write(Path.Combine(folder, "current.json"), pointerBytes);
            return changedNames.Select(name => Path.Combine(destination, name)).ToArray();
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
