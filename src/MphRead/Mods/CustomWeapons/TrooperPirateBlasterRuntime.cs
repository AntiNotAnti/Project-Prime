using System;
using MphRead.Entities;
using MphRead.Mods.MatchEvents;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

// Private local practice owner. The native Scene clock, UA bank, projectile
// collision and damage owners remain authoritative. No M7 state or wire ID.
internal sealed class TrooperPirateBlasterRuntime : IDisposable
{
    private sealed class Flight
    {
        internal required BeamProjectileEntity Beam;
        internal required CustomShotSource Source;
        internal required PirateBurstTimeline.Ticket Ticket;
        internal bool CountedHit;
    }
    private sealed class Burst
    {
        internal uint Id;
        internal byte Seen, Mask;
        internal PlayerEntity? Target;
        internal MatchSemanticActor Life;
        internal ushort Match;
    }
    private readonly record struct Mark(PlayerEntity? Target, MatchSemanticActor Life, ushort Match, ulong Until);
    internal readonly record struct View(bool Selected, int Ua, int Cooldown, bool Tails, ulong Shots, ulong Hits, int Marks);
    private readonly Scene _scene;
    private readonly PlayerEntity _player;
    private readonly long _generation;
    private readonly WeaponRegistry _registry;
    private readonly EquipInfo _equip;
    private readonly WeaponInfo _adapter;
    private readonly BeamProjectileEntity[] _pool;
    private readonly PirateBurstTimeline _timeline = new();
    private readonly Flight?[] _flights = new Flight?[40];
    private readonly Burst?[] _bursts = new Burst?[40];
    private readonly Mark[] _marks = new Mark[8];
    private WeaponOwnerContext _owner;
    private bool _disposed, _selected, _alive, _emitting, _emitted, _inputAllowed;
    private ulong _sampleFrame = ulong.MaxValue, _completedFrame = ulong.MaxValue;
    private CustomShotSource? _pendingSource;
    private PirateBurstTimeline.Ticket _pending;
    private Contact? _contact;
    internal WeaponOwnerContext Owner => _owner;
    internal ulong Shots { get; private set; }
    internal ulong Hits { get; private set; }
    internal CustomImpactFact? LastImpact { get; private set; }
    internal View Snapshot => new(Selected(_player), _player.ModPirateUa, _timeline.Cooldown,
        _timeline.HasPaidTails, Shots, Hits, CountMarks());
    internal ReadOnlySpan<BeamProjectileEntity> Beams => _pool;
    private bool AdapterValid => ReferenceEquals(_equip.Weapon, _adapter) && ReferenceEquals(_equip.Beams, _pool)
        && _equip.InfiniteAmmo && _equip.GetAmmo == null && _equip.SetAmmo == null && !_equip.Zoomed && _equip.ChargeLevel == 0
        && _equip.UnchargedDamage == 12 && _equip.MinChargeDamage == 12 && _equip.ChargedDamage == 12
        && _equip.HeadshotDamage == 16 && _equip.MinChargeHeadshotDamage == 16 && _equip.ChargedHeadshotDamage == 16
        && _equip.SplashDamage == 0 && _equip.MinChargeSplashDamage == 0 && _equip.ChargedSplashDamage == 0
        && _equip.DrawFuncIds[0] == 255 && _equip.DrawFuncIds[1] == 255
        && _equip.DmgDirTypes[0] == 255 && _equip.DmgDirTypes[1] == 255;
    private bool Eligible => !_disposed && _scene.ForegroundLease.ThreadId == Environment.CurrentManagedThreadId
        && _generation == _scene.LifetimeGeneration && !_scene.Exiting && !_scene.SideScene && !_scene.IsReplayLab
        && !_scene.Services.IsReplica && _scene.GameState.Multiplayer && _scene.GameState.Mode == GameMode.Battle
        && !_scene.GameState.Teams && !_scene.GameState.LowTier && !_scene.GameState.InstaGib
        && !_scene.GameState.OneInTheChamber && !_scene.GameState.Fiesta && !_scene.GameState.NoImperialist
        && !NetSession.Active && !DemoPlayback.IsActive
        && !ReplayCapture.LiveRecordingActive && _scene.AimTrainer == null
        && ReferenceEquals(global::MphRead.GameState.Current, _scene.GameState)
        && ReferenceEquals(PlayerEntity.LegacyRegistry, _scene.Players)
        && ReferenceEquals(_scene.CustomWeapons, _registry) && !_registry.IsDisposed && AdapterValid
        && _scene.OfflineWeapons == null && _scene.CustomCombat == null
        && _scene.Players.Items.Count > 0 && ReferenceEquals(_scene.Players.Items[0], _player)
        && !_player.IsBot && _player.SlotIndex == 0 && _player.ModCharacterGameplay.UsesPirateBlaster;
    private bool CurrentLife => _alive && _player.Health > 0
        && _player.LoadFlags.TestFlag(LoadFlags.Active) && _player.LoadFlags.TestFlag(LoadFlags.Spawned)
        && !_player.Flags2.TestFlag(PlayerFlags2.Spectating)
        && _scene.MatchEvents.TryReadOfflineLife(_scene, _player, out var match, out var actor)
        && match == _owner.MatchId && actor.SlotGeneration == _owner.Generation && actor.Life == _owner.Life;
    internal TrooperPirateBlasterRuntime(Scene scene, PlayerEntity player)
    {
        _scene = scene; _player = player; _generation = scene.LifetimeGeneration;
        if (!player.ModCharacterGameplay.UsesPirateBlaster || scene.OfflineWeapons != null || scene.CustomCombat != null)
            throw new InvalidOperationException("Explicit private Trooper Arsenal rule required.");
        _registry = scene.FreezeCustomWeaponRegistry(new[] { PirateBlasterDefinition.Definition });
        _adapter = PirateBlasterDefinition.BuildAdapter(); _pool = SceneSetup.CreateBeamList(40, scene);
        _equip = new(_adapter, _pool) { InfiniteAmmo = true };
        if (!Eligible) throw new InvalidOperationException("Private Blaster owner is unavailable.");
    }
    internal bool Selected(PlayerEntity player) => ReferenceEquals(player, _player) && Eligible && CurrentLife && _selected;
    internal bool Available(PlayerEntity player) => ReferenceEquals(player, _player) && Eligible && CurrentLife;
    // Called only at the end of the genuine native Spawn; repeated calls for
    // that same native life never refill the shared bank or reset cadence.
    internal void Spawned(PlayerEntity player)
    {
        if (!ReferenceEquals(player, _player) || !Eligible || player.Health <= 0
            || !_scene.MatchEvents.TryReadOfflineLife(_scene, player, out var match, out var actor)) return;
        var next = new WeaponOwnerContext(match, checked((ulong)_generation), actor.Slot, actor.SlotGeneration, actor.Life);
        if (_owner == next) return;
        EndLife(); _owner = next; _alive = _selected = true;
        _timeline.ResetForLife(); _player.ModGrantPirateUa();
    }
    internal void Died(PlayerEntity player) { if (ReferenceEquals(player, _player)) EndLife(); }
    private void EndLife()
    {
        _alive = _selected = false; _inputAllowed = false; _timeline.CancelTails();
        Array.Clear(_marks); Array.Clear(_bursts); _contact = null;
        foreach (var beam in _pool)
        {
            if (beam.Owner != null || beam.Lifespan > 0) beam.Destroy();
            _scene.RemoveEntity(beam);
        }
        Array.Clear(_flights);
    }
    internal void Step()
    {
        if (_disposed) return;
        if (!Eligible) { Dispose(); return; }
        if (!CurrentLife) { if (_alive) EndLife(); return; }
        if (!_player.ModPirateCanUse || !_selected) _timeline.CancelTails();
        if (_scene.GameState.MatchState != MatchState.InProgress) Array.Clear(_marks);
        for (int i = 0; i < _marks.Length; i++)
            if (_marks[i].Target is {} target && !MarkValid(_marks[i], target)) _marks[i] = default;
        for (int i = 0; i < _bursts.Length; i++) if (_bursts[i] is {} burst)
        {
            bool live = _timeline.HasPaidTails && _timeline.Snapshot.Burst == burst.Id || _emitting && _pending.Burst == burst.Id;
            foreach (var flight in _flights) if (flight?.Ticket.Burst == burst.Id) { live = true; break; }
            if (!live) _bursts[i] = null;
        }
    }
    internal void SampleInput(PlayerEntity player, bool allowed)
    {
        if (!Available(player) || _sampleFrame == _scene.FrameCount) return;
        _sampleFrame = _scene.FrameCount;
        _inputAllowed = allowed && !Mods.SpectatorMode.IsSpectating
            && Mods.Input.GamepadContexts.Current == Mods.Input.GamepadContext.Gameplay;
        if (_selected && player.ModPirateUa < 5 && !_timeline.HasPaidTails)
        { _selected = false; player.ModPirateFallback(); }
        var c = player.Controls;
        bool native = player.ModTakeNativeSelectionIntent() || player.ModNativeWheelReleaseIntent
            || c.PowerBeam.IsPressed || c.Missile.IsPressed || c.VoltDriver.IsPressed || c.Battlehammer.IsPressed
            || c.Imperialist.IsPressed || c.Judicator.IsPressed || c.Magmaul.IsPressed || c.ShockCoil.IsPressed || c.OmegaCannon.IsPressed;
        bool custom = c.CustomWeapon.IsPressed || c.AffinitySlot.IsPressed;
        bool cycle = c.NextWeapon.IsPressed || c.PrevWeapon.IsPressed;
        if (_inputAllowed && player.ModPirateCanUse)
        {
            if (native) _selected = false;
            else if (custom) _selected = true;
            else if (cycle && !player.ModCustomCycleMenuDown)
                _selected = player.ModStagePirateCycle(_selected, player.ModPirateUa >= 5 || _timeline.HasPaidTails).Custom;
        }
        if (!_inputAllowed || !_selected || !player.ModPirateCanUse) _timeline.CancelTails();
        player.ModSynchronizeCustomNativeSuppression();
    }
    internal void CompleteInput(PlayerEntity player)
    {
        if (!Available(player) || _completedFrame == _scene.FrameCount) return;
        _completedFrame = _scene.FrameCount;
        bool usable = _sampleFrame == _scene.FrameCount && _inputAllowed && _selected && player.ModPirateCanUse;
        if (!_timeline.BeginStep(_scene.FrameCount, usable) || !usable) return;
        int free = 0; foreach (var beam in _pool) if (beam.Lifespan <= 0) free++;
        if (!_timeline.TryPlan(player.Controls.Shoot.IsDown, player.ModPirateUa, free, out var ticket)) return;
        bool emitted = false, debited = false;
        try
        {
            if (ticket.Ordinal == 0)
            {
                int at = Array.FindIndex(_bursts, b => b == null);
                if (at < 0 || !player.ModSpendPirateUa(this, ticket.AmmoCost)) return;
                debited = true; _bursts[at] = new() { Id = ticket.Burst };
            }
            if (!player.ModTryPirateAim(out var origin, out var direction)) return;
            _pending = ticket; _pendingSource = CustomShotSource.ForOffline(PirateBlasterDefinition.Definition, _owner, ticket.Projectile);
            _equip.CustomShot = _pendingSource; _emitting = true; _emitted = false;
            var flags = BeamSpawnFlags.NoMuzzle;
            if (player.DoubleDamage) flags |= BeamSpawnFlags.DoubleDamage;
            else if (player.IsPrimeHunter) flags |= BeamSpawnFlags.PrimeHunter;
            BeamProjectileEntity.SpawnWithEmission(player, _equip, origin, direction, flags, player.NodeRef, _scene, out _);
            emitted = _emitted;
            if (emitted) player.ModPirateShotCue();
        }
        finally
        {
            emitted |= _emitted && _emitting;
            _emitting = _emitted = false; _pendingSource = null; _equip.CustomShot = null;
            _timeline.Complete(ticket, emitted);
            if (debited && !emitted) player.ModRefundPirateUa(this, ticket.AmmoCost);
        }
    }
    internal bool OwnsEmission(EntityBase player, EquipInfo equip) => Eligible && CurrentLife && _selected
        && ReferenceEquals(player, _player) && ReferenceEquals(equip, _equip) && _emitting && !_emitted
        && _pending.Frame == _scene.FrameCount && _sampleFrame == _scene.FrameCount && _inputAllowed
        && _player.ModPirateCanUse && ReferenceEquals(equip.CustomShot, _pendingSource)
        && _pendingSource is { } source && PirateBlasterDefinition.Is(source.Definition)
        && source.Owner == _owner && source.Sequence == _pending.Projectile;
    // Native Spawn has selected and stamped a real free member before any
    // collision callback. The pending source cannot authorize another member.
    internal void RegisterFlight(BeamProjectileEntity beam)
    {
        if (!OwnsEmission(_player, _equip) || !ReferenceEquals(beam.ModCustomShot, _pendingSource)
            || !ReferenceEquals(beam.Owner, _player) || !ReferenceEquals(beam.Equip, _equip)) return;
        int at = Array.IndexOf(_pool, beam); if (at < 0 || _flights[at] != null) return;
        _flights[at] = new() { Beam = beam, Source = _pendingSource!, Ticket = _pending };
        _emitted = true; Shots++;
        if (_scene.GameState.ShotsFired[0] < int.MaxValue) _scene.GameState.ShotsFired[0]++;
    }
    private Flight? Find(BeamProjectileEntity beam)
    {
        int at = Array.IndexOf(_pool, beam);
        return at >= 0 && _flights[at] is {} f && ReferenceEquals(f.Source, beam.ModCustomShot)
            && ReferenceEquals(beam.Owner, _player) && ReferenceEquals(beam.Equip, _equip) && f.Source.Owner == _owner ? f : null;
    }
    internal bool OwnsFlight(BeamProjectileEntity beam) => Eligible && CurrentLife && Find(beam) != null;
    internal void ForgetFlight(BeamProjectileEntity beam)
    { int at = Array.IndexOf(_pool, beam); if (at >= 0) _flights[at] = null; }
    internal sealed class Contact : IDisposable
    {
        private readonly TrooperPirateBlasterRuntime _runtime;
        internal readonly BeamProjectileEntity Beam;
        internal readonly PlayerEntity Victim;
        internal readonly MatchSemanticActor Life;
        internal readonly ushort Match;
        internal bool Entered, Reserved;
        internal Contact(TrooperPirateBlasterRuntime runtime, BeamProjectileEntity beam, PlayerEntity victim, ushort match, MatchSemanticActor life)
        { _runtime = runtime; Beam = beam; Victim = victim; Match = match; Life = life; }
        public void Dispose() { if (ReferenceEquals(_runtime._contact, this)) _runtime._contact = null; }
    }
    internal Contact? EnterContact(BeamProjectileEntity beam, PlayerEntity victim)
    {
        if (_contact != null || !OwnsFlight(beam) || victim == _player || victim.Health <= 0
            || !victim.ModIsInPlay || victim.Flags2.TestFlag(PlayerFlags2.Spectating)
            || !_scene.MatchEvents.TryReadOfflineLife(_scene, victim, out var match, out var actor)) return null;
        return _contact = new(this, beam, victim, match, actor);
    }
    internal bool TryEnterDamage(PlayerEntity victim, BeamProjectileEntity beam)
    {
        if (_contact is not { Entered: false } c || !ReferenceEquals(c.Beam, beam) || !ReferenceEquals(c.Victim, victim)
            || !OwnsFlight(beam) || !_scene.MatchEvents.TryReadOfflineLife(_scene, victim, out var match, out var actor)
            || c.Match != match || c.Life != actor) return false;
        c.Entered = true; return true;
    }
    internal bool OwnsDamage(PlayerEntity victim, BeamProjectileEntity beam) => _contact is { Entered: true } c
        && ReferenceEquals(c.Beam, beam) && ReferenceEquals(c.Victim, victim) && Eligible && CurrentLife
        && Find(beam) != null && _scene.MatchEvents.TryReadOfflineLife(_scene, victim, out var match, out var actor)
        && c.Match == match && c.Life == actor;
    internal bool TryReserveDamage(PlayerEntity victim, BeamProjectileEntity beam)
    {
        if (_contact is not { Reserved: false } c || !OwnsDamage(victim, beam)) return false;
        c.Reserved = true; return true;
    }
    internal void Impact(BeamProjectileEntity beam, PlayerEntity victim, int before, int turretBefore, DamageFlags flags, Vector3 point)
    {
        if (_contact is not { Reserved: true } c || !OwnsDamage(victim, beam) || Find(beam) is not {} flight) return;
        int body = Math.Max(0, before - victim.Health);
        int turret = Math.Max(0, turretBefore - (victim.Halfturret?.Health ?? 0));
        int damage = body + turret; if (damage == 0) return;
        LastImpact = new(PirateBlasterDefinition.Definition.Identity, flight.Source.Identity, victim.SlotIndex,
            (uint)_scene.FrameCount, damage, turret, flags.TestFlag(DamageFlags.Halfturret), flags.TestFlag(DamageFlags.Headshot), victim.Health == 0, point);
        var state = _scene.GameState;
        state.MatchDamageDealt[0] = (int)Math.Min(int.MaxValue, (long)state.MatchDamageDealt[0] + damage);
        state.MatchDamageTaken[victim.SlotIndex] = (int)Math.Min(int.MaxValue, (long)state.MatchDamageTaken[victim.SlotIndex] + damage);
        if (!flight.CountedHit)
        {
            flight.CountedHit = true; Hits++;
            if (state.ShotsHit[0] < int.MaxValue) state.ShotsHit[0]++;
        }
        Burst? burst = Array.Find(_bursts, b => b?.Id == flight.Ticket.Burst);
        byte bit = (byte)(1 << flight.Ticket.Ordinal);
        if (burst == null || (burst.Seen & bit) != 0) return;
        burst.Seen |= bit;
        if (!ReferenceEquals(burst.Target, victim) || burst.Life != c.Life || burst.Match != c.Match)
        { burst.Target = victim; burst.Life = c.Life; burst.Match = c.Match; burst.Mask = 0; }
        burst.Mask |= bit;
        if (burst.Mask == 7 && victim.Health > 0 && _scene.FrameCount <= ulong.MaxValue - 240)
            _marks[victim.SlotIndex] = new(victim, c.Life, c.Match, _scene.FrameCount + 240);
    }
    private bool MarkValid(in Mark mark, PlayerEntity target) => _scene.GameState.MatchState == MatchState.InProgress
        && ReferenceEquals(mark.Target, target)
        && target.Health > 0 && target.ModIsInPlay && !target.Flags2.TestFlag(PlayerFlags2.Spectating)
        && _scene.FrameCount < mark.Until && _scene.MatchEvents.TryReadOfflineLife(_scene, target, out var match, out var actor)
        && mark.Match == match && mark.Life == actor;
    internal bool IsMarked(PlayerEntity viewer, PlayerEntity target) => Available(viewer)
        && (uint)target.SlotIndex < 8 && MarkValid(_marks[target.SlotIndex], target);
    private int CountMarks()
    { int count = 0; if (Available(_player)) foreach (var m in _marks) if (m.Target is {} p && MarkValid(m, p)) count++; return count; }
    public void Dispose()
    { if (_disposed) return; _disposed = true; EndLife(); }
}
