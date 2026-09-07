using System;
using System.Buffers.Binary;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class CombatTests
    {
        private static CombatEvent Hit(uint id = 1) => new(id, 123, UInt32.MaxValue,
            CombatEventKind.Damage, (byte)BeamType.Imperialist, CombatEventFlags.Headshot,
            new CombatActor(0, 100, 3), new CombatActor(7, 200, 8), 50, 99,
            new Vector3(1, 2, 3), new Vector3(0.1f, 0.2f, -0.3f), 120, 300, 60, 159);

        [Fact]
        public void GoldenEventRoundTripsIdentityCommandAndOutcomeWithoutNativeContent()
        {
            byte[] bytes = new byte[CombatEvent.Size];
            CombatEvent value = Hit(); value.Write(bytes);
            Assert.Equal(82, bytes.Length);
            Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
            Assert.Equal(UInt32.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
            Assert.Equal(100ul, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(17)));
            Assert.Equal(200ul, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(30)));
            Assert.Equal(50, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(42)));
            Assert.True(CombatEvent.TryRead(bytes, out CombatEvent decoded));
            Assert.Equal(value, decoded);
            Assert.True(CombatEventBatch.MaxSize <= 508);
        }

        [Fact]
        public void ShotSeedRoundTripsAndOldOrMisplacedSeedPayloadsAreRejected()
        {
            var value = Hit() with { Kind = CombatEventKind.Shot, SpreadSeed = 0xF123ABCD };
            byte[] bytes = new byte[CombatEvent.Size];
            value.Write(bytes);
            Assert.Equal(0xF123ABCDu, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(78)));
            Assert.True(CombatEvent.TryRead(bytes, out CombatEvent decoded));
            Assert.Equal(value, decoded);
            Assert.False(CombatEvent.TryRead(bytes.AsSpan(0, 78), out _));
            (value with { Kind = CombatEventKind.Damage }).Write(bytes);
            Assert.False(CombatEvent.TryRead(bytes, out _));
        }

        [Fact]
        public void ShotLocalSpreadIgnoresOtherRandomWorkBetweenPellets()
        {
            var clean = new BeamSpread(0x87654321);
            var recycled = new BeamSpread(0x87654321);
            uint unrelated = 17;
            for (int pellet = 0; pellet < 32; pellet++)
            {
                Vector3 expected = clean.Next(Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX, 12 * 4096, 0.85f);
                for (int work = 0; work < pellet * 13; work++) Rng.CallRng(ref unrelated, 1000);
                Vector3 actual = recycled.Next(Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX, 12 * 4096, 0.85f);
                Assert.Equal(expected, actual);
                Assert.InRange(actual.Length, 0.849999f, 0.850001f);
            }
        }

        [Fact]
        public void CodecRejectsPartialUnknownNonfiniteAndCrossIdentityValues()
        {
            byte[] bytes = new byte[CombatEvent.Size];
            Hit().Write(bytes);
            for (int length = 0; length < bytes.Length; length++)
                Assert.False(CombatEvent.TryRead(bytes.AsSpan(0, length), out _));
            Assert.False(CombatEvent.TryRead(new byte[CombatEvent.Size + 1], out _));
            foreach (CombatEvent value in new[]
            {
                Hit() with { Kind = (CombatEventKind)7 }, Hit() with { Flags = (CombatEventFlags)64 },
                Hit() with { Weapon = 11 }, Hit() with { Actor = new CombatActor(8, 100, 1) },
                Hit() with { Target = new CombatActor(0, 0, 1) }, Hit() with { Target = CombatActor.None },
                Hit() with { Target = new CombatActor(0, 200, 0) },
                Hit() with { Position = new Vector3(Single.NaN, 1, 2) },
                Hit() with { Direction = new Vector3(0, Single.PositiveInfinity, 0) }
            })
            {
                value.Write(bytes);
                Assert.False(CombatEvent.TryRead(bytes, out _));
            }
            (Hit() with { Actor = CombatActor.None, Weapon = 255 }).Write(bytes);
            Assert.True(CombatEvent.TryRead(bytes, out _)); // environmental damage
        }

        [Fact]
        public void BatchValidatesWholePacketBeforeChangingPresentationOutput()
        {
            var events = new CombatEvent[CombatEventBatch.MaxCount];
            for (uint i = 0; i < events.Length; i++) events[i] = Hit(i);
            byte[] bytes = new byte[CombatEventBatch.MaxSize];
            Assert.Equal(bytes.Length, CombatEventBatch.Write(bytes, events));
            var output = new CombatEvent[events.Length];
            Assert.True(CombatEventBatch.TryRead(bytes, output, out int count));
            Assert.Equal(events.Length, count); Assert.Equal(events, output);
            Array.Fill(output, Hit(999));
            bytes[1 + CombatEvent.Size * 5 + 12] = 255;
            Assert.False(CombatEventBatch.TryRead(bytes, output, out count));
            Assert.Equal(0, count);
            Assert.All(output, e => Assert.Equal(999u, e.Id));
        }

        [Fact]
        public void JournalIsBoundedOrderedAndRetainsEventsUntilSuccessfulDelivery()
        {
            var collector = new ServerCombat();
            using (collector.Enter(44))
            {
                for (int i = 0; i < ServerCombat.Capacity; i++) Assert.True(collector.TryRecord(Hit()));
                Assert.False(collector.TryRecord(Hit()));
                Assert.Equal(1, collector.Dropped);
                Span<CombatEvent> batch = stackalloc CombatEvent[6];
                Assert.Equal(6, collector.CopyPending(batch));
                Assert.Equal(0u, batch[0].Id); Assert.Equal(5u, batch[5].Id);
                Assert.Equal(44u, batch[0].Tick);
                Assert.Equal(ServerCombat.Capacity, collector.Count); // copying is not acknowledging
                collector.Consume(6);
                for (int i = 0; i < 6; i++) Assert.True(collector.TryRecord(Hit()));
                int expected = 6;
                while (collector.Count > 0)
                {
                    int count = collector.CopyPending(batch);
                    for (int i = 0; i < count; i++) Assert.Equal((uint)expected++, batch[i].Id);
                    collector.Consume(count);
                }
                Assert.Equal(ServerCombat.Capacity + 6, expected);
                Assert.Throws<ArgumentOutOfRangeException>(() => collector.Consume(1));
            }
            Assert.Null(ServerCombat.Current);
        }

        [Fact]
        public void NestedSimulationScopeRestoresOwnerAfterFailure()
        {
            var first = new ServerCombat(); var second = new ServerCombat();
            using (first.Enter(10))
            {
                Assert.Same(first, ServerCombat.Current);
                Assert.Throws<InvalidOperationException>((Action)(() =>
                {
                    using var scope = second.Enter(20);
                    Assert.Same(second, ServerCombat.Current);
                    throw new InvalidOperationException();
                }));
                Assert.Same(first, ServerCombat.Current);
            }
            Assert.Null(ServerCombat.Current);
        }
    }
}
