using System;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Assets supply the actual weapon rows, collision rigs and world occluders.
// A proof fixture never calls Physical: the production projectile must collide.
internal static class ShotProofTests
{
    private static int _checks;
    private static PlayerEntity _shooter = null!, _victim = null!;
    private static Vector3 _base, _direction;
    private static uint _id, _frame;
    private static ushort _claimId;
    private static ShotKey _physicalShot;
    private static uint _physicalComponent;
    private static CombatAckEntry _lastAck;
    private static void Check(bool ok, string label)
    { _checks++; if (!ok) throw new InvalidOperationException(label); Console.WriteLine($"PROOF PASS {label}"); }
    private static void Frame(uint frame)
    { _frame = frame; typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, frame); }
    private static void Record()
    { NetUnlagged.Record(_frame); NetDynamicGeometryHistory.RecordWorld(_frame); }
    private static ShotProofSource Source(ushort weapons = 0x1FF, int ammo0 = 10000, int ammo1 = 10000,
        bool infinite = false, ushort charges = 0x1FF, bool supported = true)
        => new(weapons, charges, ammo0, ammo1, infinite, _shooter.Hunter, false, false, supported);
    private static void Prepare(ShotProofSource? source = null)
    {
        foreach (var beam in _shooter.EquipInfo.Beams) beam.Lifespan = 0;
        NetFireEvents.Reset(); NetHitClaims.Reset(); _physicalShot = default; _physicalComponent = 0;
        Frame(_frame + 200);
        foreach (var player in new[] { _shooter, _victim })
        {
            player.Health = 999; player.ModForceForm(false); player.Controls.ClearAll();
            player.ModReleaseSpawnProtection();
            typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(player, (ushort)0);
            typeof(PlayerEntity).GetField("_damageInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(player, (ushort)0);
        }
        _shooter.ModPlaceAt(_base); _victim.ModPlaceAt(_base + _direction * 3);
        NetHitClaims.Tick(); Record();
        NetShotProof.Seed(_shooter, source ?? Source());
    }
    private static BeamProjectileEntity Emit(BeamType weapon = BeamType.Imperialist, byte charge = 0,
        uint modifiers = 0, Vector3? ray = null, bool preserve = false, Vector3? ownerAim = null, uint? sourceFrame = null)
    {
        if (!preserve) foreach (var old in _shooter.EquipInfo.Beams) old.Lifespan = 0;
        var direction = ray ?? _direction;
        uint source = sourceFrame ?? _frame;
        var aim = ownerAim ?? direction.Normalized();
        var fire = new FireEvent(++_id, source, _frame, 0, FireEventKind.PressFire,
            (byte)weapon, charge, modifiers, FireEvent.FlagPose, _shooter.Position + Vector3.UnitY * .5f,
            direction, aim, aim, SourceLife: NetPlayerLifecycle.Get(0));
        var packet = new IntentPacket { MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
            SlotGeneration = NetPlayerLifecycle.Generation(0), LifeId = NetPlayerLifecycle.Get(0),
            Frame = source, AckFrame = _frame, Position = _shooter.Position, Aim = aim,
            Buttons = IntentButtons.InPlayState, HasFireEvents = true, FireEventCount = 1 };
        packet.FireEvents[0] = fire;
        NetShotProof.ObservePose(_shooter, packet);
        Check(NetFireEvents.Accept(0, packet), "actual carrier admitted");
        NetFireEvents.DrainAccepted(_shooter);
        var key = ShotKey.For(0, fire.ShotId);
        bool found = NetFireEvents.CaptureLedger(0).TryGet(key, out _, out var disposition, out var dispositionReason);
        Check(found && disposition == ShotDisposition.Fired,
            $"actual accepted event emitted once found={found} disposition={disposition} reason={dispositionReason}");
        var beam = _shooter.EquipInfo.Beams.FirstOrDefault(b => b.ModLaunchKey == key && b.ModComponentId != 0);
        Check(beam != null, "production projectile has shot and component identity");
        return beam!;
    }
    private static HitClaimPacket Claim(BeamProjectileEntity beam, bool direct = true, ushort damage = 1)
    {
        PlayerState state = default; NetDamage.Write(1, ref state);
        return new HitClaimPacket
        {
        MatchId = beam.ModLaunchKey.MatchId, AuthorityEpoch = beam.ModLaunchKey.AuthorityEpoch,
        ShooterGeneration = beam.ModLaunchKey.Generation, ShooterLifeId = beam.ModLaunchKey.LifeId,
        VictimSlot = 1, VictimGeneration = NetPlayerLifecycle.Generation(1), VictimLifeId = NetPlayerLifecycle.Get(1),
        ClaimId = ++_claimId, ShotId = beam.ModShotId, ComponentId = beam.ModComponentId,
        Frame = _frame, AckFrame = _frame, LaunchFrame = beam.ModLaunchFrame,
        Beam = (byte)beam.Beam, Flags = direct ? HitClaimPacket.FlagDirect : (byte)0,
        Damage = damage, HitPoint = _victim.Position,
        Direction = _physicalShot == beam.ModLaunchKey && _physicalComponent == beam.ModComponentId ? state.HitDirection : Vector3.Zero
        };
    }
    private static void Decision(in HitClaimPacket claim, ShotProofStatus status, ShotProofReason reason, string label)
    {
        int health = _victim.Health; ushort sequence = NetDamage.Sequence(1);
        var proof = NetShotProof.Evaluate(0, claim, final: true);
        Check(proof.Status == status && proof.Reason == reason,
            $"{label}: expected {status}/{reason}, got {proof.Status}/{proof.Reason} (source={proof.Source}, geometry={proof.Geometry})");
        Check(_victim.Health == health && NetDamage.Sequence(1) == sequence, $"{label} evaluation never changes gameplay");
    }
    private static void Collide(BeamProjectileEntity beam)
    {
        int before = _victim.Health;
        for (int i = 0; i < 60 && _victim.Health == before && beam.Lifespan > 0; i++) beam.Process();
        Check(_victim.Health < before, $"actual production collision/{beam.Beam}");
        _physicalShot = beam.ModLaunchKey; _physicalComponent = beam.ModComponentId;
    }
    private static byte Receive(in HitClaimPacket claim)
    {
        byte result = 255; _lastAck = default; ushort claimId = claim.ClaimId; var saved = NetHitClaims.CombatAckSink;
        NetHitClaims.CombatAckSink = (slot, entries) =>
        { foreach (var entry in entries) if (slot == 0 && entry.ClaimId == claimId) { result = entry.Result; _lastAck = entry; } };
        try
        {
            byte[] packet = new byte[1 + HitClaimPacket.Size]; packet[0] = 1; claim.Write(packet.AsSpan(1));
            NetHitClaims.Receive(0, packet);
            for (int i = 0; i <= 2 * NetHitClaims.MaxGraceFrames + 2 && result == 255; i++)
            { NetHitClaims.Tick(); if (result == 255) Frame(_frame + 1); }
            Check(result != 255, "claim receives terminal acknowledgement"); return result;
        }
        finally { NetHitClaims.CombatAckSink = saved; }
    }
    private static void NegativeCases()
    {
        Prepare(); var beam = Emit(); var claim = Claim(beam);
        var forged = claim; forged.ShotId += 10000;
        Decision(forged, ShotProofStatus.Rejected, ShotProofReason.UnknownShot, "forged shot identity");
        var mismatch = claim; mismatch.Beam = (byte)BeamType.Missile;
        Decision(mismatch, ShotProofStatus.Rejected, ShotProofReason.WeaponMismatch, "weapon mismatch");
        var stale = claim; stale.AckFrame = _frame - NetUnlagged.HistoryFrames;
        Decision(stale, ShotProofStatus.Rejected, ShotProofReason.StaleAck, "stale ACK");
        var unknownComponent = claim; unknownComponent.ComponentId += 10000;
        Decision(unknownComponent, ShotProofStatus.Unavailable, ShotProofReason.UnknownComponent, "component never emitted by authority");
        var life = claim; life.VictimLifeId++;
        Decision(life, ShotProofStatus.Rejected, ShotProofReason.WrongLife, "wrong victim life");
        var head = claim; head.Flags |= HitClaimPacket.FlagHeadshot;
        Decision(head, ShotProofStatus.Rejected, ShotProofReason.WrongHeadshot, "fabricated headshot on body ray");
        Decision(claim, ShotProofStatus.Unavailable, ShotProofReason.PhysicalWitnessRequired, "clear body ray alone cannot prove damage");
        Prepare(Source(weapons: 1)); beam = Emit(BeamType.Missile);
        Decision(Claim(beam), ShotProofStatus.Rejected, ShotProofReason.UnownedWeapon, "authority source excludes Missile");
        Prepare(Source(ammo1: 0)); beam = Emit(BeamType.Missile);
        Decision(Claim(beam), ShotProofStatus.Rejected, ShotProofReason.Ammunition, "empty source ammo");
        Prepare(Source(charges: 0));
        byte missileCharge = (byte)(_shooter.OwningScene.WeaponRules[(int)BeamType.Missile].FullCharge * 2);
        beam = Emit(BeamType.Missile, charge: missileCharge);
        Decision(Claim(beam), ShotProofStatus.Rejected, ShotProofReason.InvalidCharge, "authority source excludes charge upgrade");
        Prepare(); beam = Emit(BeamType.Missile, charge: missileCharge);
        Decision(Claim(beam), ShotProofStatus.Unavailable, ShotProofReason.SourceInputUnavailable,
            "charged Missile needs independently verified hold/release history");
        Prepare(); beam = Emit(modifiers: FireEvent.DoubleStateBit);
        Decision(Claim(beam), ShotProofStatus.Rejected, ShotProofReason.ModifierMismatch, "fabricated double damage modifier");
        Prepare(); beam = Emit(ray: _direction * 2);
        Decision(Claim(beam), ShotProofStatus.Rejected, ShotProofReason.InvalidRay, "nonunit original emitted ray");
        Prepare(); beam = Emit(ray: -_direction, ownerAim: _direction);
        Decision(Claim(beam), ShotProofStatus.Rejected, ShotProofReason.InvalidRay,
            "emitted ray disagrees with independent owner aim");
        Prepare(); Emit(); beam = Emit(preserve: true, sourceFrame: _frame + 1000);
        Decision(Claim(beam), ShotProofStatus.Rejected, ShotProofReason.SourceClock,
            "source clock cannot advance 1000 frames at unchanged authority time");
        Prepare(); Emit(); Frame(_frame + 1); Record(); beam = Emit(preserve: true);
        Decision(Claim(beam), ShotProofStatus.Rejected, ShotProofReason.FireRate, "second Imperialist inside actual cooldown");
        Prepare(); Emit(); beam = Emit(preserve: true, sourceFrame: _frame - 1);
        Decision(Claim(beam), ShotProofStatus.Rejected, ShotProofReason.FireRate,
            "late older event inside newer fired shot cooldown");
        int missileCost = _shooter.OwningScene.WeaponRules[(int)BeamType.Missile].AmmoCost;
        Prepare(Source(ammo1: missileCost)); Emit(BeamType.Missile);
        beam = Emit(BeamType.Missile, preserve: true, sourceFrame: _frame - 50);
        Decision(Claim(beam), ShotProofStatus.Rejected, ShotProofReason.Ammunition,
            "well-separated late older event cannot overspend already fired newer ammo");
        Prepare(Source(supported: false)); beam = Emit();
        Decision(Claim(beam), ShotProofStatus.Unavailable, ShotProofReason.SourceUnavailable, "unavailable authority source remains explicit");
        Prepare(); beam = Emit(); claim = Claim(beam); claim.Beam = HitClaimPacket.NoBeam;
        Decision(claim, ShotProofStatus.Unavailable, ShotProofReason.UnsupportedBombOrMelee, "bomb/melee family is unavailable");
        claim = Claim(beam); claim.Flags |= HitClaimPacket.FlagContinuousTick;
        Decision(claim, ShotProofStatus.Unavailable, ShotProofReason.UnsupportedContinuous, "continuous family is unavailable");
        var oldVictimLife = Claim(beam);
        _victim.Spawn(_victim.Position, Vector3.UnitZ, Vector3.UnitY, _victim.NodeRef, respawn: true);
        Decision(oldVictimLife, ShotProofStatus.Rejected, ShotProofReason.WrongLife, "actual victim respawn fences prior-life claim");
    }
    private static void PhysicalAndPaymentCases()
    {
        foreach (var weapon in new[] { BeamType.Imperialist, BeamType.Missile })
        {
            Prepare(); var beam = Emit(weapon); Collide(beam); var direct = Claim(beam);
            Decision(direct, ShotProofStatus.Verified, ShotProofReason.Verified, $"legal physical direct/{weapon}");
            var forgedEffect = direct; forgedEffect.Flags |= HitClaimPacket.FlagFrozen;
            Decision(forgedEffect, ShotProofStatus.Rejected, ShotProofReason.ClaimEffectsMismatch,
                $"physical witness rejects forged affliction/{weapon}");
            var forgedImpulse = direct; forgedImpulse.Direction += Vector3.UnitY * .1f;
            Decision(forgedImpulse, ShotProofStatus.Rejected, ShotProofReason.ClaimEffectsMismatch,
                $"physical witness rejects forged impulse/{weapon}");
            var wrongHead = direct; wrongHead.Flags |= HitClaimPacket.FlagHeadshot;
            Decision(wrongHead, ShotProofStatus.Rejected, ShotProofReason.WrongHeadshot, $"physical body cannot certify headshot/{weapon}");
            Check(NetShotProof.MarkClaim(0, direct) && NetShotProof.WasClaimed(0, direct), "first exact component payment recorded");
            direct.ClaimId++;
            Decision(direct, ShotProofStatus.Rejected, ShotProofReason.DuplicateComponent, "new claim ID cannot repay same component");
            var splash = Claim(beam, direct: false);
            Check(!NetShotProof.WasClaimed(0, splash), "direct payment does not consume separate splash kind");
            Decision(splash, ShotProofStatus.Unavailable, ShotProofReason.PhysicalWitnessRequired,
                "direct target excluded from production splash has no invented splash witness");
        }
        Prepare(); _victim.ModPlaceAt(_base + _direction * .9f); Record();
        var splashBeam = Emit(BeamType.Missile, ray: -Vector3.UnitY); Collide(splashBeam);
        Decision(Claim(splashBeam, direct: false), ShotProofStatus.Verified, ShotProofReason.Verified,
            "actual Missile wall collision pays off-ray splash");
        Prepare();
        typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_victim, (ushort)1000);
        var protectedBeam = Emit(); int protectedHealth = _victim.Health; ushort protectedSequence = NetDamage.Sequence(1);
        for (int i = 0; i < 60 && protectedBeam.Lifespan > 0 && !protectedBeam.Flags.TestFlag(BeamFlags.Collided); i++) protectedBeam.Process();
        Check(_victim.Health == protectedHealth && NetDamage.Sequence(1) == protectedSequence,
            "production collision denied by spawn protection does no damage");
        var protectedClaim = Claim(protectedBeam);
        Decision(protectedClaim, ShotProofStatus.Unavailable, ShotProofReason.PhysicalWitnessRequired,
            "protected collision never creates physical payment witness");
        Check(!NetShotProof.Evaluate(0, protectedClaim, final: true).PhysicalMatch,
            "denied production damage has no physical match");
        Prepare(); var parent = Emit(BeamType.Judicator);
        var result = BeamProjectileEntity.Spawn(_shooter, parent.Equip!, parent.Position, _direction,
            BeamSpawnFlags.NoMuzzle, _shooter.NodeRef, _shooter.OwningScene, parent: parent);
        var child = _shooter.EquipInfo.Beams.FirstOrDefault(b => b.ModLaunchKey == parent.ModLaunchKey
            && b.ModComponentId != parent.ModComponentId && b.Lifespan > 0);
        Check(result != BeamResultFlags.NoSpawn && child != null, "real spawned child retains distinct component identity");
        var parentClaim = Claim(parent); Check(NetShotProof.MarkClaim(0, parentClaim), "parent component payment recorded");
        var childClaim = Claim(child!);
        Check(!NetShotProof.WasClaimed(0, childClaim), "parent payment does not consume independently emitted child");
        Decision(childClaim, ShotProofStatus.Unavailable, ShotProofReason.PhysicalWitnessRequired, "unresolved real child requires its own physical collision");
    }
    private static void WeaponVariants()
    {
        Prepare(Source(charges: 0));
        var tap = Emit(BeamType.Missile, charge: 1);
        Check(!tap.Flags.TestFlag(BeamFlags.Charged), "native charge1 Missile tap remains uncharged without charge upgrade");
        Collide(tap); var tapProof = NetShotProof.Evaluate(0, Claim(tap), final: true);
        Check(tapProof.Source == ShotProofReason.Verified && tapProof.Geometry == ShotProofReason.Verified
            && tapProof.PhysicalMatch && tapProof.Status == ShotProofStatus.Verified,
            $"actual charge1 uncharged Missile tap source={tapProof.Source} geometry={tapProof.Geometry} status={tapProof.Status}");
        Prepare();
        var affinity = Weapons.GetAffinityBeam(_shooter.Hunter);
        Check(affinity == BeamType.Missile, "spawn authority fixture uses Samus Missile affinity");
        var affinityBeam = Emit(affinity, modifiers: FireEvent.AffinityStateBit); Collide(affinityBeam);
        var affinityClaim = Claim(affinityBeam); var affinityProof = NetShotProof.Evaluate(0, affinityClaim, final: true);
        Check(NetShotProof.SourceReason(affinityBeam.ModLaunchKey) == ShotProofReason.Verified
            && affinityProof.Source == ShotProofReason.Verified && affinityProof.Geometry == ShotProofReason.Verified
            && affinityProof.PhysicalMatch && affinityProof.Status == ShotProofStatus.Verified,
            $"actual uncharged affinity impact source={affinityProof.Source} geometry={affinityProof.Geometry} status={affinityProof.Status}");

        // The real wall collision creates the child. No direct Spawn(parent) call supplies this witness.
        Prepare(); _victim.ModPlaceAt(_base + _direction * 8); Record();
        var parent = Emit(BeamType.Judicator, ray: -Vector3.UnitY);
        for (int i = 0; i < 60 && parent.Lifespan > 0 && !parent.Flags.TestFlag(BeamFlags.Collided); i++) parent.Process();
        var child = _shooter.EquipInfo.Beams.FirstOrDefault(b => b != parent && b.Lifespan > 0
            && b.ModLaunchKey == parent.ModLaunchKey && b.ModComponentId != 0);
        Check(parent.Flags.TestFlag(BeamFlags.Collided) && child != null,
            "actual Judicator wall collision emits ricochet child");
        Check(child!.ModComponentId != parent.ModComponentId && child.Velocity.LengthSquared > 0,
            "actual ricochet child has independent moving component");
        _victim.ModPlaceAt(child.Position + child.Velocity.Normalized() * 1.2f - Vector3.UnitY * .5f); Record();
        Collide(child); var ricochetClaim = Claim(child); var ricochetProof = NetShotProof.Evaluate(0, ricochetClaim, final: true);
        Check(ricochetProof.Source == ShotProofReason.Verified && ricochetProof.Geometry == ShotProofReason.Verified
            && ricochetProof.PhysicalMatch && ricochetProof.Status == ShotProofStatus.Verified
            && ricochetProof.Family == ShotProofFamily.Child,
            $"actual ricochet child impact source={ricochetProof.Source} geometry={ricochetProof.Geometry} status={ricochetProof.Status} family={ricochetProof.Family}");

        Prepare();
        byte charge = (byte)(_shooter.OwningScene.WeaponRules[(int)BeamType.Missile].FullCharge * 2);
        // MP Missile homing belongs to the Samus affinity row; the ordinary row has none.
        var homing = Emit(BeamType.Missile, charge: charge, modifiers: FireEvent.AffinityStateBit);
        Check(homing.Flags.TestFlag(BeamFlags.Homing) && homing.Target == _victim,
             $"production charged affinity Missile acquires real homing target flags={homing.Flags} target={homing.Target?.Type} targetIsVictim={homing.Target == _victim}");
        Collide(homing); var homingClaim = Claim(homing); var homingProof = NetShotProof.Evaluate(0, homingClaim, final: true);
        Check(homingProof.Source == ShotProofReason.SourceInputUnavailable && homingProof.Geometry == ShotProofReason.Verified
            && homingProof.PhysicalMatch && homingProof.Status == ShotProofStatus.Unavailable
            && homingProof.Reason == ShotProofReason.SourceInputUnavailable,
            $"actual homing impact keeps unproved charge conservative source={homingProof.Source} geometry={homingProof.Geometry} status={homingProof.Status}");
        Check(_shooter.OwningScene.WeaponRules.Take(18).Where(w => w.UnchargedHoming > 0)
            .All(w => w.Flags.TestFlag(WeaponFlags.Continuous)),
            "production MP rows provide no discrete uncharged homing source variant");
    }
    private static void StrictOldLifeIdentity()
    {
        NetShotProof.Mode = ShotProofMode.Shadow; Prepare(); var beam = Emit(BeamType.Missile); Collide(beam);
        var original = Claim(beam); int health = _victim.Health; ushort sequence = NetDamage.Sequence(1);
        Check(NetShotProof.TryPhysicalOutcome(0, original, out var witnessed) && witnessed.DamageApplied > original.Damage
            && witnessed.HealthAfter == health && witnessed.DamageSequence == sequence,
            "actual physical witness captures larger native damage and final health/sequence");
        _shooter.Spawn(_shooter.Position, Vector3.UnitZ, Vector3.UnitY, _shooter.NodeRef, respawn: true);
        Check(NetPlayerLifecycle.Get(0) != original.ShooterLifeId, "actual shooter respawn advances claim source life");
        NetShotProof.Mode = ShotProofMode.Enforce;
        foreach (int mutation in new[] { 0, 1, 2 })
        {
            var forged = original; forged.ClaimId = ++_claimId;
            if (mutation == 0) forged.ComponentId += 10000;
            else if (mutation == 1) forged.ComponentId = 0;
            else forged.Beam = (byte)BeamType.Imperialist;
            Check(Receive(forged) == HitVerdictPacket.ResultWrongLife && _victim.Health == health
                && NetDamage.Sequence(1) == sequence,
                $"strict old-life physical pairing refuses mutated component/weapon/{mutation}");
        }
        original.ClaimId = ++_claimId;
        long duplicates = NetHitClaims.DuplicateHere;
        Check(Receive(original) == HitVerdictPacket.ResultDuplicate && _victim.Health == health
            && NetHitClaims.DuplicateHere == duplicates + 1,
            "strict old-life exact physical component acknowledges prior result without damage");
        Check(_lastAck.ShotId == original.ShotId && _lastAck.VictimSlot == original.VictimSlot
            && _lastAck.VictimGeneration == original.VictimGeneration && _lastAck.VictimLife == original.VictimLifeId
            && _lastAck.DamageSequence == sequence && _lastAck.HealthAfter == health
            && _lastAck.DamageApplied == witnessed.DamageApplied && _lastAck.Flags == witnessed.Flags,
            "strict old-life acknowledgement preserves immutable actual shot/victim/damage/health/sequence outcome");
        duplicates = NetHitClaims.DuplicateHere;
        Check(Receive(original) == HitVerdictPacket.ResultDuplicate && _victim.Health == health
            && NetHitClaims.DuplicateHere == duplicates,
            "strict old-life cached retry preserves exact duplicate outcome");
        foreach (int mutation in new[] { 0, 1, 2, 3 })
        {
            var cachedForgery = original;
            if (mutation == 0) cachedForgery.ComponentId += 10000;
            else if (mutation == 1) cachedForgery.ComponentId = 0;
            else if (mutation == 2) cachedForgery.Beam = (byte)BeamType.Imperialist;
            else cachedForgery.Flags &= unchecked((byte)~HitClaimPacket.FlagDirect);
            Check(Receive(cachedForgery) == HitVerdictPacket.ResultWrongLife && _victim.Health == health
                && NetDamage.Sequence(1) == sequence,
                $"strict cached old-life identity rejects changed component/weapon/kind/{mutation}");
        }
        Check(Receive(original) == HitVerdictPacket.ResultDuplicate && _victim.Health == health,
            "mutated cached retries cannot replace valid original acknowledgement");
        NetShotProof.Mode = ShotProofMode.Shadow;
    }
    private static void ComponentLineageBounds()
    {
        Check(NetShotProof.ComponentId(0, 1) == 1 && NetShotProof.ComponentId(0, 15) == 15,
            "root component ordinals retain distinct bounded identity");
        Check(NetShotProof.ComponentId(0, 0) == 0 && NetShotProof.ComponentId(0, 16) == 0,
            "invalid component ordinals fail closed");
        uint lineage = 1;
        for (int depth = 1; depth < 8; depth++) lineage = NetShotProof.ComponentId(lineage, 1);
        Check(lineage == 0x11111111 && NetShotProof.ComponentId(lineage, 1) == 0,
            "over-depth component lineage cannot wrap into a root identity");
        Prepare(); var parent = Emit(BeamType.Judicator); parent.ModComponentId = 0;
        var result = BeamProjectileEntity.Spawn(_shooter, parent.Equip!, parent.Position, _direction,
            BeamSpawnFlags.NoMuzzle, _shooter.NodeRef, _shooter.OwningScene, parent: parent);
        var descendants = _shooter.EquipInfo.Beams.Where(b => b != parent && b.ModLaunchKey == parent.ModLaunchKey
            && b.Lifespan > 0).ToArray();
        Check(result != BeamResultFlags.NoSpawn && descendants.Length > 0 && descendants.All(b => b.ModComponentId == 0),
            "actual children of missing component lineage preserve zero");
        Decision(Claim(descendants[0]), ShotProofStatus.Unavailable, ShotProofReason.UnknownComponent,
            "missing child lineage cannot certify a physical component");
    }
    private static void ShadowAndEnforcement()
    {
        foreach (var mode in new[] { ShotProofMode.Off, ShotProofMode.Shadow, ShotProofMode.Enforce })
        {
            NetShotProof.Mode = mode; Prepare(); var beam = Emit(); var claim = Claim(beam); claim.ShotId += 10000;
            int before = _victim.Health; long allowedBefore = NetShotProof.LegacyAllowedUnavailable;
            byte result = Receive(claim);
            if (mode == ShotProofMode.Enforce)
                Check(result == HitVerdictPacket.ResultRefused && _victim.Health == before,
                    "opt-in enforcement rejects forged ID through actual receive/tick");
            else
                Check(result == HitVerdictPacket.ResultApplied && _victim.Health == before - claim.Damage,
                    $"{mode} preserves legacy adjudication and damage result={HitVerdictPacket.Describe(result)} health={before}->{_victim.Health}");
            if (mode == ShotProofMode.Shadow)
                Check(NetShotProof.LegacyAllowedUnavailable > allowedBefore, "shadow records legacy allowed claim lacking proof");
        }
        NetShotProof.Mode = ShotProofMode.Shadow;
        Check(NetHitClaims.ClaimRadius == 2f, "legacy claim radius remains unchanged");
    }
    private static void BlockedWorld()
    {
        Prepare(); var scene = _shooter.OwningScene;
        Vector3 origin = _shooter.Position + Vector3.UnitY * .5f;
        float speed = scene.WeaponRules[(int)BeamType.Imperialist].UnchargedSpeed / 8192f;
        foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
        {
            if (!NetDynamicGeometryHistory.TryTraceDistanceAtFrame(scene, origin, origin + direction * speed,
                _frame, out float distance) || distance is <= .15f or >= .8f) continue;
            _victim.ModPlaceAt(origin + direction * (speed * distance + .8f) - Vector3.UnitY * .5f); Record();
            var beam = Emit(ray: direction);
            Decision(Claim(beam), ShotProofStatus.Rejected, ShotProofReason.BlockedWorld, "real world wall occludes historical target");
            return;
        }
        throw new InvalidOperationException("Room fixture has no suitable real wall in the Imperialist first segment; choose an asset room with one.");
    }
    public static int Run(string room)
    {
        bool accepted = NetFireEvents.AcceptedPlaybackEnabled; var mode = NetShotProof.Mode;
        var sim = new ServerSim();
        if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { })) return 1;
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1, RoomKey = room,
                Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte i = 0; i < 2; i++) { roster.Slots[i] = i; roster.Generations[i] = 1; }
            NetSession.ApplyRoster(roster);
            for (int i = 0; i < 120; i++) sim.Step();
            _shooter = PlayerEntity.Players[0]; _victim = PlayerEntity.Players[1]; _base = _shooter.Position;
            _shooter.OwningScene.GameState.EnhancedHunters = false;
            _shooter.OwningScene.GameState.BalancedMode = false;
            NetFireEvents.AcceptedPlaybackEnabled = true; NetShotProof.Mode = ShotProofMode.Shadow; _frame = NetSession.NetFrame;
            foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
                if (NetDynamicGeometryHistory.TryTraceDistanceAtFrame(_shooter.OwningScene,
                    _base + Vector3.UnitY * .5f, _base + direction * 4 + Vector3.UnitY * .5f, _frame, out float wall) && wall == 1)
                { _direction = direction; break; }
            Check(_direction.LengthSquared == 1, "asset fixture provides clear production collision lane");
            NegativeCases(); PhysicalAndPaymentCases(); WeaponVariants(); StrictOldLifeIdentity(); ComponentLineageBounds(); BlockedWorld(); ShadowAndEnforcement();
            Check(sim.StepFailures == 0, "no simulation failures");
            Console.WriteLine($"PASS: {_checks} shot proof asset assertions"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { sim.Stop(); NetSession.Stop(); NetFireEvents.AcceptedPlaybackEnabled = accepted; NetShotProof.Mode = mode; }
    }
}
