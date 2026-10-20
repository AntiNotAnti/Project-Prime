using System;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

internal enum HeadHitDomain : byte { Native=1, Custom=2, Local=3 }
// Tagged cosmetic keys never convert a custom payment into a native ShotKey.
internal readonly record struct HeadHitEvent(HeadHitDomain Domain, ShotKey Native,
    CustomPaymentKey Custom, WorldBootstrapIdentity Bootstrap, ulong Local, ushort DamageEvent)
{
    internal bool IsValid => Domain switch
    {
        HeadHitDomain.Native => Native.AuthorityEpoch!=0&&Native.MatchId!=0&&Native.ShooterSlot is >=0 and <8
            &&Native.Generation!=0&&Native.LifeId!=0&&Native.ShotId!=0&&DamageEvent!=0&&Custom==default&&Bootstrap==default&&Local==0,
        HeadHitDomain.Custom => Custom.IsValid&&Custom.Channel==CustomImpactChannel.Body
            &&CustomWeaponProtocol.ValidBootstrap(Bootstrap)&&Bootstrap.Start.MatchId==Custom.Action.Owner.MatchId
            &&Bootstrap.Start.AuthorityEpoch==Custom.Action.Owner.AuthorityEpoch&&Bootstrap.SlotGeneration==Custom.Action.Owner.Generation
            &&DamageEvent!=0&&Native==default&&Local==0,
        HeadHitDomain.Local => Local!=0&&Native==default&&Custom==default&&Bootstrap==default&&DamageEvent==0,
        _=>false
    };
}
internal readonly record struct HeadHitVictim(long Scene,object Actor,byte Slot,ushort Match,
    ulong Epoch,ushort Generation,uint Life,long Timeline)
{
    internal bool IsValid=>Scene>0&&Actor!=null&&Slot<8&&Life!=0
        &&(Match==0?Epoch==0&&Generation==0:Epoch!=0&&Generation!=0&&Life<=ushort.MaxValue);
    internal bool Matches(HeadHitVictim other)=>Scene==other.Scene&&ReferenceEquals(Actor,other.Actor)
        &&Slot==other.Slot&&Match==other.Match&&Epoch==other.Epoch&&Generation==other.Generation
        &&Life==other.Life&&Timeline==other.Timeline;
}
internal readonly record struct HeadHitFact(HeadHitVictim Victim,HeadHitEvent Event,uint ResolveWorld,Vector3 Direction)
{
    internal static bool TryNative(HeadHitVictim victim,in ReplayShotFact fact,out HeadHitFact result)
    {
        result=default;
        if(!victim.IsValid||victim.Match!=fact.MatchId||victim.Epoch!=fact.AuthorityEpoch
            ||victim.Slot!=fact.VictimSlot||victim.Generation!=fact.VictimGeneration||victim.Life!=fact.VictimLifeId
            ||!fact.Headshot||fact.Lethal||fact.Damage==0||fact.HealthAfter==0
            ||fact.Weapon>(byte)BeamType.OmegaCannon||!float.IsFinite(fact.ImpactPoint.X)
            ||!float.IsFinite(fact.ImpactPoint.Y)||!float.IsFinite(fact.ImpactPoint.Z)
            ||(fact.Flags&ReplayShotFactFlags.HalfturretTarget)!=0)return false;
        var key=new HeadHitEvent(HeadHitDomain.Native,new(fact.AuthorityEpoch,fact.MatchId,fact.ShooterSlot,
            fact.ShooterGeneration,fact.ShooterLifeId,fact.ShotId),default,default,0,fact.DamageEventId);
        if(!key.IsValid)return false;
        result=new(victim,key,fact.ResolveTick,Vector3.Zero);return true;
    }
    internal static bool TryCustom(HeadHitVictim victim,WorldBootstrapIdentity bootstrap,in CustomContactOutcome hit,
        Vector3 direction,out HeadHitFact result)
    {
        result=default;var key=hit.Contact.Key;
        if(!victim.IsValid||!hit.Contact.IsValid||!hit.Health.IsValid||!hit.Contact.Headshot
            ||hit.Disposition!=CustomContactDisposition.Canonical||hit.Health.BodyRemoved<=0||hit.Health.BodyAfter<=0
            ||key.Channel!=CustomImpactChannel.Body||victim.Match!=key.Action.Owner.MatchId
            ||victim.Epoch!=key.Action.Owner.AuthorityEpoch||victim.Slot!=key.Victim.Slot
            ||victim.Generation!=key.Victim.Generation||victim.Life!=key.Victim.Life)return false;
        var id=new HeadHitEvent(HeadHitDomain.Custom,default,key,bootstrap,0,hit.Health.DamageEventId);
        if(!id.IsValid)return false;
        result=new(victim,id,hit.AuthorityFrame,direction);return true;
    }
}
internal readonly record struct HeadHitPose(float Amount,Vector3 Direction);

// Fixed-step cosmetic state. Dedupe consumes even suppressed events; a quality
// toggle cannot replay a head hit. A full bounded ledger refuses new events.
internal sealed class HeadHitTrack
{
    internal const uint MaximumAge=18, Retention=120;
    internal const int Capacity=32, Duration=18;
    private struct Seen {internal HeadHitEvent Event;internal uint At;}
    private readonly Seen[] _seen=new Seen[Capacity];
    internal HeadHitVictim Victim {get;private set;}
    private int _age=Duration;
    private uint _resolved,_step;
    private bool _stepped,_active;
    private Vector3 _direction;
    internal void Bind(HeadHitVictim victim)
    {
        if(Victim.Matches(victim))return;
        Victim=victim;Array.Clear(_seen);_age=Duration;_active=_stepped=false;_resolved=0;_direction=default;
    }
    internal void Cancel(){_active=false;_age=Duration;}
    internal bool Offer(in HeadHitFact fact,uint authorityNow,bool eligible,uint? presentationWorld=null)
    {
        if(!Victim.IsValid||!Victim.Matches(fact.Victim)||!fact.Event.IsValid)return false;
        int age=unchecked((int)(authorityNow-fact.ResolveWorld));
        if(age<0||age>MaximumAge)return false;
        int free=-1;
        for(int i=0;i<Capacity;i++)
        {
            if(_seen[i].Event==fact.Event)return false;
            if(!_seen[i].Event.IsValid||unchecked((int)(authorityNow-_seen[i].At))>(int)Retention)free=i;
        }
        if(free<0)return false;
        _seen[free]=new(){Event=fact.Event,At=authorityNow};
        if(!eligible||_active&&unchecked((int)(fact.ResolveWorld-_resolved))<0)return false;
        _resolved=fact.ResolveWorld;_age=age;_active=age<Duration;
        _step=presentationWorld??authorityNow;_stepped=true;
        Vector3 d=fact.Direction;
        _direction=float.IsFinite(d.X)&&float.IsFinite(d.Y)&&float.IsFinite(d.Z)&&d.LengthSquared>1e-8f
            &&float.IsFinite(d.LengthSquared)?d.Normalized():Vector3.Zero;
        return _active;
    }
    internal void Step(uint world,bool eligible)
    {
        if(!eligible)Cancel();
        if(_stepped&&_step==world)return;
        if(_stepped&&unchecked((int)(world-_step))<0){Cancel();_step=world;return;}
        uint elapsed=_stepped?unchecked(world-_step):1;
        _step=world;_stepped=true;
        if(_active){_age+=(int)Math.Min(elapsed,(uint)Duration);if(_age>=Duration)Cancel();}
    }
    internal HeadHitPose Sample(float alpha)
    {
        if(!_active)return default;
        alpha=float.IsFinite(alpha)?Math.Clamp(alpha,0,1):0;
        float age=_age+alpha;
        float amount=age<5?age/5:Math.Clamp((Duration-age)/(Duration-5),0,1);
        amount=amount*amount*(3-2*amount);
        return new(amount,_direction);
    }
}
