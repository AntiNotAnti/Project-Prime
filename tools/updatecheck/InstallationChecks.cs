using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MphRead.Mods.Update;

internal static class InstallationChecks
{
    internal static int? RunChild(string[] args)
    {
        if (args.Length == 3 && args[0] == "--updater-ready-fixture")
        {
            if (args[2] == "exit") return 41;
            if (args[2] == "hang") { Thread.Sleep(10000); return 0; }
            Thread.Sleep(120);
            File.WriteAllText(args[1], "ready");
            Thread.Sleep(250);
            return 0;
        }
        if (args.Length != 4 || args[0] != "--crash-install") return null;
        int at = int.Parse(args[3]);
        ReleaseInstallation.Apply(args[1], args[2], step =>
        {
            if (step == at) Environment.Exit(73);
        });
        return 2;
    }

    /// <summary>
    /// Regression boundary for the real SHA-256-verified v0.1.46 desktop
    /// release, plus a deterministic future paired-release fixture.
    /// v0.1.52's publicly hosted archives were withdrawn; tests must not
    /// depend on a URL that no longer exists. The malformed-metadata and
    /// Linux runtime-lock cases are reconstructed from their recorded faults.
    /// </summary>
    internal static void RunPublishedLegacyFixture(string oldDirectory,
        Action<bool, string> check)
    {
        string game = OperatingSystem.IsWindows() ? "ProjectPrime.exe" : "ProjectPrime";
        string studio = OperatingSystem.IsWindows() ? "ProjectPrimeStudio.exe" : "ProjectPrimeStudio";
        string installed = Path.GetFullPath(oldDirectory);
        string installedBinary = Path.Combine(installed, game);
        check(File.Exists(installedBinary), "published v0.1.46 archive includes its game executable");
        var previous = ReleaseInstallation.ReadManifest(installed);
        check(previous?.Files.Contains(game) == true,
            "published v0.1.46 manifest and owned executable are valid");

        string scratch = Path.Combine(Path.GetTempPath(),
            "prime-published-legacy-" + Guid.NewGuid().ToString("N"));
        string workerDirectory = Path.Combine(scratch, "worker");
        string staged = Path.Combine(scratch, "next");
        Directory.CreateDirectory(staged);
        string originalHash = ReleaseInstallation.Hash(installedBinary);
        try
        {
            string worker = DesktopUpdate.PrepareUpdateWorker(
                installed, workerDirectory, game);
            check(ReleaseInstallation.Hash(worker) == originalHash,
                "updater helper copies the actual published v0.1.46 executable");

            File.WriteAllText(Path.Combine(installed, "test-owned-settings.json"), "keep settings");
            File.WriteAllText(Path.Combine(installed, "test-player-map.ppmap"), "keep map");

            // Faithfully reproduce v0.1.52's missing hidden paired metadata:
            // Studio was present, but its .project-prime-desktop.json was
            // removed by upload-artifact's default hidden-file policy.
            File.WriteAllText(Path.Combine(staged, game), "new game fixture");
            File.WriteAllText(Path.Combine(staged, studio), "new Studio fixture");
            ReleaseInstallation.EnsureManifest(staged);
            bool missingPairRejected = false;
            try { ReleaseInstallation.ValidateIncoming(staged, installed); }
            catch (InvalidDataException ex)
            {
                missingPairRejected = ex.Message.Contains("metadata",
                    StringComparison.OrdinalIgnoreCase);
            }
            check(missingPairRejected, "v0.1.52-style missing paired metadata is refused before exit");

            // Restore a complete pair, then reproduce the Linux-only lock
            // leak that caused the published release's Invalid release path.
            File.WriteAllText(Path.Combine(staged, ".project-prime-desktop.json"),
                System.Text.Json.JsonSerializer.Serialize(new {
                    Version = 1, GameVersion = "0.1.53",
                    StudioVersion = "0.1.53", IpcVersion = 1
                }));
            File.Delete(Path.Combine(staged, ReleaseInstallation.ManifestName));
            ReleaseInstallation.EnsureManifest(staged);
            File.WriteAllText(Path.Combine(staged, ".project-prime-update.lock"), "");
            bool lockRejected = false;
            try { ReleaseInstallation.ValidateIncoming(staged, installed); }
            catch (InvalidDataException ex)
            {
                lockRejected = ex.Message.Contains("lock",
                    StringComparison.OrdinalIgnoreCase);
            }
            check(lockRejected, "v0.1.52-style leaked updater lock is refused before exit");
            check(ReleaseInstallation.Hash(installedBinary) == originalHash
                && File.ReadAllText(Path.Combine(installed, "test-owned-settings.json")) == "keep settings"
                && File.ReadAllText(Path.Combine(installed, "test-player-map.ppmap")) == "keep map",
                "both malformed-package rejections preserve published v0.1.46 and user files");

            // A rebuilt, complete paired payload must be accepted and
            // committed without executing the incoming binary first.
            File.Delete(Path.Combine(staged, ".project-prime-update.lock"));
            ReleaseInstallation.ValidateIncoming(staged, installed);
            ReleaseInstallation.Apply(staged, installed);
            check(File.ReadAllText(Path.Combine(installed, game)) == "new game fixture"
                && File.ReadAllText(Path.Combine(installed, studio)) == "new Studio fixture",
                "repaired paired release upgrades legacy installation atomically");
            check(File.ReadAllText(Path.Combine(installed, "test-owned-settings.json")) == "keep settings"
                && File.ReadAllText(Path.Combine(installed, "test-player-map.ppmap")) == "keep map",
                "repaired cross-version upgrade preserves user-owned files");
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>
    /// Run the actual v0.1.47 published executable in its original
    /// v0.1.46-compatible three-argument apply-update protocol, against a
    /// genuine v0.1.46 installed archive and the candidate release payload.
    /// This is an asset-free Linux release gate, not a simulated transaction.
    /// </summary>
    internal static void RunCandidateReleaseSwap(string oldDirectory,
        string candidateDirectory, Action<bool, string> check)
    {
        string game = OperatingSystem.IsWindows() ? "ProjectPrime.exe" : "ProjectPrime";
        string oldRoot = Path.GetFullPath(oldDirectory);
        string incomingRoot = Path.GetFullPath(candidateDirectory);
        string oldGame = Path.Combine(oldRoot, game);
        string incomingGame = Path.Combine(incomingRoot, game);
        check(File.Exists(oldGame) && File.Exists(incomingGame),
            "genuine old release and newly built candidate both contain game apphosts");
        ReleaseInstallation.Manifest? oldManifest = ReleaseInstallation.ReadManifest(oldRoot);
        ReleaseInstallation.Manifest? newManifest = ReleaseInstallation.ReadManifest(incomingRoot);
        check(oldManifest?.Files.Contains(game) == true
            && newManifest?.Files.Contains(game) == true,
            "both real release packages have valid owned-file manifests");

        // User-owned files are outside both release manifests and must never
        // disappear when the newer transaction prunes obsolete owned files.
        string settings = Path.Combine(oldRoot, "hotfix-acceptance-settings.json");
        string map = Path.Combine(oldRoot, "hotfix-acceptance-map.ppmap");
        File.WriteAllText(settings, "player preferences");
        File.WriteAllText(map, "player map");
        ReleaseInstallation.ValidateIncoming(incomingRoot, oldRoot);
        string original = ReleaseInstallation.Hash(oldGame);
        string expected = ReleaseInstallation.Hash(incomingGame);
        check(!original.Equals(expected, StringComparison.OrdinalIgnoreCase),
            "v0.1.47 updater application is distinct from the published v0.1.46 binary");

        var start = new ProcessStartInfo(incomingGame)
        {
            WorkingDirectory = incomingRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-" + DesktopUpdate.ApplyFlag);
        start.ArgumentList.Add(oldRoot);
        // No live launcher to close in this isolated release test. The
        // deliberately nonexistent PID makes WaitForExit return immediately.
        start.ArgumentList.Add(Int32.MaxValue.ToString());
        start.ArgumentList.Add(DesktopUpdate.RelaunchSeparator);
        start.ArgumentList.Add("-frametimingcheck");
        using Process helper = Process.Start(start)
            ?? throw new IOException("Could not start v0.1.47 staged update executable.");
        if (!helper.WaitForExit(90000))
        {
            helper.Kill(entireProcessTree: true);
            helper.WaitForExit(5000);
            throw new TimeoutException("v0.1.47 released updater worker exceeded 90 seconds.");
        }
        string standardOut = helper.StandardOutput.ReadToEnd();
        string standardError = helper.StandardError.ReadToEnd();
        if (helper.ExitCode != 0)
            Console.WriteLine("RELEASE WORKER ERROR: " + standardOut + standardError);
        check(helper.ExitCode == 0, "published v0.1.47 worker applies original updater protocol");
        check(ReleaseInstallation.Hash(oldGame) == expected,
            "published v0.1.47 executable replaced the installed v0.1.46 executable");
        check(File.ReadAllText(settings) == "player preferences"
            && File.ReadAllText(map) == "player map",
            "real release upgrade retains player settings and custom maps");
        check(File.Exists(Path.Combine(oldRoot, "logs", "ProjectPrime-updater.log")),
            "real release updater writes persistent diagnostic log during handoff");
    }

    internal static void Run(Action<bool, string> check)
    {
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("No updater test host path.");
        bool dotnetHost = Path.GetFileNameWithoutExtension(executable)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        string root = Path.Combine(Path.GetTempPath(),
            "prime-hotfix-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        ProcessStartInfo Child(params string[] arguments)
        {
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (dotnetHost) info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach (string value in arguments) info.ArgumentList.Add(value);
            return info;
        }

        (string Source, string Target) Fixture(string name)
        {
            string source = Path.Combine(root, name, "source");
            string target = Path.Combine(root, name, "target");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "a.dll"), "old A");
            File.WriteAllText(Path.Combine(target, "obsolete.dll"), "old owned");
            ReleaseInstallation.EnsureManifest(target);
            File.WriteAllText(Path.Combine(target, "settings.json"), "player settings");
            File.WriteAllText(Path.Combine(target, "custom-map.ppmap"), "player map");
            File.WriteAllText(Path.Combine(source, "a.dll"), "new A");
            File.WriteAllText(Path.Combine(source, "b.dll"), "new B");
            ReleaseInstallation.EnsureManifest(source);
            return (source, target);
        }

        bool Original(string target) =>
            File.ReadAllText(Path.Combine(target, "a.dll")) == "old A"
            && File.ReadAllText(Path.Combine(target, "obsolete.dll")) == "old owned"
            && !File.Exists(Path.Combine(target, "b.dll"))
            && File.ReadAllText(Path.Combine(target, "settings.json")) == "player settings"
            && File.ReadAllText(Path.Combine(target, "custom-map.ppmap")) == "player map"
            && ReleaseInstallation.ReadManifest(target)!.Files.Length == 2;

        try
        {
            for (int step = 1; step <= 6; step++)
            {
                var fixture = Fixture("fault-" + step);
                bool failed = false;
                try
                {
                    ReleaseInstallation.Apply(fixture.Source, fixture.Target, count =>
                    {
                        if (count == step) throw new IOException("simulated interrupted install");
                    });
                }
                catch (IOException) { failed = true; }
                check(failed && Original(fixture.Target),
                    "file publication fault " + step + " restores original release and user files");
                ReleaseInstallation.Recover(fixture.Target);
                check(Original(fixture.Target), "recovery is idempotent after fault " + step);
            }

            for (int step = 1; step <= 6; step++)
            {
                var fixture = Fixture("crash-" + step);
                using Process child = Process.Start(Child(
                    "--crash-install", fixture.Source, fixture.Target, step.ToString()))!;
                if (!child.WaitForExit(15000))
                {
                    child.Kill(entireProcessTree: true);
                    throw new TimeoutException("Crash recovery test timed out.");
                }
                ReleaseInstallation.Recover(fixture.Target);
                check(child.ExitCode == 73 && Original(fixture.Target),
                    "abrupt worker exit at mutation " + step + " recovers installed files");
            }

            var success = Fixture("success");
            ReleaseInstallation.Apply(success.Source, success.Target);
            check(File.ReadAllText(Path.Combine(success.Target, "a.dll")) == "new A"
                && File.ReadAllText(Path.Combine(success.Target, "b.dll")) == "new B"
                && !File.Exists(Path.Combine(success.Target, "obsolete.dll"))
                && File.ReadAllText(Path.Combine(success.Target, "settings.json")) == "player settings"
                && File.ReadAllText(Path.Combine(success.Target, "custom-map.ppmap")) == "player map",
                "successful update replaces only release-owned files");

            var digest = Fixture("digest");
            File.WriteAllText(Path.Combine(digest.Source, "a.dll"), "corrupted after manifest");
            bool rejected = false;
            try { ReleaseInstallation.Apply(digest.Source, digest.Target); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected && Original(digest.Target),
                "modified staged payload is rejected before publication");

            // Confirm the worker bytes are taken from the installed release
            // even when the incoming staged release has a broken executable.
            string workerRoot = Path.Combine(root, "helper-fixture");
            string currentRoot = Path.Combine(workerRoot, "current");
            string nextRoot = Path.Combine(workerRoot, "next");
            string copyRoot = Path.Combine(workerRoot, "worker");
            string currentName = OperatingSystem.IsWindows() ? "ProjectPrime.exe" : "ProjectPrime";
            Directory.CreateDirectory(currentRoot);
            Directory.CreateDirectory(nextRoot);
            File.WriteAllText(Path.Combine(currentRoot, currentName), "current release worker");
            File.WriteAllText(Path.Combine(nextRoot, currentName), "broken incoming worker");
            string copied = DesktopUpdate.PrepareUpdateWorker(currentRoot, copyRoot, currentName);
            check(File.ReadAllText(copied) == "current release worker"
                && File.ReadAllText(copied) != File.ReadAllText(Path.Combine(nextRoot, currentName)),
                "update uses known-good installed helper instead of broken incoming release");

            string studioName = OperatingSystem.IsWindows()
                ? "ProjectPrimeStudio.exe" : "ProjectPrimeStudio";
            var prohibited = Fixture("unpaired-downgrade");
            File.WriteAllText(Path.Combine(prohibited.Target, studioName), "installed Studio");
            rejected = false;
            try { ReleaseInstallation.Apply(prohibited.Source, prohibited.Target); }
            catch (InvalidDataException ex)
            {
                rejected = ex.Message.Contains("separate folder", StringComparison.OrdinalIgnoreCase);
            }
            check(rejected && Original(prohibited.Target)
                && File.ReadAllText(Path.Combine(prohibited.Target, studioName)) == "installed Studio",
                "older build cannot silently remove newer installed Studio");

            var paired = Fixture("forward-paired");
            File.WriteAllText(Path.Combine(paired.Source, currentName), "new game");
            File.WriteAllText(Path.Combine(paired.Source, studioName), "new Studio");
            File.WriteAllText(Path.Combine(paired.Source, ".project-prime-desktop.json"),
                System.Text.Json.JsonSerializer.Serialize(new {
                    Version = 1,
                    GameVersion = "0.1.52",
                    StudioVersion = "0.1.52",
                    IpcVersion = 1
                }));
            File.Delete(Path.Combine(paired.Source, ReleaseInstallation.ManifestName));
            ReleaseInstallation.EnsureManifest(paired.Source);
            ReleaseInstallation.Apply(paired.Source, paired.Target);
            check(File.ReadAllText(Path.Combine(paired.Target, currentName)) == "new game"
                && File.ReadAllText(Path.Combine(paired.Target, studioName)) == "new Studio"
                && File.ReadAllText(Path.Combine(paired.Target, "settings.json")) == "player settings",
                "legacy game can transactionally upgrade to a paired Game and Studio release");

            var locked = Fixture("running");
            rejected = false;
            try
            {
                ReleaseInstallation.WaitForExit(Environment.ProcessId, 1);
                ReleaseInstallation.Apply(locked.Source, locked.Target);
            }
            catch (TimeoutException) { rejected = true; }
            check(rejected && Original(locked.Target),
                "live parent timeout leaves the installation untouched");

            foreach (string relative in new[]
            {
                "../other.dll", "a/../../other.dll", "/tmp/other.dll",
                "C:/other.dll", "a\\..\\other.dll", "./file.dll", "a//b"
            })
            {
                rejected = false;
                try { ReleaseInstallation.OwnedPath(success.Target, relative); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected, "release file traversal rejected: " + relative);
            }

            void Handoff(string mode, bool shouldAccept, TimeSpan timeout)
            {
                string ready = Path.Combine(root, "handoff-" + mode + ".ready");
                using Process process = Process.Start(Child("--updater-ready-fixture", ready, mode))!;
                bool received = DesktopUpdate.WaitForHelperReady(process, ready, timeout);
                check(received == shouldAccept,
                    "update worker " + mode + (shouldAccept ? " acknowledges readiness" : " cannot close game"));
                if (!shouldAccept)
                    check(!String.IsNullOrEmpty(DesktopUpdate.LastError),
                        "failed handoff leaves a diagnostic");
                if (mode == "hang")
                {
                    process.WaitForExit(5000);
                    check(process.HasExited, "unresponsive updater worker was reaped");
                }
                if (!process.HasExited && !process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
                if (File.Exists(ready)) File.Delete(ready);
            }
            Handoff("signal", true, TimeSpan.FromSeconds(5));
            Handoff("exit", false, TimeSpan.FromSeconds(5));
            Handoff("hang", false, TimeSpan.FromMilliseconds(200));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
