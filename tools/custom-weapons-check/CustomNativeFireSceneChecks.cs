using MphRead;
using MphRead.Entities;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

internal static partial class CustomAuthoritySceneChecks
{
    // Actual engine/lifecycle gates, with the same explicitly trusted readiness
    // publication seam as the existing source-boundary fixture. No socket claim.
    private static FireEvent GateEngineFire(PlayerEntity player,uint id,uint source)
        =>new(id,source,source,0,FireEventKind.PressFire,(byte)BeamType.PowerBeam,0,0,
            FireEvent.FlagPose,player.Position+Vector3.UnitY*.4f,Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitY,
            default,NetPlayerLifecycle.Get(player.SlotIndex));
    private static HitClaimPacket GateClaim(WeaponOwnerContext owner,in FireEvent fire)
        =>new(){MatchId=owner.MatchId,AuthorityEpoch=owner.AuthorityEpoch,ShooterGeneration=owner.Generation,
            ShooterLifeId=owner.Life,ShotId=fire.ShotId,Beam=fire.Weapon,LaunchFrame=fire.AckFrame,
            Frame=fire.SourceFrame};
    private static void NativeFireGateEngine()
    { NativeFireBeforeLife(); NativeFireUnknownOccupant(); NativeFireActualTurret(); }
    private static void NativeFireBeforeLife()
    {
        using var f=new Fixture(automaticSourceBoundary:true);ConfirmBoundary(f);
        // Run already sets Off and restores the caller's policy in its finally.
        Check(NetShotProof.Mode==ShotProofMode.Off&&NetTimedShotProof.Mode==TimedGrantProofMode.Off,
            "first-source engine gate runs with optional native and timed proof policies Off");
        Check(!f.Root.TryState(f.Shooter,out _)&&!f.Root.HasNativeSelectionHistory(f.Owner),
            "native gate fixture has genuine current actor but no custom life before accepted source consumption");
        var beforeOffer=GateClaim(f.Owner,GateEngineFire(f.Shooter,700,1001));
        Check(!f.Root.HasNativeFireGate(f.Owner.Slot)
            &&f.Root.ReadNativeClaim(f.Owner.Slot,beforeOffer)==CustomNativeFireDecision.Pending,
            "confirmed custom readiness requires native admission before first event, gate allocation or life binding");
        var input=BoundaryPacket(f,1001);var fire=GateEngineFire(f.Shooter,700,1001);
        input.HasFireEvents=true;input.FireEventCount=1;input.FireEvents[0]=fire;
        const ulong connection=0x123456789abcdef0UL;
        Check(f.Root.FilterNativeIntent(f.Owner.Slot,input,connection,out var filtered)
            &&filtered.FireEventCount==0&&filtered.HasFireEvents&&filtered.Buttons==input.Buttons
            &&f.Root.HasNativeFireGate(f.Owner.Slot)&&!f.Root.TryTakeNativeIntent(f.Owner.Slot,out _,out _,out _),
            "first valid current-owner native offer waits before life binding without erasing physical controls");
        var claim=GateClaim(f.Owner,fire);
        Check(f.Root.ReadNativeClaim(f.Owner.Slot,claim)==CustomNativeFireDecision.Pending,
            "pre-birth waiting event cannot authorize a native Shadow claim");
        PublishBoundaryInput(f,filtered,1001);
        bool hasState=f.Root.TryState(f.Shooter,out var born);
        Check(f.Root.HasNativeSelectionHistory(f.Owner)&&hasState
            &&born.Phase==CustomWeaponPhase.Unequipped&&born.Magazine==4
            &&f.Root.TryGetSourceObservation(f.Owner,1001,out var observed)&&observed.Input.Frame==1001,
            "real placement/control/source phase binds life and seals exact first neutral source");
        Check(f.Root.TryTakeNativeIntent(f.Owner.Slot,out var recovered,out var recoveredConnection,out var key)
            &&recoveredConnection==connection&&recovered.FireEventCount==1&&recovered.FireEvents[0]==fire
            &&f.Root.ReleaseNativeIntent(key,connection)
            &&f.Root.ReadNativeClaim(f.Owner.Slot,claim)==CustomNativeFireDecision.Pending,
            "real sealed source releases original pending carrier but permission alone cannot authorize claim");
        Check(NetSession.AcceptSlotIntent(f.Owner.Slot,recovered)
            &&f.Root.ReadNativeClaim(f.Owner.Slot,claim)==CustomNativeFireDecision.Released,
            "actual native event intake confirms released first-source fact before claim permission");
        Check(f.Root.TryState(f.Shooter,out var unchanged)&&unchanged==born&&f.Root.AcceptedEmissions==0
            &&f.Root.PositivePayments==0&&f.Victim.Health==200&&f.StepFailures==0,
            "source gate and native admission do not emit custom weapons or debit custom ammunition/HP");
        PublishBoundaryInput(f,BoundaryPacket(f,1002),1002);
        Check(f.Process(f.EngineAction(CustomWeaponActionKind.Equip,1002)).Result==WeaponActionResult.Accepted
            &&f.Root.ReadNativeSourceSelection(f.Owner,1002)==CustomNativeSelection.Custom,
            "actual later engine source selects custom without revising admitted earlier native source");
        uint started=NetSession.NetFrame;
        for(int i=0;i<300;i++)f.Step();
        Check(unchecked(NetSession.NetFrame-started)>256&&f.StepFailures==0&&NetShotProof.Mode==ShotProofMode.Off
            &&f.Root.ReadNativeClaim(f.Owner.Slot,claim)==CustomNativeFireDecision.Released,
            "confirmed actual native intake remains claim-readable after300 real simulation steps and later custom selection");
    }
    private static void NativeFireUnknownOccupant()
    {
        using var f=new Fixture(engineSources:true);
        var fire=GateEngineFire(f.Shooter,800,1000);var input=SourcePacket(f.Shooter,1000,Vector3.UnitZ);
        input.HasFireEvents=true;input.FireEventCount=1;input.FireEvents[0]=fire;
        Check(f.Root.FilterNativeIntent(f.Owner.Slot,input,1,out var filtered)&&filtered.FireEventCount==0
            &&f.Root.HasNativeFireGate(f.Owner.Slot),"old actual occupant establishes a pending custom-aware native slot gate");
        var body=f.Shooter.Position;NetPlayerLifecycle.SetOccupant(f.Owner.Slot,NetLifecycleTracker.Next(f.Owner.Generation));
        f.Shooter.Spawn(body,Vector3.UnitZ,Vector3.UnitY,f.Shooter.NodeRef,respawn:true);
        f.Shooter.ModPlaceAt(body);Fixture.Clean(f.Shooter,200);f.Root.Advance();
        var replacement=SourceOwner(f.Shooter);var replacementFire=fire with{SourceLife=replacement.Life};
        var claim=GateClaim(replacement,replacementFire);
        Check(replacement.Generation!=f.Owner.Generation&&f.Root.HasNativeFireGate(f.Owner.Slot)
            &&!f.Root.HasNativeSelectionHistory(replacement)&&!f.Root.IsActionLifeActive(replacement),
            "native occupant replacement keeps old slot gate without granting replacement a custom life");
        Check(f.Root.ReadNativeClaim(replacement.Slot,claim)==CustomNativeFireDecision.Released,
            "unready native-only replacement cannot inherit an old occupant's custom gate restriction");
        Check(f.Root.ReadNativeClaim(f.Owner.Slot,GateClaim(f.Owner,fire))==CustomNativeFireDecision.Pending,
            "replacement native-only fallback cannot promote old occupant pending claim");
    }
    private static void NativeFireActualTurret()
    {
        using var f=new Fixture(engineSources:true);
        var offer=GateEngineFire(f.Shooter,900,1000);var input=SourcePacket(f.Shooter,1000,Vector3.UnitZ);
        input.HasFireEvents=true;input.FireEventCount=1;input.FireEvents[0]=offer;
        Check(f.Root.FilterNativeIntent(f.Owner.Slot,input,1,out var filtered)&&filtered.FireEventCount==0,
            "turret fallback fixture retains a handheld offer pending source selection");
        var ordinary=GateClaim(f.Owner,offer);ordinary.Flags=HitClaimPacket.FlagHalfturret;
        Check(f.Root.ReadNativeClaim(f.Owner.Slot,ordinary)==CustomNativeFireDecision.Pending,
            "victim-halfturret claim flag cannot exempt an ordinary handheld source");
        f.Shooter.CreateHalfturret();var turret=f.Shooter.Halfturret;
        // Native Weavel form transition plants through AddEntity, which runs
        // Initialize and creates its native Battlehammer equipment/health split.
        f.Scene.AddEntity(turret);
        var equip=turret.EquipInfo;
        Check(equip.Weapon!=null&&equip.Weapon.Beam==BeamType.Battlehammer
            &&ReferenceEquals(equip.Beams,f.Shooter.EquipInfo.Beams)&&turret.Health>0,
            "native scene planting initializes actual turret weapon, owner pool and split health");
        equip.InfiniteAmmo=true;
        var origin=f.Shooter.Position+Vector3.UnitY*20;
        Check(NetFireEvents.CanFireTurret(f.Shooter),"actual authority turret can author under native world admission");
        Check(BeamProjectileEntity.Spawn(turret,equip,origin,Vector3.UnitY,BeamSpawnFlags.NoMuzzle,
            f.Shooter.NodeRef,f.Scene)!=BeamResultFlags.NoSpawn,
            "real native turret factory emits and commits canonical authority outcome");
        var beam=equip.Beams.First(b=>b.Lifespan>0&&ReferenceEquals(b.Owner,turret));var key=beam.ModLaunchKey;
        Check(key.ShotId!=0&&key.LifeId==f.Owner.Life&&key.Generation==f.Owner.Generation
            &&NetSession.TryGetAuthorityTurretEmission(key,out var emitted)
            &&emitted.Kind==FireEventKind.TurretFire&&emitted.Weapon==(byte)BeamType.Battlehammer,
            "canonical TurretEmitted lookup comes from actual native projectile commit, not seeded metadata");
        // Read back after the compound assertion to satisfy definite assignment
        // without inventing a fire event if the actual emission was unavailable.
        Check(NetSession.TryGetAuthorityTurretEmission(key,out var canonical),"actual turret emission is retained for claim lookup");
        var claim=GateClaim(f.Owner,canonical);
        Check(f.Root.ReadNativeClaim(f.Owner.Slot,claim)==CustomNativeFireDecision.Released,
            "canonical actual turret emission authorizes its compatible native claim outside handheld gate");
        claim.LaunchFrame++;
        Check(f.Root.ReadNativeClaim(f.Owner.Slot,claim)==CustomNativeFireDecision.Rejected,
            "canonical turret fallback still rejects mismatched launch-world claim");
        claim=GateClaim(f.Owner,canonical);claim.Beam=(byte)BeamType.PowerBeam;
        Check(f.Root.ReadNativeClaim(f.Owner.Slot,claim)==CustomNativeFireDecision.Rejected,
            "canonical turret identity cannot authorize a different native beam");
        Check(f.Root.AcceptedEmissions==0&&f.Root.PositivePayments==0&&f.Victim.Health==200&&f.StepFailures==0,
            "turret source authorization test applies no claim damage or custom resource mutation");
    }
}
