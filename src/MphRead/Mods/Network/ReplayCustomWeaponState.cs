using System;
using System.IO;
using System.Security.Cryptography;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

// Immutable detached restore point, not a native/world byte checkpoint. The
// private snapshot is never lent to a caller; every restore creates a new owner.
internal sealed class ReplayCustomWeaponRestorePoint
{
    private readonly ReplayCustomWeaponState _snapshot;
    internal ReplayCustomWeaponRestorePoint(ReplayCustomWeaponState state) => _snapshot = state.Copy();
    internal ReplayCustomWeaponState Restore(ReplayCustomWeaponContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Digest != _snapshot.Context.Digest) throw new InvalidDataException("Different custom replay restore context.");
        return _snapshot.Copy(context);
    }
}

// Passive values only: no Scene, NetSession, RNG, input, collision, admission,
// inventory funding, sound, damage or presentation device is reachable here.
internal sealed partial class ReplayCustomWeaponState
{
    internal const int FlightCapacity = 256, ActionCapacity = 1024, SeenCapacity = 1024;
    private struct FlightAnchor
    {
        internal bool Used;
        internal uint World;
        internal CustomReplicaFlight Flight;
    }
    private struct ActionEntry
    {
        internal bool Used;
        internal CustomActionIdentity Identity;
        internal ReplayCustomWeaponFact? Launch, Contact;
        internal CustomReplicaLaunch? BaselineLaunch;
        internal uint LaunchFrame, ContactFrame, TouchedWorld;
    }
    internal ReplayCustomWeaponContext Context { get; }
    internal uint RecordingFrame { get; private set; }
    internal uint World { get; private set; }
    internal ulong Sequence { get; private set; }
    internal bool Invalidated { get; private set; }
    internal bool HasBaseline { get; private set; }
    internal int KnownPickups { get; private set; }
    internal bool CompleteForRestore => !Invalidated && HasBaseline && KnownPickups == Context.Layout.Count;
    private bool _bootstrap;
    private uint _stateWorld;
    private readonly CustomReplicaSlot[] _slots = new CustomReplicaSlot[8], _scratchSlots = new CustomReplicaSlot[8];
    private readonly CustomReplicaFlight[] _scratchFlights = new CustomReplicaFlight[64];
    private readonly FlightAnchor[] _flights = new FlightAnchor[FlightCapacity];
    private readonly ActionEntry[] _actions = new ActionEntry[ActionCapacity];
    private readonly CustomPickupItemState?[] _pickups = new CustomPickupItemState?[64];
    private readonly uint[] _pickupWorlds = new uint[64];
    private readonly ulong[] _seen = new ulong[SeenCapacity];
    private readonly WeaponDigest[] _digests = new WeaponDigest[SeenCapacity];
    internal ReplayCustomWeaponState(ReplayCustomWeaponContext context)
    { ArgumentNullException.ThrowIfNull(context); Context = context; World = context.Bootstrap.AuthorityFrame; }
    private static bool Before(uint a, uint b) => unchecked((int)(a - b)) < 0;
    internal ReplayCustomWeaponState Copy(ReplayCustomWeaponContext? replacement = null)
    {
        if (!CompleteForRestore) throw new InvalidDataException("Custom replay restore point requires a complete healthy baseline and pickup table.");
        if (replacement != null && replacement.Digest != Context.Digest) throw new InvalidDataException("Different custom replay copy context.");
        var copy = new ReplayCustomWeaponState(replacement ?? Context)
        {
            RecordingFrame = RecordingFrame, World = World, Sequence = Sequence, Invalidated = Invalidated,
            HasBaseline = HasBaseline, KnownPickups = KnownPickups, _bootstrap = _bootstrap, _stateWorld = _stateWorld
        };
        _slots.CopyTo(copy._slots, 0); _flights.CopyTo(copy._flights, 0); _actions.CopyTo(copy._actions, 0);
        _pickups.CopyTo(copy._pickups, 0); _pickupWorlds.CopyTo(copy._pickupWorlds, 0);
        _seen.CopyTo(copy._seen, 0); _digests.CopyTo(copy._digests, 0); return copy;
    }
    internal ReplayCustomWeaponRestorePoint CaptureRestorePoint() => new(this);
    internal bool Accept(ReplayCustomWeaponFact fact, uint recordingFrame)
    {
        if (Invalidated) throw new InvalidDataException("Custom replay state is closed.");
        try
        {
            if (fact.Context != Context.Digest || recordingFrame < RecordingFrame)
                throw new InvalidDataException("Different custom replay scope or backward recording frame.");
            var digest = WeaponDigest.Read(SHA256.HashData(fact.CopyBytes())); int index = (int)(fact.Sequence % SeenCapacity);
            if (fact.Sequence <= Sequence)
            {
                if (_seen[index] == fact.Sequence && _digests[index] == digest) return false;
                throw new InvalidDataException("Conflicting or out-of-window custom replay retry.");
            }
            if (Sequence == ulong.MaxValue || fact.Sequence != Sequence + 1)
                throw new InvalidDataException("Missing or reordered custom replay fact.");
            if (fact.Kind == ReplayCustomWeaponFactKind.Clock && (!HasBaseline || !Before(World, fact.World)))
                throw new InvalidDataException("Custom replay clock requires a strictly newer recorded authority tick.");
            Advance(fact.World, recordingFrame);
            if (!_bootstrap)
            {
                if (fact.Kind != ReplayCustomWeaponFactKind.Bootstrap || fact.Sequence != 1)
                    throw new InvalidDataException("Custom replay has no exact bootstrap.");
                _bootstrap = true;
            }
            else if (!HasBaseline)
            {
                if (fact.Kind != ReplayCustomWeaponFactKind.Baseline || fact.Sequence != 2
                    || !fact.CopyTable(Context, _scratchSlots, _scratchFlights, out var capture))
                    throw new InvalidDataException("Custom replay has no complete initial baseline.");
                _scratchSlots.CopyTo(_slots, 0); _stateWorld = fact.World;
                for (int i = 0; i < capture.FlightCount; i++)
                {
                    var flight = _scratchFlights[i];
                    int action = Action(flight.Action, fact.World);
                    _actions[action].BaselineLaunch = new(flight.Certificate, flight.LaunchWorld, flight.SpawnPosition, flight.Direction);
                    AddFlight(flight, fact.World);
                }
                HasBaseline = true;
            }
            else Apply(fact, recordingFrame);
            _seen[index] = fact.Sequence; _digests[index] = digest;
            Sequence = fact.Sequence; return true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        { Invalidated = true; throw; }
        finally { Array.Clear(_scratchSlots); Array.Clear(_scratchFlights); }
    }
    internal void Advance(uint world, uint recordingFrame)
    {
        if (Invalidated || recordingFrame < RecordingFrame) throw new InvalidDataException("Custom replay clocks cannot move backward.");
        RecordingFrame = recordingFrame;
        if (!Before(world, World)) World = world;
        for (int i = 0; i < _flights.Length; i++)
            if (_flights[i].Used && !FlightAt(_flights[i], out _)) _flights[i] = default;
    }
    private bool FlightAt(in FlightAnchor anchor, out CustomReplicaFlight flight)
    {
        flight = default; if (!anchor.Used || Before(World, anchor.World)) return false;
        uint ticks = World - anchor.World; float seconds = ticks / 60f;
        if (anchor.Flight.RemainingSeconds <= seconds) return false;
        // Velocity is distance PER FIXED TICK, not per second. Preserve the
        // original anchor through restores to avoid accumulating seek rounding.
        flight = anchor.Flight with
        {
            Position = anchor.Flight.Position + anchor.Flight.Velocity * ticks,
            AgeSeconds = anchor.Flight.AgeSeconds + seconds,
            RemainingSeconds = anchor.Flight.RemainingSeconds - seconds
        };
        return true;
    }
    private int FindFlight(CustomActionIdentity identity)
    { for (int i = 0; i < _flights.Length; i++) if (_flights[i].Used && _flights[i].Flight.Action == identity) return i; return -1; }
    private void AddFlight(in CustomReplicaFlight flight, uint world)
    {
        var anchor = new FlightAnchor { Used = true, World = world, Flight = flight };
        if (!FlightAt(anchor, out _)) return;
        int existing = FindFlight(flight.Action);
        if (existing >= 0)
        {
            var old = _flights[existing].Flight;
            if (old.Certificate != flight.Certificate || old.LaunchWorld != flight.LaunchWorld || old.SpawnPosition != flight.SpawnPosition
                || old.Direction != flight.Direction || old.Velocity != flight.Velocity)
                throw new InvalidDataException("Contradictory custom replay flight.");
            return;
        }
        for (int i = 0; i < _flights.Length; i++) if (!_flights[i].Used) { _flights[i] = anchor; return; }
        throw new InvalidDataException("Custom replay flight budget exceeded.");
    }
    private int Action(CustomActionIdentity identity, uint world)
    {
        for (int i = 0; i < _actions.Length; i++) if (_actions[i].Used && _actions[i].Identity == identity) return i;
        for (int i = 0; i < _actions.Length; i++) if (!_actions[i].Used)
        { _actions[i] = new() { Used = true, Identity = identity, TouchedWorld = world }; return i; }
        uint retention = (uint)Context.Definition.Mechanics.LifetimeTicks + 2;
        for (int i = 0; i < _actions.Length; i++)
            if (FindFlight(_actions[i].Identity) < 0 && !Before(World, _actions[i].TouchedWorld)
                && World - _actions[i].TouchedWorld > retention)
            { _actions[i] = new() { Used = true, Identity = identity, TouchedWorld = world }; return i; }
        throw new InvalidDataException("Custom replay action history budget exceeded.");
    }
    private void Apply(ReplayCustomWeaponFact fact, uint frame)
    {
        switch (fact.Kind)
        {
            case ReplayCustomWeaponFactKind.Clock:
                break; // Advance consumed the recorded clock; no mechanics or ownership simulation.
            case ReplayCustomWeaponFactKind.State:
                if (Before(fact.World, _stateWorld) || !fact.CopyTable(Context, _scratchSlots, _scratchFlights, out _))
                    throw new InvalidDataException("Custom replay state table regressed.");
                _scratchSlots.CopyTo(_slots, 0); _stateWorld = fact.World;
                for (int i = 0; i < _flights.Length; i++)
                    if (_flights[i].Used)
                    {
                        var owner = _flights[i].Flight.Action.Owner; var slot = _slots[owner.Slot];
                        if (!slot.Occupied || slot.Spectating || slot.Generation != owner.Generation) _flights[i] = default;
                    }
                break;
            case ReplayCustomWeaponFactKind.Launch:
                if (!fact.TryLaunch(Context, out var launch)) throw new InvalidDataException("Malformed custom replay launch.");
                int launched = Action(launch.Identity, fact.World);
                if (_actions[launched].BaselineLaunch is { } initial && initial != launch)
                    throw new InvalidDataException("Accepted custom launch contradicts its initial flight.");
                if (_actions[launched].Launch is { } oldLaunch)
                {
                    oldLaunch.TryLaunch(Context, out var previous);
                    if (previous != launch) throw new InvalidDataException("Contradictory accepted custom launch.");
                    break;
                }
                _actions[launched].Launch = fact; _actions[launched].LaunchFrame = frame;
                if (_actions[launched].Contact is { } priorContact)
                {
                    priorContact.TryContact(Context, out var contactSource, out _);
                    if (contactSource != launch.Source.Action.Bootstrap)
                        throw new InvalidDataException("Accepted custom contact contradicts its launch source.");
                    break; // Contact-before-launch cannot resurrect a flight.
                }
                var actor = _slots[launch.Identity.Owner.Slot];
                if (!actor.Occupied || actor.Spectating || actor.Generation != launch.Identity.Owner.Generation) break;
                float duration = Context.Definition.Mechanics.LifetimeTicks / 60f;
                var velocity = launch.Direction * (Context.Definition.Mechanics.ProjectileSpeed4096 / 4096f);
                AddFlight(new(launch.Identity, launch.Source, launch.World,
                    unchecked(launch.World + (uint)Context.Definition.Mechanics.LifetimeTicks + 2),
                    launch.Origin, launch.Origin, velocity, launch.Direction, 0, duration), launch.World);
                break;
            case ReplayCustomWeaponFactKind.Contact:
                if (!fact.TryContact(Context, out var contactBootstrap, out var contact)) throw new InvalidDataException("Malformed custom replay contact.");
                int terminal = Action(contact.Contact.Key.Action, fact.World);
                CustomReplicaLaunch? knownLaunch = _actions[terminal].BaselineLaunch;
                if (_actions[terminal].Launch is { } launchFact && launchFact.TryLaunch(Context, out var acceptedLaunch)) knownLaunch = acceptedLaunch;
                if (knownLaunch is { } known && known.Source.Action.Bootstrap != contactBootstrap)
                    throw new InvalidDataException("Accepted custom contact contradicts its launch source.");
                if (_actions[terminal].Contact is { } oldContact)
                {
                    oldContact.TryContact(Context, out var previousSource, out var previous);
                    fact.TryContact(Context, out var source, out _);
                    if (previous != contact || previousSource != source) throw new InvalidDataException("Contradictory accepted custom contact.");
                    break;
                }
                _actions[terminal].Contact = fact; _actions[terminal].ContactFrame = frame;
                int flight = FindFlight(contact.Contact.Key.Action); if (flight >= 0) _flights[flight] = default;
                break;
            case ReplayCustomWeaponFactKind.Pickup:
                if (!fact.TryPickup(Context, out int index, out var item)) throw new InvalidDataException("Malformed custom replay pickup.");
                if (_pickups[index] is { } previousItem)
                {
                    if (Before(fact.World, _pickupWorlds[index]) || item.Key.Incarnation < previousItem.Key.Incarnation || item.Revision < previousItem.Revision
                        || (item.Key.Incarnation == previousItem.Key.Incarnation
                            ? item.Revision != previousItem.Revision || item.ReadyAt != previousItem.ReadyAt || previousItem.Available && !item.Available
                            : item.Revision <= previousItem.Revision)) throw new InvalidDataException("Custom replay pickup incarnation regressed.");
                }
                else KnownPickups++;
                _pickups[index] = item; _pickupWorlds[index] = fact.World;
                break;
            default: throw new InvalidDataException("Unexpected custom replay bootstrap/baseline replacement.");
        }
    }
    internal bool TrySlot(int index, out CustomReplicaSlot slot)
    { slot = default; if (Invalidated || !HasBaseline || (uint)index >= 8) return false; slot = _slots[index]; return true; }
    internal bool TryPickup(int index, out CustomPickupItemState item)
    { item = default; if (Invalidated || !HasBaseline || (uint)index >= Context.Layout.Count || _pickups[index] is not { } value) return false; item = value; return true; }
    internal bool TryContact(CustomActionIdentity identity, out CustomContactOutcome outcome, out uint recordingFrame)
    {
        outcome = default; recordingFrame = 0; if (Invalidated || !HasBaseline) return false;
        foreach (var action in _actions) if (action.Used && action.Identity == identity && action.Contact is { } fact)
        { recordingFrame = action.ContactFrame; return fact.TryContact(Context, out _, out outcome); }
        return false;
    }
    internal int CopyFlights(Span<CustomReplicaFlight> flights)
    {
        if (Invalidated || !HasBaseline) throw new InvalidDataException("Custom replay state is not ready.");
        if (flights.Length < FlightCapacity) throw new ArgumentException("Complete bounded flight buffer required.");
        int count = 0;
        foreach (var anchor in _flights) if (FlightAt(anchor, out var flight)) flights[count++] = flight;
        // Stable source ordering independent of arrival, allocation or restore order.
        for (int i = 1; i < count; i++)
        {
            var value = flights[i]; int j = i - 1;
            while (j >= 0 && Compare(flights[j].Action, value.Action) > 0) { flights[j + 1] = flights[j]; j--; }
            flights[j + 1] = value;
        }
        return count;
    }
    private static int Compare(CustomActionIdentity a, CustomActionIdentity b)
    {
        int result = a.Owner.Slot.CompareTo(b.Owner.Slot); if (result != 0) return result;
        result = a.Owner.Generation.CompareTo(b.Owner.Generation); if (result != 0) return result;
        result = a.Owner.Life.CompareTo(b.Owner.Life); return result != 0 ? result : a.Sequence.CompareTo(b.Sequence);
    }
}
