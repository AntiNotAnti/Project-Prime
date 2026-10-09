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
    /// Exercise the actual published v0.1.46 -> v0.1.52 package boundary,
    /// without launching the game or requiring any copyrighted game files.
    /// Paths are unpacked, SHA-256 verified release assets supplied by CI.
    /// </summary>
    internal static void RunRealReleaseSwap(string oldDirectory, string incomingDirectory,
        Action<bool, string> check)
    {
        string game = OperatingSystem.IsWindows() ? "ProjectPrime.exe" : "ProjectPrime";
        string studio = OperatingSystem.IsWindows() ? "ProjectPrimeStudio.exe" : "ProjectPrimeStudio";
        string installed = Path.GetFullPath(oldDirectory);
        string next = Path.GetFullPath(incomingDirectory);
        string oldExecutable = Path.Combine(installed, game);
        string newExecutable = Path.Combine(next, game);
        check(File.Exists(oldExecutable) && File.Exists(newExecutable),
            "both published release archives contain their platform executable");
        check(File.Exists(Path.Combine(next, studio))
            && File.Exists(Path.Combine(next, ".project-prime-desktop.json")),
            "v0.1.52 published release has paired Game + Studio payload");
        var older = ReleaseInstallation.ReadManifest(installed);
        var newer = ReleaseInstallation.ReadManifest(next);
        check(older?.Files.Contains(game) == true
            && newer?.Files.Contains(game) == true
            && newer?.Files.Contains(studio) == true,
            "both real archives carry their own release ownership manifest");

        string worker = Path.Combine(Path.GetTempPath(),
            "prime-published-helper-" + Guid.NewGuid().ToString("N"));
        string before = ReleaseInstallation.Hash(oldExecutable);
        string expected = ReleaseInstallation.Hash(newExecutable);
        try
        {
            string savedWorker = DesktopUpdate.PrepareUpdateWorker(installed, worker, game);
            check(ReleaseInstallation.Hash(savedWorker) == before,
                "the update worker is the original published v0.1.46 executable");
            File.WriteAllText(Path.Combine(installed, "test-owned-settings.json"), "keep settings");
            File.WriteAllText(Path.Combine(installed, "test-player-map.ppmap"), "keep player map");
            ReleaseInstallation.Apply(next, installed);
            check(ReleaseInstallation.Hash(Path.Combine(installed, game)) == expected,
                "v0.1.52 executable installed from verified published archive");
            check(File.Exists(Path.Combine(installed, studio)),
                "paired v0.1.52 Studio installed with the game");
            check(File.ReadAllText(Path.Combine(installed, "test-owned-settings.json")) == "keep settings"
                && File.ReadAllText(Path.Combine(installed, "test-player-map.ppmap")) == "keep player map",
                "published cross-version upgrade retains user-owned files");
        }
        finally
        {
            if (Directory.Exists(worker)) Directory.Delete(worker, recursive: true);
        }
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
