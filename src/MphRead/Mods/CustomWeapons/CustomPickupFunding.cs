using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

// World identity excludes recipient bootstrap revision/connection identity.
// The scene must freeze the expected layout before creating any owner journal.
internal readonly record struct CustomPickupWorld(MatchStartIdentity Start, WeaponDigest Manifest, WeaponDigest Layout)
{
    internal bool IsValid => Start.MatchId != 0 && Start.AuthorityEpoch != 0 && Start.StartGeneration != 0
        && Manifest.IsValid && Layout.IsValid;
}
internal readonly record struct CustomPickupItemKey(CustomPickupWorld World, ushort SpawnId, ulong Incarnation)
{
    internal bool IsValid => World.IsValid && SpawnId != 0 && Incarnation != 0;
}

internal sealed partial class CustomSourceActionJournal
{
    internal const int PickupFundingCapacity = 128, PickupSpawnCapacity = 64;
    private readonly record struct PickupFunding(WeaponRuntimeId Weapon, uint Source, int Amount);
    private sealed class PickupFunds
    {
        internal readonly uint[] Acquired = new uint[CustomWeaponManifest.MaxEntries];
        internal readonly ushort[] SpawnIds = new ushort[PickupSpawnCapacity];
        internal readonly ulong[] Incarnations = new ulong[PickupSpawnCapacity];
        internal readonly PickupFunding[] Pending = new PickupFunding[PickupFundingCapacity];
        internal int Spawns, Count;
        internal PickupFunds(uint birth) => Array.Fill(Acquired,birth);
    }
    private readonly CustomPickupWorld? _pickupWorld;
    private readonly WeaponRegistry _registry;
    private PickupFunds? _pickupFunds;
    internal ulong PickupRevision { get; private set; }
    internal int PendingPickupFundingCount => _pickupFunds?.Count ?? 0;

    /// <summary>Trusted scene contact only, after reserving world outcome delivery.
    /// This is not a client request or a control action. Contact grants ownership
    /// at the next source tick, preserving already released native selection.
    /// Repeated pickups add capped reserve in source order without resetting the
    /// magazine, cadence, reload, selection, action sequence or source clock.</summary>
    internal bool TryFundPickup(CustomPickupItemKey item, WeaponOwnerContext owner, WeaponRuntimeId weapon,
        uint verifiedContactSource, uint authorityFrame, int reserveRefill, out WeaponStateSnapshot funded)
    {
        funded = default;
        if (_processing || IsRetired || _registry.IsDisposed || owner != Owner || !item.IsValid
            || _pickupWorld is not { } world || item.World != world || authorityFrame != _authority
            || verifiedContactSource != _sourceFrontier || Before(verifiedContactSource,_committedSource)
            || weapon.Value is < 10 or > 73 || PickupRevision == ulong.MaxValue
            || !_registry.TryResolve(weapon,out var entry) || entry?.Custom is not { } definition
            || reserveRefill <= 0 || reserveRefill > definition.Mechanics.MaximumReserve) return false;
        var funds = _pickupFunds;
        int spawn = -1;
        if (funds != null)
            for (int i = 0; i < funds.Spawns; i++)
                if (funds.SpawnIds[i] == item.SpawnId) { spawn = i; break; }
        if (spawn >= 0 && item.Incarnation <= funds!.Incarnations[spawn]
            || spawn < 0 && funds?.Spawns == PickupSpawnCapacity) return false;
        bool first = _states[weapon.Value-10] == null;
        if (!first && funds?.Count == PickupFundingCapacity) return false;
        uint effectiveSource = unchecked(verifiedContactSource+1);
        // Allocate only after validation; ordinary lives retain their old cost.
        funds ??= new PickupFunds(_birth);
        WeaponRuntimeState? acquired = first ? new(definition,Owner,effectiveSource) : null;
        _pickupFunds = funds;
        if (spawn < 0) { spawn = funds.Spawns++; funds.SpawnIds[spawn] = item.SpawnId; }
        funds.Incarnations[spawn] = item.Incarnation;
        if (first)
        {
            funds.Acquired[weapon.Value-10] = effectiveSource;
            _states[weapon.Value-10] = acquired;
        }
        else funds.Pending[funds.Count++] = new(weapon,effectiveSource,reserveRefill);
        PickupRevision++;
        funded = ProjectPickupState(weapon,_states[weapon.Value-10]!,effectiveSource);
        return true;
    }

    private bool OwnedAt(WeaponRuntimeId weapon, uint source)
        => _states[weapon.Value-10] != null && (_pickupFunds == null || !Before(source,_pickupFunds.Acquired[weapon.Value-10]));

    private WeaponStateSnapshot ProjectPickupState(WeaponRuntimeId weapon, WeaponRuntimeState state, uint source)
    {
        var value = state.Snapshot();
        uint? interruption = !IsRetired && Selected == weapon && _interruption is { } tick
            && !Before(tick,value.Tick) && !Before(source,tick) ? tick : null;
        if (_pickupFunds is { } funds)
            for (int i = 0; i < funds.Count; i++)
            {
                var fund = funds.Pending[i];
                if (fund.Weapon != weapon || Before(source,fund.Source)) continue;
                if (interruption is { } stop && !Before(fund.Source,stop))
                { value = CancelProjectedReload(value,state.Definition.Mechanics,stop); interruption = null; }
                value = WeaponRuntimeState.Project(value,state.Definition.Mechanics,fund.Source);
                value = value with { Reserve = Math.Min(state.Definition.Mechanics.MaximumReserve,value.Reserve+fund.Amount) };
            }
        if (interruption is { } remaining) value = CancelProjectedReload(value,state.Definition.Mechanics,remaining);
        value = WeaponRuntimeState.Project(value,state.Definition.Mechanics,source);
        if (!IsRetired && Selected == weapon && _latestCanUse && value.Phase == CustomWeaponPhase.Unequipped)
            value = value with { Phase = value.LastAcceptedShotId != 0 && Before(source,value.FireReadyTick)
                ? CustomWeaponPhase.BoltCycling : value.Magazine == 0 ? CustomWeaponPhase.Empty : CustomWeaponPhase.Ready };
        return value;
    }
    private static WeaponStateSnapshot CancelProjectedReload(WeaponStateSnapshot value, WeaponMechanics mechanics, uint source)
        => WeaponRuntimeState.Project(value,mechanics,source) with
        { Phase = CustomWeaponPhase.Unequipped, Optic = 0, ReloadCompleteTick = 0 };

    private void CommitPickupFunding(uint source)
    {
        if (_pickupFunds is not { } funds) return;
        // Apply interruption before a later grant, so advancing to that grant
        // cannot complete a reload the engine had already cancelled.
        for (int slot = 0; slot < _states.Length; slot++)
        {
            if (_states[slot] is not { } state || Before(source,funds.Acquired[slot])) continue;
            uint? interruption = Selected.Value == slot+10 && _interruption is { } tick
                && !Before(tick,state.Snapshot().Tick) && !Before(source,tick) ? tick : null;
            for (int i = 0; i < funds.Count; i++)
            {
                var fund = funds.Pending[i];
                if (fund.Weapon.Value != slot+10 || Before(source,fund.Source)) continue;
                if (interruption is { } stop && !Before(fund.Source,stop))
                { state.SetEquipped(stop,false); interruption = null; }
                state.AddPickupReserve(fund.Source,fund.Amount);
            }
            if (interruption is { } remaining) state.SetEquipped(remaining,false);
        }
        int kept = 0;
        for (int i = 0; i < funds.Count; i++)
            if (Before(source,funds.Pending[i].Source)) funds.Pending[kept++] = funds.Pending[i];
        Array.Clear(funds.Pending,kept,funds.Count-kept); funds.Count = kept;
    }
}
