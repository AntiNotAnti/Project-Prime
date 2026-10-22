using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MphRead.Mods.Diagnostics
{
    /// <summary>
    /// Opt-in live sampler/comparer for the native-movement shadow project.
    /// It is dormant unless -movementshadow is supplied. Production samples and
    /// reference comparisons are buffered only for developer diagnostics.
    /// </summary>
    internal static class MovementShadowRuntime
    {
        private static readonly object _gate = new();
        private static readonly Dictionary<(int Slot, int Hunter, string Domain), MovementShadowAccumulator> _comparisons = new();
        private static readonly Dictionary<string, int> _skips = new(StringComparer.Ordinal);

        private static StreamWriter? _writer;
        private static StreamWriter? _comparisonWriter;
        private static string? _outputPath;
        private static bool _configured;
        private static bool _wroteHeader;
        private static bool _wroteComparisonHeader;
        private static int _samples;
        private static int _comparisonSamples;

        internal static bool Enabled { get; private set; }

        internal static void Configure(string? outputPath)
        {
            lock (_gate)
            {
                if (_configured)
                {
                    return;
                }
                _configured = true;
                Enabled = true;
                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    _outputPath = Path.GetFullPath(outputPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(_outputPath) ?? ".");
                }
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Close();
            }
            Console.WriteLine(_outputPath == null
                ? "[movementshadow] enabled; production/reference boundary summaries -> stdout"
                : $"[movementshadow] enabled; production samples -> {_outputPath}; comparisons -> {_outputPath}.compare.tsv");
        }

        internal static void ObserveProduction(in MovementBoundarySnapshot snapshot)
        {
            if (!Enabled || !snapshot.IsNativeBoundary)
            {
                return;
            }

            lock (_gate)
            {
                _samples++;
                if (_outputPath == null)
                {
                    // Keep stdout useful during interactive runs: one sample per
                    // second per observed player instead of 30 lines/second.
                    if (snapshot.SimulationFrame % 60UL == 0)
                    {
                        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                            $"[movementshadow] production frame={snapshot.SimulationFrame} slot={snapshot.Slot} "
                            + $"hunter={snapshot.Hunter} alt={(snapshot.AltForm ? 1 : 0)} "
                            + $"pos=({snapshot.Position.X:0.000000},{snapshot.Position.Y:0.000000},{snapshot.Position.Z:0.000000}) "
                            + $"vel=({snapshot.Velocity.X:0.000000},{snapshot.Velocity.Y:0.000000},{snapshot.Velocity.Z:0.000000}) "
                            + $"grounded={(snapshot.Grounded ? 1 : 0)} standing={(snapshot.Standing ? 1 : 0)} "
                            + $"spire={(snapshot.SpireClimbing ? 1 : 0)} gravity={snapshot.Gravity:0.000000}"));
                    }
                    return;
                }

                EnsureProductionWriter();
                _writer!.Write(snapshot.SimulationFrame.ToString(CultureInfo.InvariantCulture));
                _writer.Write('\t'); _writer.Write(snapshot.Slot.ToString(CultureInfo.InvariantCulture));
                _writer.Write('\t'); _writer.Write(snapshot.Hunter.ToString(CultureInfo.InvariantCulture));
                _writer.Write('\t'); _writer.Write(snapshot.AltForm ? "1" : "0");
                _writer.Write('\t'); _writer.Write(snapshot.Grounded ? "1" : "0");
                _writer.Write('\t'); _writer.Write(snapshot.Standing ? "1" : "0");
                _writer.Write('\t'); _writer.Write(snapshot.SpireClimbing ? "1" : "0");
                _writer.Write('\t'); _writer.Write(snapshot.StandingEntityId.ToString(CultureInfo.InvariantCulture));
                WriteVector(_writer, snapshot.Position);
                WriteVector(_writer, snapshot.Velocity);
                WriteVector(_writer, snapshot.Facing);
                _writer.Write('\t'); _writer.Write(snapshot.Gravity.ToString("R", CultureInfo.InvariantCulture));
                _writer.Write('\t'); _writer.Write(snapshot.Slipperiness.ToString(CultureInfo.InvariantCulture));
                WriteVector(_writer, snapshot.ContactNormal);
                _writer.Write('\t'); _writer.Write(snapshot.ContactPushout.ToString("R", CultureInfo.InvariantCulture));
                _writer.WriteLine();

                if ((_samples % 120) == 0)
                {
                    _writer.Flush();
                }
            }
        }

        internal static MovementBoundaryDifference ObserveReference(
            in MovementBoundarySnapshot current,
            in MovementBoundarySnapshot reference,
            string domain)
        {
            if (!Enabled)
            {
                return default;
            }
            if (string.IsNullOrWhiteSpace(domain))
            {
                throw new ArgumentException("Movement shadow comparison domain is required.", nameof(domain));
            }

            lock (_gate)
            {
                var key = (current.Slot, current.Hunter, domain);
                if (!_comparisons.TryGetValue(key, out MovementShadowAccumulator? accumulator))
                {
                    accumulator = new MovementShadowAccumulator();
                    _comparisons.Add(key, accumulator);
                }

                MovementBoundaryDifference difference = accumulator.Observe(current, reference);
                _comparisonSamples++;
                bool within = difference.WithinTolerance();

                if (_outputPath != null)
                {
                    EnsureComparisonWriter();
                    _comparisonWriter!.Write(current.SimulationFrame.ToString(CultureInfo.InvariantCulture));
                    _comparisonWriter.Write('\t'); _comparisonWriter.Write(current.Slot.ToString(CultureInfo.InvariantCulture));
                    _comparisonWriter.Write('\t'); _comparisonWriter.Write(current.Hunter.ToString(CultureInfo.InvariantCulture));
                    _comparisonWriter.Write('\t'); _comparisonWriter.Write(domain);
                    _comparisonWriter.Write('\t'); _comparisonWriter.Write(difference.PositionError.ToString("R", CultureInfo.InvariantCulture));
                    _comparisonWriter.Write('\t'); _comparisonWriter.Write(difference.VelocityError.ToString("R", CultureInfo.InvariantCulture));
                    _comparisonWriter.Write('\t'); _comparisonWriter.Write(difference.FacingError.ToString("R", CultureInfo.InvariantCulture));
                    _comparisonWriter.Write('\t'); _comparisonWriter.Write(difference.GravityError.ToString("R", CultureInfo.InvariantCulture));
                    _comparisonWriter.Write('\t'); _comparisonWriter.Write(difference.ContactNormalError.ToString("R", CultureInfo.InvariantCulture));
                    _comparisonWriter.Write('\t'); _comparisonWriter.Write(difference.ContactPushoutError.ToString("R", CultureInfo.InvariantCulture));
                    _comparisonWriter.Write('\t'); _comparisonWriter.Write(difference.DiscreteMatch ? "1" : "0");
                    _comparisonWriter.Write('\t'); _comparisonWriter.Write(within ? "1" : "0");
                    _comparisonWriter.WriteLine();
                    if ((_comparisonSamples % 120) == 0)
                    {
                        _comparisonWriter.Flush();
                    }
                }

                if (!within && accumulator.OutsideTolerance <= 3
                    || current.SimulationFrame % 120UL == 0)
                {
                    Console.WriteLine($"[movementshadow] compare slot={current.Slot} hunter={current.Hunter} "
                        + $"domain={domain} {MovementShadowComparer.Format(difference)} "
                        + $"within={(within ? 1 : 0)}");
                }
                return difference;
            }
        }

        internal static void Skip(string reason)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(reason))
            {
                return;
            }
            lock (_gate)
            {
                _skips.TryGetValue(reason, out int count);
                _skips[reason] = count + 1;
            }
        }

        private static void EnsureProductionWriter()
        {
            _writer ??= new StreamWriter(_outputPath!, false, new UTF8Encoding(false));
            if (_wroteHeader)
            {
                return;
            }
            _writer.WriteLine("frame\tslot\thunter\talt\tgrounded\tstanding\tspireClimbing\tstandingEntity\t"
                + "posX\tposY\tposZ\tvelX\tvelY\tvelZ\tfacingX\tfacingY\tfacingZ\tgravity\tslipperiness\t"
                + "contactNX\tcontactNY\tcontactNZ\tcontactPushout");
            _wroteHeader = true;
        }

        private static void EnsureComparisonWriter()
        {
            _comparisonWriter ??= new StreamWriter(_outputPath! + ".compare.tsv", false, new UTF8Encoding(false));
            if (_wroteComparisonHeader)
            {
                return;
            }
            _comparisonWriter.WriteLine("frame\tslot\thunter\tdomain\tpositionError\tvelocityError\tfacingError\t"
                + "gravityError\tcontactNormalError\tcontactPushoutError\tdiscreteMatch\twithinTolerance");
            _wroteComparisonHeader = true;
        }

        private static void WriteVector(StreamWriter writer, OpenTK.Mathematics.Vector3 value)
        {
            writer.Write('\t'); writer.Write(value.X.ToString("R", CultureInfo.InvariantCulture));
            writer.Write('\t'); writer.Write(value.Y.ToString("R", CultureInfo.InvariantCulture));
            writer.Write('\t'); writer.Write(value.Z.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void Close()
        {
            lock (_gate)
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
                _comparisonWriter?.Flush();
                _comparisonWriter?.Dispose();
                _comparisonWriter = null;

                Console.WriteLine($"[movementshadow] productionSamples={_samples} comparisonSamples={_comparisonSamples}");
                foreach (var pair in _comparisons)
                {
                    Console.WriteLine($"[movementshadow] summary slot={pair.Key.Slot} hunter={pair.Key.Hunter} "
                        + $"domain={pair.Key.Domain} {pair.Value.Summary()}");
                }
                foreach (var pair in _skips)
                {
                    Console.WriteLine($"[movementshadow] skipped {pair.Key}={pair.Value}");
                }
            }
        }
    }
}
