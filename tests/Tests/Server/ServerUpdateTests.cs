using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using MphRead.Mods.Update;
using Xunit;

namespace MphRead.Tests;

public sealed class ServerUpdateTests
{
    [Theory]
    [InlineData("family")]
    [InlineData("protocol")]
    [InlineData("package-hash")]
    [InlineData("file-hash")]
    [InlineData("rid")]
    [InlineData("version")]
    [InlineData("manifest-path")]
    [InlineData("zip-traversal")]
    [InlineData("zip-link")]
    [InlineData("package-oversize")]
    [InlineData("expanded-oversize")]
    public async Task InvalidPackagesNeverReachValidationOrChangeInstallation(string fault)
    {
        using var fixture = new Fixture();
        fixture.Corrupt(fault);
        int validations = 0;
        using var updater = fixture.Create((_, _) => { validations++; return Task.FromResult<string?>(null); });
        Assert.Null(await updater.CheckAsync(CancellationToken.None));
        Assert.NotEmpty(updater.LastError!);
        Assert.Equal(0, validations);
        fixture.AssertOriginal();
        Assert.Empty(Directory.GetDirectories(ServerUpdate.StageRoot(fixture.Installation)));
    }

    [Fact]
    public async Task FailedContentValidationLeavesOriginalFilesAndRemovesStage()
    {
        using var fixture = new Fixture();
        int validations = 0;
        using var updater = fixture.Create((stage, _) =>
        {
            validations++;
            Assert.Equal("new executable", File.ReadAllText(Path.Combine(stage.Directory, "FruityPrime")));
            fixture.AssertOriginal();
            return Task.FromResult<string?>("configured rotation map missing");
        });
        Assert.Null(await updater.CheckAsync(CancellationToken.None));
        Assert.Equal(1, validations);
        Assert.Contains("configured rotation map missing", updater.LastError);
        fixture.AssertOriginal();
        Assert.Empty(Directory.GetDirectories(ServerUpdate.StageRoot(fixture.Installation)));
    }

    [Fact]
    public async Task UnconfirmedValidatorExitPreservesStageAndOriginalInstallation()
    {
        using var fixture = new Fixture();
        string? directory = null;
        using (var updater = fixture.Create((stage, _) =>
        {
            directory = stage.Directory;
            throw new ServerUpdateValidatorCleanupException("validator reap failed", new TimeoutException());
        }))
        {
            Assert.Null(await updater.CheckAsync(CancellationToken.None));
            Assert.Contains("Validator exit was not confirmed", updater.LastError);
            Assert.Contains(directory!, updater.LastError);
            Assert.True(Directory.Exists(directory));
            fixture.AssertOriginal();
        }
        Assert.True(Directory.Exists(directory));
        fixture.AssertOriginal();
    }

    [Fact]
    public async Task ValidationCannotChangeStagedBytesAfterHashVerification()
    {
        using var fixture = new Fixture();
        using var updater = fixture.Create((stage, _) =>
        {
            File.WriteAllText(Path.Combine(stage.Directory, "FruityPrime"), "changed during validation");
            return Task.FromResult<string?>(null);
        });
        Assert.Null(await updater.CheckAsync(CancellationToken.None));
        Assert.Contains("hash mismatch", updater.LastError);
        fixture.AssertOriginal();
    }

    [Fact]
    public async Task BusyPlayersAndPendingChildrenDeferInstallUntilOwnerClosesAdmission()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var fixture = new Fixture();
        using var updater = fixture.Create();
        updater.CheckWhenDue();
        int players = 1, pendingChildren = 1, stops = 0, resumes = 0, restarts = 0;
        bool Idle() => players == 0 && pendingChildren == 0;
        void Stop() { stops++; }
        void Resume() { resumes++; }
        void Restart(ServerRestart restart)
        {
            restarts++;
            Assert.Equal(1, stops);
            Assert.Null(restart.CreateStartInfo());
            Assert.Equal("new executable", File.ReadAllText(Path.Combine(fixture.Installation, "FruityPrime")));
            Assert.Equal("new library", File.ReadAllText(Path.Combine(fixture.Installation, "lib/runtime.dll")));
        }
        var deadline = Stopwatch.StartNew();
        while (updater.Ready == null && updater.LastError == null && deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            Assert.False(updater.PollIdle(Idle, Stop, Resume, Restart));
            await Task.Delay(5);
        }
        Assert.NotNull(updater.Ready);
        fixture.AssertOriginal();
        Assert.Equal(0, stops);
        players = 0;
        Assert.False(updater.PollIdle(Idle, Stop, Resume, Restart));
        fixture.AssertOriginal();
        pendingChildren = 0;
        Assert.True(updater.PollIdle(Idle, Stop, Resume, Restart));
        Assert.Equal(1, restarts);
        Assert.Equal(0, resumes);
        Assert.False(updater.PollIdle(Idle, Stop, Resume, Restart));
        Assert.Equal(1, restarts);
    }

    [Fact]
    public async Task AdmissionRecheckDefersUpdateIfOwnerBecomesBusy()
    {
        using var fixture = new Fixture();
        using var updater = fixture.Create();
        await AwaitReady(updater);
        bool idle = true;
        int resumes = 0;
        Assert.False(updater.PollIdle(() => idle, () => idle = false, () => resumes++,
            _ => throw new InvalidOperationException("Must not restart while busy.")));
        Assert.Equal(1, resumes);
        fixture.AssertOriginal();
    }

    [Fact]
    public async Task ChangedStageResumesAdmissionAndDoesNotRestart()
    {
        using var fixture = new Fixture();
        using var updater = fixture.Create();
        await AwaitReady(updater);
        File.WriteAllText(Path.Combine(updater.Ready!.Directory, "FruityPrime"), "tampered");
        int stops = 0, resumes = 0;
        Assert.False(updater.PollIdle(() => true, () => stops++, () => resumes++,
            _ => throw new InvalidOperationException("Must not restart with altered bytes.")));
        Assert.Equal(1, stops);
        Assert.Equal(1, resumes);
        Assert.Contains("hash mismatch", updater.LastError);
        fixture.AssertOriginal();
    }

    [Fact]
    public void RestartAndHelperPreserveArgumentsWithoutShellQuoting()
    {
        using var fixture = new Fixture();
        string[] arguments = { "-server", "-data", "data with spaces", "literal\"quote", "東京", "", "$(touch do-not-run)", "-rotation", "maps\\custom" };
        var restart = new ServerRestart(Path.Combine(fixture.Installation, "server app"), arguments,
            fixture.Root, Supervised: false);
        ProcessStartInfo info = restart.CreateStartInfo()!;
        Assert.Equal(restart.Executable, info.FileName);
        Assert.Equal(restart.WorkingDirectory, info.WorkingDirectory);
        Assert.Equal(arguments, info.ArgumentList.ToArray());
        Assert.Empty(info.Arguments);
        Assert.False(info.UseShellExecute);
        Assert.Null((restart with { Supervised = true }).CreateStartInfo());
        var stage = new ServerUpdateStage(Path.Combine(fixture.Root, "stage with spaces 東京"), fixture.Manifest);
        string plan = Path.Combine(stage.Directory, "handoff.json");
        info = ServerUpdateInstall.HelperStart(stage, plan);
        Assert.Equal(Path.Combine(stage.Directory, "FruityPrime"), info.FileName);
        Assert.Equal(new[] { "-server-apply-update", plan }, info.ArgumentList.ToArray());
        Assert.Equal(stage.Directory, info.WorkingDirectory);
        Assert.False(info.UseShellExecute);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementFailureRestoresExistingFilesAndRemovesNewFiles(bool firstFileNew)
    {
        using var fixture = new Fixture();
        if (firstFileNew) { File.Delete(Path.Combine(fixture.Installation, "FruityPrime")); }
        using var updater = fixture.Create();
        ServerUpdateStage stage = (await updater.CheckAsync(CancellationToken.None))!;
        Assert.NotNull(stage);
        var error = Assert.Throws<IOException>(() => ServerUpdateInstall.Apply(stage, fixture.Installation, index =>
        {
            if (index == 1)
            {
                Assert.Equal("new executable", File.ReadAllText(Path.Combine(fixture.Installation, "FruityPrime")));
                throw new IOException("injected second replacement failure");
            }
        }));
        Assert.Contains("injected", error.Message);
        if (firstFileNew) { Assert.False(File.Exists(Path.Combine(fixture.Installation, "FruityPrime"))); }
        else { fixture.AssertOriginal(); }
        Assert.Equal("old library", File.ReadAllText(Path.Combine(fixture.Installation, "lib/runtime.dll")));
        Assert.Empty(Directory.GetFiles(fixture.Installation, ".server-update-*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailuresDoNotChangeCommittedInstallation(bool diagnosticThrows)
    {
        using var fixture = new Fixture();
        using var updater = fixture.Create();
        ServerUpdateStage stage = (await updater.CheckAsync(CancellationToken.None))!;
        string? firstTemporary = null;
        var failures = new List<(string Path, Exception Error)>();
        ServerUpdateInstall.Apply(stage, fixture.Installation, index =>
        {
            if (index == 0)
            {
                firstTemporary = Assert.Single(Directory.GetFiles(fixture.Installation, ".server-update-*"));
                return;
            }
            // The first temporary was renamed into place. Occupy its old path
            // with a directory so File.Delete fails during final cleanup.
            Directory.CreateDirectory(firstTemporary!);
            string transaction = Assert.Single(Directory.GetDirectories(stage.Directory, "rollback-*"));
            Directory.Move(transaction, transaction + ".retained");
            File.WriteAllText(transaction, "cleanup obstruction");
        }, (path, error) =>
        {
            failures.Add((path, error));
            if (diagnosticThrows) { throw new IOException("diagnostic observer failed"); }
        });
        Assert.Equal("new executable", File.ReadAllText(Path.Combine(fixture.Installation, "FruityPrime")));
        Assert.Equal("new library", File.ReadAllText(Path.Combine(fixture.Installation, "lib/runtime.dll")));
        Assert.Equal(2, failures.Count);
        Assert.Equal(firstTemporary, failures[0].Path);
        Assert.Contains("rollback-", failures[1].Path);
        Assert.All(failures, failure => Assert.True(failure.Error is IOException or UnauthorizedAccessException));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailuresPreserveOriginalAndRollbackExceptions(bool diagnosticThrows)
    {
        using var fixture = new Fixture();
        using var updater = fixture.Create();
        ServerUpdateStage stage = (await updater.CheckAsync(CancellationToken.None))!;
        var original = new IOException("injected replacement failure");
        var failures = new List<(string Path, Exception Error)>();
        string? blockedTemporary = null;
        var aggregate = Assert.Throws<AggregateException>(() => ServerUpdateInstall.Apply(stage, fixture.Installation, index =>
        {
            if (index != 1) { return; }
            string target = Path.Combine(fixture.Installation, "FruityPrime");
            File.Delete(target);
            Directory.CreateDirectory(target); // Restoring the first backup must now fail.
            blockedTemporary = Assert.Single(Directory.GetFiles(Path.Combine(fixture.Installation, "lib"), ".server-update-*"));
            File.Delete(blockedTemporary);
            Directory.CreateDirectory(blockedTemporary); // Cleanup also fails independently.
            throw original;
        }, (path, error) =>
        {
            failures.Add((path, error));
            if (diagnosticThrows) { throw new IOException("diagnostic observer failed"); }
        }));
        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Same(original, aggregate.InnerExceptions[0]);
        Assert.True(aggregate.InnerExceptions[1] is IOException or UnauthorizedAccessException);
        Assert.Equal(blockedTemporary, Assert.Single(failures).Path);
        Assert.DoesNotContain(aggregate.InnerExceptions, error => ReferenceEquals(error, failures[0].Error));
        string transaction = Assert.Single(Directory.GetDirectories(stage.Directory, "rollback-*"));
        Assert.Contains(transaction, aggregate.Message);
        Assert.Equal("old executable", File.ReadAllText(Path.Combine(transaction, "0")));
        Assert.Equal("old library", File.ReadAllText(Path.Combine(fixture.Installation, "lib/runtime.dll")));
    }

    [Fact]
    public async Task UnixReplacementPreservesStagedExecutablePermissions()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var fixture = new Fixture();
        using var updater = fixture.Create();
        ServerUpdateStage stage = (await updater.CheckAsync(CancellationToken.None))!;
        string executable = Path.Combine(stage.Directory, "FruityPrime");
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(executable, mode);
        ServerUpdateInstall.Apply(stage, fixture.Installation);
        Assert.Equal(mode, File.GetUnixFileMode(Path.Combine(fixture.Installation, "FruityPrime")));
        Assert.Equal("new executable", File.ReadAllText(Path.Combine(fixture.Installation, "FruityPrime")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../outside")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    [InlineData("a\\b")]
    [InlineData("a//b")]
    [InlineData("a/./b")]
    [InlineData("a/../b")]
    [InlineData("a/.hidden")]
    [InlineData("a/trailing.")]
    [InlineData("a/trailing ")]
    [InlineData("a/NUL.txt")]
    [InlineData("a/com1")]
    [InlineData("a/evil\0file")]
    [InlineData("a/new\nline")]
    public void UnsafeRelativePathsAreRefused(string path)
        => Assert.Throws<IOException>(() => ServerUpdate.SafeRelative(path));

    [Theory]
    [InlineData("https://evil.example/package.zip")]
    [InlineData("http://release-assets.githubusercontent.com/package.zip")]
    [InlineData("https://release-assets.githubusercontent.com:444/package.zip")]
    [InlineData("https://user@release-assets.githubusercontent.com/package.zip")]
    [InlineData("https://objects.githubusercontent.com/package.zip#fragment")]
    public async Task UnsafeRedirectsAreNotFollowed(string destination)
    {
        using var fixture = new Fixture();
        fixture.Override = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri(destination);
            return response;
        };
        using var updater = fixture.Create();
        Assert.Null(await updater.CheckAsync(CancellationToken.None));
        Assert.Single(fixture.Requests);
        Assert.Contains("redirect", updater.LastError);
        fixture.AssertOriginal();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MetadataLimitsRejectAdvertisedAndStreamingOversize(bool advertiseLength)
    {
        using var fixture = new Fixture();
        fixture.Override = _ =>
        {
            byte[] large = new byte[ServerUpdate.MetadataLimit + 1];
            HttpContent content = advertiseLength ? new ByteArrayContent(large)
                : new StreamContent(new NonSeekableStream(large));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };
        using var updater = fixture.Create();
        Assert.Null(await updater.CheckAsync(CancellationToken.None));
        Assert.Contains("bound", updater.LastError);
        fixture.AssertOriginal();
    }

    [Fact]
    public void ConcurrentUpdaterCannotOwnTheSameInstallation()
    {
        using var fixture = new Fixture();
        using var first = fixture.Create();
        Assert.Throws<IOException>(() => fixture.Create());
        fixture.AssertOriginal();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadDeadlineIncludesStalledMetadataAndPackageBodies(bool package)
    {
        using var fixture = new Fixture();
        var body = new StalledReadStream();
        fixture.Override = uri => !package || uri.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) } : null;
        int validations = 0;
        using var updater = fixture.Create((_, _) => { validations++; return Task.FromResult<string?>(null); },
            downloadTimeout: TimeSpan.FromMilliseconds(50));
        Assert.Null(await updater.CheckAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("cancelled", updater.LastError);
        Assert.True(body.Disposed);
        Assert.Equal(package ? 3 : 1, fixture.Requests.Count);
        Assert.Equal(0, validations);
        Assert.Empty(Directory.GetDirectories(ServerUpdate.StageRoot(fixture.Installation)));
        fixture.AssertOriginal();
    }

    [Fact]
    public void LinkedInstallationIsRejectedBeforeRequests()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var fixture = new Fixture();
        string link = Path.Combine(fixture.Root, "linked-installation");
        Directory.CreateSymbolicLink(link, fixture.Installation);
        Assert.Throws<IOException>(() => new ServerUpdate(fixture.Options with { Installation = link },
            (_, _) => Task.FromResult<string?>(null), new Handler(fixture)));
        Assert.Empty(fixture.Requests);
        fixture.AssertOriginal();
    }

    [Fact]
    public async Task LinkedInstallTargetIsRejectedWithoutReplacingLinkOrTarget()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var fixture = new Fixture();
        string outside = Path.Combine(fixture.Root, "outside-executable");
        File.WriteAllText(outside, "outside original");
        string target = Path.Combine(fixture.Installation, "FruityPrime");
        File.Delete(target);
        File.CreateSymbolicLink(target, outside);
        using var updater = fixture.Create();
        ServerUpdateStage stage = (await updater.CheckAsync(CancellationToken.None))!;
        Assert.NotNull(stage);
        Assert.Throws<IOException>(() => ServerUpdateInstall.Apply(stage, fixture.Installation));
        Assert.Equal("outside original", File.ReadAllText(outside));
        Assert.NotNull(new FileInfo(target).LinkTarget);
    }

    [Fact]
    public async Task TrustedRedirectLoopsStopAfterThreeRedirects()
    {
        using var fixture = new Fixture();
        fixture.Override = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://objects.githubusercontent.com/loop");
            return response;
        };
        using var updater = fixture.Create();
        Assert.Null(await updater.CheckAsync(CancellationToken.None));
        Assert.Equal(4, fixture.Requests.Count);
        Assert.Contains("redirect", updater.LastError);
        fixture.AssertOriginal();
    }

    [Fact]
    public async Task TrustedAssetRedirectPreservesHashValidation()
    {
        using var fixture = new Fixture();
        fixture.Override = uri =>
        {
            if (uri.Host == "github.com" && uri.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.Location = new Uri("https://release-assets.githubusercontent.com/package");
                return response;
            }
            return uri.Host == "release-assets.githubusercontent.com"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Package) }
                : null;
        };
        using var updater = fixture.Create();
        Assert.NotNull(await updater.CheckAsync(CancellationToken.None));
        Assert.Null(updater.LastError);
        Assert.Equal(4, fixture.Requests.Count);
        fixture.AssertOriginal();
    }

    [Fact]
    public void UpdateRequiresExplicitOptInAndNeverUsesRelayUpstream()
    {
        Assert.False(ServerUpdate.Enabled(Array.Empty<string>()));
        Assert.True(ServerUpdate.Enabled(new[] { "-AUTOUPDATE" }));
        Assert.False(ServerUpdate.Enabled(new[] { "-autoupdate", "-noupdate" }));
        Assert.False(ServerUpdate.Enabled(new[] { "-noupdate", "-autoupdate" }));
        Assert.True(ServerUpdate.Enabled(new[] { "--autoupdate" }));
        Assert.False(ServerUpdate.Enabled(new[] { "-autoupdate", "--noupdate" }));
        Assert.False(ServerUpdate.Enabled(new[] { "--noupdate", "--autoupdate" }));
        using var fixture = new Fixture();
        Assert.Throws<ArgumentException>(() => new ServerUpdate(fixture.Options with { Repository = "liveteklol/Fruity-Prime" },
            (_, _) => Task.FromResult<string?>(null), new Handler(fixture)));
    }

    private static async Task AwaitReady(ServerUpdate updater)
    {
        updater.CheckWhenDue();
        var deadline = Stopwatch.StartNew();
        while (updater.Ready == null && updater.LastError == null && deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            Assert.False(updater.PollIdle(() => false, () => throw new Exception("not idle"),
                () => throw new Exception("not installing"), _ => throw new Exception("not restarting")));
            await Task.Delay(5);
        }
        Assert.NotNull(updater.Ready);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "fruity-updater-tests-" + Guid.NewGuid().ToString("N"));
        public string Installation => Path.Combine(Root, "installation");
        public ServerUpdateManifest Manifest { get; set; }
        public byte[] Package { get; private set; }
        public List<Uri> Requests { get; } = new();
        public Func<Uri, HttpResponseMessage?>? Override { get; set; }
        public ServerUpdateOptions Options => new("owned/authoritative", "server-test-manifest.json", "test", new Version(2, 0), Installation,
            new ServerRestart(Path.Combine(Installation, "FruityPrime"), new[] { "-server", "-data", "content path" }, Root, !OperatingSystem.IsWindows()));
        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Installation, "lib"));
            File.WriteAllText(Path.Combine(Installation, "FruityPrime"), "old executable");
            File.WriteAllText(Path.Combine(Installation, "lib/runtime.dll"), "old library");
            Package = MakeZip("FruityPrime", link: false);
            Manifest = new(1, NetWireFamily.Authoritative, NetHeader.Version, "2.1", "test", "server-test.zip", Package.Length,
                Hash(Package), "FruityPrime", new[] { MakeFile("FruityPrime", "new executable"), MakeFile("lib/runtime.dll", "new library") });
        }
        public ServerUpdate Create(Func<ServerUpdateStage, CancellationToken, Task<string?>>? validate = null,
            TimeSpan? downloadTimeout = null)
            => new(Options, validate ?? ((_, _) => Task.FromResult<string?>(null)), new Handler(this), downloadTimeout);
        public void Corrupt(string fault)
        {
            Manifest = fault switch
            {
                "family" => Manifest with { Family = NetWireFamily.LegacyRelay },
                "protocol" => Manifest with { Protocol = (byte)(NetHeader.Version - 1) },
                "package-hash" => Manifest with { PackageSha256 = new string('0', 64) },
                "file-hash" => Manifest with { Files = new[] { Manifest.Files[0] with { Sha256 = new string('0', 64) }, Manifest.Files[1] } },
                "rid" => Manifest with { Rid = "other" },
                "version" => Manifest with { Version = "2.2" },
                "manifest-path" => Manifest with { Files = new[] { Manifest.Files[0] with { Path = "../outside" }, Manifest.Files[1] } },
                "package-oversize" => Manifest with { PackageBytes = ServerUpdate.PackageLimit + 1 },
                "expanded-oversize" => Manifest with { Files = new[] { Manifest.Files[0] with { Bytes = ServerUpdate.ExpandedLimit + 1 }, Manifest.Files[1] } },
                _ => Manifest
            };
            if (fault is "zip-traversal" or "zip-link")
            {
                Package = MakeZip(fault == "zip-traversal" ? "../outside" : "FruityPrime", fault == "zip-link");
                Manifest = Manifest with { PackageBytes = Package.Length, PackageSha256 = Hash(Package) };
            }
        }
        public HttpResponseMessage Respond(Uri uri)
        {
            Requests.Add(uri);
            if (Override?.Invoke(uri) is HttpResponseMessage response) { return response; }
            byte[] body = uri.AbsolutePath switch
            {
                "/repos/owned/authoritative/releases/latest" => JsonSerializer.SerializeToUtf8Bytes(new
                {
                    tag_name = "v2.1", draft = false, prerelease = false,
                    assets = new[] { new { name = Options.ManifestAsset, browser_download_url = "https://github.com/owned/authoritative/releases/download/v2.1/" + Options.ManifestAsset } }
                }),
                "/owned/authoritative/releases/download/v2.1/server-test-manifest.json" => JsonSerializer.SerializeToUtf8Bytes(Manifest),
                "/owned/authoritative/releases/download/v2.1/server-test.zip" => Package,
                _ => throw new InvalidOperationException("Unexpected HTTP request: " + uri)
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        }
        public void AssertOriginal()
        {
            Assert.Equal("old executable", File.ReadAllText(Path.Combine(Installation, "FruityPrime")));
            Assert.Equal("old library", File.ReadAllText(Path.Combine(Installation, "lib/runtime.dll")));
        }
        private static byte[] MakeZip(string executable, bool link)
        {
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (path, value) in new[] { (executable, "new executable"), ("lib/runtime.dll", "new library") })
                {
                    var entry = archive.CreateEntry(path);
                    entry.ExternalAttributes = (link && path == executable ? 0xA1FF : 0x81A4) << 16;
                    using var stream = entry.Open();
                    stream.Write(Encoding.UTF8.GetBytes(value));
                }
            }
            return output.ToArray();
        }
        private static ServerUpdateFile MakeFile(string path, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            return new(path, bytes.Length, Hash(bytes));
        }
        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        public void Dispose() { Directory.Delete(Root, recursive: true); }
    }

    private sealed class Handler(Fixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(fixture.Respond(request.RequestUri!));
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }

    private sealed class StalledReadStream : MemoryStream
    {
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
