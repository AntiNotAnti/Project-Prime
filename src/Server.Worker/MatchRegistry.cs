using System.Security.Cryptography;
using ProjectPrime.Server.Shared;
using MphRead.Mods.Network;
using ProjectPrime.Server.Worker.Simulation;
using MphRead.Mods.MapGen;

namespace ProjectPrime.Server.Worker;

/// <summary>Control-plane records only. Match mutation never runs under this registry lock.</summary>
internal sealed class MatchRegistry
{
    internal sealed class Entry(MatchSpec spec, byte[] fingerprint, WireMatchId wireId)
    {
        public MatchSpec Spec { get; } = spec;
        public bool HasPlayerSeat { get; } = spec.Roster.Any(seat => seat.Role == SeatRole.Player);
        public byte[] Fingerprint { get; } = fingerprint;
        public WireMatchId WireId { get; } = wireId;
        public TaskCompletionSource<WorkerEvent> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SimulationLaneManager.Lease? Lease;
        public MatchInstance? Instance;
        public MatchDatagramTransport? Transport;
        public WorkerTicketAuthority? Tickets;
        private MatchInstanceStatus? _snapshot;
        /// <summary>
        /// Immutable control-plane status. The lane publishes a whole record;
        /// readers must never observe fields being assembled in place.
        /// </summary>
        public MatchInstanceStatus? Snapshot
        {
            get => Volatile.Read(ref _snapshot);
            set => Volatile.Write(ref _snapshot, value);
        }
        public WorkerEvent? Terminal;
        public MatchContentSnapshot? ContentSnapshot;
        public bool Released;
    }
    public object Gate { get; } = new();
    public Dictionary<MatchId, Entry> Entries { get; } = new();
    public Dictionary<uint, Entry> ByWireId { get; } = new();
    private uint _nextWireId;
    public uint AllocateWireId()
    {
        if (_nextWireId == uint.MaxValue) throw new InvalidOperationException("Worker wire identity space exhausted.");
        return ++_nextWireId;
    }
    public static byte[] Fingerprint(MatchSpec spec) => SHA256.HashData(WorkerIpcCodec.Encode(new CreateMatch(spec)));
}
