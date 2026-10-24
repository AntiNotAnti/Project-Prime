using System;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Mods;
using MphRead.Mods.Oddball;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    internal const int OddballMeleeIntervalTicks = 39; // Restored original 0.65 s at the existing 60 Hz simulation.
    private int _oddMeleeCooldown;
    private int _oddMeleeBuffer;
    internal int ModOddballSwingCount { get; private set; }
    private BeamType _oddWeapon;
    private readonly OddballSkullModel _oddViewSkull = new();
    public bool ModOddballCarrying => _scene.GameState.Mode == GameMode.Oddball && OctolithFlag?.Carrier == this;
    public bool ModOddballBlocksCombat => ModOddballCarrying
        || (_scene.GameState.Mode == GameMode.Oddball && _scene.GameState.Oddball?.IsIntermission == true);

    public void ModOddballPickedUp()
    {
        _oddWeapon = CurrentWeapon;
        StopBeamChargeSfx(CurrentWeapon);
        EquipInfo.ChargeLevel = 0;
        _oddMeleeBuffer = 0;
        Flags2 &= ~PlayerFlags2.Shooting;
        UpdateZoom(false);
        Controls.Zoom.IsPressed = false;
        _oddDiagnosticTicks = 300;
        _oddDiagnosticPosition = Position;
        if (IsMainPlayer) Console.WriteLine($"[oddball-carrier-v1.3] PICKUP slot={SlotIndex} hunter={Hunter} weapon={CurrentWeapon} pos={Position} frozen={_frozenTimer} menu={Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen)}");
    }

    private int _oddDiagnosticTicks;
    private Vector3 _oddDiagnosticPosition;
    internal void ModOddballLogCarrier()
    {
        if (!IsMainPlayer || !ModOddballCarrying || _oddDiagnosticTicks <= 0) return;
        if (--_oddDiagnosticTicks % 30 != 0) return;
        Console.WriteLine($"[oddball-carrier-v1.3] tick={_scene.FrameCount} pos={Position} delta={Position-_oddDiagnosticPosition} speed={Speed} aim={_aimVec} raw={Controls.MoveUp.IsDown}/{Controls.MoveDown.IsDown}/{Controls.MoveLeft.IsDown}/{Controls.MoveRight.IsDown} look={Input.MouseDeltaX}/{Input.MouseDeltaY} frozen={_frozenTimer} block={_field6D0} noaim={Flags1.TestFlag(PlayerFlags1.NoAimInput)} health={Health} pause={_scene.GameState.MenuPause}/{_scene.GameState.DialogPause} intermission={_scene.GameState.Oddball?.IsIntermission}");
        _oddDiagnosticPosition = Position;
    }

    private void ModOddballInput()
    {
        if (_scene.GameState.Mode != GameMode.Oddball) return;
        if (_oddMeleeCooldown > 0) _oddMeleeCooldown--;
        if (!ModOddballCarrying) { _oddMeleeBuffer = 0; return; }
        bool drop = Controls.Zoom.IsPressed || (!IsBot && Controls.Morph.IsPressed)
            || Controls.NextWeapon.IsPressed || Controls.PrevWeapon.IsPressed || CurrentWeapon != _oddWeapon;
        if (drop)
        {
            OctolithFlag!.OddballDrop(); if(!MphRead.Mods.Network.NetObjectiveSync.IsClient(_scene)) Controls.Zoom.IsPressed = false; _oddMeleeBuffer = 0; return;
        }
        if (IsBot) { Controls.Morph.IsPressed = false; Controls.Morph.IsDown = false; }
        EquipInfo.ChargeLevel = 0; Flags2 &= ~PlayerFlags2.Shooting;
        // A short early-tap buffer prevents quick taps disappearing in recovery.
        // Holding Fire repeats at the same fixed limit; neither path blocks movement/aim.
        if (Controls.Shoot.IsPressed) _oddMeleeBuffer = 6;
        else if (_oddMeleeBuffer > 0) _oddMeleeBuffer--;
        if (Health > 0 && (Controls.Shoot.IsDown || _oddMeleeBuffer > 0 || IsBot)
            && _oddMeleeCooldown == 0)
        { ModOddballMelee(); _oddMeleeBuffer = 0; }
    }

    public bool ModOddballMelee()
    {
        if (!ModOddballCarrying || _oddMeleeCooldown != 0 || Health == 0 || _frozenTimer > 0
            || _scene.GameState.Oddball?.CanPickUp != true) return false;
        _oddMeleeCooldown = OddballMeleeIntervalTicks;
        ModOddballSwingCount++;
        PlayerEntity? victim = null; float nearest = 2.35f * 2.35f;
        Vector3 forward = _aimVec.LengthSquared > .001f ? _aimVec.Normalized() : FacingVector;
        foreach (var target in _scene.GetPlayerEntities())
        {
            if (target == this || target.Health == 0 || target.TeamIndex == TeamIndex
                || !target.LoadFlags.TestFlag(LoadFlags.Active)) continue;
            Vector3 between = target.Position.AddY(.9f) - Position.AddY(.9f);
            float distance = between.LengthSquared;
            if (distance > nearest || distance < .00001f || Vector3.Dot(forward, between.Normalized()) < .45f) continue;
            CollisionResult wall = default;
            if (CollisionDetection.CheckBetweenPoints(Position.AddY(.9f), target.Position.AddY(.9f), TestFlags.None, _scene, ref wall)) continue;
            nearest = distance; victim = target;
        }
        if (victim == null) return false;
        Vector3 behind = Position - victim.Position; behind.Y = 0;
        Vector3 facing = victim.ModGunVector; facing.Y = 0;
        bool back = behind.LengthSquared > .001f && facing.LengthSquared > .001f
            && Vector3.Dot(facing.Normalized(), behind.Normalized()) < -.6f;
        // Native multiplayer spawns at one tank minus one (normally 99),
        // while HealthMax permits two tanks (199). Half the cap would wrongly
        // one-shot an ordinary fresh spawn. Bonus health remains meaningful.
        int damage = back ? victim.Health : Math.Max(1, victim.Values.EnergyTank / 2);
        if(!MphRead.Mods.Network.NetObjectiveSync.IsClient(_scene)) victim.TakeDamage(damage, DamageFlags.NoDmgInvuln, null, this);
        if (!Headless.Active) _soundSource.PlayFreeSfx(SfxId.SHOTGUN_BREAK_FREEZE);
        return true;
    }

    public void ModOddballRoundRespawn()
    {
        OctolithFlag?.OddballDrop();
        var point = GetRespawnPoint();
        if (point != null) Spawn(point.Position, point.FacingVector, point.UpVector, point.NodeRef, respawn: true);
    }

    private void ModDrawOddballViewModel(Matrix4 transform)
    {
        float swing = _oddMeleeCooldown > 0 ? MathF.Sin(_oddMeleeCooldown * MathF.PI / OddballMeleeIntervalTicks) * .18f : 0;
        Vector3 right = transform.Row0.Xyz, up = transform.Row1.Xyz, forward = transform.Row2.Xyz;
        Vector3 origin = transform.Row3.Xyz + forward * (.86f + swing) - up * .23f + right * .10f;
        _oddViewSkull.Draw(_scene, origin, right, up, -forward, .34f);
    }

}
