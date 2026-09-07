using System;
using MphRead.Entities;
using Xunit;

namespace MphRead.Tests
{
    public sealed class PlayerTimingTests
    {
        [Fact]
        public void AltAttackStartupRetainsHalvingBeforeDoubling()
        {
            for (int frames = 0; frames <= UInt16.MaxValue; frames++)
                Assert.Equal(frames / 2 * 2, SimTicks.From30HzFrames(frames / 2));
            Assert.Equal(14, SimTicks.From30HzFrames(15 / 2));
        }

        [Fact]
        public void AllHunterDurationMetadataFitsCheckedConversionAndRetainsNarrowing()
        {
            foreach (PlayerValues value in Metadata.PlayerValues)
                foreach (int frames in new[] { value.AltAttackStartup, value.BoostChargeMax, value.BoostChargeMin,
                    value.AltAttackCooldown, value.AltAttackKnockbackTime, value.BombRefillTime,
                    value.BombCooldown, value.SwayStartTime })
                {
                    Assert.Equal(frames * 2, SimTicks.From30HzFrames(frames));
                    Assert.Equal(unchecked((ushort)(frames * 2)), unchecked((ushort)SimTicks.From30HzFrames(frames)));
                }
        }

        [Fact]
        public void BoostDamageKeepsTheOriginalIntegerDivision()
        {
            foreach (PlayerValues value in Metadata.PlayerValues)
            {
                if (value.BoostChargeMax == 0) { continue; }
                for (int charge = 0; charge <= UInt16.MaxValue; charge++)
                    Assert.Equal(unchecked((ushort)(value.AltAttackDamage * charge / (value.BoostChargeMax * 2))),
                        unchecked((ushort)(value.AltAttackDamage * charge / SimTicks.From30HzFrames(value.BoostChargeMax))));
            }
        }

        [Fact]
        public void SurvivalHidingClampKeepsItsExactBoundaryAndSubtractionOrder()
        {
            foreach (int legacyReveal in new[] { 300, 600 })
                for (int timer = 0; timer <= UInt16.MaxValue; timer++)
                {
                    int original = timer > 35 * 2 ? timer - 35 * 2 : 0;
                    if (original < legacyReveal * 2 && original > legacyReveal * 2 - 35 * 2)
                        original = legacyReveal * 2 - 35 * 2;
                    int converted = timer > SimTicks.From30HzFrames(35) ? timer - SimTicks.From30HzFrames(35) : 0;
                    int reveal = SimTicks.From30HzFrames(legacyReveal);
                    if (converted < reveal && converted > reveal - SimTicks.From30HzFrames(35))
                        converted = reveal - SimTicks.From30HzFrames(35);
                    Assert.Equal(original, converted);
                }
        }

        [Fact]
        public void AiDelayBoundsPreserveSeedProgressionAndChosenDelays()
        {
            foreach (uint initial in new uint[] { 0, 1, 0x3DE9179B, UInt32.MaxValue })
            {
                uint originalSeed = initial;
                uint convertedSeed = initial;
                for (int call = 0; call < 1024; call++)
                {
                    Assert.Equal(Rng.CallRng(ref originalSeed, 75 * 2) + 15 * 2,
                        Rng.CallRng(ref convertedSeed, (uint)SimTicks.From30HzFrames(75)) + (uint)SimTicks.From30HzFrames(15));
                    foreach (WeaponInfo weapon in Weapons.WeaponsMP)
                    {
                        Assert.Equal(Rng.CallRng(ref originalSeed, (uint)(weapon.FullCharge * 2)),
                            Rng.CallRng(ref convertedSeed, (uint)SimTicks.From30HzFrames(weapon.FullCharge)));
                    }
                    Assert.Equal(originalSeed, convertedSeed);
                }
            }
        }

        [Fact]
        public void JumpPadAndIdleUnsignedCastsRetainBoundaryBehavior()
        {
            for (int value = 0; value <= UInt16.MaxValue; value++)
                Assert.Equal(unchecked((ushort)(value * 2)), unchecked((ushort)SimTicks.From30HzFrames(value)));
            foreach (int value in new[] { Int32.MinValue, -1, 0, 1, Int32.MaxValue })
                Assert.Equal(unchecked((ulong)value * 2), unchecked((ulong)value * SimTicks.TicksPer30HzFrame));
            Assert.Equal(126, SimTicks.From30HzFrames(63));
        }
    }
}
