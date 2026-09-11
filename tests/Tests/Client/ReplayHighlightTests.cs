using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class ReplayHighlightTests : IDisposable
{
    private static readonly CombatActor Actor = new(1, 101, 7);
    private static readonly CombatActor OtherActor = new(2, 202, 3);
    private static readonly CombatActor Victim = new(3, 303, 4);
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        $"project-prime-replay-highlights-{Guid.NewGuid():N}");

    public ReplayHighlightTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void OrdinaryKillUsesTimelineMappingDefaultWindowAndFullActorIdentity()
    {
        ReplayHighlightEvent kill = Event(500, 1060, 1, Actor,
            HighlightKind.Kill, ReplayMarker.Kill);
        ReplayHighlightTimelineAnchor timeline = new(100, 1000, 1);

        ReplayHighlight result = Assert.Single(new HighlightAnalyzer().Analyze(
            new[] { kill }, new[] { timeline }, durationFrames: 1000));

        Assert.Equal(0u, result.StartFrame);
        Assert.Equal(160u, result.FocusFrame);
        Assert.Equal(280u, result.EndFrame);
        Assert.Equal(1060u, result.AuthoritativeTick);
        Assert.Equal(Actor, result.Focus);
        Assert.Equal(20, result.Score);
        Assert.Equal("KILL", result.Label);
    }

    [Fact]
    public void HeadshotAndMultiKillSequenceMergesWithoutClippingEarlierKills()
    {
        ReplayHighlightEvent[] events =
        {
            Event(300, 300, 1, Actor, HighlightKind.Kill, ReplayMarker.Kill),
            Event(420, 420, 2, Actor, HighlightKind.Headshot,
                ReplayMarker.Kill | ReplayMarker.Headshot),
            Event(420, 420, 10, Actor, HighlightKind.DoubleKill,
                ReplayMarker.Award | ReplayMarker.MultiKill,
                ReplayHighlightSourceKind.MatchAward, sequenceId: 2),
            Event(570, 570, 3, Actor, HighlightKind.Kill, ReplayMarker.Kill),
            Event(570, 570, 11, Actor, HighlightKind.TripleKill,
                ReplayMarker.Award | ReplayMarker.MultiKill,
                ReplayHighlightSourceKind.MatchAward, sequenceId: 3)
        };

        ReplayHighlight result = Assert.Single(new HighlightAnalyzer().Analyze(
            events, Array.Empty<ReplayHighlightTimelineAnchor>(),
            durationFrames: 1000));

        Assert.Equal(60u, result.StartFrame);
        Assert.Equal(570u, result.FocusFrame);
        Assert.Equal(690u, result.EndFrame);
        Assert.Equal(HighlightKind.TripleKill, result.Kind);
        Assert.Equal(Actor, result.Focus);
        Assert.True((result.Markers & ReplayMarker.Headshot) != 0);
        Assert.Equal("TRIPLE KILL", result.Label);
        Assert.True(result.Score > HighlightScoringPolicy.BaseScore(
            HighlightKind.TripleKill));
    }

    [Fact]
    public void OvertimeCaptureUsesRecordedContextWithoutInventingWinner()
    {
        ReplayHighlightEvent overtime = Event(500, 500, 1, CombatActor.None,
            HighlightKind.Overtime, ReplayMarker.Overtime,
            ReplayHighlightSourceKind.MatchSemantic);
        ReplayHighlightEvent capture = Event(520, 520, 2, Actor,
            HighlightKind.ObjectiveCapture, ReplayMarker.FlagCapture,
            ReplayHighlightSourceKind.MatchSemantic);

        ReplayHighlight result = Assert.Single(new HighlightAnalyzer().Analyze(
            new[] { overtime, capture },
            Array.Empty<ReplayHighlightTimelineAnchor>(), 1000));

        Assert.Equal(HighlightKind.ObjectiveCapture, result.Kind);
        Assert.Equal("OVERTIME CAPTURE", result.Label);
        Assert.Equal(Actor, result.Focus);
        Assert.Equal(150, result.Score);
    }

    [Fact]
    public void SemanticKindsHaveStableLabelsAndNoSyntheticMatchWinner()
    {
        ReplayHighlightEvent[] events =
        {
            Event(500, 500, 1, Actor, HighlightKind.ObjectiveCapture,
                ReplayMarker.FlagCapture, ReplayHighlightSourceKind.MatchSemantic),
            Event(1000, 1000, 2, Actor, HighlightKind.NodeCapture,
                ReplayMarker.NodeCapture, ReplayHighlightSourceKind.MatchSemantic),
            Event(1500, 1500, 3, Actor, HighlightKind.PrimeChange,
                ReplayMarker.PrimeChange, ReplayHighlightSourceKind.MatchSemantic),
            Event(2000, 2000, 4, CombatActor.None, HighlightKind.MatchPoint,
                ReplayMarker.MatchPoint, ReplayHighlightSourceKind.MatchSemantic),
            Event(2500, 2500, 5, CombatActor.None, HighlightKind.Overtime,
                ReplayMarker.Overtime, ReplayHighlightSourceKind.MatchSemantic),
            Event(3000, 3000, 6, CombatActor.None, HighlightKind.MatchEnd,
                ReplayMarker.MatchEnd, ReplayHighlightSourceKind.MatchSemantic)
        };

        IReadOnlyList<ReplayHighlight> results = new HighlightAnalyzer().Analyze(
            events, Array.Empty<ReplayHighlightTimelineAnchor>(), 3500);

        Assert.Equal(6, results.Count);
        Assert.Contains(results, value => value.Label == "OBJECTIVE CAPTURE");
        Assert.Contains(results, value => value.Label == "NODE CAPTURE");
        Assert.Contains(results, value => value.Label == "PRIME CHANGE");
        Assert.Contains(results, value => value.Label == "MATCH POINT");
        Assert.Contains(results, value => value.Label == "OVERTIME");
        ReplayHighlight ending = Assert.Single(results, value =>
            value.Kind == HighlightKind.MatchEnd);
        Assert.Equal(CombatActor.None, ending.Focus);
        Assert.Equal("MATCH END", ending.Label);
    }

    [Fact]
    public void OverlapDoesNotMergeSlotReuseOrDifferentActors()
    {
        CombatActor reused = Actor with { ConnectionId = 999, Life = 1 };
        ReplayHighlightEvent[] events =
        {
            Event(500, 500, 1, Actor, HighlightKind.Kill, ReplayMarker.Kill),
            Event(520, 520, 2, OtherActor, HighlightKind.Kill, ReplayMarker.Kill),
            Event(540, 540, 3, reused, HighlightKind.Kill, ReplayMarker.Kill)
        };

        IReadOnlyList<ReplayHighlight> results = new HighlightAnalyzer().Analyze(
            events, Array.Empty<ReplayHighlightTimelineAnchor>(), 1000);

        Assert.Equal(3, results.Count);
        Assert.Contains(results, result => result.Focus == Actor);
        Assert.Contains(results, result => result.Focus == OtherActor);
        Assert.Contains(results, result => result.Focus == reused);
    }

    [Fact]
    public void SelectionIsDeterministicDeduplicatedAndBoundedToEight()
    {
        ReplayHighlightEvent[] ordered = Enumerable.Range(0, 12).Select(index =>
            Event((uint)(300 + index * 500), (uint)(300 + index * 500),
                (uint)(index + 1), new CombatActor((byte)(index % 8),
                    (ulong)(1000 + index), 1), HighlightKind.Kill,
                ReplayMarker.Kill)).ToArray();
        ReplayHighlightEvent[] reversedWithDuplicate = ordered.Reverse()
            .Append(ordered[0]).ToArray();
        var analyzer = new HighlightAnalyzer();

        ReplayHighlight[] first = analyzer.Analyze(ordered,
            Array.Empty<ReplayHighlightTimelineAnchor>(), 7000).ToArray();
        ReplayHighlight[] second = analyzer.Analyze(reversedWithDuplicate,
            Array.Empty<ReplayHighlightTimelineAnchor>(), 7000).ToArray();

        Assert.Equal(HighlightAnalyzer.MaximumHighlights, first.Length);
        Assert.Equal(first, second);
        Assert.Equal(ordered.Take(8).Select(value => value.Focus),
            first.Select(value => value.Focus));
    }

    [Fact]
    public void EventIdsAreDeduplicatedWithinTheirMatchNotAcrossMatches()
    {
        ReplayHighlightEvent[] events =
        {
            new(300, 300, matchId: 1, eventId: 7, sequenceId: 7, Actor,
                HighlightKind.Kill, ReplayMarker.Kill,
                ReplayHighlightSourceKind.Kill),
            new(900, 900, matchId: 2, eventId: 7, sequenceId: 7, OtherActor,
                HighlightKind.Kill, ReplayMarker.Kill,
                ReplayHighlightSourceKind.Kill)
        };

        IReadOnlyList<ReplayHighlight> results = new HighlightAnalyzer().Analyze(
            events, Array.Empty<ReplayHighlightTimelineAnchor>(), 1200);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, value => value.Focus == Actor);
        Assert.Contains(results, value => value.Focus == OtherActor);
    }

    [Fact]
    public void GeneratedIntervalsRemainDeterministicBoundedAndIdentitySafe()
    {
        const uint duration = 720;
        // The first two windows overlap at the start and merge by their exact
        // actor. The frame-360 and frame-720 windows are adjacent to their
        // neighbors but exceed the combat linkage age, so they remain distinct.
        // The two players at frame 120 deliberately reuse a slot while changing
        // connection/life and must never be merged.
        CombatActor reused = Actor with { ConnectionId = 404, Life = 8 };
        ReplayHighlightEvent[] generated =
        {
            Event(0, 0, 1, Actor, HighlightKind.Kill, ReplayMarker.Kill),
            Event(10, 10, 2, Actor, HighlightKind.Headshot,
                ReplayMarker.Kill | ReplayMarker.Headshot),
            Event(120, 120, 3, OtherActor, HighlightKind.Kill, ReplayMarker.Kill),
            Event(120, 120, 4, reused, HighlightKind.Kill, ReplayMarker.Kill),
            Event(360, 360, 5, Victim, HighlightKind.Kill, ReplayMarker.Kill),
            Event(719, 719, 6, new CombatActor(4, 404, 9),
                HighlightKind.ObjectiveCapture, ReplayMarker.FlagCapture),
            Event(720, 720, 7, new CombatActor(5, 505, 1),
                HighlightKind.MatchEnd, ReplayMarker.MatchEnd)
        };
        var analyzer = new HighlightAnalyzer();
        ReplayHighlight[] expected = analyzer.Analyze(generated,
            Array.Empty<ReplayHighlightTimelineAnchor>(), duration).ToArray();

        Assert.Contains(expected, value => value.Focus == Actor);
        Assert.Contains(expected, value => value.Focus == reused);
        Assert.Contains(expected, value => value.Focus == new CombatActor(4, 404, 9));
        Assert.Contains(expected, value => value.Focus == new CombatActor(5, 505, 1));
        Assert.All(expected, value =>
        {
            Assert.InRange(value.StartFrame, 0u, duration);
            Assert.InRange(value.FocusFrame, 0u, duration);
            Assert.InRange(value.EndFrame, 0u, duration);
            Assert.True(value.StartFrame <= value.FocusFrame);
            Assert.True(value.FocusFrame <= value.EndFrame);
        });

        var random = new Random(0x51A7);
        for (int iteration = 0; iteration < 64; iteration++)
        {
            List<ReplayHighlightEvent> shuffled = generated.ToList();
            for (int index = shuffled.Count - 1; index > 0; index--)
            {
                int other = random.Next(index + 1);
                (shuffled[index], shuffled[other]) = (shuffled[other], shuffled[index]);
            }
            // Exact duplicate records are a normal consequence of a replay
            // retry/merge and must not alter deterministic output.
            shuffled.Add(generated[iteration % generated.Length]);
            ReplayHighlight[] actual = analyzer.Analyze(shuffled,
                Array.Empty<ReplayHighlightTimelineAnchor>(), duration).ToArray();
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task ConcurrentMetadataRequestsPublishOneValidCache()
    {
        string replay = Path.Combine(_root, "concurrent.fpreplay");
        WriteReplay(replay, includeAwardAndSemantic: true);
        var services = Enumerable.Range(0, 8)
            .Select(_ => new ReplayHighlightMetadataService(_root)).ToArray();

        ReplayHighlightMetadata[] results = await Task.WhenAll(services.Select(service =>
            Task.Run(() => service.Get(replay))));

        Assert.All(results, result => Assert.True(result.IsAvailable));
        Assert.All(results, result => Assert.Equal(results[0].Highlights,
            result.Highlights));
        Assert.Single(Directory.EnumerateFiles(services[0].CacheDirectory,
            "*.json", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(services[0].CacheDirectory,
            "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void OversizedAndUnavailableCacheAreFailSoftAndDoNotRewriteReplay()
    {
        string replay = Path.Combine(_root, "cache-faults.fpreplay");
        WriteReplay(replay, includeAwardAndSemantic: false);
        byte[] originalReplay = File.ReadAllBytes(replay);
        var service = new ReplayHighlightMetadataService(_root);
        ReplayHighlightMetadata first = service.Get(replay);
        string cachePath = service.CachePath(first.ReplayFingerprint);

        File.WriteAllBytes(cachePath,
            new byte[ReplayHighlightMetadataService.MaximumCacheBytes + 1]);
        ReplayHighlightMetadata oversized = service.Get(replay);
        Assert.True(oversized.IsAvailable);
        Assert.False(oversized.FromCache);
        Assert.True(new FileInfo(cachePath).Length
            <= ReplayHighlightMetadataService.MaximumCacheBytes);

        File.Delete(cachePath);
        // A directory at the final name simulates a read/write interruption
        // without relying on platform-specific chmod behavior. The metadata
        // result remains usable even though the cache cannot be replaced.
        Directory.CreateDirectory(cachePath);
        ReplayHighlightMetadata unavailable = service.Get(replay);
        Assert.True(unavailable.IsAvailable);
        Assert.False(unavailable.FromCache);
        Assert.True(Directory.Exists(cachePath));
        Assert.Equal(originalReplay, File.ReadAllBytes(replay));
    }

    [Theory]
    [InlineData(HighlightKind.Headshot,
        ReplayMarker.Kill | ReplayMarker.Headshot, 10)]
    [InlineData(HighlightKind.DoubleKill,
        ReplayMarker.Kill | ReplayMarker.Headshot, 20)]
    [InlineData(HighlightKind.TripleKill, ReplayMarker.FlagCapture, 20)]
    [InlineData(HighlightKind.Kill,
        ReplayMarker.Kill | ReplayMarker.MatchPoint, 10)]
    [InlineData(HighlightKind.ObjectiveCapture,
        ReplayMarker.FlagCapture | ReplayMarker.Overtime, 15)]
    [InlineData(HighlightKind.PrimeSlayer, ReplayMarker.MatchEnd, 15)]
    public void ContextBonusesAreExplicitAndDeterministic(HighlightKind kind,
        ReplayMarker markers, int expected)
    {
        Assert.Equal(expected, HighlightScoringPolicy.ContextBonus(markers, kind));
    }

    [Fact]
    public void MetadataServiceDecodesAllAuthoritativeFactKindsAndUsesCache()
    {
        string replay = Path.Combine(_root, "facts.fpreplay");
        WriteReplay(replay, includeAwardAndSemantic: true);
        byte[] immutableReplay = File.ReadAllBytes(replay);
        var service = new ReplayHighlightMetadataService(_root);

        ReplayHighlightMetadata first = service.Get(replay);
        ReplayHighlightMetadata second = service.Get(replay);

        Assert.True(first.IsAvailable);
        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(first.Highlights, second.Highlights);
        Assert.Contains(first.Highlights, value => value.Kind
            == HighlightKind.DoubleKill);
        Assert.Contains(first.Highlights, value => value.Kind
            == HighlightKind.NodeCapture);
        Assert.All(first.Highlights.Where(value => value.Focus.IsValid),
            value => Assert.True(value.Focus.ConnectionId != 0
                && value.Focus.Life != 0));
        Assert.True(File.Exists(service.CachePath(first.ReplayFingerprint)));
        Assert.Equal(immutableReplay, File.ReadAllBytes(replay));
    }

    [Fact]
    public void ProtocolNineAwardsRemainHighlightCandidates()
    {
        string replay = Path.Combine(_root, "protocol-nine.fpreplay");
        WriteReplay(replay, includeAwardAndSemantic: true, protocolVersion: 9);

        ReplayHighlightMetadata metadata = new ReplayHighlightMetadataService(_root)
            .Get(replay);

        Assert.True(metadata.IsAvailable);
        Assert.Contains(metadata.Highlights, value => value.Kind
            == HighlightKind.DoubleKill);
    }

    [Fact]
    public void CacheCorruptionOldVersionAndReplayChangeRegenerateDeterministically()
    {
        string replay = Path.Combine(_root, "changing.fpreplay");
        WriteReplay(replay, includeAwardAndSemantic: false);
        var service = new ReplayHighlightMetadataService(_root);
        ReplayHighlightMetadata first = service.Get(replay);
        string firstCache = service.CachePath(first.ReplayFingerprint);
        byte[] canonical = File.ReadAllBytes(firstCache);

        string old = System.Text.Encoding.UTF8.GetString(canonical)
            .Replace("\"analyzerVersion\":1", "\"analyzerVersion\":0",
                StringComparison.Ordinal);
        File.WriteAllText(firstCache, old);
        ReplayHighlightMetadata oldVersion = service.Get(replay);
        Assert.False(oldVersion.FromCache);
        Assert.Equal(first.Highlights, oldVersion.Highlights);

        File.WriteAllText(firstCache, "{ broken");
        ReplayHighlightMetadata corrupt = service.Get(replay);
        Assert.False(corrupt.FromCache);
        Assert.Equal(first.Highlights, corrupt.Highlights);
        Assert.Equal(canonical, File.ReadAllBytes(firstCache));

        string outsideDuration = System.Text.Encoding.UTF8.GetString(canonical)
            .Replace("\"endFrame\":280", "\"endFrame\":999999",
                StringComparison.Ordinal);
        Assert.NotEqual(System.Text.Encoding.UTF8.GetString(canonical),
            outsideDuration);
        File.WriteAllText(firstCache, outsideDuration);
        ReplayHighlightMetadata invalidBounds = service.Get(replay);
        Assert.False(invalidBounds.FromCache);
        Assert.Equal(first.Highlights, invalidBounds.Highlights);
        Assert.Equal(canonical, File.ReadAllBytes(firstCache));

        File.Delete(firstCache);
        ReplayHighlightMetadata regenerated = service.Get(replay);
        Assert.False(regenerated.FromCache);
        Assert.Equal(canonical, File.ReadAllBytes(firstCache));
        Assert.Empty(Directory.EnumerateFiles(service.CacheDirectory, "*.tmp"));

        File.Delete(replay);
        WriteReplay(replay, includeAwardAndSemantic: false, killTick: 1080);
        ReplayHighlightMetadata changed = service.Get(replay);
        Assert.NotEqual(first.ReplayFingerprint, changed.ReplayFingerprint);
        Assert.False(changed.FromCache);
    }

    [Fact]
    public void CorruptAndOldReplayFailSoftWithoutCacheOrHighlights()
    {
        string corrupt = Path.Combine(_root, "corrupt.fpreplay");
        File.WriteAllBytes(corrupt, new byte[] { 1, 2, 3, 4, 5, 6 });
        string old = Path.Combine(_root, "old.fpreplay");
        using (new ReplayWriter(old, protocolVersion: 7)) { }
        var service = new ReplayHighlightMetadataService(_root);

        ReplayHighlightMetadata bad = service.Get(corrupt);
        ReplayHighlightMetadata unsupported = service.Get(old);

        Assert.Equal(ReplayHighlightMetadataStatus.InvalidReplay, bad.Status);
        Assert.Empty(bad.Highlights);
        Assert.Equal(ReplayHighlightMetadataStatus.UnsupportedReplay,
            unsupported.Status);
        Assert.Empty(unsupported.Highlights);
    }

    private static ReplayHighlightEvent Event(uint frame, uint tick, uint id,
        CombatActor focus, HighlightKind kind, ReplayMarker markers,
        ReplayHighlightSourceKind source = ReplayHighlightSourceKind.Kill,
        uint? sequenceId = null)
        => new(frame, tick, matchId: 1, id, sequenceId ?? id, focus, kind,
            markers, source);

    private static void WriteReplay(string path, bool includeAwardAndSemantic,
        uint killTick = 1060, byte protocolVersion = 10)
    {
        using var writer = new ReplayWriter(path, protocolVersion);
        var match = new MatchTransitionPacket(1, 1000, GameMode.Battle, "unit1");
        byte[] matchBytes = new byte[MatchTransitionPacket.Size];
        match.Write(matchBytes);
        writer.WriteRecord(100, Record(ReplayRecordKind.Match, matchBytes));

        var kill = new KillEvent(10, killTick, 1, 1, Actor, Victim, 0,
            KillEventFlags.Headshot, ImmutableArray<CombatActor>.Empty);
        byte[] killBytes = new byte[KillEvent.Size];
        kill.Write(killBytes);
        writer.WriteRecord(500, EventRecord(ReliableEventType.Kill, killBytes));
        if (!includeAwardAndSemantic) return;

        MatchAward award = new(20, 10, 1, 1, killTick,
            MatchAwardKind.DoubleKill, Actor, Victim, Count: 2);
        MatchAwardPacket awardPacket = MatchAwardPacketConversion.FromAward(award);
        byte[] awardBytes = new byte[MatchAwardPacket.Size];
        awardPacket.Write(awardBytes);
        writer.WriteRecord(500,
            EventRecord(ReliableEventType.MatchAward, awardBytes));

        MatchEvent semantic = new(30, 1700, 1, 1,
            MatchEventKind.NodeCaptured, OtherActor, CombatActor.None,
            EntityId: 44, Team: 1);
        MatchSemanticEventPacket semanticPacket
            = MatchSemanticEventPacketConversion.FromEvent(semantic);
        byte[] semanticBytes = new byte[MatchSemanticEventPacket.Size];
        semanticPacket.Write(semanticBytes);
        writer.WriteRecord(800,
            EventRecord(ReliableEventType.MatchSemantic, semanticBytes));
    }

    private static byte[] EventRecord(ReliableEventType type,
        ReadOnlySpan<byte> payload)
    {
        byte[] body = new byte[5 + payload.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body, 1);
        body[4] = (byte)type;
        payload.CopyTo(body.AsSpan(5));
        return Record(ReplayRecordKind.Event, body);
    }

    private static byte[] Record(ReplayRecordKind kind, ReadOnlySpan<byte> body)
    {
        byte[] data = new byte[1 + body.Length];
        data[0] = (byte)kind;
        body.CopyTo(data.AsSpan(1));
        return data;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
