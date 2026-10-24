using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.HeroModes;

/// <summary>Versioned, fixed-step custom CTF rules. Numeric Halo-reference parity is a release gate.</summary>
public static class CtfRules
{
    public static bool Active(GameMode mode) => mode == GameMode.CaptureTheFlag;
    public static ProjectPrime.CaptureZone.HeroDefinition Definition(Hunter hunter)
    {
        var values=Metadata.PlayerValues[(int)hunter];
        return new(HeroRules.ToZone(hunter),"NATIVE MPH",2*values.EnergyTank-1,0,"Native weapons","Original MPH movement and combat","Native alternate form","Power Beam, missiles, finite ammo and map pickups");
    }
    public static uint ReduceDamage(Hunter hunter,uint damage)=>damage;
    public static bool AllowedPickup(ItemType item)=>true;
    public static System.Collections.Generic.IReadOnlyList<WeaponInfo> WeaponTable=>Weapons.WeaponsMP;
    public const uint Capability = 0x33465443; // CTF3: reliable flag-reset announcements; ordinary v53 wire prefix unchanged.
    public const int RulesetVersion = 2;
    public const int CaptureLimit = 3;
    public const int MatchSeconds = 15 * 60;
    public const ushort RespawnTicks = 10 * 60;
    public const float CarrierSpeedMultiplier = 1.2f;
    // Explicit development defaults. Do not label these as a verified Halo 3 variant.
    public const float ReturnSeconds = 3f;
    public const float ResetSeconds = 15f;
    public const float PickupRadius = 1.8f;
    public const float ReturnRadius = 2.0f;
    public const float ThrowSpeed = 11f;
    public const float ThrowLift = 3f;
    public const float Gravity = 16f;
    public const float FlagRadius = .22f;
    public const float ThrowerPickupLockSeconds = .5f;
    public const int BurstRounds = 3;
    public const int BurstSpacingTicks = 4; // 66.667 ms on the existing 60 Hz simulation.
    public const int BurstRecoveryTicks = 16; // 266.667 ms after round three.
    public const int BurstPeriodTicks = 2 * BurstSpacingTicks + BurstRecoveryTicks;
    public const string ZoomDescription = "Zoom / aim down sights. In Capture the Flag, press near an enemy flag to pick it up; while carrying a flag, press to throw it.";
    public const string ReferenceStatus = "Halo 3 two-flag reference; exact variant/timer, overtime and objective-melee parity NOT YET VERIFIED.";

    public static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    public static uint ReducedDamage(uint damage, double reduction) => damage == 0 ? 0u
        : (uint)Math.Max(1, Math.Ceiling(damage * (1 - Math.Clamp(reduction, 0, .95))));
}

public enum CtfFlagPhase { Home, Carried, Dropped }

/// <summary>
/// The same state machine used by the live flag entity and deterministic tests.
/// The authoritative simulation thread owns mutations; clients apply existing life-fenced objective snapshots.
/// </summary>
public sealed class CtfFlagState
{
    public int Team { get; }
    public Vector3 Home { get; }
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public CtfFlagPhase Phase { get; private set; }
    public int CarrierSlot { get; private set; } = -1;
    public int LastCarrierSlot { get; private set; } = -1;
    public float DroppedSeconds { get; private set; }
    public float ReturnProgress { get; private set; }
    public float ThrowerLock { get; private set; }
    public bool Grounded { get; private set; } = true;
    public uint Revision { get; private set; }

    public CtfFlagState(int team, Vector3 home)
    {
        if (team is < 0 or > 1 || !CtfRules.Finite(home)) throw new ArgumentException("Invalid CTF base.");
        Team = team; Home = Position = home; Phase = CtfFlagPhase.Home;
    }
    public bool CanPickup(int slot, int team, bool aliveBiped, bool alreadyCarrying, Vector3 playerPosition, bool clearPath)
        => slot is >= 0 and < 8 && team is >= 0 and <= 1 && team != Team && aliveBiped
            && !alreadyCarrying && Phase != CtfFlagPhase.Carried && clearPath
            && CtfRules.Finite(playerPosition) && (playerPosition - Position).LengthSquared <= CtfRules.PickupRadius * CtfRules.PickupRadius
            && (slot != LastCarrierSlot || ThrowerLock <= 0);
    public bool Pickup(int slot, int team, bool aliveBiped, bool alreadyCarrying, Vector3 playerPosition, bool clearPath)
    {
        if (!CanPickup(slot, team, aliveBiped, alreadyCarrying, playerPosition, clearPath)) return false;
        CarrierSlot = LastCarrierSlot = slot; Phase = CtfFlagPhase.Carried;
        Velocity = Vector3.Zero; DroppedSeconds = ReturnProgress = ThrowerLock = 0; Grounded = true; Revision++;
        return true;
    }
    public void Follow(Vector3 position)
    {
        if (Phase == CtfFlagPhase.Carried && CtfRules.Finite(position)) Position = position;
    }
    public bool Drop(Vector3 direction, bool thrown)
    {
        if (Phase != CtfFlagPhase.Carried) return false;
        if (!CtfRules.Finite(direction) || direction.LengthSquared < .001f) direction = Vector3.UnitZ;
        direction.Normalize();
        Velocity = thrown ? direction * CtfRules.ThrowSpeed + Vector3.UnitY * CtfRules.ThrowLift : Vector3.Zero;
        LastCarrierSlot = CarrierSlot; CarrierSlot = -1; Phase = CtfFlagPhase.Dropped;
        Grounded = false; DroppedSeconds = ReturnProgress = 0; ThrowerLock = CtfRules.ThrowerPickupLockSeconds; Revision++;
        return true;
    }
    public void Reset()
    {
        Position = Home; Velocity = Vector3.Zero; Phase = CtfFlagPhase.Home;
        CarrierSlot = LastCarrierSlot = -1; DroppedSeconds = ReturnProgress = ThrowerLock = 0; Grounded = true; Revision++;
    }
    public bool Capture(int carrierSlot, bool ownFlagHome, bool inOwnBase)
    {
        if (Phase != CtfFlagPhase.Carried || carrierSlot != CarrierSlot || !ownFlagHome || !inOwnBase) return false;
        Reset(); return true;
    }
    /// <summary>Returns true when an unattended or defended dropped flag returns home.</summary>
    public bool Advance(float dt, bool defenderNear, bool attackerNear)
    {
        if (!float.IsFinite(dt) || dt < 0 || dt > .1f) throw new ArgumentOutOfRangeException(nameof(dt));
        ThrowerLock = MathF.Max(0, ThrowerLock - dt);
        if (Phase != CtfFlagPhase.Dropped) return false;
        DroppedSeconds += dt;
        // One defender and four defenders have the same rate. Contest pauses, abandonment resets.
        if (!defenderNear) ReturnProgress = 0;
        else if (!attackerNear) ReturnProgress += dt;
        if (ReturnProgress + .0001f >= CtfRules.ReturnSeconds || DroppedSeconds + .0001f >= CtfRules.ResetSeconds)
        { Reset(); return true; }
        return false;
    }
    public Vector3 ProposeMotion(float dt)
    {
        if (Phase != CtfFlagPhase.Dropped || Grounded) return Position;
        Velocity -= Vector3.UnitY * CtfRules.Gravity * dt;
        return Position + Velocity * dt;
    }
    public void ResolveMotion(Vector3 position, Vector3 normal, bool collided)
    {
        if (!CtfRules.Finite(position) || !CtfRules.Finite(normal)) { Reset(); return; }
        Position = position;
        if (!collided) return;
        float into = Vector3.Dot(Velocity, normal);
        if (into < 0) Velocity -= normal * into;
        Velocity *= .5f;
        if (normal.Y > .5f) { Grounded = true; Velocity = Vector3.Zero; }
    }
}
