using System;
using System.IO;
using System.Threading;
using MphRead.Mods.Update;

internal static class DesktopUseChecks
{
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-desktop-use-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source"), target = Path.Combine(root, "target");
        Directory.CreateDirectory(source); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "game.dll"), "new");
        File.WriteAllText(Path.Combine(target, "game.dll"), "old");
        ReleaseInstallation.EnsureManifest(source); ReleaseInstallation.EnsureManifest(target);
        File.WriteAllText(Path.Combine(target, "settings.json"), "keep");
        string manifest = File.ReadAllText(Path.Combine(target, ReleaseInstallation.ManifestName));
        bool Original() => File.ReadAllText(Path.Combine(target, "game.dll")) == "old"
            && File.ReadAllText(Path.Combine(target, "settings.json")) == "keep"
            && File.ReadAllText(Path.Combine(target, ReleaseInstallation.ManifestName)) == manifest;
        bool Refused(Action action)
        {
            try { action(); return false; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { return true; }
        }
        try
        {
            using (var game = DesktopInstallationUse.Acquire(target))
            {
                using (var studio = DesktopInstallationUse.Acquire(target))
                    check(Refused(() => ReleaseInstallation.Apply(source, target)) && Original(),
                        "two live desktop readers refuse installation without changing owned or user files");
                check(Refused(() => ReleaseInstallation.Apply(source, target)) && Original(),
                    "remaining desktop reader still prevents installation after its peer exits");
            }
            ReleaseInstallation.Apply(source, target);
            check(File.ReadAllText(Path.Combine(target, "game.dll")) == "new"
                && File.ReadAllText(Path.Combine(target, "settings.json")) == "keep",
                "last desktop reader exit permits the actual release transaction");

            using (var own = new Mutex(false, DesktopInstallationUse.MutexName(target, studio: false)))
            {
                own.WaitOne();
                try
                {
                    check(!DesktopInstallationUse.OtherApplicationRunning(target, studio: false),
                        "game updater ignores its own live game role");
                    check(DesktopInstallationUse.OtherApplicationRunning(target, studio: true),
                        "Studio updater detects the live game role on another probe thread");
                }
                finally { own.ReleaseMutex(); }
            }
            using (var studio = new Mutex(false, DesktopInstallationUse.MutexName(target, studio: true)))
            {
                studio.WaitOne();
                try
                {
                    check(DesktopInstallationUse.OtherApplicationRunning(target, studio: false)
                        && !DesktopInstallationUse.OtherApplicationRunning(target, studio: true),
                        "role detection is symmetric and never recursively acquires its own UI mutex");
                }
                finally { studio.ReleaseMutex(); }
            }
            if (OperatingSystem.IsWindows())
            {
                using var studio = new Mutex(false,
                    DesktopInstallationUse.MutexName(AppContext.BaseDirectory, studio: true));
                studio.WaitOne();
                try
                {
                    check(!DesktopUpdate.Launch()
                        && DesktopUpdate.LastError?.StartsWith("Close Project Prime Studio", StringComparison.Ordinal) == true,
                        "Windows production Launch refuses a live Studio before staging or starting its helper");
                }
                finally { studio.ReleaseMutex(); }
            }
            else Console.WriteLine("DEFER Windows production Launch live-Studio preflight requires Windows");
            using (var abandoned = new Mutex(false, DesktopInstallationUse.MutexName(target, studio: true)))
            {
                var owner = new Thread(() => abandoned.WaitOne());
                owner.Start(); owner.Join();
                check(!DesktopInstallationUse.OtherApplicationRunning(target, studio: false),
                    "abandoned Studio mutex does not falsely block an update");
            }

            string gameName = OperatingSystem.IsWindows() ? "ProjectPrime.exe" : "ProjectPrime";
            string studioName = OperatingSystem.IsWindows() ? "ProjectPrimeStudio.exe" : "ProjectPrimeStudio";
            File.WriteAllText(Path.Combine(source, gameName), "game");
            File.WriteAllText(Path.Combine(source, studioName), "studio");
            string[] malformed = {
                "{\"Version\":2,\"GameVersion\":\"1.2.3\",\"StudioVersion\":\"1.2.3\",\"IpcVersion\":1}",
                "{\"Version\":1,\"GameVersion\":\"1.2.3\",\"StudioVersion\":\"1.2.3\",\"IpcVersion\":2}",
                "{\"Version\":1,\"GameVersion\":\"1.2.3\",\"StudioVersion\":\"1.2.4\",\"IpcVersion\":1}",
                "{\"Version\":1,\"GameVersion\":\"1.2.3\",\"StudioVersion\":\"1.2.3\"}",
                "[1,2,3]", "{", new string(' ', 16385)
            };
            string stableManifest = File.ReadAllText(Path.Combine(target, ReleaseInstallation.ManifestName));
            foreach (string metadata in malformed)
            {
                File.WriteAllText(Path.Combine(source, ".project-prime-desktop.json"), metadata);
                File.Delete(Path.Combine(source, ReleaseInstallation.ManifestName));
                ReleaseInstallation.EnsureManifest(source);
                bool validation = Refused(() => ReleaseInstallation.ValidateIncoming(source, target));
                bool apply = Refused(() => ReleaseInstallation.Apply(source, target));
                check(validation && apply && File.ReadAllText(Path.Combine(target, "game.dll")) == "new"
                    && File.ReadAllText(Path.Combine(target, "settings.json")) == "keep"
                    && File.ReadAllText(Path.Combine(target, ReleaseInstallation.ManifestName)) == stableManifest
                    && !File.Exists(Path.Combine(target, studioName)),
                    "invalid pair metadata rejected before staging and publication: case " + Array.IndexOf(malformed, metadata));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
