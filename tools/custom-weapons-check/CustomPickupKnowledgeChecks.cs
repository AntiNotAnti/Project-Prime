using System.Buffers.Binary;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure knowledge/source-fence contracts. Grants below come from a real ledger
// and empty-loadout journal; accepted-input metadata is a synthetic engine seam.
// This does not establish live ingress wiring, online pickup activation or HP.
internal static class CustomPickupKnowledgeChecks
{
    private const ulong Connection=17;
    private static readonly WeaponRuntimeId Weapon=new(10);
    private sealed class Fixture : IDisposable,ICustomPickupFunding
    {
        internal readonly WeaponOwnerContext Owner=new(1,1,0,1,1);
        internal readonly WeaponDefinition[] Definitions;
        internal readonly WeaponRegistry Registry;
        internal readonly CustomWeaponManifest Manifest;
        internal readonly CustomPickupLayout Layout;
        internal readonly CustomPickupWorld World;
        internal readonly WorldBootstrapIdentity Bootstrap;
        internal readonly CustomPickupLedger Ledger;
        internal readonly CustomSourceActionJournal Journal;
        internal readonly CustomPickupKnowledgeFence Fence;
        private readonly uint _birthSource,_birthWorld;
        internal Fixture(uint source=100,uint world=100)
        {
            _birthSource=source;_birthWorld=world;
            Definitions=new[]{WeaponDefinition.Create(Guid.Parse("00000001-0000-0000-0000-000000000001"),"Knowledge A",MechanicsChecks.Rules()),
                WeaponDefinition.Create(Guid.Parse("00000002-0000-0000-0000-000000000001"),"Knowledge B",MechanicsChecks.Rules())};
            Registry=new(true,Definitions);Manifest=new(Definitions);
            Layout=new(MapContentIdentity.BuiltIn("MP1 SANCTORUS"),Manifest,Registry,
                new[]{new CustomPickupPlacement(1,Weapon,0,0,0,0,1,16)});
            World=new(new(1,1,1),Manifest.Digest,Layout.Digest);
            Bootstrap=new(World.Start,1,1,world,1,1);Ledger=new(Layout,World,world);
            Journal=new(Owner,new(Owner,source,world),Manifest,Registry,Array.Empty<WeaponRuntimeId>(),source,world,true,World);
            Fence=NewFence();
        }
        internal CustomPickupKnowledgeFence NewFence()=>new(Connection,Layout,World,Bootstrap,Owner,Weapon,_birthSource,_birthWorld);
        internal CustomKnowledgeCapture Capture(uint source,uint world,CustomResourceKnowledge knowledge=default)
            =>Fence.CaptureAcceptedInput(Connection,Bootstrap,Owner,source,world,knowledge);
        internal CustomWeaponAction Action(uint source,uint ack=120,CustomWeaponActionKind kind=CustomWeaponActionKind.Fire)
            =>new(Bootstrap,Manifest.Digest,Owner.Slot,Owner.Life,Weapon,1,source,
                kind==CustomWeaponActionKind.Fire?ack:0,0,kind,0,Vector3.Zero,
                kind==CustomWeaponActionKind.Fire?-Vector3.UnitZ:Vector3.Zero);
        internal CustomControlRequest Control()=>new(Bootstrap,Manifest.Digest,Owner.Slot,Owner.Life,Weapon,1,CustomWeaponActionKind.Fire,0);
        internal CustomPickupOutcome Fund(uint source,uint world)
        {
            Require(Journal.ObserveSource(Owner,source,world,true),"journal accepts declared contact source");
            Require(Ledger.Advance(world)&&Ledger.TryState(0,out _),"ledger accepts declared contact world");
            Ledger.TryState(0,out var item);
            var contact=new CustomPickupContact(Owner,source,world,Vector3.Zero,Vector3.Zero,.55f,0,2,false,true,true);
            Require(Ledger.TryCollect(item.Key,contact,this,out var outcome)==CustomPickupCollectResult.Collected,
                "real ledger/journal pickup funds");
            return outcome;
        }
        public bool TryFund(CustomPickupItemKey item,in CustomPickupContact contact,WeaponRuntimeId weapon,
            int reserveRefill,out CustomPickupFundingResult funded)
        {
            funded=default;
            if(!Journal.TryFundPickup(item,contact.Owner,weapon,contact.SourceFrame,contact.AuthorityFrame,reserveRefill,out var state))return false;
            funded=new(state,Journal.PickupRevision);return true;
        }
        internal CustomPickupOutcome Issue()
        {
            Require(Capture(100,100)==CustomKnowledgeCapture.Accepted,"first empty source");
            Require(Capture(101,120)==CustomKnowledgeCapture.Accepted,"contact empty source");
            var outcome=Fund(101,120);
            Require(Capture(102,120)==CustomKnowledgeCapture.Accepted,"numerically later pre-publication empty source");
            Require(Fence.Commit(outcome),"trusted outcome creates private grant");return outcome;
        }
        public void Dispose()=>Registry.Dispose();
    }
    private static void Require(bool condition,string reason)
    {if(!condition)throw new Exception("Pickup knowledge fixture: "+reason);}
    internal static void Run(Action<bool,string> check)
    {
        int count=0;void Verify(bool pass,string label){check(pass,"pickup knowledge: "+label);count++;}
        Codec(Verify);DelayedInput(Verify);EchoOrderAndDuplicates(Verify);Bindings(Verify);
        Rotation(Verify);Rebinding(Verify);HistoryAndRetirement(Verify);Wrap(Verify);Allocation(Verify);
        Console.WriteLine($"Pickup knowledge foundation: {count} assertions; real ledger/journal funding plus synthetic accepted-input metadata; no live activation claim.");
    }
    private static void Codec(Action<bool,string> check)
    {
        check(CustomResourceKnowledge.Size==24&&default(CustomResourceKnowledge).IsEmpty
            &&default(CustomResourceKnowledge).IsValid&&!default(CustomResourceKnowledge).IsPresent,
            "empty knowledge is a valid exact 24-byte value");
        var present=new CustomResourceKnowledge(0x0102030405060708UL,0x1112131415161718UL,0x2122232425262728UL);
        byte[] bytes=new byte[24];present.Write(bytes);
        byte[] expected={8,7,6,5,4,3,2,1,24,23,22,21,20,19,18,17,40,39,38,37,36,35,34,33};
        check(bytes.SequenceEqual(expected)&&CustomResourceKnowledge.TryRead(bytes,out var parsed)&&parsed==present
            &&present.IsPresent&&!present.IsEmpty,"present token has an exact little-endian three-ulong wire image");
        Array.Clear(bytes);check(CustomResourceKnowledge.TryRead(bytes,out var empty)&&empty.IsEmpty,
            "all-zero 24-byte token decodes as empty knowledge");
        foreach(int size in new[]{0,23,25})
            check(!CustomResourceKnowledge.TryRead(new byte[size],out var invalid)&&invalid==default,
                "decoder rejects non-exact token size: "+size);
        foreach(var invalid in new[]{new CustomResourceKnowledge(0,1,0),new(0,0,1),new(1,0,0)})
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes,invalid.Revision);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8),invalid.NonceLo);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16),invalid.NonceHi);
            bool refused=false;byte[] output=Enumerable.Repeat((byte)0xcc,24).ToArray();
            try{invalid.Write(output);}catch(ArgumentException){refused=true;}
            check(!invalid.IsValid&&!CustomResourceKnowledge.TryRead(bytes,out var result)&&result==default
                &&refused&&output.All(value=>value==0xcc),"partial revision/nonce refuses decode and atomic write");
        }
        byte[] shortBuffer=Enumerable.Repeat((byte)0xcc,23).ToArray();bool shortRefused=false;
        try{present.Write(shortBuffer);}catch(ArgumentException){shortRefused=true;}
        check(shortRefused&&shortBuffer.All(value=>value==0xcc),"short write refuses before modifying destination");
        check(present.Matches(present)&&!present.Matches(present with{Revision=9})
            &&!present.Matches(present with{NonceLo=9})&&!present.Matches(present with{NonceHi=9}),
            "knowledge equality binds revision and both private nonce halves");
        bool zeroRefused=false;try{CustomResourceKnowledge.Create(0);}catch(ArgumentOutOfRangeException){zeroRefused=true;}
        uint rng1=Rng.Rng1,rng2=Rng.Rng2;
        var first=CustomResourceKnowledge.Create(9);var second=CustomResourceKnowledge.Create(9);
        check(zeroRefused&&first.IsPresent&&second.IsPresent&&!first.Matches(second)
            &&Rng.Rng1==rng1&&Rng.Rng2==rng2&&first.ToString()=="funding/9/<private>",
            "private nonce creation rejects zero revision, rotates independently of game RNG and redacts diagnostics");
    }
    private static void DelayedInput(Action<bool,string> check)
    {
        using var f=new Fixture();
        check(f.Fence.Inspect(Connection,f.Action(100),default,100)==CustomKnowledgeAdmission.NotOwned
            &&!f.Journal.TryPreview(Weapon,out _),"empty life has neither funding knowledge nor custom inventory");
        check(f.Capture(100,100)==CustomKnowledgeCapture.Accepted&&f.Capture(101,120)==CustomKnowledgeCapture.Accepted,
            "empty accepted inputs retain original sources 100/101 and contact world120");
        var outcome=f.Fund(101,120);
        check(outcome.SourceFrame==101&&outcome.AuthorityFrame==120&&outcome.Funding.State.Tick==102
            &&outcome.Funding.State.Magazine==4&&outcome.Funding.State.Reserve==16
            &&outcome.Funding.Revision==1&&f.Journal.PickupRevision==1,
            "actual ledger and empty-loadout journal produce next-source102 funding at world120");
        check(f.Capture(102,120)==CustomKnowledgeCapture.Accepted&&f.Fence.Commit(outcome),
            "numeric-later source102 is captured empty before trusted grant publication");
        var grant=f.Fence.Grant!.Value;
        check(grant.EffectiveSource==102&&grant.GrantWorld==120&&grant.Knowledge.IsPresent
            &&f.Fence.Inspect(Connection,f.Action(102),grant.Knowledge,120)==CustomKnowledgeAdmission.NoEcho,
            "source-plus-one alone cannot authorize the already accepted source102");
        check(f.Fence.EchoControl(Connection,f.Control(),grant.Knowledge)&&f.Fence.EchoFence==102,
            "first authenticated control echo freezes all previously accepted sources through102");
        check(f.Capture(102,999,grant.Knowledge)==CustomKnowledgeCapture.Conflict
            &&f.Capture(102,999)==CustomKnowledgeCapture.Duplicate
            &&f.Fence.Inspect(Connection,f.Action(102),grant.Knowledge,120)==CustomKnowledgeAdmission.MismatchedSource,
            "resending source102 with current nonce cannot upgrade its original empty knowledge");
        check(f.Capture(103,121,grant.Knowledge)==CustomKnowledgeCapture.Accepted
            &&f.Fence.Inspect(Connection,f.Action(103,100),grant.Knowledge,121)==CustomKnowledgeAdmission.BeforeFunding
            &&f.Fence.Inspect(Connection,f.Action(103,120),grant.Knowledge,121)==CustomKnowledgeAdmission.Accepted,
            "fresh source103 proves knowledge but Fire ACK world100 still refuses; ACK120 admits");
        check(f.Fence.Inspect(Connection,f.Action(103),grant.Knowledge,120)==CustomKnowledgeAdmission.BeforeFunding
            &&f.Fence.Inspect(Connection,f.Action(103,kind:CustomWeaponActionKind.Equip),grant.Knowledge,121)==CustomKnowledgeAdmission.Accepted,
            "observation cannot precede source ingress while non-Fire actions need no Fire ACK");
        var saved=f.Fence.Grant;var fence=f.Fence.EchoFence;
        check(f.Fence.Commit(outcome)&&f.Fence.Grant==saved&&f.Fence.EchoFence==fence,
            "duplicate outcome publication preserves exact nonce and established echo fence");
    }
    private static void EchoOrderAndDuplicates(Action<bool,string> check)
    {
        foreach(bool controlFirst in new[]{false,true})
        {
            using var f=new Fixture();f.Issue();var knowledge=f.Fence.Grant!.Value.Knowledge;
            if(controlFirst)Require(f.Fence.EchoControl(Connection,f.Control(),knowledge),"control-first echo");
            check(f.Capture(103,121,knowledge)==CustomKnowledgeCapture.Accepted&&f.Fence.EchoFence==102,
                "atomic first source capture freezes echo BEFORE advancing source103; controlFirst="+controlFirst);
            check(f.Fence.EchoControl(Connection,f.Control(),knowledge)&&f.Fence.EchoFence==102
                &&f.Fence.Inspect(Connection,f.Action(103),knowledge,121)==CustomKnowledgeAdmission.Accepted,
                "control/source arrival reordering preserves the same usable source and fence");
            check(f.Capture(103,999,knowledge)==CustomKnowledgeCapture.Duplicate
                &&f.Capture(103,1,knowledge)==CustomKnowledgeCapture.Duplicate
                &&f.Fence.Inspect(Connection,f.Action(103),knowledge,121)==CustomKnowledgeAdmission.Accepted
                &&f.Fence.Inspect(Connection,f.Action(103),knowledge,120)==CustomKnowledgeAdmission.BeforeFunding,
                "duplicate source retains original world121 despite forged later or earlier retry metadata");
            check(f.Capture(104,122,knowledge)==CustomKnowledgeCapture.Accepted&&f.Fence.EchoFence==102,
                "duplicate world999 does not advance ingress clock or move the first-echo boundary");
        }
    }
    private static void Bindings(Action<bool,string> check)
    {
        using var f=new Fixture();var outcome=f.Issue();var knowledge=f.Fence.Grant!.Value.Knowledge;
        var bootstrap=f.Bootstrap;
        WorldBootstrapIdentity[] foreign={bootstrap with{Revision=2},bootstrap with{SlotGeneration=2},
            bootstrap with{AuthorityFrame=99},bootstrap with{SlowRevision=2},bootstrap with{WorldRevision=2},
            bootstrap with{Start=bootstrap.Start with{MatchId=2}},bootstrap with{Start=bootstrap.Start with{AuthorityEpoch=2}},
            bootstrap with{Start=bootstrap.Start with{StartGeneration=2}}};
        foreach(var other in foreign)
        {
            check(f.Fence.CaptureAcceptedInput(Connection,other,f.Owner,500,500,knowledge)==CustomKnowledgeCapture.Invalid
                &&!f.Fence.EchoControl(Connection,f.Control() with{Bootstrap=other},knowledge)
                &&f.Fence.Inspect(Connection,f.Action(103) with{Bootstrap=other},knowledge,121)==CustomKnowledgeAdmission.Identity,
                "accepted-input/control/action cannot cross a different complete bootstrap component");
        }
        foreach(var owner in new[]{new WeaponOwnerContext(2,1,0,1,1),new(1,2,0,1,1),new(1,1,1,1,1),new(1,1,0,2,1),new(1,1,0,1,2)})
            check(f.Fence.CaptureAcceptedInput(Connection,bootstrap,owner,500,500,knowledge)==CustomKnowledgeCapture.Invalid,
                "accepted-input metadata rejects a foreign owner component before mutation: "+owner);
        foreach(var action in new[]{f.Action(103) with{Manifest=new(9,8,7,6)},f.Action(103) with{Slot=1},
            f.Action(103) with{Life=2},f.Action(103) with{Weapon=new(11)}})
        {
            var control=new CustomControlRequest(action.Bootstrap,action.Manifest,action.Slot,action.Life,action.Weapon,
                action.Sequence,action.Kind,action.Optic);
            check(f.Fence.Inspect(Connection,action,knowledge,121)==CustomKnowledgeAdmission.Identity&&!f.Fence.EchoControl(Connection,control,knowledge),
                "control/action exact manifest, slot, life and runtime binding refuses foreign scope");
        }
        check(f.Fence.SourceFrontier==102&&f.Fence.EchoFence==null
            &&f.Capture(103,121,knowledge)==CustomKnowledgeCapture.Accepted&&f.Fence.EchoFence==102,
            "foreign scope attempts cannot advance clocks, grant an echo or obstruct next legitimate input");
        void Reject(CustomPickupWorld world,WorldBootstrapIdentity boot,WeaponOwnerContext owner,WeaponRuntimeId weapon,
            CustomPickupLayout? layout=null,uint birthWorld=100)
        {
            bool refused=false;try{_=new CustomPickupKnowledgeFence(Connection,layout??f.Layout,world,boot,owner,weapon,100,birthWorld);}
            catch(ArgumentException){refused=true;}check(refused,"constructor rejects mismatched frozen pickup world/layout/owner/bootstrap/runtime");
        }
        Reject(f.World with{Manifest=new(1,2,3,4)},bootstrap,f.Owner,Weapon);
        Reject(f.World with{Layout=new(1,2,3,4)},bootstrap,f.Owner,Weapon);
        Reject(f.World with{Start=f.World.Start with{StartGeneration=2}},bootstrap,f.Owner,Weapon);
        Reject(f.World,bootstrap with{Start=bootstrap.Start with{MatchId=2}},f.Owner,Weapon);
        Reject(f.World,bootstrap,new(2,1,0,1,1),Weapon);
        Reject(f.World,bootstrap,new(1,2,0,1,1),Weapon);
        Reject(f.World,bootstrap,new(1,1,0,2,1),Weapon);
        Reject(f.World,bootstrap,f.Owner,new(11));
        Reject(f.World,bootstrap,f.Owner,Weapon,birthWorld:99);
        var otherLayout=new CustomPickupLayout(MapContentIdentity.BuiltIn("MP2 HARVESTER"),f.Manifest,f.Registry,f.Layout.Placements.ToArray());
        Reject(f.World,bootstrap,f.Owner,Weapon,otherLayout);

        var foreignWorld=f.World with{Start=f.World.Start with{AuthorityEpoch=2}};
        CustomPickupOutcome[] malformed={outcome with{Consumed=outcome.Consumed with{World=foreignWorld}},
            outcome with{Consumed=outcome.Consumed with{World=f.World with{Layout=new(9,8,7,6)}}},
            outcome with{Consumed=outcome.Consumed with{World=f.World with{Manifest=new(9,8,7,6)}}},
            outcome with{Owner=new(1,1,0,1,2)},outcome with{Consumed=outcome.Consumed with{SpawnId=2}},
            outcome with{Funding=outcome.Funding with{Revision=0}},outcome with{Revision=0},
            outcome with{Next=outcome.Next with{Available=true}},outcome with{Next=outcome.Next with{Revision=2}},
            outcome with{Next=outcome.Next with{ReadyAt=122}},
            outcome with{Funding=outcome.Funding with{State=outcome.Funding.State with{Tick=101}}},
            outcome with{Funding=outcome.Funding with{State=outcome.Funding.State with{Weapon=f.Definitions[1].Identity}}},
            outcome with{Funding=outcome.Funding with{State=outcome.Funding.State with{Phase=(CustomWeaponPhase)255}}}};
        foreach(var invalid in malformed)
        {
            var fence=f.NewFence();Require(fence.CaptureAcceptedInput(Connection,bootstrap,f.Owner,101,120,default)==CustomKnowledgeCapture.Accepted,"invalid outcome contact metadata");
            check(!fence.Commit(invalid)&&fence.Grant==null&&fence.EchoFence==null&&fence.SourceFrontier==101,
                "malformed/foreign trusted outcome cannot publish knowledge or mutate source fences");
        }
    }
    private static void Rotation(Action<bool,string> check)
    {
        using var f=new Fixture();var first=f.Issue();var old=f.Fence.Grant!.Value.Knowledge;
        Require(f.Capture(103,121,old)==CustomKnowledgeCapture.Accepted,"first funded source");
        Require(f.Capture(104,122,old)==CustomKnowledgeCapture.Accepted,"repeat contact source");
        var second=f.Fund(104,122);
        check(second.Funding.Revision==2&&second.Funding.State.Reserve==32&&f.Fence.Commit(second)
            &&f.Fence.Grant!.Value.Knowledge.Revision==2&&!f.Fence.Grant.Value.Knowledge.Matches(old)
            &&f.Fence.EchoFence==null,"real repeat funding rotates private nonce/revision and requires fresh echo");
        var current=f.Fence.Grant;var fresh=current!.Value.Knowledge;
        check(f.Capture(105,123,old)==CustomKnowledgeCapture.Accepted
            &&f.Fence.Inspect(Connection,f.Action(105,122),old,123)==CustomKnowledgeAdmission.UnknownKnowledge
            &&!f.Fence.EchoControl(Connection,f.Control(),old),"numerically fresh pending input carrying old funding stamp cannot borrow repeat resources");
        check(f.Fence.EchoControl(Connection,f.Control(),fresh)&&f.Fence.EchoFence==105
            &&f.Fence.Inspect(Connection,f.Action(105,122),fresh,123)==CustomKnowledgeAdmission.MismatchedSource,
            "new nonce cannot upgrade previously captured old-nonce input105");
        check(f.Capture(106,124,fresh)==CustomKnowledgeCapture.Accepted
            &&f.Fence.Inspect(Connection,f.Action(106,122),fresh,124)==CustomKnowledgeAdmission.Accepted,
            "post-repeat fresh stamped input is usable at the repeat funding world");
        check(!f.Fence.Commit(first)&&f.Fence.Commit(second)&&f.Fence.Grant==current&&f.Fence.EchoFence==105,
            "old publication cannot roll back knowledge and exact repeat publication preserves new nonce/fence");
        foreach(var invalid in new[]{second with{Revision=1,Funding=second.Funding with{Revision=3}},
            second with{AuthorityFrame=119,Funding=second.Funding with{Revision=3}},
            second with{SourceFrame=100,Funding=second.Funding with{Revision=3}}})
            check(!f.Fence.Commit(invalid)&&f.Fence.Grant==current&&f.Fence.EchoFence==105,
                "higher resource revision cannot reverse global revision, authority world or contact source");
    }
    private static void Rebinding(Action<bool,string> check)
    {
        using var f=new Fixture();var outcome=f.Issue();var old=f.Fence.Grant!.Value;
        Require(f.Capture(103,121,old.Knowledge)==CustomKnowledgeCapture.Accepted,"pre-rebind funded input");
        var boot=f.Bootstrap with{Revision=2,AuthorityFrame=122};
        check(f.Fence.Rebind(Connection,boot,104,122)&&f.Fence.SourceFrontier==104&&f.Fence.EchoFence==null
            &&f.Fence.Grant is {} rebound&&rebound.GrantWorld==old.GrantWorld&&rebound.EffectiveSource==old.EffectiveSource
            &&rebound.Knowledge.Revision==old.Knowledge.Revision&&!rebound.Knowledge.Matches(old.Knowledge)
            &&f.Journal.PickupRevision==1,
            "new bootstrap preserves funded resource revision/world/source while rotating private capability and clearing echo");
        var current=f.Fence.Grant!.Value;
        check(f.Fence.Rebind(Connection,boot,104,122)&&f.Fence.Grant==current&&f.Fence.SourceFrontier==104
            &&f.Fence.EchoFence==null,"exact rebind retry preserves its new nonce and empty source history");
        var action=f.Action(105,122) with{Bootstrap=boot};var control=f.Control() with{Bootstrap=boot};
        check(f.Fence.CaptureAcceptedInput(Connection,f.Bootstrap,f.Owner,105,123,current.Knowledge)==CustomKnowledgeCapture.Invalid
            &&!f.Fence.EchoControl(Connection,f.Control(),current.Knowledge)
            &&f.Fence.Inspect(Connection,f.Action(105),current.Knowledge,123)==CustomKnowledgeAdmission.Identity
            &&!f.Fence.EchoControl(Connection,control,old.Knowledge)
            &&f.Fence.Inspect(Connection,action,old.Knowledge,123)==CustomKnowledgeAdmission.UnknownKnowledge,
            "rebind refuses stale bootstrap and old capability even when resource quantities are unchanged");
        check(f.Fence.CaptureAcceptedInput(Connection+1,boot,f.Owner,105,123,current.Knowledge)==CustomKnowledgeCapture.Invalid
            &&!f.Fence.EchoControl(Connection+1,control,current.Knowledge)
            &&f.Fence.Inspect(Connection+1,action,current.Knowledge,123)==CustomKnowledgeAdmission.Identity,
            "authenticated connection is part of capture/control/action scope before any rebind");
        check(f.Fence.EchoControl(Connection,control,current.Knowledge)&&f.Fence.EchoFence==104
            &&f.Fence.Inspect(Connection,action with{SourceFrame=103},current.Knowledge,123)==CustomKnowledgeAdmission.MissingSource,
            "rebind clears old accepted-input history instead of allowing its replay with the new nonce");
        check(f.Fence.CaptureAcceptedInput(Connection,boot,f.Owner,103,123,current.Knowledge)==CustomKnowledgeCapture.Stale
            &&f.Fence.CaptureAcceptedInput(Connection,boot,f.Owner,105,121,current.Knowledge)==CustomKnowledgeCapture.Stale
            &&f.Fence.SourceFrontier==104,"new bootstrap cannot accept retroactive source or ingress world");
        check(f.Fence.CaptureAcceptedInput(Connection,boot,f.Owner,105,123,current.Knowledge)==CustomKnowledgeCapture.Accepted
            &&f.Fence.Inspect(Connection,action with{AckFrame=120},current.Knowledge,123)==CustomKnowledgeAdmission.BeforeFunding
            &&f.Fence.Inspect(Connection,action,current.Knowledge,123)==CustomKnowledgeAdmission.Accepted,
            "fresh rebound source needs Fire ACK at current bootstrap122 even though funding was world120");
        check(f.Fence.Rebind(Connection,boot,104,122)&&f.Fence.Grant==current&&f.Fence.SourceFrontier==105
            &&f.Fence.EchoFence==104&&f.Fence.Inspect(Connection,action,current.Knowledge,123)==CustomKnowledgeAdmission.Accepted,
            "original exact rebind retry after later input cannot rewind source, clear history/echo or rotate nonce");
        var newer=boot with{Revision=3,AuthorityFrame=124};
        (ulong Id,WorldBootstrapIdentity Boot,uint Source,uint World)[] attempts={(0UL,newer,106U,124U),
            (Connection,boot with{Revision=1},106U,124U),
            (Connection,boot with{SlowRevision=2},106U,124U),
            (Connection,newer with{Start=newer.Start with{MatchId=2}},106U,124U),
            (Connection,newer with{Start=newer.Start with{AuthorityEpoch=2}},106U,124U),
            (Connection,newer with{Start=newer.Start with{StartGeneration=2}},106U,124U),
            (Connection,newer with{SlotGeneration=2},106U,124U),
            (Connection,newer,104U,124U),(Connection,newer,106U,122U),
            (Connection,newer,106U,123U)};
        foreach(var attempt in attempts)
            check(!f.Fence.Rebind(attempt.Id,attempt.Boot,attempt.Source,attempt.World)
                &&f.Fence.Grant==current&&f.Fence.SourceFrontier==105&&f.Fence.EchoFence==104,
                "invalid rebind refuses atomically: connection/revision/start/generation/source/world/bootstrap floor");

        const ulong replacement=18;
        var reset=boot with{Revision=1,AuthorityFrame=124};
        var earlierReset=reset with{AuthorityFrame=122};
        check(!f.Fence.Rebind(replacement,earlierReset,104,124)
            &&!f.Fence.Rebind(replacement,earlierReset,106,122)
            &&f.Fence.Grant==current&&f.Fence.SourceFrontier==105&&f.Fence.EchoFence==104,
            "new connection permits revision reset but cannot regress accepted source or ingress world");
        check(f.Fence.Rebind(replacement,reset,106,124)&&f.Fence.Grant is {} replacementGrant
            &&replacementGrant.GrantWorld==120&&replacementGrant.EffectiveSource==102&&replacementGrant.Knowledge.Revision==1
            &&!replacementGrant.Knowledge.Matches(current.Knowledge)&&f.Fence.SourceFrontier==106&&f.Fence.EchoFence==null,
            "trusted replacement connection may reset bootstrap revision while preserving ownership and rotating nonce");
        var latest=f.Fence.Grant!.Value;var resetControl=control with{Bootstrap=reset};
        var resetAction=action with{Bootstrap=reset,SourceFrame=107,AckFrame=124};
        check(f.Fence.CaptureAcceptedInput(Connection,reset,f.Owner,107,125,latest.Knowledge)==CustomKnowledgeCapture.Invalid
            &&!f.Fence.EchoControl(Connection,resetControl,latest.Knowledge)
            &&f.Fence.Inspect(Connection,resetAction,latest.Knowledge,125)==CustomKnowledgeAdmission.Identity
            &&f.Fence.Inspect(replacement,resetAction,current.Knowledge,125)==CustomKnowledgeAdmission.UnknownKnowledge,
            "replacement rejects old connection and its stamp even against the replacement bootstrap");
        check(f.Fence.CaptureAcceptedInput(replacement,reset,f.Owner,107,125,latest.Knowledge)==CustomKnowledgeCapture.Accepted
            &&f.Fence.EchoFence==106&&f.Fence.EchoControl(replacement,resetControl,latest.Knowledge)
            &&f.Fence.Inspect(replacement,resetAction with{AckFrame=122},latest.Knowledge,125)==CustomKnowledgeAdmission.BeforeFunding
            &&f.Fence.Inspect(replacement,resetAction,latest.Knowledge,125)==CustomKnowledgeAdmission.Accepted,
            "replacement's fresh source atomically echoes and obeys its own bootstrap ACK floor124");
        check(f.Fence.Commit(outcome)&&f.Fence.Grant==latest&&f.Fence.EchoFence==106
            &&f.Fence.Rebind(replacement,reset,106,124)&&f.Fence.SourceFrontier==107,
            "old trusted publication retry and replacement rebind retry cannot undo the reconnect capability");

        using var empty=new Fixture();
        Require(empty.Capture(100,100)==CustomKnowledgeCapture.Accepted,"empty pre-rebind source");
        var emptyBoot=empty.Bootstrap with{Revision=2,AuthorityFrame=101};
        check(empty.Fence.Rebind(Connection,emptyBoot,101,101)&&empty.Fence.Grant==null
            &&empty.Fence.CaptureAcceptedInput(Connection,emptyBoot,empty.Owner,101,101,default)==CustomKnowledgeCapture.Accepted
            &&!empty.Fence.EchoControl(Connection,empty.Control() with{Bootstrap=emptyBoot},default)
            &&empty.Fence.Inspect(Connection,empty.Action(101) with{Bootstrap=emptyBoot},default,101)==CustomKnowledgeAdmission.NotOwned
            &&empty.Journal.PickupRevision==0&&!empty.Journal.TryPreview(Weapon,out _),
            "empty life stays empty through bootstrap replacement and accepts only empty knowledge without inventing funding");
        var emptyReset=emptyBoot with{Revision=1,AuthorityFrame=102};
        check(empty.Fence.Rebind(replacement,emptyReset,102,102)&&empty.Fence.Grant==null
            &&empty.Fence.SourceFrontier==102&&empty.Fence.EchoFence==null,
            "empty life also remains unowned across a new connection and reset revision");
        f.Fence.Retire();
        check(!f.Fence.Rebind(replacement,reset with{Revision=2,AuthorityFrame=126},108,126)
            &&f.Fence.Grant==null&&f.Fence.IsRetired,"retired owner cannot be revived by trusted reconnect");
        bool zeroRefused=false;
        try{_=new CustomPickupKnowledgeFence(0,empty.Layout,empty.World,empty.Bootstrap,empty.Owner,Weapon,100,100);}
        catch(ArgumentException){zeroRefused=true;}
        check(zeroRefused,"initial knowledge fence requires a nonzero authenticated connection");
    }
    private static void HistoryAndRetirement(Action<bool,string> check)
    {
        using var f=new Fixture();var outcome=f.Issue();var knowledge=f.Fence.Grant!.Value.Knowledge;
        Require(f.Capture(103,121,knowledge)==CustomKnowledgeCapture.Accepted,"retained source103");
        bool captured=true;
        for(uint source=104;source<=230;source++)captured&=f.Capture(source,source+18,knowledge)==CustomKnowledgeCapture.Accepted;
        check(captured&&CustomPickupKnowledgeFence.SourceCapacity==128
            &&f.Fence.Inspect(Connection,f.Action(103),knowledge,248)==CustomKnowledgeAdmission.Accepted,
            "exact 128-entry ring retains source103 through source230 without numeric-age expansion");
        check(f.Capture(231,249,knowledge)==CustomKnowledgeCapture.Accepted
            &&f.Fence.Inspect(Connection,f.Action(103),knowledge,249)==CustomKnowledgeAdmission.MissingSource
            &&f.Capture(103,250,knowledge)==CustomKnowledgeCapture.Stale,
            "129th newer input evicts source103 and its old duplicate cannot repopulate history");
        check(f.Capture(232,248,knowledge)==CustomKnowledgeCapture.Stale
            &&f.Capture(232,250,new(0,1,0))==CustomKnowledgeCapture.Invalid&&f.Fence.SourceFrontier==231,
            "backward ingress world and malformed knowledge refuse atomically");
        f.Fence.Retire();
        check(f.Fence.IsRetired&&f.Fence.Grant==null&&f.Fence.EchoFence==null
            &&f.Capture(232,250,knowledge)==CustomKnowledgeCapture.Retired
            &&f.Fence.Inspect(Connection,f.Action(231),knowledge,250)==CustomKnowledgeAdmission.Retired
            &&!f.Fence.EchoControl(Connection,f.Control(),knowledge)&&!f.Fence.Commit(outcome),
            "retirement clears knowledge/history and refuses all late captures, controls, actions and publication");
    }
    private static void Wrap(Action<bool,string> check)
    {
        uint birth=uint.MaxValue-2,contact=uint.MaxValue-1;
        using var f=new Fixture(birth,birth);
        Require(f.Capture(birth,birth)==CustomKnowledgeCapture.Accepted,"wrapped birth input");
        Require(f.Capture(contact,contact)==CustomKnowledgeCapture.Accepted,"wrapped contact input");
        var outcome=f.Fund(contact,contact);
        Require(f.Capture(uint.MaxValue,contact)==CustomKnowledgeCapture.Accepted,"wrapped pre-knowledge input");
        check(f.Fence.Commit(outcome)&&f.Fence.Grant!.Value.EffectiveSource==uint.MaxValue,
            "real funding retains next-source boundary immediately before uint wrap");
        var knowledge=f.Fence.Grant!.Value.Knowledge;
        check(f.Fence.EchoControl(Connection,f.Control(),knowledge)&&f.Fence.EchoFence==uint.MaxValue
            &&f.Capture(0,0,knowledge)==CustomKnowledgeCapture.Accepted&&f.Fence.EchoFence==uint.MaxValue,
            "fresh source/world zero crosses wrap after the maximum echo fence");
        check(f.Fence.Inspect(Connection,f.Action(0,birth),knowledge,0)==CustomKnowledgeAdmission.BeforeFunding
            &&f.Fence.Inspect(Connection,f.Action(0,contact),knowledge,0)==CustomKnowledgeAdmission.Accepted,
            "wrapped source still refuses pre-grant Fire ACK and admits exact grant-world ACK");
    }
    private static void Allocation(Action<bool,string> check)
    {
        using var f=new Fixture();f.Issue();var knowledge=f.Fence.Grant!.Value.Knowledge;
        Require(f.Capture(103,121,knowledge)==CustomKnowledgeCapture.Accepted,"allocation funded input");
        var action=f.Action(103);var control=f.Control();uint source=103,world=121;int admitted=0;
        void Interact()
        {
            source++;world++;
            if(f.Capture(source,world,knowledge)==CustomKnowledgeCapture.Accepted)admitted++;
            if(f.Fence.EchoControl(Connection,control,knowledge))admitted++;
            action=action with{SourceFrame=source};
            if(f.Fence.Inspect(Connection,action,knowledge,world)==CustomKnowledgeAdmission.Accepted)admitted++;
        }
        for(int i=0;i<1000;i++)Interact();
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++)Interact();
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        check(bytes==0&&admitted==303000,"100000 warmed capture/echo/inspection interactions allocate zero bytes");
        Console.WriteLine($"CUSTOM PICKUP KNOWLEDGE ALLOCATION:100000 warmed interactions={bytes}B");
    }
}
