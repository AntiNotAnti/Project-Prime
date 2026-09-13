using System;

namespace MphRead.Mods.Launcher.Gui;

internal readonly record struct PrimeSeatOfferKey(Guid LobbyId, Guid OfferId)
{
    public bool IsValid => LobbyId != Guid.Empty && OfferId != Guid.Empty;
}

internal readonly record struct PrimeSeatOfferObservation(
    PrimeSeatOfferKey Key, long LobbyRevision, DateTimeOffset ExpiresAt,
    bool Available = true);

internal enum PrimeSeatOfferTransition
{
    None,
    Open,
    Close,
    Expired
}

/// <summary>
/// Single shell-owned seat-offer modal lifecycle. Lobby revision is observed
/// but deliberately excluded from identity, so ordinary snapshot rebuilds do
/// not recreate the modal or steal focus.
/// </summary>
internal sealed class PrimeSeatOfferModalState
{
    private PrimeSeatOfferKey? _active;
    private PrimeSeatOfferKey? _suppressed;

    public PrimeSeatOfferKey? Active => _active;

    public PrimeSeatOfferTransition Observe(PrimeSeatOfferObservation? observation,
        DateTimeOffset now)
    {
        if (observation is not { Available: true } current || !current.Key.IsValid)
            return ClearActive();

        if (current.ExpiresAt <= now)
        {
            if (_suppressed == current.Key) return ClearActive();
            _suppressed = current.Key;
            _active = null;
            return PrimeSeatOfferTransition.Expired;
        }
        if (_suppressed == current.Key)
            return ClearActive();
        if (_active == current.Key)
            return PrimeSeatOfferTransition.None;

        _active = current.Key;
        if (_suppressed != current.Key) _suppressed = null;
        return PrimeSeatOfferTransition.Open;
    }

    public bool Complete(PrimeSeatOfferKey key)
    {
        if (_active != key) return false;
        _active = null;
        _suppressed = key;
        return true;
    }

    /// <summary>
    /// Let the same still-authoritative offer return after its command was
    /// rejected. A different or already-superseded offer is never revived.
    /// </summary>
    public bool Release(PrimeSeatOfferKey key)
    {
        if (_suppressed != key) return false;
        _suppressed = null;
        return true;
    }

    /// <summary>Close for route/disconnect teardown without consuming the offer.</summary>
    public bool Suspend()
    {
        if (_active is null) return false;
        _active = null;
        return true;
    }

    public void Reset()
    {
        _active = null;
        _suppressed = null;
    }

    private PrimeSeatOfferTransition ClearActive()
    {
        if (_active is null) return PrimeSeatOfferTransition.None;
        _active = null;
        return PrimeSeatOfferTransition.Close;
    }
}
