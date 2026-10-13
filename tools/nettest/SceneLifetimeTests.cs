using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Diagnostics;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// No OnLoad, game assets, GL/audio device, native window or network session.
// Full collections below are diagnostic-only, after no-inlined owner release.
internal static class SceneLifetimeTests
{
    internal static int Run()
    {
        Headless.Enter();
        var previousState = GameState.Current;
        var previousPlayers = PlayerEntity.LegacyRegistry;
        var previousRandom = Rng.Current;
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("LIFETIME FAIL " + name);
            checks++; Console.WriteLine("LIFETIME PASS " + name);
        }
        try
        {
            Check(!SceneLifetimeDiagnostics.Enabled, "ordinary execution starts without lifecycle observation");
            Check(!RuntimeHelpers.IsReferenceOrContainsReferences<SceneLifetimeSnapshot>(),
                "snapshot contains only values and cannot retain a world or callback");
            Check(Mods.Render.TransitionGcDiagnostics.Check() == 0,
                "existing transition controls preserve inactive default GC policy");
            using var observation = SceneLifetimeDiagnostics.Begin();
            WeakReference<Scene> live = CreateAndEndLive();
            Check(observation.Snapshots.Count == 4 && observation.Snapshots.Select(s => s.Stage).SequenceEqual(new[] {
                SceneLifetimeStage.Constructed, SceneLifetimeStage.CleanupStarted,
                SceneLifetimeStage.CleanupComplete, SceneLifetimeStage.VisualResourcesReleased }),
                "real content-free cleanup records ordered lifecycle boundaries once");
            Check(observation.Snapshots.All(s => s.Facades == (SceneFacadeRoots.GameState | SceneFacadeRoots.Players | SceneFacadeRoots.Random)),
                "ended live world reports the legitimate foreground facade roots");
            Check(observation.Snapshots.All(s => s.ExternalLeases == null && s.ExternalCallbacks == null),
                "untracked external owners remain unknown rather than reported as zero");
            Collect();
            Check(IsAlive(live), "live scene remains reachable while compatibility save owner is bound");
            RestoreFacades(previousState, previousPlayers, previousRandom);
            Collect();
            Check(!IsAlive(live), "eligible scene is collected after explicit diagnostic owner release despite retained snapshots");

            WeakReference<Scene> replica = CreateAndEndReplica();
            Collect();
            Check(!IsAlive(replica), "detached private replica is collected without touching foreground facades");
            Check(observation.Snapshots.Skip(4).All(s => s.Facades == SceneFacadeRoots.None && s.Role == SceneLifetimeRole.Replica),
                "replica diagnostics distinguish isolated policy and absence of facade bindings");
            Check(observation.Snapshots[4].Generation > observation.Snapshots[0].Generation,
                "local generations are monotonic across independent scene owners");
            Check(observation.Snapshots.All(s => s.OwnedTextures == 0 && s.MainPrograms == 0 && s.ModelLeases == 0
                && s.PendingTextureDecodes == 0), "content-free resource counters remain accurate");

            int outerCount = observation.Snapshots.Count;
            using (var bounded = SceneLifetimeDiagnostics.Begin(2))
            {
                CreateAndEndReplica();
                Check(bounded.Snapshots.Count == 2 && bounded.DroppedRecords == 2,
                    "overflow drops value records with an explicit count and fixed storage bound");
            }
            Check(observation.Snapshots.Count == outerCount && SceneLifetimeDiagnostics.Enabled,
                "nested observation restores the outer owner without publishing into it");
            var thread = new Thread(() => SceneLifetimeDiagnostics.Record(default));
            thread.Start(); thread.Join();
            Check(observation.Snapshots.Count == outerCount, "unrelated worker thread cannot append to scene-thread diagnostics");
            observation.Dispose();
            CreateAndEndReplica();
            Check(!SceneLifetimeDiagnostics.Enabled && observation.Snapshots.Count == outerCount,
                "disposal disables publication and retained value records remain stable");
            Console.WriteLine($"LIFETIME summary checks={checks} scope=device-free eligible-owner diagnostic; no native leak claim");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { RestoreFacades(previousState, previousPlayers, previousRandom); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Scene> CreateAndEndLive()
    {
        var scene = NewScene();
        scene.DoCleanup(); scene.DoCleanup(); scene.UnloadGl();
        return new(scene);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Scene> CreateAndEndReplica()
    {
        var host = new PassiveReplaySessionHost();
        using var session = new ReplayPlaybackSession(host);
        var scene = NewScene(new ReplaySceneServices(session, host.State));
        scene.DoCleanup(); scene.UnloadGl();
        return new(scene);
    }
    private static Scene NewScene(ISceneServices? services = null) => new(new Vector2i(32, 32),
        SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { }, services, initializeRuntime: false);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RestoreFacades(SceneGameState state, ScenePlayerRegistry players, MatchRandom random)
    { GameState.Current = state; PlayerEntity.LegacyRegistry = players; Rng.Current = random; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive(WeakReference<Scene> scene) => scene.TryGetTarget(out _);
    private static void Collect()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
