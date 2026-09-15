using System.Diagnostics;
using ProjectPrime.Server.Shared;
using MphRead.Hud.Network;
namespace MphRead.Mods.Network
{
    public sealed record SemanticAuthoritativeDiagnostics(
        bool Available,
        string? MatchId,
        ulong? ConnectionId,
        byte? Slot,
        uint? Life,
        string? Phase,
        string? WorkerId,
        string WorkerOwnership);

    public sealed partial class AuthoritativePlay
    {
        /// <summary>Bound local UDP port for owned-run diagnostics only.</summary>
        internal int LocalUdpPort => _transport.LocalPort;
        public NetworkHealthReading? ReadNetworkHealth() => Client.Connection is { } connection
            ? NetworkHealthReading.Read(connection.Metrics, Prediction, _interpolation, Stopwatch.GetTimestamp()) : null;

        /// <summary>
        /// Exposes only authority facts already owned by the live client.
        /// Worker identity is intentionally unavailable because the Node
        /// handoff contract does not expose it to clients.
        /// </summary>
        public SemanticAuthoritativeDiagnostics ReadSemanticDiagnostics()
        {
            SnapshotPlayer? local = null;
            foreach (SnapshotPlayer player in Client.SnapshotPlayers)
            {
                if (player.Slot == Client.Accepted.Slot)
                {
                    local = player;
                    break;
                }
            }
            return new SemanticAuthoritativeDiagnostics(
                Available: State == TerminalState.Active && Client.Connection != null,
                MatchId: NodeMatchId?.ToString("N"),
                ConnectionId: local?.ConnectionId ?? Client.Connection?.Id,
                Slot: IsObserver ? null : Client.Accepted.Slot,
                Life: local?.Life,
                Phase: _presentationScene?.Match.Phase.ToString(),
                WorkerId: null,
                WorkerOwnership: "unavailable; Node handoff does not expose Worker identity");
        }
    }
}
