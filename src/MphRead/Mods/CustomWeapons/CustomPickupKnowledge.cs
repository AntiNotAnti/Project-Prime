using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

// Private owner knowledge, distinct from resource quantities and action sequence.
// A control/input sample must retain the value present at creation, never resend
// with the latest inventory value. No live wire shape changes in this slice.
internal readonly record struct CustomResourceKnowledge(ulong Revision,ulong NonceLo,ulong NonceHi)
{
    internal const int Size=24;
    internal bool IsEmpty=>Revision==0&&NonceLo==0&&NonceHi==0;
    internal bool IsPresent=>Revision!=0&&(NonceLo|NonceHi)!=0;
    internal bool IsValid=>IsEmpty||IsPresent;
    internal bool Matches(in CustomResourceKnowledge other)
        =>((Revision^other.Revision)|(NonceLo^other.NonceLo)|(NonceHi^other.NonceHi))==0;
    internal static CustomResourceKnowledge Create(ulong revision)
    {
        if(revision==0)throw new ArgumentOutOfRangeException(nameof(revision));
        Span<byte> nonce=stackalloc byte[16];CustomResourceKnowledge value;
        do
        {
            RandomNumberGenerator.Fill(nonce);
            value=new(revision,BinaryPrimitives.ReadUInt64LittleEndian(nonce),BinaryPrimitives.ReadUInt64LittleEndian(nonce[8..]));
        }while(!value.IsPresent);
        return value;
    }
    internal void Write(Span<byte> bytes)
    {
        if(!IsValid||bytes.Length<Size)throw new ArgumentException("Valid knowledge and complete buffer required.");
        BinaryPrimitives.WriteUInt64LittleEndian(bytes,Revision);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..],NonceLo);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[16..],NonceHi);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes,out CustomResourceKnowledge value)
    {
        value=default;if(bytes.Length!=Size)return false;
        var candidate=new CustomResourceKnowledge(BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]),BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]));
        if(!candidate.IsValid)return false;value=candidate;return true;
    }
    public override string ToString()=>IsEmpty?"funding/none":$"funding/{Revision}/<private>";
}
internal readonly record struct CustomPickupKnowledgeGrant(CustomResourceKnowledge Knowledge,
    uint GrantWorld,uint EffectiveSource);
internal enum CustomKnowledgeCapture : byte { Accepted,Duplicate,Conflict,Invalid,Stale,Retired }
internal enum CustomKnowledgeAdmission : byte
{ Accepted,Retired,NotOwned,Identity,UnknownKnowledge,NoEcho,MissingSource,BeforeEcho,BeforeFunding,MismatchedSource }

// One scene-owned owner/weapon/bootstrap. Capture EVERY successfully accepted
// native source input, including empty knowledge, before it can be consumed by
// the custom engine. The canonical source/pose/resource gates remain mandatory.
// This model is not an ingress handler and does not activate online pickups.
internal sealed class CustomPickupKnowledgeFence
{
    internal const int SourceCapacity=128;
    private readonly record struct Input(uint Source,uint World,CustomResourceKnowledge Knowledge);
    private readonly Input[] _inputs=new Input[SourceCapacity];
    private readonly CustomPickupLayout _layout;
    private readonly CustomPickupWorld _world;
    private WorldBootstrapIdentity _bootstrap;
    private ulong _connection;
    private readonly WeaponOwnerContext _owner;
    private readonly WeaponRuntimeId _weapon;
    private int _head,_count;
    private uint _sourceFrontier,_worldFrontier,_echoFence;
    private bool _hasInput,_hasEcho;
    private CustomPickupOutcome? _funding;
    internal bool IsRetired { get; private set; }
    internal CustomPickupKnowledgeGrant? Grant { get; private set; }
    internal uint SourceFrontier=>_sourceFrontier;
    internal uint? EchoFence=>_hasEcho?_echoFence:null;
    private static bool Before(uint a,uint b)=>unchecked((int)(a-b))<0;
    private static bool Newer(uint a,uint b)=>unchecked((int)(a-b))>0;
    internal CustomPickupKnowledgeFence(ulong connectionId,CustomPickupLayout layout,CustomPickupWorld world,
        WorldBootstrapIdentity bootstrap,WeaponOwnerContext owner,WeaponRuntimeId weapon,uint birthSource,uint birthWorld)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if(connectionId==0||!world.IsValid||world.Layout!=layout.Digest||world.Manifest!=layout.Manifest
            ||!CustomWeaponProtocol.ValidBootstrap(bootstrap)||bootstrap.Start!=world.Start
            ||!owner.IsValid||owner.MatchId!=world.Start.MatchId||owner.AuthorityEpoch!=world.Start.AuthorityEpoch
            ||owner.Generation!=bootstrap.SlotGeneration||Before(birthWorld,bootstrap.AuthorityFrame))
            throw new ArgumentException("Exact pickup world, bootstrap and owner life required.");
        bool found=false;foreach(var placement in layout.Placements)found|=placement.Weapon==weapon;
        if(!found)throw new ArgumentException("Weapon must have an authored pickup placement.");
        _connection=connectionId;_layout=layout;_world=world;_bootstrap=bootstrap;_owner=owner;_weapon=weapon;
        _sourceFrontier=birthSource;_worldFrontier=birthWorld;
    }
    // Trusted native readiness transition, never a client-requested mutation.
    // Rebootstrap/reconnect preserves inventory but requires fresh owner knowledge.
    internal bool Rebind(ulong connectionId,WorldBootstrapIdentity bootstrap,uint birthSource,uint birthWorld)
    {
        if(IsRetired||connectionId==0||!CustomWeaponProtocol.ValidBootstrap(bootstrap)
            ||bootstrap.Start!=_world.Start||bootstrap.SlotGeneration!=_owner.Generation
            ||Before(birthWorld,bootstrap.AuthorityFrame))return false;
        if(connectionId==_connection&&bootstrap==_bootstrap)return true;
        if(Before(birthSource,_sourceFrontier)||Before(birthWorld,_worldFrontier))return false;
        if(connectionId==_connection&&!Newer(bootstrap.Revision,_bootstrap.Revision))return false;
        CustomPickupKnowledgeGrant? replacement=Grant is {} grant
            ?grant with{Knowledge=CustomResourceKnowledge.Create(grant.Knowledge.Revision)}:null;
        _connection=connectionId;_bootstrap=bootstrap;Grant=replacement;
        _head=0;_count=0;_hasInput=false;_hasEcho=false;_echoFence=0;Array.Clear(_inputs);
        _sourceFrontier=birthSource;_worldFrontier=birthWorld;return true;
    }
    private bool Find(uint source,out Input input)
    {
        for(int i=0;i<_count;i++)
        {
            var item=_inputs[(_head-i-1+SourceCapacity)%SourceCapacity];
            if(item.Source==source){input=item;return true;}
        }
        input=default;return false;
    }
    private void Echo()
    {if(!_hasEcho){_echoFence=_sourceFrontier;_hasEcho=true;}}
    internal CustomKnowledgeCapture CaptureAcceptedInput(ulong connectionId,WorldBootstrapIdentity bootstrap,WeaponOwnerContext owner,
        uint source,uint ingressWorld,CustomResourceKnowledge knowledge)
    {
        if(IsRetired)return CustomKnowledgeCapture.Retired;
        if(connectionId!=_connection||bootstrap!=_bootstrap||owner!=_owner||!knowledge.IsValid)return CustomKnowledgeCapture.Invalid;
        // Repeated input never upgrades its original knowledge or arrival time.
        if(Find(source,out var previous))return previous.Knowledge.Matches(knowledge)
            ?CustomKnowledgeCapture.Duplicate:CustomKnowledgeCapture.Conflict;
        if(Before(ingressWorld,_worldFrontier)||(_hasInput?!Newer(source,_sourceFrontier):Before(source,_sourceFrontier)))
            return CustomKnowledgeCapture.Stale;
        // Freeze BEFORE admitting this source, allowing an atomic first echo
        // with fresh input while excluding every previously accepted frame.
        if(Grant is {} grant&&grant.Knowledge.Matches(knowledge))Echo();
        _inputs[_head]=new(source,ingressWorld,knowledge);_head=(_head+1)%SourceCapacity;
        _count=Math.Min(SourceCapacity,_count+1);_sourceFrontier=source;_worldFrontier=ingressWorld;_hasInput=true;
        return CustomKnowledgeCapture.Accepted;
    }
    internal bool Commit(in CustomPickupOutcome outcome)
    {
        if(IsRetired)return false;
        if(_funding is {} prior)
        {
            if(outcome.Funding.Revision<=prior.Funding.Revision)
                return outcome==prior; // Publication retry retains its exact nonce/fence.
            if(outcome.Revision<=prior.Revision||Before(outcome.AuthorityFrame,prior.AuthorityFrame)
                ||Before(outcome.SourceFrame,prior.SourceFrame))return false;
        }
        int index=-1;
        for(int i=0;i<_layout.Count;i++)if(_layout.Placements[i].SpawnId==outcome.Consumed.SpawnId){index=i;break;}
        if(index<0||_layout.Placements[index].Weapon!=_weapon||!outcome.Consumed.IsValid||outcome.Consumed.World!=_world
            ||outcome.Consumed.Incarnation==ulong.MaxValue||outcome.Owner!=_owner||outcome.Funding.Revision==0
            ||outcome.Revision==0||outcome.Next.Revision!=outcome.Revision||outcome.Next.Available
            ||outcome.Next.Key!=new CustomPickupItemKey(_world,outcome.Consumed.SpawnId,outcome.Consumed.Incarnation+1)
            ||unchecked((int)(outcome.Next.ReadyAt-outcome.AuthorityFrame))!=(int)_layout.Placements[index].RespawnTicks
            ||!Find(outcome.SourceFrame,out var contact)||Before(outcome.AuthorityFrame,contact.World)
            ||Before(outcome.AuthorityFrame,_worldFrontier)
            ||outcome.Funding.State.Tick!=unchecked(outcome.SourceFrame+1)
            ||outcome.Funding.State.Phase is <CustomWeaponPhase.Unequipped or >CustomWeaponPhase.Reloading
            ||!CustomCombatResults.SnapshotValid(outcome.Funding.State,_owner,_layout.Definition(index)))return false;
        var knowledge=CustomResourceKnowledge.Create(outcome.Funding.Revision);
        Grant=new(knowledge,outcome.AuthorityFrame,unchecked(outcome.SourceFrame+1));
        _funding=outcome;_hasEcho=false;_echoFence=0;return true;
    }
    internal bool EchoControl(ulong connectionId,in CustomControlRequest request,CustomResourceKnowledge knowledge)
    {
        if(IsRetired||connectionId!=_connection||Grant is not {} grant||!request.IsValid||request.Bootstrap!=_bootstrap||request.Manifest!=_world.Manifest
            ||request.Owner!=_owner||request.Weapon!=_weapon||!grant.Knowledge.Matches(knowledge))return false;
        Echo();return true;
    }
    internal CustomKnowledgeAdmission Inspect(ulong connectionId,in CustomWeaponAction action,CustomResourceKnowledge knowledge,uint observedWorld)
    {
        if(IsRetired)return CustomKnowledgeAdmission.Retired;
        if(Grant is not {} grant)return CustomKnowledgeAdmission.NotOwned;
        if(connectionId!=_connection||!action.IsValid||action.Bootstrap!=_bootstrap||action.Manifest!=_world.Manifest||action.Owner!=_owner||action.Weapon!=_weapon)
            return CustomKnowledgeAdmission.Identity;
        if(!grant.Knowledge.Matches(knowledge))return CustomKnowledgeAdmission.UnknownKnowledge;
        if(!_hasEcho)return CustomKnowledgeAdmission.NoEcho;
        if(!Find(action.SourceFrame,out var source))return CustomKnowledgeAdmission.MissingSource;
        if(!source.Knowledge.Matches(knowledge))return CustomKnowledgeAdmission.MismatchedSource;
        if(!Newer(action.SourceFrame,_echoFence))return CustomKnowledgeAdmission.BeforeEcho;
        if(Before(action.SourceFrame,grant.EffectiveSource)||Before(source.World,grant.GrantWorld)
            ||Before(observedWorld,source.World)||Before(observedWorld,grant.GrantWorld)
            ||action.Kind==CustomWeaponActionKind.Fire&&(Before(action.AckFrame,grant.GrantWorld)
                ||Before(action.AckFrame,_bootstrap.AuthorityFrame)))
            return CustomKnowledgeAdmission.BeforeFunding;
        return CustomKnowledgeAdmission.Accepted;
    }
    internal void Retire()
    {
        IsRetired=true;Grant=null;_funding=null;_hasEcho=false;_count=0;Array.Clear(_inputs);
    }
}
