namespace MphRead;

/// <summary>One synchronous consumer owned by the match thread.</summary>
public interface IMatchEventSink
{
    void OnMatchEvent(in MatchEvent value);
    void Reset();
}

/// <summary>Allocation-free callback shape for short-lived consumers.</summary>
public delegate void MatchEventSink(in MatchEvent value);
