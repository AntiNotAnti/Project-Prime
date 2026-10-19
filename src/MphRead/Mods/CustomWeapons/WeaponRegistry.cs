using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.CustomWeapons;

/// <summary>Immutable catalog entry bound to an exact validated mechanics digest.</summary>
public sealed record WeaponDefinition
{
    public WeaponIdentity Identity { get; }
    public string Name { get; }
    public WeaponMechanics Mechanics { get; }
    public static WeaponDefinition Create(Guid contentId, string name, WeaponMechanics mechanics)
    {
        ArgumentNullException.ThrowIfNull(mechanics);
        return new(WeaponIdentity.Custom(contentId, mechanics.DefinitionHash), name, mechanics);
    }
    public WeaponDefinition(WeaponIdentity identity, string name, WeaponMechanics mechanics)
    {
        if (identity.Kind != WeaponIdentityKind.Custom) throw new ArgumentException("Custom identity required.", nameof(identity));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Name must contain 1–80 printable characters.", nameof(name));
        ArgumentNullException.ThrowIfNull(mechanics);
        if (identity.DefinitionHash != mechanics.DefinitionHash) throw new ArgumentException("Definition digest mismatch.", nameof(identity));
        Identity = identity; Name = name; Mechanics = mechanics;
    }
}

/// <summary>Scalar-only read adapter. Does not lend mutable native array storage.</summary>
public sealed class NativeWeaponView
{
    public WeaponIdentity Identity { get; }
    public int BodyDamage { get; }
    public int HeadDamage { get; }
    public int CooldownTicks { get; }
    public int AmmoCost { get; }
    internal NativeWeaponView(WeaponInfo info)
    {
        Identity = WeaponIdentity.Native(info.Beam);
        BodyDamage = info.UnchargedDamage; HeadDamage = info.HeadshotDamage;
        // Native authoring units are 30 Hz; the authority steps at 60 Hz.
        CooldownTicks = info.ShotCooldown * 2; AmmoCost = info.AmmoCost;
    }
}

public sealed class WeaponRuntimeRef
{
    public WeaponRuntimeId RuntimeId { get; }
    public WeaponIdentity Identity { get; }
    public NativeWeaponView? Native { get; }
    public NativeWeaponView? NativeAffinity { get; }
    public WeaponDefinition? Custom { get; }
    internal WeaponRuntimeRef(WeaponRuntimeId runtimeId, WeaponIdentity identity,
        NativeWeaponView? native, NativeWeaponView? affinity, WeaponDefinition? custom)
    { RuntimeId = runtimeId; Identity = identity; Native = native; NativeAffinity = affinity; Custom = custom; }
}

/// <summary>One scene/session, frozen at construction. Never a global catalog.</summary>
public sealed class WeaponRegistry : IDisposable
{
    public const int MaxCustomWeapons = 64;
    private readonly Dictionary<WeaponIdentity, WeaponRuntimeRef> _identities = new();
    private readonly Dictionary<WeaponRuntimeId, WeaponRuntimeRef> _runtime = new();
    private readonly CustomWeaponInventory?[] _inventories = new CustomWeaponInventory[8];
    private bool _disposed;
    public bool IsDisposed => _disposed;
    public int Count { get { EnsureAlive(); return _identities.Count; } }

    public WeaponRegistry(bool multiplayer, IEnumerable<WeaponDefinition> customDefinitions)
    {
        ArgumentNullException.ThrowIfNull(customDefinitions);
        // Enumerate under a hard cap; a malicious or accidental infinite input
        // cannot allocate an unbounded temporary list before validation.
        var custom = new List<WeaponDefinition>();
        foreach (var definition in customDefinitions)
        {
            if (definition == null) throw new ArgumentException("Missing definition.", nameof(customDefinitions));
            if (custom.Count == MaxCustomWeapons) throw new ArgumentException("Weapon manifest exceeds its limit.", nameof(customDefinitions));
            custom.Add(definition);
        }
        if (custom.Select(x => x.Identity).Distinct().Count() != custom.Count)
            throw new ArgumentException("Duplicate weapon revision.", nameof(customDefinitions));
        var table = multiplayer ? global::MphRead.Weapons.WeaponsMP : global::MphRead.Weapons.Weapons1P;
        for (int i = 0; i < 9; i++)
        {
            if (table[i].Beam != (BeamType)i || table[i + 9].Beam != (BeamType)i)
                throw new InvalidOperationException("Native weapon table layout changed.");
            Add(new(new((ushort)(i + 1)), WeaponIdentity.Native((BeamType)i),
                new(table[i]), new(table[i + 9]), null));
        }
        // Canonical order, independent of package enumeration, culture or client.
        foreach (var definition in custom.OrderBy(x => x.Identity.ToString(), StringComparer.Ordinal))
            Add(new(new((ushort)(_runtime.Count + 1)), definition.Identity, null, null, definition));
    }

    private void Add(WeaponRuntimeRef weapon)
    { _identities.Add(weapon.Identity, weapon); _runtime.Add(weapon.RuntimeId, weapon); }
    private void EnsureAlive()
    { if (_disposed) throw new ObjectDisposedException(nameof(WeaponRegistry)); }
    public bool TryResolve(WeaponIdentity identity, out WeaponRuntimeRef? weapon)
    { EnsureAlive(); return _identities.TryGetValue(identity, out weapon); }
    public bool TryResolve(WeaponRuntimeId runtimeId, out WeaponRuntimeRef? weapon)
    { EnsureAlive(); return _runtime.TryGetValue(runtimeId, out weapon); }
    public CustomWeaponInventory Inventory(int slot)
    {
        EnsureAlive();
        if ((uint)slot >= 8) throw new ArgumentOutOfRangeException(nameof(slot));
        return _inventories[slot] ??= new(this, (byte)slot);
    }
    public void Dispose()
    {
        if (_disposed) return;
        foreach (var inventory in _inventories) inventory?.Invalidate();
        Array.Clear(_inventories); _identities.Clear(); _runtime.Clear(); _disposed = true;
    }
}

/// <summary>Separate from native ammo, unlock bits, nine-element arrays and slots.</summary>
public sealed class CustomWeaponInventory
{
    private readonly WeaponRegistry _registry;
    private readonly byte _slot;
    private readonly Dictionary<WeaponIdentity, WeaponRuntimeState> _states = new();
    private bool _invalid, _bound;
    private WeaponOwnerContext _owner;
    private uint _tickHighWater;
    internal CustomWeaponInventory(WeaponRegistry registry, byte slot) { _registry = registry; _slot = slot; }
    private void EnsureAlive()
    { if (_invalid || _registry.IsDisposed) throw new ObjectDisposedException(nameof(CustomWeaponInventory)); }
    public bool BindLife(WeaponOwnerContext owner, uint tick)
    {
        EnsureAlive();
        if (!owner.IsValid || owner.Slot != _slot) return false;
        if (_owner.IsValid)
        {
            // A different match/authority requires a new frozen registry. Never
            // reopen a previous life to manufacture a second initial magazine.
            if (owner.MatchId != _owner.MatchId || owner.AuthorityEpoch != _owner.AuthorityEpoch) return false;
            if (owner.Generation == _owner.Generation)
            { if (unchecked((short)(owner.Life - _owner.Life)) <= 0) return false; }
            else if (unchecked((short)(owner.Generation - _owner.Generation)) <= 0) return false;
        }
        if (!AcceptTime(tick)) return false;
        ClearStates(); _owner = owner; _tickHighWater = tick; _bound = true;
        return true;
    }
    public bool Grant(WeaponIdentity identity, uint tick)
    {
        EnsureAlive();
        if (!_bound || identity.Kind != WeaponIdentityKind.Custom || _states.ContainsKey(identity)
            || !_registry.TryResolve(identity, out var weapon) || weapon!.Custom == null) return false;
        if (!AcceptTime(tick)) return false;
        _states.Add(identity, new WeaponRuntimeState(weapon.Custom,_owner,tick));
        return true;
    }
    public bool Contains(WeaponIdentity identity) { EnsureAlive(); return _states.ContainsKey(identity); }
    public bool TryGetState(WeaponIdentity identity, out WeaponRuntimeState? state)
    { EnsureAlive(); return _states.TryGetValue(identity,out state); }
    private void ObserveStateTimes()
    {
        foreach (var state in _states.Values)
        {
            uint observed = state.Snapshot().Tick;
            if (unchecked((int)(observed - _tickHighWater)) > 0) _tickHighWater = observed;
        }
    }
    private bool AcceptTime(uint tick)
    {
        ObserveStateTimes();
        if (_owner.IsValid && unchecked((int)(tick - _tickHighWater)) < 0) return false;
        _tickHighWater = tick; return true;
    }
    private void ClearStates()
    {
        ObserveStateTimes();
        foreach (var state in _states.Values) state.Invalidate(WeaponCancellation.OwnerChanged);
        _states.Clear(); _bound = false;
    }
    public void Clear() { EnsureAlive(); ClearStates(); }
    internal void Invalidate() { ClearStates(); _invalid = true; }
}
