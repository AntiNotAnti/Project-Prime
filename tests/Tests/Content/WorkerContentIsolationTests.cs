using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.Network;
using MphRead.Text;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class WorkerContentIsolationTests
{
    [Fact]
    public void ActiveLeasesFreezeContextAndFileBytesAcrossSceneClose()
    {
        string directory = Path.Combine(Path.GetTempPath(), "worker-content-" + Guid.NewGuid());
        bool mode = Read.ServerMode;
        using var context = ServerContent.PreserveContext("AMHE1");
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "_bin"));
            Directory.CreateDirectory(Path.Combine(directory, "models"));
            Directory.CreateDirectory(Path.Combine(directory, "levels"));
            string file = Path.Combine(directory, "_bin", "arm9.bin");
            File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
            ContentEnvironment.Open(directory, "AMHE1");
            using Scene first = Scene.CreateHeadless();
            using Scene second = Scene.CreateHeadless();
            WorkerContent content = first.Content!;
            Assert.Same(content, second.Content);
            Assert.Equal("AMHE1", content.Version);
            Assert.Equal(64, content.ContentHash.Length);
            Assert.True(ContentEnvironment.ResourceExists(file));
            Assert.False(ContentEnvironment.ResourceExists(Path.Combine(directory, "missing.bin")));
            File.WriteAllBytes(file, new byte[] { 9 });
            byte[] read = ContentEnvironment.ReadBytes(file);
            read[0] = 8;
            Assert.Equal(new byte[] { 1, 2, 3 }, ContentEnvironment.ReadBytes(file));
            File.Delete(file);
            Assert.True(ContentEnvironment.ResourceExists(file));
            Assert.Equal(new byte[] { 1, 2, 3 }, ContentEnvironment.ReadBytes(file));
            Assert.Throws<InvalidOperationException>(() => ContentEnvironment.Open(directory, "AMHE0"));
            Assert.Throws<InvalidOperationException>(() => Paths.MphKey = "AMHE0");
            Assert.Throws<InvalidOperationException>(() => Paths.SetPath("AMHE1", directory));
            Assert.Throws<InvalidOperationException>(() => Paths.UpdatePaths());
            Assert.Throws<InvalidOperationException>(() => Read.ServerMode = false);
            Assert.Throws<InvalidOperationException>(() => Read.ApplyFixes = !Read.ApplyFixes);
            Assert.Throws<InvalidOperationException>(() => Scene.Language = Scene.Language == Language.French
                ? Language.German : Language.French);
            Assert.Throws<InvalidOperationException>(() => RuntimeData.Load());
            Assert.Throws<InvalidOperationException>(() => Read.ClearCache());
            Assert.Throws<InvalidOperationException>(() => Strings.ClearCache());
            Assert.Throws<InvalidOperationException>(() => AiPersonality.ClearCache());
            Assert.Throws<InvalidOperationException>(() => Collision.ClearCache());
            first.CloseHeadless();
            first.CloseHeadless();
            Assert.True(Read.ServerMode);
            Assert.Equal(new byte[] { 1, 2, 3 }, ContentEnvironment.ReadBytes(file));
            Assert.Same(content, second.Content);
            Assert.Throws<InvalidOperationException>(() => ContentEnvironment.Open(directory, "AMHE1"));
        }
        finally
        {
            Read.ServerMode = mode;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public async Task ConcurrentSceneLifetimesPreserveCachesAndOwnMutableTemplates()
    {
        bool mode = Read.ServerMode;
        using var context = ServerContent.PreserveContext("AMHE1");
        try
        {
            ContentEnvironment.Open(FindContent(), "AMHE1");
            using Scene owner = Scene.CreateHeadless();
            string modelName = Metadata.HunterModels[Hunter.Samus][0];
            ModelInstance model = Read.GetModelInstance(modelName);
            var strings = Strings.ReadStringTable(StringTables.WeaponNames);
            AiPersonality.Load(owner.Players[0], GameMode.Battle);
            var personality = owner.Players[0].AiData.Personality!;
            string label = personality.Label;
            ModelMetadata metadata = Metadata.GetModelByName("AlimbicCapsule", MetaDir.Models)!;
            CollisionInstance collision = Collision.GetCollision(metadata);
            Effect effect = Read.LoadEffect(30, persistent: true);
            Effect otherEffect = Read.LoadEffect(30, persistent: false);
            Assert.True(effect.Persistent);
            Assert.False(otherEffect.Persistent);
            Assert.False(Read.GetEffect(30)!.Persistent);
            Particle firstParticle = effect.Elements.SelectMany(element => element.Particles).First();
            Particle secondParticle = otherEffect.Elements.SelectMany(element => element.Particles).First();
            Assert.NotSame(firstParticle.Model, secondParticle.Model);
            firstParticle.Node.Enabled = !secondParticle.Node.Enabled;
            Assert.NotEqual(firstParticle.Node.Enabled, secondParticle.Node.Enabled);
            var roomMetadata = Metadata.GetRoomByName("MP1 SANCTORUS").Item1!;
            CollisionInstance roomCollision = Collision.GetCollision(roomMetadata);
            Assert.NotEmpty(roomCollision.Info.Portals);
            var roomCopy = new CollisionInstance("copy", roomCollision.Info, isEntity: false);
            roomCopy.Info.Portals[0].Active = false;
            Assert.True(roomCollision.Info.Portals[0].Active);
            int modelReads = 0;
            void Observe(string name, MetaDir dir, bool fh) { if (name == modelName) modelReads++; }
            ContentFiles.ModelReading += Observe;
            try
            {
                await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
                {
                    using Scene scene = Scene.CreateHeadless();
                    Assert.Same(owner.Content, scene.Content);
                    ModelInstance other = Read.GetModelInstance(modelName);
                    Assert.Equal(model.Model.Id, other.Model.Id);
                    Assert.NotSame(model.Model, other.Model);
                    other.Model.Nodes[0].Enabled = !model.Model.Nodes[0].Enabled;
                    Assert.Same(strings, Strings.ReadStringTable(StringTables.WeaponNames));
                    AiPersonality.Load(scene.Players[0], GameMode.Battle);
                    Assert.NotSame(personality, scene.Players[0].AiData.Personality);
                    scene.Players[0].AiData.Personality!.Label = "private";
                    var otherCollision = Collision.GetCollision(metadata);
                    Assert.NotSame(collision.Info, otherCollision.Info);
                    Assert.Same(collision.Info.Points, otherCollision.Info.Points);
                    otherCollision.Active = false;
                })));
            }
            finally { ContentFiles.ModelReading -= Observe; }
            Assert.Equal(0, modelReads);
            Assert.Equal(label, personality.Label);
            Assert.True(collision.Active);
            Assert.True(Read.ServerMode);
            Assert.Same(strings, Strings.ReadStringTable(StringTables.WeaponNames));
        }
        finally { Read.ServerMode = mode; }
    }

    [Fact]
    public async Task WorkerLeasePinsSnapshotAcrossEmptyMatchGapsAndConcurrentFileReplacement()
    {
        string directory = Path.Combine(Path.GetTempPath(), "worker-gap-" + Guid.NewGuid());
        using var saved = ServerContent.PreserveContext("AMHE1");
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "_bin"));
            Directory.CreateDirectory(Path.Combine(directory, "models"));
            Directory.CreateDirectory(Path.Combine(directory, "levels"));
            string file = Path.Combine(directory, "_bin", "arm9.bin");
            byte[] original = Enumerable.Repeat((byte)7, 4096).ToArray();
            File.WriteAllBytes(file, original);
            ContentEnvironment.Open(directory, "AMHE1");
            using var worker = ContentEnvironment.AcquireContent();
            using (Scene first = Scene.CreateHeadless()) Assert.Same(worker.Content, first.Content);
            Assert.Throws<InvalidOperationException>(() => Paths.SetPath("AMHE1", directory));
            Assert.Throws<InvalidOperationException>(() => ContentEnvironment.Open(directory, "AMHE1"));
            Task writer = Task.Run(() =>
            {
                for (int index = 0; index < 64; index++) File.WriteAllBytes(file, Enumerable.Repeat((byte)index, 512).ToArray());
            });
            Task[] readers = Enumerable.Range(0, 8).Select(index => Task.Run(() =>
            {
                for (int read = 0; read < 32; read++)
                {
                    using Scene sibling = Scene.CreateHeadless();
                    Assert.Same(worker.Content, sibling.Content);
                    Assert.Equal(original, ContentEnvironment.ReadBytes(file));
                }
            })).ToArray();
            await Task.WhenAll(readers.Append(writer));
            Assert.Equal(original, ContentEnvironment.ReadBytes(file));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SnapshotHashUsesCanonicalRootRolesAndRejectsAmbiguousResourceNames()
    {
        string directory = Path.Combine(Path.GetTempPath(), "worker-hash-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "asset.bin"), new byte[] { 1, 2 });
            var plain = new WorkerContent("AMHE1", directory, "", "AMFE0", null);
            var canonical = new WorkerContent("AMHE1", Path.Combine(directory, "."), "", "AMFE0", null);
            var firstHunt = new WorkerContent("AMHE1", "", directory, "AMFE0", null);
            Assert.Equal(plain.ContentHash, canonical.ContentHash);
            Assert.NotEqual(plain.ContentHash, firstHunt.ContentHash);
            _ = new WorkerContent("AMHE1", directory, Path.Combine(directory, "."), "AMFE0", null);
            if (Path.DirectorySeparatorChar != '\\')
            {
                File.WriteAllBytes(Path.Combine(directory, "ambiguous\\name.bin"), new byte[] { 3 });
                Assert.Throws<InvalidDataException>(() => new WorkerContent("AMHE1", directory, "", "AMFE0", null));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SnapshotRejectsManifestReplacementAndLateUnverifiedResources()
    {
        string directory = Path.Combine(Path.GetTempPath(), "worker-manifest-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            byte[] bytes = new byte[] { 1, 2, 3 };
            File.WriteAllBytes(Path.Combine(directory, "asset.bin"), bytes);
            var manifest = new ServerContentManifest(1, "headless-cpu-v1", "AMHE1", "arm9", new[] { "room" }, 1,
                new[] { new ServerContentFile("asset.bin", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()) },
                new[] { new ServerContentScenario("room", GameMode.Battle) }, 2);
            string manifestPath = Path.Combine(directory, ServerContentPackage.ManifestName);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
            _ = new WorkerContent("AMHE1", directory, "", "AMFE0", manifest);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest with { Rooms = new[] { "different-room" } }));
            Assert.Throws<InvalidDataException>(() => new WorkerContent("AMHE1", directory, "", "AMFE0", manifest));
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
            File.WriteAllBytes(Path.Combine(directory, "late.bin"), bytes);
            Assert.Throws<InvalidDataException>(() => new WorkerContent("AMHE1", directory, "", "AMFE0", manifest));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void PublishedWeaponTablesAndNestedParametersCannotBeMutated()
    {
        foreach (var table in new[] { Weapons.Current, Weapons.ForceFieldLockWeapons, Weapons.PlatformWeapons, Weapons.Ricochets })
        {
            Assert.Throws<NotSupportedException>(() => ((IList<WeaponInfo>)table)[0] = table[0]);
            Assert.Throws<NotSupportedException>(() => ((IList<byte>)table[0].DrawFuncIds)[0] = 99);
            Assert.Throws<NotSupportedException>(() => ((IList<ushort>)table[0].Colors)[0] = 99);
        }
        Assert.Throws<NotSupportedException>(() => ((IList<BeamType>)Weapons.AffinityWeapons)[0] = BeamType.Missile);
    }

    private static string FindContent()
    {
        for (DirectoryInfo? dir = new(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "AMHE1");
            if (File.Exists(Path.Combine(path, "_bin", "arm9.bin"))) return path;
        }
        throw new DirectoryNotFoundException("AMHE1 test content is required.");
    }
}
