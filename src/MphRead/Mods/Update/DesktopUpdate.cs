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

        /// <summary>Temporary acknowledgement sent before the original process exits.</summary>
        public const string ReadyFlag = "updateready";

        /// <summary>Where the download and the unpacked build wait.</summary>
        private static string Staging => Path.Combine(AppContext.BaseDirectory, ".update");

        private static string StagedBuild => Path.Combine(Staging, "staged");

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
                string binary = Path.Combine(StagedBuild, UpdateCheck.BinaryName());
                var start = new ProcessStartInfo(binary)
                {
                    WorkingDirectory = StagedBuild,
                    UseShellExecute = false
                };
                start.ArgumentList.Add("-" + ApplyFlag);
                start.ArgumentList.Add(AppContext.BaseDirectory);
                start.ArgumentList.Add(Environment.ProcessId.ToString());
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
            IReadOnlyList<string>? relaunchArgs = null, string? readyFile = null)
        {
            string source = AppContext.BaseDirectory;
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
            // Recovery must precede staging cleanup. A failed rollback keeps its
            // journal and stops startup instead of silently accepting mixed files.
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

        public const string ReleaseManifestName = ReleaseInstallation.ManifestName;

        public static string VerifyInstallation()
        {
            ReleaseInstallation.Manifest? manifest;
            try { manifest = ReleaseInstallation.ReadManifest(AppContext.BaseDirectory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { return "Invalid release manifest: " + ex.Message; }
            if (manifest == null)
            {
                return "No release manifest yet. The next in-app update will create one.";
            }
            int missing = 0, changed = 0;
            foreach (string relative in manifest.Files)
            {
                string path = ReleaseInstallation.OwnedPath(AppContext.BaseDirectory, relative);
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
                        if (!String.Equals(ReleaseInstallation.Hash(path), expected,
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
