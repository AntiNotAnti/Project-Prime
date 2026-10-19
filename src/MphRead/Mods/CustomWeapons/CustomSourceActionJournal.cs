using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

/// <summary>One occupant's fixed arrival anchor, retained across lives. Observe only
/// accepted engine input, never a claimed action timestamp. Caller owns synchronization.</summary>
internal sealed class CustomSourceClock
{
    private readonly WeaponOwnerContext _occupant;
    private readonly uint _sourceAnchor, _worldAnchor;
    private uint _worldFrontier;
    private ushort _boundLife;
    private bool _hasBoundLife;
    internal CustomSourceClock(WeaponOwnerContext owner, uint source, uint world)
    {
        if (!owner.IsValid) throw new ArgumentException("Valid occupant required.", nameof(owner));
        _occupant = owner; _sourceAnchor = source; _worldAnchor = _worldFrontier = world;
    }
    internal bool Observe(WeaponOwnerContext owner, uint source, uint world)
    {
        if (!owner.IsValid || owner.MatchId != _occupant.MatchId || owner.AuthorityEpoch != _occupant.AuthorityEpoch
            || owner.Slot != _occupant.Slot || owner.Generation != _occupant.Generation
            || unchecked((int)(world-_worldFrontier)) < 0) return false;
        long sourceElapsed = unchecked((int)(source-_sourceAnchor));
        long worldElapsed = unchecked((int)(world-_worldAnchor));
        if (sourceElapsed < -128 || sourceElapsed > worldElapsed+32) return false;
        _worldFrontier = world; return true;
    }
    internal bool TryBindLife(WeaponOwnerContext owner, uint source, uint world)
    {
        if (_hasBoundLife && unchecked((short)(owner.Life-_boundLife)) <= 0) return false;
        if (!Observe(owner,source,world)) return false;
        _boundLife = owner.Life; _hasBoundLife = true; return true;
    }
}

internal enum CustomSourceDecision : byte { Pending, Rejected, Verified }
internal enum CustomQueueResult : byte { Queued, Duplicate, Conflict, Foreign, Retired, Busy, StaleAuthority, WindowExceeded, Backpressure }
internal enum CustomJournalDisposition : byte { Accepted, ResourceDenied, SourceDenied, StaleSource, NotOwned, OpticMismatch, NoSpawn, EngineFault }
internal enum CustomEmissionDisposition : byte { NotAttempted, NoSpawn, Spawned, Uncertain }

/// <summary>Engine-only dependency, not a wire type. The implementing adapter must
/// verify exact bootstrap/readiness, immutable historical pose, clock, geometry,
/// modifiers and canonical trade eligibility. These oracles are not implemented
/// or advertised by the resource journal.</summary>
internal interface ICustomSourceAdmission
{
    CustomSourceDecision Inspect(in CustomWeaponAction action, out CustomSourceCertificate source);
}

internal readonly record struct CustomSourceCertificate(CustomWeaponAction Action, uint ObservedWorldFrame,
    uint TargetWorldFrame, byte TargetWorldSubFrame, Vector3 Body, Vector3 Aim,
    bool CanUseWeapon, bool DoubleDamage, bool PrimeHunter)
{
    internal bool IsValidFor(in CustomWeaponAction action)
    {
        if (Action != action || !Action.IsValid || !float.IsFinite(Body.X) || !float.IsFinite(Body.Y) || !float.IsFinite(Body.Z)) return false;
        if (action.Kind != CustomWeaponActionKind.Fire) return true;
        return float.IsFinite(Aim.LengthSquared) && MathF.Abs(Aim.LengthSquared-1) <= .02f
            && Vector3.Dot(action.Direction.Normalized(),Aim.Normalized()) >= .99f
            && (action.Origin-(Body+Vector3.UnitY*.5f)).LengthSquared <= 9;
    }
}

internal readonly record struct CustomAcceptedLaunch(WeaponDefinition Definition, CustomSourceCertificate Source,
    WeaponStateSnapshot Before, WeaponStateSnapshot After)
{
    internal CustomActionIdentity Identity => CustomActionIdentity.For(Source.Action);
    internal uint Component => 1; // v1 precision: exactly one component
}

internal interface ICustomWeaponEmitter
{
    bool HasCapacity(WeaponDefinition definition, WeaponOwnerContext owner);
    // NoSpawn guarantees no projectile, collision or other gameplay side effect.
    // An exception/unknown result is uncertain and must never be refunded.
    CustomEmissionDisposition Emit(in CustomAcceptedLaunch launch);
}

internal readonly record struct CustomActionReceipt(CustomWeaponAction Action, uint AuthorityFrame,
    CustomJournalDisposition Disposition, WeaponActionResult Result, CustomEmissionDisposition Emission,
    CustomSourceCertificate? Source, WeaponStateSnapshot? Before, WeaponStateSnapshot? After);

/// <summary>One immutable initial loadout and owner life. Only a contiguous prefix
/// of its universal custom action sequence can mutate source-time resource state.
/// No transport, scene callback or current inventory snapshot can bypass the gate.</summary>
internal sealed class CustomSourceActionJournal
{
    internal const int PendingCapacity = 16, TerminalCapacity = 128, MaxDrainPerTick = 4, MaxOffersPerTick = 8;
    internal const uint MaxSourceAge = 64, MaxWorldAge = 64, GapTimeout = 64, TerminalRetention = 128;
    private readonly record struct Pending(CustomWeaponAction Action, uint Arrived);
    private readonly record struct Permission(uint SourceFrame, bool CanUseWeapon);
    private readonly Pending?[] _pending = new Pending?[PendingCapacity];
    private readonly Permission?[] _permissions = new Permission?[128];
    private readonly CustomActionReceipt?[] _terminal = new CustomActionReceipt?[TerminalCapacity];
    private readonly WeaponRuntimeState?[] _states = new WeaponRuntimeState?[CustomWeaponManifest.MaxEntries];
    private readonly CustomSourceClock _clock;
    private readonly WeaponDigest _manifest;
    private readonly uint _birth;
    private uint _authority, _sourceFrontier, _committedSource;
    private uint _settled;
    private int _offered, _drained;
    private bool _processing;
    private bool _latestCanUse;
    private uint? _interruption;
    internal WeaponOwnerContext Owner { get; }
    internal WeaponRuntimeId Selected { get; private set; }
    internal bool IsRetired { get; private set; }
    internal int PendingCount { get; private set; }
    internal uint SettledSequence => _settled;
    internal uint ObservedSourceFrame => _sourceFrontier;
    internal uint CommittedSourceFrame => _committedSource;

    internal CustomSourceActionJournal(WeaponOwnerContext owner, CustomSourceClock clock,
        CustomWeaponManifest manifest, WeaponRegistry registry, IEnumerable<WeaponRuntimeId> initialLoadout,
        uint verifiedBirthSource, uint authorityFrame, bool initialCanUseWeapon)
    {
        ArgumentNullException.ThrowIfNull(clock); ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(initialLoadout);
        if (!owner.IsValid || registry.IsDisposed || registry.Count != 9+manifest.Rows.Count)
            throw new ArgumentException("Verified life boundary and exact registry required.");
        foreach (var row in manifest.Rows)
            if (!registry.TryResolve(row.RuntimeId,out var entry) || entry?.Custom?.Identity != row.Identity)
                throw new ArgumentException("Manifest does not resolve exactly.", nameof(registry));
        Owner = owner; _clock = clock; _manifest = manifest.Digest;
        _birth = _sourceFrontier = _committedSource = verifiedBirthSource; _authority = authorityFrame;
        _latestCanUse = initialCanUseWeapon;
        _permissions[verifiedBirthSource % 128] = new(verifiedBirthSource,initialCanUseWeapon);
        if (!initialCanUseWeapon) _interruption = verifiedBirthSource;
        int count = 0;
        foreach (var id in initialLoadout)
        {
            if (++count > _states.Length || id.Value is < 10 or > 73 || _states[id.Value-10] != null
                || !registry.TryResolve(id,out var entry) || entry?.Custom == null)
                throw new ArgumentException("Invalid initial custom loadout.", nameof(initialLoadout));
            _states[id.Value-10] = new(entry.Custom,owner,verifiedBirthSource);
        }
        // Invalid configuration must not consume the once-per-life grant fence.
        if (!clock.TryBindLife(owner,verifiedBirthSource,authorityFrame))
            throw new ArgumentException("Owner life already bound or source clock invalid.");
    }
    private static bool Before(uint a, uint b) => unchecked((int)(a-b)) < 0;
    private bool Time(uint authority)
    {
        if (Before(authority,_authority)) return false;
        if (authority != _authority) { _authority = authority; _offered = _drained = 0; }
        return true;
    }
    internal bool ObserveSource(WeaponOwnerContext observedOwner, uint verifiedSourceFrame, uint authorityFrame, bool canUseWeapon)
    {
        if (observedOwner != Owner || _processing || IsRetired || !Time(authorityFrame) || Before(verifiedSourceFrame,_birth)
            || !_clock.Observe(Owner,verifiedSourceFrame,authorityFrame)) return false;
        int index = (int)(verifiedSourceFrame % 128);
        if (_permissions[index] is { } prior && prior.SourceFrame == verifiedSourceFrame)
        {
            if (prior.CanUseWeapon == canUseWeapon) return true;
            Retire(WeaponCancellation.RulesChanged); return false;
        }
        if (!canUseWeapon && Before(verifiedSourceFrame,_committedSource))
        { Retire(WeaponCancellation.RulesChanged); return false; }
        if (Before(verifiedSourceFrame,_sourceFrontier) && unchecked(_sourceFrontier-verifiedSourceFrame) >= 128) return false;
        _permissions[index] = new(verifiedSourceFrame,canUseWeapon);
        if (!canUseWeapon && (!_interruption.HasValue || Before(verifiedSourceFrame,_interruption.Value))) _interruption = verifiedSourceFrame;
        if (Before(_sourceFrontier,verifiedSourceFrame))
        { _sourceFrontier = verifiedSourceFrame; _latestCanUse = canUseWeapon; }
        return true;
    }
    internal CustomQueueResult Offer(in CustomWeaponAction action, uint authorityFrame)
    {
        if (_processing) return CustomQueueResult.Busy;
        if (IsRetired) return CustomQueueResult.Retired;
        if (!Time(authorityFrame)) return CustomQueueResult.StaleAuthority;
        if (!action.IsValid || action.Owner != Owner || action.Manifest != _manifest) return CustomQueueResult.Foreign;
        // Backpressure is not an application verdict: caller retains this action
        // for later ingress. Only a terminal receipt settles/burns its sequence.
        if (++_offered > MaxOffersPerTick) return CustomQueueResult.Backpressure;
        if (action.Sequence <= _settled)
            return TryGetReceipt(action.Sequence,out var prior) && prior.Action != action ? CustomQueueResult.Conflict : CustomQueueResult.Duplicate;
        if ((ulong)action.Sequence-_settled > PendingCapacity)
        { Retire(WeaponCancellation.RulesChanged); return CustomQueueResult.WindowExceeded; }
        int index = (int)(action.Sequence % PendingCapacity);
        if (_pending[index] is { } queued)
            return queued.Action == action ? CustomQueueResult.Duplicate : CustomQueueResult.Conflict;
        _pending[index] = new(action,authorityFrame); PendingCount++; return CustomQueueResult.Queued;
    }
    internal bool TryGetReceipt(uint sequence, out CustomActionReceipt receipt)
    {
        var entry = _terminal[sequence % TerminalCapacity];
        receipt = entry.GetValueOrDefault();
        return entry.HasValue && receipt.Action.Sequence == sequence;
    }
    internal bool TryPreview(WeaponRuntimeId weapon, out WeaponStateSnapshot snapshot)
    {
        snapshot = default;
        if (weapon.Value is < 10 or > 73 || _states[weapon.Value-10] is not { } state) return false;
        if (!IsRetired && Selected == weapon && _interruption is { } interruption
            && !Before(interruption,state.Snapshot().Tick) && !Before(_sourceFrontier,interruption))
        {
            state.Preview(interruption,out snapshot);
            var phase = !_latestCanUse ? CustomWeaponPhase.Unequipped
                : snapshot.LastAcceptedShotId != 0 && Before(_sourceFrontier,snapshot.FireReadyTick) ? CustomWeaponPhase.BoltCycling
                : snapshot.Magazine == 0 ? CustomWeaponPhase.Empty : CustomWeaponPhase.Ready;
            snapshot = snapshot with { Tick = _sourceFrontier, Phase = phase, Optic = 0, ReloadCompleteTick = 0 };
        }
        else
        {
            state.Preview(_sourceFrontier,out snapshot);
            if (!IsRetired && Selected == weapon && _latestCanUse && snapshot.Phase == CustomWeaponPhase.Unequipped)
                snapshot = snapshot with { Phase = snapshot.LastAcceptedShotId != 0 && Before(_sourceFrontier,snapshot.FireReadyTick)
                    ? CustomWeaponPhase.BoltCycling : snapshot.Magazine == 0 ? CustomWeaponPhase.Empty : CustomWeaponPhase.Ready };
        }
        return true;
    }
    internal bool TryCommitted(WeaponRuntimeId weapon, out WeaponStateSnapshot snapshot)
    {
        snapshot = default;
        if (weapon.Value is < 10 or > 73 || _states[weapon.Value-10] is not { } state) return false;
        snapshot = state.Snapshot(); return true;
    }
    internal bool TryProcessNext(uint authorityFrame, ICustomSourceAdmission admission,
        ICustomWeaponEmitter emitter, out CustomActionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(admission); ArgumentNullException.ThrowIfNull(emitter);
        receipt = default;
        if (_processing || IsRetired || !Time(authorityFrame)) return false;
        foreach (var queued in _pending)
            if (queued is { } waiting && unchecked(authorityFrame-waiting.Arrived) > GapTimeout)
            { Retire(WeaponCancellation.RulesChanged); return false; }
        if (_drained >= MaxDrainPerTick || _settled == uint.MaxValue
            || _pending[(_settled+1) % PendingCapacity] is not { } next) return false;
        int terminalIndex = (int)(next.Action.Sequence % TerminalCapacity);
        if (_terminal[terminalIndex] is { } retained && unchecked(authorityFrame-retained.AuthorityFrame) <= TerminalRetention)
        { Retire(WeaponCancellation.RulesChanged); return false; }
        var action = next.Action;
        var state = _states[action.Weapon.Value-10];
        _drained++; // Waiting/denied oracle calls consume the same bounded work budget.
        _processing = true;
        try
        {
            if (state == null) receipt = Denied(action,CustomJournalDisposition.NotOwned,WeaponActionResult.Unequipped);
            else if (Before(action.SourceFrame,_birth) || Before(action.SourceFrame,_committedSource))
                receipt = Denied(action,CustomJournalDisposition.StaleSource,WeaponActionResult.StaleFrame);
            else if (Before(_sourceFrontier,action.SourceFrame)) return false;
            else if (unchecked(_sourceFrontier-action.SourceFrame) > MaxSourceAge)
                receipt = Denied(action,CustomJournalDisposition.StaleSource,WeaponActionResult.StaleFrame);
            else
            {
                if (_permissions[action.SourceFrame % 128] is not { } permission || permission.SourceFrame != action.SourceFrame) return false;
                var decision = admission.Inspect(action,out var source);
                if (decision == CustomSourceDecision.Pending) return false;
                if (IsRetired || decision != CustomSourceDecision.Verified || !source.IsValidFor(action)
                    || permission.CanUseWeapon != source.CanUseWeapon)
                    receipt = Denied(action,CustomJournalDisposition.SourceDenied,WeaponActionResult.InvalidLife);
                else if (Before(_authority,source.ObservedWorldFrame) || unchecked(_authority-source.ObservedWorldFrame) > MaxWorldAge
                    || action.Kind == CustomWeaponActionKind.Fire && (Before(_authority,source.TargetWorldFrame)
                        || unchecked(_authority-source.TargetWorldFrame) > MaxWorldAge
                        || source.TargetWorldFrame == _authority && source.TargetWorldSubFrame != 0))
                    receipt = Denied(action,CustomJournalDisposition.StaleSource,WeaponActionResult.StaleFrame);
                else receipt = Apply(action,source,state,emitter);
            }
        }
        catch (Exception)
        {
            // A dependency failure before Apply is not an emitted shot. Apply
            // handles its own uncertain emission without refunding it.
            Retire(WeaponCancellation.RulesChanged);
            receipt = Denied(action,CustomJournalDisposition.EngineFault,WeaponActionResult.InvalidLife);
        }
        finally { _processing = false; }
        _terminal[terminalIndex] = receipt;
        int pendingIndex = (int)(action.Sequence % PendingCapacity);
        if (_pending[pendingIndex].HasValue) { _pending[pendingIndex] = null; PendingCount--; }
        _settled = action.Sequence; return true;
    }
    private CustomActionReceipt Denied(CustomWeaponAction action, CustomJournalDisposition disposition, WeaponActionResult result)
        => new(action,_authority,disposition,result,CustomEmissionDisposition.NotAttempted,null,null,null);
    private CustomActionReceipt Apply(CustomWeaponAction action, CustomSourceCertificate source,
        WeaponRuntimeState state, ICustomWeaponEmitter emitter)
    {
        if (Selected.IsValid && _states[Selected.Value-10] is { } selected)
        {
            if (_interruption is { } interruption && !Before(interruption,selected.Snapshot().Tick)
                && !Before(action.SourceFrame,interruption)) selected.SetEquipped(interruption,false);
            selected.SetEquipped(action.SourceFrame,source.CanUseWeapon);
        }
        state.Advance(action.SourceFrame);
        _committedSource = action.SourceFrame;
        _interruption = null;
        foreach (var observation in _permissions)
            if (observation is { CanUseWeapon: false } restriction && Before(action.SourceFrame,restriction.SourceFrame)
                && (!_interruption.HasValue || Before(restriction.SourceFrame,_interruption.Value))) _interruption = restriction.SourceFrame;
        var before = state.Snapshot();
        var result = WeaponActionResult.Unequipped;
        var disposition = CustomJournalDisposition.ResourceDenied;
        var emission = CustomEmissionDisposition.NotAttempted;
        bool emitting = false;
        try
        {
            if (!source.CanUseWeapon) { state.SetEquipped(action.SourceFrame,false); }
            else if (action.Kind == CustomWeaponActionKind.Equip)
            {
                if (Selected.IsValid && Selected != action.Weapon) _states[Selected.Value-10]!.SetEquipped(action.SourceFrame,false);
                Selected = action.Weapon; result = state.SetEquipped(action.SourceFrame,true);
            }
            else if (action.Kind == CustomWeaponActionKind.Unequip && (!Selected.IsValid || Selected == action.Weapon))
            { result = state.SetEquipped(action.SourceFrame,false); Selected = default; }
            else if (Selected == action.Weapon)
            {
                if (action.Kind == CustomWeaponActionKind.Optic) result = state.TrySetOptic(action.SourceFrame,action.Sequence,action.Optic);
                else if (action.Kind == CustomWeaponActionKind.Reload) result = state.TryReload(action.SourceFrame,action.Sequence);
                else if (action.Kind == CustomWeaponActionKind.Fire)
                {
                    before = state.Snapshot(); // after source-time reload completion, before payment
                    if (action.Optic != before.Optic) { disposition = CustomJournalDisposition.OpticMismatch; result = WeaponActionResult.InvalidOptic; }
                    else
                    {
                        result = state.TryFire(action.SourceFrame,action.Sequence,emitter.HasCapacity(state.Definition,Owner));
                        if (result == WeaponActionResult.Accepted)
                        {
                            emitting = true;
                            emission = emitter.Emit(new(state.Definition,source,before,state.Snapshot()));
                            if (emission == CustomEmissionDisposition.NoSpawn)
                            { state.CancelUnemittedFire(action.Sequence,before); result = WeaponActionResult.CapacityUnavailable; disposition = CustomJournalDisposition.NoSpawn; }
                            else if (emission != CustomEmissionDisposition.Spawned)
                                throw new InvalidOperationException("Emitter did not return a definite disposition.");
                        }
                    }
                }
            }
            if (result == WeaponActionResult.Accepted) disposition = CustomJournalDisposition.Accepted;
        }
        catch (Exception)
        {
            // Once emission was entered it may already have resolved damage.
            // Quarantine the life; never manufacture a refund of an uncertain shot.
            emission = emitting ? CustomEmissionDisposition.Uncertain : CustomEmissionDisposition.NotAttempted;
            result = WeaponActionResult.InvalidLife; disposition = CustomJournalDisposition.EngineFault;
            Retire(WeaponCancellation.RulesChanged);
        }
        return new(action,_authority,disposition,result,emission,source,before,state.Snapshot());
    }
    internal void Retire(WeaponCancellation reason)
    {
        IsRetired = true; Selected = default;
        foreach (var state in _states) state?.Invalidate(reason);
        Array.Clear(_pending); PendingCount = 0;
    }
}
