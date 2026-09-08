namespace FruityPrime.Server.Worker.Simulation;

public sealed class SimulationLaneManager : IDisposable
{
    private readonly object _gate = new();
    private readonly SimulationLane[] _lanes;
    private readonly int[] _reservations;
    private readonly WorkerOptions _options;
    private bool _disposed;
    public IReadOnlyList<LaneMetrics> Metrics => _lanes.Select(lane => lane.Metrics).ToArray();
    public SimulationLaneManager(WorkerOptions options)
    {
        options.Validate(); _options = options;
        var created = new List<SimulationLane>();
        try
        {
            for (int id = 0; id < options.SimulationLanes; id++) created.Add(new SimulationLane(id, options.CommandCapacity));
            _lanes = created.ToArray();
        }
        catch { foreach (var lane in created) lane.Dispose(); throw; }
        _reservations = new int[_lanes.Length];
    }
    public Lease Reserve()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int selected = -1;
            for (int i = 0; i < _lanes.Length; i++)
                if (_reservations[i] < _options.MaxMatchesPerLane
                    && _lanes[i].Metrics.P99Milliseconds < _options.PlacementP99Milliseconds
                    && (selected == -1 || _reservations[i] < _reservations[selected])) selected = i;
            if (selected == -1 || _reservations.Sum() >= _options.MaxMatches)
                throw new InvalidOperationException("Worker simulation lanes are at capacity or above the placement latency limit.");
            _reservations[selected]++;
            return new(this, _lanes[selected]);
        }
    }
    public sealed class Lease : IDisposable
    {
        private SimulationLaneManager? _owner;
        public SimulationLane Lane { get; }
        internal Lease(SimulationLaneManager owner, SimulationLane lane) { _owner = owner; Lane = lane; }
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner != null) lock (owner._gate) owner._reservations[Lane.Id]--;
        }
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        List<Exception> errors = new();
        foreach (var lane in _lanes) try { lane.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count > 0) throw new AggregateException(errors);
    }
}
