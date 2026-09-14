namespace MphRead.Mods.Network;

/// <summary>
/// Fences local weapon prediction against snapshots that were produced before
/// the authority processed the input that requested the new weapon.
/// </summary>
internal sealed class PendingWeaponPrediction
{
    internal const byte NoWeapon = 255;

    private bool _hasEpoch;
    private ulong _connectionId;
    private uint _life;
    private byte _authoritativeWeapon = NoWeapon;
    private bool _hasPending;
    private byte _pendingWeapon = NoWeapon;
    private uint _firstInputSequence;

    internal bool HasPending => _hasPending;
    internal byte PendingWeapon => _pendingWeapon;
    internal uint FirstInputSequence => _firstInputSequence;
    internal byte AuthoritativeWeapon => _authoritativeWeapon;

    internal void Reset()
    {
        _hasEpoch = false;
        _connectionId = 0;
        _life = 0;
        _authoritativeWeapon = NoWeapon;
        _hasPending = false;
        _pendingWeapon = NoWeapon;
        _firstInputSequence = 0;
    }

    internal void ObserveAuthoritative(ulong connectionId, uint life, byte weapon)
    {
        if (!_hasEpoch || _connectionId != connectionId || _life != life)
        {
            ResetEpoch(connectionId, life);
        }
        _authoritativeWeapon = weapon;
    }

    internal void ObserveInput(ulong connectionId, uint life, byte desiredWeapon,
        uint inputSequence)
    {
        if (!_hasEpoch || _connectionId != connectionId || _life != life)
        {
            ResetEpoch(connectionId, life);
        }
        if (desiredWeapon > 8)
        {
            return;
        }
        if (desiredWeapon == _authoritativeWeapon && !_hasPending)
        {
            return;
        }
        // Returning to the last authoritative weapon is still a new request
        // when another selection is in flight. The server may acknowledge the
        // older selection first; dropping the fence here lets that intermediate
        // snapshot pull the client back to the weapon it just left.
        // Identical retransmissions are expected while UDP is in flight. Keep
        // the first sequence as the acknowledgement fence rather than moving
        // it forward on every redundant input bundle.
        if (_hasPending && _pendingWeapon == desiredWeapon)
        {
            return;
        }
        _hasPending = true;
        _pendingWeapon = desiredWeapon;
        _firstInputSequence = inputSequence;
    }

    /// <summary>
    /// Returns whether a snapshot's weapon may be applied.  A pending request
    /// is released only after the authority acknowledges the command sequence;
    /// a release applies the state even when the server rejected the request.
    /// </summary>
    internal bool ShouldApplyAuthoritative(ulong connectionId, uint life,
        bool hasProcessedInput, uint lastProcessedInput)
    {
        if (!_hasEpoch || _connectionId != connectionId || _life != life)
        {
            ResetEpoch(connectionId, life);
            return true;
        }
        if (!_hasPending)
        {
            return true;
        }
        if (!hasProcessedInput || !Reached(lastProcessedInput, _firstInputSequence))
        {
            return false;
        }
        _hasPending = false;
        _pendingWeapon = NoWeapon;
        return true;
    }

    private void ResetEpoch(ulong connectionId, uint life)
    {
        _hasEpoch = true;
        _connectionId = connectionId;
        _life = life;
        _authoritativeWeapon = NoWeapon;
        _hasPending = false;
        _pendingWeapon = NoWeapon;
        _firstInputSequence = 0;
    }

    private static bool Reached(uint value, uint target)
        => value == target || Sequence32.IsNewer(value, target);
}
