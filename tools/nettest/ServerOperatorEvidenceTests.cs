using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using MphRead.Mods.Network;

internal static class ServerOperatorEvidenceTests
{
    private static int checks;
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
    private static void Keys(JsonElement element, params string[] expected)
        => Check(element.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(expected.Order()), "exact property allowlist");
    public static int Run()
    {
        string? previous = Environment.GetEnvironmentVariable("PRIME_SERVER_OPERATOR_OUTPUT");
        bool authority = NetSession.IsAuthority;
        var property = typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!;
        try
        {
            Environment.SetEnvironmentVariable("PRIME_SERVER_OPERATOR_OUTPUT", "/not-written-by-this-test");
            NetOperatorEvidence.BeginRun();
            property.SetValue(null, false); NetOperatorEvidence.Answer((byte)CombatAckResult.RejectedLaunch);
            property.SetValue(null, true);
            NetOperatorEvidence.Answer((byte)CombatAckResult.RejectedLaunch);
            NetOperatorEvidence.Answer((byte)CombatAckResult.RejectedLaunch);
            NetOperatorEvidence.Answer((byte)CombatAckResult.Applied);
            NetOperatorEvidence.Answer(255);
            var queue = new NetTransportSnapshot(1, 2, 3, 4, 0, 0, 5, 6, 7, 0, 0, 0, 0);
            var traffic = new NetWireTelemetry().Capture().Totals;
            var report = NetOperatorEvidence.Capture(SessionPhase.InMatch, queue, traffic);
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(report));
            var root = document.RootElement;
            Keys(root, "Schema", "Protocol", "Phase", "Queue", "Traffic", "ClaimVerdictResponses", "VerdictCountingUnit");
            Keys(root.GetProperty("Queue"), "Current", "HighWater", "Drops", "BufferExhaustions", "BufferExhaustionStatus");
            Keys(root.GetProperty("Traffic"), "ReceivedPackets", "SentPackets", "ReceivedBytes", "SentBytes");
            Check(root.GetProperty("Queue").GetProperty("BufferExhaustions").ValueKind == JsonValueKind.Null
                && root.GetProperty("Queue").GetProperty("BufferExhaustionStatus").GetString() == "UNAVAILABLE", "unmeasured pool exhaustion is not zero");
            Check(root.GetProperty("Protocol").GetByte() == NetConfig.ProtocolVersion && root.GetProperty("Phase").GetString() == "InMatch", "closed version and phase");
            foreach (var row in root.GetProperty("ClaimVerdictResponses").EnumerateArray())
            {
                Keys(row, "Outcome", "Responses");
                Check(Enum.TryParse<CombatAckResult>(row.GetProperty("Outcome").GetString(), out var result) && Enum.IsDefined(result), "closed verdict vocabulary");
                Check(row.GetProperty("Responses").GetInt64() == (result == CombatAckResult.RejectedLaunch ? 2 : result == CombatAckResult.Applied ? 1 : 0), "actual authority responses count retries and exclude invalid/observer calls");
            }
            Check(root.GetProperty("VerdictCountingUnit").GetString()!.Contains("retries included")
                && root.GetProperty("VerdictCountingUnit").GetString()!.Contains("not unique denied shots"), "counting unit explicit");
            bool invalid = false;
            try { NetOperatorEvidence.Capture((SessionPhase)255, queue, traffic); } catch (ArgumentException) { invalid = true; }
            Check(invalid, "unknown phase cannot enter operator output");
            string output = Path.Combine(Path.GetTempPath(), "prime-operator-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                using var transport = new NetTransport(0, playbackOnly: true);
                Environment.SetEnvironmentVariable("PRIME_SERVER_OPERATOR_OUTPUT", null);
                NetOperatorEvidence.BeginRun();
                Check(!NetOperatorEvidence.Enabled, "operator output defaults off");
                NetOperatorEvidence.Write(SessionPhase.Lobby, transport);
                Check(!File.Exists(output), "disabled writer creates no report");
                Environment.SetEnvironmentVariable("PRIME_SERVER_OPERATOR_OUTPUT", output);
                NetOperatorEvidence.BeginRun();
                Check(NetOperatorEvidence.Enabled, "explicit operator output enables writer");
                NetOperatorEvidence.Write(SessionPhase.Lobby, transport);
                Check(File.Exists(output), "enabled writer creates aggregate report");
                byte[] first = File.ReadAllBytes(output);
                NetOperatorEvidence.Write(SessionPhase.InMatch, transport);
                Check(File.ReadAllBytes(output).SequenceEqual(first), "second write preserves first report");
                NetOperatorEvidence.BeginRun(); // Existing destination must remain immutable across runs too.
                NetOperatorEvidence.Write(SessionPhase.InMatch, transport);
                Check(File.ReadAllBytes(output).SequenceEqual(first), "CreateNew guards existing output across reset");
            }
            finally { if (File.Exists(output)) File.Delete(output); }
            NetOperatorEvidence.BeginRun();
            Check(NetOperatorEvidence.Capture(SessionPhase.Lobby, queue, traffic).ClaimVerdictResponses.All(v => v.Responses == 0), "run reset clears aggregate counts");
            Console.WriteLine($"PASS: {checks} server operator evidence assertions"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { property.SetValue(null, authority); Environment.SetEnvironmentVariable("PRIME_SERVER_OPERATOR_OUTPUT", previous); }
    }
}
