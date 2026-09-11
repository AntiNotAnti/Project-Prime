using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
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
        MapBundleWriteResult written = await new MapBundleWriter().WriteAsync(path, new MapManifest
        {
            StableId = "community.download-fixture", Version = new MapVersion(1, 4, 0),
            Name = "Download Fixture", Author = "Tests", Redistribution = true,
            SupportedModes = [MapMode.Battle], Recipe = "map.json"
        }, [new("map.json", MapFileRole.Recipe, Encoding.UTF8.GetBytes(
            "{\"name\":\"DOWNLOAD FIXTURE\",\"textureSource\":\"MP3 PROVING GROUND\","
            + "\"materials\":[{\"name\":\"fixture\",\"sourceMaterial\":1}],"
            + "\"brushes\":[{\"min\":[-4,-1,-4],\"max\":[4,0,4]}],"
            + "\"spawns\":[{\"position\":[-2,1,0]},{\"position\":[2,1,0]}]}"))]);
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
