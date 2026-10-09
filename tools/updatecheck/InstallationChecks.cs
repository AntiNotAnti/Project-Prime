using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
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
        if (args.Length == 2 && args[0] == "--hold-installation")
        {
            using var lifetime = InstallationLifetime.AcquireApplication(args[1]);
            Console.WriteLine("READY");
            Console.Out.Flush();
            Console.ReadLine();
            return 0;
        }
        if (args.Length != 4 || args[0] != "--crash-install") return null;
        int at = int.Parse(args[3]);
        ReleaseInstallation.Apply(args[1], args[2], step => { if (step == at) Environment.Exit(73); });
        return 2;
    }
    internal static void Run(Action<bool, string> check, string[] args)
    {
        string processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Updater recovery tests require an executable process path.");
        bool dotnetHost = Path.GetFileNameWithoutExtension(processPath)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        string root = Path.Combine(Path.GetTempPath(), "prime-install-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? oldUserData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        string? oldLifetimeData = Environment.GetEnvironmentVariable("PROJECT_PRIME_INSTALLATION_LIFETIME_DATA");
        Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", Path.Combine(root, "state"));
        Environment.SetEnvironmentVariable("PROJECT_PRIME_INSTALLATION_LIFETIME_DATA", Path.Combine(root, "lifetime"));
        try
        {
            (string Source, string Target) Fixture(string name)
            {
                string source = Path.Combine(root, name, "source"), target = Path.Combine(root, name, "target");
                Directory.CreateDirectory(source); Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "a.dll"), "old A");
                File.WriteAllText(Path.Combine(target, "obsolete.dll"), "old owned");
                ReleaseInstallation.EnsureManifest(target);
                File.WriteAllText(Path.Combine(target, "settings.json"), "player data");
                File.WriteAllText(Path.Combine(source, "a.dll"), "new A");
                File.WriteAllText(Path.Combine(source, "b.dll"), "new B");
                ReleaseInstallation.EnsureManifest(source);
                return (source, target);
            }
            bool Original(string target) => File.ReadAllText(Path.Combine(target, "a.dll")) == "old A"
                && File.ReadAllText(Path.Combine(target, "obsolete.dll")) == "old owned"
                && !File.Exists(Path.Combine(target, "b.dll"))
                && File.ReadAllText(Path.Combine(target, "settings.json")) == "player data"
                && ReleaseInstallation.ReadManifest(target)!.Files.SequenceEqual(new[] { "a.dll", "obsolete.dll" });
            for (int step = 1; step <= 6; step++)
            {
                var fixture = Fixture("fault-" + step);
                bool failed = false;
                try { ReleaseInstallation.Apply(fixture.Source, fixture.Target, n => { if (n == step) throw new IOException("injected publication failure"); }); }
                catch (IOException) { failed = true; }
                check(failed && Original(fixture.Target), "publication failure " + step + " restores complete old release and user data");
                ReleaseInstallation.Recover(fixture.Target);
                check(Original(fixture.Target), "rollback " + step + " is idempotent");
            }
            for (int step = 1; step <= 6; step++)
            {
                var fixture = Fixture("crash-" + step);
                var start = new ProcessStartInfo(processPath) { UseShellExecute = false };
                if (dotnetHost)
                    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                foreach (string value in new[] { "--crash-install", fixture.Source, fixture.Target, step.ToString() }) start.ArgumentList.Add(value);
                using var child = Process.Start(start)!;
                if (!child.WaitForExit(10000)) { child.Kill(entireProcessTree: true); throw new IOException("Update recovery fixture timed out."); }
                ReleaseInstallation.Recover(fixture.Target);
                check(child.ExitCode == 73 && Original(fixture.Target), "process crash " + step + " recovers from flushed journal on next startup");
            }
            foreach (bool fileToDirectory in new[] { true, false })
            for (int step = 1; step <= 4; step++)
            {
                string parent = Path.Combine(root, "shape-" + fileToDirectory + "-" + step);
                string source = Path.Combine(parent, "source"), target = Path.Combine(parent, "target");
                Directory.CreateDirectory(source); Directory.CreateDirectory(target);
                string oldName = fileToDirectory ? "a" : "a/sub/b.dll", nextName = fileToDirectory ? "a/sub/b.dll" : "a";
                string oldPath = Path.Combine(target, oldName), newPath = Path.Combine(source, nextName);
                Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!); Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
                File.WriteAllText(oldPath, "old shape"); File.WriteAllText(newPath, "new shape");
                ReleaseInstallation.EnsureManifest(target); ReleaseInstallation.EnsureManifest(source);
                bool failed = false;
                try { ReleaseInstallation.Apply(source, target, n => { if (n == step) throw new IOException("shape failure"); }); }
                catch (IOException) { failed = true; }
                check(failed && File.ReadAllText(oldPath) == "old shape", "shape transition " + fileToDirectory + " fault " + step + " restores old path");
                ReleaseInstallation.Recover(target);
                var shapeStart = new ProcessStartInfo(processPath) { UseShellExecute = false };
                if (dotnetHost)
                    shapeStart.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                foreach (string value in new[] { "--crash-install", source, target, step.ToString() }) shapeStart.ArgumentList.Add(value);
                using (var killed = Process.Start(shapeStart)!)
                {
                    if (!killed.WaitForExit(10000)) { killed.Kill(entireProcessTree:true); throw new IOException("Shape crash fixture timed out."); }
                    ReleaseInstallation.Recover(target);
                    check(killed.ExitCode == 73 && File.ReadAllText(oldPath) == "old shape",
                        "shape transition " + fileToDirectory + " crash " + step + " recovers old path");
                }
                ReleaseInstallation.Apply(source, target);
                check(File.ReadAllText(Path.Combine(target, nextName)) == "new shape", "shape transition commits after recovery");
            }
            var obstructed = Fixture("obstructed");
            Directory.CreateDirectory(Path.Combine(obstructed.Source, "settings.json"));
            File.WriteAllText(Path.Combine(obstructed.Source, "settings.json", "child"), "release data");
            File.Delete(Path.Combine(obstructed.Source, ReleaseInstallation.ManifestName));
            ReleaseInstallation.EnsureManifest(obstructed.Source);
            bool obstructionRejected = false;
            try { ReleaseInstallation.Apply(obstructed.Source, obstructed.Target); } catch(IOException) { obstructionRejected = true; }
            check(obstructionRejected && Original(obstructed.Target), "unowned file obstructing new directory is preserved before mutation");
            var success = Fixture("success");
            using (var mapped = new FileStream(Path.Combine(success.Target, "a.dll"), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                ReleaseInstallation.Apply(success.Source, success.Target);
                using var reader = new StreamReader(mapped);
                check(reader.ReadToEnd() == "old A", "replacement preserves the running program's old file mapping");
            }
            check(File.ReadAllText(Path.Combine(success.Target, "a.dll")) == "new A"
                && File.ReadAllText(Path.Combine(success.Target, "b.dll")) == "new B"
                && !File.Exists(Path.Combine(success.Target, "obsolete.dll"))
                && File.ReadAllText(Path.Combine(success.Target, "settings.json")) == "player data",
                "successful commit replaces owned files and preserves unowned data");
            var corrupt = Fixture("digest");
            File.WriteAllText(Path.Combine(corrupt.Source, "a.dll"), "changed after manifest");
            bool rejected = false;
            try { ReleaseInstallation.Apply(corrupt.Source, corrupt.Target); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected && Original(corrupt.Target), "digest mismatch is rejected before installed files change");

            foreach (string path in new[] { "../target-other/file", "../target-other", "/tmp/file", "C:/file", "a/../../file", "a\\..\\file", "./a.dll", "a//b", "a/", ".project-prime-update-transaction/journal.json" })
            {
                rejected = false;
                try { ReleaseInstallation.OwnedPath(success.Target, path); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected, "release containment rejects " + path);
            }
            var traversal = Fixture("traversal");
            string neighbor = Path.Combine(Path.GetDirectoryName(traversal.Target)!, "target-other");
            Directory.CreateDirectory(Path.Combine(neighbor, "empty"));
            File.WriteAllText(Path.Combine(neighbor, "file"), "neighbor");
            File.WriteAllText(Path.Combine(traversal.Target, ReleaseInstallation.ManifestName),
                JsonSerializer.Serialize(new ReleaseInstallation.Manifest(1, new[] { "../target-other/file", "../target-other/empty/removed" })));
            rejected = false;
            try { ReleaseInstallation.Apply(traversal.Source, traversal.Target); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected && File.ReadAllText(Path.Combine(neighbor, "file")) == "neighbor"
                && Directory.Exists(Path.Combine(neighbor, "empty")), "old manifest cannot delete a sibling file or empty sibling directory");
            var linked = Fixture("symlink");
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(linked.Target, "escape"), neighbor);
                rejected = false;
                try { ReleaseInstallation.OwnedPath(linked.Target, "escape/file"); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected, "release containment rejects symlink ancestors");
                File.CreateSymbolicLink(Path.Combine(linked.Target, "dangling"), Path.Combine(neighbor, "missing"));
                rejected = false;
                try { ReleaseInstallation.OwnedPath(linked.Target, "dangling"); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected, "release containment rejects dangling symlinks");
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
            { Console.WriteLine("SKIP symlink creation requires developer mode on this Windows runner"); }
            // Real child processes verify the explicit start acknowledgement:
            // a launched-but-dead helper must not cause the game to exit, and
            // a hung helper must be reaped rather than applying later.
            void CheckHandoff(string outcome, bool expected, TimeSpan timeout)
            {
                string signal = Path.Combine(root, "handoff-" + outcome + ".ready");
                var start = new ProcessStartInfo(processPath) { UseShellExecute = false, CreateNoWindow = true };
                if (dotnetHost) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                start.ArgumentList.Add("--updater-ready-fixture");
                start.ArgumentList.Add(signal);
                start.ArgumentList.Add(outcome);
                using var helper = Process.Start(start)!;
                bool acknowledged = DesktopUpdate.WaitForHelperReady(helper, signal, timeout);
                check(acknowledged == expected,
                    "updater handoff " + outcome + (expected ? " confirms readiness" : " refuses to close original app"));
                if (outcome == "exit")
                    check(DesktopUpdate.LastError?.Contains("exited", StringComparison.OrdinalIgnoreCase) == true,
                        "early helper death retains a readable failure reason");
                if (outcome == "hang")
                    check(helper.HasExited && DesktopUpdate.LastError?.Contains("did not confirm", StringComparison.OrdinalIgnoreCase) == true,
                        "timed-out helper is killed instead of applying on a later process exit");
                if (!helper.HasExited && !helper.WaitForExit(5000)) helper.Kill(entireProcessTree: true);
                if (File.Exists(signal)) File.Delete(signal);
            }
            CheckHandoff("signal", expected: true, TimeSpan.FromSeconds(5));
            CheckHandoff("exit", expected: false, TimeSpan.FromSeconds(5));
            CheckHandoff("hang", expected: false, TimeSpan.FromMilliseconds(200));
            var wait = Fixture("wait");
            rejected = false;
            try { ReleaseInstallation.WaitForExit(Environment.ProcessId, 1); ReleaseInstallation.Apply(wait.Source, wait.Target); }
            catch (TimeoutException) { rejected = true; }
            check(rejected && Original(wait.Target), "a live old process prevents every installation mutation");

            var paired = Fixture("paired");
            string game = OperatingSystem.IsWindows() ? "ProjectPrime.exe" : "ProjectPrime";
            string studio = OperatingSystem.IsWindows() ? "ProjectPrimeStudio.exe" : "ProjectPrimeStudio";
            void PairSource(string directory, string gameVersion, string studioVersion)
            {
                File.WriteAllText(Path.Combine(directory, game), "game " + gameVersion);
                File.WriteAllText(Path.Combine(directory, studio), "studio " + studioVersion);
                File.WriteAllText(Path.Combine(directory, DesktopReleasePair.FileName),
                    JsonSerializer.Serialize(new DesktopReleasePair(1, gameVersion, studioVersion, 1)));
                File.Delete(Path.Combine(directory, ReleaseInstallation.ManifestName));
                ReleaseInstallation.EnsureManifest(directory);
            }
            PairSource(paired.Source, "1.2.3+abc", "1.2.4+def");
            rejected = false;
            try { ReleaseInstallation.Apply(paired.Source, paired.Target); } catch (InvalidDataException) { rejected = true; }
            check(rejected && Original(paired.Target), "mixed game/Studio release is rejected before any installed file changes");
            foreach (string unsupported in new[] { "1.2.3 unsupported", new string('1',129) })
            {
                PairSource(paired.Source,unsupported,unsupported);
                rejected=false;
                try { ReleaseInstallation.Apply(paired.Source,paired.Target); } catch(InvalidDataException) { rejected=true; }
                check(rejected && Original(paired.Target),"matching release versions must also satisfy the Studio IPC version contract");
            }
            PairSource(paired.Source, "1.2.3+abc", "1.2.3+abc");
            var hold = new ProcessStartInfo(processPath) { UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardOutput = true };
            if (dotnetHost)
                hold.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            hold.ArgumentList.Add("--hold-installation"); hold.ArgumentList.Add(paired.Target);
            using (var child = Process.Start(hold)!)
            {
                try
                {
                    string? ready = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                    check(ready == "READY", "a real child Studio application owns the paired installation lease");
                    using var secondReader = InstallationLifetime.AcquireApplication(paired.Target);
                    rejected = false;
                    try { ReleaseInstallation.Apply(paired.Source, paired.Target); } catch (IOException) { rejected = true; }
                    check(rejected && Original(paired.Target), "open Studio blocks paired update and preserves every installed byte");
                    Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",Path.Combine(root,"second-profile"));
                    try
                    {
                        rejected=false;
                        try { ReleaseInstallation.Apply(paired.Source,paired.Target); } catch(IOException) { rejected=true; }
                        check(rejected && Original(paired.Target),"a different application profile cannot bypass ownership of the same paired installation");
                    }
                    finally { Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",Path.Combine(root,"state")); }
                }
                finally
                {
                    child.Kill(); child.WaitForExit();
                }
            }
            ReleaseInstallation.Apply(paired.Source, paired.Target);
            check(File.ReadAllText(Path.Combine(paired.Target, game)) == "game 1.2.3+abc"
                && File.ReadAllText(Path.Combine(paired.Target, studio)) == "studio 1.2.3+abc"
                && File.ReadAllText(Path.Combine(paired.Target, "settings.json")) == "player data",
                "after Studio crash the kernel lease releases and the paired update commits both apps preserving user data");
            var downgrade = Fixture("unpaired-downgrade");
            rejected = false;
            try { ReleaseInstallation.Apply(downgrade.Source, paired.Target); } catch (InvalidDataException) { rejected = true; }
            check(rejected && File.ReadAllText(Path.Combine(paired.Target, studio)) == "studio 1.2.3+abc",
                "unpaired downgrade cannot silently leave a newer Studio beside an older game");
            File.Delete(Path.Combine(paired.Source, DesktopReleasePair.FileName));
            File.Delete(Path.Combine(paired.Source, ReleaseInstallation.ManifestName)); ReleaseInstallation.EnsureManifest(paired.Source);
            rejected = false;
            try { ReleaseInstallation.Apply(paired.Source, paired.Target); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "paired executables require owned compatibility metadata");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", oldUserData);
            Environment.SetEnvironmentVariable("PROJECT_PRIME_INSTALLATION_LIFETIME_DATA", oldLifetimeData);
            Directory.Delete(root, recursive: true);
        }
    }
}
