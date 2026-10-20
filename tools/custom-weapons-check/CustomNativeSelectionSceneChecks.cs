using MphRead;
using MphRead.Entities;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

internal static partial class CustomAuthoritySceneChecks
{
    private static void Selection(Fixture f,uint source,CustomNativeSelection expected,string label)
        => Check(f.Root.ReadNativeSourceSelection(f.Owner,source)==expected,label);
    private static void NativeSelectionAccepted()
    {
        using var f=new Fixture(engineSources:true);
        Selection(f,1000,CustomNativeSelection.Pending,"life birth has no native permission before actual source-phase seal");
        Check(!f.Root.TryReleaseNativeSourceSelection(f.Owner,1000),"unsealed source cannot release native emission permission");
        CaptureEngineInput(f,1000);
        Selection(f,1000,CustomNativeSelection.Native,"first genuine neutral source seals native ownership");
        Check(f.Root.TryReleaseNativeSourceSelection(f.Owner,1000),"sealed historical native source can be released");
        var equip=f.EngineAction(CustomWeaponActionKind.Equip,1001);
        Check(f.Root.Offer(equip)==CustomQueueResult.Queued,"future source equip queues before actual native input is consumed");
        Selection(f,1001,CustomNativeSelection.Pending,"unobserved queued equip cannot borrow old canonical native selection");
        Selection(f,1000,CustomNativeSelection.Native,"queued later equip preserves already released earlier native source");
        CaptureEngineInput(f,1001);
        Check(f.Root.TryState(f.Shooter,out var before)&&before.Phase==CustomWeaponPhase.Unequipped,
            "source phase does not silently apply manually queued fixture action");
        Selection(f,1001,CustomNativeSelection.Pending,"sealed source remains pending while its queued equip is unsettled");
        Check(!f.Root.TryReleaseNativeSourceSelection(f.Owner,1001),"pending equip prevents native release at the same source");
        Check(f.Root.TryProcess(f.Shooter,out var accepted)&&accepted.Action==equip
            &&accepted.Result==WeaponActionResult.Accepted&&accepted.Disposition==CustomJournalDisposition.Accepted,
            "production engine source admission accepts queued equip without a supplied certificate");
        Selection(f,1001,CustomNativeSelection.Custom,"accepted equip owns its exact historical source");
        Selection(f,1000,CustomNativeSelection.Native,"accepted equip cannot rewrite prior native source");
        CaptureEngineInput(f,1002);
        Selection(f,1002,CustomNativeSelection.Custom,"later actual source inherits accepted custom selection");
        Check(!f.Root.TryReleaseNativeSourceSelection(f.Owner,1002),"custom-owned source cannot release native permission");
        var unequip=f.EngineAction(CustomWeaponActionKind.Unequip,1003);
        Check(f.Root.Offer(unequip)==CustomQueueResult.Queued,"ordered unequip queues for a genuinely fresh source");
        CaptureEngineInput(f,1003);
        Selection(f,1003,CustomNativeSelection.Pending,"queued unequip keeps its sealed source pending until canonical settlement");
        Check(f.Root.TryProcess(f.Shooter,out var unselected)&&unselected.Result==WeaponActionResult.Accepted,
            "actual source-admitted unequip settles through the ordered journal");
        Selection(f,1003,CustomNativeSelection.Native,"accepted unequip returns exact source to native ownership");
        Selection(f,1002,CustomNativeSelection.Custom,"unequip preserves earlier custom ownership");
        Check(f.Root.TryReleaseNativeSourceSelection(f.Owner,1003)
            &&f.Root.TryState(f.Shooter,out var after)&&after.Phase==CustomWeaponPhase.Unequipped
            &&after.Magazine==before.Magazine&&after.Reserve==before.Reserve
            &&f.Oracle.Supplied.Count==0&&f.Root.AcceptedEmissions==0&&f.Root.PositivePayments==0
            &&f.Shooter.Health==200&&f.Victim.Health==200&&f.StepFailures==0,
            "selection-only flow releases native permission without source oracle certificates, ammo payment or health effects");
    }
    private static void NativeSelectionReleased()
    {
        using var f=new Fixture(engineSources:true);
        CaptureEngineInput(f,1000); CaptureEngineInput(f,1001);
        Check(f.Root.TryState(f.Shooter,out var before)&&f.Root.TryReleaseNativeSourceSelection(f.Owner,1001),
            "actual sealed native source is released before a late raw equip arrives");
        var late=f.EngineAction(CustomWeaponActionKind.Equip,1001);
        Check(f.Root.Offer(late)==CustomQueueResult.Queued,"late exact-source equip enters the bounded ordered journal");
        Selection(f,1001,CustomNativeSelection.Pending,"late queued equip is pending until its terminal released-source decision");
        Check(f.Root.TryProcess(f.Shooter,out var denied)&&denied.Action==late
            &&denied.Disposition==CustomJournalDisposition.SourceDenied&&denied.Result==WeaponActionResult.InvalidLife
            &&denied.Emission==CustomEmissionDisposition.NotAttempted
            &&f.Root.TryState(f.Shooter,out var after)&&after==before,
            "late equip at a released native source is terminal SourceDenied without any state or ammo mutation");
        Selection(f,1001,CustomNativeSelection.Native,"terminal late equip cannot revoke released native ownership");
        Check(f.Root.Offer(late)==CustomQueueResult.Duplicate&&!f.Root.TryProcess(f.Shooter,out _)
            &&f.Root.TryReceipt(late,out var retained)&&retained==denied,
            "duplicate late equip preserves its immutable denial and cannot promote or execute twice");
        CaptureEngineInput(f,1002);
        var fresh=f.Process(f.EngineAction(CustomWeaponActionKind.Equip,1002));
        Check(fresh.Result==WeaponActionResult.Accepted,"next fresh source equip recovers after terminal released-source denial");
        Selection(f,1002,CustomNativeSelection.Custom,"later accepted equip owns only its new source");
        Selection(f,1001,CustomNativeSelection.Native,"later successful equip still preserves released source history");
    }
    private static void NativeSelectionDenied()
    {
        using var f=new Fixture(engineSources:true);
        CaptureEngineInput(f,1000);
        Field(f.Shooter,"_frozenTimer",(ushort)60);
        var equip=f.EngineAction(CustomWeaponActionKind.Equip,1001);
        Check(f.Root.Offer(equip)==CustomQueueResult.Queued,"frozen-source equip queues without claiming usable source permission");
        CaptureEngineInput(f,1001);
        Selection(f,1001,CustomNativeSelection.Pending,"denied actual source with queued equip stays pending until settlement");
        Check(f.Root.ReadSourceObservation(f.Owner,1001,out _)==CustomSourceObservationStatus.Rejected
            &&!f.Root.TryGetSourceObservation(f.Owner,1001,out _),
            "actual frozen placement records a trusted denial without fabricating usable pose history");
        Check(f.Root.TryProcess(f.Shooter,out var denied)&&denied.Result!=WeaponActionResult.Accepted
            &&denied.Emission==CustomEmissionDisposition.NotAttempted
            &&f.Root.TryState(f.Shooter,out var state)&&state.Phase==CustomWeaponPhase.Unequipped
            &&state.Magazine==4&&state.Reserve==16&&f.Root.AcceptedEmissions==0,
            "denied source equip grants no custom selection and spends no resources");
        Selection(f,1001,CustomNativeSelection.Native,"settled denied equip leaves selection ownership native independently of weapon usability");
        Field(f.Shooter,"_frozenTimer",(ushort)0); CaptureEngineInput(f,1002);
        Selection(f,1002,CustomNativeSelection.Native,"thaw cannot retroactively promote denied equip to custom ownership");
    }
    private static void NativeSelectionFire()
    {
        using var f=new Fixture(engineSources:true); EquipEngine(f);
        // Explicit current-world ACK uses the genuinely recorded world sample,
        // avoiding rewind catch-up so selection is tested before real collision.
        CaptureEngineInput(f,1001,shoot:true,ack:1001);
        var fire=f.EngineAction(CustomWeaponActionKind.Fire,1001,1001,optic:1);
        var emitted=f.Process(fire);
        Check(f.Root.TryState(f.Shooter,out var spent)&&emitted.Result==WeaponActionResult.Accepted
            &&emitted.Emission==CustomEmissionDisposition.Spawned&&spent.Magazine==3&&spent.Reserve==16,
            "production engine source admission actually emits one private M7 and commits one round");
        var beam=f.Pool.Single(b=>b.ModCustomShot?.Sequence==fire.Sequence);
        Console.WriteLine($"CUSTOM SELECTION FIRE DIAG receipt={emitted} state={spent} emissions={f.Root.AcceptedEmissions} contacts={f.Root.TerminalContacts} payments={f.Root.PositivePayments} owns={f.Root.OwnsFlight(beam)} hp={f.Victim.Health} damageId={NetDamage.Sequence(f.Victim.SlotIndex)}");
        Selection(f,1001,CustomNativeSelection.Custom,"actual emitted fire locks custom ownership of its source");
        var late=f.Process(f.EngineAction(CustomWeaponActionKind.Unequip,1001));
        f.Root.TryState(f.Shooter,out var diagnosticAfter);
        Console.WriteLine($"CUSTOM SELECTION UNEQUIP DIAG receipt={late} state={diagnosticAfter} emissions={f.Root.AcceptedEmissions} contacts={f.Root.TerminalContacts} payments={f.Root.PositivePayments} owns={f.Root.OwnsFlight(beam)} hp={f.Victim.Health} damageId={NetDamage.Sequence(f.Victim.SlotIndex)}");
        Check(late.Disposition==CustomJournalDisposition.SourceDenied&&late.Result==WeaponActionResult.InvalidLife
            &&late.Emission==CustomEmissionDisposition.NotAttempted
            &&f.Root.TryState(f.Shooter,out var unchanged)&&unchanged==spent
            &&f.Root.AcceptedEmissions==1&&f.Root.TerminalContacts==0&&f.Root.PositivePayments==0
            &&f.Root.OwnsFlight(beam)&&f.Victim.Health==200&&NetDamage.Sequence(f.Victim.SlotIndex)==0,
            "same-source unequip after definite custom emission is terminal denied without refund, payment or health mutation");
        Selection(f,1001,CustomNativeSelection.Custom,"denied same-source unequip cannot reopen native emission after custom fire");
        Check(!f.Root.TryReleaseNativeSourceSelection(f.Owner,1001),"paid launch source cannot also release native permission");
        CaptureEngineInput(f,1002);
        var later=f.Process(f.EngineAction(CustomWeaponActionKind.Unequip,1002));
        Check(later.Result==WeaponActionResult.Accepted&&f.Root.TryState(f.Shooter,out var unselected)
            &&unselected.Phase==CustomWeaponPhase.Unequipped&&unselected.Magazine==3&&unselected.Reserve==16
            &&f.Root.AcceptedEmissions==1&&f.Root.TerminalContacts==0&&f.Root.PositivePayments==0,
            "strictly later genuine source unequips without repaying or refunding the admitted projectile");
        Selection(f,1001,CustomNativeSelection.Custom,"later unequip retains immutable emitted-source custom ownership");
        Selection(f,1002,CustomNativeSelection.Native,"later unequip permits native ownership only at its new source");
        for(int step=0;step<2&&f.Root.TerminalContacts==0;step++)beam.Process();
        Check(f.Victim.Health==135&&f.Root.TerminalContacts==1&&f.Root.PositivePayments==1
            &&f.Root.TryState(f.Shooter,out var afterContact)&&afterContact.Magazine==3&&afterContact.Reserve==16
            &&f.Oracle.Supplied.Count==0,
            "already admitted real projectile still pays one canonical65 contact after later selection change");
    }
    private static void NativeSelectionLifecycle()
    {
        using var f=new Fixture(engineSources:true);
        CaptureEngineInput(f,1000); CaptureEngineInput(f,1001);
        Check(f.Process(f.EngineAction(CustomWeaponActionKind.Equip,1001)).Result==WeaponActionResult.Accepted,
            "lifecycle fixture establishes actual engine-admitted source selection");
        var old=f.Owner; var body=f.Shooter.Position;
        Frame(1002); f.Shooter.Spawn(body,Vector3.UnitZ,Vector3.UnitY,f.Shooter.NodeRef,respawn:true);
        f.Shooter.ModPlaceAt(body); Fixture.Clean(f.Shooter,200); f.Root.Advance();
        var fresh=SourceOwner(f.Shooter);
        Check(fresh.Life!=old.Life&&fresh.Generation==old.Generation,"real native respawn creates a distinct life in the same occupant");
        Check(f.Root.ReadNativeSourceSelection(old,1000)==CustomNativeSelection.Native
            &&f.Root.ReadNativeSourceSelection(old,1001)==CustomNativeSelection.Custom,
            "retired old life retains its sealed historical native and custom ownership");
        Check(f.Root.ReadNativeSourceSelection(old,1002)==CustomNativeSelection.Rejected,
            "retired old life cannot grant permission for a new source");
        Check(f.Root.ReadNativeSourceSelection(fresh,1002)==CustomNativeSelection.Pending,
            "unbound actual new life cannot borrow retired life selection");
        var foreign=new WeaponOwnerContext(old.MatchId,old.AuthorityEpoch,old.Slot,
            NetLifecycleTracker.Next(old.Generation),fresh.Life);
        Check(f.Root.ReadNativeSourceSelection(foreign,1001)==CustomNativeSelection.Rejected
            &&!f.Root.TryReleaseNativeSourceSelection(foreign,1001),
            "new occupant generation cannot borrow old source selection or native release");
        Frame(1066); f.Root.Advance();
        Check(f.Root.ReadNativeSourceSelection(old,1001)==CustomNativeSelection.Custom,
            "retired selection history remains available through the inclusive native 64-world retention");
        Frame(1067); f.Root.Advance();
        Check(f.Root.ReadNativeSourceSelection(old,1001)==CustomNativeSelection.Rejected
            &&!f.Root.TryReleaseNativeSourceSelection(old,1000),
            "unpaid retired selection history expires after 64 worlds without granting old native permission");
    }
}
