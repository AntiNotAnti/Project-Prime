using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods;
using MphRead.Sound;
using OpenTK.Mathematics;

// Windowless reader/replica/seek isolation, not rendered replay acceptance.
string Value(string flag, string fallback) => Array.IndexOf(args, flag) is int i && i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
string replay = Path.GetFullPath(Value("-replay", ""));
string output = Path.GetFullPath(Value("-output", "replay-memory.json"));
int cycles = int.Parse(Value("-cycles", "120"));
if (!File.Exists(replay) || cycles < 1 || cycles > 300 || File.Exists(output)) throw new ArgumentException("Existing replay, cycles 1..300 and fresh output required.");
Headless.Enter();
typeof(Scene).Assembly.GetType("MphRead.ConsoleSetup")!.GetMethod("Run")!.Invoke(null, null);
Paths.UpdatePaths(); Paths.ChooseMphPath(); Paths.ChooseFhPath();
var records = new List<object>();
var weak = new List<(string Kind, WeakReference Reference)>();
string? reference = null, error = null;
int completed = 0;
try
{
    for (int cycle = 0; cycle < cycles; cycle++)
    {
        var result = ReplayMemory.RunCycle(replay, cycle, weak);
        if (reference != null && result.Hash != reference) throw new InvalidOperationException("Repeated replay world hash changed.");
        reference = result.Hash;
        ResourceAttribution.FullGcDrain();
        records.Add(new { cycle, result.Hash, result.Seeks, memory = ResourceAttribution.MemorySample() });
        completed++;
        Console.WriteLine($"REPLAYMEMORY cycle={completed}/{cycles}");
    }
}
catch (Exception ex) { error = ex.ToString(); }
ResourceAttribution.FullGcDrain();
var drain = new List<object>();
ResourceAttribution.QuiescentSamples(drain, 30, 1000, () => new { nativeAudioExpected = false });
var retention = weak.GroupBy(w => w.Kind).Select(g => new { kind = g.Key, observed = g.Count(), alive = g.Count(w => w.Reference.IsAlive) }).ToArray();
var payload = new { schema = 1, kind = "replay-headless-memory", manifest = ResourceAttribution.Manifest(),
    replayPath = replay, replaySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(replay))), cyclesRequested = cycles, cyclesCompleted = completed,
    seekTargets = new uint[] { 120, 30, 240, 120 }, contextCount = 0, nativeSfx = Sfx.Instance is SfxInstance, records, drain, retention,
    complete = error == null, error,
    limitations = "Existing passive replay player/seek/checkpoint path with Headless.Enter. Suppresses all presentation work by the existing headless contract; no graphics/audio context or video export. Real replay read/replica simulation/seek/disposal, not rendered replay visual correctness. Full compacting GC is diagnostic after each helper returns, not production policy. Weak owner observations are not root paths or a global memory census. No FPS evidence." };
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("REPLAYMEMORY " + (error == null ? "PASS " : "FAIL ") + output);
return error == null ? 0 : 1;

static class ReplayMemory
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type Player = typeof(Scene).Assembly.GetType("MphRead.Mods.Network.PassiveReplayPlayer")!;
    private static object Property(object value, string name) => value.GetType().GetProperty(name, Flags)!.GetValue(value)!;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static (string Hash, object[] Seeks) RunCycle(string replay, int cycle, List<(string Kind, WeakReference Reference)> weak)
    {
        object player = Activator.CreateInstance(Player, Flags, null, new object?[] { replay, new Vector2i(320, 180), null }, null)!;
        weak.Add(("passive-player", new(player)));
        var seeks = new List<object>();
        string? hash = null;
        object? previous = null;
        object ObserveCurrent()
        {
            object current = Property(player, "Current");
            if (!ReferenceEquals(current, previous))
            {
                previous = current;
                weak.Add(("passive-scene", new(current)));
                weak.Add(("scene", new(Property(current, "Scene"))));
                object session = Property(current, "Session");
                weak.Add(("session", new(session)));
                object? reader = session.GetType().GetField("_reader", Flags)?.GetValue(session);
                if (reader != null) weak.Add(("reader", new(reader)));
            }
            return current;
        }
        try
        {
            foreach (uint target in new uint[] { 120, 30, 240, 120 })
            {
                ObserveCurrent();
                long start = Stopwatch.GetTimestamp(); double pause = GC.GetTotalPauseDuration().TotalMilliseconds;
                Player.GetMethod("Seek", Flags)!.Invoke(player, new object[] { target, false });
                int iterations = 0;
                do
                {
                    Player.GetMethod("Update", Flags)!.Invoke(player, new object[] { 120, double.PositiveInfinity });
                    ObserveCurrent();
                    if (++iterations > 5000) throw new TimeoutException("Replay seek did not finish.");
                } while (!(bool)Property(player, "Ready"));
                object current = ObserveCurrent(); object session = Property(current, "Session");
                uint frame = (uint)Property(session, "CurrentFrame");
                if (frame != target) throw new InvalidOperationException($"Replay target {target} reached {frame}; recording is insufficient.");
                var scene = (Scene)Property(current, "Scene");
                string value = (string)typeof(Scene).GetMethod("ReplayPresentationHash", Flags)!.Invoke(scene, new object[] { (uint)Property(session, "RecordingFrame") })!;
                if (target == 120)
                {
                    if (hash != null && hash != value) throw new InvalidOperationException("Backward replay seek changed presentation-state hash.");
                    hash = value;
                }
                seeks.Add(new { target, frame, hash = value, seekMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                    gcPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds - pause,
                    checkpointCount = Property(player, "CheckpointCount"), checkpointBytes = Property(player, "CheckpointBytes") });
            }
        }
        finally
        {
            object current = Property(player, "Current"); object session = Property(current, "Session");
            ((IDisposable)player).Dispose(); ((IDisposable)player).Dispose();
            if ((bool)Property(session, "IsActive") || session.GetType().GetField("_reader", Flags)!.GetValue(session) != null
                || (long)Property(player, "CheckpointBytes") != 0 || (int)Property(player, "CheckpointCount") != 0)
                throw new InvalidOperationException("Replay disposal retained active reader/checkpoints.");
            var scene = (Scene)Property(current, "Scene");
            if (((HashSet<int>)typeof(Scene).GetField("_ownedTextures", Flags)!.GetValue(scene)!).Count != 0
                || (int)typeof(Scene).GetField("_frameBuffer", Flags)!.GetValue(scene)! != 0 || Sfx.Instance is SfxInstance)
                throw new InvalidOperationException("Headless replay acquired native graphics/audio ownership.");
        }
        return (hash!, seeks.ToArray());
    }
}
