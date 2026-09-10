using System.Collections.Generic;
using MphRead.Combat;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class ReplayObservationTests
{
    [Fact]
    public void ContextDropsFactsNewerThanDeliveredReplayTick()
    {
        CombatActor one = new(0, 10, 1);
        ObservationContext context = ObservationContext.Create(
            ObservationSourceKind.Replay, 100, 7, 2, MatchMode.Battle,
            MatchPhase.Playing, MatchPeriod.Regulation,
            players: [Player(0)],
            combatFeedback:
            [
                new KillFeedEntry(99, one, new CombatActor(1, 11, 1), "past"),
                new KillFeedEntry(101, one, new CombatActor(1, 11, 1), "future")
            ],
            awards:
            [
                new MatchAward(1, 1, 7, 2, 100, MatchAwardKind.FirstHunt, one,
                    CombatActor.None),
                new MatchAward(2, 2, 7, 2, 101, MatchAwardKind.FirstHunt, one,
                    CombatActor.None)
            ]);

        Assert.Equal(ObservationSourceKind.Replay, context.Source);
        Assert.Single(context.CombatFeedback);
        Assert.Single(context.Awards);
        Assert.Equal(100u, context.Awards[0].Tick);
    }

    [Fact]
    public void DirectorIsDeterministicAndHonorsManualLock()
    {
        ObservationContext tied = Context(0, [Player(0), Player(1)]);
        var director = new BroadcastDirector();
        Assert.Equal(BroadcastFocus.Player(0), director.Update(tied));
        Assert.True(director.Lock(BroadcastFocus.Player(1), tied));
        Assert.Equal(BroadcastFocus.Player(1), director.Update(Context(300,
            [Player(0), Player(1)])));
        Assert.Equal(BroadcastFocus.Player(0), director.Update(Context(306,
            [Player(0)])));
    }

    [Fact]
    public void DirectorUsesEventOverrideWithoutRapidScoreChurn()
    {
        var director = new BroadcastDirector();
        Assert.Equal(BroadcastFocus.Player(0), director.Update(Context(0,
            [Player(0), Player(1)])));
        CombatActor second = new(1, 11, 1);
        ObservationContext eventContext = ObservationContext.Create(
            ObservationSourceKind.LiveSpectator, 6, 7, 2, MatchMode.Battle,
            MatchPhase.Playing, MatchPeriod.Regulation,
            players: [Player(0), Player(1)],
            awards: [new MatchAward(1, 1, 7, 2, 6,
                MatchAwardKind.TripleKill, second, CombatActor.None)]);
        Assert.Equal(BroadcastFocus.Player(1), director.Update(eventContext));
        Assert.Equal(BroadcastFocus.Player(1), director.Update(Context(12,
            [Player(0) with { Points = 50 }, Player(1)])));
    }

    [Fact]
    public void BroadcastHudOffIsCleanFeed()
    {
        var hud = new BroadcastHud();
        hud.SetMode(BroadcastHudMode.Off);
        BroadcastHudModel model = hud.Compose(Context(60, [Player(0)]),
            BroadcastFocus.Player(0), SpectatorCameraMode.Chase, 78, 1);
        Assert.False(model.Visible);
        Assert.Empty(model.Lines);
    }

    [Fact]
    public void BroadcastHudFullIncludesDeliveredKillFeedAndAwardWhileMinimalOmitsThem()
    {
        CombatActor first = new(0, 10, 1);
        ObservationContext context = ObservationContext.Create(
            ObservationSourceKind.Replay, 60, 7, 2, MatchMode.Battle,
            MatchPhase.Playing, MatchPeriod.Regulation,
            players: [Player(0)],
            combatFeedback: [new KillFeedEntry(59, first,
                new CombatActor(1, 11, 1), "P0 eliminated P1")],
            awards: [new MatchAward(1, 1, 7, 2, 60,
                MatchAwardKind.FirstHunt, first, CombatActor.None)],
            matchTimeSeconds: 300);
        var hud = new BroadcastHud();

        BroadcastHudModel full = hud.Compose(context, BroadcastFocus.Player(0),
            SpectatorCameraMode.FirstPerson, 78, 1);
        Assert.Contains("P0 eliminated P1", full.Lines);
        Assert.Contains("FIRST HUNT · P0", full.Lines);
        Assert.Contains(full.Lines, line => line.Contains("FirstPerson"));

        hud.SetMode(BroadcastHudMode.Minimal);
        BroadcastHudModel minimal = hud.Compose(context, BroadcastFocus.Player(0),
            SpectatorCameraMode.FirstPerson, 78, 1);
        Assert.DoesNotContain("P0 eliminated P1", minimal.Lines);
        Assert.DoesNotContain("FIRST HUNT · P0", minimal.Lines);
    }

    private static ObservationContext Context(uint tick,
        IEnumerable<ObservationPlayer> players)
        => ObservationContext.Create(ObservationSourceKind.LiveSpectator, tick,
            7, 2, MatchMode.Battle, MatchPhase.Playing,
            MatchPeriod.Regulation, players: players, matchTimeSeconds: 300);

    private static ObservationPlayer Player(int slot) => new(slot,
        $"P{slot}", Hunter.Samus, slot, Connected: true, Active: true,
        Alive: true, CarriesObjective: false, IsPrime: false, Health: 100,
        Weapon: BeamType.PowerBeam, AmmoUa: 40, AmmoMissiles: 5,
        Points: 0, Kills: 0, Deaths: 0, Assists: 0,
        Position: new Vector3(slot * 4, 0, 0), Facing: -Vector3.UnitZ);
}
