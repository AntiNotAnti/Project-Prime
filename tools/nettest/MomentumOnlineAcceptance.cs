using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using OpenTK.Windowing.GraphicsLibraryFramework;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Explicit acceptance options; the original two-client worker keeps its assertions.
internal sealed record MomentumOnlineAcceptanceOptions(
    bool Spectator = false, int MinimumHumans = 1, int MinimumBots = 0,
    string Latency = "0:0", string Loss = "0", string Reorder = "0", string Duplicate = "0", int Seed = 1,
    bool RequireRejoin = false, bool RequireRespawn = false, int MinimumTransitions = 0,
    bool LobbyOwner = false, int LobbyBots = 0, int LobbyHumans = 4, bool RequireMapChange = false);

internal sealed class MomentumOnlineAcceptance : IDisposable
{
    private readonly MomentumOnlineAcceptanceOptions _options;
    private readonly (int Rtt, int Jitter, double Loss, double Reorder, double Duplicate, int Seed) _old;
    private readonly Dictionary<int, ushort> _generations = new();
    private readonly Dictionary<int, ushort> _lives = new();
    private readonly HashSet<int> _dead = new();
    private readonly HashSet<int> _missing = new();
    private readonly List<object> _samples = new();
    // Ages are NetSession simulation frames (60 Hz), not milliseconds.
    private readonly long[] _snapshotAgeBins = new long[601]; // exact0..599;600means>=600
    private long _snapshotSamples, _snapshotMissing;
    private ulong _snapshotAgeSum;
    private uint _snapshotAgeMax;
    private bool _populationObserved;
    private int _ticks, _peakHumans, _peakBots, _rejoins, _respawns, _botSamples, _humanSamples;
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    internal MomentumOnlineAcceptance(MomentumOnlineAcceptanceOptions options)
    {
        _options = options;
        _old = (NetLag.RoundTripMs, NetLag.JitterMs, NetLag.LossPercent, NetLag.ReorderRate, NetLag.DuplicateRate, NetLag.Seed);
        Require(options.MinimumHumans is >= 0 and <= 8 && options.MinimumBots is >= 0 and <= 8
            && options.MinimumHumans + options.MinimumBots <= 8, "bounded participant expectations");
        Require(NetLag.Configure(options.Latency) && NetLag.ConfigureLoss(options.Loss)
            && NetLag.ConfigureReorder(options.Reorder) && NetLag.ConfigureDuplicate(options.Duplicate)
            && NetLag.ConfigureSeed(options.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture)), "valid explicit network fault profile");
    }
    internal void PrepareLobby()
    {
        if (!_options.LobbyOwner) return;
        Require(!_options.Spectator && _options.LobbyBots is >= 0 and <= 7
            && _options.LobbyHumans is >= 1 and <= 8 && _options.LobbyBots + _options.LobbyHumans <= 8,
            "bounded real lobby setup");
        WaitLobby(() => NetSession.ServerSession.HasValue && NetSession.LocalIsLobbyOwner, "actual first-peer lobby ownership");
        Require(NetSession.IsInLobby && NetSession.PersistentLobby, "owner setup requires genuine persistent lobby");
        Require(!NetSession.SlotIsBot.Where((_, slot) => NetSession.SlotOccupied[slot]).Any(value => value), "lobby starts without bots");
        for (int index = 0; index < _options.LobbyBots; index++)
        {
            int expectedBots = index + 1;
            LobbyCommand(() => NetSession.SendLobbyCommand(LobbyCommandType.AddBot, hunter: (byte)(index % 7), botLevel: 1),
                () => Enumerable.Range(0, NetSession.SlotOccupied.Length).Count(slot => NetSession.SlotOccupied[slot] && NetSession.SlotIsBot[slot]) == expectedBots,
                "AddBot acknowledgement and real roster convergence");
        }
        WaitLobby(() => Enumerable.Range(0, NetSession.SlotOccupied.Length)
            .Count(slot => NetSession.SlotOccupied[slot] && !NetSession.SlotIsBot[slot]
                && (GameState.Nicknames[slot]?.StartsWith("MomentumCPU-", StringComparison.Ordinal) == true
                    || GameState.Nicknames[slot]?.StartsWith("MovementSpectator-", StringComparison.Ordinal) == true)) >= _options.LobbyHumans,
            "requested human peers genuinely admitted before start");
        LobbyCommand(() => NetSession.SendLobbyCommand(LobbyCommandType.StartMatch), () => NetSession.ShouldLoadMatch,
            "server accepts match start and requests actual room load");
        Console.WriteLine($"MOVEMENT LOBBY SETUP PASS humans={_options.LobbyHumans} bots={_options.LobbyBots}");
    }
    private static void LobbyCommand(Func<bool> send, Func<bool> applied, string description)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            ushort revision = NetSession.SessionRevision;
            Require(send(), "real owner submits " + description);
            WaitLobby(() => !NetSession.LobbyCommandPending, "real command acknowledgement");
            if (NetSession.LobbyMessage.Length == 0)
            { WaitLobby(applied, description); return; }
            // This is the normal client retry after reviewing a newer server state,
            // never a forged ExpectedRevision or suppressed server rejection.
            Require(NetSession.LobbyMessage.Contains("lobby changed", StringComparison.OrdinalIgnoreCase),
                "server rejected command: " + NetSession.LobbyMessage);
            WaitLobby(() => NetSession.SessionRevision != revision, "newer authoritative lobby revision before retry");
        }
        throw new InvalidOperationException("Lobby stayed busy after eight genuine stale-revision retries.");
    }
    private static void WaitLobby(Func<bool> condition, string description)
    {
        var wait = Stopwatch.StartNew();
        while (!condition() && wait.Elapsed.TotalSeconds < 40)
        { NetSession.Pump(); Connected(); Thread.Sleep(5); }
        Require(condition(), description + ": " + NetSession.LobbyMessage);
    }
    internal void Observe(Scene scene)
    {
        if (NetSession.WorldIsReady && NetRoomChange.GameplayReady && !NetSession.FreezeGameplay
            && NetSession.LocalSlot >= 0 && NetSession.LocalSlot < scene.Players.Items.Count)
        {
            var owner = scene.Players.Items[NetSession.LocalSlot];
            if (owner.LoadFlags.TestFlag(LoadFlags.Active) && owner.Health > 0
                && !owner.Flags2.TestFlag(PlayerFlags2.Spectating))
            {
                uint age = NetSession.SnapshotAge;
                if (age == UInt32.MaxValue) _snapshotMissing++;
                else
                {
                    _snapshotSamples++; _snapshotAgeSum += age; _snapshotAgeMax = Math.Max(_snapshotAgeMax, age);
                    _snapshotAgeBins[Math.Min((int)Math.Min(age, 600u), 600)]++;
                }
            }
        }
        int humans = 0, bots = 0;
        for (int slot = 0; slot < NetSession.SlotOccupied.Length; slot++)
        {
            if (!NetSession.SlotOccupied[slot])
            { if (_generations.ContainsKey(slot)) _missing.Add(slot); continue; }
            bool bot = NetSession.SlotIsBot[slot];
            if (bot) bots++; else humans++;
            ushort generation = NetPlayerLifecycle.Generation(slot), life = NetPlayerLifecycle.Get(slot);
            if (generation != 0)
            {
                if (_generations.TryGetValue(slot, out ushort prior) && prior != generation)
                {
                    if (_missing.Contains(slot)) _rejoins++;
                    _lives.Remove(slot); _dead.Remove(slot);
                }
                _generations[slot] = generation; _missing.Remove(slot);
            }
            if (slot >= scene.Players.Items.Count) continue;
            var player = scene.Players.Items[slot];
            bool active = player.LoadFlags.TestFlag(LoadFlags.Active);
            if (active)
            {
                Require(Finite(player.Position) && Finite(player.Speed), "finite accepted participant body");
                if (slot != NetSession.LocalSlot)
                {
                    Require(NetHooks.IsPuppet(player) && !scene.MomentumOwnsPlayer(player), "remote human/server bot remains a puppet");
                    if (bot) _botSamples++; else _humanSamples++;
                }
                if (player.Health == 0 && life != 0) _dead.Add(slot);
                if (player.Health > 0 && life != 0 && _dead.Contains(slot)
                    && _lives.TryGetValue(slot, out ushort oldLife) && oldLife != life)
                { _respawns++; _dead.Remove(slot); }
                if (life != 0) _lives[slot] = life;
            }
        }
        _populationObserved |= humans >= _options.MinimumHumans && bots >= _options.MinimumBots;
        _peakHumans = Math.Max(_peakHumans, humans); _peakBots = Math.Max(_peakBots, bots);
        if (_ticks++ % 60 == 0) _samples.Add(new { tick = _ticks, humans, bots, rejoins = _rejoins, respawns = _respawns });
    }
    internal void Complete()
    {
        Require(_populationObserved, "observed requested actual roster populations");
        if (_options.MinimumBots > 0) Require(_botSamples > 0, "actual server bot puppets observed active");
        if (_options.RequireRejoin) Require(_rejoins > 0, "actual absent occupant replaced by a new slot generation");
        if (_options.RequireRespawn) Require(_respawns > 0, "actual accepted death then new living life observed");
    }
    internal object Report => new { options = _options, peakHumans = _peakHumans, peakBots = _peakBots,
        observedRejoins = _rejoins, observedRespawns = _respawns, botPuppetSamples = _botSamples, humanPuppetSamples = _humanSamples,
        snapshotAge = SnapshotAgeReport, samples = _samples, scope = "real UDP roster and accepted lifecycle observations; no direct death/spawn/role/ready writes; no hit or lag compensation claim" };
    private object SnapshotAgeReport
    {
        get
        {
            long cumulative = 0, target = (long)Math.Ceiling(_snapshotSamples * .95);
            int percentile = 0;
            if (_snapshotSamples > 0)
                for (; percentile < _snapshotAgeBins.Length - 1; percentile++)
                { cumulative += _snapshotAgeBins[percentile]; if (cumulative >= target) break; }
            return new { units = "60 Hz simulation frames", scope = "ready active living local gameplay; excludes spectator/dead/loading",
                samples = _snapshotSamples, missingBeforeFirstSnapshot = _snapshotMissing,
                meanFrames = _snapshotSamples == 0 ? (double?)null : (double)_snapshotAgeSum / _snapshotSamples,
                maxFrames = _snapshotSamples == 0 ? (uint?)null : _snapshotAgeMax,
                p95Frames = _snapshotSamples == 0 || percentile == 600 ? (int?)null : percentile,
                p95AtLeast600 = _snapshotSamples > 0 && percentile == 600,
                histogram = _snapshotAgeBins.Select((count, frames) => new { frames, count }).Where(bin => bin.count > 0).ToArray(),
                overflowBin = "600 = at least600 frames" };
        }
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    public void Dispose()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        NetLag.Configure(_old.Rtt + ":" + _old.Jitter); NetLag.ConfigureLoss(_old.Loss.ToString(culture) + "%");
        NetLag.ConfigureReorder(_old.Reorder.ToString(culture)); NetLag.ConfigureDuplicate(_old.Duplicate.ToString(culture));
        NetLag.ConfigureSeed(_old.Seed.ToString(culture));
    }

    internal static int RunSpectator(int port, MovementModel expected, string outputPath, int seconds, MomentumOnlineAcceptanceOptions options)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        Scene? scene = null;
        try
        {
            Require(options.Spectator && seconds is >= 6 and <= 120 && port is > 0 and <= 65535, "bounded genuine spectator worker");
            Require(!NetSession.Active && !DemoPlayback.IsActive, "spectator starts without session");
            Require(ServerSim.Available(out string reason), reason); Headless.Enter();
            using var acceptance = new MomentumOnlineAcceptance(options);
            Require(NetLaunch.Connect("127.0.0.1", port, "MovementSpectator-" + Environment.ProcessId,
                Hunter.Samus, timeoutMs: 15000, color: 0, spectate: true), "actual spectator UDP admission: " + NetLaunch.LastJoinError);
            acceptance.PrepareLobby();
            var wait = Stopwatch.StartNew();
            while (!NetSession.ShouldLoadMatch && wait.Elapsed.TotalSeconds < 40)
            { NetSession.Pump(); Connected(); Thread.Sleep(5); }
            Require(NetSession.ShouldLoadMatch && NetSession.ActiveMatchDefinition.HasValue, "actual spectator load request");
            var definition = NetSession.ActiveMatchDefinition!.Value;
            Require(definition.MovementModel == expected, "spectator recorded effective server choice");
            int slot = NetSession.LocalSlot; ushort match = NetSession.CurrentMatchId; ulong epoch = NetSession.AuthorityEpoch;
            NetLaunch.DisableCheatsForMatch();
            scene = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { }, lifetimeOwned: value => scene = value);
            NetLaunch.BuildPlayers(scene, NetSession.LocalHunter, NetSession.LocalColor, teams: GameState.IsTeamMode(definition.Mode));
            scene.AddRoom(definition.RoomKey, definition.Mode, playerCount: NetLaunch.RoomPlayerCount);
            definition.ApplyInitialConfiguration(scene.GameState);
            typeof(Scene).GetField("_developerMomentumDiagnostics", hidden)!.SetValue(scene, true);
            scene.OnLoad();
            // MatchStart.Begin enters the chosen spectator role after loading the real actors.
            SpectatorMode.Start(); NetSession.MarkMatchLoaded();
            wait.Restart(); double next = 0;
            while ((!NetSession.WorldIsReady || !NetRoomChange.GameplayReady || NetSession.FreezeGameplay) && wait.Elapsed.TotalSeconds < 40)
            { WaitUntil(wait, ref next); scene.OnSimulationFrame(); Connected(); }
            Require(NetSession.WorldIsReady && NetRoomChange.GameplayReady && !NetSession.FreezeGameplay, "genuine spectator load barrier release");
            long firstSteps = scene.DeveloperMomentumSteps; int spectatorTicks = 0;
            wait.Restart(); next = 0;
            while (wait.Elapsed.TotalSeconds < seconds)
            {
                WaitUntil(wait, ref next); scene.OnSimulationFrame(); Connected();
                Require(NetSession.CurrentMatchId == match && NetSession.AuthorityEpoch == epoch && NetRoomChange.GameplayReady, "spectator remains in frozen generation");
                Require(SpectatorMode.PreferSpectator && SpectatorMode.IsSpectating
                    && scene.Players.Items[slot].Flags2.TestFlag(PlayerFlags2.Spectating), "native spectator role remains active");
                Require(scene.GameState.MovementModel == expected && !scene.Players.Items[slot].ModMomentumEligible, "native spectator gate suppresses movement adapter");
                Require(scene.DeveloperMomentumSteps == firstSteps, "spectator scene performs zero local movement solver steps");
                acceptance.Observe(scene); spectatorTicks++;
            }
            Require(spectatorTicks > 120, "sustained native spectator simulation"); acceptance.Complete();
            string output = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, expected = expected.ToString(), slot, match, epoch,
                spectatorTicks, wallSeconds = wait.Elapsed.TotalSeconds, simulationStepSeconds = MphRead.Mods.Render.FrameTiming.StepSeconds, solverSteps = scene.DeveloperMomentumSteps - firstSteps, acceptance = acceptance.Report }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("MOMENTUM ONLINE SPECTATOR PASS ticks=" + spectatorTicks); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("MOMENTUM ONLINE SPECTATOR FAIL " + error); return 1; }
        finally { NetCampaignEvidence.Write("simulation-client"); try { if (scene != null) MomentumSceneTests.Release(scene); } finally { NetSession.Stop(); } }
    }
    // Separately scoped from the fixed-generation fixture: native NetRoomChange owns
    // every rebuild and MatchLoaded acknowledgement after this initial room load.
    internal static int RunTransitions(int port, MovementModel expected, string outputPath, int seconds, MomentumOnlineAcceptanceOptions options)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        Scene? scene = null;
        try
        {
            Require(!options.Spectator && options.MinimumTransitions is >= 1 and <= 8
                && seconds is >= 12 and <= 120, "bounded transition worker");
            Require(!NetSession.Active && !DemoPlayback.IsActive, "transition starts without session");
            Require(ServerSim.Available(out string reason), reason); Headless.Enter();
            using var acceptance = new MomentumOnlineAcceptance(options);
            Require(NetLaunch.Connect("127.0.0.1", port, "MovementTransition-" + Environment.ProcessId,
                Hunter.Samus, timeoutMs: 15000, color: 0), "actual transition UDP admission: " + NetLaunch.LastJoinError);
            acceptance.PrepareLobby();
            var wait = Stopwatch.StartNew();
            while (!NetSession.ShouldLoadMatch && wait.Elapsed.TotalSeconds < 40)
            { NetSession.Pump(); Connected(); Thread.Sleep(5); }
            Require(NetSession.ShouldLoadMatch && NetSession.ActiveMatchDefinition.HasValue, "actual initial load request");
            var definition = NetSession.ActiveMatchDefinition!.Value;
            Require(definition.MovementModel == expected, "initial effective server choice");
            int slot = NetSession.LocalSlot;
            NetLaunch.DisableCheatsForMatch();
            var keyboard = SyntheticInput.CreateKeyboard();
            var set = typeof(KeyboardState).GetMethod("SetKeyState", hidden | BindingFlags.Public)!
                .CreateDelegate<Action<KeyboardState, Keys, bool>>();
            scene = new Scene(new Vector2i(256, 192), keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { }, lifetimeOwned: value => scene = value);
            NetLaunch.BuildPlayers(scene, NetSession.LocalHunter, NetSession.LocalColor, teams: GameState.IsTeamMode(definition.Mode));
            scene.AddRoom(definition.RoomKey, definition.Mode, playerCount: NetLaunch.RoomPlayerCount);
            definition.ApplyInitialConfiguration(scene.GameState);
            typeof(Scene).GetField("_developerMomentumDiagnostics", hidden)!.SetValue(scene, true);
            scene.OnLoad();
            var inputMode = typeof(Scene).GetField("_inputMode", hidden)!;
            inputMode.SetValue(scene, Enum.Parse(inputMode.FieldType, "All"));
            NetSession.MarkMatchLoaded();
            var completed = new HashSet<(ushort Match, ulong Epoch)>();
            var generations = new List<object>();
            var actualRooms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PlayerEntity? bound = null; int ticks = 0, readyTicks = 0, blockedTicks = 0, solverTicks = 0;
            wait.Restart(); double next = 0;
            while (wait.Elapsed.TotalSeconds < seconds)
            {
                WaitUntil(wait, ref next);
                var local = scene.Players.Items[slot];
                if (!ReferenceEquals(local, bound))
                {
                    foreach (var control in local.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
                    local.Controls.MoveUp.Key = Keys.W; local.Controls.Jump.Key = Keys.Space;
                    local.Controls.MouseAim = false; local.Controls.KeyboardAim = true; bound = local;
                }
                set(keyboard, Keys.W, ticks % 240 < 120); set(keyboard, Keys.Space, ticks % 90 == 30);
                long before = scene.DeveloperMomentumSteps;
                scene.OnSimulationFrame(); Connected();
                long delta = scene.DeveloperMomentumSteps - before;
                Require(delta is >= 0 and <= 1, "single solver application through native transition lifecycle");
                if (expected == MovementModel.Native) Require(delta == 0, "Native transition never enables Momentum");
                bool ready = NetSession.WorldIsReady && NetRoomChange.GameplayReady && !NetSession.FreezeGameplay;
                if (!ready)
                {
                    Require(delta == 0, "loading/barrier transition suppresses movement adapter"); blockedTicks++;
                }
                else
                {
                    var current = NetSession.ActiveMatchDefinition;
                    Require(current?.MovementModel == expected && scene.GameState.MovementModel == expected,
                        "native room rebuild reapplies frozen selected model");
                    Require(NetSession.CurrentMatchId != 0 && NetSession.AuthorityEpoch != 0, "real ready transition identity");
                    var identity = (NetSession.CurrentMatchId, NetSession.AuthorityEpoch);
                    if (completed.Add(identity))
                    {
                        string actualRoom = scene.Room?.Meta.Name ?? "";
                        Require(actualRoom.Length > 0 && String.Equals(actualRoom, current?.RoomKey, StringComparison.OrdinalIgnoreCase),
                            "actual loaded room matches authoritative generation");
                        actualRooms.Add(actualRoom);
                        generations.Add(new { match = identity.Item1, epoch = identity.Item2, room = actualRoom, tick = ticks });
                        scene.CameraSequences.Current?.End();
                    }
                    var owner = scene.Players.Items[slot];
                    Require(!owner.IsBot && !NetHooks.IsPuppet(owner) && Finite(owner.Position) && Finite(owner.Speed), "real owner remains finite after rebuild");
                    acceptance.Observe(scene); readyTicks++; if (delta == 1) solverTicks++;
                }
                ticks++;
            }
            Require(completed.Count >= options.MinimumTransitions + 1, "requested completed authoritative transitions observed");
            if (options.RequireMapChange) Require(actualRooms.Count >= 2, "two genuinely loaded distinct room identities required");
            Require(readyTicks > 120 && blockedTicks > 0, "observed actual ready gameplay and transition barrier");
            Require(expected == MovementModel.Native || solverTicks > 0, "Momentum executes after genuine barrier release");
            acceptance.Complete();
            string output = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, expected = expected.ToString(), slot,
                ticks, wallSeconds = wait.Elapsed.TotalSeconds, simulationStepSeconds = MphRead.Mods.Render.FrameTiming.StepSeconds, readyTicks, blockedTicks, solverTicks, completedTransitions = completed.Count - 1, generations, distinctActualRooms = actualRooms.OrderBy(room => room).ToArray(),
                acceptance = acceptance.Report,
                scope = "real UDP and native NetRoomChange room/rematch rebuild; simulation only, no forced readiness, role, spawn or lifecycle" }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("MOMENTUM ONLINE TRANSITION PASS transitions=" + (completed.Count - 1)); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("MOMENTUM ONLINE TRANSITION FAIL " + error); return 1; }
        finally { NetCampaignEvidence.Write("simulation-client"); try { if (scene != null) MomentumSceneTests.Release(scene); } finally { NetSession.Stop(); } }
    }
    // Same bounded deadline policy as the original owner worker. Loading stalls
    // do not become an unbounded catch-up loop; each engine call remains one 60 Hz step.
    private static void WaitUntil(Stopwatch clock, ref double next)
    {
        next += 1d / 60;
        double remaining = next - clock.Elapsed.TotalSeconds;
        if (remaining > .002) Thread.Sleep((int)((remaining - .001) * 1000));
        while (clock.Elapsed.TotalSeconds < next) Thread.SpinWait(32);
        if (clock.Elapsed.TotalSeconds - next > .25) next = clock.Elapsed.TotalSeconds;
    }
    private static void Connected() => Require(NetSession.Active && NetSession.IsClient && !NetSession.IsAuthority
        && !NetSession.Refused && !NetSession.SessionTimedOut && !NetSession.ConnectionLost, "spectator real UDP remains connected");
}
