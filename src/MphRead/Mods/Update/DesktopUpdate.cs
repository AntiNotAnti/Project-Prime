using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
                if (relaunchArgs != null)
                {
                    // Last, and behind a separator, because everything before
                    // it is read by position: the copying half takes the two
                    // values it needs and hands the rest to the build it
                    // starts.
                    start.ArgumentList.Add(RelaunchSeparator);
                    for (int i = 0; i < relaunchArgs.Count; i++)
                    {
                        start.ArgumentList.Add(relaunchArgs[i]);
                    }
                }
                return Process.Start(start) != null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Console.WriteLine($"[update] could not start the update: {ex}");
                return false;
            }
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
            IReadOnlyList<string>? relaunchArgs = null)
        {
            Console.WriteLine($"[update] applying to {target}");
            string source = AppContext.BaseDirectory;
            try
            {
                ReleaseInstallation.WaitForExit(waitFor);
                ReleaseInstallation.Apply(source, target);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[update] installation was not committed: {ex.Message}");
                Console.WriteLine($"[update] the staged build remains in {source}; retry after resolving the error.");
                return 1;
            }
            try
            {
                string binary = Path.Combine(target, UpdateCheck.BinaryName());
                MakeExecutable(binary);
                var restart = new ProcessStartInfo(binary)
                {
                    WorkingDirectory = target,
                    UseShellExecute = false
                };
                for (int i = 0; relaunchArgs != null && i < relaunchArgs.Count; i++)
                {
                    restart.ArgumentList.Add(relaunchArgs[i]);
                }
                Process.Start(restart);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[update] updated, but could not restart: {ex.Message}");
                return 1;
            }
            Console.WriteLine("[update] done");
            return 0;
        }

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
