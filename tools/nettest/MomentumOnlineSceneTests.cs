using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// A real UDP client in its own process. DedicatedServer and the other client are externally owned.
internal static class MomentumOnlineSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Require(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    internal static int Run(int port, MovementModel expected, string outputPath, int seconds = 12, string? replayPath = null)
    {
        Scene? scene = null;
        var clock = Stopwatch.StartNew();
        string path = Path.GetFullPath(outputPath);
        try
        {
            _checks = 0;
            Require(seconds >= 6 && seconds <= 120 && port is > 0 and <= 65535, "bounded worker duration and UDP port");
            Require(!NetSession.Active && !DemoPlayback.IsActive, "worker starts without a session");
            Require(ServerSim.Available(out string reason), reason); Headless.Enter();
            string name = "MomentumCPU-" + Environment.ProcessId;
            Require(NetLaunch.Connect("127.0.0.1", port, name, Hunter.Samus, timeoutMs: 15000, color: 0),
                "actual UDP Connect: " + NetLaunch.LastJoinError);
            Require(NetSession.IsClient && !NetSession.IsAuthority, "real UDP client role, no authority impersonation");
            var wait = Stopwatch.StartNew();
            while (!NetSession.ShouldLoadMatch && wait.Elapsed.TotalSeconds < 40)
            {
                NetSession.Pump(); CheckConnection(); Thread.Sleep(5);
            }
            Require(NetSession.ShouldLoadMatch && NetSession.ServerSession.HasValue && NetSession.ServerMatch.HasValue, "server publishes actual match and frozen session");
            MatchDefinition definition = NetSession.ActiveMatchDefinition!.Value;
            Require(definition.MovementModel == expected, "server frozen match selected expected movement model");
            ushort matchId = NetSession.CurrentMatchId; ulong epoch = NetSession.AuthorityEpoch;
            int localSlot = NetSession.LocalSlot;
            Require(localSlot >= 0 && epoch != 0 && matchId != 0, "actual welcome identifies local incarnation and match");
            NetLaunch.DisableCheatsForMatch();
            var keyboard = SyntheticInput.CreateKeyboard();
            scene = new Scene(new Vector2i(256, 192), keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { }, lifetimeOwned: value => scene = value);
            NetLaunch.BuildPlayers(scene, NetSession.LocalHunter, NetSession.LocalColor, teams: GameState.IsTeamMode(definition.Mode));
            scene.AddRoom(definition.RoomKey, definition.Mode, playerCount: NetLaunch.RoomPlayerCount);
            // The frozen server definition owns this configuration, including after
            // retail room setup installs its own defaults.
            definition.ApplyInitialConfiguration(scene.GameState);
            typeof(Scene).GetField("_developerMomentumDiagnostics", Hidden)!.SetValue(scene, true);
            scene.OnLoad();
            var inputMode = typeof(Scene).GetField("_inputMode", Hidden)!;
            inputMode.SetValue(scene, Enum.Parse(inputMode.FieldType, "All"));
            var player = scene.Players.Main;
            Require(player.SlotIndex == localSlot && !player.IsBot && !NetHooks.IsPuppet(player), "native BuildPlayers installed actual local owner");
            foreach (var control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            player.Controls.MoveUp.Key = Keys.W; player.Controls.MoveDown.Key = Keys.S;
            player.Controls.MoveRight.Key = Keys.D; player.Controls.Jump.Key = Keys.Space; player.Controls.Shoot.Key = Keys.Enter;
            player.Controls.Morph.Key = Keys.Tab;
            player.Controls.MouseAim = false; player.Controls.KeyboardAim = true;
            var set = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
            NetSession.MarkMatchLoaded();
            wait.Restart(); double next = clock.Elapsed.TotalSeconds;
            while ((!NetSession.WorldIsReady || NetSession.FreezeGameplay || player.Health == 0 || !player.ModIsInPlay) && wait.Elapsed.TotalSeconds < 40)
            {
                WaitUntil(clock, ref next);
                scene.OnSimulationFrame(); CheckConnection();
            }
            Require(NetSession.WorldIsReady && NetRoomChange.GameplayReady && !NetSession.FreezeGameplay && player.Health > 0 && player.ModIsInPlay,
                "actual UDP bootstrap and load barrier release to a living owner");
            scene.CameraSequences.Current?.End();
            Require(scene.GameState.MovementModel == expected && NetSession.ActiveMatchDefinition?.MovementModel == expected, "actual world and frozen server rule agree");
            Require(!(bool)typeof(Scene).GetField("_developerMomentumRequested", Hidden)!.GetValue(scene)!, "online adapter does not install private developer opt-in");
            if (expected == MovementModel.Momentum) Require(scene.MomentumMovementActive && scene.MomentumOwnsPlayer(player), "authorized online rule owns real local player");
            if (replayPath != null) Require(DemoRecorder.Start(), "real accepted-fact recording starts after world readiness");

            var remoteSlots = new HashSet<int>(); var previousRemote = new Dictionary<int, Vector3>();
            var samples = new List<object>(); int remoteMoving = 0, inputTicks = 0, impulses = 0, directSamples = 0, airborneTicks = 0, landings = 0;
            int altTicks = 0, airborneMorphTicks = 0, bipedReturnTicks = 0;
            Vector3 origin = player.Position; float previousYSpeed = player.Speed.Y; bool airborne = false;
            long firstSteps = scene.DeveloperMomentumSteps; ulong firstSceneFrame = scene.FrameCount;
            double readyTime = clock.Elapsed.TotalSeconds; int tick = 0;
            while (clock.Elapsed.TotalSeconds - readyTime < seconds)
            {
                WaitUntil(clock, ref next);
                int phase = tick % 240;
                set(keyboard, Keys.W, phase < 48); set(keyboard, Keys.D, phase >= 48 && phase < 96);
                set(keyboard, Keys.S, phase >= 96 && phase < 144);
                set(keyboard, Keys.Space, tick % 90 == 30);
                // Recording acceptance adds real mid-air form transitions; the
                // short protocol/rule worker keeps its original input script.
                set(keyboard, Keys.Tab, replayPath != null && tick % 600 is 220 or 350);
                long before = scene.DeveloperMomentumSteps; ulong frame = scene.FrameCount;
                scene.OnSimulationFrame(); CheckConnection();
                Require(NetSession.CurrentMatchId == matchId && NetSession.AuthorityEpoch == epoch,
                    "live fixture remains in its actual frozen match generation");
                Require(NetSession.WorldIsReady && NetRoomChange.GameplayReady && !NetSession.FreezeGameplay,
                    "real baseline readiness remains open");
                Require(scene.GameState.MovementModel == expected && NetSession.ActiveMatchDefinition?.MovementModel == expected,
                    "live server rule remains immutable");
                Require(player.SlotIndex == NetHooks.LocalSlot && !NetHooks.IsPuppet(player) && Finite(player.Position) && Finite(player.Speed), "finite actual local collision body ownership");
                long delta = scene.DeveloperMomentumSteps - before;
                Require(delta is >= 0 and <= 1, "single real local solver application per tick");
                if (expected == MovementModel.Native) Require(delta == 0 && !scene.MomentumMovementActive, "Native match never activates Momentum adapter");
                if (delta == 1)
                {
                    var sample = scene.LastMomentumStep!.Value;
                    Require(sample.Slot == localSlot && sample.SimulationFrame == frame, "diagnostic names actual local slot/frame");
                    Require(BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y) == BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y), "online horizontal adapter preserves native Y bits");
                    var result = MomentumProfileV1.Step(new Vector3(sample.NativeVelocityBefore.X * 30, 0, sample.NativeVelocityBefore.Z * 30), sample.Input);
                    Require(MathF.Abs(sample.NativeVelocityAfter.X - result.Velocity.X / 30) <= .00001f
                        && MathF.Abs(sample.NativeVelocityAfter.Z - result.Velocity.Z / 30) <= .00001f, "actual online rule uses v1 horizontal solver");
                    directSamples++;
                }
                if (player.Controls.MoveUp.IsDown || player.Controls.MoveRight.IsDown || player.Controls.MoveDown.IsDown) inputTicks++;
                if (player.Controls.Jump.IsPressed && player.Speed.Y > .01f && player.Speed.Y > previousYSpeed + .01f)
                {
                    Require(player.Speed.Y <= Fixed.ToFloat(player.Values.JumpSpeed) + .00001f, "actual native legal jump retains native impulse ceiling"); impulses++;
                }
                previousYSpeed = player.Speed.Y;
                bool grounded = player.Flags1.TestFlag(PlayerFlags1.Grounded);
                if (player.IsAltForm) altTicks++;
                if (!grounded && player.IsMorphing) airborneMorphTicks++;
                if (altTicks > 0 && !player.IsAltForm && !player.IsMorphing && !player.IsUnmorphing) bipedReturnTicks++;
                if (!grounded) { airborne = true; airborneTicks++; }
                else if (airborne) { landings++; airborne = false; }
                foreach (var remote in scene.Players.Items)
                {
                    int slot = remote.SlotIndex;
                    if (slot == localSlot || slot >= NetSession.SlotOccupied.Length || !NetSession.SlotOccupied[slot]
                        || remote.IsBot || remote.Health == 0 || !remote.LoadFlags.TestFlag(LoadFlags.Active)) continue;
                    Require(NetHooks.IsPuppet(remote) && !scene.MomentumOwnsPlayer(remote), "actual remote human has no local movement ownership");
                    Require(Finite(remote.Position) && Finite(remote.Speed), "actual remote collision body remains finite");
                    remoteSlots.Add(slot);
                    if (previousRemote.TryGetValue(slot, out Vector3 prior) && (remote.Position - prior).Length > .001f) remoteMoving++;
                    previousRemote[slot] = remote.Position;
                }
                if (tick % 60 == 0) samples.Add(new { tick, frame = scene.FrameCount, localSlot, solverSteps = scene.DeveloperMomentumSteps - firstSteps,
                    position = new[] { player.Position.X, player.Position.Y, player.Position.Z }, speed = new[] { player.Speed.X, player.Speed.Y, player.Speed.Z }, remoteHumans = remoteSlots.Count });
                tick++;
            }
            Require(inputTicks > 0 && (player.Position - origin).Length > .01f, "actual local keyboard input displaces native body");
            Require(impulses > 0 && airborneTicks > 0 && landings > 0, "actual native legal jump, airborne interval and collision landing");
            Require(remoteSlots.Count > 0 && remoteMoving > 0, "actual other UDP human including late join is observed moving");
            Require(expected == MovementModel.Native ? scene.DeveloperMomentumSteps == firstSteps : directSamples > 0, "selected model actually governs local solver execution");
            uint? recordedFrames = null;
            if (replayPath != null)
            {
                Require(altTicks > 0 && airborneMorphTicks > 0 && bipedReturnTicks > 0,
                    "actual recorded keyboard script earns mid-air morph, stable alt form and biped return");
                var timeline = ReplayCapture.Recorder.Timeline;
                Require(timeline.FirstRecordingFrame.HasValue && timeline.LastRecordingFrame.HasValue,
                    "real recorder retains accepted-fact history");
                uint start = timeline.FirstRecordingFrame!.Value, end = timeline.LastRecordingFrame!.Value;
                recordedFrames = end - start;
                Require(recordedFrames >= 1800, "recording spans seek/checkpoint acceptance intervals");
                Require(timeline.TryFreeze(start, end, out var clip) && clip != null,
                    "production timeline freezes actual accepted match facts");
                using (clip) ReplayTimelineArchive.SaveFrozen(clip!, Path.GetFullPath(replayPath));
                Require(ReplayArchive.Validate(replayPath) == ReplayOpenResult.Success,
                    "actual live fixture round-trips through production replay writer and archive validation");
                Require(DemoRecorder.LastError == null && ReplayCapture.WorldCapture.LastError == null,
                    "live canonical world and disk recording report no errors");
                DemoRecorder.Stop();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks, model = expected.ToString(), profileVersion = MomentumProfileV1.MomentumProfileVersion,
                port, localSlot, matchId, authorityEpoch = epoch, name, seconds, ticks = tick, firstSceneFrame, finalSceneFrame = scene.FrameCount,
                solverSteps = scene.DeveloperMomentumSteps - firstSteps, directSamples, inputTicks, legalJumpImpulses = impulses, airborneTicks, landings,
                altTicks, airborneMorphTicks, bipedReturnTicks, recordedFrames,
                remoteSlots = remoteSlots.OrderBy(slot => slot).ToArray(), remoteMovingSamples = remoteMoving, disconnected = false,
                scope = "real localhost UDP dedicated server + independent client processes; CPU/headless Roam; actual bootstrap/late-join/owner movement and optional accepted-fact archive; no rendered/device/bot/replay-seek acceptance", samples }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"MOMENTUM ONLINE SCENE PASS {_checks} assertions model={expected} local={localSlot} steps={scene.DeveloperMomentumSteps - firstSteps} remoteMoving={remoteMoving}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM ONLINE SCENE FAIL " + ex); return 1; }
        finally
        {
            DemoRecorder.Stop();
            try { if (scene != null) MomentumSceneTests.Release(scene); }
            finally { NetSession.Stop(); }
        }
    }
    private static void CheckConnection() => Require(NetSession.Active && NetSession.IsClient && !NetSession.IsAuthority
        && !NetSession.Refused && !NetSession.SessionTimedOut && !NetSession.ConnectionLost,
        "actual UDP session remains connected: " + NetSession.RefusedReason);
    private static void WaitUntil(Stopwatch clock, ref double next)
    {
        next += 1d / 60;
        double remaining = next - clock.Elapsed.TotalSeconds;
        if (remaining > .002) Thread.Sleep((int)((remaining - .001) * 1000));
        while (clock.Elapsed.TotalSeconds < next) Thread.SpinWait(32);
        // Bound catch-up work after an asset load or an external scheduling stall.
        if (clock.Elapsed.TotalSeconds - next > .25) next = clock.Elapsed.TotalSeconds;
    }
}
