using MphRead.Mods.Network;

namespace MphRead.Combat;

/// <summary>
/// Orders the presentation of one authoritative victim life.
///
/// A kill event and a snapshot are two transports for the same death.  This
/// gate remembers the actor identity, so either transport may arrive first,
/// while a repeated snapshot or reliable retransmit cannot restart the cue.
/// It contains no gameplay state and never changes health, score, or respawn.
/// </summary>
public sealed class DeathPresentationGate
{
    // PlayerEntity.RespawnTime is three seconds at the authoritative 60 Hz
    // simulation rate. The authored blue death effect occupies its first
    // third, i.e. 60 server ticks.
    public const uint DefaultParticleTicks = 60;

    private CombatActor _actor = CombatActor.None;
    private bool _observed;
    private bool _dead;
    private bool _killObserved;
    private bool _presented;
    private bool _altForm;
    private bool _hasKillTick;
    private uint _killTick;

    public CombatActor Actor => _actor;
    public bool Presented => _presented;
    public bool IsDead => _dead;

    public void Reset()
    {
        _actor = CombatActor.None;
        _observed = _dead = _killObserved = _presented = false;
        _altForm = false;
        _hasKillTick = false;
        _killTick = 0;
    }

    /// <summary>
    /// Records an accepted snapshot. The first snapshot of an already-dead
    /// actor is intentionally not enough to invent a death cue. A kill event
    /// retained before that snapshot is explicit confirmation and is allowed
    /// to present it.
    /// </summary>
    public DeathPresentationCue ObserveSnapshot(CombatActor actor, bool dead,
        bool altForm, uint tick, bool presentationAlreadyHandled = false)
    {
        if (!actor.IsValid)
        {
            return default;
        }

        if (_actor != actor)
        {
            Reset();
            _actor = actor;
            _observed = true;
            _dead = dead;
            _altForm = altForm;
            // The snapshot is the first observation for this actor. Do not
            // manufacture a sound/particle burst from a join-in-progress
            // dead state. ObserveKill(actor) can still confirm it later.
            return default;
        }

        _altForm = altForm;
        if (!dead)
        {
            if (_dead)
            {
                // A life normally changes with a respawn. Treat an anomalous
                // same-life resurrection as a fresh observation boundary so a
                // later authoritative death is not swallowed forever.
                _presented = false;
                _killObserved = false;
            }
            _dead = false;
            _observed = true;
            return default;
        }

        bool becameDead = _observed && !_dead;
        _observed = true;
        _dead = true;
        if (_presented || (!becameDead && !_killObserved))
        {
            return default;
        }

        return Present(tick, presentationAlreadyHandled);
    }

    /// <summary>
    /// Records an accepted reliable kill event. If the victim snapshot has
    /// already confirmed the death, this is the explicit confirmation needed
    /// after a first-observation-dead snapshot; otherwise it is held until a
    /// matching dead snapshot arrives.
    /// </summary>
    public DeathPresentationCue ObserveKill(CombatActor victim, uint tick,
        bool presentationAlreadyHandled = false)
    {
        if (!victim.IsValid)
        {
            return default;
        }

        if (_actor != victim)
        {
            Reset();
            _actor = victim;
        }

        _killObserved = true;
        _hasKillTick = true;
        _killTick = tick;
        if (!_observed || !_dead || _presented)
        {
            return default;
        }

        return Present(tick, presentationAlreadyHandled);
    }

    private DeathPresentationCue Present(uint tick, bool presentationAlreadyHandled)
    {
        _presented = true;
        // A reliable KillEvent carries the authoritative simulation tick. If
        // it arrived before the dead snapshot, retain that tick instead of
        // accidentally anchoring presentation to packet receipt/snapshot
        // order. Snapshot-only fallback remains fail-soft for lost events.
        uint presentationTick = _hasKillTick ? _killTick : tick;
        return new DeathPresentationCue(_actor, presentationTick, _altForm,
            presentationAlreadyHandled);
    }
}

public readonly record struct DeathPresentationCue(CombatActor Actor, uint Tick, bool AltForm,
    bool EnginePresentationHandled = false)
{
    public bool IsValid => Actor.IsValid;
}

/// <summary>Pure roster-to-presentation actor fence for reliable kills.</summary>
public static class DeathPresentationBinding
{
    public static bool Accepts(in CombatActor victim, ulong rosterConnectionId,
        uint rosterLife, in CombatActor presentationActor,
        in CombatActor gateActor, in CombatActor capturedActor)
    {
        if (!victim.IsValid || rosterConnectionId != victim.ConnectionId)
            return false;
        if (rosterLife == victim.Life)
            return presentationActor == victim;
        return victim.Life != uint.MaxValue && rosterLife == victim.Life + 1
            && (gateActor == victim || capturedActor == victim);
    }
}

/// <summary>
/// Keeps combat-feed deduplication independent from the idempotent death gate.
/// </summary>
public static class DeathPresentationRouting
{
    public static bool IsSemanticCurrent(in KillEvent kill, uint currentMatchId,
        uint currentPhaseRevision)
        => kill.MatchId == currentMatchId
            && kill.PhaseRevision == currentPhaseRevision;

    public static bool ShouldObserveKill(bool combatFeedAccepted, bool actorBound,
        bool semanticCurrent)
    {
        _ = combatFeedAccepted;
        return actorBound && semanticCurrent;
    }
}
