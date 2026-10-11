using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.Network;

// Separate opt-in operator aggregates; never serialize endpoint/peer/raw evidence objects.
internal static class NetOperatorEvidence
{
    internal sealed record Verdict(string Outcome, long Responses);
    internal sealed record Queue(int Current, int HighWater, long Drops,
        long? BufferExhaustions, string BufferExhaustionStatus);
    internal sealed record Traffic(long ReceivedPackets, long SentPackets, long ReceivedBytes, long SentBytes);
    internal sealed record Report(int Schema, byte Protocol, string Phase, Queue Queue,
        Traffic Traffic, Verdict[] ClaimVerdictResponses, string VerdictCountingUnit);
    private static string? Output = Environment.GetEnvironmentVariable("PRIME_SERVER_OPERATOR_OUTPUT");
    static NetOperatorEvidence() { } // Environment capture must not be hoisted before explicit use.
    internal static bool Enabled => !string.IsNullOrWhiteSpace(Output);
    private static readonly CombatAckResult[] Outcomes = Enum.GetValues<CombatAckResult>();
    private static readonly long[] Responses = new long[Outcomes.Length];
    private static bool Written;
    internal static void BeginRun() { Output = Environment.GetEnvironmentVariable("PRIME_SERVER_OPERATOR_OUTPUT"); Array.Clear(Responses); Written = false; }
    internal static void Answer(byte result)
    {
        if (!Enabled || !NetSession.IsAuthority) return;
        int at = Array.IndexOf(Outcomes, (CombatAckResult)result);
        if (at >= 0) Responses[at]++;
    }
    internal static Report Capture(SessionPhase phase, NetTransportSnapshot queue, NetWireCategorySnapshot traffic)
    {
        if (!Enum.IsDefined(phase)) throw new ArgumentException("Unknown operator phase.");
        var outcomes = new Verdict[Outcomes.Length];
        for (int i = 0; i < outcomes.Length; i++) outcomes[i] = new(Outcomes[i].ToString(), Responses[i]);
        return new(1, NetConfig.ProtocolVersion, phase.ToString(),
            new(queue.QueueCurrent, queue.QueueHighWater, queue.QueueDrops, null, "UNAVAILABLE"),
            new(traffic.RxPackets, traffic.TxPackets, traffic.RxBytes, traffic.TxBytes), outcomes,
            "Authority Answer response decisions; retries included; not unique denied shots or delivered ACKs");
    }
    internal static void Write(SessionPhase phase, NetTransport? transport)
    {
        if (!Enabled || Written || transport == null) return;
        try
        {
            var budget = transport.CaptureBudget();
            // Only explicit numeric projections are retained; Endpoints and Cosmetics are never serialized.
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(Capture(phase, budget.Queue, budget.Wire.Totals),
                new JsonSerializerOptions { WriteIndented = true });
            using var file = new FileStream(Output!, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            file.Write(bytes); file.Flush(flushToDisk: true); Written = true;
        }
        catch (IOException) { Console.Error.WriteLine("[serveroperator] aggregate write failed (IO)"); }
        catch (UnauthorizedAccessException) { Console.Error.WriteLine("[serveroperator] aggregate write failed (access)"); }
    }
}
