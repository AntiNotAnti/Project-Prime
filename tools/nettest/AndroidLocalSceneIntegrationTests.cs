using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>Actual content-free Scene and existing detached retirement owners.
/// No Android GameView, OnLoad, GL, audio device, assets or real save writes.</summary>
internal static class AndroidLocalSceneIntegrationTests
{
    internal static int Run()
    {
        Headless.Enter();
        var oldState = GameState.Current; var oldPlayers = PlayerEntity.LegacyRegistry; var oldRandom = Rng.Current;
        var oldSlot = Menu.SaveSlot; var oldSave = Menu.NeededSave;
        int checks = 0;
        void Check(bool ok, string message)
        {
            checks++; if (!ok) throw new InvalidOperationException("ANDROID LOCAL SCENE " + message);
        }
        try
        {
            string schema = ReplayWorldCheckpoint.ComputeContract(stable: true);
            var retired = new List<WeakReference>();
            for (int i = 0; i < 32; i++) retired.Add(RetireCandidate(i % 2 == 0, Check));
            for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            foreach (var weak in retired) Check(!weak.IsAlive, "retired normal/partial candidate is not retained by foreground/save owners");
            Check(ReplayWorldCheckpoint.ComputeContract(stable: true) == schema
                && schema == "A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A", "constructor registration changes no archival schema");
            Console.WriteLine($"ANDROID LOCAL SCENE INTEGRATION PASS {checks} checks; actual content-free Scene/commit/foreground owners, no Android/EGL/device acceptance.");
            return 0;
        }
        finally
        {
            Menu.SaveSlot = oldSlot; Menu.NeededSave = oldSave;
            GameState.Current = oldState; PlayerEntity.LegacyRegistry = oldPlayers; Rng.Current = oldRandom;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RetireCandidate(bool failConstructor, Action<bool, string> check)
    {
        // Offline selects no save before construction. A pre-reset partial
        // candidate may inherit bootstrap StorySave values, but slot zero
        // must never capture/write them as an Adventure payload.
        Menu.SaveSlot = 0;
        Scene? registered = null;
        try
        {
            var built = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, initializeRuntime: false, lifetimeOwned: candidate =>
                {
                    check(registered == null && ReferenceEquals(GameState.Current.Owner, candidate), "exact candidate registered once after owner binding");
                    check(ReferenceEquals(PlayerEntity.LegacyRegistry, candidate.Players)
                        && candidate.Players.Main == null, "registration precedes risky player/runtime initialization");
                    registered = candidate;
                    if (failConstructor) throw new InjectedConstructorFailure();
                });
            check(!failConstructor && ReferenceEquals(built, registered) && built.Players.Main != null,
                "successful constructor returns the exact registered candidate");
        }
        catch (InjectedConstructorFailure)
        {
            check(failConstructor && registered != null && registered.Players.Main == null,
                "throwing construction retains its exact partially initialized candidate");
        }
        var scene = registered ?? throw new InvalidOperationException("candidate was not registered");
        var commit = MatchStart.PrepareBeforeTeardown(scene.GameState)
            ?? throw new InvalidOperationException("fixture cannot have a room transition");
        check(commit.Slot == 0 && !commit.HasPayload && commit.SceneGeneration == scene.LifetimeGeneration,
            "failed Offline construction captures exact generation with no inherited Adventure bytes");
        scene.DoCleanup(); scene.UnloadGl(); MatchStart.AfterMatch(commit);
        check(commit.State == PostMatchCommitState.Released && commit.WriteAttempts == 0,
            "existing detached commit completes without save I/O");
        check(ForegroundSceneBinding.TryRelease(commit), "actual partial/normal candidate cleanup permits exact foreground release");
        check(GameState.Current.Owner == null && !ReferenceEquals(PlayerEntity.LegacyRegistry, scene.Players),
            "scene-free foreground exposes configuration without the old registry");
        check(!ForegroundSceneBinding.TryRelease(commit), "old commit cannot release another or already released owner");
        return new WeakReference(scene);
    }
    private sealed class InjectedConstructorFailure : Exception { }
}
