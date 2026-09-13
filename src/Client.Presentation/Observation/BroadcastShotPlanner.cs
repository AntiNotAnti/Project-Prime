using System;
using MphRead.Combat;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead;

public enum BroadcastShotType
{
    FirstPerson,
    TightChase,
    WideChase,
    Orbit,
    ObjectiveWide,
    FreeEstablishing
}

public enum BroadcastObserverPreset
{
    Competitive,
    Cinematic,
    Objective,
    Manual
}

public enum BroadcastShotReason
{
    NoFocus,
    PrecisionCombat,
    Pursuit,
    MultiPlayerEncounter,
    ObjectiveCarrier,
    ContestedObjective,
    ObjectiveOverview,
    FocusedPlayer
}

public readonly record struct BroadcastShotSelection(
    BroadcastFocus Focus,
    CombatActor FocusActor,
    BroadcastShotType Shot,
    BroadcastShotReason Reason,
    int CandidateScore);

public readonly record struct BroadcastShotPlannerDiagnostics(
    BroadcastFocus Focus,
    BroadcastShotType SelectedShot,
    BroadcastShotReason Reason,
    int CandidateScore,
    bool CollisionAdjusted,
    uint TransitionAge);

public readonly record struct BroadcastCameraPose(Vector3 Position, Vector3 Target);

public enum BroadcastCameraTransitionKind { Cut, Blend, Establishing }

/// <summary>
/// Presentation-only camera blend. Durations are fixed-timeline ticks so the
/// same delivered replay state produces the same transition on every host.
/// </summary>
public readonly record struct BroadcastCameraTransition(
    BroadcastCameraPose FromPose,
    BroadcastCameraPose ToPose,
    uint StartTick,
    uint DurationTicks,
    BroadcastCameraTransitionKind TransitionKind)
{
    public uint Age(uint tick)
        => Sequence32.IsNewer(StartTick, tick) ? 0 : unchecked(tick - StartTick);

    public bool Complete(uint tick) => Age(tick) >= DurationTicks;

    public BroadcastCameraPose Interpolate(BroadcastCameraPose current, uint tick)
    {
        if (TransitionKind == BroadcastCameraTransitionKind.Cut || DurationTicks == 0)
            return current;
        float amount = Math.Clamp(Age(tick) / (float)DurationTicks, 0, 1);
        // Smoothstep has deterministic endpoints and avoids a visible velocity
        // discontinuity without introducing presentation-time state.
        amount = amount * amount * (3 - 2 * amount);
        return new(Vector3.Lerp(FromPose.Position, current.Position, amount),
            Vector3.Lerp(FromPose.Target, current.Target, amount));
    }
}

/// <summary>
/// Chooses how to compose the focus selected by <see cref="BroadcastDirector"/>.
/// It consumes only delivered observation facts and never mutates simulation.
/// </summary>
public sealed class BroadcastShotPlanner
{
    private const float NearbyDistanceSquared = 12 * 12;
    private const float PursuitSpeedSquared = 0.08f * 0.08f;
    private const uint RecentCombatTicks = 120;

    private BroadcastShotSelection _selection = new(BroadcastFocus.None,
        CombatActor.None, BroadcastShotType.FreeEstablishing,
        BroadcastShotReason.NoFocus, 0);
    private bool _collisionAdjusted;
    private uint _transitionAge;

    public BroadcastObserverPreset Preset { get; private set; }
        = BroadcastObserverPreset.Competitive;

    public BroadcastShotSelection Selection => _selection;

    public void SetPreset(BroadcastObserverPreset preset)
    {
        if (!Enum.IsDefined(preset)) throw new ArgumentOutOfRangeException(nameof(preset));
        Preset = preset;
    }

    public void Reset()
    {
        _selection = new(BroadcastFocus.None, CombatActor.None,
            BroadcastShotType.FreeEstablishing, BroadcastShotReason.NoFocus, 0);
        _collisionAdjusted = false;
        _transitionAge = 0;
    }

    public BroadcastShotSelection Select(ObservationContext context, BroadcastFocus focus)
    {
        ArgumentNullException.ThrowIfNull(context);
        _selection = focus.Kind switch
        {
            BroadcastFocusKind.Player => SelectPlayer(context, focus),
            BroadcastFocusKind.Objective => SelectObjective(context, focus),
            _ => new(focus, CombatActor.None, BroadcastShotType.FreeEstablishing,
                BroadcastShotReason.NoFocus, 0)
        };
        return _selection;
    }

    public BroadcastShotPlannerDiagnostics CaptureDiagnostics()
        => new(_selection.Focus, _selection.Shot, _selection.Reason,
            _selection.CandidateScore, _collisionAdjusted, _transitionAge);

    internal void UpdatePresentationDiagnostics(bool collisionAdjusted,
        uint transitionAge)
    {
        _collisionAdjusted = collisionAdjusted;
        _transitionAge = transitionAge;
    }

    private BroadcastShotSelection SelectPlayer(ObservationContext context,
        BroadcastFocus focus)
    {
        if (!context.TryGetPlayer(focus.Id, out ObservationPlayer player)
            || !player.Selectable)
            return new(focus, CombatActor.None, BroadcastShotType.FreeEstablishing,
                BroadcastShotReason.NoFocus, 0);

        int nearbyActors = 0;
        int nearbyEnemies = 0;
        foreach (ObservationPlayer other in context.Players)
        {
            if (!other.Selectable || other.Slot == player.Slot
                || (other.Position - player.Position).LengthSquared > NearbyDistanceSquared)
                continue;
            nearbyActors++;
            if (other.Team != player.Team) nearbyEnemies++;
        }

        bool recentCombat = false;
        bool recentPrecisionKill = false;
        foreach (CombatEvent value in context.CombatEvents)
        {
            if (!Recent(context.DeliveredTick, value.Tick, RecentCombatTicks)) continue;
            if (!Matches(value.Actor, player) && !Matches(value.Target, player)) continue;
            recentCombat = true;
            if (Matches(value.Actor, player)
                && (value.Flags & CombatEventFlags.Headshot) != 0)
                recentPrecisionKill = true;
        }

        bool precision = recentCombat && (recentPrecisionKill
            || player.Weapon == BeamType.Imperialist);
        if (precision)
            return new(focus, player.Identity, BroadcastShotType.FirstPerson,
                BroadcastShotReason.PrecisionCombat, 400 + nearbyEnemies * 10);

        if (nearbyActors >= 3)
            return new(focus, player.Identity, BroadcastShotType.WideChase,
                BroadcastShotReason.MultiPlayerEncounter, 300 + nearbyActors * 10);

        if (player.CarriesObjective)
            return new(focus, player.Identity,
                Preset == BroadcastObserverPreset.Cinematic
                    ? BroadcastShotType.WideChase : BroadcastShotType.TightChase,
                BroadcastShotReason.ObjectiveCarrier, 275 + nearbyEnemies * 10);

        if (nearbyEnemies > 0 && player.Velocity.LengthSquared >= PursuitSpeedSquared)
            return new(focus, player.Identity, BroadcastShotType.TightChase,
                BroadcastShotReason.Pursuit, 240 + nearbyEnemies * 10);

        BroadcastShotType defaultShot = Preset switch
        {
            BroadcastObserverPreset.Cinematic => BroadcastShotType.Orbit,
            BroadcastObserverPreset.Objective when context.Objectives.Length > 0
                => BroadcastShotType.WideChase,
            _ => BroadcastShotType.TightChase
        };
        return new(focus, player.Identity, defaultShot,
            BroadcastShotReason.FocusedPlayer, 100);
    }

    private BroadcastShotSelection SelectObjective(ObservationContext context,
        BroadcastFocus focus)
    {
        if (!context.TryGetObjective(focus.Id, out ObservationObjective objective))
            return new(focus, CombatActor.None, BroadcastShotType.FreeEstablishing,
                BroadcastShotReason.NoFocus, 0);
        if (objective.Contested)
            return new(focus, CombatActor.None, BroadcastShotType.ObjectiveWide,
                BroadcastShotReason.ContestedObjective, 400);
        if (objective.HasCarrier)
            return new(focus, CombatActor.None, BroadcastShotType.ObjectiveWide,
                BroadcastShotReason.ObjectiveCarrier, 250);
        BroadcastShotType shot = Preset == BroadcastObserverPreset.Cinematic
            ? BroadcastShotType.Orbit : BroadcastShotType.ObjectiveWide;
        return new(focus, CombatActor.None, shot,
            BroadcastShotReason.ObjectiveOverview, 150);
    }

    private static bool Matches(CombatActor actor, ObservationPlayer player)
        => actor.IsValid && player.Identity.IsValid && actor == player.Identity;

    private static bool Recent(uint now, uint then, uint duration)
        => !Sequence32.IsNewer(then, now) && unchecked(now - then) < duration;
}
