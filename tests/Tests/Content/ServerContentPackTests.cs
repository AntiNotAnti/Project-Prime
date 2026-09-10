using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class ServerContentPackTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "project-prime-content-test-" + Guid.NewGuid().ToString("N"));

        public ServerContentPackTests() => Directory.CreateDirectory(_directory);

        [Fact]
        public void ValidatesExactManifestAndEveryFileHash()
        {
            ServerContentManifest manifest = Create();
            Assert.Equal(manifest.Files[0], ServerContentPack.Validate(_directory, "AMHE1").Files[0]);
            File.WriteAllBytes(Path.Combine(_directory, "levels", "test.bin"), [4, 3, 2, 1]);
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
        }

        [Fact]
        public void RejectsMissingFilesAndUnexpectedAssets()
        {
            Create();
            string extra = Path.Combine(_directory, "unlisted.bin");
            File.WriteAllBytes(extra, [9]);
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
            File.Delete(extra);
            File.Delete(Path.Combine(_directory, "levels", "test.bin"));
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
        }

        [Theory]
        [InlineData("../outside.bin")]
        [InlineData("/absolute.bin")]
        [InlineData("levels/../../outside.bin")]
        [InlineData("levels\\test.bin")]
        [InlineData("levels//test.bin")]
        [InlineData("C:/test.bin")]
        public void RejectsUnsafeRelativePaths(string path)
        {
            ServerContentManifest manifest = Create();
            Write(manifest with { Files = [manifest.Files[0] with { Path = path }] });
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
        }

        [Fact]
        public void RejectsVersionProfileAndIdentityMismatch()
        {
            ServerContentManifest manifest = Create();
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHP1"));
            Write(manifest with { Profile = "future-profile" });
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
            Write(manifest with { SourceArm9Sha256 = new string('0', 64) });
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
        }

        [Fact]
        public void RejectsInvalidOrUncoveredRoomModeDeclarations()
        {
            ServerContentManifest manifest = Create();
            Write(manifest with { RoomPlayerCount = 8 });
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
            Write(manifest with { Scenarios = [] });
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
            Write(manifest with { Scenarios = [new ServerContentScenario("MP4 HIGHGROUND", GameMode.Battle)] });
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
            Write(manifest with { Scenarios = [new ServerContentScenario("MP1 SANCTORUS", GameMode.None)] });
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
            Write(manifest with { Scenarios = [manifest.Scenarios[0], manifest.Scenarios[0]] });
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
        }

        [Fact]
        public void RejectsDuplicateEntriesAndInvalidSizes()
        {
            ServerContentManifest manifest = Create();
            Write(manifest with { Files = [manifest.Files[0], manifest.Files[0]] });
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
            Write(manifest with { Files = [manifest.Files[0] with { Bytes = -1 }] });
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
            Write(manifest with { Files = [manifest.Files[0] with { Bytes = 65L * 1024 * 1024 }] });
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
        }

        [Fact]
        public void RejectsSymlinkedDependencies()
        {
            Create();
            string path = Path.Combine(_directory, "levels", "test.bin");
            string target = Path.Combine(_directory, "actual.bin");
            File.Move(path, target);
            File.CreateSymbolicLink(path, target);
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
        }

        [Fact]
        public void BakeCannotWriteInsideSourceThroughDirectoryAlias()
        {
            string source = Path.Combine(_directory, "source");
            string alias = Path.Combine(_directory, "alias");
            Directory.CreateDirectory(source);
            Directory.CreateSymbolicLink(alias, source);
            Assert.Throws<ProgramException>(() => ServerContentPack.Bake(source,
                Path.Combine(alias, "package"), "AMHE1", ["MP1 SANCTORUS"]));
            Assert.False(Directory.Exists(Path.Combine(source, "package")));
        }

        [Fact]
        public void RejectsMalformedOrMissingManifestFields()
        {
            File.WriteAllText(Path.Combine(_directory, ServerContentPack.ManifestName), "{");
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
            File.WriteAllText(Path.Combine(_directory, ServerContentPack.ManifestName), "{}");
            Assert.Throws<ProgramException>(() => ServerContentPack.Validate(_directory, "AMHE1"));
        }

        private ServerContentManifest Create()
        {
            byte[] bytes = [1, 2, 3, 4];
            Directory.CreateDirectory(Path.Combine(_directory, "levels"));
            File.WriteAllBytes(Path.Combine(_directory, "levels", "test.bin"), bytes);
            var manifest = new ServerContentManifest(1, ServerContentPack.Profile, "AMHE1",
                ServerContentPack.Amhe1Arm9Sha256, ["MP1 SANCTORUS"], 600,
                [new ServerContentFile("levels/test.bin", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)))], [new ServerContentScenario("MP1 SANCTORUS", GameMode.Battle)], NetLaunch.RoomPlayerCount);
            Write(manifest);
            return manifest;
        }

        private void Write(ServerContentManifest manifest) => File.WriteAllText(
            Path.Combine(_directory, ServerContentPack.ManifestName), JsonSerializer.Serialize(manifest));

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
