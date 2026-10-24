using System.Numerics;
namespace ProjectPrime.PayloadEscort;

public enum Team { Alpha, Bravo }
public enum BodyKind { Biped, AlternateForm, DetachedEntity }
public enum RoundPhase { Preparation, Running, Finished }
public enum EndReason { TimeExpired, OvertimeExpired, Delivered, TargetBeaten }
public enum EventKind { Released, CheckpointReached, OvertimeStarted, OvertimeEnded, HealRequested, RoundFinished }
public readonly record struct PlayerKey(Guid OccupantId, int SlotGeneration, int LifeId);
// Adapter must construct these from current authoritative player bodies AFTER applying deaths.
// Visibility means the engine's objective-separation test, not visual transparency alone.
public sealed record PlayerSample(PlayerKey Key, Team Team, BodyKind Body, Vector3 Position,
    bool Connected=true, bool Alive=true, bool Spectator=false, bool Protected=false,
    bool ObjectiveVisible=true, int Health=1, int MaximumHealth=1);
public sealed record Checkpoint(long Distance, int AddedTicks, int HoldTicks);
public sealed record DomainEvent(Guid MatchId, int RoundId, long Tick, int Sequence,
    EventKind Kind, PlayerKey? Player=null, int Amount=0);
public sealed record RoundResult(Team Attacker, long BestDistance, int Checkpoints,
    bool Delivered, int RemainingTicks, EndReason Reason);

/// <summary>Authoring/test profile; numerical OW parity is NOT certified. All values explicit.</summary>
public sealed record EscortRules {
    public const int TickRate = 60;
    public const long DistanceScale = 60_000; // exact 1.5/1.75/2 unit/s rates at 60 Hz
    public string ProfileId { get; init; } = "escort-draft-reference-pending-v1";
    public int PreparationTicks { get; init; } = 45 * TickRate;
    public int InitialTicks { get; init; } = 240 * TickRate;
    public long OneEscortPerTick { get; init; } = 1500;
    public long TwoEscortsPerTick { get; init; } = 1750;
    public long ThreeEscortsPerTick { get; init; } = 2000;
    public int RollbackDelayTicks { get; init; } = 10 * TickRate;
    public long RollbackPerTick { get; init; } = 1000;
    public int OvertimeTouchGraceTicks { get; init; } = 3 * TickRate;
    public int OvertimeFuseTicks { get; init; } = 5 * TickRate;
    public int OvertimeMinimumFuseTicks { get; init; } = TickRate;
    public int OvertimeAccelerationAfterTicks { get; init; } = 20 * TickRate;
    public int OvertimeAccelerationEveryTicks { get; init; } = 10 * TickRate;
    public int HealIntervalTicks { get; init; } = 15;
    public float PresenceRadius { get; init; } = 5;
    public float PresenceHalfHeight { get; init; } = 2.8f;
    public int MinimumBankTicks { get; init; } = 60 * TickRate;
    public int MaximumLowerBankTicks { get; init; } = 120 * TickRate;
    public long WinningMargin { get; init; } = 60; // .001 world unit, provisional
    public void Validate() {
        if(string.IsNullOrWhiteSpace(ProfileId) || PreparationTicks<0 || InitialTicks<=0
            || InitialTicks>24*60*60*60 || PreparationTicks>36000 || ThreeEscortsPerTick>DistanceScale*100 || RollbackPerTick>DistanceScale*100
            || OneEscortPerTick<=0 || TwoEscortsPerTick<OneEscortPerTick || ThreeEscortsPerTick<TwoEscortsPerTick
            || RollbackDelayTicks<0 || RollbackPerTick<=0 || OvertimeTouchGraceTicks<0
            || OvertimeMinimumFuseTicks<=0 || OvertimeFuseTicks<OvertimeMinimumFuseTicks
            || OvertimeAccelerationAfterTicks<0 || OvertimeAccelerationEveryTicks<=0 || HealIntervalTicks<=0
            || !float.IsFinite(PresenceRadius) || PresenceRadius<=0 || !float.IsFinite(PresenceHalfHeight)
            || PresenceHalfHeight<=0 || MinimumBankTicks<=0 || MaximumLowerBankTicks<MinimumBankTicks || WinningMargin<=0)
            throw new ArgumentException("Invalid Escort rules profile.");
    }
    public long Speed(int escorts) => escorts switch { <=0=>0,1=>OneEscortPerTick,2=>TwoEscortsPerTick,_=>ThreeEscortsPerTick };
    public int FuseAt(long overtimeTicks) {
        long steps=overtimeTicks<OvertimeAccelerationAfterTicks?0:1+(overtimeTicks-OvertimeAccelerationAfterTicks)/OvertimeAccelerationEveryTicks;
        return (int)Math.Max(OvertimeMinimumFuseTicks, OvertimeFuseTicks-steps*TickRate);
    }
}
