using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

internal interface ICustomNativeSelectionPolicy
{
    CustomNativeSelection ReadNativeSelection(WeaponOwnerContext owner,uint source);
    bool TryReleaseNativeSelection(WeaponOwnerContext owner,uint source);
}
internal enum CustomNativeFireDecision : byte { Released,Pending,Rejected,Conflict,Overflow }

// Owner-thread intake before native proof, payment, recording or relay. Only
// waiting events own carrier copies; terminal keys cannot be promoted by retry.
internal sealed class CustomNativeFireGate
{
    internal const int Capacity=64,SeenCapacity=2048;
    internal const uint Retention=64,SeenRetention=256,AdmittedRetention=1024;
    private readonly record struct Entry(ShotKey Key,FireEvent Fire,ulong Connection,uint Arrived,
        CustomNativeFireDecision Decision,int Carrier,bool Admitted=false);
    private readonly Entry?[] _seen=new Entry?[SeenCapacity];
    private readonly IntentPacket?[] _carriers=new IntentPacket?[Capacity];
    private readonly byte _slot;
    private readonly ICustomNativeSelectionPolicy _policy;
    private uint _world;
    private bool _hasTime;
    internal int PendingCount {get;private set;}
    internal long Staged,Released,Rejected,Conflicts,Overflow,Expired;
    internal CustomNativeFireGate(byte slot,ICustomNativeSelectionPolicy policy)
    {
        if(slot>=8)throw new ArgumentOutOfRangeException(nameof(slot));
        ArgumentNullException.ThrowIfNull(policy);_slot=slot;_policy=policy;
    }
    private static bool Before(uint a,uint b)=>unchecked((int)(a-b))<0;
    private static WeaponOwnerContext Owner(in ShotKey key)
        =>new(key.MatchId,key.AuthorityEpoch,(byte)key.ShooterSlot,key.Generation,key.LifeId);
    private int Find(in ShotKey key)
    {for(int i=0;i<_seen.Length;i++)if(_seen[i]?.Key==key)return i;return -1;}
    private void Finish(int at,CustomNativeFireDecision decision)
    {
        if(_seen[at] is not {} entry)return;
        if(entry.Carrier>=0){_carriers[entry.Carrier]=null;PendingCount--;}
        _seen[at]=entry with{Decision=decision,Carrier=-1};
        if(decision==CustomNativeFireDecision.Released)Released++;else Rejected++;
    }
    internal bool Advance(uint world)
    {
        if(_hasTime){if(Before(world,_world))return false;if(world==_world)return true;}
        _hasTime=true;_world=world;
        for(int i=0;i<_seen.Length;i++)
            if(_seen[i] is {} entry)
            {
                uint age=unchecked(world-entry.Arrived);
                if(entry.Decision==CustomNativeFireDecision.Pending&&age>Retention)
                {Finish(i,CustomNativeFireDecision.Rejected);Expired++;}
                // A real native projectile can remain in flight for510 fixed
                // steps, followed by claim arrival/grace. Keep admission facts
                // independently of optional native proof and current selection.
                if(age>(entry.Admitted?AdmittedRetention:SeenRetention))_seen[i]=null;
            }
        return true;
    }
    private CustomNativeSelection Selection(in Entry entry)
        =>entry.Fire.Kind==FireEventKind.TurretFire?CustomNativeSelection.Native
            :_policy.ReadNativeSelection(Owner(entry.Key),entry.Fire.SourceFrame);
    private bool ReleasePermission(in Entry entry)
        =>entry.Fire.Kind==FireEventKind.TurretFire||_policy.TryReleaseNativeSelection(Owner(entry.Key),entry.Fire.SourceFrame);
    internal CustomNativeFireDecision Offer(in IntentPacket carrier,in FireEvent fire,ulong connection,uint world)
    {
        if(!Advance(world)||connection==0||carrier.MatchId==0||carrier.AuthorityEpoch==0||carrier.SlotGeneration==0
            ||carrier.LifeId==0||fire.ShotId==0||!NetFireEvents.Validate(carrier)
            ||fire.SourceLife!=0&&unchecked((short)(fire.SourceLife-carrier.LifeId))>0
            ||Before(carrier.Frame,fire.SourceFrame)||unchecked(carrier.Frame-fire.SourceFrame)>NetFireEvents.RetentionFrames
            ||fire.Kind==FireEventKind.TurretFire&&(fire.Weapon!=(byte)BeamType.Battlehammer||fire.Charge!=0
                ||!fire.HasPose||fire.SourceLife==0))return CustomNativeFireDecision.Rejected;
        bool present=false;
        for(int i=0;i<carrier.FireEventCount;i++)present|=carrier.FireEvents[i]==fire;
        if(!present)return CustomNativeFireDecision.Rejected;
        var key=new ShotKey(carrier.AuthorityEpoch,carrier.MatchId,_slot,carrier.SlotGeneration,
            fire.SourceLife!=0?fire.SourceLife:carrier.LifeId,fire.ShotId);
        int at=Find(key);
        if(at>=0)
        {
            var prior=_seen[at]!.Value;
            if(prior.Fire!=fire){Conflicts++;return CustomNativeFireDecision.Conflict;}
            // A new authenticated connection cannot adopt old waiting work.
            // Already released historical native facts retain their original key.
            if(prior.Decision==CustomNativeFireDecision.Pending&&prior.Connection!=connection)
                Finish(at,CustomNativeFireDecision.Rejected);
            return _seen[at]!.Value.Decision;
        }
        for(int i=0;i<_seen.Length;i++)if(!_seen[i].HasValue){at=i;break;}
        if(at<0){Overflow++;return CustomNativeFireDecision.Overflow;}
        var candidate=new Entry(key,fire,connection,world,CustomNativeFireDecision.Pending,-1);
        var selection=Selection(candidate);
        if(selection==CustomNativeSelection.Native&&ReleasePermission(candidate))
        {_seen[at]=candidate with{Decision=CustomNativeFireDecision.Released};Released++;return CustomNativeFireDecision.Released;}
        if(selection!=CustomNativeSelection.Pending)
        {_seen[at]=candidate with{Decision=CustomNativeFireDecision.Rejected};Rejected++;return CustomNativeFireDecision.Rejected;}
        int cell=-1;for(int i=0;i<_carriers.Length;i++)if(!_carriers[i].HasValue){cell=i;break;}
        if(cell<0)
        {
            _seen[at]=candidate with{Decision=CustomNativeFireDecision.Rejected};Overflow++;Rejected++;
            return CustomNativeFireDecision.Overflow;
        }
        // Inline histories/poses are values. No received datagram or borrowed
        // encoder buffer survives this synchronous copy.
        var owned=carrier;owned.FireEvents=default;owned.FireEventCount=1;owned.FireEvents[0]=fire;owned.HasFireEvents=true;
        _carriers[cell]=owned;_seen[at]=candidate with{Carrier=cell};PendingCount++;Staged++;
        return CustomNativeFireDecision.Pending;
    }
    internal bool TryTake(uint world,out IntentPacket carrier,out ulong connection,out ShotKey key)
    {
        carrier=default;connection=0;key=default;if(!Advance(world))return false;
        for(int i=0;i<_seen.Length;i++)
            if(_seen[i] is {Decision:CustomNativeFireDecision.Pending} entry)
            {
                var selection=Selection(entry);
                if(selection is CustomNativeSelection.Custom or CustomNativeSelection.Rejected)
                {Finish(i,CustomNativeFireDecision.Rejected);continue;}
                if(selection!=CustomNativeSelection.Native)continue;
                if(entry.Carrier<0||_carriers[entry.Carrier] is not {} owned)
                {Finish(i,CustomNativeFireDecision.Rejected);continue;}
                carrier=owned;connection=entry.Connection;key=entry.Key;return true;
            }
        return false;
    }
    internal bool Release(in ShotKey key,ulong connection,uint world)
    {
        if(!Advance(world))return false;int at=Find(key);
        if(at<0||_seen[at] is not {Decision:CustomNativeFireDecision.Pending} entry
            ||entry.Connection!=connection||Selection(entry)!=CustomNativeSelection.Native||!ReleasePermission(entry))return false;
        Finish(at,CustomNativeFireDecision.Released);return true;
    }
    internal void Reject(in ShotKey key)
    {int at=Find(key);if(at>=0&&_seen[at]?.Decision==CustomNativeFireDecision.Pending)Finish(at,CustomNativeFireDecision.Rejected);}
    internal CustomNativeFireDecision Read(in ShotKey key,out FireEvent fire)
    {int at=Find(key);fire=at>=0?_seen[at]!.Value.Fire:default;return at>=0?_seen[at]!.Value.Decision:CustomNativeFireDecision.Pending;}
    internal bool Confirm(in ShotKey key,in FireEvent fire)
    {
        int at=Find(key);
        if(at<0||_seen[at] is not {Decision:CustomNativeFireDecision.Released} entry||entry.Fire!=fire)return false;
        _seen[at]=entry with{Admitted=true};return true;
    }
    internal CustomNativeFireDecision ReadAdmitted(in ShotKey key,out FireEvent fire)
    {
        var decision=Read(key,out fire);int at=Find(key);
        return decision==CustomNativeFireDecision.Released&&at>=0&&!_seen[at]!.Value.Admitted
            ?CustomNativeFireDecision.Pending:decision;
    }
}
