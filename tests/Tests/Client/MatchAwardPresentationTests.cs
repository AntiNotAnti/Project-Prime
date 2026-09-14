using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods.Audio;
using MphRead.Mods.Hud;
using MphRead.Mods.Network;
using MphRead.Replay;
using MphRead.Runtime.Content;
using MphRead.Sound;
using Xunit;

namespace MphRead.Tests;

public sealed class MatchAwardPresentationTests
{
    private static readonly CombatActor Subject = new(1, 101, 4);
    private static readonly CombatActor Target = new(2, 202, 8);

    [Theory]
    [InlineData(false, 54)]
    [InlineData(true, 42)]
    public void AwardBannerUsesSeparateLaneWhileDeathRecapIsVisible(
        bool deathRecapVisible, int expectedY)
    {
        Assert.Equal(expectedY, PlayerPresentation.AwardBannerY(deathRecapVisible));
    }

    [Fact]
    public void AnnouncerSuppressesDuplicateReliableAward()
    {
        MatchAward award = Award(1, MatchAwardKind.FirstHunt, 10);
        var service = new AnnouncerService();
        Assert.True(service.Consume(award));
        Assert.False(service.Consume(award));
        Assert.Equal(1, service.QueuedCount);
        Assert.Equal(1L, service.DuplicateAwards);
    }

    [Fact]
    public void HudSuppressesDuplicateReliableAward()
    {
        MatchAward award = Award(1, MatchAwardKind.Capture, 10);
        var queue = new AwardHudQueue();
        Assert.True(queue.Enqueue(award));
        Assert.False(queue.Enqueue(award));
        Assert.Equal(1, queue.Count);
        Assert.Equal(1L, queue.DuplicateAwards);
    }

    [Fact]
    public void ReliableChannelDeliversAwardEventOnlyOncePerEventId()
    {
        var channel = new ReliableChannel();
        Assert.True(channel.TryEnqueue(ReliableEventType.MatchAward, new byte[MatchAwardPacket.Size], out uint id));
        Assert.True(channel.TryGetDue(0, out uint dueId, out ReliableEventType type, out _));
        Assert.Equal(id, dueId);
        Assert.Equal(ReliableEventType.MatchAward, type);
        var receiver = new ReliableChannel();
        Assert.True(receiver.Receive(id));
        Assert.False(receiver.Receive(id));
    }

    [Fact]
    public void TripleKillSupersedesQueuedDoubleKillForSameSubject()
    {
        var queue = new AwardHudQueue();
        Assert.True(queue.Enqueue(Award(1, MatchAwardKind.DoubleKill, 10, count: 2)));
        Assert.True(queue.Enqueue(Award(2, MatchAwardKind.TripleKill, 11, count: 3)));
        Assert.Equal(1, queue.Count);
        Assert.Equal(1L, queue.SupersededAwards);
        Assert.True(queue.TryDequeue(out MatchAward value));
        Assert.Equal(MatchAwardKind.TripleKill, value.Kind);
    }

    [Fact]
    public void AnnouncerCooldownUsesTickWrapSafeAge()
    {
        var service = new AnnouncerService();
        Assert.True(service.Consume(Award(1, MatchAwardKind.FirstHunt, uint.MaxValue - 5)));
        Assert.False(service.Consume(Award(2, MatchAwardKind.Assist, 3)));
        Assert.Equal(1L, service.DroppedCues);
    }

    [Fact]
    public void AnnouncerPriorityKeepsTripleOverLowerAwards()
    {
        var service = new AnnouncerService();
        Assert.True(service.Consume(Award(1, MatchAwardKind.Assist, 1)));
        Assert.True(service.Consume(Award(2, MatchAwardKind.TripleKill, 30)));
        Assert.True(service.TryDequeue(out AnnouncerCue cue));
        Assert.Equal(AnnouncerEvent.TripleKill, cue.Event);
    }

    [Fact]
    public void OptionalManifestMappingOverridesBuiltInAsset()
    {
        OptionalPresentationManifest manifest = new(1, "voice", "1", new string('a', 64),
            OptionalPresentationKind.Announcer,
            new[] { new ContentFileEntry("first.wav", 1, new string('0', 64)) },
            new[] { new OptionalPresentationEvent("firstHunt", "first.wav") });
        var pack = AnnouncerPack.FromOptionalPack(new InstalledOptionalPresentationPack(manifest, "/packs/voice"));
        Assert.Equal("first.wav", pack.Resolve(AnnouncerEvent.FirstHunt));
        Assert.Equal("builtin:assist", pack.Resolve(AnnouncerEvent.Assist));
    }

    [Fact]
    public void OptionalManifestAcceptsQz6MaximumStableId()
    {
        string stableId = new('v', ContentManifestLimits.MaximumStableIdLength);
        OptionalPresentationManifest manifest = new(1, stableId, "1", new string('a', 64),
            OptionalPresentationKind.Announcer,
            new[] { new ContentFileEntry("first.wav", 1, new string('0', 64)) },
            new[] { new OptionalPresentationEvent("firstHunt", "first.wav") });

        var pack = AnnouncerPack.FromOptionalPack(new InstalledOptionalPresentationPack(manifest, "/packs/voice"));

        Assert.Equal(stableId, pack.Name);
        Assert.Equal("first.wav", pack.Resolve(AnnouncerEvent.FirstHunt));
    }

    [Fact]
    public void MissingOptionalManifestFallsBackToBuiltIn()
    {
        AnnouncerService service = AnnouncerService.FromOptionalPack(null);
        Assert.True(service.Consume(Award(1, MatchAwardKind.Assist, 10)));
        Assert.True(service.TryDequeue(out AnnouncerCue cue));
        Assert.Equal("builtin:assist", cue.AssetKey);
    }

    [Fact]
    public void ProductionAudioConsumerDrainsBuiltInCueIntoExistingAudioRequests()
    {
        var service = new AnnouncerService();
        Assert.True(service.Consume(Award(1, MatchAwardKind.DoubleKill, 10, count: 2)));
        var requests = new AudioRequests();
        var emitted = new List<AudioRequest>();
        requests.Requested += emitted.Add;
        using var audio = new AnnouncerAudioPresentation(requests, service, null, new FakeFilePlayer());

        Assert.True(audio.PresentNext());
        Assert.False(audio.PresentNext());
        AudioRequest request = Assert.Single(emitted);
        Assert.Equal(AudioRequestKind.QueueStream, request.Kind);
        Assert.Equal((int)VoiceId.VOICE_CONSECUTIVE_KILLS, request.Id);
        Assert.Equal(1L, audio.CuesConsumed);
        Assert.Equal(1L, audio.BuiltInsPlayed);
    }

    [Fact]
    public void OptionalAudioFailureFallsBackToBuiltInWithoutDroppingSemanticCue()
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-announcer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "first.wav"), [1]);
        try
        {
            InstalledOptionalPresentationPack installed = OptionalPack(root);
            var service = AnnouncerService.FromOptionalPack(installed);
            Assert.True(service.Consume(Award(1, MatchAwardKind.FirstHunt, 10)));
            var requests = new AudioRequests();
            var emitted = new List<AudioRequest>();
            requests.Requested += emitted.Add;
            var files = new FakeFilePlayer { Result = false };
            using var audio = new AnnouncerAudioPresentation(requests, service,
                new MphRead.Mods.Content.OptionalPresentationAssetResolver(installed), files);

            Assert.True(audio.PresentNext());
            CompleteOptionalAudio(audio);
            Assert.Equal(Path.Combine(root, "first.wav"), files.LastPath);
            Assert.Equal(SfxId.POWER_UP1, (SfxId)Assert.Single(emitted).Id);
            Assert.Equal(1L, audio.OptionalFallbacks);
            Assert.Equal(1L, audio.BuiltInsPlayed);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ResolvedOptionalAudioUsesSingleBoundedFilePlayerInsteadOfBuiltIn()
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-announcer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "first.wav"), [1]);
        try
        {
            InstalledOptionalPresentationPack installed = OptionalPack(root);
            var service = AnnouncerService.FromOptionalPack(installed);
            Assert.True(service.Consume(Award(1, MatchAwardKind.FirstHunt, 10)));
            var requests = new AudioRequests();
            var emitted = new List<AudioRequest>();
            requests.Requested += emitted.Add;
            var files = new FakeFilePlayer { Result = true };
            using var audio = new AnnouncerAudioPresentation(requests, service,
                new MphRead.Mods.Content.OptionalPresentationAssetResolver(installed), files);

            Assert.True(audio.PresentNext());
            CompleteOptionalAudio(audio);
            Assert.Empty(emitted);
            Assert.Equal(1L, audio.OptionalFilesPlayed);
            Assert.Equal(0L, audio.BuiltInsPlayed);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void SameSizeOptionalAudioReplacementFailsClosedToBuiltIn()
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-announcer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "first.wav");
        File.WriteAllBytes(path, [1]);
        try
        {
            InstalledOptionalPresentationPack installed = OptionalPack(root);
            File.WriteAllBytes(path, [2]);
            var service = AnnouncerService.FromOptionalPack(installed);
            Assert.True(service.Consume(Award(1, MatchAwardKind.FirstHunt, 10)));
            var requests = new AudioRequests();
            var emitted = new List<AudioRequest>();
            requests.Requested += emitted.Add;
            var files = new FakeFilePlayer { Result = true };
            using var audio = new AnnouncerAudioPresentation(requests, service,
                new MphRead.Mods.Content.OptionalPresentationAssetResolver(installed), files);

            Assert.True(audio.PresentNext());
            CompleteOptionalAudio(audio);
            Assert.Null(files.LastPath);
            Assert.Equal(SfxId.POWER_UP1, (SfxId)Assert.Single(emitted).Id);
            Assert.Equal(1L, audio.OptionalFallbacks);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ReplayRetainsNewestAwardsForPresentationPastJournalCapacity()
    {
        var state = new ModernReplayState();
        state.Reset(9);
        Assert.True(state.Receive(ReplayPlaybackTests.Match(1)));
        for (uint id = 1; id <= SemanticAwardJournal.Capacity + 44; id++)
        {
            MatchAwardKind kind = id == SemanticAwardJournal.Capacity + 44
                ? MatchAwardKind.TripleKill : MatchAwardKind.Assist;
            byte count = kind == MatchAwardKind.TripleKill ? (byte)3 : (byte)1;
            MatchAward award = new(id, id + 1000, 1, 1, id * 30, kind, Subject, CombatActor.None, count);
            MatchAwardPacket packet = MatchAwardPacketConversion.FromAward(award);
            byte[] body = new byte[5 + MatchAwardPacket.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(body, 1);
            body[4] = (byte)ReliableEventType.MatchAward;
            packet.Write(body.AsSpan(5));
            Assert.True(state.Receive(ReplayPlaybackTests.Record(ReplayRecordKind.Event, body)));
        }

        var announcer = new AnnouncerService();
        var hud = new AwardHudQueue();
        Assert.Equal(SemanticAwardJournal.Capacity, state.ApplyPendingAwards(announcer, hud));
        Assert.Equal(0, state.ApplyPendingAwards(announcer, hud));
        Assert.True(announcer.TryDequeue(out AnnouncerCue cue));
        Assert.Equal((uint)(SemanticAwardJournal.Capacity + 44), cue.AwardId);
        Assert.Equal(AnnouncerEvent.TripleKill, cue.Event);
        Assert.True(hud.TryDequeue(out MatchAward presented));
        Assert.Equal((uint)(SemanticAwardJournal.Capacity + 44), presented.AwardId);
        Assert.Equal(MatchAwardKind.TripleKill, presented.Kind);
    }

    [Fact]
    public void ReplayDeliversRecordedSemanticCueWithoutReinterpretingLowLevelEvents()
    {
        var state = new ModernReplayState();
        state.Reset(9);
        Assert.True(state.Receive(ReplayPlaybackTests.Match(1)));
        MatchEvent value = new(17, 20, 1, 2, MatchEventKind.OvertimeStarted,
            CombatActor.None, CombatActor.None);
        MatchSemanticEventPacket packet = MatchSemanticEventPacketConversion.FromEvent(value);
        byte[] body = new byte[5 + MatchSemanticEventPacket.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(body, 1);
        body[4] = (byte)ReliableEventType.MatchSemantic;
        packet.Write(body.AsSpan(5));
        Assert.True(state.Receive(ReplayPlaybackTests.Record(ReplayRecordKind.Event, body)));
        var announcer = new AnnouncerService();

        Assert.Equal(1, state.ApplyPendingSemanticEvents(announcer, 255));
        Assert.Equal(0, state.ApplyPendingSemanticEvents(announcer, 255));
        Assert.True(announcer.TryDequeue(out AnnouncerCue cue));
        Assert.Equal(AnnouncerEvent.Overtime, cue.Event);
    }

    [Fact]
    public void BareMatchEndedDoesNotGuessVictory()
    {
        var service = new AnnouncerService();
        MatchEvent ended = new(1, 10, 1, 1, MatchEventKind.MatchEnded,
            CombatActor.None, CombatActor.None, Team: 255);
        Assert.False(service.Consume(ended));
        Assert.False(service.Consume(ended, 0));
    }

    [Fact]
    public void WinnerAwareMatchEndedChoosesVictoryOrDefeat()
    {
        var service = new AnnouncerService();
        MatchEvent ended = new(1, 10, 1, 1, MatchEventKind.MatchEnded,
            CombatActor.None, CombatActor.None, Team: 1);
        Assert.True(service.Consume(ended, 1));
        Assert.True(service.TryDequeue(out AnnouncerCue victory));
        Assert.Equal(AnnouncerEvent.Victory, victory.Event);
        service.Reset();
        Assert.True(service.Consume(ended, 0));
        Assert.True(service.TryDequeue(out AnnouncerCue defeat));
        Assert.Equal(AnnouncerEvent.Defeat, defeat.Event);
    }

    [Fact]
    public void MatchStartedUsesAuthoritativeGoCue()
    {
        var service = new AnnouncerService();
        MatchEvent started = new(1, 180, 1, 2, MatchEventKind.MatchStarted,
            CombatActor.None, CombatActor.None);
        Assert.True(service.Consume(started));
        Assert.True(service.TryDequeue(out AnnouncerCue cue));
        Assert.Equal(AnnouncerEvent.Go, cue.Event);
    }

    [Fact]
    public void HudQueueIsBoundedUnderBurst()
    {
        var queue = new AwardHudQueue();
        for (uint i = 1; i <= AwardHudQueue.Capacity + 4; i++)
            queue.Enqueue(Award(i, MatchAwardKind.Assist, i));
        Assert.Equal(AwardHudQueue.Capacity, queue.Count);
        Assert.True(queue.DroppedAwards > 0);
    }

    private static MatchAward Award(uint id, MatchAwardKind kind, uint tick, byte count = 1)
        => new(id, id + 100, 1, 1, tick, kind, Subject,
            kind is MatchAwardKind.Assist or MatchAwardKind.Interceptor ? Target : CombatActor.None, count);

    private static InstalledOptionalPresentationPack OptionalPack(string root)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "first.wav"));
        OptionalPresentationManifest manifest = new(1, "voice", "1", new string('a', 64),
            OptionalPresentationKind.Announcer,
            [new ContentFileEntry("first.wav", bytes.Length,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)))],
            [new OptionalPresentationEvent("firstHunt", "first.wav")]);
        return new(manifest, root);
    }

    private static void CompleteOptionalAudio(AnnouncerAudioPresentation audio)
    {
        Assert.True(SpinWait.SpinUntil(() =>
        {
            audio.PresentNext();
            return audio.OptionalFilesPlayed + audio.OptionalFallbacks > 0;
        }, TimeSpan.FromSeconds(5)));
    }

    private sealed class FakeFilePlayer : IAnnouncerFilePlayer
    {
        public bool Result { get; init; }
        public string? LastPath { get; private set; }
        public bool TryPlay(FileStream stream, float volume)
        {
            LastPath = stream.Name;
            stream.Dispose();
            return Result;
        }
        public void Stop() { }
        public void Dispose() { }
    }
}
