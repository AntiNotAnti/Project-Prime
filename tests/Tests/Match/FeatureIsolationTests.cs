using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class FeatureIsolationTests
{
    [Fact]
    public void DifferentFeatureOptionsDoNotChangeOtherScenesMatchTrace()
    {
        var defaults = new MatchFeatureSet();
        var strict = defaults with
        {
            AllowInvalidTeams = false,
            Cheats = defaults.Cheats with { QuadrupleDamage = true },
            Bugfixes = defaults.Bugfixes with { NoDoubleEnemyDeath = false }
        };
        using Scene first = Create(strict);
        using Scene second = Create(defaults);
        using Scene control = Create(defaults);
        first.Match.Flow.ProcessFrame();
        second.Match.Flow.ProcessFrame();
        control.Match.Flow.ProcessFrame();

        // Local lifecycle replaces the expired clock with its three-second
        // ending countdown after capturing the invalid-team result.
        Assert.Equal(3, first.Match.MatchTime);
        Assert.Equal(MatchPhase.Ending, first.Match.Phase);
        Assert.Equal(MatchEndReason.InvalidTeams, first.Match.Result!.EndReason);
        Assert.Equal(control.Match.MatchTime, second.Match.MatchTime);
        Assert.Equal(control.Match.Phase, second.Match.Phase);
        Assert.Equal(control.Match.Result, second.Match.Result);
        Assert.Equal(120, second.Match.MatchTime);
        Assert.Equal(MatchPhase.Playing, second.Match.Phase);
        Assert.Null(second.Match.Result);
        Assert.False(second.Features.Cheats.QuadrupleDamage);
        Assert.True(second.Features.Bugfixes.NoDoubleEnemyDeath);
        Assert.True(second.Features.AllowInvalidTeams);
    }

    [Fact]
    public void ClientPreferenceChangesOnlyAffectExplicitNewSnapshots()
    {
        bool previous = Cheats.QuadrupleDamage;
        try
        {
            Cheats.QuadrupleDamage = false;
            using Scene first = new(headless: true, features: ClientMatchFeatures.Capture());
            Cheats.QuadrupleDamage = true;
            using Scene second = new(headless: true, features: ClientMatchFeatures.Capture());
            using Scene serverDefault = new(headless: true);
            Assert.False(first.Features.Cheats.QuadrupleDamage);
            Assert.True(second.Features.Cheats.QuadrupleDamage);
            Assert.False(serverDefault.Features.Cheats.QuadrupleDamage);
        }
        finally { Cheats.QuadrupleDamage = previous; }
    }

    private static Scene Create(MatchFeatureSet features)
    {
        Scene scene = new(headless: true, features: features);
        scene.Players.MaxPlayers = 1;
        scene.Match.Phase = MatchPhase.Playing;
        scene.Match.MatchTime = 120;
        return scene;
    }
}
