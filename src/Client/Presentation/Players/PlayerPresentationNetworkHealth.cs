using System.Diagnostics;
using MphRead.Hud;
using MphRead.Hud.Network;
using MphRead.Mods.Network;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private long _nextNetworkHealth;
        private NetworkHealthState _networkHealth;
        private string _networkSummary = "", _networkSamples = "", _networkPrediction = "";
        private void DrawNetworkHealth()
        {
            if (AuthoritativePlay.Current is not { } play) return;
            long now = Stopwatch.GetTimestamp();
            if (now >= _nextNetworkHealth)
            {
                _nextNetworkHealth = now + Stopwatch.Frequency / 4;
                if (play.ReadNetworkHealth() is not { } health) return;
                _networkHealth = health.State;
                string rtt = health.HasRtt ? $"{health.Rtt:0}ms" : "--";
                string jitter = health.HasRtt ? $"{health.Jitter:0}ms" : "--";
                string interval = health.HasSnapshotInterval ? $"{health.SnapshotInterval:0}ms" : "--";
                _networkSummary = $"RTT {rtt}  jitter {jitter}  snap {interval}";
                _networkSamples = $"hold {health.HoldPercent:0.0}%  extrap {health.ExtrapolationPercent:0.0}%  queue {health.QueueAge:0}ms";
                _networkPrediction = $"err {health.PredictionError:0.00}  hard {health.HardCorrections}  packets {health.Packets} dup/reorder {health.Duplicates}/{health.Reordered}";
            }
            if (_networkHealth != NetworkHealthState.Good)
            {
                string text = _networkHealth switch { NetworkHealthState.Interrupted => "CONNECTION INTERRUPTED", NetworkHealthState.HighLatency => "HIGH LATENCY", _ => "UNSTABLE CONNECTION" };
                DrawText2D(128, 12, Align.Center, 0, text, new ColorRgba(255, 190, 64, 255), scale: .65f);
            }
            if (NetworkHealthSettings.Advanced)
            {
                DrawText2D(4, 156, Align.Left, 0, _networkSummary, scale: .55f);
                DrawText2D(4, 164, Align.Left, 0, _networkSamples, scale: .55f);
                DrawText2D(4, 172, Align.Left, 0, _networkPrediction, scale: .5f);
            }
        }
    }
}
