using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

// One custom owner identity, captured from the current actor at the actual item
// phase. Native ShotKey globals are deliberately absent: offline native keys do
// not identify the scene-owned custom life. The funding adapter rechecks it.
internal readonly record struct CustomPickupContact(WeaponOwnerContext Owner, uint SourceFrame, uint AuthorityFrame,
    Vector3 Position, Vector3 SpherePosition, float Radius, float MinHeight, float MaxHeight,
    bool Alt, bool Alive, bool CanPickup)
{
    private static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y)
        && float.IsFinite(point.Z) && MathF.Abs(point.X) <= 8192 && MathF.Abs(point.Y) <= 8192 && MathF.Abs(point.Z) <= 8192;
    internal bool IsValid => Owner.IsValid && Finite(Position) && Finite(SpherePosition)
        && float.IsFinite(Radius) && Radius > 0 && Radius <= 8
        && float.IsFinite(MinHeight) && float.IsFinite(MaxHeight)
        && MinHeight >= -128 && MaxHeight <= 128 && MinHeight <= MaxHeight;
    internal bool Reaches(Vector3 item)
    {
        var delta = item-(Alt ? SpherePosition : Position); float radius = Radius+.45f;
        return Alt ? delta.LengthSquared < radius*radius
            : delta.X*delta.X+delta.Z*delta.Z < radius*radius && delta.Y >= MinHeight && delta.Y <= MaxHeight;
    }
}
internal readonly record struct CustomPickupFundingResult(WeaponStateSnapshot State, ulong Revision);
internal readonly record struct CustomPickupItemState(CustomPickupItemKey Key, uint ReadyAt, bool Available, ulong Revision);
internal readonly record struct CustomPickupOutcome(ulong Revision, CustomPickupItemKey Consumed,
    WeaponOwnerContext Owner, uint SourceFrame, uint AuthorityFrame,
    CustomPickupItemState Next, CustomPickupFundingResult Funding);
internal enum CustomPickupCollectResult : byte
{ Collected, Invalid, Unavailable, Backpressure, FundingRefused, Busy, Retired, Fault }

// Engine-only callback. A false result MUST mean no resource/ownership mutation.
// The adapter validates the exact current actor/life and observed source before
// calling its journal. No wire packet may implement or bypass this interface.
internal interface ICustomPickupFunding
{
    bool TryFund(CustomPickupItemKey item, in CustomPickupContact contact, WeaponRuntimeId weapon,
        int reserveRefill, out CustomPickupFundingResult funded);
}

/// <summary>Scene-owned, single-threaded world arbitration. One incarnation can
/// fund one actor. A bounded immutable publication slot is reserved before the
/// funding callback; transport must retain it until reliable publication succeeds.
/// Initial delay and respawn are simulation ticks, independent of render rate.</summary>
internal sealed class CustomPickupLedger
{
    internal const int OutcomeCapacity = 128;
    private struct Item
    {
        internal ulong Incarnation, Revision;
        internal uint ReadyAt;
    }
    internal CustomPickupLayout Layout { get; }
    internal CustomPickupWorld World { get; }
    private readonly Item[] _items;
    private readonly CustomPickupOutcome[] _outcomes = new CustomPickupOutcome[OutcomeCapacity];
    private uint _now;
    private int _head, _count;
    private bool _processing;
    internal bool IsRetired { get; private set; }
    internal ulong Revision { get; private set; }
    internal uint AuthorityFrame => _now;
    internal int PendingOutcomes => _count;
    internal CustomPickupLedger(CustomPickupLayout layout, CustomPickupWorld world, uint birth)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!world.IsValid || world.Layout != layout.Digest || world.Manifest != layout.Manifest)
            throw new ArgumentException("Frozen pickup world and layout must match.",nameof(world));
        Layout = layout; World = world; _now = birth; _items = new Item[layout.Count];
        for (int i = 0; i < _items.Length; i++)
            _items[i] = new() { Incarnation = 1, ReadyAt = unchecked(birth+layout.Placements[i].InitialDelayTicks) };
    }
    private static bool Before(uint a, uint b) => unchecked((int)(a-b)) < 0;
    internal bool Advance(uint authorityFrame)
    {
        if (IsRetired || _processing || Before(authorityFrame,_now)) return false;
        _now = authorityFrame; return true;
    }
    internal bool TryState(int index, out CustomPickupItemState state)
    {
        state = default;
        if (_processing || (uint)index >= _items.Length) return false;
        var item = _items[index];
        state = new(new(World,Layout.Placements[index].SpawnId,item.Incarnation),item.ReadyAt,
            !IsRetired && !Before(_now,item.ReadyAt),item.Revision);
        return true;
    }
    internal CustomPickupCollectResult TryCollect(CustomPickupItemKey key, in CustomPickupContact contact,
        ICustomPickupFunding funding, out CustomPickupOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(funding); outcome = default;
        if (_processing) return CustomPickupCollectResult.Busy;
        if (IsRetired) return CustomPickupCollectResult.Retired;
        var owner = contact.Owner;
        if (!key.IsValid || key.World != World || !owner.IsValid || owner.MatchId != World.Start.MatchId
            || owner.AuthorityEpoch != World.Start.AuthorityEpoch || !contact.IsValid
            || !contact.Alive || !contact.CanPickup || contact.AuthorityFrame != _now) return CustomPickupCollectResult.Invalid;
        int index = -1;
        for (int i = 0; i < _items.Length; i++)
            if (Layout.Placements[i].SpawnId == key.SpawnId) { index = i; break; }
        if (index < 0) return CustomPickupCollectResult.Invalid;
        var item = _items[index]; var placement = Layout.Placements[index];
        if (item.Incarnation != key.Incarnation || Before(_now,item.ReadyAt)) return CustomPickupCollectResult.Unavailable;
        if (!contact.Reaches(placement.Position)) return CustomPickupCollectResult.Invalid;
        if (_count == OutcomeCapacity || Revision == ulong.MaxValue || item.Incarnation == ulong.MaxValue)
            return CustomPickupCollectResult.Backpressure;
        _processing = true;
        try
        {
            if (!funding.TryFund(key,contact,placement.Weapon,placement.ReserveRefill,out var funded))
                return CustomPickupCollectResult.FundingRefused;
            var state = funded.State; var definition = Layout.Definition(index);
            // A broken trusted adapter is uncertain. Quarantine rather than
            // refunding/reoffering a potentially already-funded incarnation.
            if (funded.Revision == 0 || state.Owner != owner || state.Weapon != definition.Identity
                || state.Tick != unchecked(contact.SourceFrame+1)
                || state.Phase is < CustomWeaponPhase.Unequipped or > CustomWeaponPhase.Reloading
                || state.Magazine < 0 || state.Magazine > definition.Mechanics.MagazineCapacity
                || state.Reserve < 0 || state.Reserve > definition.Mechanics.MaximumReserve
                || state.Optic < 0 || state.Optic > definition.Mechanics.OpticMagnificationsMilli.Count)
            { IsRetired = true; return CustomPickupCollectResult.Fault; }
            ulong revision = Revision+1;
            uint readyAt = unchecked(_now+placement.RespawnTicks);
            var next = new CustomPickupItemState(new(World,key.SpawnId,item.Incarnation+1),readyAt,false,revision);
            outcome = new(revision,key,owner,contact.SourceFrame,_now,next,funded);
            _outcomes[(_head+_count)%OutcomeCapacity] = outcome; _count++;
            _items[index] = new() { Incarnation = next.Key.Incarnation, ReadyAt = readyAt, Revision = revision };
            Revision = revision;
            return CustomPickupCollectResult.Collected;
        }
        catch (Exception)
        { IsRetired = true; outcome = default; return CustomPickupCollectResult.Fault; }
        finally { _processing = false; }
    }
    internal bool TryPeekOutcome(out CustomPickupOutcome outcome)
    {
        outcome = _count == 0 ? default : _outcomes[_head]; return _count != 0;
    }
    // Owner-only acknowledgement after reliable publication (or synchronous
    // offline consumption). Never accept a client's claimed publication ACK here.
    internal bool AcknowledgePublication(ulong revision)
    {
        if (_processing || _count == 0 || _outcomes[_head].Revision != revision) return false;
        _outcomes[_head] = default; _head = (_head+1)%OutcomeCapacity; _count--; return true;
    }
    internal void Retire() => IsRetired = true;
}
