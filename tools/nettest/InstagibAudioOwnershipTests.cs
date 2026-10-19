using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.Mods.Sound;
using MphRead.Sound;

namespace MphRead.NetTest;

// Audio ownership identities and cached voice lifecycle only. No Scene is
// simulated, no device is opened, and no assets or audible output are used.
internal static class InstagibAudioOwnershipTests
{
    private sealed class Voice : InstagibBeamAudio.ICueVoice
    {
        internal bool Playing, Disposed;
        internal int Stops;
        public void Play(float gain) => Playing = true;
        public void Stop() { Stops++; Playing = false; }
        public void Dispose() { Disposed = true; Playing = false; }
    }

    private sealed class QuietReplicaServices : ISceneServices
    {
        public bool IsReplica => true;
        public bool AllowsPresentationSideEffects => false;
        public bool MayEndOnScore => false;
        public bool ShouldLeaveAfterMatch => false;
        public bool SuppressDamage => true;
        public IPlayerReplicationHost PlayerReplication => throw new NotSupportedException("No simulation in audio identity fixture.");
        public bool DisablePowerups => false;
        public MphRead.Mods.Multiplayer.MatchWorldProfile? NetworkWorldProfile => null;
        public bool ReplicatesHealthSpawns => false;
        public bool TryGetHealthSpawn(short id, out HealthSpawnState state) { state = default; return false; }
    }

    private static Scene Identity(ISceneServices services)
    {
        // Only Services is read by the ownership policy. Avoid constructing a
        // foreground Scene or installing gameplay/global bindings in this test.
        var scene = (Scene)RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        typeof(Scene).GetField("<Services>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(scene, services);
        return scene;
    }

    internal static void Run(Action<bool, string> check)
    {
        check(Sfx.Instance == null, "audio ownership fixture never opens or replaces a sound backend");
        var cache = (IDictionary<(BeamFxStyle Style, bool Impact), InstagibBeamAudio.ICueVoice>)typeof(InstagibBeamAudio)
            .GetField("Players", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        check(cache.Count == 0, "audio ownership fixture starts with no resident real voices");
        var live = Identity(LiveSceneServices.Instance);
        var first = Identity(new QuietReplicaServices());
        var second = Identity(new QuietReplicaServices());
        var voice = new Voice();
        var impact = new Voice();
        ulong current = 0;
        try
        {
            ReplayAudioOwner.Reset();
            cache.Add((BeamFxStyle.IonFracture, false), voice);
            cache.Add((BeamFxStyle.IonFracture, true), impact);
            check(InstagibBeamAudio.MayPlay(live) && !InstagibBeamAudio.MayPlay(first),
                "ordinary live audio is allowed and an unowned replica stays silent");
            voice.Play(.1f); impact.Play(.1f);
            ulong prior = current = ReplayAudioOwner.Acquire(first, live);
            check(!voice.Playing && voice.Stops == 1 && !voice.Disposed
                && !impact.Playing && impact.Stops == 1 && !impact.Disposed,
                "acquiring replay audio stops the live tail without disposing its cached voice");
            check(!InstagibBeamAudio.MayPlay(live) && InstagibBeamAudio.MayPlay(first)
                && !InstagibBeamAudio.MayPlay(second), "only the current private owner may emit accents");
            voice.Play(.1f); impact.Play(.1f);
            current = ReplayAudioOwner.Acquire(second, live);
            check(!voice.Playing && voice.Stops == 2 && !impact.Playing && impact.Stops == 2, "replacement owner stops the previous replay tail");
            voice.Play(.1f); impact.Play(.1f);
            ReplayAudioOwner.Release(prior);
            ReplayAudioOwner.Release(0);
            check(voice.Playing && voice.Stops == 2 && impact.Playing && impact.Stops == 2
                && InstagibBeamAudio.MayPlay(second),
                "stale and zero release tokens cannot stop the new owner's voice");
            ReplayAudioOwner.Release(current); current = 0;
            check(!voice.Playing && voice.Stops == 3 && !voice.Disposed
                && !impact.Playing && impact.Stops == 3 && !impact.Disposed
                && ReferenceEquals(cache[(BeamFxStyle.IonFracture, false)], voice)
                && ReferenceEquals(cache[(BeamFxStyle.IonFracture, true)], impact),
                "current release stops its tail while retaining the bounded cached voice");
            check(InstagibBeamAudio.MayPlay(live) && !InstagibBeamAudio.MayPlay(first)
                && !InstagibBeamAudio.MayPlay(second), "release restores live-only audio permission");
        }
        finally
        {
            ReplayAudioOwner.Release(current);
            ReplayAudioOwner.Reset();
            cache.Clear(); voice.Dispose(); impact.Dispose();
        }
    }
}
