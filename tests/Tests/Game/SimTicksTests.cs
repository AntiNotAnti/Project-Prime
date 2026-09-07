using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Entities;
using Xunit;

namespace MphRead.Tests
{
    public sealed class SimTicksTests
    {
        [Theory]
        [InlineData(1, 2)]
        [InlineData(2, 4)]
        [InlineData(5, 10)]
        [InlineData(8, 16)]
        [InlineData(15, 30)]
        [InlineData(60, 120)]
        [InlineData(75, 150)]
        [InlineData(90, 180)]
        [InlineData(150, 300)]
        [InlineData(210, 420)]
        [InlineData(300, 600)]
        [InlineData(600, 1200)]
        [InlineData(900, 1800)]
        public void MigratedDurationsKeepTheirGoldenTickCounts(int frames, int ticks)
            => Assert.Equal(ticks, SimTicks.From30HzFrames(frames));

        [Fact]
        public void CompileTimeConstantsRetainRespawnAndSpawnCooldownValues()
        {
            const ushort respawn = 3 * SimTicks.Hz;
            const ushort cooldown = 2 * SimTicks.TicksPer30HzFrame;
            Assert.Equal((ushort)180, respawn);
            Assert.Equal(PlayerEntity.RespawnTime, respawn);
            Assert.Equal((ushort)4, cooldown);
            Assert.Equal(60, SimTicks.Hz);
            Assert.Equal(30, SimTicks.LegacyHz);
        }

        [Fact]
        public void SignedIntegerConversionsCheckOverflowWithoutChangingValidValues()
        {
            Assert.Equal(-120, SimTicks.FromSeconds(-2));
            Assert.Equal(0, SimTicks.From30HzFrames(0));
            Assert.Equal(-2, SimTicks.From30HzFrames(-1));
            Assert.Equal(Int32.MaxValue - 1, SimTicks.From30HzFrames(Int32.MaxValue / 2));
            Assert.Equal(Int32.MinValue, SimTicks.From30HzFrames(Int32.MinValue / 2));
            Assert.Throws<OverflowException>(() => SimTicks.From30HzFrames(Int32.MaxValue / 2 + 1));
            Assert.Throws<OverflowException>(() => SimTicks.From30HzFrames(Int32.MinValue / 2 - 1));
            Assert.Equal(2147483640, SimTicks.FromSeconds(35791394));
            Assert.Throws<OverflowException>(() => SimTicks.FromSeconds(35791395));
            Assert.Throws<OverflowException>(() => SimTicks.FromSeconds(-35791395));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(16, 0)]
        [InlineData(17, 1)]
        [InlineData(999, 59)]
        [InlineData(1000, 60)]
        [InlineData(-16, 0)]
        [InlineData(-17, -1)]
        [InlineData(Int32.MaxValue, 128849018)]
        [InlineData(Int32.MinValue, -128849018)]
        public void MillisecondsTruncateFractionalTicksTowardZeroWithoutIntermediateOverflow(int ms, int ticks)
            => Assert.Equal(ticks, SimTicks.FromMilliseconds(ms));

        [Fact]
        public void ExistingMetadataCastsAreEquivalentAcrossTheEntireUshortDomain()
        {
            for (int frames = 0; frames <= UInt16.MaxValue; frames++)
            {
                Assert.Equal(frames * 2, SimTicks.From30HzFrames(frames));
                Assert.Equal(unchecked((ushort)(frames * 2)), unchecked((ushort)SimTicks.From30HzFrames(frames)));
            }
            foreach (PlayerValues values in Metadata.PlayerValues)
            {
                Assert.Equal((ushort)(values.SpawnInvulnerability * 2), (ushort)SimTicks.From30HzFrames(values.SpawnInvulnerability));
                Assert.Equal((ushort)(values.DamageInvuln * 2), (ushort)SimTicks.From30HzFrames(values.DamageInvuln));
                Assert.Equal((ushort)60, (ushort)SimTicks.From30HzFrames(values.SpawnInvulnerability));
            }
        }

        [Fact]
        public void RespawnDisplayRetainsLegacyDivisionAtEveryRelevantBoundary()
        {
            // This intentionally preserves the extra second at exact multiples; it is not Ceiling(time/60).
            foreach (int time in new[] { -61, -60, -59, -1, 0, 1, 59, 60, 61, 299, 300, 600, 1800 })
                Assert.Equal((time + 30 * 2) / (30 * 2), (time + SimTicks.Hz) / SimTicks.Hz);
        }

        [Fact]
        public void BurnPulseAndFreezeThresholdKeepTickBoundarySemantics()
        {
            for (int tick = 0; tick <= 300; tick++)
            {
                Assert.Equal(tick % (8 * 2) == 0, tick % SimTicks.From30HzFrames(8) == 0);
                Assert.Equal(tick > 60 * 2, tick > SimTicks.From30HzFrames(60));
                Assert.Equal(tick < 15 * 2, tick < SimTicks.From30HzFrames(15));
            }
            Assert.Equal((ushort)160, (ushort)(SimTicks.From30HzFrames(75) + SimTicks.From30HzFrames(5)));
        }

        [Fact]
        public void WeaponChargeFractionsRetainTheirExactFloatingPointBits()
        {
            foreach (WeaponInfo weapon in AllWeapons())
            {
                Assert.Equal(weapon.ShotCooldown * 2, SimTicks.From30HzFrames(weapon.ShotCooldown));
                Assert.Equal(weapon.AutofireCooldown * 2, SimTicks.From30HzFrames(weapon.AutofireCooldown));
                var charges = new HashSet<int> { 0, 1, UInt16.MaxValue };
                foreach (int boundary in new[] { weapon.MinCharge * 2, weapon.FullCharge * 2 })
                    for (int delta = -1; delta <= 1; delta++) charges.Add(Math.Clamp(boundary + delta, 0, UInt16.MaxValue));
                foreach (int charge in charges)
                {
                    float oldFraction = (charge - weapon.MinCharge * 2) / (float)(weapon.FullCharge * 2 - weapon.MinCharge * 2);
                    float newFraction = (charge - SimTicks.From30HzFrames(weapon.MinCharge))
                        / (float)(SimTicks.From30HzFrames(weapon.FullCharge) - SimTicks.From30HzFrames(weapon.MinCharge));
                    Assert.Equal(BitConverter.SingleToInt32Bits(oldFraction), BitConverter.SingleToInt32Bits(newFraction));
                    Assert.Equal(charge >= weapon.MinCharge * 2, charge >= SimTicks.From30HzFrames(weapon.MinCharge));
                    Assert.Equal(charge >= weapon.FullCharge * 2, charge >= SimTicks.From30HzFrames(weapon.FullCharge));
                }
            }
        }

        [Fact]
        public void PowerBeamAutofirePreservesTruncationBeforeDurationConversion()
        {
            foreach (byte baseCooldown in AllWeapons().Select(w => w.AutofireCooldown).Distinct())
                for (int elapsed = 0; elapsed <= UInt16.MaxValue; elapsed++)
                {
                    int pbAuto = Math.Min(elapsed / 2, 90);
                    pbAuto = (int)(pbAuto * 15 / 90f);
                    Assert.Equal((ushort)((pbAuto + baseCooldown) * 2),
                        (ushort)SimTicks.From30HzFrames(pbAuto + baseCooldown));
                }
        }

        private static IEnumerable<WeaponInfo> AllWeapons()
            => Weapons.WeaponsMP.Concat(Weapons.ForceFieldLockWeapons).Concat(Weapons.PlatformWeapons);
    }
}
