using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class LauncherCompatibilityTests
    {
        [Theory]
        [InlineData(NetWireFamily.LegacyRelay, 5)]
        [InlineData(NetWireFamily.LegacyRelay, 6)]
        [InlineData(NetWireFamily.Unknown, 6)]
        [InlineData(NetWireFamily.Authoritative, 5)]
        public void OnlineIncompatibleServersAreIdentifiedBeforeSelection(NetWireFamily family, int protocol)
        {
            var status = new ServerStatus { Online = true, Family = family, Protocol = protocol };
            Assert.False(status.Compatible);
            string description = TextLauncher.Describe(status);
            Assert.StartsWith("Online — ", description);
            Assert.Contains(status.IncompatibilityReason, description);
            Assert.NotEmpty(status.IncompatibilityReason);
        }

        [Fact]
        public void CompatibleServerDescriptionPreservesRoomPlayersAndLatency()
        {
            var status = new ServerStatus { Online = true, Family = NetWireFamily.Authoritative,
                Protocol = NetHeader.Version, RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle,
                Players = 3, MaxPlayers = 8, Latency = 42 };
            Assert.True(status.Compatible);
            Assert.Equal("MP1 SANCTORUS (Battle) 3/8 42 ms", TextLauncher.Describe(status));
        }
    }
}
