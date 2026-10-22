using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

internal static class SourceJournalChecks
{
    // These stubs attest deliberately supplied engine observations. They do not
    // implement or prove historical pose, geometry, modifiers or trade admission.
    internal sealed class Admission(Fixture fixture) : ICustomSourceAdmission
    {
        internal CustomSourceDecision Decision = CustomSourceDecision.Verified;
        internal Func<CustomSourceCertificate,CustomSourceCertificate>? Alter;
        internal Action? DuringInspect;
        internal bool Throws;
        internal int Calls;
        public CustomSourceDecision Inspect(in CustomWeaponAction action, out CustomSourceCertificate source)
        {
            Calls++; DuringInspect?.Invoke();
            if (Throws) throw new InvalidOperationException("Explicit test oracle failure.");
            var observation = fixture.Observations[action.SourceFrame];
            source = new(action,observation.World,fixture.Now,0,Vector3.Zero,new(0,0,-1),observation.CanUse,false,false);
            if (Alter != null) source = Alter(source);
            return Decision;
        }
    }
    internal sealed class Emitter : ICustomWeaponEmitter
    {
        internal bool Capacity = true, Throws, CapacityThrows;
        internal CustomEmissionDisposition Disposition = CustomEmissionDisposition.Spawned;
        internal int CapacityCalls, Calls;
        internal CustomAcceptedLaunch? Launch;
        public bool HasCapacity(WeaponDefinition definition, WeaponOwnerContext owner)
        {
            CapacityCalls++;
            if (CapacityThrows) throw new InvalidOperationException("Explicit test capacity failure.");
            return Capacity;
        }
        public CustomEmissionDisposition Emit(in CustomAcceptedLaunch launch)
        {
            Calls++; Launch = launch;
            if (Throws) throw new InvalidOperationException("Explicit uncertain test emission.");
            return Disposition;
        }
    }
    // Only for the bounded allocation measurement: the loop supplies exact
    // engine observations with equal source/world clocks. No dictionary or
    // delegate is read or grown on this path; historical engine checks remain stubbed.
    private sealed class HotAdmission : ICustomSourceAdmission
    {
        internal uint World;
        public CustomSourceDecision Inspect(in CustomWeaponAction action, out CustomSourceCertificate source)
        {
            source = new(action,World,World,0,Vector3.Zero,new(0,0,-1),true,false,false);
            return CustomSourceDecision.Verified;
        }
    }
    internal sealed class Fixture : IDisposable
    {
        internal readonly Dictionary<uint,(uint World,bool CanUse)> Observations = new();
        internal readonly WeaponRegistry Registry;
        internal readonly CustomWeaponManifest Manifest;
        internal readonly CustomSourceClock Clock;
        internal readonly CustomSourceActionJournal Journal;
        internal readonly Admission Oracle;
        internal readonly Emitter Engine = new();
        internal readonly WorldBootstrapIdentity Bootstrap;
        internal readonly CustomPickupWorld? PickupWorld;
        internal uint Now;
        internal Fixture(uint birth = 100, uint world = 100, ushort life = 1,
            CustomSourceClock? clock = null, bool initialCanUse = true, WeaponRuntimeId[]? loadout = null, bool pickupFunding = false)
        {
            var rules = MechanicsChecks.Rules();
            var definitions = new[]
            {
                WeaponDefinition.Create(Guid.Parse("00000001-0000-0000-0000-000000000001"),"Journal A",rules),
                WeaponDefinition.Create(Guid.Parse("00000002-0000-0000-0000-000000000001"),"Journal B",rules)
            };
            Registry = new(true,definitions); Manifest = new(definitions);
            var owner = new WeaponOwnerContext(1,1,0,1,life);
            Clock = clock ?? new(owner,birth,world);
            Bootstrap = new(new(1,1,1),1,1,1,1,1); Now = world;
            PickupWorld = pickupFunding ? new(new(1,1,1),Manifest.Digest,new(1,2,3,4)) : null;
            try { Journal = new(owner,Clock,Manifest,Registry,loadout ?? new[] { new WeaponRuntimeId(10) },birth,world,initialCanUse,PickupWorld); }
            catch { Registry.Dispose(); throw; }
            Observations.Add(birth,(world,initialCanUse)); Oracle = new(this);
        }
        internal CustomWeaponAction Action(uint sequence, uint source, CustomWeaponActionKind kind = CustomWeaponActionKind.Fire,
            ushort weapon = 10, byte optic = 0)
            => new(Bootstrap,Manifest.Digest,Journal.Owner.Slot,Journal.Owner.Life,new(weapon),sequence,source,
                kind == CustomWeaponActionKind.Fire ? Now : 0,0,kind,optic,
                kind == CustomWeaponActionKind.Fire ? new(0,.5f,0) : Vector3.Zero,
                kind == CustomWeaponActionKind.Fire ? new(0,0,-1) : Vector3.Zero);
        internal bool Observe(uint source, uint world, bool canUse = true)
        {
            Now = world;
            bool accepted = Journal.ObserveSource(Journal.Owner,source,world,canUse);
            if (accepted && !Observations.ContainsKey(source)) Observations.Add(source,(world,canUse));
            return accepted;
        }
        internal CustomQueueResult Offer(CustomWeaponAction action, uint world)
        { Now = world; return Journal.Offer(action,world); }
        internal bool Process(uint world, out CustomActionReceipt receipt)
        { Now = world; return Journal.TryProcessNext(world,Oracle,Engine,out receipt); }
        internal CustomActionReceipt Take(uint world, Action<bool,string> check, string label)
        { bool processed = Process(world,out var receipt); check(processed,label); return receipt; }
        internal WeaponStateSnapshot State(ushort weapon = 10)
        {
            if (!Journal.TryCommitted(new(weapon),out var state)) throw new Exception("Missing owned test state.");
            return state;
        }
        internal void Equip(Action<bool,string> check)
        {
            uint source = Journal.ObservedSourceFrame;
            check(Offer(Action(1,source,CustomWeaponActionKind.Equip),Now) == CustomQueueResult.Queued,"journal fixture queues explicit equip");
            check(Take(Now,check,"journal fixture processes explicit equip").Disposition == CustomJournalDisposition.Accepted,
                "journal fixture equips owned initial loadout");
        }
        internal void Reloading(Action<bool,string> check)
        {
            Equip(check);
            Offer(Action(2,100),100); Take(100,check,"reload fixture fires initial round");
            Observe(101,101); Offer(Action(3,101,CustomWeaponActionKind.Reload),101);
            check(Take(101,check,"reload fixture processes reload").Result == WeaponActionResult.Accepted,"reload fixture starts reload");
        }
        public void Dispose() => Registry.Dispose();
    }

    internal static void Run(Action<bool,string> check)
    {
        using (var f = new Fixture())
        {
            f.Equip(check);
            var fire = f.Action(2,100); f.Offer(fire,100);
            var accepted = f.Take(100,check,"initial custom fire processes");
            check(accepted.Disposition == CustomJournalDisposition.Accepted && accepted.Emission == CustomEmissionDisposition.Spawned
                && accepted.Before?.Magazine == 4 && accepted.After?.Magazine == 3 && f.Engine.Calls == 1,
                "accepted receipt freezes source and exact one-round transition");
            var launch = f.Engine.Launch!.Value;
            check(launch.Identity == CustomActionIdentity.For(fire) && launch.Component == 1
                && launch.Definition.Identity == f.Manifest.Rows[0].Identity && launch.Source == accepted.Source
                && launch.Before == accepted.Before && launch.After == accepted.After,"launch retains exact immutable definition/action/source evidence");
            f.Observe(101,101); f.Observe(110,110);
            var reload = f.Action(3,101,CustomWeaponActionKind.Reload); var delayedFire = f.Action(4,110);
            check(f.Offer(delayedFire,110) == CustomQueueResult.Queued && !f.Process(110,out _)
                && f.Journal.SettledSequence == 2,"reordered newer fire waits for missing reload sequence");
            f.Offer(reload,110);
            check(f.Take(110,check,"recovered reload processes first").Result == WeaponActionResult.Accepted
                && f.Take(110,check,"newer fire processes after reload").Result == WeaponActionResult.Reloading
                && f.Engine.Calls == 1,"reordered reload cannot retroactively authorize fire");
            check(f.Journal.TryGetReceipt(2,out var retained) && retained == accepted
                && f.Offer(fire,111) == CustomQueueResult.Duplicate && !f.Process(111,out _)
                && f.Engine.Calls == 1,"duplicate retry returns original immutable receipt without another emission");
        }
        using (var f = new Fixture())
        {
            f.Equip(check);
            var fire = f.Action(3,100); var unequip = f.Action(2,100,CustomWeaponActionKind.Unequip);
            f.Offer(fire,100); check(!f.Process(100,out _),"equal-tick fire waits for prior unequip"); f.Offer(unequip,100);
            check(f.Take(100,check,"equal-tick unequip processes").Result == WeaponActionResult.Accepted
                && f.Take(100,check,"equal-tick fire follows unequip").Result == WeaponActionResult.Unequipped
                && !f.Journal.Selected.IsValid && f.Engine.Calls == 0,"equal source frames follow universal action sequence");
        }
        using (var f = new Fixture(loadout: new[] { new WeaponRuntimeId(10),new WeaponRuntimeId(11) }))
        {
            f.Equip(check); var original = f.Action(2,100);
            check(f.Offer(original,100) == CustomQueueResult.Queued
                && f.Offer(original with { Kind = CustomWeaponActionKind.Equip, Origin = Vector3.Zero, Direction = Vector3.Zero, AckFrame = 0 },100) == CustomQueueResult.Conflict
                && f.Offer(original with { Weapon = new(11) },100) == CustomQueueResult.Conflict,
                "pending sequence conflicts cannot change action kind or owned weapon");
            f.Take(100,check,"original conflicting sequence still processes");
            check(f.Offer(original with { Weapon = new(11) },101) == CustomQueueResult.Conflict
                && f.Offer(original with { SourceFrame = 101 },101) == CustomQueueResult.Conflict
                && f.Engine.Calls == 1 && f.Journal.SettledSequence == 2,"settled global sequence cannot revive under another weapon or tick");
        }
        using (var f = new Fixture())
        {
            var action = f.Action(1,100,CustomWeaponActionKind.Equip);
            check(f.Offer(action with { Life = 2 },100) == CustomQueueResult.Foreign
                && f.Offer(action with { Manifest = new(9,8,7,6) },100) == CustomQueueResult.Foreign
                && f.Offer(action with { Slot = 1 },100) == CustomQueueResult.Foreign
                && f.Journal.PendingCount == 0,"foreign owner life/slot/manifest cannot enter journal");
            f.Offer(action with { Weapon = new(11) },100);
            check(f.Take(100,check,"unowned registered weapon settles").Disposition == CustomJournalDisposition.NotOwned
                && f.Oracle.Calls == 0 && f.Engine.Calls == 0,"registry presence does not grant initial loadout ownership");
        }
        var loadout = new[] { new WeaponRuntimeId(10) };
        using (var f = new Fixture(loadout: loadout))
        {
            loadout[0] = new(11); f.Equip(check);
            check(f.Journal.TryCommitted(new(10),out _) && !f.Journal.TryCommitted(new(11),out _),"initial loadout is detached from caller array");
        }

        using (var f = new Fixture())
        {
            f.Equip(check); var future = f.Action(2,140); f.Offer(future,100);
            check(!f.Process(100,out _) && f.Journal.ObservedSourceFrame == 100 && f.Journal.CommittedSourceFrame == 100
                && !f.Observe(140,100) && f.Engine.Calls == 0,"claimed future action cannot advance source watermark or clock");
            check(f.Observe(140,140),"later verified engine observation admits source time");
            check(f.Take(140,check,"future action settles after engine catch-up").Disposition == CustomJournalDisposition.Accepted,
                "future action waits for verified source rather than authorizing itself");
        }
        using (var f = new Fixture())
        {
            f.Equip(check); f.Observe(150,150); f.Offer(f.Action(2,125),150);
            check(!f.Process(150,out _) && f.Oracle.Calls == 1,"frontier does not substitute for exact source permission");
            check(f.Observe(125,151),"retained exact historical permission may arrive late");
            check(f.Take(151,check,"exact permission unblocks action").Disposition == CustomJournalDisposition.Accepted,
                "late exact permission authorizes only its own source frame");
        }
        foreach (uint age in new uint[] { 64,65 })
        using (var f = new Fixture())
        {
            f.Equip(check); f.Observe(100+age,100+age); f.Offer(f.Action(2,100),100+age);
            var receipt = f.Take(100+age,check,"source age boundary processes " + age);
            check(receipt.Disposition == (age == 64 ? CustomJournalDisposition.Accepted : CustomJournalDisposition.StaleSource),
                "source age is bounded independently of arrival " + age);
        }
        using (var f = new Fixture())
        {
            f.Equip(check); f.Observe(120,120); f.Offer(f.Action(2,120,CustomWeaponActionKind.Optic),120); f.Take(120,check,"commit newer source");
            f.Observe(119,121); f.Offer(f.Action(3,119),121);
            check(f.Take(121,check,"backdated action settles").Disposition == CustomJournalDisposition.StaleSource
                && f.Journal.CommittedSourceFrame == 120 && f.State().Magazine == 4,"action cannot backdate committed source state");
        }
        using (var f = new Fixture())
        {
            f.Offer(f.Action(1,99),100);
            check(f.Take(100,check,"pre-life action settles").Disposition == CustomJournalDisposition.StaleSource
                && f.Journal.CommittedSourceFrame == 100,"source before verified life birth is denied");
            check(!f.Observe(133,100) && f.Journal.ObservedSourceFrame == 100 && f.Observe(132,100)
                && !f.Observe(133,100) && f.Observe(133,101),"fixed clock anchor permits at most32 ticks of source acceleration");
            check(!f.Observe(134,100) && f.Offer(f.Action(2,133),100) == CustomQueueResult.StaleAuthority,
                "authority time cannot rewind observation or ingress budgets");
        }
        using (var f = new Fixture(birth: 10000,world: 100))
        {
            f.Equip(check); f.Observe(10010,110); f.Offer(f.Action(2,10010),110);
            var receipt = f.Take(110,check,"offset source clock fire processes");
            check(receipt.Disposition == CustomJournalDisposition.Accepted && receipt.Source?.ObservedWorldFrame == 110
                && receipt.Source?.TargetWorldFrame == 110,"source/observation/target clocks remain distinct under large offset");
        }
        uint wrapBirth = uint.MaxValue-10;
        using (var f = new Fixture(wrapBirth,wrapBirth))
        {
            f.Equip(check); uint afterWrap = unchecked(wrapBirth+20); f.Observe(afterWrap,afterWrap);
            f.Offer(f.Action(2,afterWrap),afterWrap);
            check(f.Take(afterWrap,check,"wrapped source action processes").Disposition == CustomJournalDisposition.Accepted
                && f.Journal.CommittedSourceFrame == afterWrap && !f.Observe(unchecked(wrapBirth-1),afterWrap),
                "source, authority, clock and life boundary compare correctly across uint wrap");
        }
        using (var first = new Fixture())
        {
            var newerOwner = new WeaponOwnerContext(1,1,0,1,2);
            check(!first.Journal.ObserveSource(newerOwner,101,101,false) && !first.Journal.IsRetired
                && first.Journal.ObservedSourceFrame == 100 && first.Observe(101,101),
                "new-life observation cannot advance or restrict retained old-life journal");
            bool duplicateRefused = false;
            try { using var duplicate = new Fixture(101,101,1,first.Clock); }
            catch (ArgumentException) { duplicateRefused = true; }
            check(duplicateRefused,"shared clock refuses same-life journal reconstruction and fresh resources");
            first.Journal.Retire(WeaponCancellation.Death);
            duplicateRefused = false;
            try { using var duplicate = new Fixture(101,101,1,first.Clock); }
            catch (ArgumentException) { duplicateRefused = true; }
            check(duplicateRefused,"retirement cannot release once-per-life resource grant fence");
            using var next = new Fixture(133,101,2,first.Clock);
            check(next.Journal.Owner.Life == 2 && next.Observe(134,102),"shared occupant clock survives respawn at allowed offset");
            check(first.Clock.Observe(first.Journal.Owner,135,103),"clock observation remains life-agnostic for retained old-life flights");
            bool refused = false;
            try { using var invalid = new Fixture(200,103,3,first.Clock); }
            catch (ArgumentException) { refused = true; }
            check(refused,"respawn cannot reset source clock anchor to legitimize acceleration");
        }
        var unboundClock = new CustomSourceClock(new(1,1,0,1,1),100,100);
        bool invalidLoadoutRefused = false;
        try { using var invalid = new Fixture(clock: unboundClock,loadout: new[] { new WeaponRuntimeId(10),new WeaponRuntimeId(10) }); }
        catch (ArgumentException) { invalidLoadoutRefused = true; }
        using (var valid = new Fixture(clock: unboundClock))
            check(invalidLoadoutRefused && valid.Journal.Owner.Life == 1 && valid.State().Magazine == 4,
                "invalid initial loadout cannot consume otherwise valid once-per-life binding");
        using (var old = new Fixture(life: ushort.MaxValue))
        using (var wrapped = new Fixture(life: 1,clock: old.Clock))
            check(wrapped.Journal.Owner.Life == 1,"strict shared-clock life ordering permits nonzero ushort wrap");

        WorldEvidenceChecks(check);
        BudgetAndRetentionChecks(check);
        EmissionChecks(check);
        InterruptionChecks(check);
        AllocationChecks(check);
    }

    private static void WorldEvidenceChecks(Action<bool,string> check)
    {
        using (var f = new Fixture())
        {
            f.Equip(check); f.Offer(f.Action(2,100),165);
            check(f.Take(165,check,"stalled frontier observation expires").Disposition == CustomJournalDisposition.StaleSource
                && f.Engine.Calls == 0,"stalled source frontier cannot preserve observation authority forever");
        }
        foreach (string kind in new[] { "future observation","old target","future target","future fractional target","mismatched action","invalid aim","invalid muzzle","permission disagreement" })
        using (var f = new Fixture())
        {
            f.Equip(check); f.Oracle.Alter = source => kind switch
            {
                "future observation" => source with { ObservedWorldFrame = 101 },
                "old target" => source with { TargetWorldFrame = 35 },
                "future target" => source with { TargetWorldFrame = 101 },
                "future fractional target" => source with { TargetWorldSubFrame = 1 },
                "mismatched action" => source with { Action = source.Action with { Sequence = 3 } },
                "invalid aim" => source with { Aim = Vector3.Zero },
                "invalid muzzle" => source with { Body = new(100,0,0) },
                _ => source with { CanUseWeapon = false }
            };
            f.Offer(f.Action(2,100),100); var receipt = f.Take(100,check,"bad certificate settles: " + kind);
            check(receipt.Disposition == (kind is "future observation" or "old target" or "future target" or "future fractional target"
                    ? CustomJournalDisposition.StaleSource : CustomJournalDisposition.SourceDenied)
                && f.State().Magazine == 4 && f.Engine.Calls == 0,"certificate cannot bypass bounded source validation: " + kind);
        }
        using (var f = new Fixture())
        {
            f.Equip(check); f.Oracle.Decision = CustomSourceDecision.Rejected; f.Offer(f.Action(2,100),100);
            check(f.Take(100,check,"trusted rejection settles").Disposition == CustomJournalDisposition.SourceDenied
                && f.State().Tick == 100 && f.Engine.Calls == 0,"trusted engine rejection does not debit or emit");
        }
        foreach (bool fractional in new[] { false,true })
        using (var f = new Fixture())
        {
            f.Equip(check); f.Oracle.Alter = source => source with
                { TargetWorldFrame = fractional ? 99u : 36u, TargetWorldSubFrame = fractional ? (byte)255 : (byte)0 };
            f.Offer(f.Action(2,100),100);
            check(f.Take(100,check,"bounded historical target processes").Disposition == CustomJournalDisposition.Accepted,
                "target permits exact64-tick history and past fractional sample " + fractional);
        }
        using (var f = new Fixture())
        {
            f.Oracle.Alter = source => source with { ObservedWorldFrame = 101 };
            f.Offer(f.Action(1,100,CustomWeaponActionKind.Equip),100);
            check(f.Take(100,check,"non-fire future observation settles").Disposition == CustomJournalDisposition.StaleSource
                && !f.Journal.Selected.IsValid,"all action kinds require a non-future source observation");
        }
        using (var f = new Fixture())
        {
            f.Equip(check); f.Oracle.Alter = source => source with { CanUseWeapon = false };
            f.Offer(f.Action(2,100,CustomWeaponActionKind.Reload),100);
            check(f.Take(100,check,"non-fire observation mismatch settles").Disposition == CustomJournalDisposition.SourceDenied,
                "reload uses exact permission evidence too");
        }
    }

    private static void BudgetAndRetentionChecks(Action<bool,string> check)
    {
        using (var f = new Fixture())
        {
            for (uint sequence=1; sequence<=8; sequence++)
                check(f.Offer(f.Action(sequence,100,sequence == 1 ? CustomWeaponActionKind.Equip : CustomWeaponActionKind.Optic),100)
                    == CustomQueueResult.Queued,"bounded offer budget accepts first8 " + sequence);
            var ninth = f.Action(9,100,CustomWeaponActionKind.Optic);
            check(f.Offer(ninth,100) == CustomQueueResult.Backpressure && f.Journal.PendingCount == 8
                && !f.Journal.TryGetReceipt(9,out _) && !f.Journal.IsRetired,"offer backpressure is transient and does not burn sequence");
            check(f.Offer(ninth,101) == CustomQueueResult.Queued,"backpressured action may retry next authority tick");
            for (int i=0; i<4; i++) f.Take(101,check,"bounded drain processes action " + i);
            check(!f.Process(101,out _) && f.Journal.SettledSequence == 4 && f.Journal.PendingCount == 5,"drain processes at most4 actions per tick");
            check(f.Process(102,out _) && f.Journal.SettledSequence == 5,"drain resumes next authority tick");
        }
        using (var f = new Fixture())
        {
            f.Oracle.Decision = CustomSourceDecision.Pending; f.Offer(f.Action(1,100,CustomWeaponActionKind.Equip),100);
            for (int i=0; i<12; i++) check(!f.Process(100,out _),"pending oracle never manufactures a terminal receipt " + i);
            check(f.Oracle.Calls == 4 && f.Journal.PendingCount == 1 && f.Journal.SettledSequence == 0,"pending oracle attempts share4-call drain budget");
            check(!f.Process(101,out _) && f.Oracle.Calls == 5,"pending oracle budget resets on next tick");
            f.Oracle.Decision = CustomSourceDecision.Verified;
            check(f.Take(101,check,"pending source recovers").Disposition == CustomJournalDisposition.Accepted,"pending source later settles once");
        }
        using (var f = new Fixture())
        {
            f.Offer(f.Action(2,100),100);
            check(!f.Process(164,out _) && !f.Journal.IsRetired && f.Journal.PendingCount == 1,"missing sequence waits through64-tick boundary");
            check(!f.Process(165,out _) && f.Journal.IsRetired && f.Journal.PendingCount == 0
                && f.Engine.Calls == 0 && f.State().Phase == CustomWeaponPhase.Invalid,"missing sequence timeout retires instead of skipping gap");
        }
        using (var f = new Fixture())
        {
            check(f.Offer(f.Action(17,100),100) == CustomQueueResult.WindowExceeded && f.Journal.IsRetired
                && f.Journal.PendingCount == 0 && f.Offer(f.Action(1,100),101) == CustomQueueResult.Retired,
                "sequence farther than16 fails closed without replacing pending work");
        }
        foreach (bool expire in new[] { false,true })
        using (var f = new Fixture())
        {
            CustomActionReceipt first = default;
            for (uint sequence=1; sequence<=128; sequence++)
            {
                uint world = 100+(sequence-1)/4;
                f.Offer(f.Action(sequence,100,sequence == 1 ? CustomWeaponActionKind.Equip : CustomWeaponActionKind.Optic),world);
                var receipt = f.Take(world,check,"retain bounded terminal " + sequence);
                if (sequence == 1) first = receipt;
                check(receipt.Disposition == CustomJournalDisposition.Accepted,"terminal fill preserves exact sequence " + sequence);
            }
            uint nextWorld = expire ? 229u : 228u;
            f.Observe(nextWorld,nextWorld); f.Offer(f.Action(129,nextWorld,CustomWeaponActionKind.Optic),nextWorld);
            if (expire)
                check(f.Take(nextWorld,check,"expired terminal slot reuses").Disposition == CustomJournalDisposition.Accepted
                    && !f.Journal.IsRetired && !f.Journal.TryGetReceipt(1,out _) && f.Journal.TryGetReceipt(129,out _),
                    "terminal slot may reuse only after retention expires");
            else
                check(!f.Process(nextWorld,out _) && f.Journal.IsRetired && f.Journal.TryGetReceipt(1,out var retained)
                    && retained == first && !f.Journal.TryGetReceipt(129,out _),"terminal saturation at exact128-tick retention never evicts live receipt");
        }
    }

    private static void EmissionChecks(Action<bool,string> check)
    {
        foreach (bool noCapacity in new[] { true,false })
        using (var f = new Fixture())
        {
            f.Reloading(check); f.Observe(239,239); f.Engine.Capacity = !noCapacity;
            f.Engine.Disposition = noCapacity ? CustomEmissionDisposition.Spawned : CustomEmissionDisposition.NoSpawn;
            var denied = f.Action(4,239); f.Offer(denied,239); var receipt = f.Take(239,check,"capacity/no-spawn action settles");
            check(receipt.Disposition == (noCapacity ? CustomJournalDisposition.ResourceDenied : CustomJournalDisposition.NoSpawn)
                && receipt.Result == WeaponActionResult.CapacityUnavailable && receipt.Before?.Magazine == 4
                && receipt.Before?.Reserve == 15 && receipt.After?.Magazine == 4 && receipt.After?.Reserve == 15
                && receipt.After?.LastAcceptedShotId == 2 && f.Journal.CommittedSourceFrame == 239,
                "failed emission retains source-time reload transfer while refunding only fire");
            check(f.Engine.Calls == (noCapacity ? 1 : 2) && f.Offer(denied,240) == CustomQueueResult.Duplicate
                && !f.Process(240,out _),"failed emission burns sequence and never emits on duplicate retry");
            if (!noCapacity)
                check(f.Engine.Launch?.Before.Magazine == 4 && f.Engine.Launch?.After.Magazine == 3,
                    "definite no-spawn adapter saw exactly one tentative debit");
            f.Engine.Capacity = true; f.Engine.Disposition = CustomEmissionDisposition.Spawned;
            f.Offer(f.Action(5,239),240);
            check(f.Take(240,check,"fresh action after failed emission").Disposition == CustomJournalDisposition.Accepted
                && f.State().Magazine == 3 && f.State().Reserve == 15,"fresh sequence may fire after proven no-spawn without phantom cooldown");
        }
        foreach (bool throws in new[] { false,true })
        using (var f = new Fixture())
        {
            f.Equip(check); f.Engine.Throws = throws; f.Engine.Disposition = CustomEmissionDisposition.Uncertain;
            f.Offer(f.Action(2,100),100); var receipt = f.Take(100,check,"uncertain emission settles");
            check(receipt.Disposition == CustomJournalDisposition.EngineFault && receipt.Emission == CustomEmissionDisposition.Uncertain
                && f.Journal.IsRetired && f.State().Magazine == 3 && f.State().LastAcceptedShotId == 2
                && f.State().Phase == CustomWeaponPhase.Invalid && f.Engine.Calls == 1,
                "uncertain or throwing emission never refunds a possibly paid shot " + throws);
        }
        foreach (bool oracleFailure in new[] { true,false })
        using (var f = new Fixture())
        {
            f.Equip(check); f.Oracle.Throws = oracleFailure; f.Engine.CapacityThrows = !oracleFailure;
            f.Offer(f.Action(2,100),100); var receipt = f.Take(100,check,"pre-emission dependency failure settles");
            check(receipt.Disposition == CustomJournalDisposition.EngineFault && receipt.Emission == CustomEmissionDisposition.NotAttempted
                && f.Journal.IsRetired && f.State().Magazine == 4 && f.Engine.Calls == 0,
                "dependency failure before emission retires without inventing fire " + oracleFailure);
        }
        using (var f = new Fixture())
        {
            f.Equip(check); f.Oracle.DuringInspect = () => f.Journal.Retire(WeaponCancellation.OwnerChanged);
            f.Offer(f.Action(2,100),100); var receipt = f.Take(100,check,"reentrant retirement settles");
            check(f.Journal.IsRetired && receipt.Disposition == CustomJournalDisposition.SourceDenied
                && f.Engine.CapacityCalls == 0 && f.Engine.Calls == 0 && f.State().Magazine == 4,
                "reentrant retirement in admission prevents capacity reservation and emission");
        }
        using (var f = new Fixture())
        {
            f.Equip(check); f.Offer(f.Action(2,100,CustomWeaponActionKind.Optic,optic: 1),100); f.Take(100,check,"scope action processes");
            f.Offer(f.Action(3,100,optic: 0),100);
            check(f.Take(100,check,"wrong optic fire settles").Disposition == CustomJournalDisposition.OpticMismatch
                && f.State().Magazine == 4 && f.Engine.Calls == 0,"fire cannot author different optic state from ordered history");
        }
    }

    private static void InterruptionChecks(Action<bool,string> check)
    {
        foreach (bool evict in new[] { false,true })
        using (var f = new Fixture())
        {
            f.Reloading(check); var committed = f.State();
            check(f.Observe(150,150,false) && f.Journal.TryPreview(new(10),out var restricted)
                && restricted.Phase == CustomWeaponPhase.Unequipped && restricted.Magazine == 3 && restricted.Reserve == 16
                && restricted.ReloadCompleteTick == 0 && f.State() == committed,
                "engine form interruption previews reload cancellation without advancing committed history");
            uint resumed = evict ? 278u : 239u;
            f.Observe(resumed,resumed);
            check(f.Journal.TryPreview(new(10),out var preview) && preview.Tick == resumed
                && preview.Phase == CustomWeaponPhase.Ready && preview.Magazine == 3 && preview.Reserve == 16
                && preview.ReloadCompleteTick == 0 && f.State() == committed,
                "oldest interruption survives resumed biped observation and permission-ring eviction " + evict);
            f.Offer(f.Action(4,resumed),resumed); var receipt = f.Take(resumed,check,"fire after verified biped resumption");
            check(receipt.Disposition == CustomJournalDisposition.Accepted && receipt.Before?.Magazine == 3
                && receipt.Before?.Reserve == 16 && receipt.After?.Magazine == 2 && receipt.After?.Reserve == 16
                && f.Journal.Selected == new WeaponRuntimeId(10),"selected custom weapon resumes without completing interrupted reload " + evict);
        }
        using (var f = new Fixture())
        {
            f.Reloading(check); f.Observe(240,240,false);
            check(f.Journal.TryPreview(new(10),out var preview) && preview.Magazine == 4 && preview.Reserve == 15
                && preview.Phase == CustomWeaponPhase.Unequipped && f.State().Magazine == 3,
                "interruption after reload deadline preserves legitimate projected completion");
        }
        using (var f = new Fixture())
        {
            f.Equip(check); f.Observe(120,120); f.Offer(f.Action(2,120,CustomWeaponActionKind.Optic),120); f.Take(120,check,"commit before late restriction");
            check(!f.Observe(110,121,false) && f.Journal.IsRetired && f.State().Phase == CustomWeaponPhase.Invalid,
                "novel late restriction before committed source retires instead of rewriting history");
        }
        using (var f = new Fixture())
        {
            f.Equip(check); f.Observe(300,300); f.Offer(f.Action(2,300,CustomWeaponActionKind.Optic),300); f.Take(300,check,"commit beyond permission retention");
            check(!f.Observe(150,301,false) && f.Journal.IsRetired,
                "novel pre-commit restriction outside permission ring still invalidates incomplete committed history");
        }
        using (var f = new Fixture(initialCanUse: false))
        {
            f.Offer(f.Action(1,100,CustomWeaponActionKind.Equip),100);
            check(f.Take(100,check,"restricted initial equip settles").Result == WeaponActionResult.Unequipped
                && !f.Journal.Selected.IsValid,"life-start form restriction does not equip weapon");
            f.Observe(101,101); f.Offer(f.Action(2,101,CustomWeaponActionKind.Equip),101);
            check(f.Take(101,check,"verified biped explicitly equips").Result == WeaponActionResult.Accepted
                && f.State().Magazine == 4,"verified later biped may equip initial owned loadout");
        }
        using (var f = new Fixture())
        {
            f.Equip(check); f.Journal.TryGetReceipt(1,out var firstProof); var committed = f.State();
            check(f.Observe(100,100) && f.State() == committed && !f.Journal.IsRetired,
                "identical exact permission observation is idempotent");
            check(!f.Observe(100,101,false) && f.Journal.IsRetired
                && f.Journal.TryGetReceipt(1,out var retained) && retained == firstProof
                && retained.Source?.CanUseWeapon == true
                && f.Offer(f.Action(2,100),101) == CustomQueueResult.Retired && !f.Process(101,out _)
                && f.Engine.Calls == 0 && f.State().Magazine == 4,
                "contradictory exact permission retires without replacing first proof or emitting fire");
        }
    }

    private static void AllocationChecks(Action<bool,string> check)
    {
        using var fixture = new Fixture();
        var owner = new WeaponOwnerContext(1,1,0,1,2);
        var clock = new CustomSourceClock(owner,100,100);
        var loadout = new[] { new WeaponRuntimeId(10) };
        long before = GC.GetAllocatedBytesForCurrentThread();
        var journal = new CustomSourceActionJournal(owner,clock,fixture.Manifest,fixture.Registry,loadout,100,100,true);
        long constructorBytes = GC.GetAllocatedBytesForCurrentThread()-before;
        Console.WriteLine($"Custom source journal construction: {constructorBytes} allocated bytes (informational; registry/manifest/clock/loadout prebuilt).");
        var admission = new HotAdmission { World = 100 };
        var emitter = new Emitter();
        var template = new CustomWeaponAction(fixture.Bootstrap,fixture.Manifest.Digest,0,2,new(10),1,100,0,0,
            CustomWeaponActionKind.Equip,0,Vector3.Zero,Vector3.Zero);
        check(journal.Offer(template,100) == CustomQueueResult.Queued
            && journal.TryProcessNext(100,admission,emitter,out var equipped)
            && equipped.Disposition == CustomJournalDisposition.Accepted,"allocation fixture equips preconstructed journal");
        bool allProcessed = true;
        for (uint sequence=2; sequence<20002; sequence++)
        {
            uint world = 100+2*(sequence-1); admission.World = world;
            var action = template with { Sequence = sequence, SourceFrame = world, Kind = CustomWeaponActionKind.Optic, Optic = (byte)(sequence%3) };
            allProcessed &= journal.ObserveSource(owner,world,world,true) && journal.Offer(action,world) == CustomQueueResult.Queued
                && journal.TryProcessNext(world,admission,emitter,out var receipt)
                && receipt.Disposition == CustomJournalDisposition.Accepted && receipt.Action == action;
        }
        check(allProcessed,"journal warmup processes20000 exact observed optic actions");
        before = GC.GetAllocatedBytesForCurrentThread();
        for (uint sequence=20002; sequence<120002; sequence++)
        {
            // Two ticks per operation makes terminal-slot reuse256 ticks old,
            // beyond the128-tick retention bound, without changing capacity rules.
            uint world = 100+2*(sequence-1); admission.World = world;
            var action = template with { Sequence = sequence, SourceFrame = world, Kind = CustomWeaponActionKind.Optic, Optic = (byte)(sequence%3) };
            allProcessed &= journal.ObserveSource(owner,world,world,true) && journal.Offer(action,world) == CustomQueueResult.Queued
                && journal.TryProcessNext(world,admission,emitter,out var receipt)
                && receipt.Disposition == CustomJournalDisposition.Accepted && receipt.Action == action;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread()-before;
        check(allProcessed && allocated == 0 && journal.SettledSequence == 120001 && journal.PendingCount == 0
            && !journal.IsRetired && journal.TryCommitted(new(10),out var state) && state.Magazine == 4 && state.Reserve == 16
            && state.Tick == 240100 && emitter.Calls == 0,
            "100000 warmed observed/queued/processed journal optic actions allocate zero bytes and settle exact receipts");
    }
}
