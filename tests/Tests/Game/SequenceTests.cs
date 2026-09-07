using System;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class SequenceTests
    {
        [Theory]
        [InlineData(1u, 0u, true)]
        [InlineData(0u, UInt32.MaxValue, true)]
        [InlineData(UInt32.MaxValue, 0u, false)]
        [InlineData(42u, 42u, false)]
        [InlineData(0x80000000u, 0u, false)]
        [InlineData(0u, 0x80000000u, false)]
        public void OrderingUsesSerialArithmetic(uint sequence, uint previous, bool newer)
        {
            Assert.Equal(newer, Sequence32.IsNewer(sequence, previous));
        }

        [Fact]
        public void ReceiveWindowTracksReorderingAndDuplicatesAcrossWrap()
        {
            var window = new ReceiveWindow();
            Assert.False(window.HasReceived);
            Assert.Equal(ReceiveResult.Newest, window.Record(UInt32.MaxValue - 1));
            Assert.Equal(ReceiveResult.Newest, window.Record(1));
            Assert.Equal(4u, window.AckBits);
            Assert.Equal(ReceiveResult.OutOfOrder, window.Record(0));
            Assert.Equal(ReceiveResult.OutOfOrder, window.Record(UInt32.MaxValue));
            Assert.Equal(7u, window.AckBits);
            Assert.Equal(ReceiveResult.Duplicate, window.Record(UInt32.MaxValue));
            Assert.Equal(ReceiveResult.Duplicate, window.Record(1));
            Assert.Equal(1u, window.Ack);
        }

        [Theory]
        [InlineData(31u, 0x40000000u)]
        [InlineData(32u, 0x80000000u)]
        [InlineData(33u, 0u)]
        [InlineData(1024u, 0u)]
        public void LargeJumpsDoNotUseMaskedShiftCounts(uint jump, uint expected)
        {
            var window = new ReceiveWindow();
            window.Record(20);
            window.Record(20 + jump);
            Assert.Equal(expected, window.AckBits);
            Assert.Equal(ReceiveResult.TooOld, window.Record(20 + jump - 33));
        }

        [Fact]
        public void AckBitsDescribeExactlyThePrevious32Datagrams()
        {
            for (int bit = 0; bit < 32; bit++)
            {
                uint sequence = unchecked(10u - (uint)bit - 1);
                Assert.True(ReceiveWindow.IsAcknowledged(sequence, 10, 1u << bit));
                Assert.False(ReceiveWindow.IsAcknowledged(sequence, 10, 0));
            }
            Assert.True(ReceiveWindow.IsAcknowledged(10, 10, 0));
            Assert.False(ReceiveWindow.IsAcknowledged(11, 10, UInt32.MaxValue));
            Assert.False(ReceiveWindow.IsAcknowledged(unchecked(10u - 33), 10, UInt32.MaxValue));
        }

        [Fact]
        public void WindowMatchesAReferenceSetOverRandomLossAndReordering()
        {
            var window = new ReceiveWindow();
            var received = new System.Collections.Generic.HashSet<uint>();
            var random = new Random(0x51A);
            uint newest = UInt32.MaxValue - 1000;
            for (int i = 0; i < 10000; i++)
            {
                uint sequence = random.Next(3) == 0
                    ? unchecked(newest - (uint)random.Next(40))
                    : unchecked(newest + (uint)random.Next(1, 6));
                ReceiveResult result = window.Record(sequence);
                if (result == ReceiveResult.Newest)
                {
                    newest = sequence;
                }
                if (result != ReceiveResult.TooOld)
                {
                    received.Add(sequence);
                }
                for (uint distance = 1; distance <= 32; distance++)
                {
                    uint previous = unchecked(window.Ack - distance);
                    Assert.Equal(received.Contains(previous),
                        ReceiveWindow.IsAcknowledged(previous, window.Ack, window.AckBits));
                }
            }
        }
    }
}
