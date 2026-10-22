// Golden values from the native local keyboard/body seam, not a movement implementation.
// ServerSim retains its dedicated Roam camera: this does not prove client/HUD or multiplayer parity.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;
internal static class NativeMovementBaselineTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const double Tolerance = 0.00001; // Absolute engine units; integers/flags compare exactly.
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static readonly string[] Scenarios = { "idle", "forward-release", "strafe-release",
        "diagonal-release", "direction-change-reversal", "single-jump", "repeated-jump", "air-input-landing" };
    private static void Session(string name, object value) => typeof(NetSession).GetProperty(name)!.SetValue(null, value);
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static float[] V(Vector3 value) => new[] { value.X, value.Y, value.Z };

    internal static int Run(string room, string fixture, bool record)
    {
        try
        {
            Require(ServerSim.Available(out string reason), reason);
            string sourceSha = SourceSha();
            var runs = new List<object>();
            foreach (Hunter hunter in Hunters)
                foreach (string scenario in Scenarios)
                {
                    runs.Add(Capture(room, hunter, scenario));
                    Console.WriteLine($"NATIVE BASELINE captured {hunter}/{scenario}");
                }
            string[] parts = Array.ConvertAll(Hunters, hunter => Path.GetFileNameWithoutExtension(fixture) + "-" + hunter.ToString().ToLowerInvariant() + ".json");
            var data = new { schema = 1, sourceSha, room, simulationHz = 60, pairedBoundaryHz = 30,
                seed1 = 8128, seed2 = 0x8128a5a5u, warmupTicks = 120, scenarioTicks = 180,
                tolerance = Tolerance, scope = "local ServerSim native keyboard/body; Roam camera; stock biped; keyboard aim does not turn the body in this Roam seam; no multiplayer proof",
                units = "Position: engine units; Speed: native internal velocity (not units/second); Values: raw native table integers",
                parts };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
            if (record)
            {
                Require(!File.Exists(fixture), "Refusing to overwrite golden fixture; use a new explicit path or remove it deliberately.");
                string path = Path.GetFullPath(fixture);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                for (int i = 0; i < Hunters.Length; i++)
                {
                    string partPath = Path.Combine(Path.GetDirectoryName(path)!, parts[i]);
                    Require(!File.Exists(partPath), "Refusing to overwrite golden part: " + partPath);
                }
                for (int i = 0; i < Hunters.Length; i++)
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, parts[i]),
                        JsonSerializer.Serialize(runs.GetRange(i * Scenarios.Length, Scenarios.Length))
                            .Replace("},{\"tick\":", "},\n{\"tick\":").Replace("},{\"hunter\":", "},\n{\"hunter\":") + Environment.NewLine);
                File.WriteAllText(path, json + Environment.NewLine);
                Console.WriteLine($"NATIVE BASELINE RECORDED {path} source={sourceSha}");
            }
            else
            {
                using var expected = JsonDocument.Parse(File.ReadAllText(fixture));
                using var actual = JsonDocument.Parse(json);
                Compare(expected.RootElement, actual.RootElement, "$", metadata: true);
                for (int i = 0; i < Hunters.Length; i++)
                {
                    using var expectedPart = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(fixture))!, parts[i])));
                    using var actualPart = JsonDocument.Parse(JsonSerializer.Serialize(runs.GetRange(i * Scenarios.Length, Scenarios.Length)));
                    Compare(expectedPart.RootElement, actualPart.RootElement, Hunters[i].ToString());
                }
                Console.WriteLine($"NATIVE BASELINE PASS 56 native trajectories; golden source={expected.RootElement.GetProperty("sourceSha").GetString()}, current={sourceSha}, tolerance={Tolerance}");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"NATIVE BASELINE FAIL: {ex}"); return 1; }
    }

    private static object Capture(string room, Hunter hunter, string scenario)
    {
        // Fresh seeded room for every scenario. Never assign position/speed per tick.
        Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
        var roster = RosterPacket.Create();
        roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 1;
        roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Hunters[0] = (byte)hunter;
        roster.Names[0] = "Native baseline " + hunter;
        var sim = new ServerSim();
        Require(sim.Start(room, GameMode.Battle, 2, _ => { }, () => { }, roster: roster), "room start");
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            NetSession.ApplyRoster(roster);
            var scene = (Scene)typeof(ServerSim).GetField("_scene", Private)!.GetValue(sim)!;
            scene.Players.MainPlayerIndex = 0;
            var inputMode = typeof(Scene).GetField("_inputMode", Private)!;
            inputMode.SetValue(scene, Enum.Parse(inputMode.FieldType, "All"));
            scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
            NetSlotManager.Sync();
            Session(nameof(NetSession.Role), NetRole.Host); Session(nameof(NetSession.LocalSlot), 0);
            var player = scene.Players.Items[0];
            Require(!player.IsBot && player.Hunter == hunter, "native hunter identity");
            var keyboard = (KeyboardState)typeof(Scene).GetField("_keyboardState", Private)!.GetValue(scene)!;
            var setKey = typeof(KeyboardState).GetMethod("SetKeyState", Private | BindingFlags.Public)!
                .CreateDelegate<Action<KeyboardState, Keys, bool>>();
            var bindings = new (Keybind Control, Keys Key)[] { (player.Controls.MoveUp, Keys.W),
                (player.Controls.MoveDown, Keys.S), (player.Controls.MoveLeft, Keys.A),
                (player.Controls.MoveRight, Keys.D), (player.Controls.Jump, Keys.Space),
                (player.Controls.AimLeft, Keys.Left), (player.Controls.AimRight, Keys.Right) };
            foreach (Keybind control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            foreach (var binding in bindings) binding.Control.Key = binding.Key;
            player.Controls.MouseAim = false; player.Controls.KeyboardAim = true;
            void Step(Keys[] down)
            {
                foreach (var binding in bindings) setKey(keyboard, binding.Key, Array.IndexOf(down, binding.Key) >= 0);
                Session(nameof(NetSession.NetFrame), unchecked(NetSession.NetFrame + 1));
                sim.Step();
                Require(sim.StepFailures == 0, "native simulation exception");
                Session(nameof(NetSession.Role), NetRole.Server);
                try { NetSession.BroadcastSnapshot(); }
                finally { Session(nameof(NetSession.Role), NetRole.Host); }
            }
            for (int i = 0; i < 120; i++) Step(Array.Empty<Keys>());
            inputMode.SetValue(scene, Enum.Parse(inputMode.FieldType, "All"));
            Require(player.ModIsInPlay && player.Health > 0 && !player.IsAltForm
                && scene.CameraMode == CameraMode.Roam && NetHooks.LocalSlot == player.SlotIndex, "live native biped seam");
            var values = new SortedDictionary<string, object>();
            foreach (var field in player.Values.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (field.FieldType.IsPrimitive || field.FieldType.IsEnum)
                    values.Add(field.Name, Convert.ToInt64(field.GetValue(player.Values)));
            var ticks = new List<object>();
            Vector3 start = player.Position, firstVelocity = Vector3.Zero;
            float previousVerticalSpeed = player.Speed.Y;
            int upwardImpulses = 0;
            bool turned = false;
            bool airborne = false, upward = false, landed = false, moved = false, inputObserved = false;
            for (int tick = 0; tick < 180; tick++)
            {
                Keys[] down = Input(scenario, tick);
                Step(down);
                Require(player.ModIsInPlay && player.Health > 0 && !player.IsAltForm, "scenario remains a live biped");
                bool grounded = player.Flags1.TestFlag(PlayerFlags1.Grounded);
                if (previousVerticalSpeed <= 0 && player.Speed.Y > 0) upwardImpulses++;
                previousVerticalSpeed = player.Speed.Y;
                Vector3 horizontal = new Vector3(player.Speed.X, 0, player.Speed.Z);
                if (tick == 20) firstVelocity = horizontal;
                if (tick > 48 && firstVelocity.Length > .001f && horizontal.Length > .001f)
                    turned |= Vector3.Dot(firstVelocity.Normalized(), horizontal.Normalized()) < -.5f;
                landed |= airborne && grounded;
                airborne |= !grounded; upward |= player.Speed.Y > 0;
                moved |= (player.Position - start).Length > .001f;
                var controls = new List<string>();
                foreach (var binding in bindings) if (binding.Control.IsDown) controls.Add(binding.Key.ToString());
                inputObserved |= controls.Count > 0;
                var intent = NetPlayerBridge.CaptureIntent(player);
                ticks.Add(new { tick = tick + 1, paired30HzBoundary = (tick + 1) % 2 == 0,
                    position = V(player.Position), speed = V(player.Speed), facing = V(player.FacingVector),
                    horizontalSpeed = MathF.Sqrt(player.Speed.X * player.Speed.X + player.Speed.Z * player.Speed.Z),
                    flags1 = (uint)player.Flags1, flags2 = (uint)player.Flags2, grounded,
                    requestedKeys = Array.ConvertAll(down, key => key.ToString()), controls,
                    buttons = (uint)intent.Buttons, moveX = intent.MoveX, moveY = intent.MoveY });
            }
            if (scenario != "idle") Require(inputObserved, "requested native input observed");
            if (scenario.Contains("release") || scenario == "air-input-landing") Require(moved, "native displacement");
            if (scenario.Contains("jump") || scenario == "air-input-landing")
                Require(airborne && upward && landed, "native upward impulse, air interval and landing");
            if (scenario == "direction-change-reversal") Require(turned, "native horizontal velocity reverses under movement input");
            if (scenario == "repeated-jump") Require(upwardImpulses >= 2, "multiple native jump impulses");
            return new { hunter = hunter.ToString(), scenario, values, ticks };
        }
        finally { sim.Stop(); }
    }

    private static Keys[] Input(string scenario, int tick) => scenario switch {
        "forward-release" when tick < 48 => new[] { Keys.W },
        "strafe-release" when tick < 48 => new[] { Keys.D },
        "diagonal-release" when tick < 48 => new[] { Keys.W, Keys.D },
        "direction-change-reversal" when tick < 32 => new[] { Keys.W },
        "direction-change-reversal" when tick < 64 => new[] { Keys.D },
        "direction-change-reversal" when tick < 96 => new[] { Keys.S },
        "single-jump" when tick == 0 => new[] { Keys.Space },
        "repeated-jump" when tick % 40 == 0 => new[] { Keys.Space },
        "air-input-landing" when tick == 0 => new[] { Keys.Space },
        "air-input-landing" when tick < 24 => new[] { Keys.W },
        "air-input-landing" when tick < 48 => new[] { Keys.D },
        _ => Array.Empty<Keys>() };

    private static string SourceSha()
    {
        string? frozenSha = Environment.GetEnvironmentVariable("PRIME_TEST_SOURCE_SHA");
        if (frozenSha != null)
        {
            Require(frozenSha.Length == 40, "PRIME_TEST_SOURCE_SHA must be an exact committed 40-digit hexadecimal SHA");
            foreach (char value in frozenSha)
                Require(value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F',
                    "PRIME_TEST_SOURCE_SHA must be an exact committed 40-digit hexadecimal SHA");
            return frozenSha.ToLowerInvariant();
        }
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, ".git"))
            && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        Require(directory != null, "source repository unavailable beside test build");
        using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
            { RedirectStandardOutput = true, UseShellExecute = false, WorkingDirectory = directory!.FullName })!;
        string sha = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        Require(process.ExitCode == 0 && sha.Length == 40, "source SHA unavailable");
        return sha;
    }

    private static void Compare(JsonElement expected, JsonElement actual, string path, bool metadata = false)
    {
        Require(expected.ValueKind == actual.ValueKind, path + " kind differs");
        if (expected.ValueKind == JsonValueKind.Object)
        {
            int count = 0;
            foreach (var property in expected.EnumerateObject())
            {
                count++;
                Require(actual.TryGetProperty(property.Name, out var value), path + "." + property.Name + " missing");
                if (metadata && property.Name == "sourceSha") continue; // New source is the purpose of a regression check.
                Compare(property.Value, value, path + "." + property.Name);
            }
            int actualCount = 0; foreach (var _ in actual.EnumerateObject()) actualCount++;
            Require(count == actualCount, path + " property count differs");
        }
        else if (expected.ValueKind == JsonValueKind.Array)
        {
            Require(expected.GetArrayLength() == actual.GetArrayLength(), path + " length differs");
            for (int i = 0; i < expected.GetArrayLength(); i++) Compare(expected[i], actual[i], path + "[" + i + "]");
        }
        else if (expected.ValueKind == JsonValueKind.Number)
        {
            double a = expected.GetDouble(), b = actual.GetDouble();
            bool integral = expected.TryGetInt64(out long ai) && actual.TryGetInt64(out long bi);
            Require(double.IsFinite(a) && double.IsFinite(b)
                && (integral ? a == b : Math.Abs(a - b) <= Tolerance), $"{path}: expected {a}, actual {b}");
        }
        else Require(expected.GetRawText() == actual.GetRawText(), path + " value differs");
    }
}
