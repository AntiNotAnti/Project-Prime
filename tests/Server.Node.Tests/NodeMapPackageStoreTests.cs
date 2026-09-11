using System.Text;
using MphRead.Mods.MapGen;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Maps;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class NodeMapPackageStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "project-prime-node-map-store-" + Guid.NewGuid().ToString("N"));

    public NodeMapPackageStoreTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ServesOnlyTheExactValidatedRedistributableArtifact()
    {
        (string path, MapBundleWriteResult written) = await WritePackageAsync(redistribution: true);
        NodeMapConfiguration configuration = Configuration(path, written);
        var store = new NodeMapPackageStore([configuration], _root);

        Assert.Single(store.ReadyConfigurations);
        Assert.Equal(NodeMapPackageState.Ready, Assert.Single(store.States).State);
        Assert.False(store.TryOpen(configuration.StableId!, configuration.Version!,
            new string('0', 64), out _, out _));
        Assert.True(store.TryOpen(configuration.StableId!, configuration.Version!,
            configuration.ArtifactHash!, out FileStream? stream, out var requirement));
        using (stream)
        {
            Assert.Equal(written.PackageSize, stream!.Length);
            Assert.Equal(configuration.MapContentHash, requirement!.ContentHash);
        }
    }

    [Fact]
    public async Task InvalidPackageIsIsolatedAndPostStartupMutationIsRejected()
    {
        (string privatePath, MapBundleWriteResult privateBundle) = await WritePackageAsync(false, "private.fpmap");
        (string path, MapBundleWriteResult written) = await WritePackageAsync(true, "mutable.fpmap");
        NodeMapConfiguration configuration = Configuration(path, written);
        NodeMapConfiguration invalidConfiguration = Configuration(privatePath, privateBundle) with
            { MapKey = "PRIVATE NODE STORE" };
        var store = new NodeMapPackageStore([invalidConfiguration, configuration], _root);
        Assert.Single(store.ReadyConfigurations);
        Assert.Equal(configuration.MapKey, store.ReadyConfigurations[0].MapKey);
        Assert.Contains(store.States, state => state.MapKey == invalidConfiguration.MapKey
            && state.State == NodeMapPackageState.Invalid);
        Assert.Single(NodeContentCatalog.FromConfiguration(store.ReadyConfigurations).Maps);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 0x5a;
        await File.WriteAllBytesAsync(path, bytes);

        Assert.Throws<InvalidDataException>(() => store.TryOpen(configuration.StableId!,
            configuration.Version!, configuration.ArtifactHash!, out _, out _));
    }

    private async Task<(string Path, MapBundleWriteResult Written)> WritePackageAsync(
        bool redistribution, string filename = "map.fpmap")
    {
        string path = Path.Combine(_root, filename);
        MapBundleWriteResult result = await new MapBundleWriter().WriteAsync(path, new MapManifest
        {
            StableId = "community.node-store",
            Version = new MapVersion(1, 4, 0),
            Name = "Node Store",
            Author = "Tests",
            Redistribution = redistribution,
            SupportedModes = [MapMode.Battle],
            Recipe = "map.json"
        }, [new("map.json", MapFileRole.Recipe, Encoding.UTF8.GetBytes(
            "{\"name\":\"NODE STORE\",\"materials\":[{\"name\":\"fixture\",\"sourceMaterial\":1}],"
            + "\"brushes\":[{\"min\":[0,0,0],\"max\":[1,1,1]}],"
            + "\"spawns\":[{\"position\":[0,2,0]},{\"position\":[2,2,0]}]}"))]);
        return (path, result);
    }

    private static NodeMapConfiguration Configuration(string path, MapBundleWriteResult written)
        => new()
        {
            MapKey = "NODE STORE",
            ContentHash = new string('1', 64),
            ContentVersion = "AMHE1",
            BuildVersion = "test",
            ProtocolVersion = 8,
            StableId = written.ContentIdentity.Identity.StableId,
            Version = written.ContentIdentity.Identity.Version.ToString(),
            MapContentHash = written.ContentIdentity.ContentHash,
            ArtifactHash = written.ArtifactHash,
            PackageSize = written.PackageSize,
            PackagePath = path
        };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
