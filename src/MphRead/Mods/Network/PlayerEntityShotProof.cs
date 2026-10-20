using MphRead.Mods.Network;
namespace MphRead.Entities;
public partial class PlayerEntity
{
    // Called only after the engine rebuilds the spawn loadout. Never sample these
    // fields after ApplyIntent, which may contain client-reported inventory.
    private void ModSeedShotProof()
    {
        ushort weapons=0,charges=0;
        for(int i=0;i<9;i++)
        {
            if(_availableWeapons[(BeamType)i])weapons|=(ushort)(1<<i);
            if(_availableCharges[(BeamType)i])charges|=(ushort)(1<<i);
        }
        NetShotProof.Seed(this,new ShotProofSource(weapons,charges,_ammo[0],_ammo[1],
            EquipInfo.InfiniteAmmo,Hunter,IsPrimeHunter,DoubleDamage,
            !_scene.GameState.BalancedMode && !_scene.GameState.OneInTheChamber
                && _scene.GameState.Mode!=GameMode.GunGame && !Mods.EnhancedHunters.EnhancedHunters.Enabled(this),
            AmmoMax0: _ammoMax[0], AmmoMax1: _ammoMax[1]));
    }
    // This snapshot is valid only at the native pre-TakeDamage seam. The
    // post-step historical profile has a different phase and cannot substitute.
    internal FastNativeDamageGates ModCaptureFastDamageGates(uint frame)
    {
        var game = _scene.GameState;
        return new(ShotKey.For(SlotIndex, 0), frame, FastDamageCapturePhase.NativePreDamage,
            !_scene.Services.IsReplica && game.Multiplayer && NetSession.IsAuthority,
            _health, ModIsInPlay && LoadFlags.TestFlag(LoadFlags.Active)
                && !Flags2.TestFlag(PlayerFlags2.Spectating),
            !IsAltForm && !ModCollisionIsAltForm && !IsMorphing && !IsUnmorphing, IsBot,
            _spawnInvulnTimer > 0, ModMatchSpawnProtectionActive, _damageInvulnTimer > 0,
            IsMainPlayer && _scene.CameraSequences.Current?.BlockInput == true,
            _scene.Services.SuppressDamage, _scene.AimTrainer != null,
            _frozenTimer > 0 || _burnTimer > 0 || _disruptedTimer > 0,
            Mods.EnhancedHunters.EnhancedHunters.Enabled(this), game.Teams, game.FriendlyFire,
            game.InstaGib, game.OneInTheChamber, TeamIndex, game.Mode, game.TeamCount);
    }
    internal bool ModCaptureFastDamageProfile(uint frame, out TimedVictimDamageProfile profile)
    {
        profile = default;
        var game = _scene.GameState;
        if (!NetTimedDamageProfile.TryPackEffectiveness(BeamEffectiveness, out uint packed)
            || (uint)game.DamageLevel >= Metadata.DamageLevels.Count
            || (uint)SlotIndex >= NetSession.SlotDamageReduction.Length) return false;
        profile = new(ShotKey.For(SlotIndex, 0), packed, Metadata.DamageLevels[game.DamageLevel],
            NetSession.SlotDamageReduction[SlotIndex], Hunter, game.BalancedMode,
            IsMorphing || IsUnmorphing, ModBalancedDisruptImmunityTimer, true, frame);
        return true;
    }

}
