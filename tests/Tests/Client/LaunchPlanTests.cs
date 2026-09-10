using System;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    [Collection("Replay global state")]
    public sealed class LaunchPlanTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        public void SupportedPersistedKindsValidate(int persistedValue)
        {
            var plan = new LaunchPlan { Kind = (LaunchKind)persistedValue };

            plan.Validate();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(6)]
        [InlineData(-1)]
        [InlineData(99)]
        public void RetiredAndUnknownPersistedKindsAreRejected(int persistedValue)
        {
            var plan = new LaunchPlan { Kind = (LaunchKind)persistedValue };

            Assert.Throws<ArgumentException>(plan.Validate);
        }

        [Theory]
        [InlineData(1)]
        public void NonReplayLaunchRequiresAuthoritativeSessionBeforeLoading(int persistedValue)
        {
            Assert.Null(AuthoritativePlay.Current);
            var plan = new LaunchPlan { Kind = (LaunchKind)persistedValue };

            Assert.Equal(MatchExitReason.FailedToStart,
                MatchStart.Run(new MenuSettings(), plan).Reason);
        }
    }
}
