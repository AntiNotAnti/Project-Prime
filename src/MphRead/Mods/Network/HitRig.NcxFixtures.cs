using System;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

// Explicit test-harness emulation of the pinned native Wells/Lanes control
// script in a declared open box. This is not movement validation or gameplay.
public static partial class HitRig
{
    private static long _ncxPlacements;
    private static bool _ncxFixtureRejected;
    internal static bool NcxFixtureActive => Mode is RigMode.NcxStatic or RigMode.NcxLateral or RigMode.NcxJump or RigMode.NcxObserve;
    internal static bool NcxObserverRole => Mode == RigMode.NcxObserve && NetSession.LocalSlot == 2;
    internal static float NcxFixtureAimHeight(int frame) => frame / 300 % 2 == 0 ? .95f : .30f;
    internal static bool NcxFixtureCanFire(RigMode mode, int slot)
        => (mode is RigMode.NcxStatic or RigMode.NcxLateral or RigMode.NcxJump) && slot is 0 or 1;
    internal static string? NcxFixtureRoom(RigMode mode) => mode switch
    {
        RigMode.NcxStatic => "NCX EMULATED STATIC",
        RigMode.NcxLateral => "NCX EMULATED LATERAL",
        RigMode.NcxJump => "NCX EMULATED JUMP",
        _ => null
    };
    private static bool ConfigureNcxFixture(string? value)
    {
        RigMode? mode = value?.Trim().ToLowerInvariant() switch
        {
            "ncx-static" => RigMode.NcxStatic,
            "ncx-lateral" => RigMode.NcxLateral,
            "ncx-jump" => RigMode.NcxJump,
            "ncx-observe" => RigMode.NcxObserve,
            _ => null
        };
        if (!mode.HasValue) return false;
        Mode = mode.Value;
        return true;
    }
    private static void ResetNcxFixture() { _ncxPlacements = 0; _ncxFixtureRejected = false; }
    private static bool DriveNcxFixture(PlayerEntity player, PlayerControls controls)
    {
        if (!NcxFixtureActive) return false;
        AimDeltaX = AimDeltaY = 0;
        string? room = player.OwningScene.Room?.Meta.Name;
        bool roomValid = Mode == RigMode.NcxObserve
            ? room is "NCX EMULATED STATIC" or "NCX EMULATED LATERAL" or "NCX EMULATED JUMP"
            : string.Equals(room, NcxFixtureRoom(Mode), StringComparison.OrdinalIgnoreCase);
        int slot = NetSession.LocalSlot;
        bool observer = Mode == RigMode.NcxObserve && slot == 2;
        if (!roomValid || player.Hunter != Hunter.Samus || (!observer && !NcxFixtureCanFire(Mode, slot)))
        {
            if (!_ncxFixtureRejected) NetLog.Event("NCX emulated fixture refused: declared room, Samus and exact slot roles required");
            _ncxFixtureRejected = true;
            return true;
        }
        if (observer)
        {
            // Same ordinary connected-body vantage as native -hitrig observe.
            // Never press Shoot, including while dead; no synthetic observer hit.
            Vector3 position = player.Position;
            if (player.Health > 0 && (MathF.Abs(position.X) > 1 || MathF.Abs(position.Z - 9) > 1))
            { player.ModPlaceAt(new Vector3(0, .6f, 9)); _ncxPlacements++; }
            return true;
        }
        if (player.Health == 0)
        {
            // Reuse native input-driven respawn, including its downward aim.
            controls.Shoot.IsDown = true;
            AimDeltaY = -TurnRate;
            return true;
        }
        float cellX = slot == 0 ? -6 : 6;
        Vector3 at = player.Position;
        if (MathF.Abs(at.X - cellX) > 2)
        { player.ModPlaceAt(new Vector3(cellX, .6f, 0)); _ncxPlacements++; return true; }
        if (!player.Flags1.TestFlag(PlayerFlags1.Standing)) FramesAirborne++;
        float rise = MathF.Abs(player.Speed.Y);
        VerticalSpeedSum += rise; VerticalSpeedSamples++;
        WorstVerticalSpeed = MathF.Max(WorstVerticalSpeed, rise);
        if (Mode == RigMode.NcxLateral)
        {
            controls.MoveLeft.IsDown = _frame / 40 % 2 == 0;
            controls.MoveRight.IsDown = !controls.MoveLeft.IsDown;
        }
        // CellOpponent additionally checks opposite cell geometry. This explicit
        // two-slot pairing prevents a third observing body becoming a target.
        PlayerEntity? other = (slot ^ 1) < PlayerEntity.Players.Count ? PlayerEntity.Players[slot ^ 1] : null;
        if (other != null && (!other.LoadFlags.TestFlag(LoadFlags.Active)
            || !other.LoadFlags.TestFlag(LoadFlags.Spawned) || other.Health <= 0
            || MathF.Abs(other.Position.X + at.X) >= 2.5f || MathF.Abs(other.Position.X) <= 3.5f)) other = null;
        if (player.CurrentWeapon != BeamType.PowerBeam) player.ModArmWeapon(BeamType.PowerBeam);
        player.ModSetAmmo(Int32.MaxValue, Int32.MaxValue);
        // The ordinary Prime AimAt intentionally maps positive values to the
        // authored head band. This fixture instead honors the exact native
        // script height, including its separate 0.30 chest-band interval.
        bool onTarget = false;
        if (other != null)
        {
            (float turnX, float turnY) = player.ModAimDeltaTowards(other.Position + Vector3.UnitY * NcxFixtureAimHeight(_frame));
            if (Single.IsFinite(turnX) && Single.IsFinite(turnY))
            {
                AimDeltaX = Math.Clamp(turnX, -TurnRate, TurnRate);
                AimDeltaY = Math.Clamp(turnY, -TurnRate, TurnRate);
                onTarget = MathF.Abs(turnX) < FiringCone && MathF.Abs(turnY) < FiringCone;
            }
        }
        if (onTarget) FramesOnTarget++;
        int tap = Weapons.Current[(int)BeamType.PowerBeam].ShotCooldown * 2 + 3;
        controls.Shoot.IsDown = onTarget && _frame % tap < 3;
        if (controls.Shoot.IsDown && _frame % tap == 0) Triggers++;
        return true;
    }
    private static string DescribeNcxFixture() => $"hit rig: {Mode} emulated open-box, slot {NetSession.LocalSlot}, "
        + $"{_ncxPlacements} scripted placements, {Triggers} trigger edges, {FramesOnTarget} frames on target, "
        + $"{FramesAirborne} frames airborne, rejected={_ncxFixtureRejected}; both participants fire; observer never fires";
}
