namespace MphRead;

/// <summary>Presentation/highlight facts. These never change score or rating.</summary>
public enum MatchAwardKind : byte
{
    FirstHunt = 1,
    DoubleKill,
    TripleKill,
    Interceptor,
    Defender,
    PrimeSlayer,
    Capture,
    Assist
}
