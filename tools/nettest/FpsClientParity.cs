using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Physics;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

internal static class FpsClientParity
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    public sealed record State(int Slot, float[] Position, float[] Speed);
    public sealed record Result(State Local, State[] Authority, State[] Observed, long Snaps, float WorstSnap,
        long RejectedUpdates, int ScriptTicks, int PumpStalls);
    static float[] V(Vector3 v) => new[] { v.X, v.Y, v.Z };
    static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    public static int Worker(string[] args)
    {
        // role, data, port, scenario, output directory, lag profile, stall milliseconds
        bool nativeCadence = args.Length == 9 && args[8] == "native";
        var movementMode = nativeCadence ? NativeMovementMode.DiagnosticCadence60 : NativeMovementMode.Legacy60;
        string role = args[1], folder = Path.GetFullPath(args[5]);
        Directory.SetCurrentDirectory(args[2]); Paths.UpdatePaths(); Paths.ChooseMphPath(); Headless.Enter();
        var scenario = FpsScenario.Load(args[4]); int port = int.Parse(args[3]);
        if (role == "authority")
        {
            var server = new DedicatedServer(port, 2, MapRotation.SingleMatch(scenario.Room, GameMode.Battle, 120, 999))
            { ReplayPolicy = new ServerReplayPolicy(Enabled: false), DiagnosticNativeMovementMode = movementMode };
            bool placed = false;
            server.DiagnosticBeforeStep = world =>
            {
                string request = Path.Combine(folder, "spawn-request");
                if (placed || !File.Exists(request)) return;
                int slot = int.Parse(File.ReadAllText(request));
                var player = world.Players.Items[slot];
                if (!player.ModIsInPlay) return;
                player.Spawn(new(scenario.Spawn[0], scenario.Spawn[1] - 1, scenario.Spawn[2]),
                    new(scenario.Facing[0], scenario.Facing[1], scenario.Facing[2]), Vector3.UnitY, player.NodeRef, respawn: true);
                placed = true;
                string placement = Path.Combine(folder, "spawn-authority.json");
                Write(placement + ".tmp", new { slot, life = NetPlayerLifecycle.Get(slot), position = V(player.Position) });
                File.Move(placement + ".tmp", placement);
            };
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            server.Run(stop.Token); return 0;
        }
        NetLag.Configure(args[6]); NetLag.ConfigureSeed("731");
        if (!NetLaunch.Join("127.0.0.1", port, "fps-" + role, Hunter.Samus)) return 1;
        var keyboard = SyntheticInput.CreateKeyboard();
        var set = typeof(KeyboardState).GetMethod("SetKeyState", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .CreateDelegate<Action<KeyboardState, Keys, bool>>();
        var scene = new Scene(new Vector2i(256, 192), keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { });
        scene.NativeMovementMode = movementMode;
        try
        {
            NetLaunch.BuildPlayers(scene, Hunter.Samus, 0, teams: false);
            scene.AddRoom(scenario.Room, GameMode.Battle, playerCount: NetLaunch.RoomPlayerCount);
            scene.OnLoad(); NetSession.MarkMatchLoaded();
            var local = scene.Players.Items[NetSession.LocalSlot];
            foreach (var b in local.Controls.All) { b.Type = ButtonType.Key; b.Key = Keys.Unknown; }
            void Bind(Keybind b, Keys key) { b.Type = ButtonType.Key; b.Key = key; }
            Bind(local.Controls.MoveUp, Keys.W); Bind(local.Controls.MoveDown, Keys.S);
            Bind(local.Controls.MoveLeft, Keys.A); Bind(local.Controls.MoveRight, Keys.D); Bind(local.Controls.Jump, Keys.Space);
            local.Controls.MouseAim = false; local.Controls.KeyboardAim = false;
            GamepadContexts.Current = GamepadContext.Gameplay; GamepadContexts.Focused = true;
            var clock = Stopwatch.StartNew(); double next = 0;
            void Tick(bool advanceSimulation = true)
            {
                double wait = next - clock.Elapsed.TotalSeconds;
                if (wait > 0) Thread.Sleep(TimeSpan.FromSeconds(wait));
                next = Math.Max(next + 1.0 / 60, clock.Elapsed.TotalSeconds);
                if (advanceSimulation)
                {
                    scene.OnSimulationFrame(); NetSmoothing.PreparePresentation(1);
                }
                else
                {
                    // Drain transport with the measured endpoint held, including
                    // its velocity. A pad may bounce forever, so waiting for a
                    // physically resting state is not a valid convergence gate.
                    NetSession.Update(clock.Elapsed.TotalSeconds);
                    NetSession.SendIntent(NetPlayerBridge.CaptureIntent(local));
                }
            }
            for (int i = 0; i < 180; i++) Tick();
            if (!local.ModIsInPlay) throw new Exception("Local player not spawned");
            File.WriteAllText(Path.Combine(folder, role + ".ready"), NetSession.LocalSlot.ToString());
            while (!File.Exists(Path.Combine(folder, role == "a" ? "b.ready" : "a.ready")))
            { if (clock.Elapsed.TotalSeconds > 15) throw new TimeoutException("peer readiness"); Tick(); }
            int ticks = 0, stalls = 0;
            var localTrace = new List<State>();
            if (role == "a")
            {
                string request = Path.Combine(folder, "spawn-request");
                File.WriteAllText(request + ".tmp", local.SlotIndex.ToString());
                File.Move(request + ".tmp", request);
                string placement = Path.Combine(folder, "spawn-authority.json");
                while (!File.Exists(placement))
                { if (clock.Elapsed.TotalSeconds > 20) throw new TimeoutException("authority placement"); Tick(); }
                using var spawn = JsonDocument.Parse(File.ReadAllText(placement));
                ushort life = spawn.RootElement.GetProperty("life").GetUInt16();
                while (NetPlayerLifecycle.Get(local.SlotIndex) != life)
                { if (clock.Elapsed.TotalSeconds > 20) throw new TimeoutException("spawn replication"); Tick(); }
                if (nativeCadence && (scene.FrameCount & 1) != 0) Tick();
                // Establish the exact fixture state after the authority's new life
                // arrives, using the same scoped spawn path as normal replication.
                bool applyingSpawn = NetPlayerLifecycle.ApplyingSpawn;
                NetPlayerLifecycle.ApplyingSpawn = true;
                try { local.Spawn(new(scenario.Spawn[0], scenario.Spawn[1] - 1, scenario.Spawn[2]),
                    new(scenario.Facing[0], scenario.Facing[1], scenario.Facing[2]), Vector3.UnitY, local.NodeRef, respawn: true); }
                finally { NetPlayerLifecycle.ApplyingSpawn = applyingSpawn; }
                if (local.Position != new Vector3(scenario.Spawn[0], scenario.Spawn[1], scenario.Spawn[2]))
                    throw new Exception("Client did not establish the authority-approved scenario spawn");
                local.Speed = Vector3.Zero;
                scene.Random.SetRng1(scenario.Seed1); scene.Random.SetRng2(scenario.Seed2);
                for (int i = 0; i < scenario.SettleNativeTicks * 2; i++) Tick();
                scene.CameraSequences.Current = null;
                scene.SetFreeCamera(false);
                ulong startFrame = scene.FrameCount;
                var health = local.Health;
                PhysicsTrace.Start(Path.Combine(folder, "local.jsonl"));
                for (int frame = 0; frame < scenario.NativeTicks * 2; frame++)
                {
                    if (frame == scenario.NativeTicks && int.Parse(args[7]) > 0)
                    { Thread.Sleep(int.Parse(args[7])); stalls++; }
                    var input = scenario.AtPrimeTick(frame);
                    set(keyboard, Keys.W, input.MoveY > 0); set(keyboard, Keys.S, input.MoveY < 0);
                    set(keyboard, Keys.D, input.MoveX > 0); set(keyboard, Keys.A, input.MoveX < 0); set(keyboard, Keys.Space, input.Jump);
                    Tick(); ticks++;
                    if (local.Health != health) throw new Exception("Unexpected damage contaminated network fixture");
                    localTrace.Add(new(local.SlotIndex, V(local.Position), V(local.Speed)));
                }
                PhysicsTrace.Stop();
                var captured = File.ReadLines(Path.Combine(folder, "local.jsonl"))
                    .Select(line => JsonSerializer.Deserialize<PhysicsSample>(line, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!)
                    .Where(row => row.Player == local.SlotIndex).ToArray();
                int traceStride = nativeCadence ? 2 : 1;
                if (captured.Length != ticks / traceStride) throw new Exception("Missing local input trace");
                for (int i = 0; i < captured.Length; i++)
                {
                    var input = scenario.AtPrimeTick(i * traceStride); var stage = captured[i].Stages!;
                    if (captured[i].Frame != startFrame + (ulong)((i + 1) * traceStride)
                        || stage.MoveX != input.MoveX || stage.MoveY != input.MoveY || stage.JumpPressed != input.Jump)
                        throw new Exception($"Network client did not consume scripted input at {i}");
                }
                File.WriteAllLines(Path.Combine(folder, "local-boundaries.jsonl"), captured.Select(row =>
                    JsonSerializer.Serialize(row with { Frame = row.Frame - startFrame, Player = 0 },
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })));
                Write(Path.Combine(folder, "trajectory.json"), localTrace);
                foreach (var key in new[] { Keys.W, Keys.S, Keys.D, Keys.A, Keys.Space }) set(keyboard, key, false);
                for (int i = 0; i < 180; i++) Tick(advanceSimulation: false); // Publish the exact captured endpoint and drain latency queues.
                File.WriteAllText(Path.Combine(folder, "a.done"), "done");
            }
            else
            {
                while (!File.Exists(Path.Combine(folder, "a.done")))
                { if (clock.Elapsed.TotalSeconds > 30) throw new TimeoutException("scenario completion"); Tick(); }
                for (int i = 0; i < 60; i++) Tick();
            }
            var authority = Enumerable.Range(0, PlayerEntity.MaxPlayers).Where(i => NetSession.RemoteStateValid[i])
                .Select(i => new State(i, V(NetSession.RemoteStates[i].Position), V(NetSession.RemoteStates[i].Speed))).ToArray();
            var observed = scene.Players.Items.Where(p => p.ModIsInPlay).Select(p => new State(p.SlotIndex, V(p.Position), V(p.Speed))).ToArray();
            Write(Path.Combine(folder, role + ".json"), new Result(new(local.SlotIndex, V(local.Position), V(local.Speed)), authority, observed,
                NetPlayerBridge.Snaps, NetPlayerBridge.WorstSnap, NetPlayerBridge.RejectedUpdates, ticks, stalls));
            if (role == "a") while (!File.Exists(Path.Combine(folder, "b.json")))
            { if (clock.Elapsed.TotalSeconds > 35) throw new TimeoutException("observer completion"); Tick(advanceSimulation: false); }
            return 0;
        }
        finally { PhysicsTrace.Stop(); scene.DoCleanup(); scene.UnloadGl(); NetSession.Stop(); }
    }

    public static int Run(string data, string scenarioPath, string output, bool nativeCadence = false, string? reference = null)
    {
        data = Path.GetFullPath(data); scenarioPath = Path.GetFullPath(scenarioPath); output = Path.GetFullPath(output);
        if (nativeCadence && (reference == null || !File.Exists(reference)))
            throw new FileNotFoundException("Native client parity requires the original-ROM reference.", reference);
        reference = reference == null ? null : Path.GetFullPath(reference);
        if (Directory.Exists(output)) throw new IOException("Use a new output directory");
        Directory.CreateDirectory(output); var scenario = FpsScenario.Load(scenarioPath);
        if (scenario.PlacementRequired != null || scenario.InitialImpulse != null || scenario.InitialFreezeNativeTicks > 0 || scenario.Inputs.Any(input => input.Fire))
        {
            Console.Error.WriteLine("Client parity requires a verified digital-input fixture without injected impulses or combat input.");
            return 2;
        }
        var reports = new List<object>(); int failures = 0; State[]? baseline = null;
        foreach (var profile in new[] { ("0ms", "0", 0), ("50ms", "50", 0), ("100ms", "100", 0),
            ("200ms", "200", 0), ("jitter", "100:40", 0), ("stall", "100", 150) })
        {
            string dir = Path.Combine(output, profile.Item1); Directory.CreateDirectory(dir);
            using var socket = new System.Net.Sockets.UdpClient(0);
            int port = ((System.Net.IPEndPoint)socket.Client.LocalEndPoint!).Port; socket.Close();
            var logs = new List<StreamWriter>();
            Process Start(string role)
            {
                var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                foreach (var arg in new[] { "--fps-client-worker", role, data, port.ToString(), scenarioPath, dir, profile.Item2, profile.Item3.ToString() }) info.ArgumentList.Add(arg);
                if (nativeCadence) info.ArgumentList.Add("native");
                var process = Process.Start(info)!;
                var log = new StreamWriter(Path.Combine(dir, role + ".log")) { AutoFlush = true };
                logs.Add(log);
                object gate = new();
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock(gate) log.WriteLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock(gate) log.WriteLine(e.Data); };
                process.BeginOutputReadLine(); process.BeginErrorReadLine(); return process;
            }
            using var server = Start("authority"); Thread.Sleep(1200);
            using var a = Start("a"); using var b = Start("b");
            bool exited = a.WaitForExit(35000) && b.WaitForExit(5000);
            if (!a.HasExited) a.Kill(true); if (!b.HasExited) b.Kill(true); if (!server.HasExited) server.Kill(true);
            a.WaitForExit(); b.WaitForExit(); server.WaitForExit();
            foreach (var log in logs) log.Dispose();
            if (!exited || a.ExitCode != 0 || b.ExitCode != 0) { failures++; reports.Add(new { profile = profile.Item1, passed = false, error = "worker failed; see logs" }); continue; }
            var ar = JsonSerializer.Deserialize<Result>(File.ReadAllText(Path.Combine(dir,"a.json")))!;
            var br = JsonSerializer.Deserialize<Result>(File.ReadAllText(Path.Combine(dir,"b.json")))!;
            var accepted = br.Authority.SingleOrDefault(s => s.Slot == ar.Local.Slot);
            var seen = br.Observed.SingleOrDefault(s => s.Slot == ar.Local.Slot);
            double Distance(float[] x, float[] y) => Math.Sqrt(x.Zip(y, (v,w) => (double)(v-w)*(v-w)).Sum());
            double authorityError = accepted == null ? double.PositiveInfinity : Distance(ar.Local.Position, accepted.Position);
            double observerError = seen == null ? double.PositiveInfinity : Distance(ar.Local.Position, seen.Position);
            var trajectory = JsonSerializer.Deserialize<State[]>(File.ReadAllText(Path.Combine(dir,"trajectory.json")))!;
            baseline ??= trajectory;
            double trajectoryError = trajectory.Length == baseline.Length
                ? trajectory.Zip(baseline, (x,y) => Math.Max(Distance(x.Position,y.Position), Distance(x.Speed,y.Speed))).Max()
                : double.PositiveInfinity;
            bool nativePassed = true;
            if (nativeCadence)
            {
                var comparisons = PhysicsTrace.Compare(reference!, Path.Combine(dir, "local-boundaries.jsonl"));
                Write(Path.Combine(dir, "native-comparison.json"), comparisons);
                nativePassed = comparisons.All(row => row.Passed);
            }
            // Protocol 33 reports position, not the owner's integration velocity.
            // The authority derives per-report-frame velocity; after holding
            // the endpoint it must be zero, and the observer must adopt it.
            double ownerToAuthoritySpeedDifference = accepted == null ? double.PositiveInfinity : Distance(ar.Local.Speed, accepted.Speed);
            double authoritySpeedError = accepted == null ? double.PositiveInfinity : Distance(new float[3], accepted.Speed);
            double observerSpeedError = seen == null || accepted == null ? double.PositiveInfinity : Distance(accepted.Speed, seen.Speed);
            bool passed = nativePassed && authoritySpeedError <= .001 && observerSpeedError <= .001 && trajectoryError <= .001 && ar.RejectedUpdates == 0 && authorityError <= .001 && observerError <= .001 && ar.ScriptTicks == scenario.NativeTicks * 2;
            if (!passed) failures++;
            reports.Add(new { profile = profile.Item1, passed, nativePassed, authorityError, observerError, authoritySpeedError, observerSpeedError, ownerToAuthoritySpeedDifference, trajectoryError, ar.Snaps, ar.WorstSnap, ar.RejectedUpdates, ar.PumpStalls });
            Console.WriteLine($"[fps-client-parity] {profile.Item1}: {(passed ? "PASS" : "FAIL")} authority={authorityError} observer={observerError} trajectory={trajectoryError}");
        }
        Write(Path.Combine(output, "report.json"), new { movementAuthority = "owner-authored; authority acceptance and observer convergence", nativeCadence, reference, velocityComparison = "held-endpoint report velocity zero; observer equals authority; owner physics velocity retained separately", reports, failures });
        return failures == 0 ? 0 : 1;
    }
}
