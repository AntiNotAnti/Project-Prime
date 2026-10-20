using System;

namespace MphRead.Mods.CustomWeapons;

// Only the authenticated transport owner publishes accepted current carriers.
// The engine opens a boundary only after actually consuming that exact input.
internal interface ICustomSourceBoundaryReadiness : ICustomSourceReadiness
{
    bool TryOpen(in CustomSourceInput input,out CustomSourceReadyContext context);
}

internal sealed class CustomPeerSourceBoundary : ICustomSourceBoundaryReadiness
{
    private readonly CustomReadinessPeer _peer;
    private CustomPeerReadyEvidence? _ready;
    private CustomSourceInput _latest;
    private bool _hasInput,_invalidated;
    private ulong _serial,_readyFence;
    private CustomSourceReadyContext? _context;
    internal CustomPeerSourceBoundary(CustomReadinessPeer peer)
        => _peer = peer ?? throw new ArgumentNullException(nameof(peer));
    private bool SameOccupant(WeaponOwnerContext owner)
        => owner.IsValid && owner.Slot == _peer.Wire.Slot
            && owner.MatchId == _peer.Wire.Bootstrap.Start.MatchId
            && owner.AuthorityEpoch == _peer.Wire.Bootstrap.Start.AuthorityEpoch
            && owner.Generation == _peer.Wire.Bootstrap.SlotGeneration;
    internal bool NoteAcceptedInput(in CustomSourceInput input)
    {
        if (_invalidated || _peer.Invalidated || !input.IsValid || !SameOccupant(input.Owner)) return false;
        if (_hasInput && (input.Owner.Life == _latest.Owner.Life
                ? unchecked((int)(input.Frame-_latest.Frame)) <= 0
                : unchecked((short)(input.Owner.Life-_latest.Owner.Life)) <= 0)) return false;
        if (_serial == ulong.MaxValue) { _invalidated = true; return false; }
        _latest = input; _hasInput = true; _serial++; return true;
    }
    internal bool ConfirmReadiness()
    {
        if (_invalidated || _peer.Invalidated || _peer.Evidence is not { } ready) return false;
        if (_ready != null)
        {
            if (!ReferenceEquals(_ready,ready)) _invalidated = true;
            return !_invalidated;
        }
        _ready = ready; _readyFence = _serial; return true;
    }
    private bool Current
    {
        get
        {
            if (_invalidated || _peer.Invalidated || _ready == null) return false;
            if (ReferenceEquals(_ready,_peer.Evidence)) return true;
            _invalidated = true; return false;
        }
    }
    public bool TryOpen(in CustomSourceInput input,out CustomSourceReadyContext context)
    {
        context = default;
        if (!Current || !_hasInput || _serial <= _readyFence || input != _latest || !SameOccupant(input.Owner)) return false;
        if (_context is { } opened && unchecked((int)(input.Frame-opened.FirstSourceFrame)) < 0) return false;
        if (_context == null)
            _context = new(new(_ready!.Bootstrap,_ready.Manifest,_ready.Slot,WeaponMechanics.SchemaVersion,
                WeaponMechanics.BehaviorVersion,CustomWeaponReady.PrecisionCapability),
                _ready.OpenedWorld,input.Frame,_ready.ConnectionId);
        context = _context.Value; return true;
    }
    public bool TryGet(WeaponOwnerContext owner,out CustomSourceReadyContext context)
    {
        context = default;
        if (!Current || !_context.HasValue || !SameOccupant(owner)) return false;
        context = _context.Value; return true;
    }
}
