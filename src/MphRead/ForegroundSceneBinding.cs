using System;
using MphRead.Entities;
using MphRead.Mods.Diagnostics;
using MphRead.Mods.Launcher;

namespace MphRead;

internal readonly record struct ForegroundSceneLease(long Generation, int ThreadId);

// A value lease, not a restore stack. Existing activation points still bind
// exactly the same facades; a released owner leaves configuration-only bootstrap
// values, never the previous scene/player graph.
internal static class ForegroundSceneBinding
{
    private static readonly object Gate = new();
    internal static ForegroundSceneLease Bind(Scene scene)
    {
        if (scene.Services.IsReplica)
            throw new InvalidOperationException("Private replicas cannot bind foreground compatibility facades.");
        lock (Gate)
        {
            PlayerEntity.LegacyRegistry = scene.Players;
            GameState.Current = scene.GameState;
            Rng.Current = scene.Random;
            return new(scene.LifetimeGeneration, Environment.CurrentManagedThreadId);
        }
    }

    private static bool IsCurrent(Scene scene) => scene.ForegroundLease.Generation == scene.LifetimeGeneration
        && ReferenceEquals(GameState.Current, scene.GameState)
        && ReferenceEquals(PlayerEntity.LegacyRegistry, scene.Players) && ReferenceEquals(Rng.Current, scene.Random);

    // The global AI adapter is distinct from a room's owned state. Check and
    // reset under the same gate as a foreground switch, so an older still-live
    // worker cannot clear globals belonging to a newly bound scene.
    internal static bool TryResetLegacyAiGlobals(Scene scene)
    {
        lock (Gate)
        {
            if (!IsCurrent(scene)) return false;
            PlayerEntity.PlayerAiData.InitializeGlobals();
            return true;
        }
    }

    internal static bool TryRelease(PostMatchCommitContext commit)
    {
        if (commit.State != PostMatchCommitState.Released || commit.LegacyTransitionTiming || commit.SceneGeneration == 0)
            return false;
        lock (Gate)
        {
            Scene? scene = GameState.Current.Owner;
            if (scene == null || scene.ForegroundLease.Generation != commit.SceneGeneration
                || scene.ForegroundLease.ThreadId != Environment.CurrentManagedThreadId
                || !scene.ForegroundReleaseEligible || !IsCurrent(scene)) return false;

            var players = new ScenePlayerRegistry { MaxPlayers = scene.Players.MaxPlayers };
            var configuration = scene.GameState.ForScene(players);
            var random = new MatchRandom();
            random.SetRng1(scene.Random.Rng1); random.SetRng2(scene.Random.Rng2);
            // Drop only this ended owner's AI entries after all eligibility
            // gates. Never clear the complete global table or newer actors.
            PlayerEntity.PlayerAiData.ReleaseScene(scene);
            PlayerEntity.LegacyRegistry = players;
            GameState.Current = configuration;
            Rng.Current = random;
            scene.ObserveLifetime(SceneLifetimeStage.CompatibilityReleased);
            return true;
        }
    }
}

public partial class Scene
{
    internal ForegroundSceneLease ForegroundLease { get; private set; }
    private bool _cleanupComplete;
    private bool _visualReleaseComplete;
    private bool _unfencedCallbacksAtCleanup;
    internal bool ForegroundReleaseEligible => _cleanupComplete && _visualReleaseComplete
        && !_unfencedCallbacksAtCleanup && !GameState.InRoomTransition
        && _room?.TransitionWorkerPending != true && !SideScene && !IsReplayLab && !Services.IsReplica
        && !Mods.Network.NetAiClient.IsBoundTo(this);
    internal void BindForegroundFacades() => ForegroundLease = ForegroundSceneBinding.Bind(this);
}
