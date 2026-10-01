using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.Settings;

/// <summary>Explicit user action only. No auth, game data, replays or cache discovery.</summary>
internal static class SettingsArchive
{
    internal const int MaximumFiles = 128;
    internal const int MaximumTotalBytes = 16 * 1024 * 1024;
    internal const int MaximumArchiveBytes = 20 * 1024 * 1024;
    private const string ManifestName = "manifest.json";
    internal static object Gate => SettingsPersistence.Gate;

    internal static void Export(string root, Stream destination, string version)
    {
        lock (Gate)
        {
            // Finish all reads and validation before writing even the manifest.
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var manifestFiles = new List<SettingsArchiveFile>();
            long total = 0;
            foreach (var store in SettingsArchiveRegistry.Enumerate(root))
            {
                byte[] bytes = ReadBoundedFile(SettingsArchiveRegistry.Destination(root, store.Path), store.MaximumBytes);
                SettingsArchiveValidator.Validate(store, bytes);
                if (!files.TryAdd(store.Path, bytes)) throw new InvalidDataException("Duplicate settings path.");
                manifestFiles.Add(new(store.Path, store.Category));
                total += bytes.Length;
                if (files.Count > MaximumFiles || total > MaximumTotalBytes) throw new InvalidDataException("Settings exceed archive limits.");
            }
            using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            using (var output = zip.CreateEntry(ManifestName).Open())
                JsonSerializer.Serialize(output, new SettingsArchiveManifest(1, "Project Prime", version, manifestFiles), SettingsArchiveManifest.Json);
            foreach (var (path, bytes) in files)
            {
                using var output = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
                output.Write(bytes);
            }
        }
    }

    internal static void ExportFile(string root, string path, string version)
    {
        string full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(full), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a .zip filename for the settings archive.");
        if (SettingsArchiveRegistry.Enumerate(root).Any(s => string.Equals(
            SettingsArchiveRegistry.Destination(root, s.Path), full, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The archive cannot replace a settings store.");
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { Export(root, file, version); file.Flush(true); }
            File.Move(temporary, full, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static Dictionary<string, byte[]> Read(Stream source)
    {
        // Buffer with a compressed-byte limit; supports Android content-provider streams too.
        using var bounded = new MemoryStream(ReadBounded(source, MaximumArchiveBytes));
        using var zip = new ZipArchive(bounded, ZipArchiveMode.Read);
        if (zip.Entries.Count > MaximumFiles + 1) throw new InvalidDataException("Too many archive entries.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            SettingsArchiveRegistry.ValidatePath(entry.FullName);
            if (!entries.TryAdd(entry.FullName, entry)) throw new InvalidDataException("Duplicate archive entry.");
            // UNIX links, directories and special files are never settings.
            int kind = (entry.ExternalAttributes >> 16) & 0xf000;
            if (kind != 0 && kind != 0x8000 || (entry.ExternalAttributes & 0x10) != 0)
                throw new InvalidDataException("Only regular settings files are allowed.");
        }
        if (!entries.TryGetValue(ManifestName, out var manifestEntry) || manifestEntry.FullName != ManifestName)
            throw new InvalidDataException("Missing manifest.json.");
        SettingsArchiveManifest manifest;
        using (var stream = manifestEntry.Open())
        {
            string text = SettingsArchiveValidator.Utf8.GetString(ReadBounded(stream, 64 * 1024));
            manifest = JsonSerializer.Deserialize<SettingsArchiveManifest>(text, SettingsArchiveManifest.Json)
                ?? throw new InvalidDataException("Invalid manifest.");
            using var json = JsonDocument.Parse(text);
            RejectManifestDuplicates(json.RootElement);
        }
        if (manifest.Format != 1 || manifest.Product != "Project Prime" || manifest.Files == null
            || manifest.Files.Count > MaximumFiles || string.IsNullOrWhiteSpace(manifest.CreatedByVersion)
            || manifest.CreatedByVersion.Length > 128) throw new InvalidDataException("Unsupported settings archive.");
        if (entries.Count != manifest.Files.Count + 1) throw new InvalidDataException("Manifest does not describe every entry.");
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var item in manifest.Files)
        {
            if (item == null) throw new InvalidDataException("Invalid manifest entry.");
            var store = SettingsArchiveRegistry.Resolve(item.Path);
            if (store.Category != item.Category || !entries.TryGetValue(item.Path, out var entry) || entry.FullName != item.Path)
                throw new InvalidDataException("Settings entry does not match manifest.");
            if (entry.Length > store.MaximumBytes || (total += entry.Length) > MaximumTotalBytes)
                throw new InvalidDataException("Settings exceed archive limits.");
            using var stream = entry.Open();
            byte[] bytes = ReadBounded(stream, store.MaximumBytes);
            if (bytes.Length != entry.Length) throw new InvalidDataException("Incorrect entry length.");
            SettingsArchiveValidator.Validate(store, bytes);
            if (!result.TryAdd(item.Path, bytes)) throw new InvalidDataException("Duplicate manifest path.");
        }
        return result;
    }

    private static void RejectManifestDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in element.EnumerateObject())
            { if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate manifest property."); RejectManifestDuplicates(p.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) RejectManifestDuplicates(value);
    }

    internal static void Import(string root, Stream source, Action<int>? beforeInstall = null)
    {
        lock (Gate)
        {
            var files = Read(source); // No directories or destination files touched before complete validation.
            Install(root, files.ToDictionary(pair => pair.Key, pair => (byte[]?)pair.Value), beforeInstall);
        }
    }

    internal static void Reset(string root, Action<int>? beforeInstall = null)
    {
        lock (Gate)
        {
            var files = SettingsArchiveRegistry.Enumerate(root).ToDictionary(store => store.Path, _ => (byte[]?)null);
            // HUD's loader falls back to its normal .json.bak sidecar. Remove those
            // in the same rollback transaction, including orphaned backups, or reset
            // could silently resurrect the old layout. Archive recovery files remain.
            string profiles = SettingsArchiveRegistry.Destination(root, "Savedata/hud-profiles");
            if (Directory.Exists(profiles))
                foreach (string backup in Directory.EnumerateFiles(profiles, "*.json.bak"))
                {
                    string relative = "Savedata/hud-profiles/" + Path.GetFileName(backup);
                    SettingsArchiveRegistry.Resolve(relative[..^4]);
                    files.Add(relative, null);
                }
            if (files.Count > MaximumFiles * 2) throw new InvalidDataException("Too many settings stores to reset.");
            Install(root, files, beforeInstall);
        }
    }

    private static void Install(string root, Dictionary<string, byte[]?> files, Action<int>? beforeInstall)
    {
            var transaction = new List<(string Destination, string Staged, string Backup, bool Existed, bool Delete)>();
            int installed = 0;
            bool preserveRecovery = false;
            var createdDirectories = new List<string>();
            try
            {
                long backupBytes = 0;
                foreach (var (path, bytes) in files)
                {
                    string destination = SettingsArchiveRegistry.Destination(root, path);
                    string directory = Path.GetDirectoryName(destination)!;
                    var missing = new Stack<string>();
                    for (string? d = directory; d != null && !Directory.Exists(d); d = Path.GetDirectoryName(d)) missing.Push(d);
                    while (missing.TryPop(out string? d)) { Directory.CreateDirectory(d); createdDirectories.Add(d); }
                    string suffix = ".archive-" + Guid.NewGuid().ToString("N");
                    var entry = (Destination: destination, Staged: destination + suffix + ".tmp", Backup: destination + suffix + ".bak", Existed: File.Exists(destination), Delete: bytes == null);
                    transaction.Add(entry);
                    if (bytes != null) WriteDurable(entry.Staged, bytes);
                    if (entry.Existed)
                    {
                        byte[] original = ReadBoundedFile(destination, MaximumTotalBytes);
                        if ((backupBytes += original.Length) > MaximumTotalBytes) throw new InvalidDataException("Existing settings exceed rollback limits.");
                        WriteDurable(entry.Backup, original);
                    }
                }
                foreach (var entry in transaction)
                {
                    beforeInstall?.Invoke(installed);
                    // Recheck containment immediately before replacement.
                    SettingsArchiveRegistry.Destination(root, Path.GetRelativePath(root, entry.Destination).Replace('\\', '/'));
                    if (!entry.Delete) File.Move(entry.Staged, entry.Destination, true);
                    else File.Delete(entry.Destination);
                    installed++;
                }
            }
            catch (Exception error)
            {
                var failures = new List<Exception> { error };
                for (int i = installed - 1; i >= 0; i--)
                {
                    var entry = transaction[i];
                    try
                    {
                        SettingsArchiveRegistry.Destination(root, Path.GetRelativePath(root, entry.Destination).Replace('\\', '/'));
                        if (entry.Existed) File.Move(entry.Backup, entry.Destination, true);
                        else File.Delete(entry.Destination);
                    }
                    catch (Exception rollback) { failures.Add(rollback); }
                }
                if (failures.Count > 1)
                {
                    preserveRecovery = true;
                    throw new AggregateException("Restore failed and rollback could not finish. Recovery files were retained beside the settings.", failures);
                }
                throw;
            }
            finally
            {
                if (!preserveRecovery)
                    foreach (var entry in transaction) { TryDelete(entry.Staged); TryDelete(entry.Backup); }
                for (int i = createdDirectories.Count - 1; i >= 0; i--)
                    try { if (!Directory.EnumerateFileSystemEntries(createdDirectories[i]).Any()) Directory.Delete(createdDirectories[i]); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
            }
    }
    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private static void WriteDurable(string path, byte[] bytes)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes); file.Flush(true);
    }
    private static byte[] ReadBoundedFile(string path, int limit)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > limit) throw new InvalidDataException("Settings store exceeds its size limit.");
        return ReadBounded(file, limit);
    }
    private static byte[] ReadBounded(Stream source, int limit)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Settings data exceeds its size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
