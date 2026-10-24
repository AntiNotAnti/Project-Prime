using System;
using MphRead.Mods.HeroModes;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
namespace MphRead.Entities;

public partial class PlayerEntity
{
    private ushort _ctzFreezeCooldown, _ctzAuraRemaining, _ctzTargetTicks;
    private byte _ctzAuraHealsRemaining;
    private sbyte _ctzHealedTarget=-1;
    private ushort _ctzTargetGeneration,_ctzTargetLife;
    private float _ctzHammerRemainder;
    private ulong _ctzSupportTick=ulong.MaxValue;
    private ushort _ctzOccupantGeneration;
    private bool _ctzHaveOccupant;
    private void ModTrackCtzOccupant()
    {
        ushort generation=NetPlayerLifecycle.Generation(SlotIndex);
        if(_ctzHaveOccupant && generation!=_ctzOccupantGeneration)
        {
            // A replacement player must not inherit a departed occupant's cooldown or healing.
            _ctzFreezeCooldown=0;_ctzAuraRemaining=0;_ctzAuraHealsRemaining=0;_ctzAuraHealer=null;
            _ctzTargetTicks=0;_ctzHealedTarget=-1;_ctzHammerRemainder=0;
            _ctzSupportTick=ulong.MaxValue;
        }
        _ctzOccupantGeneration=generation;_ctzHaveOccupant=true;
    }
    internal ushort ModCtzFreezeCooldown=>_ctzFreezeCooldown;
    internal ushort ModCtzAuraRemaining=>_ctzAuraRemaining;
    internal bool ModCtzShowingHealedAlly => HeroRules.Active(_scene.GameState.Mode) && CtzRules.IsSupportHunter(Hunter)
        && _ctzTargetTicks>0 && _opponentIndex==_ctzHealedTarget && (uint)_ctzHealedTarget<8
        && _scene.Players.Items[_ctzHealedTarget].TeamIndex==TeamIndex
        && _scene.Players.Items[_ctzHealedTarget].Health>0
        && (!NetSession.Active || NetPlayerLifecycle.Matches(_ctzHealedTarget,_ctzTargetGeneration,_ctzTargetLife));
    internal bool ModCtzChargeBlocked=>HeroRules.Active(_scene.GameState.Mode) && Hunter==Hunter.Noxus
        && CurrentWeapon==BeamType.Judicator && _ctzFreezeCooldown>0;
    internal void ModStartCtzFreezeCooldown()
    { ModTrackCtzOccupant();_ctzFreezeCooldown=CtzRules.NoxusFreezeCooldownTicks; }
    internal void ModResetCtzLifeSupport()
    {
        ModTrackCtzOccupant();
        _ctzAuraRemaining=0;_ctzAuraHealsRemaining=0;_ctzAuraHealer=null;_ctzHammerRemainder=0;
        _ctzTargetTicks=0;_ctzHealedTarget=-1;
        // The release-based cooldown intentionally survives death, respawn and hunter reselection.
    }
    internal uint ModCtzWholeDamage(BeamProjectileEntity beam,float damage)
    {
        if(!float.IsFinite(damage) || damage<=0) return 0;
        if(HeroRules.Active(_scene.GameState.Mode) && beam.Beam==BeamType.Battlehammer
            && CtzSpawnRooms.Owner(beam)?.Hunter==Hunter.Weavel)
        {
            float exact=damage+_ctzHammerRemainder;
            uint whole=(uint)Math.Clamp(exact,0,int.MaxValue);
            _ctzHammerRemainder=exact-whole;
            return whole;
        }
        return (uint)Math.Clamp(damage,0,int.MaxValue);
    }
    internal int ModGainCtzHealth(int amount, PlayerEntity? healer = null, ushort? sourceGeneration = null)
    {
        if(!CtzHealing.IsAuthority(_scene) || _health<=0 || !ModIsInPlay || amount<=0) return 0;
        int turret=Flags2.TestFlag(PlayerFlags2.Halfturret) ? Halfturret?.Health ?? 0 : 0;
        int capacity=HeroRules.Definition(Hunter).MaxHealth;
        int gain=Math.Min(amount,Math.Max(0,capacity-_health-turret));
        // Credit the controlled body; do not let native split-form healing create a second health pool.
        gain=Math.Min(gain,Math.Max(0,_healthMax-_health));
        _health+=gain;
        ModRecordCtzHealing(gain, healer, sourceGeneration ?? (healer == null ? (ushort)0 : NetPlayerLifecycle.Generation(healer.SlotIndex)));
        return gain;
    }
    internal void ModStartCtzHealingAura(PlayerEntity? healer = null)
    {
        if(!CtzHealing.IsAuthority(_scene) || _health<=0 || _ctzAuraRemaining>0) return;
        _ctzAuraRemaining=CtzRules.HealingAuraTicks;_ctzAuraHealsRemaining=CtzRules.SpireAuraPulses;
        _ctzAuraHealer=healer;_ctzAuraHealerGeneration=healer == null ? (ushort)0 : NetPlayerLifecycle.Generation(healer.SlotIndex);
    }
    internal void ModFocusCtzTeammate(PlayerEntity target)
    {
        if(!HeroRules.Active(_scene.GameState.Mode) || !CtzRules.IsSupportHunter(Hunter)
            || target==this || target.TeamIndex!=TeamIndex || target.Health<=0 || !target.ModIsInPlay) return;
        _ctzHealedTarget=(sbyte)target.SlotIndex;
        _ctzTargetTicks=Hunter==Hunter.Spire ? CtzRules.MaximumHealedAllyRevealTicks : CtzRules.HealedAllyRevealTicks;
        _ctzTargetGeneration=NetPlayerLifecycle.Generation(target.SlotIndex);
        _ctzTargetLife=NetPlayerLifecycle.Get(target.SlotIndex);
        if(IsMainPlayer)
        {
            UpdateOpponent(target.SlotIndex);
            _opponentHealthbarTimer=_ctzTargetTicks/60f; // Spire's panel survives all five aura pulses.
        }
    }
    internal void ModTickCtzSupport()
    {
        ModTrackCtzOccupant();
        if(!HeroRules.Active(_scene.GameState.Mode) || _ctzSupportTick==_scene.FrameCount) return;
        _ctzSupportTick=_scene.FrameCount;
        if(_ctzFreezeCooldown>0) _ctzFreezeCooldown--;
        if(_ctzTargetTicks>0) _ctzTargetTicks--;
        if(_health<=0 || !ModIsInPlay) { _ctzAuraRemaining=0;_ctzAuraHealsRemaining=0;return; }
        if(_ctzAuraRemaining>0)
        {
            _ctzAuraRemaining--;
            if(_ctzAuraRemaining%(CtzRules.HealingAuraTicks/CtzRules.SpireAuraPulses)==0 && _ctzAuraHealsRemaining>0)
            { _ctzAuraHealsRemaining--;ModGainCtzHealth(CtzRules.SpireHealPerPulse, _ctzAuraHealer, _ctzAuraHealerGeneration); }
        }
    }
    internal void ModEnforceCtzSpawnRoom(Vector3 before)
    {
        if(!CtzSpawnRooms.BlocksMovement(this,before,Position)) return;
        // Owning clients and native bots use the same local collision restriction.
        // Network intent validation rejects illegal room positions without introducing owner rollback.
        ModPlaceAt(before); Speed=Vector3.Zero;
    }
    private void ModDrawCtzHealingAura(Vector3 position)
    {
        if(!HeroRules.Active(_scene.GameState.Mode) || _ctzAuraRemaining==0 || Health<=0) return;
        float time=(float)_scene.ElapsedTime;
        for(int i=0;i<6;i++)
        {
            float angle=time*2+i*MathF.Tau/6;
            var p=position+new Vector3(MathF.Cos(angle)*.65f,.25f+((time*.4f+i/6f)%1)*1.1f,MathF.Sin(angle)*.65f);
            _scene.AddSingleParticle(SingleType.Death,p,new Vector3(.1f,1f,.4f),.55f,.06f,cosmeticTint:true);
        }
    }
    internal CtzSupportState ModCaptureCtzSupport()
    {
        ModTrackCtzOccupant();
        return new((byte)SlotIndex,NetPlayerLifecycle.Generation(SlotIndex),
        NetPlayerLifecycle.Get(SlotIndex),_ctzFreezeCooldown,_ctzAuraRemaining,_ctzAuraHealsRemaining,
        _ctzHealedTarget,_ctzTargetGeneration,_ctzTargetLife,_ctzTargetTicks);
    }
    internal void ModApplyCtzSupport(CtzSupportState state)
    {
        if(state.Slot!=SlotIndex || !NetPlayerLifecycle.Matches(SlotIndex,state.Generation,state.Life)) return;
        ModTrackCtzOccupant();
        _ctzFreezeCooldown=state.FreezeCooldown;_ctzAuraRemaining=state.AuraRemaining;_ctzAuraHealsRemaining=state.AuraPulses;
        _ctzHealedTarget=state.HealTarget;_ctzTargetTicks=state.TargetTicks;
        _ctzTargetGeneration=state.TargetGeneration;_ctzTargetLife=state.TargetLife;
        if(IsMainPlayer && CtzRules.IsSupportHunter(Hunter) && _ctzTargetTicks>0 && (uint)_ctzHealedTarget<8
            && NetPlayerLifecycle.Matches(_ctzHealedTarget,_ctzTargetGeneration,_ctzTargetLife))
        {
            var target=_scene.Players.Items[_ctzHealedTarget];
            if(target.TeamIndex==TeamIndex && target.Health>0 && target.ModIsInPlay)
            {
                UpdateOpponent(_ctzHealedTarget);
                _opponentHealthbarTimer=_ctzTargetTicks/60f;
            }
        }
    }
}
