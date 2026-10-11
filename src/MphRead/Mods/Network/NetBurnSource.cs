using System;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Network;

/// <summary>Value ownership survives projectile pool reuse, source respawn and
/// disconnect. A replacement occupant never inherits credit or changes tick policy.</summary>
internal readonly record struct NetBurnSource(ShotKey Key, uint Component, uint LaunchFrame,
    BeamType Weapon, ushort Duration, int TickFrames, uint Damage, bool Balanced)
{
    internal static NetBurnSource? Current { get; private set; }
    private readonly record struct Tick(ShotKey Source, uint Component, NetTargetIdentity Victim,
        uint Frame, bool Turret);
    private static readonly Tick?[,] LastTicks = new Tick?[8, 2];
    internal static NetBurnSource From(BeamProjectileEntity beam, ushort duration, bool balanced)
        => new(NetSession.Active ? beam.ModLaunchKey
                : ShotKey.For((beam.Owner as PlayerEntity ?? (beam.Owner as HalfturretEntity)?.Owner)?.SlotIndex ?? -1, 0), beam.ModComponentId, beam.ModLaunchFrame, beam.Beam,
            balanced ? BalancedModeRules.SpireBurnDurationFrames : duration,
            balanced ? BalancedModeRules.SpireBurnTickFrames : 16,
            beam.ModDoubleAtLaunch ? 2u : 1u, balanced);
    internal PlayerEntity? ResolveOwner(Scene scene)
    {
        if ((uint)Key.ShooterSlot >= scene.Players.Items.Count) return null;
        if (NetSession.Active && (!NetSession.MatchesStream(Key.MatchId, Key.AuthorityEpoch)
            || Key.Generation == 0 || Key.Generation != NetPlayerLifecycle.Generation(Key.ShooterSlot))) return null;
        return scene.Players.Items[Key.ShooterSlot];
    }
    internal void Apply(PlayerEntity victim, DamageFlags extraFlags = 0)
    {
        if (NetSession.Active && (!NetHitClaims.Arbitrating
            || !NetSession.MatchesStream(Key.MatchId, Key.AuthorityEpoch))) return;
        bool turret = extraFlags.TestFlag(DamageFlags.Halfturret);
        if (NetSession.Active)
        {
            var tick = new Tick(Key, Component, NetTargetIdentity.ForSlot(victim.SlotIndex), NetSession.NetFrame, turret);
            int kind = turret ? 1 : 0;
            if (LastTicks[victim.SlotIndex, kind] == tick) return;
            LastTicks[victim.SlotIndex, kind] = tick;
        }
        var saved = Current; Current = this;
        try
        {
            victim.TakeDamage(Damage, DamageFlags.NoSfx | DamageFlags.Burn | DamageFlags.NoDmgInvuln | extraFlags,
                null, ResolveOwner(victim.OwningScene));
        }
        finally { Current = saved; }
    }
    internal static void Reset() { Array.Clear(LastTicks); Current = null; }
}
