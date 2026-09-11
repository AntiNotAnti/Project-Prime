using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using MphRead.Mods.Update;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Tests;

[Collection("Transport impairment")]
[Trait("RequiresGameContent", "true")]
public sealed class MapAcquisitionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "project-prime-map-download-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousData;
    private readonly string _previousMaps;
    private readonly IDisposable _contentContext;

    public MapAcquisitionServiceTests()
    {
        Directory.CreateDirectory(_root);
        _previousData = Environment.GetEnvironmentVariable("PRIME_DATA_DIRECTORY");
        _previousMaps = CustomRooms.MapDirectory;
        Environment.SetEnvironmentVariable("PRIME_DATA_DIRECTORY", Path.Combine(_root, "data"));
        CustomRooms.MapDirectory = Path.Combine(_root, "shipped-maps");
        _contentContext = ServerContent.PreserveContext("AMHE1");
        // Client-side compilation needs model texture and palette payloads;
        // headless tests may leave the process in server-only content mode.
        Read.ServerMode = false;
        string gameData = Path.GetFullPath(Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(AppContext.BaseDirectory, "../../../../../AMHE1"));
        ContentEnvironment.Open(gameData, "AMHE1");
    }

    [Fact]
    public async Task MissingExactMapDownloadsVerifiesInstallsCompilesAndBecomesReady()
    {
        (byte[] bytes, MapRequirement requirement) = await PackageAsync();
        var handler = new StubHandler(request =>
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Contains(requirement.ArtifactHash, request.RequestUri.AbsolutePath);
            return Response(HttpStatusCode.OK, bytes);
        });
        using var service = new MapAcquisitionService(handler);

        InstalledMap installed = await service.AcquireAsync(requirement,
            "wss://node.example.test/v1/control", null, CancellationToken.None);

        Assert.Equal(MapBuildState.Ready, installed.BuildState);
        Assert.Equal(requirement.ContentHash, installed.ContentIdentity.ContentHash);
        Assert.True(service.IsInstalled(requirement));
        Assert.Single(Directory.EnumerateFiles(MapStoragePaths.InstalledMaps, "*.fpmap"));
    }

    [Fact]
    public async Task ConcurrentServicesShareOneArtifactOwnerAndRemainDurable()
    {
        (byte[] bytes, MapRequirement requirement) = await PackageAsync();
        int downloads = 0;
        var services = new List<MapAcquisitionService>();
        try
        {
            for (int index = 0; index < 8; index++)
            {
                services.Add(new MapAcquisitionService(new StubHandler(request =>
                {
                    Interlocked.Increment(ref downloads);
                    return Response(HttpStatusCode.OK, bytes);
                })));
            }

            Task<InstalledMap>[] acquisitions = services.Select(service =>
                service.AcquireAsync(requirement, "wss://node.example.test/v1/control",
                    null, CancellationToken.None)).ToArray();
            InstalledMap[] installed = await Task.WhenAll(acquisitions);

            Assert.All(installed, map => Assert.Equal(MapBuildState.Ready, map.BuildState));
            Assert.Equal(1, downloads);
            Assert.True(services[0].IsInstalled(requirement));
        }
        finally
        {
            foreach (MapAcquisitionService service in services) service.Dispose();
        }
    }

    [Fact]
    public async Task CanceledWaiterDoesNotCancelTheArtifactOwner()
    {
        (byte[] bytes, MapRequirement requirement) = await PackageAsync();
        var gate = new GateHandler(bytes);
        using var owner = new MapAcquisitionService(gate);
        using var waiter = new MapAcquisitionService(gate);

        Task<InstalledMap> ownerTask = owner.AcquireAsync(requirement,
            "wss://node.example.test/v1/control", null, CancellationToken.None);
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        using var canceled = new CancellationTokenSource();
        Task<InstalledMap> waiterTask = waiter.AcquireAsync(requirement,
            "wss://node.example.test/v1/control", null, canceled.Token);
        // The owner is blocked after acquiring the process-wide artifact gate;
        // give the independent waiter a chance to reach its own WaitAsync.
        await Task.Delay(20);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiterTask);

        gate.Release.TrySetResult(true);
        InstalledMap installed = await ownerTask;
        Assert.Equal(MapBuildState.Ready, installed.BuildState);
        Assert.Equal(1, gate.Calls);
    }

    [Fact]
    public async Task TruncatedDownloadRetainsPartialAndResumesWithValidatedRange()
    {
        (byte[] bytes, MapRequirement requirement) = await PackageAsync();
        int split = Math.Max(1, bytes.Length / 2);
        int calls = 0;
        var handler = new StubHandler(request =>
        {
            int call = Interlocked.Increment(ref calls);
            if (call == 1)
                return Response(HttpStatusCode.OK, bytes[..split]);

            Assert.Equal(split, request.Headers.Range!.Ranges.Single().From);
            HttpResponseMessage response = Response(HttpStatusCode.PartialContent,
                bytes[split..]);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                split, bytes.Length - 1, bytes.Length);
            return response;
        });
        using var service = new MapAcquisitionService(handler);

        InstalledMap installed = await service.AcquireAsync(requirement,
            "wss://node.example.test/v1/control", null, CancellationToken.None);

        Assert.Equal(MapBuildState.Ready, installed.BuildState);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task InvalidResumeRangeDeletesPartialAndFailsClosed()
    {
        (byte[] bytes, MapRequirement requirement) = await PackageAsync();
        int split = Math.Max(1, bytes.Length / 2);
        string partial = Path.Combine(MapStoragePaths.MapCache,
            requirement.ArtifactHash.ToLowerInvariant() + MapBundle.Extension + ".partial");
        string sidecar = partial + ".json";
        Directory.CreateDirectory(MapStoragePaths.MapCache);
        File.WriteAllBytes(partial, bytes[..split]);
        File.WriteAllText(sidecar, JsonSerializer.Serialize(new
        {
            StableId = requirement.StableId,
            Version = requirement.Version,
            ContentHash = requirement.ContentHash,
            ArtifactHash = requirement.ArtifactHash,
            PackageSize = requirement.PackageSize
        }));

        using var service = new MapAcquisitionService(new StubHandler(request =>
        {
            Assert.Equal(split, request.Headers.Range!.Ranges.Single().From);
            HttpResponseMessage response = Response(HttpStatusCode.PartialContent,
                bytes[split..]);
            // Deliberately start one byte beyond the requested range.
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                split + 1, bytes.Length - 1, bytes.Length);
            return response;
        }));

        MapPackageException error = await Assert.ThrowsAsync<MapPackageException>(() =>
            service.AcquireAsync(requirement, "wss://node.example.test/v1/control",
                null, CancellationToken.None));

        Assert.Equal("MAP-DL-007", error.Code);
        Assert.False(File.Exists(partial));
        Assert.False(File.Exists(sidecar));
    }

    [Fact]
    public async Task CorruptPartialSidecarIsDiscardedBeforeFreshRetry()
    {
        (byte[] bytes, MapRequirement requirement) = await PackageAsync();
        string partial = Path.Combine(MapStoragePaths.MapCache,
            requirement.ArtifactHash.ToLowerInvariant() + MapBundle.Extension + ".partial");
        string sidecar = partial + ".json";
        Directory.CreateDirectory(MapStoragePaths.MapCache);
        File.WriteAllBytes(partial, bytes[..Math.Max(1, bytes.Length / 2)]);
        File.WriteAllText(sidecar, "{ definitely-not-json");
        int calls = 0;

        using var service = new MapAcquisitionService(new StubHandler(request =>
        {
            Assert.Null(request.Headers.Range);
            Interlocked.Increment(ref calls);
            return Response(HttpStatusCode.OK, bytes);
        }));

        InstalledMap installed = await service.AcquireAsync(requirement,
            "wss://node.example.test/v1/control", null, CancellationToken.None);

        Assert.Equal(MapBuildState.Ready, installed.BuildState);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RedirectAndWrongArtifactFailWithoutInstallingAnything()
    {
        (byte[] bytes, MapRequirement requirement) = await PackageAsync();
        using (var redirects = new MapAcquisitionService(new StubHandler(_ =>
            Response(HttpStatusCode.Redirect, []))))
        {
            MapPackageException error = await Assert.ThrowsAsync<MapPackageException>(() =>
                redirects.AcquireAsync(requirement, "wss://node.example.test/v1/control",
                    null, CancellationToken.None));
            Assert.Equal("MAP-DL-002", error.Code);
        }

        MapRequirement wrong = requirement with { ArtifactHash = new string('f', 64) };
        using (var hashes = new MapAcquisitionService(new StubHandler(_ =>
            Response(HttpStatusCode.OK, bytes))))
        {
            MapPackageException error = await Assert.ThrowsAsync<MapPackageException>(() =>
                hashes.AcquireAsync(wrong, "wss://node.example.test/v1/control",
                    null, CancellationToken.None));
            Assert.Equal("MAP-DL-004", error.Code);
        }
        Assert.False(Directory.Exists(MapStoragePaths.InstalledMaps)
            && Directory.EnumerateFiles(MapStoragePaths.InstalledMaps, "*.fpmap").Any());
    }

    [Theory]
    [InlineData("ws://node.example.test/v1/control")]
    [InlineData("wss://user@node.example.test/v1/control")]
    [InlineData("https://node.example.test/v1/control")]
    public async Task DownloadOriginMustBeTheSecureControlOrigin(string endpoint)
    {
        (_, MapRequirement requirement) = await PackageAsync();
        Assert.Throws<InvalidOperationException>(() =>
            MapAcquisitionService.DownloadUri(endpoint, requirement));
    }

    private async Task<(byte[] Bytes, MapRequirement Requirement)> PackageAsync()
    {
        string path = Path.Combine(_root, "incoming-" + Guid.NewGuid().ToString("N") + ".fpmap");
        const string stableId = "community.download-fixture";
        var version = new MapVersion(1, 4, 0);
        var definition = JsonSerializer.Deserialize(
            "{\"name\":\"DOWNLOAD FIXTURE\",\"textureSource\":\"MP3 PROVING GROUND\","
                + "\"materials\":[{\"name\":\"fixture\",\"sourceMaterial\":2}],"
                + "\"brushes\":[{\"min\":[-4,-1,-4],\"max\":[4,0,4]}],"
                + "\"spawns\":[{\"position\":[-2,1,0]},{\"position\":[2,1,0]}]}",
            MapJsonContext.Default.MapDefinition)!;
        MapProject project = MapProject.FromLegacy(definition, stableId);
        project.Version = version;
        project.Metadata = new MapProjectMetadata
        {
            Name = "Download Fixture",
            Author = "Tests",
            Redistribution = true
        };
        project.SupportedModes = [MapMode.Battle];
        byte[] recipe = JsonSerializer.SerializeToUtf8Bytes(project,
            MapJsonContext.Default.MapProject);
        MapBundleWriteResult written = await new MapBundleWriter().WriteAsync(path, new MapManifest
        {
            StableId = stableId, Version = version,
            Name = "Download Fixture", Author = "Tests", Redistribution = true,
            SupportedModes = [MapMode.Battle], Recipe = "map.json"
        }, [new("map.json", MapFileRole.Recipe, recipe)]);
        MapBundleValidationResult validation = new MapBundleValidator().Validate(path);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine,
            validation.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        string baseHash = ContentEnvironment.GetContentIdentity().ContentHash;
        string match = MapRequirement.ComputeMatchContentHash(baseHash,
            written.ContentIdentity.Identity.StableId,
            written.ContentIdentity.Identity.Version.ToString(), written.ContentIdentity.ContentHash,
            BuildVersion.Display, NetHeader.Version);
        return (await File.ReadAllBytesAsync(path), new(
            written.ContentIdentity.Identity.StableId,
            written.ContentIdentity.Identity.Version.ToString(), written.ContentIdentity.ContentHash,
            written.ArtifactHash, written.PackageSize, match));
    }

    private static HttpResponseMessage Response(HttpStatusCode status, byte[] bytes)
        => new(status) { Content = new ByteArrayContent(bytes) };

    private sealed class GateHandler(byte[] bytes) : HttpMessageHandler
    {
        internal readonly TaskCompletionSource<bool> Started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return Response(HttpStatusCode.OK, bytes);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    public void Dispose()
    {
        _contentContext.Dispose();
        CustomRooms.MapDirectory = _previousMaps;
        Environment.SetEnvironmentVariable("PRIME_DATA_DIRECTORY", _previousData);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
