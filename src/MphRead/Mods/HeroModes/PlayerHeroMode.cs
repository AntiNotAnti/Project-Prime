using MphRead.Mods.Render.Hud;
using System;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Mods.HeroModes;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
namespace MphRead.Entities;
public partial class PlayerEntity
{
    private ulong _ctzKnockWindow;
    private Vector3 _ctzKnockUsed;
    internal Vector3 ModBoundCtzKnockback(Vector3 incoming)
    {
        if (!CtzRules.Finite(incoming)) return Vector3.Zero;
        if (_scene.FrameCount < _ctzKnockWindow || _scene.FrameCount-_ctzKnockWindow >= CtzRules.BurstPeriodTicks)
        { _ctzKnockWindow=_scene.FrameCount; _ctzKnockUsed=Vector3.Zero; }
        var total=_ctzKnockUsed+incoming;
        total.Y=Math.Clamp(total.Y,-.12f,.12f);
        if(total.LengthSquared>.45f*.45f)total=Vector3.Normalize(total)*.45f;
        Vector3 allowed=total-_ctzKnockUsed;_ctzKnockUsed=total;
        return allowed;
    }

    internal void ModChangeHeroWhileDead(Hunter hunter)
    {
        if (!HeroRules.Active(_scene.GameState.Mode) || Health > 0 || Hunter == hunter) return;
        ushort countdown = _respawnTimer;
        var deadTime = _timeSinceDead;
        var load = LoadFlags;
        ModCancelHeroFire();
        ModPrepareHunterResources(hunter);
        ModSetHunter(hunter);
        Initialize();
        // Reinitializing graphics must not grant a life, heal, or reset the authoritative countdown.
        _health = 0; _healthMax = HeroRules.Definition(hunter).MaxHealth;
        _respawnTimer = countdown; _timeSinceDead = deadTime; LoadFlags = load;
    }

    internal bool ModApplyHeroLoadout()
    {
        if (!HeroRules.Active(_scene.GameState.Mode)) return false;
        var hero = HeroRules.Definition(Hunter);
        _healthMax = _health = hero.MaxHealth;
        ModResetCtzLifeSupport();
        _ctzKnockUsed=Vector3.Zero; _ctzKnockWindow=_scene.FrameCount;
        var weapon = HeroRules.Primary(Hunter);
        _availableWeapons.ClearAll(); _availableCharges.ClearAll();
        _availableWeapons[weapon] = _availableCharges[weapon] = true;
        _weaponSlots[0] = weapon; _weaponSlots[1] = _weaponSlots[2] = BeamType.None;
        _ammo[UA] = _ammo[Missiles] = 0;
        EquipInfo.InfiniteAmmo = true; EquipInfo.ChargeLevel = 0;
        PreviousWeapon = weapon;
        TryEquipWeapon(weapon, silent: true);
        return true;
    }

    internal void ModCancelHeroFire()
    {
        if (EquipInfo.ChargeLevel > 0) StopBeamChargeSfx(CurrentWeapon);
        EquipInfo.ChargeLevel = 0;
        if (EquipInfo.Zoomed) UpdateZoom(false);
        _chargeEffect?.SetDrawEnabled(false);
        _muzzleEffect?.SetDrawEnabled(false);
        // Cancel delayed, not-yet-launched rounds only; already flying projectiles remain alive.
        foreach (var beam in EquipInfo.Beams)
            if (beam.Owner == this && beam.Age < 0) beam.Lifespan = 0;
    }


    public bool ModZoneSpawnProtected => _spawnInvulnTimer > 0 || ModMatchSpawnProtectionActive;
    internal void ModZoneRoundRespawn()
    {
        ModCancelHeroFire();
        var point=GetRespawnPoint();
        if(point!=null) Spawn(point.Position,point.FacingVector,point.UpVector,point.NodeRef,respawn:true);
    }
    private void ModDrawCtzHud()
    {
        if (ShowScoreboard || _scene.GameState.MenuPause || _scene.GameState.MatchState != MatchState.InProgress) return;
        var s=_scene.CaptureZone.State;
        // Score and round wins belong to the real scoreboard, never a background overlay.
        string status=CtzPresentation.ObjectiveStatus(s);
        if (status.Length>0)
        {
            using (UseHudLayout(10, 0, 0)) DrawText2D(128,39,Align.Center,0,status,scale:.45f);
        }
        if (CtzPresentation.ShowRoundStartBanner(s))
        {
            // A transient center-screen announcement, not an uneditable permanent HUD element.
            DrawText2D(128,92,Align.Center,0,CtzPresentation.ModeTitle,scale:.85f);
        }
        if (Health==0 && _respawnTimer>0)
        {
            using (UseHudLayout(Array.IndexOf(HudProfileDefaults.ElementIds, "match.respawn"), 128, 150))
                DrawText2D(128,150,Align.Center,0,$"RESPAWN IN {(_respawnTimer+59)/60}",scale:.6f);
        }
        if (Hunter == Hunter.Noxus)
        {
            using (UseHudLayout(Array.IndexOf(HudProfileDefaults.ElementIds, "combat.abilityCooldown"), 128, 176))
                DrawText2D(128,176,Align.Center,0,ModCtzFreezeCooldown > 0
                    ? $"FREEZE READY IN {(ModCtzFreezeCooldown+59)/60}s" : "FREEZE READY",scale:.45f);
        }
    }

    private void ModDrawCtzClock()
    {
        // The scoreboard has its own clock in its header. Gameplay honors the saved match.timer layout.
        if (ShowScoreboard) return;
        using var layout=UseHudLayout(6,128,10);
        DrawText2D(128,10,Align.Center,0,"ROUND TIME",scale:.55f);
        DrawText2D(128,20,Align.Center,0,CtzPresentation.RoundTime(_scene.CaptureZone.State),scale:.65f);
    }
}
