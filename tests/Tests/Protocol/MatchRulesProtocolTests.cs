using System;
using System.Buffers.Binary;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    [Collection("Match baseline globals")]
    public sealed class MatchRulesProtocolTests
    {
        private static MatchRules Rules(MatchMode mode = MatchMode.TeamDefender) => new(mode, "TEST ROOM", 6,
            TimeSpan.FromTicks(123456789), 31, TimeSpan.FromTicks(234567891), 4,
            friendlyFire: true, affinityWeapons: true, playerRadar: true, octolithReset: true, damageLevel: 2);

        [Fact]
        public void CompleteRulesRoundTripAcrossReliableBoundaries()
        {
            // Live protocol 11 adds deterministic Morph Ball boost input; older replay fixtures
            // below intentionally keep their historical protocol versions.
            Assert.Equal(11, NetHeader.Version);
            foreach (MatchMode mode in Enum.GetValues<MatchMode>())
            {
                MatchRules rules = Rules(mode);
                byte[] bytes = new byte[JoinAcceptedPacket.Size];
                var welcome = new JoinAcceptedPacket(123, 5, 42, uint.MaxValue, 60, rules);
                welcome.Write(bytes);
                Assert.True(JoinAcceptedPacket.TryRead(bytes, out var decoded));
                Assert.Equal(welcome, decoded);
                bytes = new byte[MatchTransitionPacket.Size];
                var transition = new MatchTransitionPacket(43, 8, rules);
                transition.Write(bytes);
                Assert.True(MatchTransitionPacket.TryRead(bytes, out var next));
                Assert.Equal(transition, next);
            }
        }

        [Fact]
        public void PacketWrappersWriteOnlyTheirPrefixInLargerScratchBuffers()
        {
            byte[] scratch = new byte[NetConfig.MaxPacketSize];
            Array.Fill(scratch, (byte)0xA5);
            var welcome = new JoinAcceptedPacket(123, 5, 42, 100, 60, Rules());
            welcome.Write(scratch);
            Assert.True(JoinAcceptedPacket.TryRead(scratch.AsSpan(0, JoinAcceptedPacket.Size), out var decoded));
            Assert.Equal(welcome, decoded);
            for (int i = JoinAcceptedPacket.Size; i < scratch.Length; i++) Assert.Equal(0xA5, scratch[i]);
            Array.Fill(scratch, (byte)0xA5);
            var transition = new MatchTransitionPacket(42, 100, Rules());
            transition.Write(scratch);
            Assert.True(MatchTransitionPacket.TryRead(scratch.AsSpan(0, MatchTransitionPacket.Size), out var next));
            Assert.Equal(transition, next);
            for (int i = MatchTransitionPacket.Size; i < scratch.Length; i++) Assert.Equal(0xA5, scratch[i]);
        }

        [Fact]
        public void AbsentAndZeroDurationsRemainDistinct()
        {
            foreach (TimeSpan? duration in new TimeSpan?[] { null, TimeSpan.Zero, TimeSpan.FromDays(1) })
            {
                var rules = new MatchRules(MatchMode.Survival, "TEST", timeLimit: duration, objectiveTimeGoal: duration);
                byte[] bytes = new byte[MatchRulesWire.Size];
                MatchRulesWire.Write(bytes, rules);
                Assert.True(MatchRulesWire.TryRead(bytes, out var decoded));
                Assert.Equal(rules, decoded);
            }
        }

        [Fact]
        public void AssignedExtensionFieldsRoundTripWithoutLosingPolicyMeaning()
        {
            MatchRules rules = new(MatchMode.Battle, "POLICY", maxPlayers: 2,
                timeLimit: TimeSpan.FromMinutes(7), scoreGoal: 7,
                spawnPolicy: SpawnPolicy.Duel, cancelSpawnProtectionOnOffensiveAction: true,
                assistMinimumDamage: 31, assistWindowTicks: 401,
                overtimePolicy: OvertimePolicy.ModeDefault,
                lateJoinPolicy: LateJoinPolicy.SpectateUntilNextMatch,
                pickupRespawnAnnouncements: true,
                rulesetPreset: RulesetPreset.Duel,
                rankingEligibility: RankingEligibility.VerifiedServerOnly,
                radarPolicy: RadarPolicy.Disabled,
                teamBalancePolicy: TeamBalancePolicy.Locked,
                killcamPolicy: KillcamPolicy.PostRound);
            byte[] bytes = new byte[MatchRulesWire.Size];

            MatchRulesWire.Write(bytes, rules);

            Assert.Equal((byte)RulesetPreset.Duel, bytes[71]);
            Assert.Equal((byte)RankingEligibility.VerifiedServerOnly, bytes[78]);
            Assert.Equal((byte)RadarPolicy.Disabled, bytes[79]);
            Assert.Equal((byte)TeamBalancePolicy.Locked, bytes[80]);
            Assert.Equal((byte)KillcamPolicy.PostRound, bytes[81]);
            Assert.True(MatchRulesWire.TryRead(bytes, out MatchRules decoded));
            Assert.Equal(rules, decoded);
        }

        [Theory]
        [InlineData(71, 4)] // RulesetPreset.Custom is the final assigned value.
        [InlineData(78, 2)] // RankingEligibility.VerifiedServerOnly is the final assigned value.
        [InlineData(79, 3)] // RadarPolicy.Enabled is the final assigned value.
        [InlineData(80, 2)] // TeamBalancePolicy.Locked is the final assigned value.
        [InlineData(81, 3)] // KillcamPolicy.PostRound is the final assigned value.
        [InlineData(82, 1)]
        [InlineData(83, 1)]
        public void InvalidAssignedOrReservedExtensionBytesAreRejected(int offset, byte value)
        {
            byte[] bytes = new byte[MatchRulesWire.Size];
            MatchRulesWire.Write(bytes, Rules());
            bytes[offset] = value;

            Assert.False(MatchRulesWire.TryRead(bytes, out _));
        }

        [Fact]
        public void LegacyZeroKillcamByteDecodesAsDisabled()
        {
            byte[] bytes = new byte[MatchRulesWire.Size];
            MatchRulesWire.Write(bytes, Rules());
            Assert.Equal((byte)KillcamPolicy.Immediate, bytes[81]);

            bytes[81] = 0;

            Assert.True(MatchRulesWire.TryRead(bytes, out MatchRules decoded));
            Assert.Equal(KillcamPolicy.Disabled, decoded.KillcamPolicy);
            Assert.Equal(0, bytes[82]);
            Assert.Equal(0, bytes[83]);
        }

        [Fact]
        public void KillcamPolicyDefaultsAndConstructorValidationAreExplicit()
        {
            Assert.Equal(KillcamPolicy.Immediate,
                MatchRules.CreateDefault(MatchMode.Battle, "DEFAULT").KillcamPolicy);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MatchRules(MatchMode.Battle, "INVALID", killcamPolicy: (KillcamPolicy)3));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(1, 9)]
        [InlineData(2, 16)]
        [InlineData(3, 3)]
        [InlineData(28, 1)]
        public void InvalidRuleScalarsAreRejected(int offset, byte value)
        {
            byte[] bytes = new byte[MatchRulesWire.Size];
            MatchRulesWire.Write(bytes, Rules());
            bytes[offset] = value;
            Assert.False(MatchRulesWire.TryRead(bytes, out _));
        }

        [Fact]
        public void NegativeGoalsAndUnsupportedDurationSentinelsAreRejected()
        {
            foreach (int offset in new[] { 4, 8, 12, 20 })
            {
                byte[] bytes = new byte[MatchRulesWire.Size];
                MatchRulesWire.Write(bytes, Rules());
                if (offset < 12) { BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), -1); }
                else { BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset), -2); }
                Assert.False(MatchRulesWire.TryRead(bytes, out _));
            }
            byte[] valid = new byte[MatchRulesWire.Size];
            MatchRulesWire.Write(valid, Rules());
            for (int size = 0; size < valid.Length; size++)
            { Assert.False(MatchRulesWire.TryRead(valid.AsSpan(0, size), out _)); }
        }

        [Fact]
        public void InputBundleCarriesPhaseRevisionAndRejectsOldHeader()
        {
            InputCommand input = new(1, 1, 8, InputButtons.Jump, InputButtons.Jump, Vector3.UnitZ, InputCommand.NoWeapon);
            byte[] bytes = new byte[InputBundle.MaxSize];
            int length = InputBundle.Write(bytes, 42, new[] { input }, uint.MaxValue);
            Span<InputCommand> decoded = stackalloc InputCommand[InputBundle.Capacity];
            Assert.True(InputBundle.TryRead(bytes.AsSpan(0, length), decoded, out uint match, out uint phase, out int count));
            Assert.Equal(42u, match); Assert.Equal(uint.MaxValue, phase); Assert.Equal(1, count); Assert.Equal(input, decoded[0]);
            byte[] old = new byte[5 + InputCommand.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(old, 42); old[4] = 1; input.Write(old.AsSpan(5));
            Assert.False(InputBundle.TryRead(old, decoded, out _, out _, out _));
        }

        [Theory]
        [InlineData(MatchPhase.WaitingForPlayers)]
        [InlineData(MatchPhase.Countdown)]
        [InlineData(MatchPhase.Playing)]
        [InlineData(MatchPhase.Ending)]
        [InlineData(MatchPhase.Intermission)]
        public void ExplicitPhasesAndWraparoundDeadlinesRoundTrip(MatchPhase phase)
        {
            WorldRecord record = new(WorldRecordKind.Match, 255, 0, 0, new Vector3(-1, 0, 0), 3, (uint)phase, 0, uint.MaxValue, 0);
            byte[] bytes = new byte[WorldRecord.Size]; record.Write(bytes);
            Assert.True(WorldRecord.TryRead(bytes, out var decoded)); Assert.Equal(record, decoded);
            record = new(WorldRecordKind.Lifecycle, 255, 1, 0, Vector3.Zero, uint.MaxValue - 10, 169, 2, 0, 0);
            record.Write(bytes);
            Assert.True(WorldRecord.TryRead(bytes, out decoded)); Assert.Equal(record, decoded);
        }
        [Theory]
        [InlineData((byte)5)]
        [InlineData((byte)6)]
        public void OldAuthoritativeReplaysUseTheirOwnMatchAndWorldLayouts(byte protocol)
        {
            var replay = new ModernReplayState(); replay.Reset(protocol);
            byte[] match = new byte[1 + 9 + MatchStatePacket.MaxNameBytes];
            match[0] = (byte)ReplayRecordKind.Match;
            BinaryPrimitives.WriteUInt32LittleEndian(match.AsSpan(1), 7);
            match[9] = (byte)GameMode.Battle;
            System.Text.Encoding.ASCII.GetBytes("TEST").CopyTo(match, 10);
            Assert.True(replay.Receive(match));
            Assert.Null(replay.InitialRules);
            Assert.False(MatchTransitionPacket.TryRead(match.AsSpan(1), out _));
            var records = new WorldRecord[17];
            records[0] = new(WorldRecordKind.Match, 255, 0, 0, new Vector3(-1, 0, 0), 3, 2, 0, uint.MaxValue, 0);
            for (byte slot = 0; slot < 8; slot++)
            {
                records[1 + slot * 2] = new(WorldRecordKind.Score, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                records[2 + slot * 2] = new(WorldRecordKind.Time, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
            }
            byte[] world = new byte[1 + WorldPacket.MaxSize]; world[0] = (byte)ReplayRecordKind.World;
            int length = WorldPacket.Write(world.AsSpan(1), 7, 1, 0, records, 0);
            Assert.True(replay.Receive(world.AsSpan(0, length + 1)));
            Assert.True(replay.World.HasState);
            Assert.Equal(MatchPhase.Intermission, replay.World.Phase);
            var live = new ClientWorldState(); live.Reset(7);
            Assert.False(live.Receive(world.AsSpan(1, length)));
            Assert.False(live.HasState);
        }

        [Fact]
        public void OversizedMatchClockAndAmbiguousDeadlineAreRejected()
        {
            byte[] rules = new byte[MatchRulesWire.Size];
            MatchRulesWire.Write(rules, Rules());
            BinaryPrimitives.WriteInt64LittleEndian(rules.AsSpan(12), TimeSpan.MaxValue.Ticks);
            Assert.False(MatchRulesWire.TryRead(rules, out _));
            byte[] bytes = new byte[WorldRecord.Size];
            foreach (WorldRecord invalid in new[]
            {
                new WorldRecord(WorldRecordKind.Lifecycle, 255, 1, 0, Vector3.Zero, 0, 0x80000000, 1, 0, 0),
                new WorldRecord(WorldRecordKind.Lifecycle, 255, 1, 0, Vector3.Zero, 10, 9, 1, 0, 0),
                new WorldRecord(WorldRecordKind.Lifecycle, 255, 2, 0, Vector3.Zero, 0, 180, 1, 0, 0),
                new WorldRecord(WorldRecordKind.Lifecycle, 255, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0)
            })
            {
                invalid.Write(bytes);
                Assert.False(WorldRecord.TryRead(bytes, out _));
            }
        }

        [Fact]
        public void EffectiveRadarIsReplicatedWithoutChangingConfiguredRules()
        {
            Scene scene = Scene.CreateHeadless();
            try
            {
                MatchRules rules = new(MatchMode.Survival, "TEST", playerRadar: false);
                scene.Match.ApplyRules(rules);
                var records = new WorldRecord[WorldPacket.CanonicalRecordCount];
                records[0] = new(WorldRecordKind.Match, 255, 2, 0, new Vector3(-1, 0, 0),
                    (uint)GameMode.Survival, (uint)MatchPhase.Playing, 0, uint.MaxValue, 0);
                for (byte slot = 0; slot < 8; slot++)
                {
                    records[1 + slot * 2] = new(WorldRecordKind.Score, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                    records[2 + slot * 2] = new(WorldRecordKind.Time, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                }
                records[17] = new(WorldRecordKind.Lifecycle, 255, 0, 0, Vector3.Zero, 0, 0, 1, 0, 0);
                for (byte slot = 0; slot < 8; slot++)
                {
                    int index = 18 + slot * 5;
                    records[index] = new(WorldRecordKind.CombatStats, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                    records[index + 1] = new(WorldRecordKind.ObjectiveStats, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                    records[index + 2] = new(WorldRecordKind.WeaponStats0, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                    records[index + 3] = new(WorldRecordKind.WeaponStats1, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                    records[index + 4] = new(WorldRecordKind.PlayerIdentity, slot, 0, 0, Vector3.Zero,
                        (uint)Hunter.Samus, slot < 2 ? slot : uint.MaxValue, slot < 2 ? 1u : 0u, 0, 0)
                    { PlayerName = $"P{slot}" };
                }
                var world = new ClientWorldState(); world.Reset(7);
                byte[] packet = new byte[WorldPacket.MaxSize];
                int lastLength = 0;
                bool ReceiveBatch(int offset, uint revision, uint tick)
                {
                    lastLength = WorldPacket.Write(packet, 7, revision, tick, records, offset);
                    Assert.True(WorldPacket.TryValidate(packet.AsSpan(0, lastLength), 7));
                    return world.Receive(packet.AsSpan(0, lastLength));
                }
                Assert.False(ReceiveBatch(0, 1, 0));
                Assert.False(ReceiveBatch(24, 1, 0));
                Assert.True(ReceiveBatch(48, 1, 0));
                world.Apply(scene);
                Assert.True(scene.Match.RadarPlayers);
                Assert.Same(rules, scene.Match.Rules);
                Assert.False(rules.PlayerRadar);
                Assert.False(WorldPacket.TryValidate(packet.AsSpan(0, lastLength), 7, legacy: true));
                records[0] = records[0] with { Flags = 0 };
                Assert.False(ReceiveBatch(0, 2, 1));
                Assert.False(ReceiveBatch(24, 2, 1));
                Assert.True(ReceiveBatch(48, 2, 1));
                world.Apply(scene);
                Assert.False(scene.Match.RadarPlayers);
                Assert.Same(rules, scene.Match.Rules);
            }
            finally { scene.CloseHeadless(); }
        }

    }
}
