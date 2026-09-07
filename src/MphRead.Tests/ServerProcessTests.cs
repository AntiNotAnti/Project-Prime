using System;
using System.IO;
using System.Threading;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class ServerProcessTests
    {
        [Fact]
        public void ProcessArgumentsPreserveCustomMapDirectoryAsOneAbsoluteArgument()
        {
            string mapDirectory = Path.Combine("custom maps", "arenas' $example");
            var start = ServerProcess.CreateStartInfo(mapDirectory);
            int index = start.ArgumentList.IndexOf("-mapdir");
            Assert.True(index >= 0);
            Assert.Equal(Path.GetFullPath(mapDirectory), start.ArgumentList[index + 1]);
            Assert.Equal(index + 2, start.ArgumentList.Count);
            Assert.False(start.UseShellExecute);
            Assert.Empty(start.Arguments);
        }

        [Fact]
        public void DirectoryHostingRequiresExplicitContentAndBoundedPortRange()
        {
            var master = new MasterServer(0);
            Assert.False(master.CanHost);
            master.SetHostPorts(29000, 29003);
            Assert.False(master.CanHost);
            master.SetHostContent(Path.GetTempPath(), "AMHE1");
            Assert.True(master.CanHost);
            Assert.Throws<ArgumentException>(() => master.SetHostContent(Path.GetTempPath(), "AMHE9"));
            Assert.Throws<ArgumentOutOfRangeException>(() => master.SetHostPorts(0, 20));
            Assert.Throws<ArgumentOutOfRangeException>(() => master.SetHostPorts(1000, 1064));
            Assert.Throws<ArgumentOutOfRangeException>(() => master.SetHostPorts(65535, 65536));
        }

        [Fact]
        public void DirectoryOnlyRemainsQueryableAndExplainsMissingHostingContent()
        {
            var master = new MasterServer(0);
            master.SetHostPorts(29000, 29003);
            Exception? failure = null;
            var worker = new Thread(() =>
            {
                try { master.Run(); }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true };
            worker.Start();
            try
            {
                Assert.True(SpinWait.SpinUntil(() => master.BoundPort > 0 || !worker.IsAlive, 3000));
                Assert.Null(failure);
                MasterListResult before = NetMasterClient.Query("127.0.0.1", master.BoundPort, 2000);
                Assert.True(before.Answered);
                Assert.Empty(before.Servers);
                HostedGame reply = NetMasterClient.RequestGame("127.0.0.1", master.BoundPort,
                    "MP1 SANCTORUS", GameMode.Battle, 60, 0, 8, "unlisted check", 2000);
                Assert.False(reply.Started);
                Assert.Contains("game content", reply.Reason);
                MasterListResult after = NetMasterClient.Query("127.0.0.1", master.BoundPort, 2000);
                Assert.True(after.Answered);
                Assert.Empty(after.Servers);
                Assert.Null(failure);
            }
            finally
            {
                master.Stop();
                Assert.True(worker.Join(3000));
            }
        }

        [Theory]
        [InlineData("MP1 SANCTORUS\nMP3 PROVING GROUND", 60, 0)]
        [InlineData("MP1 SANCTORUS | Nodes", 60, 0)]
        [InlineData("MP1 SANCTORUS # ignored", 60, 0)]
        [InlineData("MP1 SANCTORUS", Single.NaN, 0)]
        [InlineData("MP1 SANCTORUS", -1, 0)]
        [InlineData("MP1 SANCTORUS", 86401, 0)]
        [InlineData("MP1 SANCTORUS", 60, -1)]
        public void InvalidRulesCannotBecomeRotationFileInstructions(string room, float time, int points)
        {
            Assert.Throws<ArgumentException>(() => ServerProcess.Start(Path.GetTempPath(), "AMHE1",
                MapRotation.SingleMatch(room, GameMode.Battle, time, points), 0, 8, false));
        }

        [Fact]
        public void CancelledStartupNeverStartsChild()
        {
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => ServerProcess.Start(Path.GetTempPath(), "AMHE1",
                MapRotation.SingleMatch("MP1 SANCTORUS", GameMode.Battle, 60, 0), 0, 8, false, cancel: cancel.Token));
        }

        [Fact]
        public void EarlyChildFailureIncludesItsContentError()
        {
            string data = Path.Combine(Path.GetTempPath(), "fruity-empty-content-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(data);
            try
            {
                IOException error = Assert.Throws<IOException>(() => ServerProcess.Start(data, "AMHE1",
                    MapRotation.SingleMatch("MP1 SANCTORUS", GameMode.Battle, 60, 0), 0, 8, false));
                Assert.Contains("stopped during startup", error.Message);
                Assert.Contains("Server data is missing", error.Message);
                Assert.Empty(Directory.EnumerateFileSystemEntries(data));
            }
            finally
            {
                Directory.Delete(data);
            }
        }
    }
}
