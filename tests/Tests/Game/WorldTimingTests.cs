using System;
using MphRead.Entities;
using Xunit;

namespace MphRead.Tests
{
    public sealed class WorldTimingTests
    {
        [Theory]
        [InlineData(3, 6)]
        [InlineData(9, 18)]
        [InlineData(22, 44)]
        [InlineData(31, 62)]
        [InlineData(43, 86)]
        [InlineData(120, 240)]
        [InlineData(450, 900)]
        public void WorldCounterConversionsKeepGoldenDurations(int frames, int ticks)
            => Assert.Equal(ticks, SimTicks.From30HzFrames(frames));

        [Fact]
        public void LegacyFloatSecondsRetainMultiplyAndDivideBitsSeparately()
        {
            // Division and reciprocal multiplication can differ by one ULP. Production keeps both forms.
            for (int frames = 0; frames <= UInt16.MaxValue; frames++)
            {
                Assert.Equal(BitConverter.SingleToInt32Bits(frames / 30f),
                    BitConverter.SingleToInt32Bits(frames / (float)SimTicks.LegacyHz));
                Assert.Equal(BitConverter.SingleToInt32Bits(frames * (1 / 30f)),
                    BitConverter.SingleToInt32Bits(frames * SimTicks.LegacyFrameSeconds));
            }
            Assert.NotEqual(BitConverter.SingleToInt32Bits(7 / 30f), BitConverter.SingleToInt32Bits(7 * (1 / 30f)));
        }

        [Fact]
        public void PlatformIntervalRetainsItsExistingUnsignedCastAndOverflowSemantics()
        {
            foreach (uint frames in new uint[] { 0, 1, 65535, 1073741823, 1073741824, 2147483647, 2147483648, UInt32.MaxValue })
                Assert.Equal(unchecked((int)frames * 2), unchecked((int)frames * SimTicks.TicksPer30HzFrame));
        }

        [Fact]
        public void CameraReversalAndInterpolationKeepMetadataBoundaryBehavior()
        {
            foreach (PlayerValues values in Metadata.PlayerValues)
                for (int timer = 0; timer <= UInt16.MaxValue; timer++)
                {
                    Assert.Equal(unchecked((ushort)(values.CamSwitchTime * 2 - timer)),
                        unchecked((ushort)(SimTicks.From30HzFrames(values.CamSwitchTime) - timer)));
                    Assert.Equal(BitConverter.SingleToInt32Bits(timer / (values.CamSwitchTime * 2f)),
                        BitConverter.SingleToInt32Bits(timer / (float)SimTicks.From30HzFrames(values.CamSwitchTime)));
                }
            for (int tick = 0; tick <= 18; tick++)
                Assert.Equal(360 * tick / (9 * 2), 360 * tick / SimTicks.From30HzFrames(9));
        }

        [Fact]
        public void ShockCoilEscalationAndBombClampKeepExactBoundaryResults()
        {
            for (int tick = 0; tick <= UInt16.MaxValue; tick++)
            {
                Assert.Equal(tick >= 120 * 2 ? 4 : tick / (30 * 2),
                    tick >= SimTicks.From30HzFrames(120) ? 4 : tick / SimTicks.Hz);
                Assert.Equal(tick > 22 * 2 ? 22 * 2 : tick,
                    tick > SimTicks.From30HzFrames(22) ? SimTicks.From30HzFrames(22) : tick);
            }
        }
    }
}
