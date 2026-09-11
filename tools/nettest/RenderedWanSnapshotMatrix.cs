using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead.NetTest;

/// <summary>
/// Bounded 30/60 Hz A/B definitions for release tooling. These definitions
/// describe work to be run by an external rendered/WAN operator; they do not
/// change the production snapshot cadence.
/// </summary>
internal enum SnapshotMatrixDepth
{
    Smoke,
    Release,
    Deep
}

internal enum SnapshotRateHz
{
    Hz30 = 30,
    Hz60 = 60
}

internal static class SnapshotMatrixParser
{
    internal static SnapshotMatrixDepth ParseDepth(string value) => value switch
    {
        "smoke" => SnapshotMatrixDepth.Smoke,
        "release" => SnapshotMatrixDepth.Release,
        "deep" => SnapshotMatrixDepth.Deep,
        _ => throw new ArgumentException("Snapshot matrix depth must be smoke, release, or deep.")
    };

    internal static string Format(SnapshotMatrixDepth value) => value switch
    {
        SnapshotMatrixDepth.Smoke => "smoke",
        SnapshotMatrixDepth.Release => "release",
        SnapshotMatrixDepth.Deep => "deep",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

internal sealed record SnapshotMatrixCell(
    string PairId, SnapshotRateHz SnapshotRate, int Repeat,
    int RoundTripMs, int JitterMs, double LossPercent, uint ImpairmentSeed,
    string Choreography, string TimingProfile, int Participants, int Seconds)
{
    internal void Validate()
    {
        if (PairId is not { Length: >= 3 and <= 64 }
            || PairId.Any(char.IsControl)
            || Repeat is < 1 or > SnapshotMatrixDefinition.MaximumRepeats
            || !Enum.IsDefined(SnapshotRate)
            || RoundTripMs is < 0 or > 300
            || JitterMs is < 0 or > 100
            || !double.IsFinite(LossPercent) || LossPercent is < 0 or > 5
            || ImpairmentSeed == 0
            || Choreography is not { Length: >= 1 and <= 64 }
            || Choreography.Any(char.IsControl)
            || TimingProfile is not { Length: >= 1 and <= 64 }
            || TimingProfile.Any(char.IsControl)
            || Participants is < 2 or > 2
            || Seconds is < 12 or > 60)
            throw new ArgumentException("Snapshot matrix cell is outside the bounded release harness policy.");
    }
}

internal sealed record SnapshotMatrixDefinition(
    SnapshotMatrixDepth Depth, int Repeats, SnapshotMatrixCell[] Cells)
{
    internal const int MaximumCells = 64;
    internal const int MaximumRepeats = 3;
    internal const int ProductionSnapshotRateHz = 30;

    internal static SnapshotMatrixDefinition Create(SnapshotMatrixDepth depth,
        uint seed = 0x51A7_30C0)
    {
        if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
        int repeats;
        int seconds;
        (int Rtt, int Jitter, double Loss)[] impairments;
        switch (depth)
        {
            case SnapshotMatrixDepth.Smoke:
                repeats = 1;
                seconds = 12;
                impairments = [(0, 0, 0d), (100, 10, 1d)];
                break;
            case SnapshotMatrixDepth.Release:
                repeats = 2;
                seconds = 30;
                impairments = [(0, 0, 0d), (50, 5, 0d), (150, 20, 2d)];
                break;
            case SnapshotMatrixDepth.Deep:
                repeats = 3;
                seconds = 60;
                impairments = [(0, 0, 0d), (50, 5, 0d), (100, 10, 1d),
                    (200, 25, 3d), (300, 50, 5d)];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(depth));
        }

        var cells = new List<SnapshotMatrixCell>(impairments.Length * repeats * 2);
        for (int impairment = 0; impairment < impairments.Length; impairment++)
        {
            (int rtt, int jitter, double loss) = impairments[impairment];
            for (int repeat = 1; repeat <= repeats; repeat++)
            {
                string pair = $"{SnapshotMatrixParser.Format(depth)}-{impairment + 1:00}-{repeat:00}";
                uint pairSeed = unchecked(seed + (uint)(impairment * 0x9E37)
                    + (uint)(repeat * 0x10001));
                // Both rate cells intentionally share every input except the
                // rate. This makes an A/B result attributable to cadence,
                // rather than a different impairment or choreography.
                foreach (SnapshotRateHz rate in Enum.GetValues<SnapshotRateHz>())
                {
                    cells.Add(new SnapshotMatrixCell(pair, rate, repeat, rtt, jitter,
                        loss, pairSeed, "headshot-vertical-strafe-close-long",
                        "fixed-60-input", Participants: 2, seconds));
                }
            }
        }
        var definition = new SnapshotMatrixDefinition(depth, repeats, cells.ToArray());
        definition.Validate();
        return definition;
    }

    internal void Validate()
    {
        if (!Enum.IsDefined(Depth) || Repeats is < 1 or > MaximumRepeats
            || Cells is null || Cells.Length is < 2 or > MaximumCells)
            throw new ArgumentException("Snapshot matrix definition is invalid.");
        foreach (SnapshotMatrixCell cell in Cells) cell.Validate();

        var pairs = Cells.GroupBy(cell => (cell.PairId, cell.Repeat)).ToArray();
        if (pairs.Length * 2 != Cells.Length
            || pairs.Any(pair => pair.Count() != 2
                || pair.Select(cell => cell.SnapshotRate).Distinct().Count() != 2
                || pair.Select(cell => (cell.RoundTripMs, cell.JitterMs, cell.LossPercent,
                    cell.ImpairmentSeed, cell.Choreography, cell.TimingProfile,
                    cell.Participants, cell.Seconds)).Distinct().Count() != 1))
            throw new ArgumentException("Snapshot matrix cells are not paired identical A/B repetitions.");
    }
}

/// <summary>Explicit evidence collected by a matrix runner for one rate cell.</summary>
internal sealed record SnapshotMatrixMetrics(
    string EvidenceClass, bool RealWanEvidence, bool ClientCompleted,
    bool WorkerCompleted, bool TransportCompleted, int SnapshotRateHz,
    long SnapshotsSent, long SnapshotsReceived, long PacketsSent,
    long PacketsReceived, long PacketsDropped, double ClientP95Ms,
    double WorkerP95Ms, double TransportP95Ms, long AllocatedBytes,
    string? FailureReason)
{
    internal void Validate(SnapshotMatrixCell cell)
    {
        if (EvidenceClass is not { Length: >= 1 and <= 96 }
            || EvidenceClass.Any(char.IsControl)
            || RealWanEvidence != (EvidenceClass == "real-wan-independent-path")
            || SnapshotRateHz != (int)cell.SnapshotRate
            || SnapshotsSent < 0 || SnapshotsReceived < 0 || PacketsSent < 0
            || PacketsReceived < 0 || PacketsDropped < 0
            || !double.IsFinite(ClientP95Ms) || ClientP95Ms < 0
            || !double.IsFinite(WorkerP95Ms) || WorkerP95Ms < 0
            || !double.IsFinite(TransportP95Ms) || TransportP95Ms < 0
            || AllocatedBytes < 0 || (FailureReason?.Any(char.IsControl) ?? false))
            throw new ArgumentException("Snapshot matrix metrics are invalid.");
    }

    internal bool Complete => ClientCompleted && WorkerCompleted && TransportCompleted;
}

internal sealed record SnapshotMatrixCellResult(
    SnapshotMatrixCell Cell, SnapshotMatrixMetrics Metrics, bool Passed)
{
    internal void Validate()
    {
        Cell.Validate();
        Metrics.Validate(Cell);
        if (Passed && (!Metrics.Complete || Metrics.FailureReason != null))
            throw new ArgumentException("A passed snapshot matrix cell contains incomplete metrics.");
        if (!Passed && Metrics.FailureReason is null)
            throw new ArgumentException("A failed snapshot matrix cell must carry a failure reason.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SnapshotMatrixDecisionReport(
    string Schema, Guid RunId, string Scenario, SnapshotMatrixDepth Depth,
    int ProductionSnapshotRateHz, int? RecommendedSnapshotRateHz,
    bool RealWanEvidence, bool Accepted, bool RenderedWanProof,
    string Decision, string[] FailureReasons, SnapshotMatrixCellResult[] Cells)
{
    internal const string CurrentSchema = "project-prime.snapshot-matrix.v1";

    internal static SnapshotMatrixDecisionReport Build(Guid runId,
        SnapshotMatrixDefinition definition, IReadOnlyList<SnapshotMatrixCellResult> results)
    {
        if (runId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(runId));
        ArgumentNullException.ThrowIfNull(results);
        definition.Validate();
        var failures = new List<string>();
        foreach (SnapshotMatrixCellResult result in results) result.Validate();
        var expected = definition.Cells.ToDictionary(cell =>
            (cell.PairId, cell.SnapshotRate, cell.Repeat));
        var seen = new HashSet<(string, SnapshotRateHz, int)>();
        foreach (SnapshotMatrixCellResult result in results)
        {
            var key = (result.Cell.PairId, result.Cell.SnapshotRate, result.Cell.Repeat);
            if (!expected.TryGetValue(key, out SnapshotMatrixCell? expectedCell)
                || expectedCell != result.Cell || !seen.Add(key))
                failures.Add("unexpected-or-duplicate-matrix-cell");
        }
        if (seen.Count != expected.Count) failures.Add("matrix-cell-missing");

        bool realWan = results.Count > 0 && results.All(result
            => result.Metrics.RealWanEvidence
                && result.Metrics.EvidenceClass == "real-wan-independent-path");
        bool cellsComplete = results.Count == definition.Cells.Length
            && results.All(result => result.Passed && result.Metrics.Complete);
        if (!realWan) failures.Add("real-wan-evidence-required");
        if (!cellsComplete) failures.Add("matrix-cell-failed-or-incomplete");

        int? recommendation = null;
        string decision;
        bool accepted = false;
        if (!realWan)
        {
            // Fail closed. The report is useful for planning, but no matrix
            // result is allowed to promote the production 30 Hz default.
            decision = "INSUFFICIENT_REAL_WAN_EVIDENCE";
        }
        else if (failures.Count != 0)
        {
            decision = "MATRIX_FAILED";
        }
        else
        {
            var pairs = results.GroupBy(result => (result.Cell.PairId, result.Cell.Repeat));
            double p95Thirty = pairs.Average(pair => pair.Single(value
                => value.Cell.SnapshotRate == SnapshotRateHz.Hz30).Metrics.TransportP95Ms);
            double p95Sixty = pairs.Average(pair => pair.Single(value
                => value.Cell.SnapshotRate == SnapshotRateHz.Hz60).Metrics.TransportP95Ms);
            recommendation = p95Sixty <= p95Thirty * 1.05 ? 60 : 30;
            decision = recommendation == 60 ? "REVIEW_60_HZ" : "RETAIN_30_HZ";
            accepted = true;
        }

        return new(CurrentSchema, runId, "headshot", definition.Depth,
            SnapshotMatrixDefinition.ProductionSnapshotRateHz, recommendation,
            realWan, accepted, RenderedWanProof: false, decision,
            failures.Distinct(StringComparer.Ordinal).ToArray(), results.ToArray());
    }
}

internal sealed record SnapshotMatrixPlanReport(
    string Schema, Guid RunId, string Scenario, DateTimeOffset StartedUtc,
    SnapshotMatrixDepth Depth, SnapshotMatrixDefinition Definition,
    SnapshotMatrixDecisionReport Decision)
{
    internal const string CurrentSchema = "project-prime.snapshot-matrix-plan.v1";
}

internal static partial class RenderedWanValidationCheck
{
    /// <summary>
    /// Materializes a fresh, not-yet-run matrix plan and its intentionally
    /// fail-closed decision report. No synthetic metrics are promoted to WAN
    /// evidence and an existing output directory is never reused.
    /// </summary>
    internal static int SnapshotMatrixPlan(string[] args)
    {
        if (args.Length is not (2 or 4)
            || args.Length == 4 && args[2] != "--depth")
        {
            Console.Error.WriteLine(
                "Usage: nettest --rendered-wan-snapshot-matrix-plan OUTPUT_DIRECTORY [--depth smoke|release|deep]");
            return 2;
        }

        SnapshotMatrixDepth depth;
        try { depth = SnapshotMatrixParser.ParseDepth(args.Length == 4 ? args[3] : "smoke"); }
        catch (ArgumentException error) { Console.Error.WriteLine(error.Message); return 2; }

        Guid runId = Guid.NewGuid();
        DateTimeOffset started = DateTimeOffset.UtcNow;
        try
        {
            string output = RenderedWanRunReservation.ReserveNewDirectory(args[1]);
            SnapshotMatrixDefinition definition = SnapshotMatrixDefinition.Create(depth);
            var results = definition.Cells.Select(cell => new SnapshotMatrixCellResult(cell,
                new SnapshotMatrixMetrics("rendered-loopback-process-local-impairment", false,
                    ClientCompleted: false, WorkerCompleted: false, TransportCompleted: false,
                    (int)cell.SnapshotRate, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                    "not-run"), Passed: false)).ToArray();
            SnapshotMatrixDecisionReport decision = SnapshotMatrixDecisionReport.Build(
                runId, definition, results);
            SnapshotMatrixPlanReport plan = new(SnapshotMatrixPlanReport.CurrentSchema,
                runId, "headshot", started, depth, definition, decision);
            WriteSnapshotJson(Path.Combine(output, "matrix-plan.json"), plan);
            WriteSnapshotJson(Path.Combine(output, "decision-report.json"), decision);
            Console.WriteLine($"RENDERED_WAN_SNAPSHOT_MATRIX_PLAN result=GENERATED run={runId:D} depth={SnapshotMatrixParser.Format(depth)} decision={decision.Decision} accepted=false renderedWanProof=false");
            return 0;
        }
        catch (Exception error) when (error is ArgumentException or IOException
            or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"RENDERED_WAN_SNAPSHOT_MATRIX_PLAN result=FAIL classification=HARNESS_INVALID run={runId:D} reason={error.Message}");
            return 1;
        }
    }

    internal static int SnapshotMatrixSelfTest()
    {
        int cases = 0;
        foreach (SnapshotMatrixDepth depth in Enum.GetValues<SnapshotMatrixDepth>())
        {
            SnapshotMatrixDefinition definition = SnapshotMatrixDefinition.Create(depth);
            definition.Validate();
            int expectedPairs = definition.Cells.Length / 2;
            if (definition.Cells.GroupBy(cell => (cell.PairId, cell.Repeat)).Count() != expectedPairs)
                throw new InvalidOperationException("Snapshot matrix pair count is not deterministic.");
            cases++;

            var results = definition.Cells.Select(cell => new SnapshotMatrixCellResult(cell,
                new SnapshotMatrixMetrics("rendered-loopback-process-local-impairment", false,
                    ClientCompleted: true, WorkerCompleted: true, TransportCompleted: true,
                    (int)cell.SnapshotRate, 720, 720, 1000, 1000, 0, 1, 1, 1, 0, null),
                Passed: true)).ToArray();
            SnapshotMatrixDecisionReport report = SnapshotMatrixDecisionReport.Build(
                Guid.NewGuid(), definition, results);
            if (report.RealWanEvidence || report.Accepted || report.RecommendedSnapshotRateHz != null
                || report.ProductionSnapshotRateHz != 30
                || report.Decision != "INSUFFICIENT_REAL_WAN_EVIDENCE"
                || report.RenderedWanProof)
                throw new InvalidOperationException("Snapshot matrix did not fail closed without WAN evidence.");
            SnapshotMatrixDecisionReport decoded = RenderedWanReportCodec
                .Deserialize<SnapshotMatrixDecisionReport>(RenderedWanReportCodec.Serialize(report));
            if (decoded.RunId != report.RunId || decoded.Cells.Length != report.Cells.Length
                || decoded.Decision != report.Decision)
                throw new InvalidOperationException("Snapshot matrix JSON round-trip changed the decision report.");
            cases++;
        }

        if (SnapshotMatrixParser.ParseDepth("smoke") != SnapshotMatrixDepth.Smoke
            || SnapshotMatrixParser.Format(SnapshotMatrixDepth.Deep) != "deep")
            throw new InvalidOperationException("Snapshot matrix parser round-trip failed.");
        try { SnapshotMatrixParser.ParseDepth("full"); throw new InvalidOperationException("Invalid depth accepted."); }
        catch (ArgumentException) { cases++; }
        Console.WriteLine($"RENDERED_WAN_SNAPSHOT_MATRIX_SELF_TEST result=PASS cases={cases}");
        return 0;
    }

    private static void WriteSnapshotJson(string path, object value)
    {
        byte[] bytes = RenderedWanReportCodec.Serialize(value);
        var options = new FileStreamOptions
        {
            Access = FileAccess.Write, Mode = FileMode.CreateNew,
            Share = FileShare.None, Options = FileOptions.WriteThrough
        };
        using var stream = new FileStream(path, options);
        stream.Write(bytes);
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }
}
