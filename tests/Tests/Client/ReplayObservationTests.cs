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
    public void ContextResolvesActorsByConnectionAndLifeNotSlot()
    {
        CombatActor original = new(0, 10, 1);
        CombatActor reusedSlot = new(0, 99, 1);
        ObservationContext context = Context(20,
            [Player(0) with { Identity = reusedSlot }]);

        Assert.False(context.TryGetPlayer(original, out _));
        Assert.True(context.TryGetPlayer(reusedSlot, out ObservationPlayer player));
        Assert.Equal(reusedSlot, player.Identity);
    }

    [Fact]
    public void DirectorDoesNotScoreStaleActorAfterSlotReuse()
    {
        CombatActor original = new(0, 10, 1);
        ObservationPlayer reused = Player(0) with
        {
            Identity = new CombatActor(0, 99, 1)
        };
        ObservationContext baseline = Context(120, [reused]);
        ObservationContext staleEvent = ObservationContext.Create(
            ObservationSourceKind.LiveSpectator, 120, 7, 2, MatchMode.Battle,
            MatchPhase.Playing, MatchPeriod.Regulation,
            players: [reused], combatEvents: [Combat(1, 120) with { Actor = original }]);

        var director = new BroadcastDirector();
        Assert.Equal(director.Interest(baseline, BroadcastFocus.Player(0)),
            director.Interest(staleEvent, BroadcastFocus.Player(0)));
    }

    [Fact]
    public void DirectorDoesNotScoreAFormerLifeInTheSameSlot()
    {
        CombatActor formerLife = new(0, 10, 1);
        ObservationPlayer current = Player(0) with
        {
            Identity = new CombatActor(0, 10, 2)
        };
        ObservationContext baseline = Context(120, [current]);
        ObservationContext staleEvent = ObservationContext.Create(
            ObservationSourceKind.LiveSpectator, 120, 7, 2, MatchMode.Battle,
            MatchPhase.Playing, MatchPeriod.Regulation,
            players: [current], combatEvents: [Combat(1, 120) with { Actor = formerLife }]);

        var director = new BroadcastDirector();
        Assert.Equal(director.Interest(baseline, BroadcastFocus.Player(0)),
            director.Interest(staleEvent, BroadcastFocus.Player(0)));
    }

    [Fact]
    public void DirectorRecoversAnInvalidManualTargetAndRemovedObjective()
    {
        ObservationObjective flag = Objective(40, carrierSlot: 0, atBase: true);
        ObservationContext initial = ObservationContext.Create(
            ObservationSourceKind.LiveSpectator,
            100, 7, 2, MatchMode.Capture, MatchPhase.Playing,
            MatchPeriod.Regulation, [Player(0), Player(1)], [flag]);
        var director = new BroadcastDirector();
        Assert.True(director.Lock(BroadcastFocus.Objective(flag.EntityId), initial));

        ObservationContext carrierChanged = ObservationContext.Create(
            ObservationSourceKind.LiveSpectator, 106, 7, 2, MatchMode.Capture,
            MatchPhase.Playing, MatchPeriod.Regulation,
            players: [Player(0) with { CarriesObjective = false },
                Player(1) with { CarriesObjective = true }],
            objectives: [Objective(flag.EntityId, carrierSlot: 1, atBase: false)]);
        Assert.Equal(BroadcastFocus.Objective(flag.EntityId),
            director.Update(carrierChanged));

        ObservationContext removed = Context(112, [Player(0), Player(1)]);
        Assert.Equal(BroadcastFocus.Player(0), director.Update(removed));
        Assert.False(director.IsManualLock);

        ObservationContext stalePlayer = Context(118,
            [Player(0), Player(1) with { Identity = new CombatActor(1, 11, 9) }]);
        Assert.Equal(BroadcastFocus.Player(0), director.Update(stalePlayer));
        Assert.False(director.IsManualLock);
    }

    [Fact]
    public void DirectorDiagnosticsExposeShotAgeScoresOverridesAndRecentPenalty()
    {
        var director = new BroadcastDirector();
        ObservationContext first = Context(100, [Player(0), Player(1)]);
        Assert.Equal(BroadcastFocus.Player(0), director.Update(first));
        Assert.True(director.Lock(BroadcastFocus.Player(1),
            Context(110, [Player(0), Player(1)])));

        CombatActor firstActor = Player(0).Identity;
        ObservationContext eventContext = ObservationContext.Create(
            ObservationSourceKind.LiveSpectator, 116, 7, 2, MatchMode.Battle,
            MatchPhase.Playing, MatchPeriod.Regulation,
            players: [Player(0), Player(1)], awards: [new MatchAward(
                1, 1, 7, 2, 116, MatchAwardKind.DoubleKill,
                firstActor, CombatActor.None, Count: 2)]);

        BroadcastDirectorDiagnostics diagnostics = director.CaptureDiagnostics(eventContext);
        Assert.Equal(BroadcastFocus.Player(1), diagnostics.Focus);
        Assert.True(diagnostics.ManualLock);
        Assert.Equal(6u, diagnostics.ShotAgeTicks);
        Assert.True(diagnostics.CurrentScore > 0);
        Assert.True(diagnostics.BestScore > diagnostics.CurrentScore);
        Assert.True(diagnostics.BestOverride);
        Assert.Equal(35, diagnostics.BestRecentPenalty);
        Assert.True(diagnostics.RecentActorHead > 0);
    }

    [Fact]
    public void DirectorPrioritizesPrimeOvertimeAndMatchPointFacts()
    {
        WorldEvent matchPoint = new(1, 100, 7, 2, WorldSubjectKind.Match,
            WorldSignalKind.MatchPoint, Team: 0, EntityId: 0,
            Actor: CombatActor.None, Position: Vector3.Zero, A: 0);
        ObservationContext context = ObservationContext.Create(
            ObservationSourceKind.LiveSpectator, 100, 7, 2, MatchMode.PrimeHunter,
            MatchPhase.Playing, MatchPeriod.Overtime,
            players: [Player(0) with { IsPrime = true, CarriesObjective = true },
                Player(1)], worldFeedback: [matchPoint]);
        var director = new BroadcastDirector();

        Assert.True(context.IsOvertime);
        Assert.True(context.IsMatchPoint);
        Assert.Equal(BroadcastFocus.Player(0), director.Update(context));
        BroadcastDirectorDiagnostics diagnostics = director.CaptureDiagnostics(context);
        Assert.True(diagnostics.BestScore >= 395);
    }

    [Fact]
    public void DirectorResetsOnBackwardTickWithoutRetainingShotAge()
    {
        var director = new BroadcastDirector();
        Assert.Equal(BroadcastFocus.Player(0),
            director.Update(Context(500, [Player(0), Player(1)])));
        Assert.Equal(BroadcastFocus.Player(0),
            director.Update(Context(400, [Player(0), Player(1)])));

        BroadcastDirectorDiagnostics diagnostics = director.CaptureDiagnostics(
            Context(400, [Player(0), Player(1)]));
        Assert.Equal(0u, diagnostics.ShotAgeTicks);
        Assert.Equal(400u, diagnostics.ShotStartedTick);
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
    public void DirectorManualLockDoesNotLeakIntoAnotherMatch()
    {
        var director = new BroadcastDirector();
        ObservationContext first = Context(60, [Player(0), Player(1)]);
        Assert.Equal(BroadcastFocus.Player(0), director.Update(first));
        Assert.True(director.Lock(BroadcastFocus.Player(1), first));

        ObservationContext nextMatch = ObservationContext.Create(
            ObservationSourceKind.LiveSpectator, 120, 8, 1, MatchMode.Battle,
            MatchPhase.Playing, MatchPeriod.Regulation,
            players: [Player(0), Player(1)], matchTimeSeconds: 300);

        Assert.Equal(BroadcastFocus.Player(0), director.Update(nextMatch));
        Assert.False(director.IsManualLock);
    }

    [Fact]
    public void FirstCombatFactEstablishesJournalEpochAndNewMatchClearsTheOldOne()
    {
        var journal = new BroadcastObservationJournal();
        CombatEvent first = Combat(id: 1, tick: 10);
        CombatEvent second = Combat(id: 2, tick: 20);

        Assert.True(journal.Record(first, 7, 2));
        Assert.Single(journal.Snapshot(7, 2, 10).CombatEvents);
        Assert.True(journal.Record(second, 8, 1));

        BroadcastObservationFacts next = journal.Snapshot(8, 1, 20);
        Assert.Single(next.CombatEvents);
        Assert.Equal(2u, next.CombatEvents[0].Id);
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

    private static ObservationPlayer Player(int slot) => new ObservationPlayer(slot,
        $"P{slot}", Hunter.Samus, slot, Connected: true, Active: true,
        Alive: true, CarriesObjective: false, IsPrime: false, Health: 100,
        Weapon: BeamType.PowerBeam, AmmoUa: 40, AmmoMissiles: 5,
        Points: 0, Kills: 0, Deaths: 0, Assists: 0,
        Position: new Vector3(slot * 4, 0, 0), Facing: -Vector3.UnitZ)
        with { Identity = new CombatActor((byte)slot, (ulong)(10 + slot), 1) };

    private static ObservationObjective Objective(int entityId, int carrierSlot,
        bool atBase)
        => new(entityId, ObservationObjectiveKind.Flag, Vector3.Zero, 0,
            Contested: false, carrierSlot, atBase);

    private static CombatEvent Combat(uint id, uint tick) => new(id, tick, 0,
        CombatEventKind.Damage, 0, CombatEventFlags.None,
        new CombatActor(0, 10, 1), new CombatActor(1, 11, 1), 90, 10,
        Vector3.Zero, Vector3.UnitZ, 0, 0, 0);
}
