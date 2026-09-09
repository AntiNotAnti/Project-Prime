using System;
using MphRead.Combat;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Sound;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

/// <summary>
/// QZ0 translations for objective boundaries and exactly-once semantic
/// presentation. Authoritative entity edges and WorldEvents are the facts;
/// presentation may consume each edge once but may not invent a transition.
/// </summary>
[Collection("Match baseline globals")]
public sealed class Qz0ObjectivePresentationRegressionTests
{
    [Fact]
    [Trait("Regression", "QZ0")]
    public void DoorCloseOpenEdgesEmitOnePresentationSignalWithoutSteadyFrameDuplicates()
    {
        // Prime invariant: DoorEntity.Process owns the authoritative state edge;
        // each open/close edge emits one matching presentation request, while
        // steady simulation frames do not replay that semantic transition.
        var fixture = new MovingGeometryFixture();
        using var scene = new Scene();
        PlayerEntity player = scene.Players[0];
        player.Health = 100;
        player.Position = Vector3.Zero;
        scene.InsertEntity(player);
        DoorEntity door = fixture.Door(scene);
        int opens = 0, closes = 0;
        scene.Audio.Requested += request =>
        {
            if (request.Kind != AudioRequestKind.Play || !ReferenceEquals(request.Source, door._soundSource)) return;
            if (request.Id == (int)SfxId.DOOR_OPEN) opens++;
            if (request.Id == (int)SfxId.DOOR_CLOSE) closes++;
        };

        door.Flags |= DoorFlags.ShotOpen;
        Assert.True(door.Process());
        Assert.True(door.Flags.TestFlag(DoorFlags.ShouldOpen));
        Assert.Equal(1, opens);
        Assert.True(door.Process());
        Assert.Equal(1, opens);

        player.Position = new Vector3(10, 0, 0);
        door.AnimInfo.Flags[0] |= AnimFlags.Ended;
        door.AnimInfo.Flags[0] &= ~AnimFlags.Reverse;
        Assert.True(door.Process());
        Assert.False(door.Flags.TestFlag(DoorFlags.ShouldOpen));
        Assert.True(door.Flags.TestFlag(DoorFlags.Closed));
        Assert.Equal(1, closes);
        Assert.True(door.Process());
        Assert.Equal(1, closes);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void CarrierSuicideCannotCreateAnInterceptorOrSelfAward()
    {
        // Prime invariant: a carrier's suicide remains a non-competitive kill;
        // the carrier flag cannot turn it into an Interceptor or streak award.
        var fixture = new SlotReuseFixture();
        var dispatcher = new MatchEventDispatcher();
        var awards = new System.Collections.Generic.List<MatchAward>();
        var engine = new AwardEngine();
        engine.Awarded += awards.Add;
        dispatcher.Subscribe(engine);
        var suicide = new MatchEvent(0, 100, 1, 1, MatchEventKind.PlayerKilled,
            fixture.ReplacementLife, fixture.ReplacementLife, EntityId: 44, Team: 1,
            Flags: MatchEventFlags.Suicide | MatchEventFlags.ObjectiveCarrier);

        Assert.True(suicide.IsValid);
        dispatcher.Dispatch(suicide);
        dispatcher.Dispatch(new(0, 100, 1, 1, MatchEventKind.ObjectiveDropped,
            fixture.ReplacementLife, CombatActor.None, EntityId: 44, Team: 1));

        Assert.Empty(awards);
        Assert.Equal(2, engine.EventsConsumed);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void ObjectiveResetOnScoreTickPreservesBothTransitionsExactlyOnce()
    {
        // Prime invariant: resetting an objective on the same authoritative
        // tick as the score boundary cannot erase or duplicate either fact.
        var feedback = new WorldFeedback();
        feedback.Bind(1, 1);
        var reset = new WorldEvent(30, 500, 1, 1, WorldSubjectKind.Flag,
            WorldSignalKind.FlagReset, 1, 44, CombatActor.None, Vector3.Zero);
        var matchPoint = new WorldEvent(31, 500, 1, 1, WorldSubjectKind.Match,
            WorldSignalKind.MatchPoint, 1, 0, CombatActor.None, Vector3.Zero);

        Assert.True(feedback.Process(reset, CombatActor.None, 500));
        Assert.True(feedback.Process(matchPoint, CombatActor.None, 500));
        Assert.False(feedback.Process(reset, CombatActor.None, 501));
        Assert.False(feedback.Process(matchPoint, CombatActor.None, 501));
        Assert.Equal(2u, feedback.Sequence);
        Assert.Equal("MATCH POINT", feedback.Message);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void ScoreLimitWinsDeterministicallyWhenTheTimerExpiresOnTheSameTick()
    {
        // Prime invariant: when score and regulation expiration coincide, the
        // authority retains ScoreGoal and does not invent an overtime period.
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        state.Activate(0, 0);
        state.Activate(1, 1);
        match.ApplyRules(match.Rules.With(scoreGoal: 3,
            overtimePolicy: OvertimePolicy.ModeDefault));
        match.Phase = MatchPhase.Playing;
        match.Period = MatchPeriod.Regulation;
        match.MatchTime = 0;
        match.TeamPoints[0] = 3;
        match.TeamPoints[1] = 2;

        match.Logic.ProcessMode();
        MatchOvertime.Evaluate(state.Scene, regulationExpired: true, explicitEnd: null);

        Assert.Equal(MatchEndReason.ScoreGoal, match.PendingEndReason);
        Assert.Equal(MatchPeriod.Regulation, match.Period);
        Assert.Equal(0, match.MatchTime);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void FlagCaptureIsPresentedExactlyOnceWhenTheReliableEventRetransmits()
    {
        // Prime invariant: a retransmitted objective transition produces one
        // cue, one sequence increment, and no duplicate UI state.
        var feedback = new WorldFeedback();
        feedback.Bind(1, 1);
        WorldEvent captured = Flag(9, WorldSignalKind.FlagCaptured);

        Assert.True(feedback.Process(captured, CombatActor.None, 100));
        Assert.False(feedback.Process(captured, CombatActor.None, 101));
        Assert.Equal(1u, feedback.Sequence);
        Assert.Equal("FLAG CAPTURED", feedback.Message);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void PickupRespawnAcrossEventIdWrapRemainsExactlyOnce()
    {
        // Prime invariant: event-ID wrap is not a second pickup respawn, while
        // a genuinely newer wrapped ID remains deliverable.
        var feedback = new WorldFeedback();
        feedback.Bind(1, 1);
        WorldEvent first = Respawn(UInt32.MaxValue - 1, ItemType.DoubleDamage);
        WorldEvent wrapped = Respawn(1, ItemType.DoubleDamage);

        Assert.True(feedback.Process(first, CombatActor.None, 10, pickupRespawnAnnouncements: true));
        Assert.True(feedback.Process(wrapped, CombatActor.None, 20, pickupRespawnAnnouncements: true));
        Assert.False(feedback.Process(wrapped, CombatActor.None, 21, pickupRespawnAnnouncements: true));
        Assert.Equal(2u, feedback.Sequence);
        Assert.Equal("MAJOR PICKUP AVAILABLE", feedback.Message);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void OrdinaryPickupRespawnDoesNotBecomeAHighPriorityObjectiveCue()
    {
        // Prime invariant: presentation policy distinguishes an ordinary pickup
        // from a major objective resource without changing world state.
        var feedback = new WorldFeedback();
        feedback.Bind(1, 1);
        Assert.True(feedback.Process(Respawn(1, ItemType.HealthSmall), CombatActor.None, 10,
            pickupRespawnAnnouncements: true));
        Assert.Equal(0u, feedback.Sequence);
        Assert.Equal("", feedback.Message);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void NodeCaptureRequiresAnAuthoritativeTeamAndNodeShape()
    {
        // Prime invariant: an objective capture is accepted only with a valid
        // team and node subject; malformed client-like claims are rejected.
        WorldEvent valid = new(1, 30, 1, 1, WorldSubjectKind.Node,
            WorldSignalKind.NodeCaptured, 1, 22, new CombatActor(0, 5, 1), Vector3.Zero);
        WorldEvent invalidTeam = valid with { Team = 8 };
        WorldEvent invalidSubject = valid with { Subject = WorldSubjectKind.Flag };

        Assert.True(valid.IsValid);
        Assert.False(invalidTeam.IsValid);
        Assert.False(invalidSubject.IsValid);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void OvertimeAndMatchPointUseTypedObjectiveBoundaries()
    {
        // Prime invariant: overtime and match point are distinct, bounded
        // semantic transitions and cannot be replaced by arbitrary values.
        WorldEvent overtime = new(1, 50, 1, 1, WorldSubjectKind.Match,
            WorldSignalKind.OvertimeStarted, 255, 0, CombatActor.None, Vector3.Zero, A: 1);
        WorldEvent matchPoint = new(2, 51, 1, 1, WorldSubjectKind.Match,
            WorldSignalKind.MatchPoint, 0, 0, CombatActor.None, Vector3.Zero);
        WorldEvent invalidOvertime = overtime with { A = 3 };

        Assert.True(overtime.IsValid);
        Assert.True(matchPoint.IsValid);
        Assert.False(invalidOvertime.IsValid);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void ObjectiveFeedbackPublishesOvertimeThenMatchPointInServerOrder()
    {
        // Prime invariant: a client presents the authoritative event order and
        // never reorders a score-boundary transition from local wall time.
        var feedback = new WorldFeedback();
        feedback.Bind(1, 1);
        WorldEvent overtime = new(10, 500, 1, 1, WorldSubjectKind.Match,
            WorldSignalKind.OvertimeStarted, 255, 0, CombatActor.None, Vector3.Zero, A: 1);
        WorldEvent matchPoint = new(11, 501, 1, 1, WorldSubjectKind.Match,
            WorldSignalKind.MatchPoint, 0, 0, CombatActor.None, Vector3.Zero);

        Assert.True(feedback.Process(overtime, CombatActor.None, 600));
        Assert.Equal("OVERTIME", feedback.Message);
        Assert.True(feedback.Process(matchPoint, CombatActor.None, 601));
        Assert.Equal("MATCH POINT", feedback.Message);
        Assert.Equal(2u, feedback.Sequence);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void WorldEventRoundTripPreservesObjectiveIdentityAcrossTickWrap()
    {
        // Prime invariant: serialization preserves server tick, event ID, and
        // objective identity exactly across uint wrap.
        WorldEvent value = Flag(UInt32.MaxValue - 2, WorldSignalKind.FlagReset) with
        {
            Tick = UInt32.MaxValue - 1,
            MatchId = 77,
            PhaseRevision = 9
        };
        byte[] bytes = new byte[WorldEvent.Size];
        value.Write(bytes);
        Assert.True(WorldEvent.TryRead(bytes, out WorldEvent decoded));
        Assert.Equal(value, decoded);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void UnsupportedSemanticTransitionCannotReachPresentation()
    {
        // Prime invariant: an unknown/foreign event kind fails closed instead
        // of being interpreted as a door, pickup, or objective transition.
        WorldEvent unknown = Flag(1, (WorldSignalKind)250);
        byte[] bytes = new byte[WorldEvent.Size];
        Assert.False(unknown.IsValid);
        Assert.Throws<ArgumentException>(() => unknown.Write(bytes));
        Assert.False(WorldEvent.TryRead(bytes, out _));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void WorldFeedbackAcceptsReorderedObjectiveFactsButNeverDuplicatesAnId()
    {
        // Prime invariant: bounded reordering is safe, while the same semantic
        // event ID remains exactly-once even if it arrives repeatedly.
        var feedback = new WorldFeedback();
        feedback.Bind(1, 1);
        WorldEvent later = Flag(20, WorldSignalKind.FlagCaptured);
        WorldEvent earlier = Flag(19, WorldSignalKind.FlagDropped);

        Assert.True(feedback.Process(later, CombatActor.None, 20));
        Assert.True(feedback.Process(earlier, CombatActor.None, 19));
        Assert.False(feedback.Process(later, CombatActor.None, 21));
        Assert.Equal(2u, feedback.Sequence);
    }

    private static WorldEvent Flag(uint id, WorldSignalKind kind)
        => new(id, id, 1, 1, WorldSubjectKind.Flag, kind, 255, 1,
            new CombatActor(0, 10, 1), Vector3.Zero);

    private static WorldEvent Respawn(uint id, ItemType item)
        => new(id, id, 1, 1, WorldSubjectKind.Spawner, WorldSignalKind.PickupRespawned,
            255, 7, CombatActor.None, new Vector3(4, 5, 6), A: (uint)item);
}
