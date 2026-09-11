using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace MphRead.Mods.Update;

public enum UpdateTransactionState
{
    Applying,
    Committed,
    RolledBack,
    RecoveryRequired,
    OldProcessStillRunning
}

public sealed record UpdateTransactionFile(
    string Path,
    bool WasPresent,
    string? BackupPath,
    string? BackupSha256,
    string? ExpectedSha256);

public sealed record UpdateTransactionJournal(
    int SchemaVersion,
    string FromVersion,
    string ToVersion,
    UpdateTransactionState State,
    string StagedDirectory,
    IReadOnlyList<UpdateTransactionFile> Files);

public sealed record UpdateTransactionResult(
    bool Success,
    UpdateTransactionState State,
    string? Error)
{
    public bool RolledBack => State == UpdateTransactionState.RolledBack;
}

/// <summary>A process-wide lock that is never removed while held.</summary>
internal sealed class UpdateInstallationLock : IDisposable
{
    private readonly FileStream _stream;

    private UpdateInstallationLock(FileStream stream) => _stream = stream;

    public static UpdateInstallationLock? TryAcquire(string updateDirectory)
    {
        try
        {
            if (Directory.Exists(updateDirectory)
                && UpdateFileSystem.IsLinkOrReparse(updateDirectory))
                return null;
            Directory.CreateDirectory(updateDirectory);
            string path = Path.Combine(updateDirectory, "update.lock");
            FileStream stream = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 1, FileOptions.WriteThrough);
            return new UpdateInstallationLock(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}

/// <summary>Small filesystem helpers used by staging, apply, and recovery.</summary>
internal static class UpdateFileSystem
{
    internal static string Sha256(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream))
            .ToLowerInvariant();
    }

    internal static bool IsLinkOrReparse(string path)
    {
        try
        {
            FileAttributes attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReparsePoint) != 0
                || (File.Exists(path) && new FileInfo(path).LinkTarget != null);
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (UnauthorizedAccessException) { return true; }
    }

    internal static string FullPathUnder(string root, string relative)
    {
        if (!ReleaseFilesManifestValidator.IsSafeRelativePath(relative)
            && !relative.Equals("release-files.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"unsafe update path '{relative}'");
        string fullRoot = Path.GetFullPath(root);
        string full = Path.GetFullPath(Path.Combine(fullRoot,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("update path escapes its root");
        return full;
    }

    internal static void EnsureDirectoryChainSafe(string root, string directory)
    {
        string fullRoot = Path.GetFullPath(root);
        string full = Path.GetFullPath(directory);
        string prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!full.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("update directory escapes its root");
        string relative = Path.GetRelativePath(fullRoot, full);
        if (relative == ".") return;
        string current = fullRoot;
        foreach (string component in relative.Split(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            if (File.Exists(current) || Directory.Exists(current))
            {
                if (IsLinkOrReparse(current))
                    throw new InvalidDataException("update path traverses a link or reparse point");
            }
            else
            {
                Directory.CreateDirectory(current);
            }
        }
    }

    internal static void FlushFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1, FileOptions.WriteThrough);
        stream.Flush(flushToDisk: true);
    }

    internal static void AtomicWrite(string path, ReadOnlySpan<byte> bytes)
    {
        string? parent = Path.GetDirectoryName(path);
        if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void CopyDurably(string source, string destination)
    {
        string? parent = Path.GetDirectoryName(destination);
        if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        using (FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan))
        using (FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 64 * 1024, FileOptions.WriteThrough))
        {
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
        }
        PreserveUnixMode(source, destination);
    }

    internal static void PreserveUnixMode(string source, string destination)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
    }

    internal static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}

/// <summary>
/// Applies a staged desktop release as a journaled per-file transaction.
/// It intentionally does not claim whole-directory atomicity: every mutation
/// is recoverable through the durable journal and backups.
/// </summary>
public sealed class DesktopUpdateTransaction
{
    public const int JournalSchemaVersion = 1;
    public const string ReleaseFilesName = "release-files.json";
    public static readonly TimeSpan DefaultProcessWait = TimeSpan.FromSeconds(30);

    private readonly string _installation;
    private readonly string _staged;
    private readonly TimeSpan _processWait;
    private readonly Func<int, TimeSpan, bool>? _waitForProcess;
    private readonly Func<string, string, Exception?>? _failureInjector;

    public DesktopUpdateTransaction(string installationDirectory, string stagedDirectory,
        TimeSpan? processWait = null,
        Func<int, TimeSpan, bool>? waitForProcess = null,
        Func<string, string, Exception?>? failureInjector = null)
    {
        _installation = Path.GetFullPath(installationDirectory);
        _staged = Path.GetFullPath(stagedDirectory);
        _processWait = processWait ?? DefaultProcessWait;
        _waitForProcess = waitForProcess;
        _failureInjector = failureInjector;
    }

    public UpdateTransactionResult Apply(string fromVersion, string toVersion, int oldPid = -1)
    {
        string updateDirectory = Path.Combine(_installation, ".update");
        UpdateInstallationLock? installationLock = UpdateInstallationLock.TryAcquire(updateDirectory);
        if (installationLock == null)
            return new(false, UpdateTransactionState.RolledBack, "another update is already running");
        using (installationLock)
        {
            try
            {
                string journalPath = Path.Combine(updateDirectory, "transaction.json");
                if (!WaitForOldProcess(oldPid))
                    return new(false, UpdateTransactionState.OldProcessStillRunning,
                        "the previous Project Prime process did not exit in time");
                if (!File.Exists(journalPath))
                    ClearStaleBackup(updateDirectory);
                ReleaseFilesManifest manifest = LoadStagedManifest();
                string oldManifestPath = Path.Combine(_installation, ReleaseFilesName);
                ReleaseFilesManifest? oldManifest = TryLoadOldManifest(oldManifestPath);
                string stagedReleaseFiles = UpdateFileSystem.FullPathUnder(
                    _staged, ReleaseFilesName);
                string stagedReleaseFilesHash = UpdateFileSystem.Sha256(stagedReleaseFiles);
                var newFiles = manifest.Files.ToDictionary(f => f.Path,
                    StringComparer.OrdinalIgnoreCase);
                var oldFiles = oldManifest?.Files.ToDictionary(f => f.Path,
                    StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, ReleaseFile>(StringComparer.OrdinalIgnoreCase);
                var obsoleteFiles = new Dictionary<string, ReleaseFile>(
                    StringComparer.OrdinalIgnoreCase);
                if (oldManifest != null)
                {
                    foreach ((string path, ReleaseFile file) in oldFiles)
                    {
                        if (newFiles.ContainsKey(path)
                            || UpdatePathPolicy.IsNeverManaged(path))
                            continue;
                        string destination = UpdateFileSystem.FullPathUnder(_installation, path);
                        if (!File.Exists(destination))
                            continue;
                        if (UpdateFileSystem.IsLinkOrReparse(destination))
                            throw new InvalidDataException(
                                $"obsolete path is a link or reparse point: {path}");
                        string? parent = Path.GetDirectoryName(destination);
                        if (!String.IsNullOrEmpty(parent))
                            UpdateFileSystem.EnsureDirectoryChainSafe(_installation, parent);
                        if (String.Equals(UpdateFileSystem.Sha256(destination), file.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                            obsoleteFiles.Add(path, file);
                    }
                }

                var affected = new List<UpdateTransactionFile>();
                foreach (string path in newFiles.Keys.Concat(
                    obsoleteFiles.Keys.Except(newFiles.Keys, StringComparer.OrdinalIgnoreCase))
                    .Append(ReleaseFilesName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p,
                        StringComparer.OrdinalIgnoreCase))
                {
                    string destination = UpdateFileSystem.FullPathUnder(_installation, path);
                    bool present = File.Exists(destination);
                    string? destinationParent = Path.GetDirectoryName(destination);
                    if (!String.IsNullOrEmpty(destinationParent))
                        UpdateFileSystem.EnsureDirectoryChainSafe(_installation,
                            destinationParent);
                    if (present && UpdateFileSystem.IsLinkOrReparse(destination))
                        throw new InvalidDataException($"installed path is a link or reparse point: {path}");
                    string? backupPath = present
                        ? Path.Combine(updateDirectory, "backup",
                            path.Replace('/', Path.DirectorySeparatorChar)) : null;
                    string? backupHash = null;
                    if (present)
                    {
                        if (backupPath == null) throw new InvalidDataException("backup path missing");
                        UpdateFileSystem.EnsureDirectoryChainSafe(updateDirectory,
                            Path.GetDirectoryName(backupPath)!);
                        UpdateFileSystem.CopyDurably(destination, backupPath);
                        backupHash = UpdateFileSystem.Sha256(backupPath);
                    }
                    string? expected = path.Equals(ReleaseFilesName,
                        StringComparison.OrdinalIgnoreCase)
                        ? stagedReleaseFilesHash
                        : newFiles.TryGetValue(path, out ReleaseFile? newFile)
                            ? newFile.Sha256 : null;
                    affected.Add(new UpdateTransactionFile(path, present,
                        backupPath == null ? null : Path.GetRelativePath(updateDirectory, backupPath),
                        backupHash, expected));
                }

                var journal = new UpdateTransactionJournal(JournalSchemaVersion,
                    fromVersion, toVersion, UpdateTransactionState.Applying, _staged, affected);
                WriteJournal(journalPath, journal);

                foreach (ReleaseFile file in manifest.Files.OrderBy(f => f.Path,
                    StringComparer.OrdinalIgnoreCase))
                {
                    string source = UpdateFileSystem.FullPathUnder(_staged, file.Path);
                    string destination = UpdateFileSystem.FullPathUnder(_installation, file.Path);
                    InstallOne(source, destination, file.Path);
                }
                InstallOne(stagedReleaseFiles,
                    UpdateFileSystem.FullPathUnder(_installation, ReleaseFilesName), ReleaseFilesName);

                foreach (string obsolete in obsoleteFiles.Keys.Except(newFiles.Keys,
                    StringComparer.OrdinalIgnoreCase))
                {
                    string path = UpdateFileSystem.FullPathUnder(_installation, obsolete);
                    if (File.Exists(path))
                    {
                        if (UpdateFileSystem.IsLinkOrReparse(path))
                            throw new InvalidDataException($"obsolete path is a link: {obsolete}");
                        File.Delete(path);
                    }
                }
                VerifyInstalled(manifest);
                foreach (string obsolete in obsoleteFiles.Keys)
                {
                    string path = UpdateFileSystem.FullPathUnder(_installation, obsolete);
                    if (File.Exists(path))
                        throw new InvalidDataException(
                            $"obsolete managed file was not removed: {obsolete}");
                }
                WriteJournal(journalPath, journal with { State = UpdateTransactionState.Committed });
                CleanupTransaction(updateDirectory, journalPath);
                return new(true, UpdateTransactionState.Committed, null);
            }
            catch (Exception ex)
            {
                string journalPath = Path.Combine(updateDirectory, "transaction.json");
                try
                {
                    if (File.Exists(journalPath))
                    {
                        UpdateTransactionJournal journal = ReadJournal(journalPath);
                        if (journal.State == UpdateTransactionState.Applying)
                        {
                            if (Rollback(journal, updateDirectory))
                            {
                                WriteJournal(journalPath,
                                    journal with { State = UpdateTransactionState.RolledBack });
                                CleanupTransaction(updateDirectory, journalPath);
                                return new(false, UpdateTransactionState.RolledBack, ex.Message);
                            }
                            WriteJournal(journalPath,
                                journal with { State = UpdateTransactionState.RecoveryRequired });
                            return new(false, UpdateTransactionState.RecoveryRequired,
                                $"update failed and rollback requires recovery: {ex.Message}");
                        }
                    }
                }
                catch (Exception recoveryError)
                {
                    return new(false, UpdateTransactionState.RecoveryRequired,
                        $"update failed and recovery failed: {recoveryError.Message}");
                }
                return new(false, UpdateTransactionState.RolledBack, ex.Message);
            }
        }
    }

    public static UpdateTransactionResult Recover(string installationDirectory)
    {
        string installation = Path.GetFullPath(installationDirectory);
        string updateDirectory = Path.Combine(installation, ".update");
        string journalPath = Path.Combine(updateDirectory, "transaction.json");
        // Ordinary startup must remain read-only when there is no transaction:
        // do not create .update or acquire a lock merely to discover that it is
        // absent. Once a journal exists, the lock/recheck below makes recovery
        // race-safe with a staged/applying process.
        if (!File.Exists(journalPath))
            return new(true, UpdateTransactionState.Committed, null);
        UpdateInstallationLock? installationLock = UpdateInstallationLock.TryAcquire(updateDirectory);
        if (installationLock == null)
            return new(false, UpdateTransactionState.RecoveryRequired, "update lock is held");
        using (installationLock)
        {
            if (!File.Exists(journalPath))
                return new(true, UpdateTransactionState.Committed, null);
            return RecoverLocked(installation);
        }
    }

    /// <summary>
    /// Recover an installation while the caller already owns its persistent
    /// update lock. Staging uses this form so recovery and replacement cannot
    /// race between two lock acquisitions.
    /// </summary>
    internal static UpdateTransactionResult RecoverLocked(string installationDirectory)
    {
        string installation = Path.GetFullPath(installationDirectory);
        string updateDirectory = Path.Combine(installation, ".update");
        {
            string journalPath = Path.Combine(updateDirectory, "transaction.json");
            if (!File.Exists(journalPath)) return new(true, UpdateTransactionState.Committed, null);
            try
            {
                UpdateTransactionJournal journal = ReadJournal(journalPath);
                if (journal.State == UpdateTransactionState.Applying)
                {
                    if (VerifyJournalFiles(installation, journal, expectNew: true))
                    {
                        WriteJournal(journalPath, journal with { State = UpdateTransactionState.Committed });
                        CleanupTransaction(updateDirectory, journalPath);
                        return new(true, UpdateTransactionState.Committed, null);
                    }
                    if (!Rollback(journal, updateDirectory))
                    {
                        WriteJournal(journalPath,
                            journal with { State = UpdateTransactionState.RecoveryRequired });
                        return new(false, UpdateTransactionState.RecoveryRequired,
                            "interrupted update could not be rolled back");
                    }
                    journal = journal with { State = UpdateTransactionState.RolledBack };
                    WriteJournal(journalPath, journal);
                }
                if (journal.State is UpdateTransactionState.Committed or UpdateTransactionState.RolledBack)
                {
                    CleanupTransaction(updateDirectory, journalPath);
                    return new(true, journal.State, null);
                }
                return new(false, UpdateTransactionState.RecoveryRequired,
                    "manual update recovery is required");
            }
            catch (Exception ex)
            {
                return new(false, UpdateTransactionState.RecoveryRequired, ex.Message);
            }
        }
    }

    /// <summary>Verify the installed release before a clean rollback restart.</summary>
    internal static bool VerifyInstalledRelease(string installationDirectory, string version)
    {
        try
        {
            if (!UpdateManifestValidator.IsExactVersion(version)) return false;
            string installation = Path.GetFullPath(installationDirectory);
            string metadata = UpdateFileSystem.FullPathUnder(installation, ReleaseFilesName);
            if (!File.Exists(metadata) || UpdateFileSystem.IsLinkOrReparse(metadata)) return false;
            ReleaseFilesManifest manifest = ReleaseFilesJson.Parse(File.ReadAllBytes(metadata));
            if (!String.Equals(manifest.Version, version, StringComparison.Ordinal)) return false;
            foreach (ReleaseFile file in manifest.Files)
            {
                string path = UpdateFileSystem.FullPathUnder(installation, file.Path);
                if (!File.Exists(path) || UpdateFileSystem.IsLinkOrReparse(path)
                    || !String.Equals(UpdateFileSystem.Sha256(path), file.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
        catch (Exception) { return false; }
    }

    private ReleaseFilesManifest LoadStagedManifest()
    {
        string path = UpdateFileSystem.FullPathUnder(_staged, ReleaseFilesName);
        if (!File.Exists(path) || UpdateFileSystem.IsLinkOrReparse(path))
            throw new InvalidDataException("staged package has no safe release-files.json");
        byte[] bytes = File.ReadAllBytes(path);
        ReleaseFilesManifest manifest = ReleaseFilesJson.Parse(bytes);
        long stagedBytes = 0;
        foreach (ReleaseFile file in manifest.Files)
        {
            string source = UpdateFileSystem.FullPathUnder(_staged, file.Path);
            if (!File.Exists(source) || UpdateFileSystem.IsLinkOrReparse(source))
                throw new InvalidDataException($"staged release file is missing or unsafe: {file.Path}");
            long length = new FileInfo(source).Length;
            if (length > ReleaseFilesManifestValidator.MaxFileBytes
                || (stagedBytes += length) > ReleaseFilesManifestValidator.MaxExtractedBytes)
                throw new InvalidDataException($"staged release file is too large: {file.Path}");
            if (!String.Equals(UpdateFileSystem.Sha256(source), file.Sha256,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"staged release file hash mismatch: {file.Path}");
        }
        return manifest;
    }

    private static ReleaseFilesManifest? TryLoadOldManifest(string path)
    {
        try
        {
            if (!File.Exists(path) || UpdateFileSystem.IsLinkOrReparse(path)) return null;
            return ReleaseFilesJson.Parse(File.ReadAllBytes(path));
        }
        catch (Exception)
        {
            // A tampered/legacy old manifest is not trusted for deletion. New
            // managed destinations are still backed up for first migration.
            return null;
        }
    }

    private bool WaitForOldProcess(int pid)
    {
        if (pid <= 0) return true;
        if (_waitForProcess != null) return _waitForProcess(pid, _processWait);
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.WaitForExit((int)Math.Min(Int32.MaxValue,
                _processWait.TotalMilliseconds));
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private void InstallOne(string source, string destination, string relative)
    {
        if (_failureInjector?.Invoke(source, destination) is Exception injected)
            throw injected;
        if (!File.Exists(source) || UpdateFileSystem.IsLinkOrReparse(source))
            throw new InvalidDataException($"staged file is missing or unsafe: {relative}");
        string? parent = Path.GetDirectoryName(destination);
        if (!String.IsNullOrEmpty(parent))
            UpdateFileSystem.EnsureDirectoryChainSafe(_installation, parent);
        string temporary = destination + ".update-tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            UpdateFileSystem.CopyDurably(source, temporary);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(temporary, destination, overwrite: true);
                    break;
                }
                catch (IOException) when (attempt < 20) { Thread.Sleep(100); }
                catch (UnauthorizedAccessException) when (attempt < 20) { Thread.Sleep(100); }
            }
            // CopyDurably carries the source mode onto the temporary file
            // before the rename. Keep the explicit mode application for file
            // systems that normalize a mode during Move, but fail closed if
            // executable permissions cannot be restored.
            UpdateFileSystem.PreserveUnixMode(source, destination);
        }
        finally
        {
            UpdateFileSystem.TryDeleteFile(temporary);
        }
    }

    private void VerifyInstalled(ReleaseFilesManifest manifest)
    {
        foreach (ReleaseFile file in manifest.Files)
        {
            string path = UpdateFileSystem.FullPathUnder(_installation, file.Path);
            if (!File.Exists(path) || UpdateFileSystem.IsLinkOrReparse(path)
                || !String.Equals(UpdateFileSystem.Sha256(path), file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"installed file verification failed: {file.Path}");
        }
        string metadata = UpdateFileSystem.FullPathUnder(_installation, ReleaseFilesName);
        if (!File.Exists(metadata) || UpdateFileSystem.IsLinkOrReparse(metadata))
            throw new InvalidDataException("installed release-files.json is missing");
        _ = ReleaseFilesJson.Parse(File.ReadAllBytes(metadata));
    }

    private static bool Rollback(UpdateTransactionJournal journal, string updateDirectory)
    {
        try
        {
            foreach (UpdateTransactionFile file in journal.Files)
            {
                string destination = UpdateFileSystem.FullPathUnder(
                    Path.GetDirectoryName(updateDirectory)!, file.Path);
                if (file.WasPresent)
                {
                    if (file.BackupPath == null) return false;
                    string backup = UpdateFileSystem.FullPathUnder(updateDirectory,
                        file.BackupPath.Replace(Path.DirectorySeparatorChar, '/'));
                    if (!File.Exists(backup) || UpdateFileSystem.IsLinkOrReparse(backup)
                        || file.BackupSha256 == null
                        || !String.Equals(UpdateFileSystem.Sha256(backup), file.BackupSha256,
                            StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (File.Exists(destination) && UpdateFileSystem.IsLinkOrReparse(destination))
                        return false;
                    string? parent = Path.GetDirectoryName(destination);
                    if (!String.IsNullOrEmpty(parent))
                        UpdateFileSystem.EnsureDirectoryChainSafe(Path.GetDirectoryName(updateDirectory)!, parent);
                    string temp = destination + ".rollback-tmp-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        UpdateFileSystem.CopyDurably(backup, temp);
                        File.Move(temp, destination, overwrite: true);
                    }
                    finally { UpdateFileSystem.TryDeleteFile(temp); }
                }
                else
                {
                    if (!File.Exists(destination))
                        continue;
                    // Only remove a path that this transaction actually
                    // installed. A file that appeared while rolling back is
                    // player-owned evidence, not ours to delete. It also means
                    // rollback is incomplete and must remain recoverable.
                    if (file.ExpectedSha256 == null
                        || !String.Equals(UpdateFileSystem.Sha256(destination),
                            file.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (UpdateFileSystem.IsLinkOrReparse(destination)
                        || !UpdateFileSystem.TryDeleteFile(destination))
                        return false;
                }
            }
            foreach (UpdateTransactionFile file in journal.Files)
            {
                if (!file.WasPresent) continue;
                string destination = UpdateFileSystem.FullPathUnder(
                    Path.GetDirectoryName(updateDirectory)!, file.Path);
                if (!File.Exists(destination) || file.BackupSha256 == null
                    || !String.Equals(UpdateFileSystem.Sha256(destination), file.BackupSha256,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool VerifyJournalFiles(string installation,
        UpdateTransactionJournal journal, bool expectNew)
    {
        try
        {
            foreach (UpdateTransactionFile file in journal.Files)
            {
                string path = UpdateFileSystem.FullPathUnder(installation, file.Path);
                if (!expectNew) continue;
                if (file.ExpectedSha256 == null)
                {
                    if (File.Exists(path)) return false;
                    continue;
                }
                if (!File.Exists(path) || UpdateFileSystem.IsLinkOrReparse(path)
                    || !String.Equals(UpdateFileSystem.Sha256(path), file.ExpectedSha256,
                        StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
        catch (Exception) { return false; }
    }

    private static void WriteJournal(string path, UpdateTransactionJournal journal)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        UpdateFileSystem.AtomicWrite(path, JsonSerializer.SerializeToUtf8Bytes(journal, options));
    }

    private static UpdateTransactionJournal ReadJournal(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > 512 * 1024) throw new InvalidDataException("update journal is too large");
        UpdateTransactionJournal? journal = JsonSerializer.Deserialize<UpdateTransactionJournal>(bytes);
        if (journal == null || journal.SchemaVersion != JournalSchemaVersion
            || !UpdateManifestValidator.IsExactVersion(journal.FromVersion)
            || !UpdateManifestValidator.IsExactVersion(journal.ToVersion)
            || journal.State is not (UpdateTransactionState.Applying
                or UpdateTransactionState.Committed
                or UpdateTransactionState.RolledBack
                or UpdateTransactionState.RecoveryRequired)
            || String.IsNullOrWhiteSpace(journal.StagedDirectory)
            || !Path.IsPathFullyQualified(journal.StagedDirectory)
            || journal.Files == null || journal.Files.Count > ReleaseFilesManifestValidator.MaxFiles)
            throw new InvalidDataException("invalid update journal");
        string updateDirectory = Path.GetDirectoryName(path)!;
        string installation = Path.GetDirectoryName(updateDirectory)!;
        string staged = Path.GetFullPath(journal.StagedDirectory);
        if (Directory.Exists(staged) && UpdateFileSystem.IsLinkOrReparse(staged))
            throw new InvalidDataException("journal staged directory is a link");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (UpdateTransactionFile file in journal.Files)
        {
            if (file == null || String.IsNullOrWhiteSpace(file.Path)
                || !seen.Add(file.Path)
                || (!ReleaseFilesManifestValidator.IsSafeRelativePath(file.Path)
                    && !file.Path.Equals(ReleaseFilesName,
                        StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("journal contains an unsafe or duplicate path");
            _ = UpdateFileSystem.FullPathUnder(installation, file.Path);
            if (file.WasPresent)
            {
                if (String.IsNullOrWhiteSpace(file.BackupPath)
                    || !ReleaseFilesManifestValidator.IsSafeRelativePath(
                        file.BackupPath.Replace(Path.DirectorySeparatorChar, '/'))
                    || !UpdateManifestValidator.IsSha256(file.BackupSha256))
                    throw new InvalidDataException("journal backup metadata is invalid");
                _ = UpdateFileSystem.FullPathUnder(updateDirectory,
                    file.BackupPath.Replace(Path.DirectorySeparatorChar, '/'));
            }
            else if (file.BackupPath != null || file.BackupSha256 != null)
            {
                throw new InvalidDataException("journal has a backup for an absent file");
            }
            if (!file.WasPresent && file.ExpectedSha256 == null)
                throw new InvalidDataException("journal has no old or new state for a file");
            if (file.ExpectedSha256 != null
                && !UpdateManifestValidator.IsSha256(file.ExpectedSha256))
                throw new InvalidDataException("journal expected hash is invalid");
            if (file.Path.Equals(ReleaseFilesName, StringComparison.OrdinalIgnoreCase)
                && !UpdateManifestValidator.IsSha256(file.ExpectedSha256))
                throw new InvalidDataException("journal release-files hash is missing");
        }
        return journal;
    }

    private static void CleanupTransaction(string updateDirectory, string journalPath)
    {
        // Keep update.lock itself as the persistent lock object. Only remove
        // transaction data created by this updater.
        TryDeleteTree(Path.Combine(updateDirectory, "backup"));
        TryDeleteTree(Path.Combine(updateDirectory, "package"));
        TryDeleteTree(Path.Combine(updateDirectory, "staged"));
        _ = UpdateFileSystem.TryDeleteFile(Path.Combine(updateDirectory, "staged-update.json"));
        _ = UpdateFileSystem.TryDeleteFile(Path.Combine(updateDirectory, "launch.claim"));
        _ = UpdateFileSystem.TryDeleteFile(Path.Combine(updateDirectory, "launch.pending"));
        try { if (File.Exists(journalPath)) File.Delete(journalPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void ClearStaleBackup(string updateDirectory)
    {
        string backup = Path.Combine(updateDirectory, "backup");
        if (!Directory.Exists(backup)) return;
        if (UpdateFileSystem.IsLinkOrReparse(backup))
            throw new InvalidDataException("stale update backup is a link");
        Directory.Delete(backup, recursive: true);
        if (Directory.Exists(backup))
            throw new IOException("stale update backup could not be cleared");
    }

    private static void TryDeleteTree(string path)
    {
        try
        {
            if (Directory.Exists(path) && !UpdateFileSystem.IsLinkOrReparse(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
