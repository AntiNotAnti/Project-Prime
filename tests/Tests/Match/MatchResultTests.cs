using MphRead.Entities;
using System.Linq;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class MatchResultTests
{
    [Fact]
    public void CapturedResultIsADeepImmutableSnapshotAndCapturesOnlyOnce()
    {
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        state.Activate(0, 0);
        state.Activate(1, 1);
        state.Scene.Roster.Nicknames[0] = "Alpha";
        state.Scene.Roster.Nicknames[1] = "Bravo";
        match.MatchId = 42;
        match.MatchTime = 42;
        match.ActivePlayers = 2;
        match.PrimeHunter = 1;
        match.ResultSlots[0] = 0;
        match.ResultSlots[1] = 1;
        match.TeamStandings[0] = 3;
        match.Points[0] = 9;
        match.Kills[0] = 4;
        match.Time[0] = 18.5f;
        match.BeamKills[0, 2] = 7;
        match.TeamPoints[0] = 9;
        match.TeamKills[0] = 4;
        match.TeamDeaths[0] = 2;
        match.TeamTime[0] = 18.5f;

        match.CaptureResult(12.5f);
        Assert.NotNull(match.Result);
        MatchResult result = match.Result!;

        // Mutating every live source used by the captured records must not alter
        // the completed result, including nested beam and ordering data.
        match.MatchTime = 1;
        match.ActivePlayers = 1;
        match.PrimeHunter = 0;
        match.Points[0] = 1;
        match.Kills[0] = 1;
        match.Time[0] = 2;
        match.BeamKills[0, 2] = 1;
        match.TeamPoints[0] = 1;
        match.TeamKills[0] = 1;
        match.TeamDeaths[0] = 0;
        match.TeamTime[0] = 2;
        match.ResultSlots[0] = 1;
        state.Players[0].TeamIndex = 1;
        state.Scene.Roster.Nicknames[0] = "Changed";
        match.ApplyRules(match.Rules.With(scoreGoal: 99));

        Assert.Equal(42u, result.MatchId);
        Assert.Equal(12.5f, result.CompletedAtSimulationTime);
        Assert.Equal(42f, result.RemainingMatchTime);
        Assert.Equal(2, result.ActivePlayers);
        Assert.Equal(1, result.PrimeHunter);
        Assert.Equal("Alpha", result.Players[0].Nickname);
        Assert.Equal(0, result.Players[0].TeamIndex);
        Assert.Equal(9, result.Players[0].Points);
        Assert.Equal(4, result.Players[0].Kills);
        Assert.Equal(18.5f, result.Players[0].Time);
        Assert.Equal(7, result.Players[0].BeamKills[2]);
        Assert.Equal(9, result.Teams[0].Points);
        Assert.Equal(4, result.Teams[0].Kills);
        Assert.Equal(2, result.Teams[0].Deaths);
        Assert.Equal(18.5f, result.Teams[0].Time);
        Assert.Equal(0, result.ResultSlots[0]);
        Assert.Equal(1, result.ResultSlots[1]);
        Assert.Equal(7, result.Players[0].BeamKills.ToArray()[2]);

        // CaptureResult is idempotent for the completed round; a later call
        // cannot replace the immutable snapshot or its completion timestamp.
        match.CaptureResult(99.0f);
        Assert.Same(result, match.Result);
        Assert.Equal(12.5f, match.Result!.CompletedAtSimulationTime);
    }
}
