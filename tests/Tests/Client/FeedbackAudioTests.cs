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
    public void StopRequestsPreserveBusOrderAndForceSemantics()
    {
        var requests = new AudioRequests();
        var emitted = new List<AudioRequest>();
        requests.Requested += emitted.Add;

        requests.StopEnvironment();
        requests.StopAll(force: false);

        Assert.Collection(emitted,
            request => Assert.Equal(AudioRequestKind.StopEnvironment, request.Kind),
            request =>
            {
                Assert.Equal(AudioRequestKind.StopAll, request.Kind);
                Assert.False(request.Force);
            });
    }

    [Fact]
    public void DistinctCuesEmitTheirMappedSounds()
    {
        WithHeadlessScene((scene, requests) =>
        {
            var audio = new FeedbackAudio(scene);
            FeedbackCue[] cues =
            {
                FeedbackCue.Hit, FeedbackCue.Kill, FeedbackCue.PickupRespawned,
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
            Assert.True(audio.Play(FeedbackCue.PickupRespawned, 100, new Vector3(1, 2, 3)));
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

    [Theory]
    [InlineData(ItemType.HealthSmall, SfxId.POWER_UP1)]
    [InlineData(ItemType.HealthMedium, SfxId.POWER_UP2)]
    [InlineData(ItemType.HealthBig, SfxId.POWER_UP2)]
    [InlineData(ItemType.UASmall, SfxId.AMMO_POWER_UP1)]
    [InlineData(ItemType.MissileSmall, SfxId.AMMO_POWER_UP1)]
    [InlineData(ItemType.UABig, SfxId.AMMO_POWER_UP2)]
    [InlineData(ItemType.MissileBig, SfxId.AMMO_POWER_UP2)]
    [InlineData(ItemType.DoubleDamage, SfxId.DOUBLE_DAMAGE_POWER_UP)]
    [InlineData(ItemType.Cloak, SfxId.CLOAK_POWER_UP)]
    [InlineData(ItemType.Deathalt, SfxId.DOUBLE_DAMAGE_POWER_UP)]
    [InlineData(ItemType.ArtifactKey, SfxId.KEY_PICKUP)]
    [InlineData(ItemType.VoltDriver, SfxId.WEAPON_POWER_UP)]
    [InlineData(ItemType.Battlehammer, SfxId.WEAPON_POWER_UP)]
    [InlineData(ItemType.Imperialist, SfxId.WEAPON_POWER_UP)]
    [InlineData(ItemType.Judicator, SfxId.WEAPON_POWER_UP)]
    [InlineData(ItemType.Magmaul, SfxId.WEAPON_POWER_UP)]
    [InlineData(ItemType.ShockCoil, SfxId.WEAPON_POWER_UP)]
    [InlineData(ItemType.OmegaCannon, SfxId.WEAPON_POWER_UP)]
    [InlineData(ItemType.AffinityWeapon, SfxId.WEAPON_POWER_UP)]
    public void PickupMapperMatchesAuthoritativeItemSemantics(ItemType itemType, SfxId expected)
    {
        Assert.True(FeedbackAudio.TryGetPickupSound(itemType, out SfxId actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UnsupportedPickupHasNoAcquisitionSound()
    {
        Assert.False(FeedbackAudio.TryGetPickupSound(ItemType.EnergyTank, out _));
        Assert.False(FeedbackAudio.TryGetPickupSound(ItemType.UAExpansion, out _));
        Assert.False(FeedbackAudio.TryGetPickupSound(ItemType.MissileExpansion, out _));
    }

    [Fact]
    public void RapidAcquisitionsRemainIndependentAndNonPositional()
    {
        WithHeadlessScene((scene, requests) =>
        {
            var audio = new FeedbackAudio(scene);

            Assert.True(audio.PlayPickupAcquired(ItemType.HealthSmall, 100));
            Assert.True(audio.PlayPickupAcquired(ItemType.UASmall, 101));
            Assert.True(audio.PlayPickupAcquired(ItemType.VoltDriver, 102));

            Assert.Equal(new[] { SfxId.POWER_UP1, SfxId.AMMO_POWER_UP1, SfxId.WEAPON_POWER_UP },
                requests.Select(request => (SfxId)request.Id));
            Assert.All(requests, request =>
            {
                Assert.Equal(AudioRequestKind.Play, request.Kind);
                Assert.False(request.NoUpdate);
            });
        });
    }

    [Fact]
    public void RespawnCueRemainsPositional()
    {
        WithHeadlessScene((scene, requests) =>
        {
            var audio = new FeedbackAudio(scene);
            Assert.True(audio.Play(FeedbackCue.PickupRespawned, 100, new Vector3(1, 2, 3)));
            AudioRequest request = Assert.Single(requests);
            Assert.True(request.NoUpdate);
            Assert.NotNull(request.Source);
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
