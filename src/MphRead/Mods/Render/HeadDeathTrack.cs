using System;
using MphRead.Mods.Cosmetics.Death;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

internal readonly record struct HeadDeathPose(bool Active,float Head,float Collapse,float Alpha,int Side,Vector3 Direction);

// Exactly one accepted cause per victim life, including an ordinary body death.
// This never changes HP/death state or emits audio. A fact may precede the HP
// snapshot, but only an actually dead actor can expose a corpse pose.
internal sealed partial class HeadDeathTrack
{
    internal const uint Duration=42, MaximumDuration=144, PendingLifetime=18;
    internal HeadHitVictim Victim {get;private set;}
    internal bool HasCause {get;private set;}
    internal bool BeganDead {get;private set;}
    internal HeadHitEvent Event {get;private set;}
    private bool _allowed,_active;
    private uint _offeredAt,_initialAge,_age;
    private int _side;
    private Vector3 _direction;
    internal void Bind(HeadHitVictim victim)
    {
        if(Victim.Matches(victim))return;
        Victim=victim;HasCause=BeganDead=_allowed=_active=false;Event=default;
        _offeredAt=_initialAge=_age=0;_side=0;_direction=default;
    }
    internal bool Accept(in HeadHitFact fact,uint authorityNow,uint presentationNow,
        bool headshot,bool eligible,bool historical=false)
    {
        if(HasCause||!Victim.IsValid||!Victim.Matches(fact.Victim)||!fact.Event.IsValid)return false;
        int age=unchecked((int)(authorityNow-fact.ResolveWorld));
        if(age<0||age>(historical?MaximumDuration:PendingLifetime))return false;
        HasCause=true;Event=fact.Event;_initialAge=_age=(uint)age;_offeredAt=presentationNow;
        _allowed=headshot&&eligible;_active=false;BeganDead=false;
        var d=fact.Direction;
        _direction=float.IsFinite(d.LengthSquared)&&d.LengthSquared>1e-8f?d.Normalized():Vector3.Zero;
        _side=StableSide(fact);
        return _allowed;
    }
    internal void Cancel(){_allowed=_active=false;}
    internal void Suppress(in HeadHitFact fact)
    {
        if(!HasCause&&Victim.IsValid&&Victim.Matches(fact.Victim)&&fact.Event.IsValid)
        {HasCause=true;Event=fact.Event;Cancel();}
    }
    internal void Step(uint world,bool dead,bool eligible)
    {
        if(!HasCause)return;
        if(!eligible||BeganDead&&!dead){Cancel();return;}
        int delta=unchecked((int)(world-_offeredAt));
        if(delta<0){Cancel();return;}
        _age=(uint)Math.Min(MaximumDuration,(ulong)_initialAge+(uint)delta);
        if(!dead)
        {if((uint)delta>PendingLifetime)Cancel();return;}
        BeganDead=true;_active=_allowed&&_age<MaximumDuration;
    }
    internal HeadDeathPose Sample(float alpha,DeathPresentationDefinition definition)
    {
        if(!_active)return default;
        alpha=float.IsFinite(alpha)?Math.Clamp(alpha,0,1):0;
        float age=_age+alpha;
        if(age>=(definition.WireId==0?Duration:MaximumDuration))return default;
        float alphaBody=age<30?1:Math.Clamp((Duration-age)/12,0,1);
        float kick=age<4?age/4:Math.Clamp((12-age)/8,0,1);
        float collapse=Math.Clamp(age/12,0,1);collapse=collapse*collapse*(3-2*collapse);
        if(definition.WireId!=0)
        {
            // User-selected disappearance/float/shatter keeps its original
            // body lifetime, surface, particles and whole-root pose.
            // Hold the settled knees while the catalog body is visible. Fading
            // this to an upright native pose looked like the corpse stood up
            // before disappearing. Existing HideBodyAt/duration still wins;
            // 144ticks bounds all current effects (longest duration2.2seconds).
            if(definition.PoseStyle==DeathPoseStyle.ShatterFreeze)kick=collapse=0;
            else if(definition.PoseStyle==DeathPoseStyle.Float)collapse*=.15f;
            else if(definition.PoseStyle==DeathPoseStyle.KneelCollapse)collapse*=.25f;
            alphaBody=1;
        }
        return new(true,kick,collapse,alphaBody,_side,_direction);
    }
    private static int StableSide(in HeadHitFact fact)
    {
        // Fixed integer framing, not randomized GetHashCode or simulation RNG.
        ulong hash=14695981039346656037;
        void Add(ulong value){for(int i=0;i<8;i++){hash^=(byte)value;hash*=1099511628211;value>>=8;}}
        var id=fact.Event;var victim=fact.Victim;
        Add((byte)id.Domain);Add(victim.Slot);Add(victim.Match);Add(victim.Epoch);Add(victim.Generation);Add(victim.Life);
        Add(id.DamageEvent);Add(id.Local);
        if(id.Domain==HeadHitDomain.Native)
        {var n=id.Native;Add(n.AuthorityEpoch);Add(n.MatchId);Add((uint)n.ShooterSlot);Add(n.Generation);Add(n.LifeId);Add(n.ShotId);}
        else if(id.Domain==HeadHitDomain.Custom)
        {
            var c=id.Custom;var a=c.Action;Add(a.Owner.AuthorityEpoch);Add(a.Owner.MatchId);Add(a.Owner.Slot);
            Add(a.Owner.Generation);Add(a.Owner.Life);Add(a.Weapon.Value);Add(a.Sequence);Add(c.Component);Add((byte)c.Channel);
            Span<byte> digest=stackalloc byte[WeaponDigest.Size];a.Manifest.Write(digest);
            foreach(byte b in digest)Add(b);
            Span<byte> bootstrap=stackalloc byte[32];id.Bootstrap.Write(bootstrap);foreach(byte b in bootstrap)Add(b);
        }
        return (hash&1)==0?-1:1;
    }
}
