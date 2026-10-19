using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

internal readonly record struct CustomVictimIdentity(byte Slot, ushort Generation, ushort Life)
{
    internal bool IsValid => Slot < 8 && Generation != 0 && Life != 0;
}
internal enum CustomImpactChannel : byte { Body = 1, Halfturret = 2 }
internal readonly record struct CustomPaymentKey(CustomActionIdentity Action, CustomVictimIdentity Victim,
    uint Component, CustomImpactChannel Channel)
{
    internal bool IsValid => Action.IsValid && Victim.IsValid && Component == 1
        && Channel is CustomImpactChannel.Body or CustomImpactChannel.Halfturret;
}
internal readonly record struct CustomContact(CustomPaymentKey Key, Vector3 Point, bool Headshot)
{
    internal bool IsValid => Key.IsValid && float.IsFinite(Point.X) && float.IsFinite(Point.Y)
        && float.IsFinite(Point.Z) && MathF.Abs(Point.X) <= 1_000_000 && MathF.Abs(Point.Y) <= 1_000_000
        && MathF.Abs(Point.Z) <= 1_000_000;
}
/// <summary>Actual canonical health observations, captured before turret splitting.
/// Damage amount is computed here, never supplied as a client claim.</summary>
internal readonly record struct CustomDamageResult(int BodyBefore, int BodyAfter, int TurretBefore,
    int TurretAfter, ushort DamageEventId)
{
    internal int BodyRemoved => BodyBefore-BodyAfter;
    internal int TurretRemoved => TurretBefore-TurretAfter;
    internal bool IsValid => BodyBefore is >= 0 and <= ushort.MaxValue && BodyAfter >= 0 && BodyAfter <= BodyBefore
        && TurretBefore is >= 0 and <= ushort.MaxValue && TurretAfter >= 0 && TurretAfter <= TurretBefore
        && (BodyRemoved > 0 || TurretRemoved > 0 ? DamageEventId != 0 : DamageEventId == 0);
}
internal readonly record struct CustomContactOutcome(CustomContact Contact, uint AuthorityFrame,
    CustomDamageResult Health)
{
    internal bool Lethal => Health.BodyRemoved > 0 && Health.BodyAfter == 0;
}
internal enum CustomContactDecision : byte { Reserved, Duplicate, Consumed, Busy, Refused }

/// <summary>One life-owned custom payment domain. A scene adapter must retain old
/// life stores for admitted flights on respawn, and retire them on occupant/epoch
/// replacement. No transport, source-proof, health mutation or native ledger here.
/// Single authority-thread ownership; all work and storage are bounded.</summary>
internal sealed class CustomLaunchPayments : IDisposable
{
    internal const int DefaultCapacity = 128, MaximumCapacity = 1024;
    internal const uint OutcomeRetention = 256;
    private enum Phase : byte { Empty, Emitting, Flying, ContactReserved, Terminal, NoSpawn, Faulted }
    private struct Entry
    {
        internal Phase Phase;
        internal ulong Token, ContactToken;
        internal CustomShotSource? Source;
        internal uint Began, FlightDuration, RetainedSince;
        internal bool EmitterPending;
        internal CustomContact Contact;
        internal CustomContactOutcome Outcome;
    }
    private readonly Entry[] _entries;
    private readonly WeaponDefinition?[] _definitions = new WeaponDefinition?[64];
    private readonly WeaponDigest _manifest;
    private uint _frame, _highestSequence;
    private ulong _token;
    internal WeaponOwnerContext Owner { get; }
    internal bool IsRetired { get; private set; }
    // Uncertain work closes new launch admission; independently admitted flights remain payable.
    internal bool IsQuarantined { get; private set; }
    internal int Capacity => _entries.Length;
    internal uint HighestSequence => _highestSequence;
    internal CustomLaunchPayments(WeaponOwnerContext owner, CustomWeaponManifest manifest,
        WeaponRegistry registry, uint authorityFrame, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(registry);
        if (!owner.IsValid || registry.IsDisposed || capacity is < 1 or > MaximumCapacity
            || registry.Count != 9+manifest.Rows.Count) throw new ArgumentException("Exact life/manifest and bounded storage required.");
        foreach (var row in manifest.Rows)
        {
            if (!registry.TryResolve(row.RuntimeId,out var entry) || entry?.Custom?.Identity != row.Identity)
                throw new ArgumentException("Registry must resolve the complete frozen manifest.");
            _definitions[row.RuntimeId.Value-10] = entry.Custom;
        }
        Owner = owner; _manifest = manifest.Digest; _frame = authorityFrame; _entries = new Entry[capacity];
    }
    private static bool Before(uint left, uint right) => unchecked((int)(left-right)) < 0;
    internal bool Advance(uint authorityFrame)
    {
        if (IsRetired || Before(authorityFrame,_frame)) return false;
        _frame = authorityFrame;
        for (int i=0; i<_entries.Length; i++)
        {
            ref var entry = ref _entries[i];
            if (entry.Phase == Phase.Empty) continue;
            if (entry.EmitterPending && unchecked(_frame-entry.Began) > entry.FlightDuration)
            {
                IsQuarantined = true; entry.EmitterPending = false;
                // A paid catch-up result survives a lost/uncertain emitter return.
                if (entry.Phase != Phase.Terminal) { entry.Phase = Phase.Faulted; entry.RetainedSince = _frame; }
            }
            if (entry.Phase is Phase.Emitting or Phase.Flying or Phase.ContactReserved
                && unchecked(_frame-entry.Began) > entry.FlightDuration)
            {
                // A timed-out in-progress canonical call is uncertain, never refundable.
                if (entry.Phase != Phase.Flying) IsQuarantined = true;
                entry.Phase = Phase.Faulted; entry.RetainedSince = _frame;
            }
            if (entry.Phase is Phase.Terminal or Phase.NoSpawn or Phase.Faulted
                && unchecked(_frame-entry.RetainedSince) > OutcomeRetention)
            {
                // A long-lived projectile's receipt can expire before its
                // emitter deadline. Never silently release uncertain emission.
                if (entry.EmitterPending) IsQuarantined = true;
                entry = default;
            }
        }
        return true;
    }
    internal readonly struct LaunchLease : IDisposable
    {
        private readonly CustomLaunchPayments? _store;
        internal readonly int Index;
        internal readonly ulong Token;
        internal LaunchLease(CustomLaunchPayments store, int index, ulong token)
        { _store = store; Index = index; Token = token; }
        internal bool BelongsTo(CustomLaunchPayments store) => ReferenceEquals(_store,store);
        public void Dispose() => _store?.Abandon(this);
    }
    internal readonly struct ContactLease : IDisposable
    {
        private readonly CustomLaunchPayments? _store;
        internal readonly int Index;
        internal readonly ulong LaunchToken, Token;
        internal ContactLease(CustomLaunchPayments store,int index,ulong launchToken,ulong token)
        { _store = store; Index = index; LaunchToken = launchToken; Token = token; }
        internal bool BelongsTo(CustomLaunchPayments store) => ReferenceEquals(_store,store);
        public void Dispose() => _store?.Abandon(this);
    }
    /// <summary>Capacity only; source, loadout and trade still require admission.
    /// Caller reserves this store and reliable outcome capacity before effects.</summary>
    internal bool CanReserveLaunch(uint authorityFrame)
    {
        if (!Advance(authorityFrame) || IsQuarantined || _highestSequence == uint.MaxValue || _token == ulong.MaxValue) return false;
        foreach (var entry in _entries) if (entry.Phase == Phase.Empty) return true;
        return false;
    }
    internal bool TryBeginLaunch(in CustomAcceptedLaunch launch, uint authorityFrame,
        out LaunchLease lease, out CustomShotSource? source)
    {
        lease = default; source = null;
        if (!Advance(authorityFrame) || IsQuarantined || !CustomShotSource.IsConsistentLaunch(launch)) return false;
        var action = launch.Source.Action;
        if (!action.IsValid || action.Owner != Owner || action.Manifest != _manifest || action.Sequence <= _highestSequence
            || action.Weapon.Value is < 10 or > 73 || !ReferenceEquals(_definitions[action.Weapon.Value-10],launch.Definition)
            || Before(_frame,launch.Source.ObservedWorldFrame)
            || unchecked(_frame-launch.Source.ObservedWorldFrame) > CustomSourceActionJournal.MaxWorldAge
            || launch.Source.TargetWorldFrame == 0 || Before(_frame,launch.Source.TargetWorldFrame)
            || unchecked(_frame-launch.Source.TargetWorldFrame) > CustomSourceActionJournal.MaxWorldAge
            || launch.Source.TargetWorldFrame == _frame && launch.Source.TargetWorldSubFrame != 0) return false;
        // Consistency above is not a substitute for the caller's source proof.
        _highestSequence = action.Sequence; // Permanent replay fence, even after tombstone expiry or saturation.
        int at = -1;
        for (int i=0; i<_entries.Length; i++) if (_entries[i].Phase == Phase.Empty) { at = i; break; }
        if (at < 0 || _token == ulong.MaxValue) return false;
        source = CustomShotSource.FromLaunch(launch);
        ulong token = ++_token;
        _entries[at] = new Entry { Phase = Phase.Emitting, Token = token, Source = source, Began = _frame,
            FlightDuration = (uint)launch.Definition.Mechanics.LifetimeTicks+2, RetainedSince = _frame, EmitterPending = true };
        lease = new(this,at,token); return true;
    }
    private bool Valid(in LaunchLease lease) => !IsRetired && lease.BelongsTo(this)
        && (uint)lease.Index < _entries.Length && _entries[lease.Index].Phase != Phase.Empty
        && _entries[lease.Index].Token == lease.Token;
    private bool Valid(in ContactLease lease) => !IsRetired && lease.BelongsTo(this)
        && (uint)lease.Index < _entries.Length && _entries[lease.Index].Phase == Phase.ContactReserved
        && _entries[lease.Index].Token == lease.LaunchToken && _entries[lease.Index].ContactToken == lease.Token;
    internal bool CommitEmitted(in LaunchLease lease)
    {
        if (!Valid(lease)) return false;
        ref var entry = ref _entries[lease.Index];
        if (!entry.EmitterPending || entry.Phase is Phase.NoSpawn or Phase.Faulted) return false;
        entry.EmitterPending = false;
        if (entry.Phase == Phase.Emitting) entry.Phase = Phase.Flying;
        return true; // An already reserved/terminal catch-up contact stays consumed.
    }
    internal bool CompleteNoSpawn(in LaunchLease lease)
    {
        if (!Valid(lease)) return false;
        ref var entry = ref _entries[lease.Index];
        if (!entry.EmitterPending || entry.Phase != Phase.Emitting) return false;
        entry.EmitterPending = false; entry.Phase = Phase.NoSpawn; entry.RetainedSince = _frame; return true;
    }
    private void Abandon(in LaunchLease lease)
    {
        if (!Valid(lease) || !_entries[lease.Index].EmitterPending) return;
        ref var entry = ref _entries[lease.Index];
        IsQuarantined = true; entry.EmitterPending = false;
        // Never erase an outcome if the emitter throws after catch-up paid.
        if (entry.Phase != Phase.Terminal) { entry.Phase = Phase.Faulted; entry.RetainedSince = _frame; }
    }
    private void Abandon(in ContactLease lease)
    {
        if (!Valid(lease)) return;
        ref var entry = ref _entries[lease.Index];
        IsQuarantined = true; entry.Phase = Phase.Faulted; entry.RetainedSince = _frame;
    }
    internal CustomContactDecision TryBeginContact(CustomShotSource source, in CustomContact contact,
        uint authorityFrame, out ContactLease lease, out CustomContactOutcome prior)
    {
        lease = default; prior = default;
        if (!Advance(authorityFrame) || source == null || !contact.IsValid || contact.Key.Action.Owner != Owner
            || source.Action != contact.Key.Action) return CustomContactDecision.Refused;
        for (int i=0; i<_entries.Length; i++)
        {
            ref var entry = ref _entries[i];
            if (!ReferenceEquals(entry.Source,source)) continue;
            if (entry.Phase == Phase.Terminal)
            {
                if (entry.Outcome.Contact.Key != contact.Key) return CustomContactDecision.Consumed;
                prior = entry.Outcome; return CustomContactDecision.Duplicate;
            }
            if (entry.Phase == Phase.ContactReserved) return CustomContactDecision.Busy;
            if (entry.Phase is not (Phase.Emitting or Phase.Flying)) return CustomContactDecision.Consumed;
            if (_token == ulong.MaxValue) return CustomContactDecision.Refused;
            entry.Contact = contact; entry.ContactToken = ++_token; entry.Phase = Phase.ContactReserved;
            lease = new(this,i,entry.Token,entry.ContactToken); return CustomContactDecision.Reserved;
        }
        return CustomContactDecision.Refused;
    }
    internal bool CompleteContact(in ContactLease lease, in CustomDamageResult health, out CustomContactOutcome outcome)
    {
        outcome = default;
        if (!Valid(lease)) return false;
        if (!health.IsValid) { Abandon(lease); return false; }
        ref var entry = ref _entries[lease.Index];
        outcome = new(entry.Contact,_frame,health); entry.Outcome = outcome;
        entry.Phase = Phase.Terminal; entry.RetainedSince = _frame; return true;
    }
    internal bool TryGetOutcome(in CustomPaymentKey key, out CustomContactOutcome outcome)
    {
        outcome = default;
        if (IsRetired || !key.IsValid || key.Action.Owner != Owner || key.Action.Manifest != _manifest) return false;
        foreach (var entry in _entries)
            if (entry.Phase == Phase.Terminal && entry.Outcome.Contact.Key == key)
            { outcome = entry.Outcome; return true; }
        return false;
    }
    internal void RetireOccupant()
    { IsRetired = true; Array.Clear(_entries); }
    public void Dispose() => RetireOccupant();
}
