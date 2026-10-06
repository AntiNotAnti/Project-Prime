using System;
using System.Diagnostics;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Offline timing and normal native lifecycle fixtures; no socket or authored attacks.</summary>
public static class NetMovementCheck
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    public static int Run(string? assetDirectory = null)
    {
        _checks = 0;
        try
        {
            Headless.Enter();
            Timing();
            if (assetDirectory != null)
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(assetDirectory));
            Paths.UpdatePaths(); Paths.ChooseMphPath();
            Lifecycle();
            Console.WriteLine($"PASS: {_checks} movement timing/lifecycle checks");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine($"FAIL: movement checks: {ex}"); return 1; }
        finally { NetSession.Stop(); FrameTiming.Reset(); }
    }

    private static void Timing()
    {
        NetSession.StartPlayback(); // playback-only transport never opens a socket
        try
        {
            foreach (int fps in new[] { 30, 60, 120, 144, 240 })
            {
                NetSmoothing.Reset(); FrameTiming.Reset();
                long origin = Stopwatch.GetTimestamp();
                uint filed = 1000;
                NetSmoothing.Record(filed, ReadOnlySpan<PlayerState>.Empty, origin);
                int steps = 0;
                for (int draw = 1; draw <= fps * 10; draw++)
                {
                    uint newest = 1000 + (uint)((long)draw * 60 / fps);
                    if (newest != filed)
                    {
                        NetSmoothing.Record(newest, ReadOnlySpan<PlayerState>.Empty,
                            origin + (long)Math.Round((newest - 1000) * (double)Stopwatch.Frequency / 60));
                        filed = newest;
                    }
                    int owed = FrameTiming.Advance(1.0 / fps);
                    for (int i = 0; i < owed; i++) { NetSmoothing.Tick(); steps++; }
                }
                Check(Math.Abs(steps - 600) <= 1, $"{fps} FPS: fixed simulation cadence");
                Check(NetSmoothing.JitterFrames < 0.001 && NetSmoothing.Delay < 2,
                    $"{fps} FPS: regular coalesced records do not accumulate delay ({NetSmoothing.Delay:F3})");
                Console.WriteLine($"MOVEMENT TIMING {fps} FPS: {steps} ticks, delay={NetSmoothing.Delay:F3} frames, jitter={NetSmoothing.JitterFrames:F6}");
            }
            NetSmoothing.Reset();
            long start = Stopwatch.GetTimestamp();
            NetSmoothing.Record(1000, ReadOnlySpan<PlayerState>.Empty, start);
            double peak = 0;
            for (uint step = 1; step <= 180; step++)
            {
                // Transit increases suddenly, then drains at one frame per
                // authority frame. Equal timestamps represent a received burst.
                int offset = (step % 20) switch { 1 => 4, 2 => 3, 3 => 2, 4 => 1, _ => 0 };
                NetSmoothing.Record(1000 + step, ReadOnlySpan<PlayerState>.Empty,
                    start + (long)Math.Round((step + offset) * (double)Stopwatch.Frequency / 60));
                NetSmoothing.Tick(); peak = Math.Max(peak, NetSmoothing.Delay);
            }
            Check(NetSmoothing.JitterFrames > 0.2 && peak > NetSmoothing.MinDelayFrames + 0.3,
                "actual burst transit jitter still raises adaptive buffering");
            NetSmoothing.Reset();
            NetSmoothing.Record(1000, ReadOnlySpan<PlayerState>.Empty, start);
            for (int i = 0; i < 30; i++) NetSmoothing.Tick();
            Check(NetSmoothing.Starved > 0 && NetSmoothing.Delay > NetSmoothing.MinDelayFrames,
                "actual missing delivery still triggers bounded emergency buffering");
            Check(NetSmoothing.Delay <= NetSmoothing.MaxDelayFrames, "emergency buffer is bounded");
            Check(NetHooks.IntentFresh(30) && !NetHooks.IntentFresh(31), "neutral policy boundary is exactly 30 frames");
            Check(PlayerReplicationBridge.RemainingRespawnTicks(100, 90) == 10
                && PlayerReplicationBridge.RemainingRespawnTicks(100, 101) == 0
                && PlayerReplicationBridge.RemainingRespawnTicks(5, uint.MaxValue - 4) == 10,
                "authority eligibility handles zero/expired and modular frame boundaries");
        }
        finally { NetSession.Stop(); }
    }

    private static void Lifecycle()
    {
        var roster = RosterPacket.Create();
        roster.Count = 1; roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
        roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Hunters[0] = (byte)Hunter.Samus;
        roster.Teams[0] = -1; roster.Names[0] = "MOVEMENT FIXTURE";
        var session = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, MaxPlayers = 1,
            Phase = SessionPhase.InMatch, Match = new MatchDefinition { RoomKey = "MP1 SANCTORUS",
                Mode = GameMode.Battle, TimeLimitSeconds = 600 }, WorldProfile = MatchWorldProfile.Resolve(1) };
        var sim = new ServerSim();
        try
        {
        Check(sim.Start("MP1 SANCTORUS", GameMode.Battle, 1, _ => { }, () => { }, roster, session), "native fixture loads");
        NetSlotManager.Sync(); sim.Step();
        var player = PlayerEntity.Players[0];
        MphRead.Sound.Sfx.QueueStream(VoiceId.VOICE_ONE_KILL_TO_WIN);
        Check(true, "headless native voice cue tolerates absent audio runtime");
        player.OwningScene.GameState.PointGoal = 1000;
        Check(player.Health > 0, "native fixture spawns");
        ushort life = NetPlayerLifecycle.Get(0); int health = player.Health; int commits = 0;
        NetSlotManager.QueueHunterChoice(0, Hunter.Spire, _ => commits++);
        NetSlotManager.Sync();
        Check(player.Hunter == Hunter.Samus && player.Health == health && NetPlayerLifecycle.Get(0) == life,
            "ordinary queued choice does not reinitialize the current life");
        // Normal death/respawn, without fabricated packets or attack claims.
        player.TakeDamage(0, DamageFlags.Death, null, source: null);
        Check(player.RespawnTimer == PlayerEntity.RespawnTime, "normal death uses native eligibility timer");
        var dead = default(PlayerState);
        player.OwningScene.PlayerReplication.CaptureMovementState(player, ref dead);
        Check(PlayerReplicationBridge.RemainingRespawnTicks(dead.RespawnEligibleFrame, NetSession.NetFrame)
            == PlayerEntity.RespawnTime, "wire eligibility describes the native death gate");
        int savedCount = player.OwningScene.Players.PlayerCount;
        player.OwningScene.Players.PlayerCount = 4;
        var countdown = typeof(PlayerEntity).GetMethod("GetTimeUntilRespawn",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Check((int)countdown.Invoke(player, null)! == PlayerEntity.RespawnTime,
            "online HUD uses authority gate even when observer count rises");
        player.OwningScene.Players.PlayerCount = savedCount;
        for (int i = 0; i < PlayerEntity.RespawnTime; i++) sim.Step();
        Check(sim.StepFailures == 0, "normal native lifecycle has no simulation failures");
        Check(player.Health > 0 && player.Hunter == Hunter.Spire && commits == 1
            && NetPlayerLifecycle.Get(0) == NetLifecycleTracker.Next(life), "queued hunter commits before exactly one next life");
        for (int i = 0; i < 5; i++) sim.Step();
        Check(commits == 1 && NetPlayerLifecycle.Get(0) == NetLifecycleTracker.Next(life), "no echoed hunter/reset life after spawn");

        var host = new FixtureHost();
        var bridge = new PlayerReplicationBridge(host);
        var state = new PlayerState { SlotIndex = 0, SlotGeneration = 1, LifeId = 1,
            Hunter = (byte)player.Hunter, Health = 100, Position = player.Position, Facing = Vector3.UnitZ,
            Flags = PlayerState.FlagActive | PlayerState.FlagSpawned };
        bridge.ApplyState(player, state, isLocal: true);
        player.Position += new Vector3(8, 0, 0);
        state.Flags |= PlayerState.FlagFrozen; state.FreezeEventId = 1;
        bridge.ApplyState(player, state, isLocal: true);
        Check(player.Position == state.Position && player.ModFrozen, "first freeze event corrects owner body");
        player.Position += new Vector3(0, -1, 0); var falling = player.Position;
        bridge.ApplyState(player, state, isLocal: true);
        Check(player.Position == falling, "duplicate freeze event does not pin native falling");
        state.Flags &= unchecked((byte)~PlayerState.FlagFrozen);
        bridge.ApplyState(player, state, isLocal: true);
        Check((ushort)typeof(PlayerEntity).GetField("_frozenTimer",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(player)! == 1
            && player.Position == falling, "thaw hands off to the native one-tick break without repeating correction");
        state.Flags |= PlayerState.FlagFrozen; state.FreezeEventId = 2; state.Position += new Vector3(2, 0, 0);
        bridge.ApplyState(player, state, isLocal: true);
        Check(player.Position == state.Position, "new same-life freeze corrects once again");
        state.FreezeEventId = 1; state.Position += new Vector3(50, 0, 0);
        bridge.ApplyState(player, state, isLocal: true);
        Check(player.Position != state.Position, "older freeze event cannot replay owner correction");
        player.Controls.SetAnalogMovement(1, 1);
        bridge.NeutralizeInput(player);
        Check(!player.Controls.AnalogMoveActive, "neutralization clears held analog movement");
        player.ModSetFrozen(false);
        player.Controls.MoveDown.IsDown = true;
        player.Controls.SetAnalogMovement(0, 1);
        NetSession.RemoteIntents[0] = player.OwningScene.PlayerReplication.CaptureIntent(player);
        NetSession.RemoteIntentValid[0] = true;
        NetSession.RemoteIntentArrived[0] = NetSession.NetFrame - 31;
        Check(NetHooks.TryApplyRemoteInput(player, 0) && !player.Controls.MoveDown.IsDown
            && !player.Controls.AnalogMoveActive, "stale production input path neutralizes cached controls");
        player.Position += new Vector3(0, -1, 0); var environmentPosition = player.Position;
        NetHooks.AfterRemoteMovement(player);
        Check(player.Position == environmentPosition, "stale post-step path leaves native environmental movement intact");

        // Exercise the common suspension helper without a client socket. The
        // platform callbacks and resumed baseline delivery need device coverage.
        typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Client);
        typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, false);
        typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, 0);
        typeof(Scene).GetField("_cameraMode", System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance)!.SetValue(player.OwningScene, CameraMode.Player);
        var liveBridge = player.OwningScene.PlayerReplication;
        player.Controls.Shoot.IsDown = player.Controls.Shoot.IsPressed = true;
        player.Controls.SetAnalogMovement(1, 0);
        liveBridge.RecordPresses(player);
        ushort previousEdge = liveBridge.CaptureIntent(player).Presses[0];
        Check(previousEdge != 0, "normal held input has a retained edge before suspension");
        NetSession.SuspendClient(player.OwningScene);
        var suspended = liveBridge.CaptureIntent(player);
        Check(NetSession.ClientSuspended && NetSession.FreezeGameplay
            && !player.Controls.Shoot.IsDown && !player.Controls.AnalogMoveActive
            && suspended.Presses[0] == 0, "suspension clears held controls and retained input edges");
        NetSession.ResumeClient(player.OwningScene);
        Check(!NetSession.ClientSuspended && NetSession.FreezeGameplay
            && !NetSession.RemoteIntentValid[0], "resume waits for a fresh baseline and discards cached input");
        player.Controls.Jump.IsDown = player.Controls.Jump.IsPressed = true;
        liveBridge.RecordPresses(player);
        ushort resumedEdge = liveBridge.CaptureIntent(player).Presses[0];
        Check(resumedEdge != 0 && (byte)resumedEdge != (byte)previousEdge,
            "new same-life input after resume preserves edge sequence identity");
        NetSession.LocalHunter = Hunter.Samus;
        RespawnChoice.Request(Hunter.Spire, 0);
        RespawnChoice.ObserveAuthoritySpawn(Hunter.Spire);
        RespawnChoice.ObserveAuthoritySpawn(Hunter.Spire); // Repeated same-life bootstrap.
        NetSession.LocalHunter = Hunter.Samus;
        RespawnChoice.ObserveAuthorityRoster(Hunter.Samus, 0);
        Check(RespawnChoice.IdentifyHunter == Hunter.Spire,
            "repeat spawn then older roster cannot echo the previous hunter choice");
        NetSession.LocalHunter = Hunter.Spire;
        NetSession.LocalColor = 1;
        RespawnChoice.ObserveAuthorityRoster(Hunter.Spire, 1);
        Check(RespawnChoice.IdentifyColor == 0,
            "matching hunter with an older suit cannot finish the pending appearance");
        NetSession.LocalColor = 0;
        RespawnChoice.ObserveAuthorityRoster(Hunter.Spire, 0);
        NetSession.LocalHunter = Hunter.Samus;
        Check(RespawnChoice.IdentifyHunter == Hunter.Samus,
            "matching authority life and roster finish the pending hunter choice");
        typeof(NetSession).GetField("_lastSnapshotFrame", System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static)!.SetValue(null, 100u);
        typeof(NetSession).GetProperty(nameof(NetSession.SnapshotArrived))!.SetValue(null, 10u);
        typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, 20u);
        state.Health = 0; state.Flags = PlayerState.FlagActive; state.RespawnEligibleFrame = 240;
        bridge.ApplyState(player, state, isLocal: true);
        Check(player.RespawnTimer == 130, "dead owner countdown uses authority frame plus receipt age");
        typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, 21u);
        bridge.ApplyState(player, state, isLocal: true);
        Check(player.RespawnTimer == 129, "cached dead snapshot cannot freeze the displayed respawn schedule");
        var replicaBridge = new PlayerReplicationBridge(new FixtureHost(isReplica: true, frame: 200));
        replicaBridge.ApplyState(player, state, isLocal: false);
        Check(player.RespawnTimer == 40, "replica death timer uses its recording frame independently of live snapshot clocks");
        }
        finally { sim.Stop(); }
    }

    private sealed class FixtureHost : IPlayerReplicationHost
    {
        public FixtureHost(bool isReplica = false, uint frame = 100) { IsReplica = isReplica; Frame = frame; }
        public bool IsReplica { get; } public bool Active => true; public bool IsAuthority => false;
        public bool IsHost => false; public int LocalSlot => 0; public uint Frame { get; }
        public bool Settling => false; public bool GameplayReady => true; public bool CanSpawn => true;
        public int Ping(int slot) => 0;
        public bool Matches(int slot, ushort generation, ushort life) => slot == 0 && generation == 1 && life == 1;
        public bool TryGetIntent(int slot, out IntentPacket intent) { intent = default; return false; }
        public bool TryGetState(int slot, out PlayerState state) { state = default; return false; }
        public uint IntentAge(int slot) => 0;
        public void OnSpawn(PlayerEntity player) { }
        public void BeginLife(PlayerEntity player, in PlayerState state) { }
        public void Spawn(PlayerEntity player, in PlayerState state) { }
        public void ReplayDamage(PlayerEntity player, in PlayerState state) { }
        public void ReplayDeath(PlayerEntity player) { }
        public void NoteDeath(int slot) { }
        public int HealthFor(PlayerEntity player, int health, bool local) => health;
        public bool SamplePosition(int slot, bool presentation, out Vector3 position, out bool alt)
        { position = default; alt = false; return false; }
        public void StampAcknowledgement(ref IntentPacket intent) { }
    }
}
