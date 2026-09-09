using MphRead.Mods.Network;

namespace MphRead;

/// <summary>Bounded semantic award fact. No display text crosses the wire.</summary>
public readonly record struct MatchAward(uint AwardId, uint SourceEventId, uint MatchId,
    uint PhaseRevision, uint Tick, MatchAwardKind Kind, CombatActor Subject,
    CombatActor Target = default, byte Count = 1, ushort Value = 0)
{
    public bool IsValid => AwardId != 0 && SourceEventId != 0 && MatchId != 0 && PhaseRevision != 0
        && Kind is >= MatchAwardKind.FirstHunt and <= MatchAwardKind.Assist
        && Subject.IsValid && (Target.IsNone || Target.IsValid) && Count > 0;

    public int Priority => Kind switch
    {
        MatchAwardKind.TripleKill => 100,
        MatchAwardKind.DoubleKill => 90,
        MatchAwardKind.FirstHunt => 80,
        MatchAwardKind.PrimeSlayer => 70,
        MatchAwardKind.Interceptor => 60,
        MatchAwardKind.Defender => 50,
        MatchAwardKind.Capture => 40,
        MatchAwardKind.Assist => 20,
        _ => 0
    };
}
