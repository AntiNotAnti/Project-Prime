using System;

namespace MphRead.Mods.CustomWeapons;

// Subordinate private burst state; only the Scene's current fixed step
// supplies time. This type grants no actor, ammo, emission or network authority.
internal sealed class PirateBurstTimeline
{
    internal readonly record struct Ticket(ulong Frame, uint Burst, uint Projectile, int Ordinal, int AmmoCost);
    internal readonly record struct View(bool HasStep, ulong Frame, int NextOrdinal, ulong ReadyAt,
        uint Burst, uint Projectile, bool Exhausted);
    private bool _hasStep, _usable, _proposed, _resolved, _exhausted;
    private ulong _frame, _start, _readyAt;
    private int _nextOrdinal = -1;
    private uint _burst, _projectile;
    private Ticket _proposal;
    internal View Snapshot => new(_hasStep, _frame, _nextOrdinal, _readyAt, _burst, _projectile, _exhausted);
    internal int Cooldown => !_hasStep || _readyAt <= _frame ? 0 : (int)Math.Min(40UL, _readyAt - _frame);
    internal bool HasPaidTails => _nextOrdinal >= 1;

    internal bool BeginStep(ulong frame, bool usable)
    {
        // Duplicate/out-of-order calls cannot reopen a consumed source step.
        if (_hasStep && frame <= _frame) return false;
        if (_hasStep && _proposed && !_resolved)
        {
            // A caller that abandons a source transaction cannot reopen time or
            // guess whether a native emission happened. Retire this runtime.
            _exhausted = true;
        }
        if (_hasStep && frame != _frame + 1) CancelTails();
        _hasStep = true; _frame = frame; _usable = usable;
        _proposed = _resolved = false; _proposal = default;
        if (!usable || _nextOrdinal >= 1 && frame > _start + (ulong)(_nextOrdinal * 6)) CancelTails();
        return true;
    }

    internal bool TryPlan(bool held, int nativeUa, int freeSlots, out Ticket ticket)
    {
        ticket = default;
        if (!_hasStep || !_usable || _proposed || _exhausted) return false;
        int ordinal = _nextOrdinal;
        if (ordinal >= 1)
        {
            // Paid tails consume no UA and do not require the trigger to stay held.
            if (_frame != _start + (ulong)(ordinal * 6)) return false;
            if (freeSlots < 1) { CancelTails(); return false; }
        }
        else
        {
            if (!held || nativeUa < 5 || freeSlots < 3 || _frame < _readyAt) return false;
            // Reserve a full burst's lifetime and identities before any effect.
            if (_frame > ulong.MaxValue - 40 || _burst == uint.MaxValue
                || _projectile > uint.MaxValue - 3)
            { _exhausted = true; return false; }
            ordinal = 0;
        }
        if (_projectile == uint.MaxValue)
        { _exhausted = true; CancelTails(); return false; }
        // Reserve provenance before native callbacks. A canceled/failed attempt
        // may leave an identity gap; it can never reuse an emitted flight's key.
        if (ordinal == 0) _burst++;
        _projectile++;
        ticket = _proposal = new(_frame, _burst, _projectile, ordinal, ordinal == 0 ? 5 : 0);
        _proposed = true;
        return true;
    }

    // Called exactly once after the real native Spawn outcome. The external
    // owner commits the ticket's UA5 only on a successful first emission, in
    // its validated synchronous source scope. This helper never owns ammo.
    internal bool Complete(in Ticket ticket, bool emitted)
    {
        if (!_proposed || _resolved || ticket != _proposal || ticket.Frame != _frame) return false;
        _resolved = true;
        if (ticket.Ordinal == 0)
        {
            if (emitted)
            {
                _start = _frame; _readyAt = _frame + 40;
                _nextOrdinal = _usable ? 1 : -1;
            }
        }
        else _nextOrdinal = _usable && ticket.Ordinal == 1 ? 2 : -1;
        return true;
    }

    internal void CancelTails()
    {
        _nextOrdinal = -1;
        // An open synchronous proposal still has to settle its actual Spawn
        // outcome. A successful first emission retains40 even if callbacks
        // canceled future tails; failed first emission does not gain a wait.
        _usable = false;
    }

    internal void ResetForLife()
    {
        _hasStep = false; _usable = _proposed = _resolved = false;
        _frame = _start = _readyAt = 0; _nextOrdinal = -1; _proposal = default;
        // Sequences never roll back, even across life changes. Exhaustion stays
        // terminal for this exact runtime instead of adopting stale provenance.
    }
}
