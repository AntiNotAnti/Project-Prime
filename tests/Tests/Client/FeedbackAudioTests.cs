using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Combat;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Sound;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Client;

[Collection("Match baseline globals")]
public sealed class FeedbackAudioTests
{
    [Fact]
    public void DistinctCuesEmitTheirMappedSounds()
    {
        WithHeadlessScene((scene, requests) =>
        {
            var audio = new FeedbackAudio(scene);
            FeedbackCue[] cues =
            {
                FeedbackCue.Hit, FeedbackCue.Kill, FeedbackCue.Pickup,
                FeedbackCue.ObjectiveScored, FeedbackCue.Overtime, FeedbackCue.MatchPoint
            };

            for (int i = 0; i < cues.Length; i++)
                Assert.True(audio.Play(cues[i], (uint)(100 + i * 100)));

            Assert.Equal(cues.Length, requests.Count);
            Assert.Equal(cues.Select(FeedbackAudio.Sound), requests.Select(request => (SfxId)request.Id));
            Assert.Equal(cues.Length, requests.Select(request => request.Id).Distinct().Count());
            Assert.All(requests, request => Assert.Equal(AudioRequestKind.Play, request.Kind));
        });
    }

    [Fact]
    public void HitAndObjectiveCuesRespectIndependentSpacingWindows()
    {
        WithHeadlessScene((scene, requests) =>
        {
            var audio = new FeedbackAudio(scene);

            Assert.True(audio.Play(FeedbackCue.Hit, 100));
            Assert.False(audio.Play(FeedbackCue.Hit, 103));
            Assert.True(audio.Play(FeedbackCue.Hit, 104));

            Assert.True(audio.Play(FeedbackCue.ObjectiveTaken, 200));
            Assert.False(audio.Play(FeedbackCue.ObjectiveTaken, 229));
            Assert.True(audio.Play(FeedbackCue.ObjectiveTaken, 230));

            Assert.Equal(4, requests.Count);
        });
    }

    [Fact]
    public void MutedOrZeroGainCuesEmitNoAudioRequest()
    {
        float previousVolume = FeedbackAudio.Volume;
        int previousMute = Sfx.TimedSfxMute;
        try
        {
            WithHeadlessScene((scene, requests) =>
            {
                FeedbackAudio.Volume = 1;
                Sfx.TimedSfxMute = 1;
                var audio = new FeedbackAudio(scene);
                Assert.False(audio.Play(FeedbackCue.Hit, 1));
                Assert.Empty(requests);

                Sfx.TimedSfxMute = 0;
                FeedbackAudio.Volume = 0;
                audio = new FeedbackAudio(scene);
                Assert.False(audio.Play(FeedbackCue.Kill, 100));
                Assert.Empty(requests);
            });
        }
        finally
        {
            FeedbackAudio.Volume = previousVolume;
            Sfx.TimedSfxMute = previousMute;
        }
    }

    [Fact]
    public void CriticalHealthCueUsesHysteresisAndResetsForNewIdentity()
    {
        WithHeadlessScene((scene, requests) =>
        {
            var audio = new FeedbackAudio(scene);
            var local = new CombatActor(0, 100, 1);
            var replacement = new CombatActor(0, 100, 2);

            audio.ObserveHealth(local, 24, 10);
            audio.ObserveHealth(local, 20, 11);
            audio.ObserveHealth(local, 30, 12);
            Assert.Single(requests);

            audio.ObserveHealth(local, 35, 13);
            audio.ObserveHealth(local, 24, 44);
            Assert.Equal(2, requests.Count);

            audio.ObserveHealth(replacement, 24, 100);
            Assert.Equal(3, requests.Count);
            Assert.All(requests, request => Assert.Equal((int)FeedbackAudio.Sound(FeedbackCue.CriticalHealth), request.Id));
        });
    }

    [Fact]
    public void FeedbackAudioDoesNotMutateGameplayState()
    {
        WithHeadlessScene((scene, requests) =>
        {
            uint matchId = scene.Match.MatchId;
            MatchPhase phase = scene.Match.Phase;
            uint phaseRevision = scene.Match.PhaseRevision;
            int activePlayers = scene.Match.ActivePlayers;
            int playerCount = scene.Players.ActiveCount;
            int mainPlayer = scene.LocalPlayerSlot;

            var audio = new FeedbackAudio(scene);
            var local = new CombatActor(0, 100, 1);
            Assert.True(audio.Play(FeedbackCue.Pickup, 100, new Vector3(1, 2, 3)));
            audio.ObserveHealth(local, 24, 200);

            Assert.Equal(2, requests.Count);
            Assert.Equal(matchId, scene.Match.MatchId);
            Assert.Equal(phase, scene.Match.Phase);
            Assert.Equal(phaseRevision, scene.Match.PhaseRevision);
            Assert.Equal(activePlayers, scene.Match.ActivePlayers);
            Assert.Equal(playerCount, scene.Players.ActiveCount);
            Assert.Equal(mainPlayer, scene.LocalPlayerSlot);
        });
    }

    private static void WithHeadlessScene(Action<Scene, List<AudioRequest>> action)
    {
        Scene scene = Scene.CreateHeadless();
        var requests = new List<AudioRequest>();
        scene.Audio.Requested += requests.Add;
        try
        {
            action(scene, requests);
        }
        finally
        {
            scene.Audio.Requested -= requests.Add;
            scene.CloseHeadless();
        }
    }
}
