using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class ShotSpreadSeedTests
    {
        [Fact]
        public void MatchSpreadDoesNotDependOnInterveningGameplayRandomness()
        {
            var random = new MatchRandom();
            var enabled = new ServerCombat(spreadSeed: 123);
            var disabled = new ServerCombat(lagCompEnabled: false, spreadSeed: 123);
            uint previous = 123;
            for (int shot = 0; shot < 32; shot++)
            {
                uint expected = disabled.NextSpreadSeed();
                for (int effect = 0; effect < shot; effect++) random.GetRandomInt2(100);
                uint global = random.Rng2;
                Assert.Equal(expected, enabled.NextSpreadSeed());
                Assert.Equal(global, random.Rng2);
                Assert.NotEqual(previous, expected);
                previous = expected;
            }
        }

        [Fact]
        public void ResetRestoresInitialMatchSpreadWithoutReadingGlobalState()
        {
            var random = new MatchRandom();
            var combat = new ServerCombat(spreadSeed: 456);
            uint first = combat.NextSpreadSeed();
            uint second = combat.NextSpreadSeed();
            random.GetRandomInt2(100);
            combat.Reset();
            Assert.Equal(first, combat.NextSpreadSeed());
            Assert.Equal(second, combat.NextSpreadSeed());
            Assert.NotEqual(first, new ServerCombat(spreadSeed: 789).NextSpreadSeed());
        }

        [Fact]
        public void ExplicitMatchSeedIsCapturedAtConstructionRatherThanFirstShot()
        {
            var random = new MatchRandom();
            random.SetRng2(0x12345678);
            var combat = new ServerCombat(spreadSeed: random.Rng2);
            random.SetRng2(999);
            Assert.Equal(0xA4629249u, combat.NextSpreadSeed());
            Assert.Equal(999u, random.Rng2);
        }
    }
}
