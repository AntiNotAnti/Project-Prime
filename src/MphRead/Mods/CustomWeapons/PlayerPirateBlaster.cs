using System;
using MphRead.Formats;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    internal int ModPirateUa => _ammo[UA];
    internal void ModGrantPirateUa() => _ammo[UA] = Math.Min(60, _ammoMax[UA]);
    internal bool ModSpendPirateUa(TrooperPirateBlasterRuntime owner, int amount)
    {
        if (!ReferenceEquals(_scene.PirateBlaster, owner) || !owner.Available(this) || amount != 5 || _ammo[UA] < amount) return false;
        _ammo[UA] -= amount; return true;
    }
    internal void ModRefundPirateUa(TrooperPirateBlasterRuntime owner, int amount)
    {
        if (ReferenceEquals(_scene.PirateBlaster, owner) && owner.Available(this) && amount == 5)
            _ammo[UA] = Math.Min(_ammoMax[UA], _ammo[UA] + amount);
    }
    internal bool ModPirateCanUse => LoadFlags.TestFlag(LoadFlags.Active) && ModIsInPlay && _health > 0
        && !Flags2.TestFlag(PlayerFlags2.Spectating) && _frozenTimer == 0 && !_field6D0
        && !IsAltForm && !IsMorphing && !IsUnmorphing && !ScanVisor && AttachedEnemy == null
        && _field35C == null && !Flags2.TestAny(PlayerFlags2.BipedLock | PlayerFlags2.BipedStuck)
        && GunAnimation != GunAnimation.UpDown && !ModCustomCycleMenuDown
        && !(IsMainPlayer && _scene.CameraSequences.Current?.BlockInput == true)
        && !_scene.GameState.MenuPause && !_scene.GameState.DialogPause
        && _scene.GameState.MatchState == MatchState.InProgress;
    internal bool ModTryPirateAim(out Vector3 origin, out Vector3 direction)
    {
        origin = _muzzlePos; direction = _aimPosition - origin;
        if (!SpatialFinite(origin) || !SpatialDirection(direction)) return false;
        if (_disruptedTimer > 0)
        {
            direction.X += Fixed.ToFloat((int)_scene.Random.GetRandomInt2(24576) - 12288);
            direction.Y += Fixed.ToFloat((int)_scene.Random.GetRandomInt2(24576) - 12288);
            direction.Z += Fixed.ToFloat((int)_scene.Random.GetRandomInt2(24576) - 12288);
        }
        if (!SpatialDirection(direction)) return false;
        direction = direction.Normalized(); return true;
    }
    internal void ModPirateShotCue()
    {
        ModReleaseSpawnProtection();
        ModControllerFeedback(Mods.Input.GamepadFeedback.Fire);
        _timeSinceShot = 0; Flags1 |= PlayerFlags1.ShotUncharged;
        if (IsMainPlayer) HudOnFiredShot();
        PlayBeamShotSfx(BeamType.PowerBeam, false, false, false, 0);
    }
    internal void ModPirateFallback() { if (CurrentWeapon != BeamType.PowerBeam) TryEquipWeapon(BeamType.PowerBeam); }
    private bool _pirateCyclePending;
    private ulong _pirateCycleFrame;
    private WeaponOwnerContext _pirateCycleOwner;
    private CustomCycleTarget _pirateCycleTarget;
    internal CustomCycleTarget ModStagePirateCycle(bool selected, bool hasAmmo)
    {
        ushort eligible = 0;
        for (int i = 0; i < _weaponOrder.Length; i++) if (CanCycleToWeapon(_weaponOrder[i])) eligible |= (ushort)(1 << i);
        _pirateCycleTarget = CustomWeaponCycle.Resolve(_weaponOrder, eligible, CurrentWeapon, selected, hasAmmo,
            Controls.ScrollAllWeapons, Controls.NextWeapon.IsPressed);
        _pirateCyclePending = true; _pirateCycleFrame = _scene.FrameCount;
        _pirateCycleOwner = _scene.PirateBlaster!.Owner;
        return _pirateCycleTarget;
    }
    private bool ModApplyPirateCycle()
    {
        if (!_pirateCyclePending || _pirateCycleFrame != _scene.FrameCount) return false;
        _pirateCyclePending = false;
        if (_scene.PirateBlaster is {} owner && owner.Available(this) && owner.Owner == _pirateCycleOwner
            && !_pirateCycleTarget.Custom && CurrentWeapon != _pirateCycleTarget.Native) TryEquipWeapon(_pirateCycleTarget.Native);
        return true;
    }
    private bool ModPirateSelected => _scene.PirateBlaster?.Selected(this) == true;
    private void ModProcessPirateMarks()
    {
        if (_scene.PirateBlaster is not {} owner || !owner.Available(this)) return;
        foreach (var other in _scene.Players.Items)
            if (owner.IsMarked(this, other)) AddLocatorInfo(other.Position + Vector3.UnitY * .75f,
                _playerLocator, new ColorRgb(31, 12, 4), stableId: other.SlotIndex);
    }
}
