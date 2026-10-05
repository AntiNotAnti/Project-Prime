using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MphRead.Mods.Diagnostics
{
    /// <summary>
    /// Opt-in live production sampler for the native-movement shadow project.
    /// It is dormant unless -movementshadow is supplied. The first runtime slice
    /// records Samus/Spire production state at equivalent 30 Hz boundaries; a
    /// later slice feeds the native reference state into the same contract.
    /// </summary>
    internal static class MovementShadowRuntime
    {
        private static readonly object _gate = new();
        private static StreamWriter? _writer;
        private static string? _outputPath;
        private static bool _configured;
        private static bool _wroteHeader;
        private static int _samples;

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
                ? "[movementshadow] enabled; production boundary samples will be summarized to stdout"
                : $"[movementshadow] enabled; production boundary samples -> {_outputPath}");
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

                _writer ??= new StreamWriter(_outputPath, append: false, new UTF8Encoding(false));
                if (!_wroteHeader)
                {
                    _writer.WriteLine("frame\tslot\thunter\talt\tgrounded\tstanding\tspireClimbing\tstandingEntity\t"
                        + "posX\tposY\tposZ\tvelX\tvelY\tvelZ\tfacingX\tfacingY\tfacingZ\tgravity\tslipperiness\t"
                        + "contactNX\tcontactNY\tcontactNZ\tcontactPushout");
                    _wroteHeader = true;
                }

                _writer.Write(snapshot.SimulationFrame.ToString(CultureInfo.InvariantCulture));
                _writer.Write('\t'); _writer.Write(snapshot.Slot.ToString(CultureInfo.InvariantCulture));
                _writer.Write('\t'); _writer.Write(snapshot.Hunter.ToString(CultureInfo.InvariantCulture));
                _writer.Write('\t'); _writer.Write(snapshot.AltForm ? "1" : "0");
                _writer.Write('\t'); _writer.Write(snapshot.Grounded ? "1" : "0");
                _writer.Write('\t'); _writer.Write(snapshot.Standing ? "1" : "0");
                _writer.Write('\t'); _writer.Write(snapshot.SpireClimbing ? "1" : "0");
                _writer.Write('\t'); _writer.Write(snapshot.StandingEntityId.ToString(CultureInfo.InvariantCulture));
                WriteVector(snapshot.Position);
                WriteVector(snapshot.Velocity);
                WriteVector(snapshot.Facing);
                _writer.Write('\t'); _writer.Write(snapshot.Gravity.ToString("R", CultureInfo.InvariantCulture));
                _writer.Write('\t'); _writer.Write(snapshot.Slipperiness.ToString(CultureInfo.InvariantCulture));
                WriteVector(snapshot.ContactNormal);
                _writer.Write('\t'); _writer.Write(snapshot.ContactPushout.ToString("R", CultureInfo.InvariantCulture));
                _writer.WriteLine();

                if ((_samples % 120) == 0)
                {
                    _writer.Flush();
                }
            }
        }

        private static void WriteVector(OpenTK.Mathematics.Vector3 value)
        {
            _writer!.Write('\t'); _writer.Write(value.X.ToString("R", CultureInfo.InvariantCulture));
            _writer.Write('\t'); _writer.Write(value.Y.ToString("R", CultureInfo.InvariantCulture));
            _writer.Write('\t'); _writer.Write(value.Z.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void Close()
        {
            lock (_gate)
            {
                if (_writer == null)
                {
                    return;
                }
                _writer.Flush();
                _writer.Dispose();
                _writer = null;
                Console.WriteLine($"[movementshadow] wrote {_samples} production boundary samples");
            }
        }
    }
}
