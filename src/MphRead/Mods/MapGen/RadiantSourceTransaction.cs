using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

public enum RadiantSourceCommitPoint { JournalStaged, SourceCommitted, SidecarCommitted, AdjunctCommitted }

/// <summary>A recoverable source workspace, including optional native authoring metadata. Recovery only
/// replaces bytes proven to belong to this transaction; external edits win.</summary>
public static class RadiantSourceTransaction
{
    // JSON journals base64-encode both bounded source and native metadata.
    private const long MaxJournal = 184L * 1024 * 1024;
    private sealed record Journal(int Version, string Source, string Sidecar,
        string? PreviousSourceHash, string? PreviousSidecarHash,
        byte[] SourceBytes, byte[] SidecarBytes, string? Adjunct = null,
        byte[]? AdjunctBytes = null, string? PreviousAdjunctHash = null);

    public static FileStream Acquire(string source)
    {
        source = Path.GetFullPath(source);
        RejectLinks(source);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        RejectLinks(source + ".prime-lock");
        return new FileStream(source + ".prime-lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }

    public static void Recover(string source, string sidecar)
    {
        using var lease = Acquire(source);
        RecoverUnderLease(source, sidecar);
    }

    internal static void RecoverUnderLease(string source, string sidecar)
    {
        (source, sidecar) = ValidatePaths(source, sidecar);
        string pending = sidecar + ".prime-transaction";
        RejectLinks(pending);
        if (!File.Exists(pending)) return;
        if (new FileInfo(pending).Length > MaxJournal)
            throw new InvalidDataException("Radiant recovery journal is oversized; preserve it for manual recovery.");
        Journal journal;
        try { journal = JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(pending))
            ?? throw new InvalidDataException("Invalid Radiant recovery journal."); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid Radiant recovery journal.", ex); }
        if (journal.Version is not (1 or 2) || journal.Source != Path.GetFileName(source)
            || journal.Sidecar != Path.GetFileName(sidecar)
            || journal.SourceBytes == null || journal.SidecarBytes == null
            || journal.SourceBytes.Length > 64 * 1024 * 1024
            || journal.SidecarBytes.Length > 65536
            || !ValidHash(journal.PreviousSourceHash) || !ValidHash(journal.PreviousSidecarHash)
            || !ValidHash(journal.PreviousAdjunctHash))
            throw new InvalidDataException("Radiant recovery ownership or bounds are invalid.");
        ValidatePayload(journal);
        Complete(source, sidecar, journal, null);
        File.Delete(pending);
    }

    /// <summary>Caller holds Acquire throughout validation and Commit. Fault
    /// callback is intended for deterministic crash-point acceptance tests.</summary>
    public static void CommitUnderLease(string source, string sidecar,
        byte[] sourceBytes, byte[] sidecarBytes,
        string? previousSourceHash, string? previousSidecarHash,
        Action<RadiantSourceCommitPoint>? fault = null,
        string? adjunctPath = null, byte[]? adjunctBytes = null, string? previousAdjunctHash = null)
    {
        (source, sidecar) = ValidatePaths(source, sidecar);
        if (sourceBytes.Length > 64 * 1024 * 1024 || sidecarBytes.Length > 65536)
            throw new InvalidDataException("Radiant source pair exceeds its bounds.");
        if (File.Exists(sidecar + ".prime-transaction"))
            throw new IOException("Recover the pending Radiant source transaction before exporting.");
        RejectLinks(sidecar + ".prime-transaction");
        if ((adjunctPath == null) != (adjunctBytes == null)
            || adjunctPath != null && !Path.GetFullPath(adjunctPath).Equals(source + ".prime-source.json", StringComparison.Ordinal))
            throw new InvalidDataException("Native adjunct requires the matching source workspace path.");
        var journal = new Journal(adjunctBytes == null ? 1 : 2, Path.GetFileName(source), Path.GetFileName(sidecar),
            previousSourceHash?.ToUpperInvariant(), previousSidecarHash?.ToUpperInvariant(), sourceBytes, sidecarBytes,
            adjunctPath == null ? null : Path.GetFileName(adjunctPath), adjunctBytes, previousAdjunctHash?.ToUpperInvariant());
        ValidatePayload(journal);
        WriteDurable(sidecar + ".prime-transaction", JsonSerializer.SerializeToUtf8Bytes(journal));
        fault?.Invoke(RadiantSourceCommitPoint.JournalStaged);
        Complete(source, sidecar, journal, fault);
        File.Delete(sidecar + ".prime-transaction");
    }

    private static void ValidatePayload(Journal journal)
    {
        var identity = QuakeMapInterchange.ReadIdentityBytes(journal.SidecarBytes);
        if (!identity.SourceSha256.Equals(Hash(journal.SourceBytes), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Radiant transaction source and identity fingerprint differ.");
        MapValidator.RequireRuntimeName(identity.RoomName);
        if (!ValidHash(journal.PreviousSourceHash) || !ValidHash(journal.PreviousSidecarHash)
            || !ValidHash(journal.PreviousAdjunctHash))
            throw new InvalidDataException("Invalid prior source workspace fingerprint.");
        if (journal.Version == 1)
        {
            if (journal.Adjunct != null || journal.AdjunctBytes != null || journal.PreviousAdjunctHash != null)
                throw new InvalidDataException("Unexpected native adjunct in a legacy source-pair transaction.");
        }
        else if (journal.Version == 2)
        {
            if (journal.Adjunct != journal.Source + ".prime-source.json" || journal.AdjunctBytes == null
                || journal.AdjunctBytes.Length is <= 0 or > 64 * 1024 * 1024
                || journal.Adjunct == journal.Sidecar)
                throw new InvalidDataException("Invalid native workspace recovery metadata.");
            RadiantSourceWorkspace.ValidateManifestBytes(journal.AdjunctBytes, identity.MapId, identity.SourceSha256);
        }
        else throw new InvalidDataException("Unsupported source workspace recovery version.");
    }

    private static void Complete(string source, string sidecar, Journal journal,
        Action<RadiantSourceCommitPoint>? fault)
    {
        string nextSource = Hash(journal.SourceBytes), nextSidecar = Hash(journal.SidecarBytes);
        string? currentSource = HashFile(source), currentSidecar = HashFile(sidecar);
        string? adjunct = journal.Adjunct == null ? null : Path.Combine(Path.GetDirectoryName(source)!, journal.Adjunct);
        if (adjunct != null) RejectLinks(adjunct);
        string? currentAdjunct = adjunct == null ? null : HashFile(adjunct);
        string? nextAdjunct = journal.AdjunctBytes == null ? null : Hash(journal.AdjunctBytes);
        // Validate every workspace member before touching any. A save after a crash is
        // never discarded in order to make recovery appear successful.
        if ((currentSource != journal.PreviousSourceHash && currentSource != nextSource)
            || (currentSidecar != journal.PreviousSidecarHash && currentSidecar != nextSidecar)
            || (currentAdjunct != journal.PreviousAdjunctHash && currentAdjunct != nextAdjunct))
            throw new IOException("Radiant recovery conflict: external edits were preserved. "
                + "Keep source, sidecar and .prime-transaction for explicit reconciliation.");
        if (currentSource != nextSource) WriteDurable(source, journal.SourceBytes);
        fault?.Invoke(RadiantSourceCommitPoint.SourceCommitted);
        if (HashFile(source) != nextSource || HashFile(sidecar) != currentSidecar
            || adjunct != null && HashFile(adjunct) != currentAdjunct)
            throw new IOException("Radiant source pair changed during commit; recovery journal retained.");
        if (currentSidecar != nextSidecar) WriteDurable(sidecar, journal.SidecarBytes);
        fault?.Invoke(RadiantSourceCommitPoint.SidecarCommitted);
        if (HashFile(source) != nextSource || HashFile(sidecar) != nextSidecar
            || adjunct != null && HashFile(adjunct) != currentAdjunct)
            throw new IOException("Radiant source workspace changed during commit; recovery journal retained.");
        if (adjunct != null)
        {
            if (currentAdjunct != nextAdjunct) WriteDurable(adjunct, journal.AdjunctBytes!);
            fault?.Invoke(RadiantSourceCommitPoint.AdjunctCommitted);
            if (HashFile(source) != nextSource || HashFile(sidecar) != nextSidecar || HashFile(adjunct) != nextAdjunct)
                throw new IOException("Radiant source workspace changed during native metadata commit; recovery journal retained.");
        }
    }

    private static (string, string) ValidatePaths(string source, string sidecar)
    {
        source = Path.GetFullPath(source); sidecar = Path.GetFullPath(sidecar);
        if (source.Equals(sidecar, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetDirectoryName(source), Path.GetDirectoryName(sidecar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Radiant source and sidecar require distinct files in one directory.");
        RejectLinks(source); RejectLinks(sidecar);
        return (source, sidecar);
    }

    private static void RejectLinks(string path)
    {
        // Validate the destinations and workspace owner, not system aliases
        // such as macOS /var -> /private/var used by the temporary directory.
        foreach (string? current in new[] { path, Path.GetDirectoryName(path) })
        {
            if (current == null) continue;
            try
            {
                if (new FileInfo(current).LinkTarget != null
                    || File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("Radiant source files and their owning directory must not be symbolic links.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private static bool ValidHash(string? hash) => hash == null || (hash.Length == 64
        && System.Linq.Enumerable.All(hash, Uri.IsHexDigit));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string? HashFile(string path) => File.Exists(path) ? MapBuildFingerprint.HashFile(path).ToUpperInvariant() : null;
    private static void WriteDurable(string path, byte[] bytes)
    {
        RejectLinks(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(bytes); output.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
