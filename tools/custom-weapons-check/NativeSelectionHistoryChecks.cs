using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure native/custom selection boundaries and ordered-journal integration. The
// journal oracle supplies explicit equal-clock facts, never engine input/pose,
// geometry, transport proof or an actual projectile emission.
internal static class NativeSelectionHistoryChecks
{
    private static readonly WeaponOwnerContext Owner=new(7,0xfedcba9876543210UL,3,9,12);
    internal static void Run(Action<bool,string> check)
    {
        Baseline(check);HistoricalRecovery(check);SameSourceLock(check);CustomRelease(check);ForeignAndRetirement(check);
        Retention(check);Wrap(check);JournalBoundaries(check);JournalEmissionRelease(check);Allocation(check);
    }
    private static void Baseline(Action<bool,string> check)
    {
        var history=new CustomNativeSelectionHistory(Owner,1000);
        check(history.Read(Owner,1000)==CustomNativeSelection.Pending
            &&history.Read(Owner,1001)==CustomNativeSelection.Pending,"unsealed birth and future source have no native authorization");
        check(history.Observe(Owner,1000)&&history.Read(Owner,1000)==CustomNativeSelection.Pending,
            "observing engine source does not seal a selection decision");
        check(history.Seal(Owner,1000)&&history.Read(Owner,1000)==CustomNativeSelection.Native
            &&history.Read(Owner,999)==CustomNativeSelection.Native,
            "sealed baseline and retained pre-custom-life source select ordinary native weapons");
        check(history.Read(Owner,1001)==CustomNativeSelection.Pending&&!history.TryReleaseNative(Owner,1001),
            "native release cannot authorize a source beyond sealed frontier");
        check(history.Observe(Owner,1001)&&history.Commit(Owner,1001,new(10))
            &&history.Read(Owner,1001)==CustomNativeSelection.Pending,"custom commit remains pending until input binding phase seals source");
        check(history.Seal(Owner,1001)&&history.Read(Owner,1001)==CustomNativeSelection.Custom,
            "sealed custom boundary excludes native fire at exact equip source");
        check(!history.Observe(Owner,1000)&&!history.Seal(Owner,1000)&&!history.Seal(Owner,1002)
            &&!history.CanEquip(Owner,1002)&&!history.Commit(Owner,1002,new(10))
            &&history.Read(Owner,1001)==CustomNativeSelection.Custom,
            "backdated observation/seal and unobserved future commit cannot move an accepted frontier");
    }
    private static void HistoricalRecovery(Action<bool,string> check)
    {
        var history=new CustomNativeSelectionHistory(Owner,1000);
        history.Observe(Owner,1000);history.Seal(Owner,1000);
        history.Observe(Owner,1010);history.Commit(Owner,1010,new(10));history.Seal(Owner,1010);
        check(history.Read(Owner,1009)==CustomNativeSelection.Native&&history.TryReleaseNative(Owner,1009)
            &&history.Read(Owner,1010)==CustomNativeSelection.Custom&&!history.TryReleaseNative(Owner,1010),
            "late pre-equip native recovery uses source history while current custom selection remains exclusive");
        history.Observe(Owner,1020);history.Commit(Owner,1020,default);history.Seal(Owner,1020);
        check(history.Read(Owner,1019)==CustomNativeSelection.Custom&&history.Read(Owner,1020)==CustomNativeSelection.Native
            &&history.TryReleaseNative(Owner,1020),"unequip opens native fire only at its own historical source boundary");
    }
    private static void SameSourceLock(Action<bool,string> check)
    {
        var history=new CustomNativeSelectionHistory(Owner,1000);history.Observe(Owner,1000);
        check(history.CanEquip(Owner,1000)&&history.Commit(Owner,1000,new(10))
            &&history.Commit(Owner,1000,default)&&history.Commit(Owner,1000,new(73)),
            "same-source ordered selection changes are allowed before any native release");
        history.Seal(Owner,1000);
        check(history.Read(Owner,1000)==CustomNativeSelection.Custom&&!history.TryReleaseNative(Owner,1000),
            "last same-source custom selection wins and cannot release native fire");
        var native=new CustomNativeSelectionHistory(Owner,1000);native.Observe(Owner,1000);native.Seal(Owner,1000);
        check(native.TryReleaseNative(Owner,1000)&&native.TryReleaseNative(Owner,1000)
            &&!native.CanEquip(Owner,1000)&&!native.Commit(Owner,1000,new(10))
            &&native.Read(Owner,1000)==CustomNativeSelection.Native,
            "actual native release is idempotent and permanently forbids retroactive same-source equip");
        var provisional=new CustomNativeSelectionHistory(Owner,1000);provisional.Observe(Owner,1000);provisional.Seal(Owner,1000);
        check(provisional.Read(Owner,1000)==CustomNativeSelection.Native&&provisional.Commit(Owner,1000,new(10))
            &&provisional.Read(Owner,1000)==CustomNativeSelection.Custom&&provisional.Commit(Owner,1000,default)
            &&provisional.Read(Owner,1000)==CustomNativeSelection.Native,
            "a native read alone is provisional and same-source last-wins commitment remains legal before actual release");
        native.Observe(Owner,1010);native.Seal(Owner,1010);native.TryReleaseNative(Owner,1010);
        check(!native.CanEquip(Owner,1005)&&!native.Commit(Owner,1005,new(10))
            &&native.Read(Owner,1005)==CustomNativeSelection.Native,"highest native release protects every earlier source from a late equip");
        check(native.Observe(Owner,1011)&&native.CanEquip(Owner,1011)&&native.Commit(Owner,1011,new(10))
            &&native.Seal(Owner,1011)&&native.Read(Owner,1011)==CustomNativeSelection.Custom,
            "released native source cannot forbid a genuinely later equip");
        foreach(ushort id in new ushort[]{1,9,74,ushort.MaxValue})
            check(!native.Commit(Owner,1011,new(id)),"selection history refuses native/unknown nonzero runtime IDs");
    }
    private static void CustomRelease(Action<bool,string> check)
    {
        var history=new CustomNativeSelectionHistory(Owner,1000);
        check(!history.TryReleaseCustom(Owner,1000)&&!history.TryReleaseCustom(Owner,1001),
            "custom release refuses before any custom selection or observed source");
        history.Observe(Owner,1000);history.Seal(Owner,1000);
        check(!history.TryReleaseCustom(Owner,1000),"ordinary native selection cannot release a custom emission");
        history.Commit(Owner,1000,new(10));
        var foreign=new WeaponOwnerContext(Owner.MatchId,Owner.AuthorityEpoch,Owner.Slot,Owner.Generation,13);
        check(!history.TryReleaseCustom(foreign,1000)&&!history.TryReleaseCustom(Owner,1001)
            &&history.CanUnequip(Owner,1000),"wrong-owner or future custom release cannot lock current selection");
        check(history.TryReleaseCustom(Owner,1000)&&history.TryReleaseCustom(Owner,1000)
            &&!history.CanUnequip(Owner,1000)&&!history.Commit(Owner,1000,default)
            &&history.Read(Owner,1000)==CustomNativeSelection.Custom,
            "definite custom release is idempotent and blocks same-source unequip without changing custom ownership");
        history.Observe(Owner,1001);history.Commit(Owner,1001,new(10));history.TryReleaseCustom(Owner,1001);
        check(!history.TryReleaseCustom(Owner,1000)&&!history.CanUnequip(Owner,1001)
            &&!history.Commit(Owner,1001,default),"backdated release cannot move the highest custom emission fence");
        check(history.Observe(Owner,1002)&&history.CanUnequip(Owner,1002)&&history.Commit(Owner,1002,default)
            &&history.Seal(Owner,1002)&&history.Read(Owner,1001)==CustomNativeSelection.Custom
            &&history.Read(Owner,1002)==CustomNativeSelection.Native&&!history.TryReleaseCustom(Owner,1002),
            "source after highest custom release may unequip while historical emitted custom source remains owned");
    }
    private static void ForeignAndRetirement(Action<bool,string> check)
    {
        var history=new CustomNativeSelectionHistory(Owner,1000);history.Observe(Owner,1000);
        history.Commit(Owner,1000,new(10));history.Seal(Owner,1000);
        foreach(var foreign in new[]{default(WeaponOwnerContext),new(8,Owner.AuthorityEpoch,3,9,12),
            new(7,Owner.AuthorityEpoch^0x8000000000000000UL,3,9,12),new(7,Owner.AuthorityEpoch,4,9,12),
            new(7,Owner.AuthorityEpoch,3,10,12),new(7,Owner.AuthorityEpoch,3,9,13)})
            check(history.Read(foreign,1000)==CustomNativeSelection.Rejected&&!history.Observe(foreign,1001)
                &&!history.Seal(foreign,1001)&&!history.CanEquip(foreign,1001)&&!history.Commit(foreign,1001,default)
                &&!history.TryReleaseNative(foreign,999),"every full owner identity substitution refuses without changing original history");
        history.Retire();history.Retire();
        check(history.Read(Owner,999)==CustomNativeSelection.Native&&history.Read(Owner,1000)==CustomNativeSelection.Custom
            &&history.Read(Owner,1001)==CustomNativeSelection.Rejected,"retirement preserves sealed historical answers and rejects future source");
        check(!history.Observe(Owner,1001)&&!history.Seal(Owner,1001)&&!history.CanEquip(Owner,1001)
            &&!history.Commit(Owner,1001,default)&&history.Read(Owner,1000)==CustomNativeSelection.Custom,
            "retired history cannot mutate, unseal or reopen custom selection");
    }
    private static void Retention(Action<bool,string> check)
    {
        var edge=new CustomNativeSelectionHistory(Owner,1000);edge.Observe(Owner,1000);edge.Seal(Owner,1000);
        edge.Observe(Owner,1127);edge.Seal(Owner,1127);
        check(edge.Read(Owner,1000)==CustomNativeSelection.Native,"exact source age127 remains inside128-source retained window");
        edge.Observe(Owner,1128);edge.Seal(Owner,1128);
        check(edge.Read(Owner,1000)==CustomNativeSelection.Rejected&&edge.Read(Owner,1001)==CustomNativeSelection.Native,
            "exact source age128 expires without a current-selection fallback");
        var history=new CustomNativeSelectionHistory(Owner,1000);
        for(uint frame=1000;frame<1132;frame++)
            check(history.Observe(Owner,frame)&&history.Commit(Owner,frame,(frame&1)==0?new WeaponRuntimeId(10):default)
                &&history.Seal(Owner,frame),"bounded selection boundary ring reclaims expired entries without dropping current boundary");
        check(history.Read(Owner,1000)==CustomNativeSelection.Rejected&&history.Read(Owner,1001)==CustomNativeSelection.Rejected
            &&history.Read(Owner,1130)==CustomNativeSelection.Custom&&history.Read(Owner,1131)==CustomNativeSelection.Native,
            "expired selection has no current fallback while retained ring tail preserves exact source answers");
        check(history.Observe(Owner,1500)&&history.Seal(Owner,1500)
            &&history.Read(Owner,1500)==CustomNativeSelection.Native&&history.Read(Owner,1131)==CustomNativeSelection.Rejected,
            "long unchanged selection retains its anchor after original boundary ages beyond source window");
    }
    private static void Wrap(Action<bool,string> check)
    {
        uint birth=uint.MaxValue-2;var history=new CustomNativeSelectionHistory(Owner,birth);
        check(history.Observe(Owner,birth)&&history.Seal(Owner,birth)&&history.TryReleaseNative(Owner,birth),
            "native selection/release admits nonzero source near uint wrap");
        check(history.Observe(Owner,uint.MaxValue)&&history.Commit(Owner,uint.MaxValue,new(10))
            &&history.Seal(Owner,uint.MaxValue)&&history.Observe(Owner,0)&&history.Seal(Owner,0)
            &&history.Read(Owner,birth)==CustomNativeSelection.Native&&history.Read(Owner,uint.MaxValue)==CustomNativeSelection.Custom
            &&history.Read(Owner,0)==CustomNativeSelection.Custom&&history.Read(Owner,1)==CustomNativeSelection.Pending,
            "serial uint wrap preserves native/custom boundaries and future pending including valid source zero");
        check(!history.CanEquip(Owner,birth)&&!history.Commit(Owner,birth,new(10)),
            "uint wrap cannot bypass earlier native release fence");
        history.Retire();check(history.Read(Owner,0)==CustomNativeSelection.Custom&&history.Read(Owner,1)==CustomNativeSelection.Rejected,
            "retired wrapped history preserves exact sealed zero source and denies future");
    }
    private sealed class SelectionOracle : ICustomSourceAdmission,ICustomWeaponEmitter
    {
        internal bool Pending;
        internal CustomEmissionDisposition? Emission;
        internal int Emissions;
        public CustomSourceDecision Inspect(in CustomWeaponAction action,out CustomSourceCertificate source)
        {
            // Explicit equal-clock source facts, not engine pose/geometry proof.
            source=new(action,action.SourceFrame,action.SourceFrame,0,Vector3.Zero,Vector3.UnitZ,true,false,false);
            return Pending?CustomSourceDecision.Pending:CustomSourceDecision.Verified;
        }
        public bool HasCapacity(WeaponDefinition definition,WeaponOwnerContext owner)=>true;
        public CustomEmissionDisposition Emit(in CustomAcceptedLaunch launch)
        {Emissions++;return Emission??throw new InvalidOperationException("Selection-only journal test must not emit.");}
    }
    private sealed class JournalFixture : IDisposable
    {
        internal readonly WeaponDefinition Definition=RiftlineM7.Create();
        internal readonly WeaponRegistry Registry;
        internal readonly CustomWeaponManifest Manifest;
        internal readonly CustomSourceActionJournal Journal;
        internal readonly SelectionOracle Oracle=new();
        internal readonly WorldBootstrapIdentity Bootstrap=new(new(Owner.MatchId,Owner.AuthorityEpoch,1),1,Owner.Generation,1000,1,1);
        internal JournalFixture()
        {
            Registry=new(true,new[]{Definition});Manifest=new(new[]{Definition});
            Journal=new(Owner,new CustomSourceClock(Owner,1000,1000),Manifest,Registry,new[]{new WeaponRuntimeId(10)},1000,1000,true);
        }
        internal CustomWeaponAction Action(uint sequence,uint source,CustomWeaponActionKind kind)
            =>new(Bootstrap,Manifest.Digest,Owner.Slot,Owner.Life,new(10),sequence,source,
                kind==CustomWeaponActionKind.Fire?source:0,0,kind,0,
                kind==CustomWeaponActionKind.Fire?new(0,.5f,0):Vector3.Zero,
                kind==CustomWeaponActionKind.Fire?Vector3.UnitZ:Vector3.Zero);
        internal bool Process(uint world,out CustomActionReceipt receipt)=>Journal.TryProcessNext(world,Oracle,Oracle,out receipt);
        public void Dispose()=>Registry.Dispose();
    }
    private static void JournalBoundaries(Action<bool,string> check)
    {
        using(var f=new JournalFixture())
        {
            check(f.Journal.SealNativeSelection()&&f.Journal.ReadNativeSelection(Owner,1000)==CustomNativeSelection.Native,
                "real ordered journal starts with a sealed native selection baseline");
            check(f.Journal.ObserveSource(Owner,1001,1001,true)
                &&f.Journal.Offer(f.Action(1,1001,CustomWeaponActionKind.Equip),1001)==CustomQueueResult.Queued
                &&f.Journal.SealNativeSelection()&&f.Journal.ReadNativeSelection(Owner,1001)==CustomNativeSelection.Pending
                &&!f.Journal.TryReleaseNativeSelection(Owner,1001)&&f.Journal.TryReleaseNativeSelection(Owner,1000),
                "queued equip holds native release while earlier native recovery remains allowed");
            f.Oracle.Pending=true;
            check(!f.Process(1001,out _)&&f.Journal.ReadNativeSelection(Owner,1001)==CustomNativeSelection.Pending,
                "unresolved supplied admission leaves selector pending instead of granting native permission");
            f.Oracle.Pending=false;
            check(f.Process(1001,out var equipped)&&equipped.Result==WeaponActionResult.Accepted
                &&f.Journal.Selected==new WeaponRuntimeId(10)&&f.Journal.ReadNativeSelection(Owner,1001)==CustomNativeSelection.Custom,
                "accepted journal equip commits exact historical custom ownership");
            check(f.Journal.ObserveSource(Owner,1002,1002,true)
                &&f.Journal.Offer(f.Action(2,1002,CustomWeaponActionKind.Unequip),1002)==CustomQueueResult.Queued
                &&f.Journal.SealNativeSelection()&&f.Journal.ReadNativeSelection(Owner,1002)==CustomNativeSelection.Pending
                &&f.Journal.ReadNativeSelection(Owner,1001)==CustomNativeSelection.Custom,
                "queued unequip is pending at its source without erasing earlier custom ownership");
            check(f.Process(1002,out var unequipped)&&unequipped.Result==WeaponActionResult.Accepted
                &&f.Journal.ReadNativeSelection(Owner,1002)==CustomNativeSelection.Native
                &&f.Journal.TryReleaseNativeSelection(Owner,1002),"ordered accepted unequip releases native ownership");
            f.Journal.TryCommitted(new(10),out var before);
            check(f.Journal.Offer(f.Action(3,1002,CustomWeaponActionKind.Equip),1002)==CustomQueueResult.Queued
                &&f.Process(1002,out var denied)&&denied.Disposition==CustomJournalDisposition.SourceDenied
                &&denied.Result==WeaponActionResult.InvalidLife&&f.Journal.SettledSequence==3
                &&f.Journal.TryCommitted(new(10),out var after)&&after==before&&!f.Journal.Selected.IsValid
                &&f.Journal.ReadNativeSelection(Owner,1002)==CustomNativeSelection.Native,
                "journal denies late same-source equip before resource mutation after native release");
            check(f.Journal.ObserveSource(Owner,1003,1003,true)
                &&f.Journal.Offer(f.Action(4,1003,CustomWeaponActionKind.Equip),1003)==CustomQueueResult.Queued
                &&f.Process(1003,out var later)&&later.Result==WeaponActionResult.Accepted&&f.Journal.SealNativeSelection(),
                "native release fence permits next-source journal equip");
            f.Journal.Retire(WeaponCancellation.Death);
            check(f.Journal.ReadNativeSelection(Owner,1003)==CustomNativeSelection.Custom
                &&f.Journal.ReadNativeSelection(Owner,1000)==CustomNativeSelection.Native
                &&f.Journal.TryReleaseNativeSelection(Owner,1000)
                &&f.Journal.ReadNativeSelection(Owner,1004)==CustomNativeSelection.Rejected&&f.Oracle.Emissions==0,
                "retired journal retains accepted selection history without current-state fallback or selector emission");
        }
        using(var gap=new JournalFixture())
        {
            gap.Journal.ObserveSource(Owner,1001,1001,true);
            check(gap.Journal.Offer(gap.Action(2,1001,CustomWeaponActionKind.Equip),1001)==CustomQueueResult.Queued
                &&gap.Journal.SealNativeSelection()&&gap.Journal.ReadNativeSelection(Owner,1001)==CustomNativeSelection.Pending
                &&gap.Journal.ReadNativeSelection(Owner,1000)==CustomNativeSelection.Native&&!gap.Process(1001,out _),
                "out-of-order selector behind a sequence gap cannot release native permission or block earlier sources");
        }
    }
    private static void JournalEmissionRelease(Action<bool,string> check)
    {
        foreach(bool noSpawn in new[]{true,false})
        {
            using var f=new JournalFixture();f.Oracle.Emission=noSpawn?CustomEmissionDisposition.NoSpawn:CustomEmissionDisposition.Spawned;
            check(f.Journal.Offer(f.Action(1,1000,CustomWeaponActionKind.Equip),1000)==CustomQueueResult.Queued
                &&f.Process(1000,out var equip)&&equip.Result==WeaponActionResult.Accepted,
                "supplied-emitter release fixture equips through actual resource journal");
            check(f.Journal.Offer(f.Action(2,1000,CustomWeaponActionKind.Fire),1000)==CustomQueueResult.Queued
                &&f.Process(1000,out var fire)&&fire.Emission==f.Oracle.Emission&&f.Oracle.Emissions==1
                &&f.Journal.SealNativeSelection(),"actual journal observes declared definite/no-spawn emitter result without a physical projectile claim");
            check(f.Journal.Offer(f.Action(3,1000,CustomWeaponActionKind.Unequip),1000)==CustomQueueResult.Queued
                &&f.Process(1000,out var unequip)&&unequip.Disposition==(noSpawn?CustomJournalDisposition.Accepted:CustomJournalDisposition.SourceDenied)
                &&f.Journal.TryCommitted(new(10),out var state)&&state.Magazine==(noSpawn?4:3)
                &&f.Journal.ReadNativeSelection(Owner,1000)==(noSpawn?CustomNativeSelection.Native:CustomNativeSelection.Custom),
                noSpawn?"definite no-spawn refunds ammo and does not freeze same-source custom ownership"
                    :"definite custom emission prevents same-source unequip/native-fire laundering and keeps one-round debit");
            if(!noSpawn)
                check(f.Journal.ObserveSource(Owner,1001,1001,true)
                    &&f.Journal.Offer(f.Action(4,1001,CustomWeaponActionKind.Unequip),1001)==CustomQueueResult.Queued
                    &&f.Process(1001,out var later)&&later.Result==WeaponActionResult.Accepted&&f.Journal.SealNativeSelection()
                    &&f.Journal.TryReleaseNativeSelection(Owner,1001),"ordered journal permits native ownership at source after definite custom emission");
        }
    }
    private static void Allocation(Action<bool,string> check)
    {
        var history=new CustomNativeSelectionHistory(Owner,1000);uint source=1000;bool ok=true;
        void Probe()
        {
            bool custom=(source&1)==0;
            ok&=history.Observe(Owner,source)&&history.Commit(Owner,source,custom?new WeaponRuntimeId(10):default)
                &&history.Seal(Owner,source)&&history.Read(Owner,source)==(custom?CustomNativeSelection.Custom:CustomNativeSelection.Native);
            ok&=custom?history.TryReleaseCustom(Owner,source):history.TryReleaseNative(Owner,source);
            source++;
        }
        for(int i=0;i<10000;i++)Probe();long bytes=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++)Probe();bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
        Console.WriteLine($"NATIVE SELECTION HISTORY ALLOCATION:100000 warmed observe/commit/seal/read/release cycles={bytes}B");
        check(ok&&bytes==0,"bounded native selection history allocates zero through100000 warmed boundary cycles");
    }
}
