using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Update;

/// <summary>
/// Desktop staging and the compatibility <c>-applyupdate</c> entry point.
/// Preparation happens beside the installation; mutation is delegated to the
/// journaled <see cref="DesktopUpdateTransaction"/> in the staged process.
/// </summary>
public static class DesktopUpdate
{
    public const string ApplyFlag = "applyupdate";
    private const string PackageDirectoryName = "package";
    private const string StagedDirectoryName = "staged";
    private const string StagedClaimName = "staged-update.json";
    private const string LaunchPendingName = "launch.pending";
    private const string LaunchClaimName = "launch.claim";

    private sealed record StagedUpdateClaim(int SchemaVersion, string TransactionId,
        string Version, string StagedDirectory, int OwnerProcessId,
        long OwnerStartTimeUtcTicks);

    private sealed record LaunchClaim(int SchemaVersion, string TransactionId,
        int ProcessId, long ProcessStartTimeUtcTicks);

    public static string? LastError { get; private set; }

    // Tests capture this seam so rollback-restart verification never launches
    // an uncontrolled process. Production leaves it null and uses Process.Start.
    internal static Func<ProcessStartInfo, Process?>? ProcessStarterForTests { get; set; }

    public static bool Supported
    {
        get
        {
            if (OperatingSystem.IsAndroid() || !BuildVersion.IsRelease) return false;
            try
            {
                string probe = Path.Combine(AppContext.BaseDirectory,
                    ".update-probe-" + Guid.NewGuid().ToString("N"));
                try
                {
                    using FileStream stream = new(probe, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough);
                    stream.Flush(flushToDisk: true);
                    return true;
                }
                finally
                {
                    try { if (File.Exists(probe)) File.Delete(probe); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (Exception) { return false; }
        }
    }

    public static async Task<bool> StageAsync(UpdateInfo update,
        IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return await StageCoreAsync(update, AppContext.BaseDirectory, progress,
            cancellationToken, handler: null).ConfigureAwait(false);
    }

    internal static Task<bool> StageAtAsync(UpdateInfo update, string targetDirectory,
        HttpMessageHandler handler, IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return StageCoreAsync(update, targetDirectory, progress, cancellationToken, handler);
    }

    private static async Task<bool> StageCoreAsync(UpdateInfo update, string targetDirectory,
        IProgress<UpdateProgress>? progress, CancellationToken cancellationToken,
        HttpMessageHandler? handler)
    {
        LastError = null;
        if (update.Package == null || update.PackageUri == null)
        {
            LastError = "this release has no signed package for this platform";
            return false;
        }
        if (!Supported && !update.AllowLocalTestInstall)
        {
            LastError = "local or read-only builds are not replaced automatically";
            return false;
        }

        string target = Path.GetFullPath(targetDirectory);
        string updateDirectory = Path.Combine(target, ".update");
        string stagedRoot = Path.Combine(updateDirectory, StagedDirectoryName);
        string packageRoot = Path.Combine(updateDirectory, PackageDirectoryName);
        string stagedClaimPath = Path.Combine(updateDirectory, StagedClaimName);
        UpdateInstallationLock? updateLock = UpdateInstallationLock.TryAcquire(updateDirectory);
        if (updateLock == null)
        {
            LastError = "another update is already running";
            return false;
        }
        using (updateLock)
        {
            try
            {
                // Recovery and staging share one lock acquisition. This keeps
                // a second process from observing a cleaned journal and then
                // racing the first process while it replaces package files.
                UpdateTransactionResult recovery = DesktopUpdateTransaction.RecoverLocked(target);
                if (!recovery.Success && recovery.State == UpdateTransactionState.RecoveryRequired)
                {
                    LastError = recovery.Error ?? "installation recovery is required";
                    return false;
                }
                if (!RecoverStaleClaims(updateDirectory, stagedRoot, packageRoot))
                {
                    LastError = "an update is already staged or launching";
                    return false;
                }
                TryDeleteTree(packageRoot);
                TryDeleteTree(stagedRoot);
                string transactionId = Guid.NewGuid().ToString("N");
                string packageDirectory = Path.Combine(packageRoot, transactionId);
                string stagedDirectory = Path.Combine(stagedRoot, transactionId);
                Directory.CreateDirectory(packageDirectory);
                string archive = Path.Combine(packageDirectory,
                    update.Package.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        ? "package.zip" : "package.tar.gz");
                DownloadResult downloaded = await UpdateDownload.DownloadAsync(update.Package,
                    update.PackageUri, archive, progress, cancellationToken, handler)
                    .ConfigureAwait(false);
                if (!downloaded.Success)
                {
                    LastError = downloaded.Error ?? "the package download failed";
                    return false;
                }
                Directory.CreateDirectory(stagedDirectory);
                ExtractSafe(archive, stagedDirectory,
                    update.Package.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                TryDeleteFile(archive);

                string binary = Path.Combine(stagedDirectory, BinaryName());
                if (!File.Exists(binary) || UpdateFileSystem.IsLinkOrReparse(binary))
                {
                    LastError = $"the package does not contain a safe {BinaryName()}";
                    return false;
                }
                ReleaseFilesManifest manifest = ReadAndValidateStaged(update, stagedDirectory);
                if (!manifest.Files.Any(file => String.Equals(file.Path, BinaryName(),
                    StringComparison.OrdinalIgnoreCase)))
                {
                    LastError = $"release-files.json does not manage {BinaryName()}";
                    return false;
                }
                MakeExecutable(binary);
                Process owner = Process.GetCurrentProcess();
                var claim = new StagedUpdateClaim(1, transactionId, update.VersionString,
                    stagedDirectory, owner.Id, ReadStartTimeUtcTicks(owner));
                UpdateFileSystem.AtomicWrite(stagedClaimPath,
                    JsonSerializer.SerializeToUtf8Bytes(claim));
                return true;
            }
            catch (OperationCanceledException)
            {
                LastError = "cancelled";
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Console.WriteLine($"[update] could not stage the update: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>Legacy synchronous staging facade.</summary>
    public static bool Stage(UpdateInfo update, Action<float>? progress = null,
        CancellationToken cancel = default)
    {
        return StageAsync(update,
            new Progress<UpdateProgress>(p => progress?.Invoke((float)p.Fraction)), cancel)
            .GetAwaiter().GetResult();
    }

    public static bool Launch()
    {
        return LaunchAt(AppContext.BaseDirectory);
    }

    internal static bool LaunchAt(string targetDirectory)
    {
        LastError = null;
        string target = Path.GetFullPath(targetDirectory);
        string staging = Path.Combine(target, ".update");
        UpdateInstallationLock? updateLock = UpdateInstallationLock.TryAcquire(staging);
        if (updateLock == null)
        {
            LastError = "another update is already running";
            return false;
        }
        using (updateLock)
        {
            return LaunchLocked(target, staging);
        }
    }

    private static bool LaunchLocked(string target, string staging)
    {
        try
        {
            string stagedRoot = Path.Combine(staging, StagedDirectoryName);
            string stagedClaimPath = Path.Combine(staging, StagedClaimName);
            string pendingPath = Path.Combine(staging, LaunchPendingName);
            string launchPath = Path.Combine(staging, LaunchClaimName);
            StagedUpdateClaim claim = ReadStagedClaim(stagedClaimPath, stagedRoot)
                ?? throw new InvalidDataException("the staged update claim is missing");
            string binary = Path.Combine(claim.StagedDirectory, BinaryName());
            if (!File.Exists(binary) || UpdateFileSystem.IsLinkOrReparse(binary))
                throw new FileNotFoundException("the staged executable is missing", binary);
            var pending = new LaunchClaim(1, claim.TransactionId, Environment.ProcessId,
                ReadStartTimeUtcTicks(Process.GetCurrentProcess()));
            UpdateFileSystem.AtomicWrite(pendingPath,
                JsonSerializer.SerializeToUtf8Bytes(pending));
            var start = new ProcessStartInfo(binary)
            {
                WorkingDirectory = claim.StagedDirectory,
                UseShellExecute = false
            };
            start.ArgumentList.Add("-" + ApplyFlag);
            start.ArgumentList.Add(target);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            start.ArgumentList.Add(BuildVersion.Current?.ToString(3) ?? "0.0.0");
            start.ArgumentList.Add(claim.TransactionId);
            Process? child = StartProcess(start);
            if (child != null)
            {
                var launched = new LaunchClaim(1, claim.TransactionId, child.Id,
                    ReadStartTimeUtcTicks(child));
                LaunchClaim? childClaim = ReadLaunchClaim(launchPath);
                if (childClaim == null)
                    UpdateFileSystem.AtomicWrite(launchPath,
                        JsonSerializer.SerializeToUtf8Bytes(launched));
                else if (!String.Equals(childClaim.TransactionId, claim.TransactionId,
                    StringComparison.Ordinal) || childClaim.ProcessId != child.Id)
                    throw new InvalidDataException("the staged child claimed a different update");
                TryDeleteFile(pendingPath);
                return true;
            }
            TryDeleteFile(pendingPath);
            TryDeleteFile(launchPath);
            return false;
        }
        catch (Exception ex)
        {
            TryDeleteFile(Path.Combine(staging, LaunchPendingName));
            TryDeleteFile(Path.Combine(staging, LaunchClaimName));
            LastError = ex.Message;
            Console.WriteLine($"[update] could not start the update: {ex.Message}");
            return false;
        }
    }

    /// <summary>Apply the staged package from the new process.</summary>
    public static int Apply(string target, int waitFor, string? fromVersion = null,
        string? transactionId = null)
    {
        target = Path.GetFullPath(target);
        string source = Path.GetFullPath(AppContext.BaseDirectory);
        return ApplyFromSource(target, source, waitFor, fromVersion, transactionId);
    }

    internal static int ApplyFromSource(string target, string source, int waitFor,
        string? fromVersion = null, string? transactionId = null,
        Func<string, string, Exception?>? failureInjector = null)
    {
        target = Path.GetFullPath(target);
        source = Path.GetFullPath(source);
        try
        {
            if (transactionId != null && !ValidateLaunchClaim(target, source, transactionId))
                throw new InvalidDataException("the staged update claim does not belong to this process");
            string from = fromVersion ?? "0.0.0";
            if (!UpdateManifestValidator.IsExactVersion(from))
                throw new InvalidDataException("the old launcher version is invalid");
            string to = ReadStagedVersion(source)
                ?? throw new InvalidDataException("staged release version is missing");
            UpdateTransactionResult result = new DesktopUpdateTransaction(target, source,
                failureInjector: failureInjector)
                .Apply(from, to, waitFor);
            if (!result.Success)
            {
                if (result.State == UpdateTransactionState.RolledBack
                    && DesktopUpdateTransaction.VerifyInstalledRelease(target, from))
                {
                    string oldBinary = Path.Combine(target, BinaryName());
                    Process? restarted = StartProcess(new ProcessStartInfo(oldBinary)
                    {
                        WorkingDirectory = target,
                        UseShellExecute = false
                    });
                    if (restarted != null)
                    {
                        Console.WriteLine("[update] update rolled back; old launcher restarted");
                        return 0;
                    }
                }
                Console.WriteLine($"[update] apply failed ({result.State}): {result.Error}");
                return 1;
            }
            string binary = Path.Combine(target, BinaryName());
            MakeExecutable(binary);
            if (StartProcess(new ProcessStartInfo(binary)
            {
                WorkingDirectory = target,
                UseShellExecute = false
            }) == null)
                return 1;
            Console.WriteLine("[update] update committed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[update] update failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Run journal recovery before ordinary launcher startup.</summary>
    public static UpdateTransactionResult Recover()
    {
        if (OperatingSystem.IsAndroid())
            return new(true, UpdateTransactionState.Committed, null);
        return DesktopUpdateTransaction.Recover(Path.GetFullPath(AppContext.BaseDirectory));
    }

    private static Process? StartProcess(ProcessStartInfo start)
    {
        Func<ProcessStartInfo, Process?>? starter = ProcessStarterForTests;
        return starter != null ? starter(start) : Process.Start(start);
    }

    private static long ReadStartTimeUtcTicks(Process process)
    {
        try { return process.StartTime.ToUniversalTime().Ticks; }
        catch (Exception) { return 0; }
    }

    private static bool IsProcessAlive(int processId, long startTimeUtcTicks)
    {
        if (processId <= 0) return false;
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited) return false;
            return startTimeUtcTicks <= 0
                || ReadStartTimeUtcTicks(process) == startTimeUtcTicks;
        }
        catch (Exception) { return false; }
    }

    private static bool RecoverStaleClaims(string staging, string stagedRoot,
        string packageRoot)
    {
        string pendingPath = Path.Combine(staging, LaunchPendingName);
        string launchPath = Path.Combine(staging, LaunchClaimName);
        string stagedClaimPath = Path.Combine(staging, StagedClaimName);
        bool pendingExists = File.Exists(pendingPath);
        bool launchExists = File.Exists(launchPath);
        LaunchClaim? pending = ReadLaunchClaim(pendingPath);
        LaunchClaim? launch = ReadLaunchClaim(launchPath);
        if (pendingExists && pending != null
            && IsProcessAlive(pending.ProcessId, pending.ProcessStartTimeUtcTicks))
            return false;
        if (launchExists && launch != null
            && IsProcessAlive(launch.ProcessId, launch.ProcessStartTimeUtcTicks))
            return false;
        if (pendingExists) TryDeleteFile(pendingPath);
        if (launchExists) TryDeleteFile(launchPath);

        bool stagedExists = File.Exists(stagedClaimPath);
        StagedUpdateClaim? staged = ReadStagedClaim(stagedClaimPath, stagedRoot);
        if (stagedExists && staged != null
            && IsProcessAlive(staged.OwnerProcessId, staged.OwnerStartTimeUtcTicks))
            return false;
        if (stagedExists) TryDeleteFile(stagedClaimPath);
        if (stagedExists || pendingExists || launchExists)
        {
            TryDeleteTree(packageRoot);
            TryDeleteTree(stagedRoot);
        }
        return true;
    }

    private static LaunchClaim? ReadLaunchClaim(string path)
    {
        try
        {
            if (!File.Exists(path) || UpdateFileSystem.IsLinkOrReparse(path)) return null;
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length > 16 * 1024) return null;
            LaunchClaim? claim = JsonSerializer.Deserialize<LaunchClaim>(bytes);
            if (claim == null || claim.SchemaVersion != 1
                || claim.TransactionId.Length != 32
                || claim.TransactionId.Any(c => !Uri.IsHexDigit(c))) return null;
            return claim;
        }
        catch (Exception) { return null; }
    }

    private static bool ValidateLaunchClaim(string target, string source,
        string transactionId)
    {
        if (transactionId.Length != 32 || transactionId.Any(c => !Uri.IsHexDigit(c)))
            return false;
        try
        {
            string updateDirectory = Path.Combine(Path.GetFullPath(target), ".update");
            string claimPath = Path.Combine(updateDirectory, StagedClaimName);
            string launchPath = Path.Combine(updateDirectory, LaunchClaimName);
            StagedUpdateClaim? staged = ReadStagedClaim(claimPath,
                Path.Combine(updateDirectory, StagedDirectoryName));
            LaunchClaim? launch = ReadLaunchClaim(launchPath);
            if (launch == null)
            {
                LaunchClaim? pending = ReadLaunchClaim(
                    Path.Combine(updateDirectory, LaunchPendingName));
                if (pending == null
                    || !String.Equals(pending.TransactionId, transactionId,
                        StringComparison.Ordinal))
                    return false;
                // The parent writes launch.pending before Process.Start. The
                // child can reach this method before the parent has had a
                // chance to write launch.claim, so claim the handoff with an
                // atomic create. The parent observes and preserves this claim.
                var childClaim = new LaunchClaim(1, transactionId, Environment.ProcessId,
                    ReadStartTimeUtcTicks(Process.GetCurrentProcess()));
                try
                {
                    WriteCreateNewJson(launchPath, childClaim);
                    TryDeleteFile(Path.Combine(updateDirectory, LaunchPendingName));
                    launch = childClaim;
                }
                catch (IOException)
                {
                    launch = ReadLaunchClaim(launchPath);
                }
            }
            if (staged == null || launch == null
                || !String.Equals(staged.TransactionId, transactionId,
                    StringComparison.Ordinal)
                || !String.Equals(launch.TransactionId, transactionId,
                    StringComparison.Ordinal)
                || launch.ProcessId != Environment.ProcessId
                || !String.Equals(Path.GetFullPath(staged.StagedDirectory), source,
                    StringComparison.OrdinalIgnoreCase))
                return false;
            return launch.ProcessStartTimeUtcTicks <= 0
                || launch.ProcessStartTimeUtcTicks
                    == ReadStartTimeUtcTicks(Process.GetCurrentProcess());
        }
        catch (Exception) { return false; }
    }

    private static void WriteCreateNewJson<T>(string path, T value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 4096, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static StagedUpdateClaim? ReadStagedClaim(string claimPath, string stagedRootPath)
    {
        try
        {
            if (!File.Exists(claimPath) || UpdateFileSystem.IsLinkOrReparse(claimPath))
                return null;
            byte[] bytes = File.ReadAllBytes(claimPath);
            if (bytes.Length > 16 * 1024) return null;
            StagedUpdateClaim? claim = JsonSerializer.Deserialize<StagedUpdateClaim>(bytes);
            if (claim == null || claim.SchemaVersion != 1
                || claim.TransactionId.Length != 32
                || claim.TransactionId.Any(c => !Uri.IsHexDigit(c))
                || !UpdateManifestValidator.IsExactVersion(claim.Version)
                || claim.OwnerProcessId <= 0
                || !Path.IsPathFullyQualified(claim.StagedDirectory))
                return null;
            string stagedRoot = Path.GetFullPath(stagedRootPath);
            string staged = Path.GetFullPath(claim.StagedDirectory);
            string prefix = stagedRoot.EndsWith(Path.DirectorySeparatorChar)
                ? stagedRoot : stagedRoot + Path.DirectorySeparatorChar;
            if (!staged.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(staged) || UpdateFileSystem.IsLinkOrReparse(staged))
                return null;
            return claim with { StagedDirectory = staged };
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Compatibility name retained for callers that used to delete all stage
    /// data. It now performs recovery and only cleans committed/rolled-back
    /// transaction artifacts; it never removes the persistent lock file.
    /// </summary>
    public static void Clean()
    {
        _ = Recover();
    }

    internal static string BinaryName()
    {
        string name = UpdateCheck.IsServerBuild ? Branding.FileName + "Server" : Branding.FileName;
        return OperatingSystem.IsWindows() ? name + ".exe" : name;
    }

    private static ReleaseFilesManifest ReadAndValidateStaged(UpdateInfo update, string staged)
    {
        string metadata = UpdateFileSystem.FullPathUnder(staged,
            DesktopUpdateTransaction.ReleaseFilesName);
        if (!File.Exists(metadata) || UpdateFileSystem.IsLinkOrReparse(metadata))
            throw new InvalidDataException("desktop package is missing release-files.json");
        ReleaseFilesManifest manifest = ReleaseFilesJson.Parse(File.ReadAllBytes(metadata));
        if (!String.Equals(manifest.Version, update.VersionString,
            StringComparison.Ordinal))
            throw new InvalidDataException("release-files version does not match update manifest");
        var listed = manifest.Files.Select(file => file.Path).ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        long extractedBytes = 0;
        var directories = new Stack<string>();
        directories.Push(staged);
        while (directories.Count > 0)
        {
            string directory = directories.Pop();
            if (UpdateFileSystem.IsLinkOrReparse(directory))
                throw new InvalidDataException("desktop package contains a linked directory");
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (UpdateFileSystem.IsLinkOrReparse(entry))
                    throw new InvalidDataException("desktop package contains a link or reparse point");
                if (Directory.Exists(entry))
                {
                    directories.Push(entry);
                    continue;
                }
                if (!File.Exists(entry))
                    throw new InvalidDataException("desktop package contains an unknown filesystem entry");
                FileInfo info = new(entry);
                if (info.Length > ReleaseFilesManifestValidator.MaxFileBytes
                    || (extractedBytes += info.Length)
                        > ReleaseFilesManifestValidator.MaxExtractedBytes)
                    throw new InvalidDataException("desktop package extracted size is too large");
                string relative = Path.GetRelativePath(staged, entry).Replace(
                    Path.DirectorySeparatorChar, '/');
                if (!String.Equals(relative, DesktopUpdateTransaction.ReleaseFilesName,
                        StringComparison.OrdinalIgnoreCase)
                    && !listed.Contains(relative)
                    && !UpdatePathPolicy.IsNeverManaged(relative))
                    throw new InvalidDataException(
                        $"desktop package contains unlisted file '{relative}'");
            }
        }
        foreach (ReleaseFile file in manifest.Files)
        {
            string path = UpdateFileSystem.FullPathUnder(staged, file.Path);
            if (!File.Exists(path) || UpdateFileSystem.IsLinkOrReparse(path))
                throw new InvalidDataException($"desktop package is missing '{file.Path}'");
        }
        return manifest;
    }

    private static string? ReadStagedVersion(string source)
    {
        try
        {
            string path = UpdateFileSystem.FullPathUnder(source,
                DesktopUpdateTransaction.ReleaseFilesName);
            return ReleaseFilesJson.Parse(File.ReadAllBytes(path)).Version;
        }
        catch (Exception) { return null; }
    }

    private static void ExtractSafe(string archive, string destination, bool zip)
    {
        if (zip)
        {
            using ZipArchive input = ZipFile.OpenRead(archive);
            long extractedBytes = 0;
            int entryCount = 0;
            foreach (ZipArchiveEntry entry in input.Entries)
            {
                if (++entryCount > ReleaseFilesManifestValidator.MaxFiles + 1024)
                    throw new InvalidDataException("archive contains too many entries");
                string relative = NormalizeArchivePath(entry.FullName);
                // Unix mode 0120000 marks symbolic links in a ZIP external
                // attribute. Reject them before directory handling or opening
                // the entry stream.
                int mode = (entry.ExternalAttributes >> 16) & 0xF000;
                if (mode == 0xA000)
                    throw new InvalidDataException("archive contains a symbolic link");
                if (relative.Length == 0 || relative.EndsWith('/'))
                {
                    if (relative.Length > 0)
                        UpdateFileSystem.EnsureDirectoryChainSafe(destination,
                            UpdateFileSystem.FullPathUnder(destination,
                                relative.TrimEnd('/')));
                    continue;
                }
                if (!IsSafeArchiveFilePath(relative))
                    throw new InvalidDataException($"archive contains unsafe path '{relative}'");
                string output = UpdateFileSystem.FullPathUnder(destination, relative);
                string? parent = Path.GetDirectoryName(output);
                if (!String.IsNullOrEmpty(parent))
                    UpdateFileSystem.EnsureDirectoryChainSafe(destination, parent);
                using Stream inputStream = entry.Open();
                using FileStream outputStream = new(output, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 64 * 1024,
                    FileOptions.WriteThrough);
                CopyBounded(inputStream, outputStream, ref extractedBytes);
                outputStream.Flush(flushToDisk: true);
            }
            return;
        }

        using FileStream compressed = File.OpenRead(archive);
        using var plain = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new TarReader(plain);
        long tarExtractedBytes = 0;
        int tarEntryCount = 0;
        TarEntry? tarEntry;
        while ((tarEntry = reader.GetNextEntry()) != null)
        {
            if (++tarEntryCount > ReleaseFilesManifestValidator.MaxFiles + 1024)
                throw new InvalidDataException("archive contains too many entries");
            string relative = NormalizeArchivePath(tarEntry.Name);
            if (tarEntry.EntryType == TarEntryType.Directory)
            {
                relative = relative.TrimEnd('/');
                if (relative.Length == 0) continue;
                if (!ReleaseFilesManifestValidator.IsSafeRelativePath(relative))
                    throw new InvalidDataException($"archive contains unsafe path '{relative}'");
                UpdateFileSystem.EnsureDirectoryChainSafe(destination,
                    UpdateFileSystem.FullPathUnder(destination, relative));
                continue;
            }
            if (!IsSafeArchiveFilePath(relative))
                throw new InvalidDataException($"archive contains unsafe path '{relative}'");
            if (tarEntry.EntryType != TarEntryType.RegularFile)
                throw new InvalidDataException("archive contains a non-regular file");
            string output = UpdateFileSystem.FullPathUnder(destination, relative);
            string? parent = Path.GetDirectoryName(output);
            if (!String.IsNullOrEmpty(parent))
                UpdateFileSystem.EnsureDirectoryChainSafe(destination, parent);
            using Stream inputStream = tarEntry.DataStream
                ?? throw new InvalidDataException("tar entry has no data stream");
            using FileStream outputStream = new(output, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough);
            CopyBounded(inputStream, outputStream, ref tarExtractedBytes);
            outputStream.Flush(flushToDisk: true);
        }
    }

    private static void CopyBounded(Stream input, Stream output, ref long extractedBytes)
    {
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            int read = input.Read(buffer, 0, buffer.Length);
            if (read == 0) return;
            total += read;
            extractedBytes += read;
            if (total > ReleaseFilesManifestValidator.MaxFileBytes
                || extractedBytes > ReleaseFilesManifestValidator.MaxExtractedBytes)
                throw new InvalidDataException("archive entry is too large");
            output.Write(buffer, 0, read);
        }
    }

    private static string NormalizeArchivePath(string path)
    {
        string normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized;
    }

    private static bool IsSafeArchiveFilePath(string relative)
    {
        return String.Equals(relative, DesktopUpdateTransaction.ReleaseFilesName,
                StringComparison.OrdinalIgnoreCase)
            || ReleaseFilesManifestValidator.IsSafeRelativePath(relative);
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path, File.GetUnixFileMode(path)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute
                | UnixFileMode.OtherExecute);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[update] could not make executable: {ex.Message}");
        }
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

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
