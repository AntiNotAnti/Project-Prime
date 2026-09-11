using System.Diagnostics;
using MphRead.Hud;
using MphRead.Hud.Network;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Render;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private long _nextNetworkHealth;
        private NetworkHealthState _networkHealth;
        private readonly NetworkHealthSmoother _networkHealthSmoother = new();
        private string _networkSummary = "", _networkSamples = "", _networkPrediction = "";
        private string _runtimeSummary = "", _inputSummary = "";
        private void DrawNetworkHealth()
        {
            long now = Stopwatch.GetTimestamp();
            if (now >= _nextNetworkHealth)
            {
                _nextNetworkHealth = now + Stopwatch.Frequency / 4;
                if (AuthoritativePlay.Current is { } play)
                {
                    if (play.Client.State == NetConnectionState.Connecting)
                    {
                        _networkHealth = _networkHealthSmoother.Observe(NetworkHealthState.Reconnecting);
                    }
                    else if (play.ReadNetworkHealth() is { } health)
                    {
                        _networkHealth = _networkHealthSmoother.Observe(health.State);
                        string rtt = health.HasRtt ? $"{health.Rtt:0}ms" : "--";
                        string jitter = health.HasRtt ? $"{health.Jitter:0}ms" : "--";
                        string interval = health.HasSnapshotInterval ? $"{health.SnapshotInterval:0}ms" : "--";
                        _networkSummary = $"RTT {rtt}  jitter {jitter}  snap {interval}";
                        _networkSamples = $"hold {health.HoldPercent:0.0}%  extrap {health.ExtrapolationPercent:0.0}%  queue {health.QueueAge:0}ms";
                        _networkPrediction = $"err {health.PredictionError:0.00}  hard {health.HardCorrections}  packets {health.Packets} dup/reorder {health.Duplicates}/{health.Reordered}";
                    }
                }

                FrameTimingDiagnosticsSnapshot timing = FrameTiming.CaptureDiagnostics();
                _runtimeSummary = $"sim {Percentiles(timing.Simulation)} render {Percentiles(timing.Render)}ms"
                    + $" hz {FrameTiming.MeasuredSimulationHz:0.0}/{FrameTiming.MeasuredFrameHz:0.0}"
                    + $" step {FrameTiming.StepsThisFrame} total {timing.TotalSteps}"
                    + $" drop/stall {timing.DroppedSteps}/{timing.Stalls}";
                GamepadMovementSample movement = GamepadInput.Movement;
                _inputSummary = $"GC {timing.GcAllocatedBytesPerSecond:0}B/s c{timing.Gen0Collections}/{timing.Gen1Collections}/{timing.Gen2Collections}"
                    + $" look {GamepadInput.LookCoordinator.ActiveLookDevice}"
                    + $"  stick {movement.Magnitude:0.00}  aimω {GamepadInput.AimAngularVelocity.X:0.0},{GamepadInput.AimAngularVelocity.Y:0.0}";
            }
            if (AuthoritativePlay.Current != null
                && (_networkHealth is NetworkHealthState.Unstable
                    or NetworkHealthState.Poor or NetworkHealthState.Reconnecting))
            {
                string text = _networkHealth switch
                {
                    NetworkHealthState.Reconnecting => "RECONNECTING…",
                    NetworkHealthState.Poor => "POOR CONNECTION",
                    _ => "UNSTABLE CONNECTION"
                };
                DrawText2D(128, 12, Align.Center, 0, text, new ColorRgba(255, 190, 64, 255), scale: .65f);
            }
            if (NetworkHealthSettings.Advanced)
            {
                DrawText2D(4, 156, Align.Left, 0, _networkSummary, scale: .55f);
                DrawText2D(4, 164, Align.Left, 0, _networkSamples, scale: .55f);
                DrawText2D(4, 172, Align.Left, 0, _networkPrediction, scale: .5f);
                DrawText2D(4, 180, Align.Left, 0, _runtimeSummary, scale: .45f);
                DrawText2D(4, 188, Align.Left, 0, _inputSummary, scale: .5f);
            }
        }

        private static string Percentiles(BoundedPercentileSnapshot sample)
            => sample.Count == 0 ? "--/--/--/--" : $"{sample.P50:0.00}/{sample.P95:0.00}/{sample.P99:0.00}/{sample.Max:0.00}";
    }
}
