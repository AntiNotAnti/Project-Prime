using System;
using System.Buffers.Binary;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class WorldTests
    {
        private static WorldRecord[] Records(int count)
        {
            var records = new WorldRecord[count];
            records[0] = new WorldRecord(WorldRecordKind.Match, 255, 0, 0, new Vector3(600, 600, 0), 3,
                (uint)MatchPhase.Playing, 10, uint.MaxValue, 0);
            for (byte slot = 0; slot < 8; slot++)
            {
                records[1 + slot * 2] = new WorldRecord(WorldRecordKind.Score, slot, 0, 0, Vector3.Zero, slot, 0, 0, 0, 0);
                records[2 + slot * 2] = new WorldRecord(WorldRecordKind.Time, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
            }
            if (count >= 18)
            {
                // Version 7 keeps lifecycle facts separate from the legacy match
                // record.  The revision is deliberately nonzero: a complete
                // modern world must carry an identifiable phase generation.
                records[17] = new WorldRecord(WorldRecordKind.Lifecycle, 255, 0, 0,
                    Vector3.Zero, 0, 0, 1, 0, 0);
            }
            for (byte slot = 0; slot < 8; slot++)
            {
                int index = 18 + slot * 5;
                if (index < count) records[index] = new(WorldRecordKind.CombatStats, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                if (index + 1 < count) records[index + 1] = new(WorldRecordKind.ObjectiveStats, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                if (index + 2 < count) records[index + 2] = new(WorldRecordKind.WeaponStats0, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                if (index + 3 < count) records[index + 3] = new(WorldRecordKind.WeaponStats1, slot, 0, 0, Vector3.Zero, 0, 0, 0, 0, 0);
                if (index + 4 < count)
                {
                    records[index + 4] = new(WorldRecordKind.PlayerIdentity, slot, 0, 0, Vector3.Zero,
                        (uint)Hunter.Samus, slot < 2 ? slot : uint.MaxValue, slot < 2 ? 1u : 0u, 0, 0)
                    { PlayerName = $"P{slot}" };
                }
            }
            for (int i = WorldPacket.CanonicalRecordCount; i < count; i++)
            { records[i] = new WorldRecord(WorldRecordKind.Item, (byte)(i % 22), 0, (uint)i, new Vector3(i, 2, 3), uint.MaxValue, uint.MaxValue, 0, 0, 0); }
            return records;
        }
        private static byte[] Batch(WorldRecord[] records, int offset, uint revision = 1, uint match = 7)
        {
            var data = new byte[WorldPacket.MaxSize];
            int length = WorldPacket.Write(data, match, revision, 120, records, offset);
            return data[..length];
        }
        [Fact]
        public void MaximumWorldArrivesAtomicallyWithReorderingAndDuplicates()
        {
            var source = Records(WorldPacket.Capacity);
            var client = new ClientWorldState(); client.Reset(7);
            for (int offset = 240; offset > 0; offset -= 24)
            {
                byte[] batch = Batch(source, offset);
                Assert.True(batch.Length + NetHeader.Size <= NetConfig.MaxPacketSize);
                Assert.False(client.Receive(batch)); Assert.False(client.Receive(batch));
                Assert.False(client.HasState);
            }
            Assert.True(client.Receive(Batch(source, 0)));
            Assert.Equal(source, client.Records.ToArray());
            Assert.Equal(MatchPhase.Playing, client.Phase);
            Assert.Equal(1u, client.PhaseRevision);
            Assert.False(client.Receive(Batch(source, 0)));
        }
        [Fact]
        public void LossKeepsPreviousCompleteStateAndNewRevisionSupersedesPartialAcrossWrap()
        {
            var source = Records(64);
            var client = new ClientWorldState(); client.Reset(7);
            Assert.False(client.Receive(Batch(source, 0, uint.MaxValue)));
            Assert.False(client.Receive(Batch(source, 24, uint.MaxValue)));
            Assert.True(client.Receive(Batch(source, 48, uint.MaxValue)));
            Assert.False(client.Receive(Batch(source, 0, 0)));
            Assert.Equal(uint.MaxValue, client.Revision);
            Assert.False(client.Receive(Batch(source, 24, 1)));
            Assert.False(client.Receive(Batch(source, 24, 0)));
            Assert.False(client.Receive(Batch(source, 0, 1)));
            Assert.True(client.Receive(Batch(source, 48, 1)));
            Assert.Equal(1u, client.Revision);
            client.Reset(8);
            Assert.False(client.Receive(Batch(source, 0, 2)));
            Assert.False(client.HasState);
        }
        [Fact]
        public void MalformedBatchCannotContaminateAssembly()
        {
            WorldRecord[] records = Records(64);
            byte[] data = Batch(records, 0);
            for (int size = 0; size < data.Length; size++) { Assert.False(WorldPacket.TryValidate(data.AsSpan(0, size), 7)); }
            Assert.False(WorldPacket.TryValidate(data, 8));
            byte[] bad = (byte[])data.Clone(); bad[16] = 25;
            Assert.False(WorldPacket.TryValidate(bad, 7));
            bad = (byte[])data.Clone(); bad[17] = 1;
            Assert.False(WorldPacket.TryValidate(bad, 7));
            bad = (byte[])data.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(WorldPacket.HeaderSize + 8), 0x7FC00000);
            var client = new ClientWorldState(); client.Reset(7);
            Assert.False(client.Receive(bad)); Assert.False(client.Receive(Batch(records, 24)));
            Assert.False(client.Receive(data));
            Assert.True(client.Receive(Batch(records, 48)));
            Assert.Equal(records, client.Records.ToArray());
        }
        [Fact]
        public void DuplicateIdsAcrossBatchesAndMissingGlobalTablesAreRejected()
        {
            var records = Records(70); records[69] = records[58] with { Slot = 3 };
            var client = new ClientWorldState(); client.Reset(7);
            Assert.False(client.Receive(Batch(records, 0))); Assert.False(client.Receive(Batch(records, 24)));
            Assert.False(client.Receive(Batch(records, 48)));
            Assert.False(client.HasState);
            client.Reset(7);
            records = Records(17); records[4] = records[3];
            Assert.False(client.Receive(Batch(records, 0, 2)));
            Assert.False(client.HasState);
        }
        [Fact]
        public void CanonicalLivePrefixDecodesOnlyWhenComplete()
        {
            var records = Records(WorldPacket.CanonicalRecordCount);
            var client = new ClientWorldState(); client.Reset(7);
            Assert.True(client.ValidatePacket(Batch(records, 0)));
            Assert.False(client.Receive(Batch(records, 24)));
            Assert.False(client.Receive(Batch(records, 48)));
            Assert.True(client.Receive(Batch(records, 0)));
            Assert.Equal(records, client.Records.ToArray());

            records = Records(WorldPacket.CanonicalRecordCount - 1);
            client.Reset(7);
            Assert.False(client.Receive(Batch(records, 0, 2)));
            Assert.False(client.Receive(Batch(records, 24, 2)));
            Assert.False(client.Receive(Batch(records, 48, 2)));
            Assert.False(client.HasState);
        }
        [Fact]
        public void AuthoritativeGoalRepairsTheRoomSetupDefault()
        {
            const int admittedGoal = 31;
            WorldRecord[] records = Records(WorldPacket.CanonicalRecordCount);
            records[0] = records[0] with
            {
                Position = new Vector3(600, 0, 0),
                C = admittedGoal
            };
            var client = new ClientWorldState();
            client.Reset(7);
            Assert.False(client.Receive(Batch(records, 0)));
            Assert.False(client.Receive(Batch(records, 24)));
            Assert.True(client.Receive(Batch(records, 48)));
            Scene scene = Scene.CreateHeadless();
            try
            {
                MatchRules admitted = new(MatchMode.Battle, "TEST", scoreGoal: admittedGoal);
                scene.Match.ApplyRules(admitted.With(scoreGoal: 7));

                client.Apply(scene);

                Assert.Equal(admittedGoal, scene.Match.Rules.ScoreGoal);
                Assert.Equal(admittedGoal, scene.Match.Rules.LegacyPointGoal);
            }
            finally
            {
                scene.CloseHeadless();
            }
        }
        [Fact]
        public void RandomPayloadsAreBoundedAndNonthrowing()
        {
            var random = new Random(919);
            var client = new ClientWorldState(); client.Reset(7);
            var bytes = new byte[1100];
            for (int i = 0; i < 20000; i++)
            {
                random.NextBytes(bytes);
                Assert.False(client.Receive(bytes.AsSpan(0, random.Next(bytes.Length))));
            }
        }
    }
}
