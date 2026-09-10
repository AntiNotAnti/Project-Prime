using System.Diagnostics;
using System;
using System.Text;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Periodic report of what the running game believes about a networked
    /// session.
    ///
    /// This exists because the interesting failure is invisible from the
    /// wire: two clients can be perfectly connected -- correct slots, agreed
    /// clock -- while each one's scene contains only its own player, so
    /// nobody sees anybody and the scoreboard lists one name. Only the game
    /// process can see that, so it reports it here rather than a test trying
    /// to reconstruct the engine's state from outside.
    ///
    /// Printed to the console once a second while MPHREAD_NET_DEBUG is set, or
    /// whenever -netdebug is passed.
    /// </summary>
    public static class NetDiagnostics
    {
        private static double _lastReport;
        private static bool _enabled;
        private static bool _checked;
        private static NetDiagnosticWindow _authoritativeWindow = new();

        public static bool Enabled
        {
            get
            {
                if (!_checked)
                {
                    _checked = true;
                    _enabled = Environment.GetEnvironmentVariable("MPHREAD_NET_DEBUG") != null;
                }
                return _enabled;
            }
            set
            {
                if (_enabled != value) _authoritativeWindow = new NetDiagnosticWindow();
                _checked = true;
                _enabled = value;
            }
        }

        /// <summary>
        /// Call from the authoritative client owner after polling. Only periodic
        /// reports allocate. RTT variation, local drops and sequence gaps remain
        /// distinct observations; none is relabelled as measured WAN packet loss.
        /// </summary>
        public static void ReportAuthoritative(NetClient client, ClientPrediction prediction,
            SnapshotInterpolation interpolation, NetTrafficMetrics traffic,
            PredictedHitFeedback hitPrediction, PredictedSelfImpulse selfImpulsePrediction)
        {
            if (!Enabled) return;
            long now = Stopwatch.GetTimestamp();
            var counters = new NetDiagnosticCounters(client.Connection?.Id ?? 0, client.Accepted.MatchId,
                client.State, client.HasSnapshot, client.Snapshot.Sequence, client.SnapshotsReceived,
                traffic.BytesReceived, traffic.BytesSent);
            if (!_authoritativeWindow.TrySample(now, counters, out NetDiagnosticRates rates)) return;

            HitPredictionMetrics hit = hitPrediction.Metrics;
            SelfImpulseMetrics selfImpulse = selfImpulsePrediction.Metrics;
            NetMetrics clock = client.Clock.Metrics;
            NetMetrics? connection = client.Connection?.Metrics;
            NetSample error = prediction.Error;
            string packetIntervalPercentiles = connection is null ? "n/a" : Percentiles(connection.PacketReceiveIntervalMs);
            string snapshotIntervalPercentiles = connection is null ? "n/a" : Percentiles(connection.SnapshotIntervalMs);
            double? estimatedAge = client.HasSnapshot && client.Clock.Synchronized
                ? NetDiagnosticWindow.EstimatedSnapshotAgeMs(client.Clock.EstimateServerTick(now), client.Snapshot.ServerTick)
                : null;
            double? receivedGap = client.HasSnapshot && client.SnapshotReceivedAt > 0
                ? Math.Max(0, now - client.SnapshotReceivedAt) * (1000.0 / Stopwatch.Frequency) : null;

            Console.WriteLine($"[net] slot={client.Accepted.Slot} state={client.State}"
                + $" players={client.SnapshotPlayers.Length} RTT last/smoothed/min="
                + $"{Sample(clock.Rtt.Count > 0 ? clock.Rtt.Last : null)}/"
                + $"{Sample(clock.Rtt.Count > 0 ? clock.SmoothedRttMs : null)}/"
                + $"{Sample(clock.Rtt.Count > 0 ? clock.Rtt.Min : null)} ms"
                + $" RTT-jitter={Sample(clock.Rtt.Count > 1 ? clock.JitterMs : null)} ms"
                + $" RTT-p50/p95/p99/p99.9/max={Percentiles(clock.Rtt)} ms"
                + $" packet-interval-p50/p95/p99/p99.9/max={packetIntervalPercentiles} ms"
                + $" snapshot-interval-p50/p95/p99/p99.9/max={snapshotIntervalPercentiles} ms"
                + $" snapshots={Sample(rates.SnapshotHz)} Hz age-est={Sample(estimatedAge)} ms"
                + $" received-gap={Sample(receivedGap)} ms server-silence={Sample(connection?.SilenceMs(now))} ms"
                + $" missing-or-stale={Sample(rates.MissingOrStalePercent)}%"
                + $" ({rates.MissingOrStaleSnapshots}/{rates.SnapshotSequenceSpan}) WAN-loss=n/a");
            Console.WriteLine($"[net-render] prediction-error last/avg/max="
                + $"{Sample(error.Count > 0 ? error.Last : null)}/{Sample(error.Count > 0 ? error.Mean : null)}/"
                + $"{Sample(error.Count > 0 ? error.Max : null)} world-units"
                + $" corrections={prediction.Corrections} hard={prediction.HardCorrections} history-misses={prediction.HistoryMisses}"
                + $" interpolation-samples={interpolation.InterpolatedSamples} underrun={interpolation.UnderrunSamples}"
                + $" extrapolated={interpolation.ExtrapolatedSamples} held={interpolation.HeldSamples}"
                + $" delay-ticks={interpolation.DelayTicks:0.###}"
                + $" max-extrapolation={Sample(interpolation.MaximumExtrapolationTicks * (1000.0 / 60))} ms");
            Console.WriteLine($"[net-traffic] packets in/out={traffic.PacketsReceived}/{traffic.PacketsSent}"
                + $" bytes in/out={counters.BytesReceived}/{counters.BytesSent}"
                + $" KiB/s in/out={Sample(rates.BytesReceivedPerSecond / 1024)}/{Sample(rates.BytesSentPerSecond / 1024)}"
                + $" queue-age avg/max={Sample(connection?.QueueAgeMs.Count > 0 ? connection.QueueAgeMs.Mean : null)}/"
                + $"{Sample(connection?.QueueAgeMs.Count > 0 ? connection.QueueAgeMs.Max : null)} ms"
                + $" rejected transport/protocol={traffic.PacketsRejected}/{client.Rejected}"
                + $" queue-drops={traffic.QueueDrops} simulated-drops={traffic.SimulatedDrops} send-errors={traffic.SendErrors}");
            Console.WriteLine($"[net-hit] discrete predicted/confirmed/denied/authority-only="
                + $"{hit.Predicted}/{hit.Confirmed}/{hit.Denied}/{hit.AuthoritativeUnpredicted}"
                + $" pending={hit.Pending} duplicate-prevented={hit.DuplicatePrevented}"
                + $" headshot/kill-promotions={hit.HeadshotPromotions}/{hit.KillPromotions}"
                + $" confirmation/denial={Sample(hit.ConfirmationRate * 100)}/{Sample(hit.DenialRate * 100)}%"
                + $" shot-to-predicted/confirmed/lead={Sample(hit.MeanShotToPredictionFrames)}"
                + $"/{Sample(hit.MeanShotToConfirmationFrames)}/{Sample(hit.MeanPredictionLeadFrames)} frames"
                + $" continuous predicted/confirmed/denied/authority-only/pending="
                + $"{hit.ContinuousPredicted}/{hit.ContinuousConfirmed}/{hit.ContinuousDenied}"
                + $"/{hit.ContinuousAuthoritativeUnpredicted}/{hit.ContinuousPending}");
            Console.WriteLine($"[net-self-impulse] predicted/confirmed/expired/authority-applied="
                + $"{selfImpulse.Predicted}/{selfImpulse.Confirmed}/{selfImpulse.Expired}"
                + $"/{selfImpulse.AuthoritativeApplied} bomb-jumps={selfImpulse.BombJumpsPredicted}"
                + $" duplicate-prevented={selfImpulse.DuplicatePrevented} pending={selfImpulse.Pending}"
                + $" impulse avg/max={Sample(selfImpulse.MeanImpulseMagnitude)}"
                + $"/{Sample(selfImpulse.ImpulseMagnitudeMax)} corrections avg/max="
                + $"{Sample(selfImpulse.MeanCorrectionDistance)}/{Sample(selfImpulse.CorrectionDistanceMax)}"
                + $" hard={selfImpulse.HardCorrections}");
        }

        private static string Sample(double? value)
            => value?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";

        private static string Percentiles(NetSample sample)
        {
            if (sample.Count == 0) return "n/a";
            BoundedPercentileSnapshot percentiles = sample.Percentiles;
            return $"{percentiles.P50:0.0}/{percentiles.P95:0.0}/{percentiles.P99:0.0}/"
                + $"{percentiles.P999:0.0}/{percentiles.Max:0.0}";
        }

        public static void Report(Scene scene, double time)
        {
            if (!Enabled || !NetSession.Active)
            {
                return;
            }
            if (time - _lastReport < 1.0)
            {
                return;
            }
            _lastReport = time;

            var line = new StringBuilder();
            line.Append("[netdbg] role=").Append(NetSession.Role);
            line.Append(" slot=").Append(NetSession.LocalSlot);

            int active = 0;
            int created = 0;
            line.Append(" slots=[");
            for (int i = 0; i < scene.Players.MaxPlayers; i++)
            {
                PlayerEntity? player = scene.Players[i];
                if (player == null)
                {
                    line.Append('-');
                    continue;
                }
                created++;
                bool isActive = player.LoadFlags.TestFlag(LoadFlags.Active);
                if (isActive)
                {
                    active++;
                }
                // A = active player, B = active but AI-driven (wrong on a
                // remote slot -- PlayerAi would overwrite network intent),
                // s = slot-active only, . = present but inactive.
                line.Append(isActive ? (player.IsBot ? 'B' : 'A')
                    : player.LoadFlags.TestFlag(LoadFlags.SlotActive) ? 's' : '.');
            }
            line.Append("] active=").Append(active);
            line.Append(" scoreboard=").Append(scene.Match.ActivePlayers);
            line.Append(" created=").Append(created);

            line.Append(" remoteState=[");
            for (int i = 0; i < NetSession.RemoteStateValid.Length; i++)
            {
                line.Append(NetSession.RemoteStateValid[i] ? 'y' : 'n');
            }
            line.Append("] remoteIntent=[");
            for (int i = 0; i < NetSession.RemoteIntentValid.Length; i++)
            {
                line.Append(NetSession.RemoteIntentValid[i] ? 'y' : 'n');
            }
            line.Append(']');

            // Surface the failure directly rather than leaving it to be
            // spotted in the slot map: an AI-driven remote slot is always a
            // bug, and it is the one that produced bots in a networked match.
            int botRemotes = 0;
            for (int i = 0; i < scene.Players.MaxPlayers; i++)
            {
                PlayerEntity? p = scene.Players[i];
                if (p != null && i != NetSession.LocalSlot && p.IsBot
                    && p.LoadFlags.TestFlag(LoadFlags.Active))
                {
                    botRemotes++;
                }
            }
            if (botRemotes > 0)
            {
                line.Append("  !! ").Append(botRemotes).Append(" remote slot(s) still AI-driven");
            }

            MatchStatePacket? match = NetSession.ServerMatch;
            if (match != null)
            {
                line.Append(" serverMap=").Append(match.Value.RoomKey);
                line.Append(" serverPlayers=").Append(match.Value.PlayerCount);
            }
            Console.WriteLine(line.ToString());
            Console.WriteLine("[netmetrics] " + NetSession.Metrics.Describe(Stopwatch.GetTimestamp())
                + $" frame-work avg/max {NetSession.Metrics.WorkDurationMs.Mean:0.000}"
                + $"/{NetSession.Metrics.WorkDurationMs.Max:0.000} ms");
            if (NetSession.TrafficMetrics is NetTrafficMetrics traffic)
            {
                Console.WriteLine("[nettraffic] " + traffic.Describe());
            }
        }
    }
}
