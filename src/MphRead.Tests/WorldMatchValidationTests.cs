using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class WorldMatchValidationTests
    {
        private static WorldRecord Match(uint phase = 0, uint goal = 0, float objectiveSeconds = 0)
            => new(WorldRecordKind.Match, 255, 0, 0, new Vector3(-1, objectiveSeconds, 0),
                3, phase, goal, uint.MaxValue, 0);

        [Theory]
        [InlineData(0u)]
        [InlineData(1u)]
        [InlineData(2u)]
        [InlineData(3u)]
        [InlineData(4u)]
        public void MatchPhasesRoundTripWithUnlimitedClockAndZeroGoals(uint phase)
        {
            WorldRecord original = Match(phase);
            byte[] bytes = new byte[WorldRecord.Size];
            original.Write(bytes);
            Assert.True(WorldRecord.TryRead(bytes, out WorldRecord decoded));
            Assert.Equal(original, decoded);
        }

        [Fact]
        public void MaximumSignedGoalAndFiniteObjectiveRoundTrip()
        {
            WorldRecord original = Match(goal: int.MaxValue, objectiveSeconds: 90);
            byte[] bytes = new byte[WorldRecord.Size];
            original.Write(bytes);
            Assert.True(WorldRecord.TryRead(bytes, out WorldRecord decoded));
            Assert.Equal(original, decoded);
        }

        [Theory]
        [InlineData(5u, 0u, 0f)]
        [InlineData(uint.MaxValue, 0u, 0f)]
        [InlineData(0u, 2147483648u, 0f)]
        [InlineData(0u, uint.MaxValue, 0f)]
        [InlineData(0u, 0u, -1f)]
        [InlineData(0u, 0u, float.MaxValue)]
        [InlineData(0u, 0u, float.NaN)]
        [InlineData(0u, 0u, float.PositiveInfinity)]
        public void InvalidMatchRulesAreRejectedBeforeWorldAssembly(uint phase, uint goal, float objectiveSeconds)
        {
            WorldRecord record = Match(phase, goal, objectiveSeconds);
            byte[] bytes = new byte[WorldRecord.Size];
            record.Write(bytes);
            Assert.False(WorldRecord.TryRead(bytes, out _));
            byte[] packet = new byte[WorldPacket.MaxSize];
            int length = WorldPacket.Write(packet, 7, 1, 0, new[] { record }, 0);
            Assert.False(WorldPacket.TryValidate(packet.AsSpan(0, length), 7));
            var world = new ClientWorldState();
            world.Reset(7);
            Assert.False(world.Receive(packet.AsSpan(0, length)));
            Assert.False(world.HasState);
            Assert.Equal(0, world.Count);
        }
    }
}
