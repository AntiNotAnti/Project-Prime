using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods.Network;
using MphRead.Replay;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

/// <summary>
/// Deterministic QZ3 translated regressions. Every case drives the semantic
/// dispatcher with authoritative facts; none infer an award from snapshots.
/// </summary>
public sealed class MatchAwardsTests
{
    private static readonly CombatActor Killer = new(1, 1001, 7);
    private static readonly CombatActor KillerTwo = new(2, 1002, 3);
    private static readonly CombatActor Victim = new(3, 1003, 2);

    [Fact]
    public void DispatcherAssignsOneSemanticIdAcrossProducerKinds()
    {
        var dispatcher = new MatchEventDispatcher();
        var received = new List<MatchEvent>();
        dispatcher.Subscribe((in MatchEvent value) => received.Add(value));
        MatchEvent first = dispatcher.Dispatch(Kill(10, Killer, Victim));
        MatchEvent second = dispatcher.Dispatch(new(0, 10, 1, 1, MatchEventKind.PlayerSpawned,
            Killer, CombatActor.None));
        Assert.Equal(2, received.Count);
        Assert.Equal(1u, first.Id);
        Assert.Equal(2u, second.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void DispatcherReturnsNormalizedFactAndDuplicateSourceIsDeduplicated()
    {
        var engine = new AwardEngine();
        var awards = new List<MatchAward>();
        engine.Awarded += awards.Add;
        var dispatcher = new MatchEventDispatcher();
        dispatcher.Subscribe(engine);
        MatchEvent normalized = dispatcher.Dispatch(Kill(10, Killer, Victim));
        dispatcher.Dispatch(normalized);
        Assert.Single(awards);
        Assert.Equal(1L, engine.DuplicateEvents);
        Assert.Equal(normalized.Id, awards[0].SourceEventId);
    }

    [Fact]
    public void DispatcherRoundResetKeepsSemanticIdsGloballyUnique()
    {
        var dispatcher = new MatchEventDispatcher();
        var engine = new AwardEngine();
        var awards = new List<MatchAward>();
        engine.Awarded += awards.Add;
        dispatcher.Subscribe(engine);
        MatchEvent first = dispatcher.Dispatch(Kill(10, Killer, Victim));
        dispatcher.Reset();
        MatchEvent second = dispatcher.Dispatch(Kill(20, Killer, Victim with { Life = 4 }));
        Assert.Equal(1u, first.Id);
        Assert.Equal(2u, second.Id);
        Assert.Equal(1u, awards[0].AwardId);
        Assert.Equal(2u, awards[1].AwardId);
    }

    [Fact]
    public void FirstHuntIsPublishedOnlyForFirstCompetitiveKill()
    {
        var awards = Run(Kill(10, Killer, Victim));
        Assert.Contains(awards, value => value.Kind == MatchAwardKind.FirstHunt);
        Assert.DoesNotContain(awards, value => value.Kind == MatchAwardKind.DoubleKill);
    }

    [Fact]
    public void DoubleKillRequiresSecondKillInsideTickWindow()
    {
        var awards = Run(Kill(10, Killer, Victim), Kill(190, Killer, Victim with { Life = 4 }));
        Assert.Contains(awards, value => value.Kind == MatchAwardKind.DoubleKill);
        Assert.Single(awards.FindAll(value => value.Kind == MatchAwardKind.FirstHunt));
    }

    [Fact]
    public void TripleKillRequiresThirdKillInsideTickWindow()
    {
        var awards = Run(Kill(10, Killer, Victim), Kill(20, Killer, Victim with { Life = 4 }),
            Kill(30, Killer, Victim with { Life = 5 }));
        Assert.Contains(awards, value => value.Kind == MatchAwardKind.TripleKill);
        Assert.Contains(awards, value => value.Kind == MatchAwardKind.DoubleKill);
    }

    [Fact]
    public void FourthKillDoesNotInventQuadrupleAward()
    {
        var awards = Run(Kill(10, Killer, Victim), Kill(20, Killer, Victim with { Life = 4 }),
            Kill(30, Killer, Victim with { Life = 5 }), Kill(40, Killer, Victim with { Life = 6 }));
        Assert.DoesNotContain(awards, value => (byte)value.Kind > (byte)MatchAwardKind.TripleKill);
        Assert.Single(awards.FindAll(value => value.Kind == MatchAwardKind.TripleKill));
    }

    [Fact]
    public void KillAfterWindowStartsNewStreakWithoutDouble()
    {
        var awards = Run(Kill(10, Killer, Victim), Kill(10 + AwardPolicy.KillWindowTicks + 1,
            Killer, Victim with { Life = 4 }));
        Assert.DoesNotContain(awards, value => value.Kind == MatchAwardKind.DoubleKill);
    }

    [Fact]
    public void SpawnResetsOnlyThatActorStreak()
    {
        var dispatcher = new MatchEventDispatcher();
        var engine = new AwardEngine(); var awards = new List<MatchAward>(); engine.Awarded += awards.Add;
        dispatcher.Subscribe(engine);
        dispatcher.Dispatch(Kill(10, Killer, Victim));
        dispatcher.Dispatch(new(0, 11, 1, 1, MatchEventKind.PlayerSpawned, Killer, CombatActor.None));
        dispatcher.Dispatch(Kill(12, Killer, Victim with { Life = 4 }));
        Assert.DoesNotContain(awards, value => value.Kind == MatchAwardKind.DoubleKill);
    }

    [Fact]
    public void RoundBoundaryResetsFirstHuntAndStreaks()
    {
        var dispatcher = new MatchEventDispatcher(); var engine = new AwardEngine();
        var awards = new List<MatchAward>(); engine.Awarded += awards.Add; dispatcher.Subscribe(engine);
        dispatcher.Dispatch(Kill(10, Killer, Victim));
        dispatcher.Dispatch(new(0, 20, 1, 2, MatchEventKind.CountdownStarted,
            CombatActor.None, CombatActor.None));
        dispatcher.Dispatch(new(0, 30, 1, 2, MatchEventKind.PlayerKilled, Killer, Victim with { Life = 4 }));
        Assert.Equal(2, awards.FindAll(value => value.Kind == MatchAwardKind.FirstHunt).Count);
        Assert.DoesNotContain(awards, value => value.Kind == MatchAwardKind.DoubleKill);
    }

    [Fact]
    public void SuicideResetsActorAndDoesNotAward()
    {
        var awards = Run(Kill(10, Killer, Killer, MatchEventFlags.Suicide));
        Assert.Empty(awards);
    }

    [Fact]
    public void EarlierLifeSelfProjectileRetainsIdentityAndIsStillSuicide()
    {
        CombatActor priorLife = Killer with { Life = 1 };
        CombatActor currentLife = Killer with { Life = 2 };
        MatchEvent value = Kill(10, priorLife, currentLife, MatchEventFlags.Suicide);
        Assert.True(value.IsValid);
        Assert.Empty(Run(value));
    }

    [Fact]
    public void TeamKillResetsActorAndDoesNotAward()
    {
        var awards = Run(Kill(10, Killer, Victim, MatchEventFlags.TeamKill));
        Assert.Empty(awards);
    }

    [Fact]
    public void EnvironmentKillDoesNotAwardAStreak()
    {
        var awards = Run(Kill(10, CombatActor.None, Victim, MatchEventFlags.EnvironmentKill));
        Assert.Empty(awards);
    }

    [Fact]
    public void CarrierKillProducesInterceptorFromPreMutationFact()
    {
        var awards = Run(Kill(10, Killer, Victim, MatchEventFlags.ObjectiveCarrier));
        Assert.Contains(awards, value => value.Kind == MatchAwardKind.Interceptor && value.Target == Victim);
    }

    [Fact]
    public void PrimeKillProducesPrimeSlayerFromFact()
    {
        var awards = Run(Kill(10, Killer, Victim, MatchEventFlags.PrimeTarget));
        Assert.Contains(awards, value => value.Kind == MatchAwardKind.PrimeSlayer);
    }

    [Fact]
    public void ObjectiveDefendedFactProducesDefenderWithoutReinterpretingKillFlag()
    {
        Assert.DoesNotContain(Run(Kill(10, Killer, Victim, MatchEventFlags.DefendingObjective)),
            value => value.Kind == MatchAwardKind.Defender);
        var awards = Run(new MatchEvent(0, 10, 1, 1, MatchEventKind.ObjectiveDefended,
            Killer, Victim, EntityId: 44, Team: 1));
        MatchAward defender = Assert.Single(awards);
        Assert.Equal(MatchAwardKind.Defender, defender.Kind);
        Assert.Equal(Victim, defender.Target);
    }

    [Fact]
    public void GeneratedSemanticPacketRoundTripsEveryEnumeratedKind()
    {
        MatchEvent[] values =
        {
            new(1, 1, 9, 2, MatchEventKind.MatchStarted, CombatActor.None, CombatActor.None),
            new(2, 2, 9, 2, MatchEventKind.CountdownStarted, CombatActor.None, CombatActor.None),
            new(3, 3, 9, 2, MatchEventKind.PlayerKilled, Killer, Victim,
                Flags: MatchEventFlags.TeamKill | MatchEventFlags.Bot),
            new(4, 4, 9, 2, MatchEventKind.PlayerAssisted, KillerTwo, Victim),
            new(5, 5, 9, 2, MatchEventKind.PlayerSpawned, Killer, CombatActor.None),
            new(6, 6, 9, 2, MatchEventKind.ObjectivePickedUp, Killer, CombatActor.None, 10),
            new(7, 7, 9, 2, MatchEventKind.ObjectiveDropped, Killer, CombatActor.None, 10),
            new(8, 8, 9, 2, MatchEventKind.ObjectiveCaptured, Killer, CombatActor.None, 10),
            new(9, 9, 9, 2, MatchEventKind.ObjectiveDefended, Killer, Victim, 10),
            new(10, 10, 9, 2, MatchEventKind.PrimeChanged, Killer, CombatActor.None),
            new(11, 11, 9, 2, MatchEventKind.NodeCaptured, Killer, CombatActor.None, 10, 1),
            new(12, 12, 9, 2, MatchEventKind.OvertimeStarted, CombatActor.None, CombatActor.None),
            new(13, 13, 9, 2, MatchEventKind.MatchPointReached, CombatActor.None, CombatActor.None),
            new(14, 14, 9, 2, MatchEventKind.MatchEnded, CombatActor.None, CombatActor.None, Team: 1)
        };

        foreach (MatchEvent value in values)
        {
            MatchSemanticEventPacket packet = MatchSemanticEventPacketConversion.FromEvent(value);
            byte[] bytes = new byte[MatchSemanticEventPacket.Size];
            packet.Write(bytes);
            Assert.True(MatchSemanticEventPacket.TryRead(bytes, out MatchSemanticEventPacket decoded));
            Assert.True(MatchSemanticEventPacketConversion.TryToEvent(decoded, out MatchEvent restored));
            Assert.Equal(value, restored);
        }
    }

    [Fact]
    public void BotKillFollowsTheSameExplicitAwardPolicy()
    {
        var awards = Run(Kill(10, Killer, Victim, MatchEventFlags.Bot));
        Assert.Contains(awards, value => value.Kind == MatchAwardKind.FirstHunt);
    }

    [Fact]
    public void CaptureProducesCaptureAwardWithoutScoreMutation()
    {
        var awards = Run(new MatchEvent(0, 10, 1, 1, MatchEventKind.ObjectiveCaptured, Killer,
            CombatActor.None, EntityId: 44, Team: 1));
        Assert.Single(awards);
        Assert.Equal(MatchAwardKind.Capture, awards[0].Kind);
    }

    [Fact]
    public void NodeCaptureAlsoUsesCaptureAward()
    {
        var awards = Run(new MatchEvent(0, 10, 1, 1, MatchEventKind.NodeCaptured, Killer,
            CombatActor.None, EntityId: 17, Team: 1));
        Assert.Single(awards);
        Assert.Equal(MatchAwardKind.Capture, awards[0].Kind);
    }

    [Fact]
    public void AssistUsesFullSubjectIdentity()
    {
        var awards = Run(new MatchEvent(0, 10, 1, 1, MatchEventKind.PlayerAssisted,
            KillerTwo, Victim));
        Assert.Single(awards);
        Assert.Equal(KillerTwo, awards[0].Subject);
        Assert.Equal(Victim, awards[0].Target);
    }

    [Fact]
    public void MatchEndedStopsSubsequentAwards()
    {
        var dispatcher = new MatchEventDispatcher(); var engine = new AwardEngine();
        var awards = new List<MatchAward>(); engine.Awarded += awards.Add; dispatcher.Subscribe(engine);
        dispatcher.Dispatch(new(0, 5, 1, 1, MatchEventKind.MatchEnded,
            CombatActor.None, CombatActor.None, Team: 1));
        dispatcher.Dispatch(Kill(6, Killer, Victim));
        Assert.Empty(awards);
    }

    [Fact]
    public void WrappedTickWindowRemainsDeterministic()
    {
        var awards = Run(Kill(uint.MaxValue - 5, Killer, Victim),
            Kill(3, Killer, Victim with { Life = 4 }));
        Assert.Contains(awards, value => value.Kind == MatchAwardKind.DoubleKill);
    }

    [Fact]
    public void ReusedSlotWithNewConnectionCannotContinueStreak()
    {
        CombatActor replacement = Killer with { ConnectionId = 9001, Life = 1 };
        var awards = Run(Kill(10, Killer, Victim), Kill(20, replacement, Victim with { Life = 4 }));
        Assert.DoesNotContain(awards, value => value.Kind == MatchAwardKind.DoubleKill);
    }

    [Fact]
    public void ReusedSlotWithNewLifeCannotContinueStreak()
    {
        CombatActor replacement = Killer with { Life = 8 };
        var awards = Run(Kill(10, Killer, Victim), Kill(20, replacement, Victim with { Life = 4 }));
        Assert.DoesNotContain(awards, value => value.Kind == MatchAwardKind.DoubleKill);
    }

    [Fact]
    public void IndependentMatchDispatchersDoNotShareAwardsOrIds()
    {
        var first = new MatchEventDispatcher(); var second = new MatchEventDispatcher();
        var firstEngine = new AwardEngine(); var secondEngine = new AwardEngine();
        var firstAwards = new List<MatchAward>(); var secondAwards = new List<MatchAward>();
        firstEngine.Awarded += firstAwards.Add; secondEngine.Awarded += secondAwards.Add;
        first.Subscribe(firstEngine); second.Subscribe(secondEngine);
        MatchEvent firstEvent = first.Dispatch(KillForMatch(101, 10, Killer, Victim));
        MatchEvent secondEvent = second.Dispatch(KillForMatch(202, 10, Killer, Victim));
        Assert.Equal(firstEvent.Id, secondEvent.Id);
        Assert.Single(firstAwards); Assert.Single(secondAwards);
        Assert.Equal(firstAwards[0].SourceEventId, secondAwards[0].SourceEventId);
        Assert.Equal(101u, firstAwards[0].MatchId); Assert.Equal(202u, secondAwards[0].MatchId);
    }

    [Fact]
    public void TwoMatchRuntimesKeepAwardStateIndependent()
    {
        var left = new MatchRuntime(MatchRules.CreateDefault(MatchMode.Battle, "qz3-left"))
        {
            MatchId = 301
        };
        var right = new MatchRuntime(MatchRules.CreateDefault(MatchMode.Battle, "qz3-right"))
        {
            MatchId = 302
        };
        var leftAwards = new List<MatchAward>();
        var rightAwards = new List<MatchAward>();
        left.Awards.Awarded += leftAwards.Add;
        right.Awards.Awarded += rightAwards.Add;

        left.SemanticEvents.Dispatch(KillForMatch(left.MatchId, 10, Killer, Victim));
        right.SemanticEvents.Dispatch(KillForMatch(right.MatchId, 10, Killer, Victim));

        Assert.Single(leftAwards);
        Assert.Single(rightAwards);
        Assert.Equal(301u, leftAwards[0].MatchId);
        Assert.Equal(302u, rightAwards[0].MatchId);
        Assert.Equal(1u, left.SemanticEvents.LastEventId);
        Assert.Equal(1u, right.SemanticEvents.LastEventId);
    }

    [Fact]
    public void GeneratedAwardPacketRoundTripsWithoutStrings()
    {
        MatchAward source = new(7, 8, 1, 2, 99, MatchAwardKind.Interceptor,
            Killer, Victim, 1, 0);
        MatchAwardPacket packet = MatchAwardPacketConversion.FromAward(source);
        byte[] bytes = new byte[MatchAwardPacket.Size]; packet.Write(bytes);
        Assert.True(MatchAwardPacket.TryRead(bytes, out MatchAwardPacket parsed));
        Assert.Equal(source, MatchAwardPacketConversion.ToAward(parsed));
        Assert.Equal(MatchAwardPacket.Size, bytes.Length);
    }

    [Fact]
    public void GeneratedAwardPacketRejectsOutOfRangeIdentity()
    {
        MatchAwardPacket packet = new(1, 2, 1, 1, 1, MatchAwardKind.Assist,
            8, 1, 1, 255, 0, 0, 1, 0);
        Assert.False(packet.Validate());
    }

    [Fact]
    public void GeneratedAwardPacketRejectsAnInvalidOptionalTargetIdentity()
    {
        MatchAwardPacket packet = new(1, 2, 1, 1, 1, MatchAwardKind.Assist,
            1, 1, 1, 2, 0, 0, 1, 0);
        Assert.True(packet.Validate());
        Assert.False(MatchAwardPacketConversion.TryToAward(packet, out _));
    }

    [Fact]
    public void ReplayMarkerUsesRecordedTripleAwardRatherThanSnapshotInference()
    {
        MatchAward award = new(7, 8, 1, 2, 99, MatchAwardKind.TripleKill,
            Killer, Victim, 3, 0);
        MatchAwardPacket packet = MatchAwardPacketConversion.FromAward(award);
        byte[] bytes = new byte[MatchAwardPacket.Size]; packet.Write(bytes);
        ReplayMarker marker = ReplayRecorder.MarkerFor(new NetApplicationEvent(1,
            ReliableEventType.MatchAward, bytes));
        Assert.Equal(ReplayMarker.Award | ReplayMarker.MultiKill, marker);
    }

    [Fact]
    public void CaptureAwardDoesNotDuplicateItsProtocolNineObjectiveMarker()
    {
        MatchAward award = new(7, 8, 1, 2, 99, MatchAwardKind.Capture,
            Killer, CombatActor.None);
        MatchAwardPacket packet = MatchAwardPacketConversion.FromAward(award);
        byte[] bytes = new byte[MatchAwardPacket.Size];
        packet.Write(bytes);

        ReplayMarker marker = ReplayRecorder.MarkerFor(new NetApplicationEvent(1,
            ReliableEventType.MatchAward, bytes), protocol: 9);

        Assert.Equal(ReplayMarker.Award, marker);
    }

    [Theory]
    [InlineData(WorldSignalKind.FlagCaptured, MatchEventKind.ObjectiveCaptured, ReplayMarker.FlagCapture)]
    [InlineData(WorldSignalKind.NodeCaptured, MatchEventKind.NodeCaptured, ReplayMarker.NodeCapture)]
    [InlineData(WorldSignalKind.PrimeChanged, MatchEventKind.PrimeChanged, ReplayMarker.PrimeChange)]
    [InlineData(WorldSignalKind.MatchPoint, MatchEventKind.MatchPointReached, ReplayMarker.MatchPoint)]
    [InlineData(WorldSignalKind.OvertimeStarted, MatchEventKind.OvertimeStarted, ReplayMarker.Overtime)]
    public void ProtocolNineUsesOneSemanticMarkerWhileProtocolEightKeepsWorldCompatibility(
        WorldSignalKind worldKind, MatchEventKind semanticKind, ReplayMarker expected)
    {
        WorldEvent world = WorldMarkerEvent(worldKind);
        byte[] worldBytes = new byte[WorldEvent.Size];
        world.Write(worldBytes);
        var lowLevel = new NetApplicationEvent(1, ReliableEventType.WorldEvent, worldBytes);

        Assert.Equal(ReplayMarker.None, ReplayRecorder.MarkerFor(lowLevel, protocol: 9));
        Assert.Equal(expected, ReplayRecorder.MarkerFor(lowLevel, protocol: 8));

        MatchEvent semantic = SemanticMarkerEvent(semanticKind);
        MatchSemanticEventPacket packet = MatchSemanticEventPacketConversion.FromEvent(semantic);
        byte[] semanticBytes = new byte[MatchSemanticEventPacket.Size];
        packet.Write(semanticBytes);
        Assert.Equal(expected, ReplayRecorder.MarkerFor(new NetApplicationEvent(1,
            ReliableEventType.MatchSemantic, semanticBytes), protocol: 9));
    }

    [Fact]
    public void TerminalWorldMatchEndMarkerIsLegacyOnlyAndExactlyOnce()
    {
        var indexer = new ReplayEventIndexer();

        Assert.Equal(ReplayMarker.None,
            indexer.ForTerminalWorld(protocol: 9, terminal: true));
        Assert.Equal(ReplayMarker.MatchEnd,
            indexer.ForTerminalWorld(protocol: 8, terminal: true));
        Assert.Equal(ReplayMarker.None,
            indexer.ForTerminalWorld(protocol: 8, terminal: true));
    }

    [Fact]
    public void ReplayMarkerUsesRecordedGeneralSemanticFact()
    {
        MatchEvent value = new(8, 99, 1, 2, MatchEventKind.OvertimeStarted,
            CombatActor.None, CombatActor.None);
        MatchSemanticEventPacket packet = MatchSemanticEventPacketConversion.FromEvent(value);
        byte[] bytes = new byte[MatchSemanticEventPacket.Size];
        packet.Write(bytes);

        ReplayMarker marker = ReplayRecorder.MarkerFor(new NetApplicationEvent(1,
            ReliableEventType.MatchSemantic, bytes));

        Assert.Equal(ReplayMarker.Overtime, marker);
    }

    [Fact]
    public void AwardJournalDeduplicatesRawFacts()
    {
        MatchAward award = new(7, 8, 1, 2, 99, MatchAwardKind.Capture, Killer,
            CombatActor.None);
        var journal = new SemanticAwardJournal();
        Assert.True(journal.Record(award));
        Assert.False(journal.Record(award));
        Assert.Equal(award, journal[0]);
        Assert.Equal(1L, journal.DuplicateAwards);
    }

    [Fact]
    public void AwardJournalSeekResetReplaysSameFactsAndDedupWindow()
    {
        MatchAward first = new(1, 2, 1, 1, 10, MatchAwardKind.FirstHunt, Killer, CombatActor.None);
        MatchAward second = new(2, 3, 1, 1, 11, MatchAwardKind.Assist, KillerTwo, Victim);
        var journal = new SemanticAwardJournal();
        Assert.Equal(2, journal.ResetAndReplay(new[] { first, second }));
        Assert.Equal(2, journal.Count);
        Assert.Equal(2, journal.ResetAndReplay(new[] { first, first, second }));
        Assert.Equal(2, journal.Count);
    }

    [Fact]
    public void AwardJournalCheckpointRestoresDedupState()
    {
        MatchAward award = new(1, 2, 1, 1, 10, MatchAwardKind.FirstHunt, Killer, CombatActor.None);
        var source = new SemanticAwardJournal(); source.Record(award);
        using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) source.Write(writer);
        stream.Position = 0;
        var restored = new SemanticAwardJournal();
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true)) Assert.True(restored.Read(reader));
        Assert.False(restored.Record(award));
        Assert.Equal(1L, restored.DuplicateAwards);
    }

    [Fact]
    public void AwardJournalRetainsNewestFactsAndCheckpointOrderPastCapacity()
    {
        var source = new SemanticAwardJournal();
        var facts = new MatchAward[SemanticAwardJournal.Capacity + 44];
        for (uint id = 1; id <= facts.Length; id++)
        {
            facts[id - 1] = new(id, id + 1000, 1, 1, id, MatchAwardKind.Assist, Killer, CombatActor.None);
            Assert.True(source.Record(facts[id - 1]));
        }

        Assert.Equal(SemanticAwardJournal.Capacity, source.Count);
        Assert.Equal(44L, source.DroppedAwards);
        Assert.Equal(300L, source.Revision);
        Span<MatchAward> retained = stackalloc MatchAward[SemanticAwardJournal.Capacity];
        Assert.Equal(SemanticAwardJournal.Capacity, source.CopyTo(retained));
        Assert.Equal(facts[44], retained[0]);
        Assert.Equal(facts[^1], retained[^1]);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) source.Write(writer);
        stream.Position = 0;
        var restored = new SemanticAwardJournal();
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
            Assert.True(restored.Read(reader));
        Assert.Equal(source.Revision, restored.Revision);
        Assert.Equal(SemanticAwardJournal.Capacity, restored.CopyTo(retained));
        Assert.Equal(facts[44], retained[0]);
        Assert.Equal(facts[^1], retained[^1]);
        Assert.False(restored.Record(facts[^1]));
        Assert.Equal(1L, restored.DuplicateAwards);
    }

    [Fact]
    public void AwardEngineCountersExposeDroppedFactsWhenNoConsumer()
    {
        var dispatcher = new MatchEventDispatcher(); var engine = new AwardEngine(); dispatcher.Subscribe(engine);
        dispatcher.Dispatch(Kill(10, Killer, Victim));
        Assert.Equal(1L, engine.AwardsDropped);
        Assert.Equal(0L, engine.AwardsPublished);
    }

    private static List<MatchAward> Run(params MatchEvent[] events)
    {
        var dispatcher = new MatchEventDispatcher(); var engine = new AwardEngine();
        var awards = new List<MatchAward>(); engine.Awarded += awards.Add; dispatcher.Subscribe(engine);
        foreach (MatchEvent value in events) dispatcher.Dispatch(value);
        return awards;
    }

    private static MatchEvent Kill(uint tick, CombatActor killer, CombatActor victim,
        MatchEventFlags flags = MatchEventFlags.None)
        => KillForMatch(1, tick, killer, victim, flags);

    private static MatchEvent KillForMatch(uint matchId, uint tick, CombatActor killer, CombatActor victim,
        MatchEventFlags flags = MatchEventFlags.None)
        => new(0, tick, matchId, 1, MatchEventKind.PlayerKilled, killer, victim,
            Team: 1, Flags: flags);

    private static WorldEvent WorldMarkerEvent(WorldSignalKind kind)
        => kind switch
        {
            WorldSignalKind.FlagCaptured => new(1, 10, 1, 2, WorldSubjectKind.Flag,
                kind, 1, 44, Killer, Vector3.Zero),
            WorldSignalKind.NodeCaptured => new(1, 10, 1, 2, WorldSubjectKind.Node,
                kind, 1, 44, Killer, Vector3.Zero),
            WorldSignalKind.PrimeChanged => new(1, 10, 1, 2, WorldSubjectKind.Match,
                kind, 1, 0, Killer, Vector3.Zero),
            WorldSignalKind.MatchPoint => new(1, 10, 1, 2, WorldSubjectKind.Match,
                kind, 1, 0, CombatActor.None, Vector3.Zero),
            WorldSignalKind.OvertimeStarted => new(1, 10, 1, 2, WorldSubjectKind.Match,
                kind, 255, 0, CombatActor.None, Vector3.Zero, A: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static MatchEvent SemanticMarkerEvent(MatchEventKind kind)
        => kind switch
        {
            MatchEventKind.ObjectiveCaptured => new(1, 10, 1, 2, kind,
                Killer, CombatActor.None, EntityId: 44, Team: 1),
            MatchEventKind.NodeCaptured => new(1, 10, 1, 2, kind,
                Killer, CombatActor.None, EntityId: 44, Team: 1),
            MatchEventKind.PrimeChanged => new(1, 10, 1, 2, kind,
                Killer, CombatActor.None, Team: 1),
            MatchEventKind.MatchPointReached or MatchEventKind.OvertimeStarted => new(1, 10, 1, 2,
                kind, CombatActor.None, CombatActor.None, Team: kind == MatchEventKind.MatchPointReached ? (byte)1 : (byte)255),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
}
