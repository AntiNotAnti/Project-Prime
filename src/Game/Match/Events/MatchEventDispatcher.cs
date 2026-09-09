using System;

namespace MphRead;

/// <summary>
/// One small synchronous dispatcher. It has a fixed sink budget and runs only
/// on the owning MatchInstance thread; it is deliberately not an event bus.
/// </summary>
public sealed class MatchEventDispatcher
{
    public const int MaximumSinks = 16;

    private readonly IMatchEventSink?[] _sinks = new IMatchEventSink?[MaximumSinks];
    private readonly MatchEventSink?[] _callbacks = new MatchEventSink?[MaximumSinks];
    private uint _nextId = 1;
    private bool _dispatching;

    public int SinkCount { get; private set; }
    public uint LastEventId { get; private set; }

    public bool Subscribe(IMatchEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (_dispatching) throw new InvalidOperationException("Match event sinks cannot change during dispatch.");
        if (SinkCount == MaximumSinks) return false;
        for (int i = 0; i < MaximumSinks; i++)
            if (_sinks[i] == null && _callbacks[i] == null)
            {
                _sinks[i] = sink;
                SinkCount++;
                return true;
            }
        return false;
    }

    public bool Subscribe(MatchEventSink callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (_dispatching) throw new InvalidOperationException("Match event sinks cannot change during dispatch.");
        if (SinkCount == MaximumSinks) return false;
        for (int i = 0; i < MaximumSinks; i++)
            if (_sinks[i] == null && _callbacks[i] == null)
            {
                _callbacks[i] = callback;
                SinkCount++;
                return true;
            }
        return false;
    }

    public bool Unsubscribe(IMatchEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (_dispatching) throw new InvalidOperationException("Match event sinks cannot change during dispatch.");
        for (int i = 0; i < MaximumSinks; i++)
            if (_sinks[i] == sink)
            {
                _sinks[i] = null;
                SinkCount--;
                return true;
            }
        return false;
    }

    /// <summary>
    /// Dispatches one fact and returns the normalized fact. Producers pass
    /// <c>Id = 0</c>; only this match-owned dispatcher allocates semantic IDs.
    /// An explicit ID remains accepted for replay/reliable redelivery, where
    /// preserving the recorded semantic identity is required.
    /// </summary>
    public MatchEvent Dispatch(in MatchEvent value)
    {
        MatchEvent normalized = value;
        if (normalized.Id == 0)
        {
            normalized = normalized with { Id = NextId() };
        }
        else if (normalized.Id >= _nextId)
        {
            _nextId = normalized.Id == uint.MaxValue ? 1 : normalized.Id + 1;
        }
        if (!normalized.IsValid) throw new ArgumentException("Invalid match semantic event.", nameof(value));
        LastEventId = normalized.Id;
        _dispatching = true;
        try
        {
            for (int i = 0; i < MaximumSinks; i++)
            {
                _sinks[i]?.OnMatchEvent(normalized);
                _callbacks[i]?.Invoke(in normalized);
            }
        }
        finally
        {
            _dispatching = false;
        }
        return normalized;
    }

    public void Reset()
    {
        if (_dispatching) throw new InvalidOperationException("Match events cannot reset during dispatch.");
        for (int i = 0; i < MaximumSinks; i++) _sinks[i]?.Reset();
        // Reset consumer state at a round/phase boundary, but keep allocating
        // from the same match-owned identity sequence. Reusing an old semantic
        // ID would make a delayed event from an earlier round indistinguishable
        // from a new fact in replay/reliable consumers.
        LastEventId = 0;
    }

    private uint NextId()
    {
        uint value = _nextId++;
        if (_nextId == 0) _nextId = 1;
        return value == 0 ? NextId() : value;
    }
}
