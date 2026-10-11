using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MphRead.Mods.Network;

namespace MphRead.NetTest;
internal static class ServerPerformanceEvidenceTests
{
    // Run off/on in separate processes: opt-in is intentionally frozen at initialization.
    public static int Run(string[] args)
    {
        string directory = Path.Combine(Path.GetTempPath(), "prime-perf-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, "aggregate.json");
        bool enabled = args.Length > 1 && args[1] == "on";
        Environment.SetEnvironmentVariable("PRIME_SERVER_PERF_OUTPUT", enabled ? output : null);
        try
        {
            var type = typeof(NetConfig).Assembly.GetType("MphRead.Mods.Network.NetPerformanceEvidence", true)!;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            object? Call(string name, params object[] values) => type.GetMethod(name, flags)!.Invoke(null, values);
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
            uint frame = NetSession.NetFrame;
            Call("BeginRun");
            Check((bool)type.GetProperty("Enabled", flags)!.GetValue(null)! == enabled, "explicit/default opt-in");
            var operation = type.GetNestedType("Operation", BindingFlags.NonPublic)!;
            var counts = (long[,])type.GetField("Counts", flags)!.GetValue(null)!;
            Check(counts.GetLength(0) == 6 && counts.GetLength(1) == 257, "bounded aggregate result storage");
            for (int op = 0; op < 6; op++)
            {
                object key = Enum.ToObject(operation, op);
                Call("End", key, 0L, -1); // disabled/absent start owes no sample
                for (int n = 0; n < 2; n++)
                {
                    long start = (long)Call("Start")!;
                    Call("End", key, start, op == 4 ? 0 : -1);
                }
                Check(counts[op, op == 4 ? 0 : 256] == (enabled ? 2 : 0), "exact scope sample count");
            }
            Check(NetSession.NetFrame == frame, "instrumentation does not advance gameplay clock");
            if (enabled)
            {
                var terminal = type.GetField("TerminalResult", flags)!;
                var answer = typeof(NetHitClaims).GetMethod("Answer", flags)!;
                terminal.SetValue(null, -1);
                answer.Invoke(null, new object?[] { -1, (ushort)123, HitVerdictPacket.ResultApplied, true, null });
                Check((int)terminal.GetValue(null)! == -1, "invalid slot does not classify a terminal answer");
                int index = (int)typeof(NetHitClaims).GetMethod("SeenIndex", flags)!.Invoke(null, new object[] { 0, (ushort)123 })!;
                var ids = (ushort[,])typeof(NetHitClaims).GetField("_seenIds", flags)!.GetValue(null)!;
                var results = (byte[,])typeof(NetHitClaims).GetField("_seenResults", flags)!.GetValue(null)!;
                ids[0,index] = 123; results[0,index] = HitVerdictPacket.ResultDuplicate;
                answer.Invoke(null, new object?[] { 0, (ushort)123, HitVerdictPacket.ResultApplied, true, null });
                Check((int)terminal.GetValue(null)! == HitVerdictPacket.ResultDuplicate, "cached immutable result classifies actual answer");
                NetHitClaims.Reset();
            }
            Call("Write");
            Check(File.Exists(output) == enabled, "default off creates no evidence");
            if (enabled)
            {
                byte[] original = File.ReadAllBytes(output);
                using var json = JsonDocument.Parse(original);
                Check(json.RootElement.GetProperty("rows").GetArrayLength() == 6, "one aggregate row per measured operation");
                foreach (var h in json.RootElement.GetProperty("histograms").EnumerateArray())
                { long total = 0; foreach (var b in h.EnumerateArray()) total += b.GetInt64(); Check(total == 2, "histogram count matches scopes"); }
                Check(json.RootElement.GetProperty("gcPauseMilliseconds").GetDouble() >= 0, "nonnegative GC duration");
                Call("Write"); Check(File.ReadAllBytes(output).AsSpan().SequenceEqual(original), "repeat flush immutable");
                Call("BeginRun"); foreach (long count in counts) Check(count == 0, "run reset clears all counts");
                Call("Write"); Check(File.ReadAllBytes(output).AsSpan().SequenceEqual(original), "existing prior run never overwritten");
            }
            Console.WriteLine("PASS: server performance evidence " + (enabled ? "on" : "off")); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(directory, true); }
    }
}
