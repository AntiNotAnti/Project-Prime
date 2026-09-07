using System;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class ServerSpawnOptionsTests
    {
        [Theory]
        [InlineData(null, false, SpawnPolicy.Classic)]
        [InlineData("classic", true, SpawnPolicy.Classic)]
        [InlineData("Enhanced", true, SpawnPolicy.Enhanced)]
        [InlineData("DUEL", true, SpawnPolicy.Duel)]
        public void PolicyNamesAreExplicitAndDefaultRemainsClassic(string? value, bool present, SpawnPolicy expected)
            => Assert.Equal(expected, ServerSpawnOptions.ParsePolicy(value, present));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("unknown")]
        public void PresentPolicyRejectsMissingOrUnsupportedValues(string? value)
            => Assert.Throws<ArgumentException>(() => ServerSpawnOptions.ParsePolicy(value, true));

        [Fact]
        public void CancellationRequiresAnExplicitBoolean()
        {
            Assert.False(ServerSpawnOptions.ParseCancellation(null, false));
            Assert.False(ServerSpawnOptions.ParseCancellation("false", true));
            Assert.True(ServerSpawnOptions.ParseCancellation("true", true));
            Assert.Throws<ArgumentException>(() => ServerSpawnOptions.ParseCancellation(null, true));
            Assert.Throws<ArgumentException>(() => ServerSpawnOptions.ParseCancellation("yes", true));
        }

        [Fact]
        public void RulesRetainSpawnOptionsAcrossRotationAndValidateTheEnum()
        {
            MatchRules defaults = MatchRules.CreateDefault(MatchMode.Battle, "UNIT_TEST1");
            Assert.Equal(SpawnPolicy.Classic, defaults.SpawnPolicy);
            Assert.False(defaults.CancelSpawnProtectionOnOffensiveAction);
            MatchRules configured = defaults.With(spawnPolicy: SpawnPolicy.Duel, cancelSpawnProtectionOnOffensiveAction: true);
            MatchRules rotated = configured.With(roomKey: "UNIT_TEST2");
            Assert.Equal(SpawnPolicy.Duel, rotated.SpawnPolicy);
            Assert.True(rotated.CancelSpawnProtectionOnOffensiveAction);
            Assert.Throws<ArgumentOutOfRangeException>(() => defaults.With(spawnPolicy: (SpawnPolicy)255));
        }
    }
}
