using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MphRead.Mods.Update;

/// <summary>Shared desktop/server release ownership and recoverable file publication.
/// Incoming files are verified and flushed before any installed file is moved.
/// The journal precedes mutations; the ownership manifest is published last.</summary>
internal static class ReleaseInstallation
{
    internal const string ManifestName = ".project-prime-files.json";
    private const string TransactionName = ".project-prime-update-transaction";
    private const string LockName = ".project-prime-update.lock";
    private const int MaximumManifestBytes = 8 * 1024 * 1024;
    private const int MaximumFiles = 100000;
    internal sealed record Manifest(int Version, string[] Files, Dictionary<string, string>? Hashes = null);
    private sealed record Operation(string Path, bool HadOriginal, bool Install);
    private sealed record Journal(int Version, Operation[] Operations,
        string[] OriginalDirectories, string[] CreatedDirectories);
    private static readonly StringComparer Names = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // Reject ambiguous portable paths before combining them. Then reject every
    // existing reparse point, including an entry whose target is currently absent.
    internal static string OwnedPath(string root, string relative, bool internalPath = false)
    {
        if (String.IsNullOrEmpty(relative) || relative.Length > 1024 || relative.Contains('\\')
            || relative.Contains(':') || Path.IsPathRooted(relative))
            throw new InvalidDataException("Invalid release path.");
        string[] components = relative.Split('/');
        if (components.Any(c => c.Length == 0 || c is "." or ".." || c.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || c.EndsWith(' ') || c.EndsWith('.'))
            || !internalPath && (components[0].Equals(TransactionName, StringComparison.OrdinalIgnoreCase)
                || components[0].Equals(LockName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Invalid release path.");
        string path = Path.GetFullPath(root);
        foreach (string component in components)
        {
            path = Path.Combine(path, component);
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Release paths cannot cross symbolic links.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return path;
    }

    internal static Manifest? ReadManifest(string root)
    {
        string path = OwnedPath(root, ManifestName);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaximumManifestBytes) throw new InvalidDataException("Release manifest is too large.");
        Manifest? manifest;
        try { manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(path)); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid release manifest.", ex); }
        if (manifest?.Version != 1 || manifest.Files == null || manifest.Files.Length > MaximumFiles)
            throw new InvalidDataException("Unsupported release manifest.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string relative in manifest.Files)
        {
            OwnedPath(root, relative);
            if (relative.Equals(ManifestName, StringComparison.OrdinalIgnoreCase) || !seen.Add(relative))
                throw new InvalidDataException("Ambiguous release ownership.");
            if (manifest.Hashes != null && (!manifest.Hashes.TryGetValue(relative, out string? hash)
                || hash.Length != 64 || !hash.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Invalid release digest.");
        }
        return manifest;
    }

    internal static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static void EnsureManifest(string root)
    {
        if (ReadManifest(root) != null) return;
        var files = new List<string>();
        void Walk(string directory)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                OwnedPath(root, relative);
                if (Directory.Exists(path)) Walk(path);
                else if (relative != ManifestName) files.Add(relative);
                if (files.Count > MaximumFiles) throw new InvalidDataException("Too many release files.");
            }
        }
        Walk(root);
        files.Sort(StringComparer.Ordinal);
        var manifest = new Manifest(1, files.ToArray(), files.ToDictionary(p => p, p => Hash(OwnedPath(root, p)), Names));
        DurableWrite(OwnedPath(root, ManifestName), JsonSerializer.SerializeToUtf8Bytes(manifest));
        _ = ReadManifest(root);
    }

    internal static void WaitForExit(int pid, int timeoutMs = 30000)
    {
        if (pid <= 0 || timeoutMs < 0) throw new ArgumentOutOfRangeException(nameof(pid));
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.WaitForExit(timeoutMs))
                throw new TimeoutException($"Process {pid} is still running; the installation was not changed.");
        }
        catch (ArgumentException) { } // The old process has already exited.
    }

    internal static void Apply(string source, string target, Action<int>? afterMutation = null)
    {
        source = Path.GetFullPath(source); target = Path.GetFullPath(target);
        Directory.CreateDirectory(target);
        using var held = Acquire(target);
        RecoverCore(target);
        EnsureManifest(source);
        var next = ReadManifest(source)!;
        var previous = ReadManifest(target);
        // Do not permit a backport of the old standalone game to silently
        // remove Project Prime Studio from a later paired installation.
        // A v0.1.52 -> v0.1.47 switch is therefore rejected before changes.
        bool incomingStudio = next.Files.Any(p => Path.GetFileName(p) is
            "ProjectPrimeStudio" or "ProjectPrimeStudio.exe");
        bool installedStudio = previous?.Files.Any(p => Path.GetFileName(p) is
                "ProjectPrimeStudio" or "ProjectPrimeStudio.exe") == true
            || File.Exists(Path.Combine(target, "ProjectPrimeStudio"))
            || File.Exists(Path.Combine(target, "ProjectPrimeStudio.exe"));
        if (installedStudio && !incomingStudio)
            throw new InvalidDataException("The selected release does not include the paired Project Prime Studio. Extract the older release into a separate folder.");
        if (incomingStudio)
        {
            string paired = Path.Combine(source, ".project-prime-desktop.json");
            if (!next.Files.Contains(".project-prime-desktop.json", Names) || !File.Exists(paired))
                throw new InvalidDataException("The paired desktop release is missing compatibility metadata.");
            using var pairDocument = JsonDocument.Parse(File.ReadAllText(paired));
            JsonElement pairRoot = pairDocument.RootElement;
            if (pairRoot.ValueKind != JsonValueKind.Object
                || !pairRoot.TryGetProperty("GameVersion", out JsonElement gameVersion)
                || !pairRoot.TryGetProperty("StudioVersion", out JsonElement studioVersion)
                || !String.Equals(gameVersion.GetString(), studioVersion.GetString(), StringComparison.Ordinal)
                || String.IsNullOrWhiteSpace(gameVersion.GetString()))
                throw new InvalidDataException("Game and Project Prime Studio release versions do not match.");
        }
        var keep = next.Files.ToHashSet(Names);
        var obsolete = previous?.Files.Where(p => !keep.Contains(p))
            ?? Directory.EnumerateFiles(target).Select(Path.GetFileName).Where(p => p != null && Legacy(p) && !keep.Contains(p)).Select(p => p!);
        var operations = obsolete.OrderBy(p => p, StringComparer.Ordinal).Select(p => new Operation(p, File.Exists(OwnedPath(target, p)), false))
            .Concat(next.Files.OrderBy(p => p, StringComparer.Ordinal).Select(p => new Operation(p, File.Exists(OwnedPath(target, p)), true)))
            .Append(new(ManifestName, File.Exists(OwnedPath(target, ManifestName)), true)).ToArray();
        // Validate the complete target set and shape transitions before creating
        // transaction state. Only previous release-owned directories may be removed.
        foreach (var op in operations) OwnedPath(target, op.Path);
        var oldFiles = (previous?.Files ?? Array.Empty<string>()).ToHashSet(Names);
        var oldDirectories = Parents(oldFiles).ToHashSet(Names);
        var requiredDirectories = Parents(next.Files).ToHashSet(Names);
        var obsoleteFiles = operations.Where(op => !op.Install).Select(op => op.Path).ToHashSet(Names);
        foreach (string relative in requiredDirectories)
            if (File.Exists(OwnedPath(target, relative)) && !obsoleteFiles.Contains(relative))
                throw new IOException("An unowned file obstructs the new release directory: " + relative);
        foreach (var op in operations.Where(op => op.Install))
        {
            string destination = OwnedPath(target, op.Path);
            if (!Directory.Exists(destination)) continue;
            if (!oldDirectories.Contains(op.Path)) throw new IOException("An unowned directory obstructs the new release file: " + op.Path);
            void CheckTree(string directory)
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    string relative = Path.GetRelativePath(target, entry).Replace('\\', '/');
                    OwnedPath(target, relative);
                    if (Directory.Exists(entry))
                    {
                        if (!oldDirectories.Contains(relative)) throw new IOException("An unowned directory obstructs the release: " + relative);
                        CheckTree(entry);
                    }
                    else if (!obsoleteFiles.Contains(relative)) throw new IOException("An unowned file obstructs the release: " + relative);
                }
            }
            CheckTree(destination);
        }
        var possibleDirectories = Parents(operations.Select(op => op.Path)).Concat(oldDirectories).Distinct(Names).ToArray();
        string[] originalDirectories = possibleDirectories.Where(p => Directory.Exists(OwnedPath(target, p))).ToArray();
        string[] createdDirectories = requiredDirectories.Where(p => !Directory.Exists(OwnedPath(target, p))).ToArray();
        string transaction = OwnedPath(target, TransactionName, internalPath: true);
        Directory.CreateDirectory(transaction);
        int mutation = 0;
        try
        {
            foreach (var op in operations.Where(o => o.Install))
            {
                string from = OwnedPath(source, op.Path);
                string incoming = OwnedPath(transaction, "incoming/" + op.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(incoming)!);
                using (var input = File.OpenRead(from))
                using (var output = new FileStream(incoming, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { input.CopyTo(output); output.Flush(flushToDisk: true); }
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(incoming, File.GetUnixFileMode(from));
                if (op.Path != ManifestName && next.Hashes != null
                    && !Hash(incoming).Equals(next.Hashes[op.Path], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Staged release digest mismatch: " + op.Path);
            }
            if (originalDirectories.Length > 2 * MaximumFiles || createdDirectories.Length > 2 * MaximumFiles)
                throw new InvalidDataException("Update directory journal exceeds its count budget.");
            byte[] journalBytes = JsonSerializer.SerializeToUtf8Bytes(new Journal(1, operations, originalDirectories, createdDirectories));
            if (journalBytes.Length > 2 * MaximumManifestBytes) throw new InvalidDataException("Update journal exceeds its byte budget.");
            DurableWrite(OwnedPath(transaction, "journal.json"), journalBytes);
            bool obsoleteComplete = false;
            foreach (var op in operations)
            {
                if (op.Install && !obsoleteComplete)
                {
                    PruneEmptyDirectories(target, oldDirectories.Where(p => !requiredDirectories.Contains(p)));
                    obsoleteComplete = true;
                }
                string destination = OwnedPath(target, op.Path);
                if (op.HadOriginal)
                {
                    string backup = OwnedPath(transaction, "backup/" + op.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Move(destination, backup);
                    afterMutation?.Invoke(++mutation);
                }
                if (op.Install)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(OwnedPath(transaction, "incoming/" + op.Path), destination);
                    afterMutation?.Invoke(++mutation);
                }
            }
            DurableWrite(OwnedPath(transaction, "committed"), new byte[] { 1 });
        }
        catch
        {
            RecoverCore(target);
            throw;
        }
        // A running Windows image can keep its backup open until process exit.
        // Committed backups may be cleaned at the next startup; never roll back
        // a successfully committed installation just because cleanup was denied.
        Cleanup(transaction);
    }

    internal static void Recover(string target)
    {
        if (!Directory.Exists(target)) return;
        using var held = Acquire(target);
        RecoverCore(Path.GetFullPath(target));
    }

    private static FileStream Acquire(string target)
        => new(OwnedPath(target, LockName, internalPath: true), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static void RecoverCore(string target)
    {
        string transaction = OwnedPath(target, TransactionName, internalPath: true);
        if (!Directory.Exists(transaction)) return;
        string journalPath = OwnedPath(transaction, "journal.json");
        if (!File.Exists(OwnedPath(transaction, "committed")) && File.Exists(journalPath))
        {
            if (new FileInfo(journalPath).Length > 2L * MaximumManifestBytes) throw new InvalidDataException("Update journal is too large.");
            Journal? journal;
            try { journal = JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(journalPath)); }
            catch (JsonException ex) { throw new InvalidDataException("Invalid update journal; recovery is required.", ex); }
            if (journal?.Version != 1 || journal.Operations == null || journal.Operations.Length > 2 * MaximumFiles + 1
                || journal.OriginalDirectories == null || journal.CreatedDirectories == null
                || journal.OriginalDirectories.Length > 2 * MaximumFiles || journal.CreatedDirectories.Length > 2 * MaximumFiles)
                throw new InvalidDataException("Unsupported update journal; recovery is required.");
            var seen = new HashSet<string>(Names);
            foreach (var op in journal.Operations)
            {
                OwnedPath(target, op.Path);
                if (!seen.Add(op.Path)) throw new InvalidDataException("Ambiguous update journal.");
            }
            foreach (string relative in journal.OriginalDirectories.Concat(journal.CreatedDirectories)) OwnedPath(target, relative);
            // Remove published files first, then their newly-created empty
            // directories, before a backup can restore a file at that path.
            foreach (var op in journal.Operations.Reverse())
            {
                string destination = OwnedPath(target, op.Path);
                string backup = OwnedPath(transaction, "backup/" + op.Path);
                if (op.Install && (File.Exists(backup) || !op.HadOriginal
                    && !File.Exists(OwnedPath(transaction, "incoming/" + op.Path))))
                    File.Delete(destination);
            }
            PruneEmptyDirectories(target, journal.CreatedDirectories);
            foreach (var op in journal.Operations.Reverse())
            {
                string backup = OwnedPath(transaction, "backup/" + op.Path);
                if (!File.Exists(backup)) continue;
                string destination = OwnedPath(target, op.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(backup, destination);
            }
            foreach (string relative in journal.OriginalDirectories)
                Directory.CreateDirectory(OwnedPath(target, relative));
        }
        Cleanup(transaction);
        if (Directory.Exists(transaction) && !File.Exists(OwnedPath(transaction, "committed")))
            throw new IOException("The previous update could not be recovered completely.");
        if (Directory.Exists(transaction))
            throw new IOException("A committed update backup is still in use. Restart before applying another update.");
    }

    private static IEnumerable<string> Parents(IEnumerable<string> paths)
    {
        foreach (string relative in paths)
        {
            int slash = relative.LastIndexOf('/');
            while (slash > 0)
            {
                string parent = relative[..slash]; yield return parent;
                slash = parent.LastIndexOf('/');
            }
        }
    }
    private static void PruneEmptyDirectories(string root, IEnumerable<string> paths)
    {
        foreach (string relative in paths.Distinct(Names).OrderByDescending(p => p.Length))
        {
            string path = OwnedPath(root, relative);
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
        }
    }

    private static void Cleanup(string directory)
    {
        // Validate the entire tree before recursive deletion can follow an
        // injected link or publish a partially validated journal.
        if (!Directory.Exists(directory)) return;
        void Validate(string path)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(path))
            {
                OwnedPath(directory, Path.GetRelativePath(directory, entry).Replace('\\', '/'));
                if (Directory.Exists(entry)) Validate(entry);
            }
        }
        Validate(directory);
        try
        {
            // Keep both control files until all potentially mapped backups
            // have been removed. A denied delete must preserve commit identity.
            foreach (string child in Directory.EnumerateFileSystemEntries(directory))
            {
                if (Path.GetFileName(child) is "journal.json" or "committed") continue;
                if (Directory.Exists(child)) Directory.Delete(child, recursive: true);
                else File.Delete(child);
            }
            File.Delete(Path.Combine(directory, "journal.json"));
            File.Delete(Path.Combine(directory, "committed"));
            Directory.Delete(directory);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void DurableWrite(string path, byte[] bytes)
    {
        string temporary = path + ".writing";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { stream.Write(bytes); stream.Flush(flushToDisk: true); }
        File.Move(temporary, path);
    }

    private static bool Legacy(string name)
        => new[] { "MphRead", "FruityPrime", "PrimeHuntersOnline" }.Any(prefix =>
            name.Equals(prefix, StringComparison.OrdinalIgnoreCase) || name.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase));
}
