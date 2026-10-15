using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using MphRead.Mods.Update;

internal static class ArchiveChecks
{
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-stage-check-" + Guid.NewGuid().ToString("N"));
        string installation = Path.Combine(root, "installed");
        Directory.CreateDirectory(installation);
        File.WriteAllText(Path.Combine(installation, "settings.json"), "player settings");
        File.WriteAllText(Path.Combine(installation, UpdateCheck.BinaryName()), "installed executable");
        try
        {
            foreach (bool zip in new[] { true, false })
            {
                string kind = zip ? "ZIP" : "TAR.GZ";
                string Fixture(string name, IReadOnlyDictionary<string, string> entries)
                {
                    string archive = Path.Combine(root, kind + "-" + name + (zip ? ".zip" : ".tar.gz"));
                    if (zip)
                    {
                        using var writer = ZipFile.Open(archive, ZipArchiveMode.Create);
                        foreach (var pair in entries)
                        {
                            using Stream output = writer.CreateEntry(pair.Key).Open();
                            output.Write(Encoding.UTF8.GetBytes(pair.Value));
                        }
                    }
                    else
                    {
                        using var file = File.Create(archive);
                        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
                        using var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true);
                        // Release workflow packages with `tar czf ... .`; retain its root/relative forms.
                        writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "./"));
                        foreach (var pair in entries)
                        {
                            using var data = new MemoryStream(Encoding.UTF8.GetBytes(pair.Value));
                            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "./" + pair.Key)
                            {
                                DataStream = data,
                                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                            });
                        }
                    }
                    return archive;
                }

                string staged = Path.Combine(root, kind + "-staged");
                var valid = new Dictionary<string, string>
                {
                    [UpdateCheck.BinaryName()] = "incoming executable",
                    ["data/note.txt"] = "release asset",
                    [".release-metadata"] = "hidden release asset"
                };
                string archive = Fixture("valid", valid);
                DesktopUpdate.PrepareArchive(archive, zip, staged, installation);
                check(File.ReadAllText(Path.Combine(staged, UpdateCheck.BinaryName())) == "incoming executable"
                    && File.ReadAllText(Path.Combine(staged, "data/note.txt")) == "release asset"
                    && File.ReadAllText(Path.Combine(staged, ".release-metadata")) == "hidden release asset"
                    && ReleaseInstallation.ReadManifest(staged)?.Files.Length == 3 && !File.Exists(archive),
                    kind + " staging preserves nested/hidden release files and legacy manifest generation");
                if (!OperatingSystem.IsWindows())
                    check((File.GetUnixFileMode(Path.Combine(staged, UpdateCheck.BinaryName())) & UnixFileMode.UserExecute) != 0,
                        kind + " staging preserves executable installation behavior");
                Directory.Delete(staged, recursive: true);

                archive = Fixture("missing-binary", new Dictionary<string, string> { ["data/note.txt"] = "partial payload" });
                bool rejected = false;
                try { DesktopUpdate.PrepareArchive(archive, zip, staged, installation); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected && !Directory.Exists(staged) && !File.Exists(archive),
                    kind + " missing-binary rejection removes partial staged files and downloaded archive");
                if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);

                string studio = OperatingSystem.IsWindows() ? "ProjectPrimeStudio.exe" : "ProjectPrimeStudio";
                string HashText(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));
                var paired = new Dictionary<string, string>(valid)
                {
                    [studio] = "incoming studio",
                    [ReleaseInstallation.PairName] = System.Text.Json.JsonSerializer.Serialize(new ReleaseInstallation.DesktopPair(
                        2, "1.2.3", "1.2.3", 1, UpdateCheck.BinaryName(), studio,
                        HashText("incoming executable"), HashText("incoming studio")))
                };
                archive = Fixture("paired-valid", paired);
                DesktopUpdate.PrepareArchive(archive, zip, staged, installation);
                var pair = ReleaseInstallation.ValidatePair(staged, ReleaseInstallation.ReadManifest(staged)!);
                check(pair.GameVersion == "1.2.3" && File.Exists(Path.Combine(staged, studio)),
                    kind + " stages exact Game and Studio binary identities with hidden paired metadata");
                Directory.Delete(staged, recursive: true);
                archive = Fixture("invalid-paired", new Dictionary<string, string>(valid) { [studio] = "incoming studio" });
                rejected = false;
                try { DesktopUpdate.PrepareArchive(archive, zip, staged, installation); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected && !Directory.Exists(staged) && !File.Exists(archive),
                    kind + " compatibility rejection removes the staged payload");
                if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);

                archive = Fixture("traversal", new Dictionary<string, string>(valid) { ["../escape.txt"] = "invalid path" });
                rejected = false;
                try { DesktopUpdate.PrepareArchive(archive, zip, staged, installation); }
                catch (IOException) { rejected = true; }
                check(rejected && !Directory.Exists(staged) && !File.Exists(archive) && !File.Exists(Path.Combine(root, "escape.txt")),
                    kind + " late extraction failure cleans previously extracted entries without escaping staging");
                if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);
                if (File.Exists(archive)) File.Delete(archive);

                if (!zip && !OperatingSystem.IsWindows())
                {
                    archive = Path.Combine(root, "symlink.tar.gz");
                    using (var file = File.Create(archive))
                    using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
                    using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
                    {
                        using var data = new MemoryStream(Encoding.UTF8.GetBytes("incoming executable"));
                        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, UpdateCheck.BinaryName()) { DataStream = data });
                        writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "linked-binary") { LinkName = UpdateCheck.BinaryName() });
                    }
                    rejected = false;
                    try { DesktopUpdate.PrepareArchive(archive, zip, staged, installation); }
                    catch (InvalidDataException) { rejected = true; }
                    check(rejected && !Directory.Exists(staged) && !File.Exists(archive)
                        && File.ReadAllText(Path.Combine(installation, "settings.json")) == "player settings",
                        "TAR.GZ release link rejection removes the staged link and payload while preserving installed user data");
                    if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);
                }

                archive = Fixture("cancelled", valid);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                bool cancelled = false;
                try { DesktopUpdate.PrepareArchive(archive, zip, staged, installation, cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                check(cancelled && !Directory.Exists(staged) && !File.Exists(archive),
                    kind + " cancellation after download cannot leave a launchable staged update");
                if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);
            }
            check(File.ReadAllText(Path.Combine(installation, "settings.json")) == "player settings"
                && File.ReadAllText(Path.Combine(installation, UpdateCheck.BinaryName())) == "installed executable"
                && Directory.EnumerateFiles(installation).Count() == 2,
                "all archive staging success/failure cases preserve installed executable and user settings");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
