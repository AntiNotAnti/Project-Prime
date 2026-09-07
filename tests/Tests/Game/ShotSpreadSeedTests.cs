using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    [CollectionDefinition("Shot spread RNG", DisableParallelization = true)]
    public sealed class ShotSpreadSeedCollection { }

    [Collection("Shot spread RNG")]
    public sealed class ShotSpreadSeedTests
    {
        [Fact]
        public void MatchSpreadDoesNotDependOnInterveningGameplayRandomness()
        {
            uint saved = Rng.Rng2;
            try
            {
                var enabled = new ServerCombat(spreadSeed: 123);
                var disabled = new ServerCombat(lagCompEnabled: false, spreadSeed: 123);
                uint previous = 123;
                for (int shot = 0; shot < 32; shot++)
                {
                    uint expected = disabled.NextSpreadSeed();
                    for (int effect = 0; effect < shot; effect++) Rng.GetRandomInt2(100);
                    uint global = Rng.Rng2;
                    Assert.Equal(expected, enabled.NextSpreadSeed());
                    Assert.Equal(global, Rng.Rng2);
                    Assert.NotEqual(previous, expected);
                    previous = expected;
                }
            }
            finally { Rng.SetRng2(saved); }
        }

        [Fact]
        public void ResetRestoresInitialMatchSpreadWithoutReadingGlobalState()
        {
            uint saved = Rng.Rng2;
            try
            {
                var combat = new ServerCombat(spreadSeed: 456);
                uint first = combat.NextSpreadSeed();
                uint second = combat.NextSpreadSeed();
                Rng.GetRandomInt2(100);
                combat.Reset();
                Assert.Equal(first, combat.NextSpreadSeed());
                Assert.Equal(second, combat.NextSpreadSeed());
                Assert.NotEqual(first, new ServerCombat(spreadSeed: 789).NextSpreadSeed());
            }
            finally { Rng.SetRng2(saved); }
        }

        [Fact]
        public void DefaultSeedIsCapturedAtConstructionRatherThanFirstShot()
        {
            uint saved = Rng.Rng2;
            try
            {
                Rng.SetRng2(0x12345678);
                var combat = new ServerCombat();
                Rng.SetRng2(999);
                Assert.Equal(0xA4629249u, combat.NextSpreadSeed());
                Assert.Equal(999u, Rng.Rng2);
            }
            finally { Rng.SetRng2(saved); }
        }
    }
}
