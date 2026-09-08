using System;
using System.Buffers.Binary;
using MphRead.Combat;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class Protocol8AfflictionTests
    {
        private static SnapshotPlayer Player(byte slot = 0) => new()
        {
            Slot = slot, Hunter = Hunter.Samus, Life = 1, ConnectionId = (ulong)slot + 100,
            Aim = Vector3.UnitZ, Facing = Vector3.UnitZ, Health = 99,
            Flags = SnapshotPlayerFlags.Burning | SnapshotPlayerFlags.Disrupted | SnapshotPlayerFlags.RadarReveal | SnapshotPlayerFlags.RadarRevealPrevious,
            BurnTicks = 300, DisruptTicks = 180, Assists = 7
        };

        [Fact]
        public void SnapshotAppendOffsetsAndMaximumDatagramAreStable()
        {
            byte[] bytes = new byte[SnapshotPlayer.Size];
            Player().Write(bytes);
            Assert.Equal(96, bytes.Length);
            Assert.Equal(300, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(88)));
            Assert.Equal(180, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(90)));
            Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(92)));
            Assert.True(SnapshotPlayer.TryRead(bytes, out SnapshotPlayer parsed));
            Assert.Equal(300, parsed.BurnTicks);
            Assert.Equal(180, parsed.DisruptTicks);
            Assert.True((parsed.Flags & SnapshotPlayerFlags.RadarReveal) != 0);
            Assert.True((parsed.Flags & SnapshotPlayerFlags.RadarRevealPrevious) != 0);
            Assert.Equal(818, NetHeader.Size + SnapshotPacket.MaxSize);
            Assert.Equal(9, NetHeader.Version);
            Assert.False(NetWireIdentity.IsCompatible(NetWireIdentity.Family, 7));
            Assert.False(NetWireIdentity.IsCompatible(NetWireIdentity.Family, 8));
            Assert.True(NetWireIdentity.IsCompatible(NetWireIdentity.Family, 9));
        }

        [Theory]
        [InlineData(88, 0)]
        [InlineData(90, 0)]
        [InlineData(92, -1)]
        [InlineData(12, 0)]
        public void InvalidDurableStatusOrIdentityIsRejected(int offset, int value)
        {
            byte[] bytes = new byte[SnapshotPlayer.Size];
            Player().Write(bytes);
            if (offset < 92 && offset >= 88) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), (ushort)value);
            else BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
            Assert.False(SnapshotPlayer.TryRead(bytes, out _));
        }

        [Fact]
        public void MalformedLatePlayerDoesNotPartiallyApplyBatch()
        {
            var packet = new SnapshotPacket(1, 2, 3, 0, false, 0, 0);
            byte[] bytes = new byte[SnapshotPacket.HeaderSize + 2 * SnapshotPlayer.Size];
            packet.Write(bytes, new[] { Player(), Player(1) });
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(SnapshotPacket.HeaderSize + SnapshotPlayer.Size + 92), -1);
            SnapshotPlayer[] output = { new() { Health = 41 }, new() { Health = 42 } };
            Assert.False(SnapshotPacket.TryRead(bytes, output, out _, out int count));
            Assert.Equal(0, count);
            Assert.Equal(41, output[0].Health);
            Assert.Equal(42, output[1].Health);
        }

        [Fact]
        public void FrozenVersion7SnapshotRemainsDemoOnly()
        {
            byte[] old = new byte[26 + 88];
            BinaryPrimitives.WriteUInt32LittleEndian(old, 100);
            BinaryPrimitives.WriteUInt32LittleEndian(old.AsSpan(8), 1);
            old[17] = 1;
            Span<byte> player = old.AsSpan(26);
            BinaryPrimitives.WriteUInt16LittleEndian(player[4..], (ushort)SnapshotPlayerFlags.Burning);
            BinaryPrimitives.WriteUInt32LittleEndian(player[12..], 1);
            BinaryPrimitives.WriteUInt64LittleEndian(player[16..], 100);
            BinaryPrimitives.WriteSingleLittleEndian(player[56..], 1); // aim.z
            BinaryPrimitives.WriteSingleLittleEndian(player[68..], 1); // facing.z
            var output = new SnapshotPlayer[8];
            Assert.False(SnapshotPacket.TryRead(old, output, out _, out _));
            Assert.True(Protocol7DemoCodec.TryReadSnapshot(old, output, out SnapshotPacket packet, out int count));
            Assert.Equal(100u, packet.ServerTick);
            Assert.Equal(1, count);
            Assert.Equal(SnapshotPlayerFlags.Burning, output[0].Flags);
            Assert.Equal(0, output[0].BurnTicks); // unknown historical duration, never invented
            Assert.Equal(0, output[0].Assists);
        }

        [Fact]
        public void RulesExtensionRoundTripsAndVersion7DefaultsStayFrozen()
        {
            MatchRules rules = MatchRules.CreateDefault(MatchMode.Battle, "MP6 HEADSHOT")
                .With(spawnPolicy: SpawnPolicy.Duel, cancelSpawnProtectionOnOffensiveAction: true);
            byte[] bytes = new byte[MatchRulesWire.Size];
            Array.Fill(bytes, (byte)255);
            MatchRulesWire.Write(bytes, rules);
            Assert.Equal(84, bytes.Length);
            Assert.Equal(2, bytes[68]);
            Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(72)));
            Assert.True(MatchRulesWire.TryRead(bytes, out MatchRules parsed));
            Assert.Equal(SpawnPolicy.Duel, parsed.SpawnPolicy);
            Assert.True(parsed.CancelSpawnProtectionOnOffensiveAction);
            Assert.True(Protocol7DemoRules.TryRead(bytes.AsSpan(0, 68), out MatchRules legacy));
            Assert.Equal(SpawnPolicy.Classic, legacy.SpawnPolicy);
            Assert.False(legacy.CancelSpawnProtectionOnOffensiveAction);
            Assert.Equal(20, legacy.AssistMinimumDamage);
            Assert.Equal(300, legacy.AssistWindowTicks);
            Assert.Equal(OvertimePolicy.Disabled, legacy.OvertimePolicy);
            Assert.Equal(LateJoinPolicy.JoinImmediately, legacy.LateJoinPolicy);
            Assert.False(legacy.PickupRespawnAnnouncements);
            Assert.Equal(RulesetPreset.Classic, legacy.RulesetPreset);
            Assert.Equal(RankingEligibility.Unranked, legacy.RankingEligibility);
            Assert.Equal(RadarPolicy.Classic, legacy.RadarPolicy);
            Assert.Equal(TeamBalancePolicy.BeforeStart, legacy.TeamBalancePolicy);
            Assert.False(MatchRulesWire.TryRead(bytes.AsSpan(0, 68), out _));
            foreach ((int offset, byte value) in new[]
            {
                (71, (byte)4), // RulesetPreset.Custom is the final assigned value.
                (73, (byte)1), // High byte makes the cancel/announcement flags exceed their mask.
                (78, (byte)2), // RankingEligibility.VerifiedServerOnly is the final assigned value.
                (79, (byte)3), // RadarPolicy.Enabled is the final assigned value.
                (80, (byte)2), // TeamBalancePolicy.Locked is the final assigned value.
                (81, (byte)1), (82, (byte)1), (83, (byte)1)
            })
            {
                bytes[offset] = value;
                Assert.False(MatchRulesWire.TryRead(bytes, out _));
                bytes[offset] = 0;
            }
            bytes[68] = 3;
            Assert.False(MatchRulesWire.TryRead(bytes, out _));
            bytes[68] = 0;
            bytes[72] = 4;
            Assert.False(MatchRulesWire.TryRead(bytes, out _));
        }

        private static CombatEvent Affliction(uint id, uint tick, CombatActor target, ushort burn = 300)
            => new(id, tick, 0, CombatEventKind.Affliction, 0, 0, CombatActor.None, target,
                99, 0, Vector3.Zero, Vector3.Zero, 0, burn, 180);

        [Fact]
        public void MissingEventSnapshotRestoresStatusAndExpiresAtServerTick()
        {
            var state = new NetworkAfflictionState();
            Assert.True(state.Reconcile(new(0, 100, 1), 1000, new(0, 300, 180)));
            Assert.Equal(new AfflictionTimers(0, 1, 0), state.At(1299));
            Assert.Equal(default, state.At(1300));
        }

        [Fact]
        public void DuplicateAndReorderedEventsCannotRestartOrResurrectStatus()
        {
            var state = new NetworkAfflictionState();
            CombatActor actor = new(0, 100, 1);
            state.Reconcile(actor, 100, default);
            CombatEvent applied = Affliction(10, 101, actor);
            Assert.True(state.Apply(applied));
            Assert.False(state.Apply(applied));
            Assert.False(state.Reconcile(actor, 100, default));
            Assert.Equal(299, state.At(102).Burn);
            Assert.True(state.Reconcile(actor, 105, default));
            Assert.False(state.Apply(Affliction(11, 104, actor)));
            Assert.False(state.Apply(Affliction(12, 105, actor)));
            Assert.Equal(default, state.At(105));
        }

        [Fact]
        public void ReplacementIdentityAndTickWrapDoNotLeakAffliction()
        {
            var state = new NetworkAfflictionState();
            CombatActor original = new(0, 100, 1), replacement = new(0, 200, 1);
            state.Reconcile(original, UInt32.MaxValue - 2, new(0, 5, 0));
            Assert.Equal(2, state.At(0).Burn);
            state.Reconcile(replacement, 1, default);
            Assert.False(state.Apply(Affliction(20, 2, original)));
            Assert.Equal(default, state.At(2));
        }
    }
}
