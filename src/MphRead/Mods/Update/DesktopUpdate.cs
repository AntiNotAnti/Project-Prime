using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace MphRead.Mods.Update
{
    /// <summary>
    /// Replace this installation with the release's own package, without
    /// anybody opening a browser.
    ///
    /// The awkward part is that a program cannot overwrite the file it is
    /// running from -- Windows refuses outright, and on Unix it works in a way
    /// that is worse than refusing. So the swap is done by a **second copy of
    /// the new build**: the archive is unpacked beside the installation, the
    /// unpacked binary is started with <c>-applyupdate</c>, this process
    /// exits, and that copy waits for it to be gone, copies itself and
    /// everything beside it over the installation, and starts it again.
    ///
    /// Running the *new* binary as the one doing the copying, rather than the
    /// old one, is what makes it a single mechanism: the old build never has
    /// to know how a future release wants to be laid out, and the file doing
    /// the work is never one of the files being replaced.
    ///
    /// Nothing is deleted from the installation. The copy is a copy over the
    /// top, which is exactly what the instructions on the release page have
    /// always said to do by hand -- so a player's <c>paths.txt</c>, their
    /// <c>controls.txt</c>, their saves and their extracted game files are
    /// where they were.
    /// </summary>
    public static class DesktopUpdate
    {
        /// <summary>The argument that turns a launch into the copying half.</summary>
        public const string ApplyFlag = "applyupdate";

        /// <summary>Temporary acknowledgement sent before the existing process exits.</summary>
        public const string ReadyFlag = "updateready";

        /// <summary>Where the download and the unpacked build wait.</summary>
        private static string Staging => Path.Combine(AppContext.BaseDirectory, ".update");

        private static string StagedBuild => Path.Combine(Staging, "staged");

        // Use the currently installed and tested updater to apply an incoming
        // archive. Some newer builds cannot run their own -applyupdate
        // entry point before their additional native/Studio startup completes.
        // This is especially important on Linux, not only Windows.
        private static string UpdateWorker => Path.Combine(Staging, "worker");

        /// <summary>
        /// Where a staged build sits, for an installer that is not this one.
        /// <see cref="ServerUpdate"/> stages with the code here and then
        /// applies it in a way a supervised server survives.
        /// </summary>
        public static string StagedBuildPath => StagedBuild;

        /// <summary>Why the last attempt produced nothing.</summary>
        public static string? LastError { get; private set; }

        /// <summary>
        /// Whether this installation can be replaced in place: a published
        /// build, in a directory this user may write to.
        ///
        /// A read-only directory is the ordinary case for a system-wide
        /// install, and the answer there is the release page, not a failure
        /// half way through a copy.
        /// </summary>
        public static bool Supported
        {
            get
            {
                // Copying files into a signed app invalidates its resource seal.
                // macOS updates use the release page and replace the whole app.
                if (OperatingSystem.IsMacOS() || OperatingSystem.IsAndroid() || !BuildVersion.IsRelease)
                {
                    return false;
                }
                try
                {
                    string probe = Path.Combine(AppContext.BaseDirectory, ".update-probe");
                    File.WriteAllBytes(probe, Array.Empty<byte>());
                    File.Delete(probe);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Fetch and unpack, and report whether the swap can now be started.
        ///
        /// Everything that can fail happens here, while the program is still
        /// running and can say so on screen. By the time <see cref="Launch"/>
        /// is called there is a complete, unpacked build on disk and the only
        /// work left is copying it.
        /// </summary>
        public static bool Stage(UpdateInfo update, Action<float>? progress = null,
            CancellationToken cancel = default)
        {
            LastError = null;
            if (update.AssetUrl.Length == 0)
            {
                LastError = "this release has no package for this platform";
                return false;
            }
            if (!UpdateDownload.SupportsDigest(update.AssetDigest))
            {
                LastError = "this release asset has no supported SHA-256 digest";
                return false;
            }
            try
            {
                Clean();
                Directory.CreateDirectory(Staging);
                bool zip = update.AssetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                string archive = Path.Combine(Staging, zip ? "package.zip" : "package.tar.gz");
                if (!UpdateDownload.Fetch(update.AssetUrl, archive, update.AssetSize,
                    progress, cancel, expectedDigest: update.AssetDigest))
                {
                    LastError = UpdateDownload.LastError ?? "the download failed";
                    return false;
                }
                Directory.CreateDirectory(StagedBuild);
                if (zip)
                {
                    ZipFile.ExtractToDirectory(archive, StagedBuild, overwriteFiles: true);
                }
                else
                {
                    using FileStream compressed = File.OpenRead(archive);
                    using var plain = new GZipStream(compressed, CompressionMode.Decompress);
                    // The tar reader is what carries the executable bit across;
                    // a zip has none to carry, which is why Windows ships one.
                    TarFile.ExtractToDirectory(plain, StagedBuild, overwriteFiles: true);
                }
                File.Delete(archive);
                string binary = Path.Combine(StagedBuild, UpdateCheck.BinaryName());
                if (!File.Exists(binary))
                {
                    LastError = $"the package does not contain {UpdateCheck.BinaryName()}";
                    return false;
                }
                MakeExecutable(binary);
                // V0.1.52's released Windows archive lacks required hidden
                // Game/Studio metadata, while its Linux manifest owns a
                // forbidden updater lock. Reject those packages *before*
                // ending the current game session, not after a failed copy.
                ReleaseInstallation.ValidateIncoming(StagedBuild, AppContext.BaseDirectory);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Console.WriteLine($"[update] could not stage the update: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Start the unpacked build in its copying mode and return.
        ///
        /// The caller's next act must be to exit: the copy waits for this
        /// process to be gone before it touches anything, and a launcher that
        /// stayed open would leave it waiting until its own deadline.
        /// </summary>
        /// <param name="relaunchArgs">
        /// What to start the updated build with, or null for nothing -- which
        /// is right for the launcher, where a bare invocation is the front
        /// screen and is exactly where the player was.
        ///
        /// It is not right for anything else. A dedicated server is the same
        /// binary told what to be by its command line, so restarting it bare
        /// does not restart the server: it opens a launcher, or a console
        /// menu, on a machine with nobody at it, and the port stays shut until
        /// somebody notices. Whatever was typed has to come back.
        /// </param>
        public static bool Launch(IReadOnlyList<string>? relaunchArgs = null)
        {
            LastError = null;
            // Process.Start only proves that Windows created a process. It does
            // not prove the self-contained staged executable reached its apply
            // entry point. Do not close the running game until it acknowledges.
            string ready = Path.Combine(Staging, "handoff-" + Guid.NewGuid().ToString("N") + ".ready");
            try
            {
                // Do not execute the incoming (possibly incompatible) version
                // before we have installed it. Run our own small, known-working
                // updater from a detached copy of the current single-file
                // executable; it is not one of the files to be replaced.
                // A staged archive may have changed since its preparation.
                ReleaseInstallation.ValidateIncoming(StagedBuild, AppContext.BaseDirectory);
                string binary = PrepareUpdateWorker(AppContext.BaseDirectory,
                    UpdateWorker, UpdateCheck.BinaryName());
                var start = new ProcessStartInfo(binary)
                {
                    WorkingDirectory = UpdateWorker,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                start.ArgumentList.Add("-" + ApplyFlag);
                start.ArgumentList.Add(AppContext.BaseDirectory);
                start.ArgumentList.Add(Environment.ProcessId.ToString());
                start.ArgumentList.Add("-updatesource");
                start.ArgumentList.Add(StagedBuild);
                start.ArgumentList.Add("-" + ReadyFlag);
                start.ArgumentList.Add(ready);
                if (relaunchArgs != null)
                {
                    start.ArgumentList.Add(RelaunchSeparator);
                    for (int i = 0; i < relaunchArgs.Count; i++)
                    {
                        start.ArgumentList.Add(relaunchArgs[i]);
                    }
                }
                using Process? helper = Process.Start(start);
                if (helper == null)
                {
                    LastError = "Windows could not create the update helper process.";
                    return false;
                }
                if (!WaitForHelperReady(helper, ready, TimeSpan.FromSeconds(15)))
                {
                    Diagnostic(AppContext.BaseDirectory, "handoff refused: " + LastError);
                    return false;
                }
                Diagnostic(AppContext.BaseDirectory, "staged helper acknowledged startup; closing original process");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Diagnostic(AppContext.BaseDirectory, "could not start update helper: " + ex);
                return false;
            }
            finally
            {
                try { File.Delete(ready); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// The worker must be the executable that the user currently has, not
        /// the executable from the release being installed. Desktop releases
        /// are published self-contained/single-file; no game resources are
        /// loaded when the worker enters -applyupdate.
        /// </summary>
        internal static string PrepareUpdateWorker(string installedDirectory,
            string workerDirectory, string binaryName)
        {
            string installed = Path.Combine(installedDirectory, binaryName);
            string worker = Path.Combine(workerDirectory, binaryName);
            if (!File.Exists(installed))
                throw new FileNotFoundException("The current release executable is missing.", installed);
            Directory.CreateDirectory(workerDirectory);
            File.Copy(installed, worker, overwrite: true);
            MakeExecutable(worker);
            // Prevent accidental execution of the incoming release instead.
            if (!File.Exists(worker) || new FileInfo(worker).Length != new FileInfo(installed).Length)
                throw new IOException("Could not stage the current update worker.");
            return worker;
        }

        /// <summary>
        /// An unacknowledged helper is never allowed to linger, waiting for an
        /// unrelated future game exit and then unexpectedly replacing files.
        /// Kept separate so process handoff can be tested without installing.
        /// </summary>
        internal static bool WaitForHelperReady(Process helper, string readyFile, TimeSpan timeout)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < timeout)
            {
                if (File.Exists(readyFile)) return true;
                if (helper.HasExited)
                {
                    LastError = $"The update helper exited before it was ready (exit code {helper.ExitCode}).";
                    return false;
                }
                Thread.Sleep(50);
            }
            if (File.Exists(readyFile)) return true;
            LastError = "The update helper did not confirm startup before its deadline; the game has not been closed.";
            try
            {
                if (!helper.HasExited)
                {
                    helper.Kill();
                    helper.WaitForExit(2000);
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return false;
        }

        /// <summary>
        /// The copying half, in the new build: wait for the old one to go,
        /// copy over it, start it again.
        ///
        /// Console output rather than a window on purpose. This runs for about
        /// a second between one launcher closing and the next opening, and a
        /// window in the middle of that would be a flash nobody can read; what
        /// it is for is the log somebody reads when the game did not come
        /// back.
        /// </summary>
        /// <summary>
        /// Marks the end of what <see cref="Apply"/> reads by position and the
        /// start of what it passes on. A literal rather than a dash-flag so it
        /// cannot collide with an argument the server was actually given.
        /// </summary>
        public const string RelaunchSeparator = "--relaunch";

        public static int Apply(string target, int waitFor,
            IReadOnlyList<string>? relaunchArgs = null, string? readyFile = null,
            string? stagedSource = null)
        {
            // For an update initiated by v0.1.47, the temporary worker is
            // copied from the *current* release and the new release is merely
            // payload. Older versions still launch the staged release directly
            // and pass no stagedSource; preserve that older protocol.
            string source = stagedSource ?? AppContext.BaseDirectory;
            if (!Directory.Exists(source) ||
                !File.Exists(Path.Combine(source, UpdateCheck.BinaryName())))
            {
                Diagnostic(target, "the staged release payload is missing: " + source);
                return 1;
            }
            Diagnostic(target, $"apply worker started: source={source}, target={target}, parent={waitFor}");
            // Older installed versions do not pass a ready file. Preserve that
            // protocol so they can still update to this version.
            if (readyFile != null)
            {
                try
                {
                    File.WriteAllText(readyFile, "ready");
                }
                catch (Exception ex)
                {
                    Diagnostic(target, "could not acknowledge update worker: " + ex);
                    return 1;
                }
            }

            bool oldProcessExited = false;
            try
            {
                // Closing the native renderer, audio and Studio broker can take
                // longer than the original 30-second deadline on Windows.
                ReleaseInstallation.WaitForExit(waitFor, timeoutMs: 120000);
                oldProcessExited = true;
                Diagnostic(target, "old process exited; applying the verified release");
                ApplyWithSharingRetries(source, target);
                Diagnostic(target, "release transaction committed");
            }
            catch (Exception ex)
            {
                Diagnostic(target, "installation was not committed: " + ex);
                bool restored = false;
                if (oldProcessExited)
                {
                    try
                    {
                        ReleaseInstallation.Recover(target);
                        restored = File.Exists(Path.Combine(target, UpdateCheck.BinaryName()));
                        Diagnostic(target, restored
                            ? "existing installation is available after recovery"
                            : "the existing executable is missing after recovery");
                    }
                    catch (Exception recovery)
                    {
                        Diagnostic(target, "automatic rollback verification failed: " + recovery);
                    }
                }
                if (restored)
                {
                    try
                    {
                        // Preserve the usable old installation, but avoid
                        // immediately showing the same update prompt again.
                        StartInstalled(target, new[] { "-launcher", "-noupdate" });
                        Diagnostic(target, "restarted the unchanged installation with automatic updates disabled for this session");
                    }
                    catch (Exception restart)
                    {
                        Diagnostic(target, "could not restore the launcher: " + restart);
                    }
                }
                AlertFailure(target, "Project Prime could not install this update. "
                    + (restored ? "The previous version was restarted." : "The installed version was not restarted automatically.")
                    + "\n\nReason: " + ex.Message);
                return 1;
            }
            try
            {
                StartInstalled(target, relaunchArgs);
            }
            catch (Exception ex)
            {
                Diagnostic(target, "the update installed, but relaunch failed: " + ex);
                AlertFailure(target, "The update installed successfully, but Project Prime could not restart. "
                    + "Open ProjectPrime.exe manually.\n\nReason: " + ex.Message);
                return 1;
            }
            Diagnostic(target, "updated installation relaunched successfully");
            return 0;
        }

        private static void ApplyWithSharingRetries(string source, string target)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    ReleaseInstallation.Apply(source, target);
                    return;
                }
                catch (IOException ex) when (OperatingSystem.IsWindows()
                    && (ex.HResult & 0xffff) is 32 or 33 && attempt < 6)
                {
                    // Windows may briefly retain an image/DLL sharing lock after
                    // the owning game process has exited. The transaction must
                    // be fully recovered before trying the complete copy again.
                    ReleaseInstallation.Recover(target);
                    Diagnostic(target, $"Windows sharing violation; retrying verified update ({attempt}/5): {ex.Message}");
                    Thread.Sleep(attempt * 300);
                }
            }
        }

        private static void StartInstalled(string target, IReadOnlyList<string>? args)
        {
            string binary = Path.Combine(target, UpdateCheck.BinaryName());
            MakeExecutable(binary);
            var start = new ProcessStartInfo(binary)
            {
                WorkingDirectory = target,
                UseShellExecute = false
            };
            if (args != null)
                foreach (string arg in args) start.ArgumentList.Add(arg);
            using Process? process = Process.Start(start);
            if (process == null) throw new IOException("Windows did not create the updated process.");
        }

        /// <summary>
        /// Independent of game preferences and of the ephemeral staged folder.
        /// Failure logs survive both rollback and the next startup's stage sweep.
        /// </summary>
        private static void Diagnostic(string target, string message)
        {
            Console.WriteLine("[update] " + message);
            try
            {
                string path = DiagnosticPath(target);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                    File.Delete(path);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
            }
            catch (Exception) { /* Diagnostics must not prevent recovery. */ }
        }

        public static string DiagnosticPath(string installation)
            => Path.Combine(installation, "logs", "ProjectPrime-updater.log");

        private static void AlertFailure(string target, string message)
        {
            if (!OperatingSystem.IsWindows()
                || Environment.GetEnvironmentVariable("PROJECT_PRIME_UPDATER_SILENT") == "1") return;
            try
            {
                MessageBoxW(IntPtr.Zero, message + "\n\nDetails: " + DiagnosticPath(target),
                    "Project Prime updater", 0x00000010u | 0x00040000u);
            }
            catch (Exception) { /* The persistent log is the fallback. */ }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
        private static extern int MessageBoxW(IntPtr owner, string message, string title, uint flags);

        /// <summary>
        /// Remove what a previous update left behind.
        ///
        /// Called at startup, because the copying process cannot delete the
        /// directory it is running from: by the time anybody could, the
        /// program doing it is the one that was just installed.
        /// </summary>
        public static void Clean()
        {
            if (OperatingSystem.IsMacOS()) { return; }
            // A crash can leave an interrupted copy journal. Restore the
            // previous release before clearing its staging payload, otherwise
            // recovery evidence can be destroyed or an installation mixed.
            ReleaseInstallation.Recover(AppContext.BaseDirectory);
            try
            {
                if (Directory.Exists(Staging))
                {
                    Directory.Delete(Staging, recursive: true);
                }
            }
            catch (Exception)
            {
                // Litter, not a failure. The next stage overwrites it.
            }
        }

        public const string ReleaseManifestName = ".project-prime-files.json";
        private const int ReleaseManifestVersion = 1;

        private sealed record ReleaseManifest(
            int Version,
            string[] Files,
            Dictionary<string, string>? Hashes = null);

        /// <summary>
        /// Make staged/fresh packages self-describing. New release archives
        /// already contain this file, but generating it here keeps upgrades
        /// from older packages safe too.
        /// </summary>
        private static void EnsureReleaseManifest(string root)
        {
            string path = Path.Combine(root, ReleaseManifestName);
            if (File.Exists(path)) return;
            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
                .Where(file => !String.Equals(file, ReleaseManifestName,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var hashes = files.ToDictionary(
                relative => relative,
                relative => HashFile(Path.Combine(root,
                    relative.Replace('/', Path.DirectorySeparatorChar))),
                StringComparer.OrdinalIgnoreCase);
            File.WriteAllText(path, JsonSerializer.Serialize(
                new ReleaseManifest(ReleaseManifestVersion, files, hashes)));
        }

        private static ReleaseManifest? ReadReleaseManifest(string root)
        {
            try
            {
                string path = Path.Combine(root, ReleaseManifestName);
                if (!File.Exists(path)) return null;
                ReleaseManifest? manifest = JsonSerializer.Deserialize<ReleaseManifest>(
                    File.ReadAllText(path));
                return manifest?.Version == ReleaseManifestVersion ? manifest : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Delete only files the previous release explicitly owned. Player
        /// data is never inferred from absence in the new archive.
        /// </summary>
        internal static void RemoveObsoleteReleaseFiles(string source, string target)
        {
            ReleaseManifest? next = ReadReleaseManifest(source);
            if (next == null) return;
            var keep = next.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
            ReleaseManifest? previous = ReadReleaseManifest(target);
            if (previous != null)
            {
                foreach (string relative in previous.Files)
                {
                    if (keep.Contains(relative)) continue;
                    DeleteOwnedFile(target, relative);
                }
                RemoveEmptyReleaseDirectories(target, previous.Files, keep);
                return;
            }

            // Pre-manifest installs: remove only old executable/runtime names
            // Project Prime itself has used. Never sweep arbitrary files.
            foreach (string path in Directory.EnumerateFiles(target, "*",
                SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(path);
                string relative = Path.GetRelativePath(target, path).Replace('\\', '/');
                if (keep.Contains(relative)) continue;
                bool legacy = name.Equals("MphRead", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("MphRead.exe", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("FruityPrime", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("FruityPrime.exe", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("PrimeHuntersOnline", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("PrimeHuntersOnline.exe", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("MphRead.", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("FruityPrime.", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("PrimeHuntersOnline.", StringComparison.OrdinalIgnoreCase);
                if (legacy)
                {
                    try { File.Delete(path); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
        }

        private static void DeleteOwnedFile(string root, string relative)
        {
            try
            {
                string fullRoot = Path.GetFullPath(root);
                string path = Path.GetFullPath(Path.Combine(root,
                    relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) return;
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException) { }
        }

        private static void RemoveEmptyReleaseDirectories(string target,
            IEnumerable<string> previous, HashSet<string> keep)
        {
            var directories = previous.Where(path => !keep.Contains(path))
                .Select(path => Path.GetDirectoryName(path.Replace('/',
                    Path.DirectorySeparatorChar)))
                .Where(path => !String.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(path => path!.Length);
            foreach (string? relative in directories)
            {
                try
                {
                    string directory = Path.Combine(target, relative!);
                    if (Directory.Exists(directory)
                        && !Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }

        public static string VerifyInstallation()
        {
            ReleaseManifest? manifest = ReadReleaseManifest(AppContext.BaseDirectory);
            if (manifest == null)
            {
                return "No release manifest yet. The next in-app update will create one.";
            }
            int missing = 0, changed = 0;
            foreach (string relative in manifest.Files)
            {
                string path = Path.Combine(AppContext.BaseDirectory,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    missing++;
                    continue;
                }
                if (manifest.Hashes != null
                    && manifest.Hashes.TryGetValue(relative, out string? expected))
                {
                    try
                    {
                        if (!String.Equals(HashFile(path), expected,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            changed++;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        changed++;
                    }
                }
            }
            if (missing == 0 && changed == 0)
            {
                return manifest.Hashes == null
                    ? $"Release files verified ({manifest.Files.Length} files present; legacy manifest has no hashes)."
                    : $"Release files verified ({manifest.Files.Length} SHA-256 checks passed).";
            }
            return $"{missing} release file(s) missing; {changed} file(s) failed SHA-256 verification.";
        }

        private static string HashFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static void MakeExecutable(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }
            try
            {
                // A zip carries no mode bits and a tar does; setting it either
                // way costs one syscall and removes the difference.
                File.SetUnixFileMode(path, File.GetUnixFileMode(path)
                    | UnixFileMode.UserExecute | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherExecute);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[update] could not make {path} executable: {ex.Message}");
            }
        }
    }
}
