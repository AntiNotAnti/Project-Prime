using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Real scene/foreground policy, synthetic room-ready bit only. No assets,
// player physics, transport, rendering or replay playback is executed here.
internal static class MomentumAdmissionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static int Run()
    {
        Headless.Enter();
        var scenes = new List<Scene>();
        NetRole role = NetSession.Role;
        int checks = 0;
        void Check(bool value, string message)
        { checks++; if (!value) throw new InvalidOperationException(message); }
        Scene Candidate(ISceneServices? services = null)
        {
            var scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, services, initializeRuntime: false);
            scenes.Add(scene);
            scene.GameState.Mode = GameMode.Battle;
            return scene;
        }
        void Ready(Scene scene) => typeof(Scene).GetField("_roomLoaded", Hidden)!.SetValue(scene, true);
        try
        {
            Check(!NetSession.Active && GameState.Current.Owner == null, "fresh offline process required");
            string schema = ReplayWorldCheckpoint.ComputeContract(stable: true);
            var scene = Candidate();
            Check(scene.DeveloperMomentumPracticeTuning == null, "no private preset by default");
            Check(!scene.DeveloperMomentumActive && !scene.DeveloperMomentumInputAllowed, "Native and blocked input by default");
            Check(!scene.TryEnableDeveloperMomentum(), "room must be ready");
            Ready(scene);
            scene.SideScene = true;
            Check(!scene.TryEnableDeveloperMomentum(), "side scene cannot opt in");
            scene.SideScene = false;
            scene.GameState.Mode = GameMode.SinglePlayer;
            Check(!scene.TryEnableDeveloperMomentum(), "campaign cannot opt in");
            scene.GameState.Mode = GameMode.Battle;
            scene.GameState.BalancedMode = true;
            Check(!scene.TryEnableDeveloperMomentum(), "uncalibrated Balanced rules rejected");
            scene.GameState.BalancedMode = false;
            scene.GameState.EnhancedHunters = true;
            Check(!scene.TryEnableDeveloperMomentum(), "uncalibrated Enhanced rules rejected");
            scene.GameState.EnhancedHunters = false;
            Check(!Task.Run(() => scene.TryEnableDeveloperMomentum()).GetAwaiter().GetResult(), "scene owner thread required");
            foreach (NetRole activeRole in new[] { NetRole.Client, NetRole.Host, NetRole.Server })
            {
                typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, activeRole);
                Check(!scene.TryEnableDeveloperMomentum(), "live role rejected: " + activeRole);
            }
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, role);
            typeof(Scene).GetField("_frameCount", Hidden)!.SetValue(scene, 1UL);
            Check(!scene.TryEnableDeveloperMomentum(), "late first opt-in rejected");
            typeof(Scene).GetField("_frameCount", Hidden)!.SetValue(scene, 0UL);
            Check(scene.TryEnableDeveloperMomentum() && scene.DeveloperMomentumActive, "explicit owner binding at synthetic load boundary");
            Check(!scene.TryEnableDeveloperMomentum(practiceTuning: MomentumPracticeTuning.Quake3Default), "selection immutable including preset");
            Check(scene.DeveloperMomentumPracticeTuning == null, "v1 selection remains default");
            Check(scene.DeveloperMomentumSteps == 0 && scene.LastMomentumStep == null, "diagnostics off by default");
            scene.SideScene = true;
            Check(!scene.DeveloperMomentumActive, "changed scene policy retires selection");
            scene.SideScene = false;
            Check(!scene.DeveloperMomentumActive && !scene.TryEnableDeveloperMomentum(), "retirement cannot reactivate");

            var old = Candidate(); Ready(old);
            Check(old.TryEnableDeveloperMomentum(), "second fresh candidate may select");
            var replacement = Candidate(); Ready(replacement);
            Check(!old.DeveloperMomentumActive && replacement.TryEnableDeveloperMomentum(), "foreground ownership cannot leak");
            var replica = Candidate(new ReplicaPolicy()); Ready(replica);
            Check(!replica.TryEnableDeveloperMomentum() && !replica.DeveloperMomentumActive, "private replica cannot opt in");
            var practice = Candidate(); Ready(practice);
            Check(practice.TryEnableDeveloperMomentum(practiceTuning: MomentumPracticeTuning.Quake3Default),
                "fresh scene may select immutable private preset");
            Check(Equals(practice.DeveloperMomentumPracticeTuning, MomentumPracticeTuning.Quake3Default),
                "scene retains selected preset");
            Check(!practice.TryEnableDeveloperMomentum(), "private preset cannot be replaced by v1");
            Check(!ReplayCapture.CanStartLiveRecording(out string? recordError) && !string.IsNullOrWhiteSpace(recordError),
                "central recording guard rejects private preset with actionable reason");
            string? demoPath = DemoRecorder.CurrentPath, serverPath = ServerReplayRecorder.CurrentPath,
                clipPath = DemoClip.LastSavedPath;
            Check(!DemoRecorder.Start() && !DemoRecorder.IsRecording, "actual recorder start refuses private preset");
            Check(!ServerReplayRecorder.Start(new ReplayMetadata()) && !ServerReplayRecorder.IsRecording
                && !string.IsNullOrWhiteSpace(ServerReplayRecorder.LastError), "server recorder guard refuses private preset before metadata/policy");
            Check(DemoClip.Save() == null && !DemoClip.IsSaving && !string.IsNullOrWhiteSpace(DemoClip.LastError),
                "actual clip save refuses private preset");
            Check(DemoRecorder.CurrentPath == demoPath && ServerReplayRecorder.CurrentPath == serverPath
                && DemoClip.LastSavedPath == clipPath, "refused recording entrypoints leave paths unchanged");
            practice.SideScene = true;
            Check(!practice.DeveloperMomentumActive, "practice retires on ownership policy loss");
            practice.SideScene = false;
            Check(!practice.DeveloperMomentumActive && !practice.TryEnableDeveloperMomentum(),
                "private preset retirement permanent");
            Check(!ReplayCapture.CanStartLiveRecording(out _), "retired unsupported preset remains unrecordable");
            var supported = Candidate(); Ready(supported);
            Check(ReplayCapture.CanStartLiveRecording(out _), "fresh supported scene does not inherit recording block");
            // Synthetic recorder-pending admission control only; no writer, packets,
            // authority, physics or fake successful recording is constructed.
            FieldInfo pending = typeof(DemoRecorder).GetField("_pending", BindingFlags.Static | BindingFlags.NonPublic)!;
            object? previousPending = pending.GetValue(null);
            try
            {
                pending.SetValue(null, true);
                Check(!supported.TryEnableDeveloperMomentum(practiceTuning: MomentumPracticeTuning.Quake3Default),
                    "private preset rejected while real recorder admission reports pending");
            }
            finally { pending.SetValue(null, previousPending); }
            FieldInfo serverPending = typeof(ServerReplayRecorder).GetField("_pending", BindingFlags.Static | BindingFlags.NonPublic)!;
            object? previousServerPending = serverPending.GetValue(null);
            try
            {
                serverPending.SetValue(null, true);
                Check(!supported.TryEnableDeveloperMomentum(practiceTuning: MomentumPracticeTuning.Quake3Default),
                    "private preset rejected while server recorder pending");
            }
            finally { serverPending.SetValue(null, previousServerPending); }
            Check(supported.TryEnableDeveloperMomentum(), "supported v1 selection remains available after rejected practice request");
            var ownerLoss = Candidate(); Ready(ownerLoss);
            Check(ownerLoss.TryEnableDeveloperMomentum(practiceTuning: MomentumPracticeTuning.Quake3Default),
                "owner-loss candidate selects practice");
            int ownerIndex = ownerLoss.Players.MainPlayerIndex;
            ownerLoss.Players.MainPlayerIndex = ownerIndex == 0 ? 1 : 0;
            Check(!ownerLoss.DeveloperMomentumActive, "changed local registry owner retires practice");
            ownerLoss.Players.MainPlayerIndex = ownerIndex;
            Check(!ownerLoss.DeveloperMomentumActive && !ownerLoss.TryEnableDeveloperMomentum(),
                "restoring owner cannot reactivate retired practice");
            var recordingRetire = Candidate(); Ready(recordingRetire);
            Check(recordingRetire.TryEnableDeveloperMomentum(practiceTuning: MomentumPracticeTuning.Quake3Default),
                "recording-retirement candidate selects practice");
            try
            {
                pending.SetValue(null, true);
                Check(!recordingRetire.DeveloperMomentumActive,
                    "new recorder pending state retires an already selected private preset");
            }
            finally { pending.SetValue(null, previousPending); }
            Check(!recordingRetire.DeveloperMomentumActive && !recordingRetire.TryEnableDeveloperMomentum(),
                "ending recording cannot reactivate retired private preset");
            Check(ReplayWorldCheckpoint.ComputeContract(stable: true) == schema
                && schema == "A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A",
                "existing declared replay schema unchanged");
            Console.WriteLine($"MOMENTUM ADMISSION PASS {checks} owner/scene-policy checks; synthetic readiness, no physics/network/replay acceptance.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM ADMISSION FAIL " + ex); return 1; }
        finally
        {
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, role);
            foreach (Scene scene in scenes)
            {
                try { scene.DoCleanup(); }
                finally { scene.UnloadGl(); }
            }
        }
    }

    private sealed class ReplicaPolicy : ISceneServices
    {
        public bool IsReplica => true;
        public bool MayEndOnScore => false;
        public bool ShouldLeaveAfterMatch => false;
        public bool SuppressDamage => true;
        public bool AllowsPresentationSideEffects => false;
        public IPlayerReplicationHost PlayerReplication => LivePlayerReplicationHost.Instance;
        public bool DisablePowerups => false;
        public MphRead.Mods.Multiplayer.MatchWorldProfile? NetworkWorldProfile => null;
        public bool ReplicatesHealthSpawns => false;
        public bool TryGetHealthSpawn(short id, out HealthSpawnState state) { state = default; return false; }
    }
}
