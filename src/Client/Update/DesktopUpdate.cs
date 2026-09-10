using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

    private static string Staging => Path.Combine(AppContext.BaseDirectory, ".update");
    private static string StagedBuild => Path.Combine(Staging, StagedDirectoryName);
    private static string PackageDirectory => Path.Combine(Staging, PackageDirectoryName);

    public static string? LastError { get; private set; }

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

        string target = Path.GetFullPath(AppContext.BaseDirectory);
        string updateDirectory = Path.Combine(target, ".update");
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
                TryDeleteTree(PackageDirectory);
                TryDeleteTree(StagedBuild);
                Directory.CreateDirectory(PackageDirectory);
                string archive = Path.Combine(PackageDirectory,
                    update.Package.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        ? "package.zip" : "package.tar.gz");
                DownloadResult downloaded = await UpdateDownload.DownloadAsync(update.Package,
                    update.PackageUri, archive, progress, cancellationToken).ConfigureAwait(false);
                if (!downloaded.Success)
                {
                    LastError = downloaded.Error ?? "the package download failed";
                    return false;
                }
                Directory.CreateDirectory(StagedBuild);
                ExtractSafe(archive, StagedBuild,
                    update.Package.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                TryDeleteFile(archive);

                string binary = Path.Combine(StagedBuild, BinaryName());
                if (!File.Exists(binary) || UpdateFileSystem.IsLinkOrReparse(binary))
                {
                    LastError = $"the package does not contain a safe {BinaryName()}";
                    return false;
                }
                ReleaseFilesManifest manifest = ReadAndValidateStaged(update, StagedBuild);
                if (!manifest.Files.Any(file => String.Equals(file.Path, BinaryName(),
                    StringComparison.OrdinalIgnoreCase)))
                {
                    LastError = $"release-files.json does not manage {BinaryName()}";
                    return false;
                }
                MakeExecutable(binary);
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
        try
        {
            string binary = Path.Combine(StagedBuild, BinaryName());
            if (!File.Exists(binary) || UpdateFileSystem.IsLinkOrReparse(binary))
                throw new FileNotFoundException("the staged executable is missing", binary);
            var start = new ProcessStartInfo(binary)
            {
                WorkingDirectory = StagedBuild,
                UseShellExecute = false
            };
            start.ArgumentList.Add("-" + ApplyFlag);
            start.ArgumentList.Add(AppContext.BaseDirectory);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            start.ArgumentList.Add(BuildVersion.Current?.ToString(3) ?? "0.0.0");
            return Process.Start(start) != null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Console.WriteLine($"[update] could not start the update: {ex.Message}");
            return false;
        }
    }

    /// <summary>Apply the staged package from the new process.</summary>
    public static int Apply(string target, int waitFor, string? fromVersion = null)
    {
        target = Path.GetFullPath(target);
        string source = Path.GetFullPath(AppContext.BaseDirectory);
        try
        {
            string from = fromVersion ?? "0.0.0";
            if (!UpdateManifestValidator.IsExactVersion(from))
                throw new InvalidDataException("the old launcher version is invalid");
            string to = ReadStagedVersion(source)
                ?? throw new InvalidDataException("staged release version is missing");
            UpdateTransactionResult result = new DesktopUpdateTransaction(target, source)
                .Apply(from, to, waitFor);
            if (!result.Success)
            {
                Console.WriteLine($"[update] apply failed ({result.State}): {result.Error}");
                return 1;
            }
            string binary = Path.Combine(target, BinaryName());
            MakeExecutable(binary);
            Process.Start(new ProcessStartInfo(binary)
            {
                WorkingDirectory = target,
                UseShellExecute = false
            });
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
                if (!ReleaseFilesManifestValidator.IsSafeRelativePath(relative))
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
            if (!ReleaseFilesManifestValidator.IsSafeRelativePath(relative))
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
