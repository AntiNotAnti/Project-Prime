using System;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private readonly uint[] _chamberShots = new uint[16];
    private int _chamberShotCursor;
    private uint _lastChamberLocalShotFrame;

    internal void ApplyGunGameLoadout(bool force = false)
    {
        if (_scene.GameState.Mode != GameMode.GunGame || Health == 0) return;
        BeamType weapon = GunGameRules.Weapon(_scene.GameState.Points[SlotIndex]);
        if (!force && CurrentWeapon == weapon && EquipInfo.InfiniteAmmo && _availableWeapons[weapon]) return;
        _availableWeapons.ClearAll(); _availableCharges.ClearAll();
        _availableWeapons[weapon] = _availableCharges[weapon] = true;
        _weaponSlots[0] = weapon; _weaponSlots[1] = _weaponSlots[2] = BeamType.None;
        _ammo[UA] = 99; _ammo[Missiles] = 0;
        EquipInfo.InfiniteAmmo = true;
        EquipInfo.ChargeLevel = 0;
        PreviousWeapon = weapon;
        TryEquipWeapon(weapon, silent: true);
    }

    internal void ApplySpawnLoadoutModifiers()
    {
        Array.Clear(_chamberShots); _chamberShotCursor = 0; _lastChamberLocalShotFrame = 0;
        var state = _scene.GameState;
        var host = _scene.Services.PlayerReplication;
        if (!host.Active) state.SpawnOrdinals[SlotIndex]++;
        if (!state.Fiesta && !state.OneInTheChamber) return;
        BeamType first, second;
        if (state.OneInTheChamber)
        {
            first = BeamType.Imperialist; second = BeamType.PowerBeam;
        }
        else
        {
            ushort match;
            uint life;
            if (_scene.Services is ReplaySceneServices replay)
            {
                match = replay.State.Match?.MatchId ?? 0;
                life = host.TryGetState(SlotIndex, out var recorded) ? (uint)recorded.LifeId : 0u;
            }
            else
            {
                match = NetSession.Active ? NetSession.CurrentMatchId : (ushort)0;
                life = NetSession.Active ? NetPlayerLifecycle.Get(SlotIndex) : (uint)state.SpawnOrdinals[SlotIndex];
            }
            (first, second) = SpawnLoadoutRules.Fiesta(match, life, SlotIndex, state.NoImperialist);
        }
        _availableWeapons.ClearAll(); _availableCharges.ClearAll();
        _availableWeapons[first] = _availableWeapons[second] = true;
        _availableCharges[first] = _availableCharges[second] = true;
        _weaponSlots[0] = first; _weaponSlots[1] = second; _weaponSlots[2] = BeamType.None;
        EquipInfo.InfiniteAmmo = false; EquipInfo.ChargeLevel = 0;
        _ammo[UA] = state.OneInTheChamber ? ChamberShotCost : _ammoMax[UA];
        _ammo[Missiles] = 0;
        PreviousWeapon = first;
        TryEquipWeapon(first, silent: true);
    }

    private int ChamberShotCost => Math.Max(1, (int)_scene.WeaponRules[(int)BeamType.Imperialist].AmmoCost);

    internal void AwardChamberShot()
    {
        if (!_scene.GameState.OneInTheChamber || Health == 0) return;
        _ammo[UA] = Math.Min(ushort.MaxValue, _ammo[UA] + ChamberShotCost);
        TryEquipWeapon(BeamType.Imperialist, silent: true);
    }

    private void UpdateChamberFallback()
    {
        if (_scene.GameState.OneInTheChamber && Health > 0 && CurrentWeapon == BeamType.Imperialist
            && _ammo[UA] < ChamberShotCost) TryEquipWeapon(BeamType.PowerBeam, silent: true);
    }

    internal void NoteChamberShot()
    {
        if (!_scene.GameState.OneInTheChamber || CurrentWeapon != BeamType.Imperialist) return;
        _lastChamberLocalShotFrame = _scene.Services.PlayerReplication.Frame;
        if (_scene.Services.PlayerReplication.IsAuthority)
        {
            _chamberShots[_chamberShotCursor] = NetFireEvents.ActiveShotId(this);
            _chamberShotCursor = (_chamberShotCursor + 1) % _chamberShots.Length;
        }
    }

    internal bool KnowsChamberShot(uint shotId) => shotId != 0 && Array.IndexOf(_chamberShots, shotId) >= 0;

    internal void ApplyChamberAmmo(ushort ammo, uint acknowledgedFrame)
    {
        if (!_scene.GameState.OneInTheChamber) return;
        if (!_scene.Services.IsReplica && IsMainPlayer && _lastChamberLocalShotFrame != 0
            && NetLifecycleTracker.Newer(_lastChamberLocalShotFrame, acknowledgedFrame)) return;
        _ammo[UA] = ammo; _ammo[Missiles] = 0;
        UpdateChamberFallback();
    }
}
