using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

[Flags] internal enum CustomRifleCueMask : byte { None=0,Shot=1,Bolt=2,Reload=4 }
internal readonly record struct CustomRifleContext(WeaponOwnerContext Owner,WorldBootstrapIdentity Bootstrap,
    WeaponDigest Manifest,WeaponIdentity Weapon,bool Offline=false)
{
    internal bool IsValid=>Owner.IsValid&&Weapon.Kind==WeaponIdentityKind.Custom&&Weapon.ContentId==RiftlineM7.ContentId
        &&(Offline?Bootstrap==default&&Manifest==default:Manifest.IsValid&&CustomWeaponProtocol.ValidBootstrap(Bootstrap)
            &&Bootstrap.Start.MatchId==Owner.MatchId&&Bootstrap.Start.AuthorityEpoch==Owner.AuthorityEpoch);
}
internal readonly record struct CustomRiflePose(float Kick,float Bolt,float Magazine,float Raise,float Flash);

// Cosmetic clocks only. One owner/bootstrap/manifest/definition per track. A
// baseline restores pose silently; old/reordered shots cannot replay a sound.
internal sealed class CustomRifleTrack
{
    internal CustomRifleContext Context { get; private set; }
    internal bool Selected { get; private set; }
    private uint _shotHigh,_predicted,_reloadEnd;
    private WorldBootstrapIdentity _sourceBootstrap;
    private bool _sourceBound,_reloading;
    private int _fireAge=-1,_reloadAge=-1,_reloadDuration=138,_raiseAge=12;
    internal void Reset()
    {Context=default;Selected=_reloading=_sourceBound=false;_sourceBootstrap=default;_shotHigh=_predicted=_reloadEnd=0;_fireAge=_reloadAge=-1;_raiseAge=12;}
    internal CustomRifleCueMask Synchronize(CustomRifleContext context,in WeaponStateSnapshot state,bool selected,int reloadDuration,bool offline)
    {
        if(!context.IsValid||offline!=context.Offline||state.Owner!=context.Owner||state.Weapon!=context.Weapon){Reset();return 0;}
        bool reload=selected&&state.Phase==CustomWeaponPhase.Reloading;
        if(Context!=context)
        {
            Reset();Context=context;_shotHigh=state.LastAcceptedShotId;Selected=selected;_raiseAge=selected?0:12;
            _reloadDuration=Math.Clamp(reloadDuration,1,3600);_reloading=reload;_reloadEnd=state.ReloadCompleteTick;
            if(reload)_reloadAge=Math.Clamp(_reloadDuration-unchecked((int)(state.ReloadCompleteTick-state.Tick)),0,_reloadDuration);
            return 0;
        }
        var cues=CustomRifleCueMask.None;
        if(selected&&!Selected)_raiseAge=0;
        if(!selected){_fireAge=_reloadAge=-1;_predicted=0;}
        Selected=selected;_reloadDuration=Math.Clamp(reloadDuration,1,3600);
        if(reload&&(!_reloading||_reloadEnd!=state.ReloadCompleteTick))
        {_reloadAge=0;cues|=CustomRifleCueMask.Reload;}
        if(!reload)_reloadAge=-1;
        _reloading=reload;_reloadEnd=state.ReloadCompleteTick;
        if(offline&&selected&&state.LastAcceptedShotId>_shotHigh&&Shot(context.Bootstrap,state.LastAcceptedShotId))cues|=CustomRifleCueMask.Shot;
        return cues;
    }
    private bool Shot(WorldBootstrapIdentity source,uint sequence)
    {
        if(!Selected||sequence==0||sequence<=_shotHigh||_sourceBound&&source!=_sourceBootstrap)return false;
        _sourceBootstrap=source;_sourceBound=true;_shotHigh=sequence;_predicted=0;_fireAge=0;return true;
    }
    internal bool Submitted(CustomRifleContext context,uint sequence,bool canFire)
    {
        if(Context!=context||!canFire||!Shot(context.Bootstrap,sequence))return false;
        _predicted=sequence;return true;
    }
    internal bool AcceptedLaunch(in CustomReplicaLaunch launch)
    {
        var action=launch.Source.Action;
        if(Context.Offline||!Context.IsValid||!action.IsValid||action.Weapon.Value!=10||action.Owner!=Context.Owner||action.Manifest!=Context.Manifest
            ||action.Bootstrap.Start!=Context.Bootstrap.Start||action.Bootstrap.SlotGeneration!=Context.Owner.Generation
            ||action.Kind!=CustomWeaponActionKind.Fire)return false;
        return Shot(action.Bootstrap,action.Sequence);
    }
    internal bool Apply(in CustomPresentationResult result)
    {
        if(Context.Owner!=result.Owner||Context.Bootstrap!=result.Bootstrap||Context.Manifest!=result.Manifest
            ||result.Kind!=CustomWeaponActionKind.Fire||result.Sequence!=_predicted)return false;
        bool cancel=!result.Accepted&&_shotHigh==_predicted;
        if(cancel)_fireAge=-1;
        _predicted=0;return cancel;
    }
    internal CustomRifleCueMask Step()
    {
        var cue=CustomRifleCueMask.None;
        if(_raiseAge<12)_raiseAge++;
        if(_fireAge>=0)
        {if(++_fireAge==16&&Selected)cue=CustomRifleCueMask.Bolt;if(_fireAge>=75)_fireAge=-1;}
        if(_reloadAge>=0&&_reloadAge<_reloadDuration)_reloadAge++;
        return cue;
    }
    internal CustomRiflePose Sample(float alpha)
    {
        alpha=float.IsFinite(alpha)?Math.Clamp(alpha,0,1):0;
        float age=_fireAge<0?100:_fireAge+alpha;
        float bolt=age<8?0:age<16?(age-8)/8:age<24?1:age<34?1-(age-24)/10:0;
        float reload=_reloadAge<0?0:Math.Clamp((_reloadAge+alpha)/_reloadDuration,0,1);
        float magazine=reload<.2f?reload/.2f:reload<.7f?1:Math.Clamp((1-reload)/.3f,0,1);
        return new(Math.Clamp(1-age/6,0,1),bolt,magazine,Math.Clamp(1-(_raiseAge+alpha)/12,0,1),Math.Clamp(1-age/3,0,1));
    }
}
