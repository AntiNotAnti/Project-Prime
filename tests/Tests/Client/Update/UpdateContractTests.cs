using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Update;
using Xunit;

namespace MphRead.Tests.Client.Update;

public sealed class UpdateContractTests
{
    [Fact]
    public void ManifestValidationRejectsUnknownSchemaDuplicateRidAndUnsafeNames()
    {
        UpdateManifest valid = Manifest();
        UpdateManifest parsed = UpdateManifestJson.Parse(UpdateManifestJson.Serialize(valid));
        Assert.Equal(UpdateManifestJson.Serialize(valid), UpdateManifestJson.Serialize(parsed));
        Assert.Throws<UpdateManifestValidationException>(() =>
            UpdateManifestValidator.Validate(valid with { SchemaVersion = 2 }));
        Assert.Throws<UpdateManifestValidationException>(() =>
            UpdateManifestValidator.Validate(valid with { Packages = [valid.Packages[0], valid.Packages[0]] }));
        Assert.Throws<UpdateManifestValidationException>(() =>
            UpdateManifestValidator.Validate(valid with
            {
                Packages = [valid.Packages[0] with { FileName = "../bad.zip" }]
            }));
        Assert.Throws<UpdateManifestValidationException>(() =>
            UpdateManifestValidator.Validate(valid with { Version = "1.2" }));
        Assert.Throws<UpdateManifestValidationException>(() =>
            UpdateManifestValidator.Validate(valid with
            {
                Packages = [valid.Packages[0] with { Size = -1 }]
            }));
        Assert.Throws<UpdateManifestValidationException>(() =>
            UpdateManifestValidator.Validate(valid with
            {
                Packages = [valid.Packages[0] with { Sha256 = "abcd" }]
            }));
        Assert.Throws<UpdateManifestValidationException>(() =>
            UpdateManifestJson.Parse(Encoding.UTF8.GetBytes("{}")));
    }

    [Fact]
    public async Task ManifestClientRejectsAReleaseWithoutTheCurrentRid()
    {
        byte[] manifest = UpdateManifestJson.Serialize(Manifest("1.1.0"));
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var feed = new FeedHandler(manifest, signer.SignData(
            manifest, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        var client = new UpdateManifestClient(feed,
            new UpdateTrust(signer.ExportSubjectPublicKeyInfo()),
            installedVersion: new Version(1, 0, 0), rid: "linux-x64");

        UpdateCheckResult result = await client.CheckAsync(
            cancellationToken: CancellationToken.None);

        UpdateCheckResult.Failed failed = Assert.IsType<UpdateCheckResult.Failed>(result);
        Assert.Contains("no package for linux-x64", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseFilesRejectPlayerOwnedPaths()
    {
        string hash = new('a', 64);
        Assert.Throws<UpdateManifestValidationException>(() =>
            ReleaseFilesManifestValidator.Validate(new ReleaseFilesManifest(1, "1.0.0",
                [new ReleaseFile("paths.txt", hash)])));
        Assert.Throws<UpdateManifestValidationException>(() =>
            ReleaseFilesManifestValidator.Validate(new ReleaseFilesManifest(1, "1.0.0",
                [new ReleaseFile("saves/slot.dat", hash)])));
        Assert.Throws<UpdateManifestValidationException>(() =>
            ReleaseFilesManifestValidator.Validate(new ReleaseFilesManifest(1, "1.0.0",
                [new ReleaseFile("screenshots/old.png", hash)])));
        Assert.Throws<UpdateManifestValidationException>(() =>
            ReleaseFilesManifestValidator.Validate(new ReleaseFilesManifest(1, "1.0.0",
                [new ReleaseFile("files/AMHE1/_archives/player.bin", hash)])));
        Assert.Throws<UpdateManifestValidationException>(() =>
            ReleaseFilesManifestValidator.Validate(new ReleaseFilesManifest(1, "1.0.0",
                [new ReleaseFile("content/community-pack/manifest.json", hash)])));
    }

    [Fact]
    public void LauncherPolicyMigratesLegacyValuesAndDefaultsFreshFilesToAutomatic()
    {
        string originalDirectory = MphRead.Mods.Launcher.LauncherPrefs.Directory;
        UpdatePolicy originalPolicy = MphRead.Mods.Launcher.LauncherPrefs.UpdatePolicy;
        string root = Directory.CreateTempSubdirectory("prime-prefs-").FullName;
        try
        {
            MphRead.Mods.Launcher.LauncherPrefs.Directory = root;
            MphRead.Mods.Launcher.LauncherPrefs.Load();
            Assert.Equal(UpdatePolicy.Automatic,
                MphRead.Mods.Launcher.LauncherPrefs.UpdatePolicy);
            File.WriteAllText(Path.Combine(root, "launcher.txt"), "auto_update=true\n");
            MphRead.Mods.Launcher.LauncherPrefs.Load();
            Assert.Equal(UpdatePolicy.NotifyOnly,
                MphRead.Mods.Launcher.LauncherPrefs.UpdatePolicy);
            string migrated = File.ReadAllText(Path.Combine(root, "launcher.txt"));
            Assert.Contains("update_policy=NotifyOnly", migrated, StringComparison.Ordinal);
            Assert.DoesNotContain("auto_update=", migrated, StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(root, "launcher.txt"), "auto_update=false\n");
            MphRead.Mods.Launcher.LauncherPrefs.Load();
            Assert.Equal(UpdatePolicy.Off,
                MphRead.Mods.Launcher.LauncherPrefs.UpdatePolicy);
        }
        finally
        {
            MphRead.Mods.Launcher.LauncherPrefs.Directory = originalDirectory;
            MphRead.Mods.Launcher.LauncherPrefs.UpdatePolicy = originalPolicy;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TrustVerifiesExactDerSignatureAndRejectsModificationAndP1363()
    {
        byte[] data = Encoding.UTF8.GetBytes("signed update metadata");
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = signer.ExportSubjectPublicKeyInfo();
        byte[] signature = signer.SignData(data, HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        var trust = new UpdateTrust(publicKey);
        Assert.True(trust.VerifyManifest(data, signature));
        data[0] ^= 1;
        Assert.False(trust.VerifyManifest(data, signature));
        data[0] ^= 1;
        byte[] p1363 = signer.SignData(data, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        Assert.False(trust.VerifyManifest(data, p1363));
        Assert.False(trust.VerifyManifest(data, Array.Empty<byte>()));
        Assert.False(trust.VerifyManifest(data, signature[..^1]));
        using ECDsa wrongSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.False(new UpdateTrust(wrongSigner.ExportSubjectPublicKeyInfo())
            .VerifyManifest(data, signature));
    }

    [Fact]
    public async Task DownloadVerifiesHashLengthAndAllowedRedirects()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("signed package bytes");
        var handler = new DownloadHandler(bytes);
        string root = Directory.CreateTempSubdirectory("prime-download-").FullName;
        try
        {
            string destination = Path.Combine(root, "package.zip");
            var package = new UpdatePackage("win-x64", "package.zip", bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            DownloadResult downloaded = await UpdateDownload.DownloadAsync(package,
                new Uri("https://github.com/AntiNotAnti/Project-Prime-Releases/redirect"),
                destination, handler: handler);
            Assert.True(downloaded.Success, downloaded.Error);
            Assert.Equal(bytes, File.ReadAllBytes(destination));
            Assert.False(File.Exists(destination + ".part"));

            DownloadResult wrongHash = await UpdateDownload.DownloadAsync(
                package with { Sha256 = new string('0', 64) },
                new Uri("https://github.com/AntiNotAnti/Project-Prime-Releases/package.zip"),
                destination, handler: handler);
            Assert.False(wrongHash.Success);
            Assert.False(File.Exists(destination + ".part"));

            DownloadResult wrongLength = await UpdateDownload.DownloadAsync(
                package with { Size = bytes.Length + 1 },
                new Uri("https://github.com/AntiNotAnti/Project-Prime-Releases/package.zip"),
                destination, handler: handler);
            Assert.False(wrongLength.Success);
            Assert.False(File.Exists(destination + ".part"));

            DownloadResult http = await UpdateDownload.DownloadAsync(package,
                new Uri("http://github.com/AntiNotAnti/Project-Prime-Releases/package.zip"),
                destination, handler: handler);
            Assert.False(http.Success);

            DownloadResult badRedirect = await UpdateDownload.DownloadAsync(package,
                new Uri("https://github.com/AntiNotAnti/Project-Prime-Releases/untrusted"),
                destination, handler: handler);
            Assert.False(badRedirect.Success);

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            DownloadResult cancellation = await UpdateDownload.DownloadAsync(package,
                new Uri("https://github.com/AntiNotAnti/Project-Prime-Releases/package.zip"),
                destination, cancellationToken: cancelled.Token, handler: handler);
            Assert.True(cancellation.Cancelled);
            Assert.False(File.Exists(destination + ".part"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TransactionUpdatesManagedFilesRemovesObsoleteAndPreservesPlayerFiles()
    {
        string root = Directory.CreateTempSubdirectory("prime-update-").FullName;
        try
        {
            string staged = Path.Combine(root, "staged");
            Directory.CreateDirectory(staged);
            File.WriteAllText(Path.Combine(root, "ProjectPrime"), "v1");
            File.WriteAllText(Path.Combine(root, "old.dll"), "old");
            File.WriteAllText(Path.Combine(root, "modified.dll"), "old-modified");
            File.WriteAllText(Path.Combine(root, "paths.txt"), "player paths");
            File.WriteAllText(Path.Combine(root, "my-map.fpmap"), "player");
            WriteReleaseFiles(root, "1.0.0", ("ProjectPrime", "v1"), ("old.dll", "old"),
                ("modified.dll", "old-modified"));
            File.WriteAllText(Path.Combine(root, "modified.dll"), "player modified");
            Directory.CreateDirectory(Path.Combine(root, ".update", "backup"));
            File.WriteAllText(Path.Combine(root, ".update", "backup", "stale.bin"), "stale");
            File.WriteAllText(Path.Combine(staged, "ProjectPrime"), "v2");
            File.WriteAllText(Path.Combine(staged, "new.dll"), "new");
            WriteReleaseFiles(staged, "1.1.0", ("ProjectPrime", "v2"), ("new.dll", "new"));

            UpdateTransactionResult result = new DesktopUpdateTransaction(root, staged)
                .Apply("1.0.0", "1.1.0");
            Assert.True(result.Success, result.Error);
            Assert.Equal("v2", File.ReadAllText(Path.Combine(root, "ProjectPrime")));
            Assert.Equal("new", File.ReadAllText(Path.Combine(root, "new.dll")));
            Assert.False(File.Exists(Path.Combine(root, "old.dll")));
            Assert.Equal("player modified", File.ReadAllText(Path.Combine(root, "modified.dll")));
            Assert.Equal("player paths", File.ReadAllText(Path.Combine(root, "paths.txt")));
            Assert.Equal("player", File.ReadAllText(Path.Combine(root, "my-map.fpmap")));
            Assert.False(File.Exists(Path.Combine(root, ".update", "backup", "stale.bin")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void SyntheticV1ToV2UpdateLaunchesNewBuildAndPreservesPlayerFile()
    {
        if (OperatingSystem.IsWindows())
        {
            // The release workflow runs this test on Linux. Windows has no
            // guaranteed shell with which to make the tiny executable marker.
            return;
        }

        string root = Directory.CreateTempSubdirectory("prime-update-e2e-").FullName;
        try
        {
            string staged = Path.Combine(root, "staged");
            Directory.CreateDirectory(staged);
            File.WriteAllText(Path.Combine(root, "ProjectPrime"),
                "#!/bin/sh\nprintf '1.0.0'\n");
            File.WriteAllText(Path.Combine(root, "paths.txt"), "player cartridge paths");
            WriteReleaseFiles(root, "1.0.0", ("ProjectPrime", "#!/bin/sh\nprintf '1.0.0'\n"));
            File.WriteAllText(Path.Combine(staged, "ProjectPrime"),
                "#!/bin/sh\nprintf '1.1.0'\n");
            WriteReleaseFiles(staged, "1.1.0", ("ProjectPrime", "#!/bin/sh\nprintf '1.1.0'\n"));

            UpdateTransactionResult result = new DesktopUpdateTransaction(root, staged)
                .Apply("1.0.0", "1.1.0");

            Assert.True(result.Success, result.Error);
            var start = new ProcessStartInfo("/bin/sh")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add(Path.Combine(root, "ProjectPrime"));
            using Process process = Process.Start(start)!;
            string reportedVersion = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("1.1.0", reportedVersion);
            Assert.Equal("player cartridge paths",
                File.ReadAllText(Path.Combine(root, "paths.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TransactionFailureRollsBackAndOldProcessTimeoutDoesNotMutate()
    {
        string root = Directory.CreateTempSubdirectory("prime-update-").FullName;
        try
        {
            string staged = Path.Combine(root, "staged");
            Directory.CreateDirectory(staged);
            File.WriteAllText(Path.Combine(root, "ProjectPrime"), "v1");
            WriteReleaseFiles(root, "1.0.0", ("ProjectPrime", "v1"));
            File.WriteAllText(Path.Combine(staged, "ProjectPrime"), "v2");
            File.WriteAllText(Path.Combine(staged, "new.dll"), "new");
            WriteReleaseFiles(staged, "1.1.0", ("ProjectPrime", "v2"), ("new.dll", "new"));
            UpdateTransactionResult failed = new DesktopUpdateTransaction(root, staged,
                failureInjector: (_, destination) => destination.EndsWith("new.dll", StringComparison.Ordinal)
                    ? new IOException("injected") : null).Apply("1.0.0", "1.1.0");
            Assert.False(failed.Success);
            Assert.Equal(UpdateTransactionState.RolledBack, failed.State);
            Assert.Equal("v1", File.ReadAllText(Path.Combine(root, "ProjectPrime")));
            Assert.False(File.Exists(Path.Combine(root, "new.dll")));
            Assert.Equal("1.0.0", ReleaseFilesJson.Parse(
                File.ReadAllBytes(Path.Combine(root, "release-files.json"))).Version);

            Directory.CreateDirectory(Path.Combine(root, ".update", "backup"));
            File.WriteAllText(Path.Combine(root, ".update", "backup", "must-remain"), "evidence");
            UpdateTransactionResult timedOut = new DesktopUpdateTransaction(root, staged,
                waitForProcess: (_, _) => false).Apply("1.0.0", "1.1.0", oldPid: 7);
            Assert.False(timedOut.Success);
            Assert.Equal("v1", File.ReadAllText(Path.Combine(root, "ProjectPrime")));
            Assert.True(File.Exists(Path.Combine(root, ".update", "backup", "must-remain")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RecoveryTreatsNullExpectedHashAsRequiredAbsence()
    {
        string root = Directory.CreateTempSubdirectory("prime-recovery-").FullName;
        try
        {
            string update = Path.Combine(root, ".update");
            string backup = Path.Combine(update, "backup");
            Directory.CreateDirectory(backup);
            string oldBinary = "old";
            string newBinary = "new";
            string oldDependency = "old dependency";
            File.WriteAllText(Path.Combine(root, "ProjectPrime"), newBinary);
            File.WriteAllText(Path.Combine(root, "old.dll"), oldDependency);
            File.WriteAllText(Path.Combine(backup, "ProjectPrime"), oldBinary);
            File.WriteAllText(Path.Combine(backup, "old.dll"), oldDependency);
            var journal = new UpdateTransactionJournal(1, "1.0.0", "1.1.0",
                UpdateTransactionState.Applying, Path.Combine(update, "staged"),
                [
                    new UpdateTransactionFile("ProjectPrime", true,
                        "backup/ProjectPrime", Hash(oldBinary), Hash(newBinary)),
                    new UpdateTransactionFile("old.dll", true,
                        "backup/old.dll", Hash(oldDependency), null)
                ]);
            File.WriteAllBytes(Path.Combine(update, "transaction.json"),
                JsonSerializer.SerializeToUtf8Bytes(journal));

            UpdateTransactionResult recovered = DesktopUpdateTransaction.Recover(root);
            Assert.True(recovered.Success, recovered.Error);
            Assert.Equal(UpdateTransactionState.RolledBack, recovered.State);
            Assert.Equal(oldBinary, File.ReadAllText(Path.Combine(root, "ProjectPrime")));
            Assert.Equal(oldDependency, File.ReadAllText(Path.Combine(root, "old.dll")));
            Assert.False(File.Exists(Path.Combine(update, "transaction.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TransactionLockPreventsConcurrentMutation()
    {
        string root = Directory.CreateTempSubdirectory("prime-lock-").FullName;
        try
        {
            string staged = Path.Combine(root, "staged");
            string update = Path.Combine(root, ".update");
            Directory.CreateDirectory(staged);
            Directory.CreateDirectory(update);
            File.WriteAllText(Path.Combine(root, "ProjectPrime"), "v1");
            WriteReleaseFiles(root, "1.0.0", ("ProjectPrime", "v1"));
            File.WriteAllText(Path.Combine(staged, "ProjectPrime"), "v2");
            WriteReleaseFiles(staged, "1.1.0", ("ProjectPrime", "v2"));
            using FileStream held = new(Path.Combine(update, "update.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            UpdateTransactionResult result = new DesktopUpdateTransaction(root, staged)
                .Apply("1.0.0", "1.1.0");
            Assert.False(result.Success);
            Assert.Equal("v1", File.ReadAllText(Path.Combine(root, "ProjectPrime")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CoordinatorDeduplicatesCheckAndWaitsForSafePoint()
    {
        byte[] manifest = UpdateManifestJson.Serialize(Manifest("1.1.0"));
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var feed = new FeedHandler(manifest, signer.SignData(
            manifest, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        var client = new UpdateManifestClient(feed,
            new UpdateTrust(signer.ExportSubjectPublicKeyInfo()), installedVersion: new Version(1, 0, 0),
            rid: "win-x64");
        var installer = new FakeInstaller();
        var coordinator = new UpdateCoordinator(client, installer,
            installedVersion: new Version(1, 0, 0));
        Task<UpdateCheckResult> first = coordinator.CheckAsync();
        Task<UpdateCheckResult> second = coordinator.CheckAsync();
        Assert.Same(first, second);
        Assert.IsType<UpdateCheckResult.Available>(await first);
        Assert.True(await coordinator.DownloadAsync());
        Assert.Equal(UpdateState.WaitingForSafePoint, coordinator.Status.State);
        Assert.False(await coordinator.InstallIfReadyAsync());
        coordinator.SetSafeToRestart(true);
        Assert.True(await coordinator.InstallIfReadyAsync());
        Assert.Equal(UpdateState.Restarting, coordinator.Status.State);
        Assert.Null(coordinator.AcquirePlayLease());

        // A completed normal check is cached for the process; this is a
        // different task wrapper but must not perform another feed request.
        UpdateCheckResult cached = await coordinator.CheckAsync();
        Assert.IsType<UpdateCheckResult.Available>(cached);
        Assert.Equal(2, feed.Requests);
        Assert.True(await coordinator.DownloadAsync());
    }

    private static UpdateManifest Manifest(string version = "1.0.0") => new(
        1, "stable", version, new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        [new UpdatePackage("win-x64", "ProjectPrime-v" + version + "-win-x64.zip", 3,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]);

    private static void WriteReleaseFiles(string root, string version,
        params (string Path, string Contents)[] files)
    {
        var entries = new List<ReleaseFile>();
        foreach ((string path, string contents) in files)
            entries.Add(new ReleaseFile(path, Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(contents))).ToLowerInvariant()));
        File.WriteAllBytes(Path.Combine(root, "release-files.json"),
            ReleaseFilesJson.Serialize(new ReleaseFilesManifest(1, version, entries)));
    }

    private static string Hash(string contents) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(contents))).ToLowerInvariant();

    private sealed class FeedHandler : HttpMessageHandler
    {
        private readonly byte[] _manifest;
        private readonly byte[] _signature;
        private int _requests;
        public int Requests => Volatile.Read(ref _requests);
        public FeedHandler(byte[] manifest, byte[] signature)
        {
            _manifest = manifest;
            _signature = signature;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            bool signature = request.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(signature ? _signature : _manifest)
            });
        }
    }

    private sealed class DownloadHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        public DownloadHandler(byte[] bytes) => _bytes = bytes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/redirect",
                StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    RequestMessage = request,
                    Headers = { Location = new Uri(
                        "https://objects.githubusercontent.com/Project-Prime/package.zip") }
                });
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/untrusted",
                StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    RequestMessage = request,
                    Headers = { Location = new Uri("https://example.com/package.zip") }
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(_bytes)
            });
        }
    }

    private sealed class FakeInstaller : IUpdateInstaller
    {
        public bool Allowed => true;
        public bool ExitAfterInstall => true;
        public Action<bool, string>? Finished { get; set; }
        public bool Prepare(UpdateInfo update, Action<float>? progress, out string error)
        {
            error = "";
            return true;
        }
        public bool Install(out string error)
        {
            error = "";
            return true;
        }
        public bool RequestPermission() => true;
        public Task<UpdatePrepareResult> PrepareAsync(UpdateInfo update,
            IProgress<UpdateProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdatePrepareResult(true, null));
    }
}
