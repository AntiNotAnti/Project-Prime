using System;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Asset-backed native damage and arbitration regressions. Fixtures emit through
// accepted fire playback; native repeats use the actual public TakeDamage hook.
// This is an actual admitted ServerSim authority with combat-seam placement,
// speed and packet fixtures, not live UDP or player-input movement qualification.
internal static class SelfSplashTests
{
    private static int _checks;
    private static PlayerEntity _shooter = null!, _victim = null!;
    private static Vector3 _base, _direction;
    private static uint _id, _frame;
    private static ushort _claimId;
    private static ShotKey _physicalShot;
    private static uint _physicalComponent;
    private static CombatAckEntry _lastAck;
    private static MovementModel _model;
    private static void Check(bool ok, string label)
    { _checks++; if (!ok) throw new InvalidOperationException(label); Console.WriteLine($"SELF SPLASH PASS {label}"); }
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
            player.ModReleaseSpawnProtection(); player.ModSetFrozen(false); player.ModSetBurning(false); player.ModSetDisrupted(false); player.Speed = Vector3.Zero;
            typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(player, (ushort)0);
            typeof(PlayerEntity).GetField("_damageInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(player, (ushort)0);
        }
        _shooter.ModPlaceAt(_base); _victim.ModPlaceAt(_base + _direction * 3);
        NetHitClaims.Tick(); Record();
        NetShotProof.Seed(_shooter, source ?? Source());
    }
    private static int NativeWeaponIndex(BeamType weapon)
        => (int)weapon + (Weapons.GetAffinityBeam(_shooter.Hunter) == weapon ? 9 : 0);
    private static bool AdditiveBlast(BeamType weapon) => weapon == BeamType.Missile
        ? _model is MovementModel.Quake3 or MovementModel.Traversal
        : weapon == BeamType.Magmaul && _model == MovementModel.Traversal;
    private static BeamProjectileEntity EmitFloorBlast(BeamType weapon)
    {
        if (weapon != BeamType.Magmaul) return Emit(weapon, ray: -Vector3.UnitY);
        byte charge = (byte)(_shooter.OwningScene.WeaponRules[NativeWeaponIndex(weapon)].FullCharge * 2);
        // Strict unpaid rescue requires independently observed charge input.
        // A Shadow-mode emitted projectile alone cannot certify a claimed hold.
        // Advance fixture clocks with one accepted physical-input carrier per
        // frame: reset, the asset's full hold duration, then release in Emit.
        bool admitted = true;
        for (int held = 0; held <= charge; held++)
        {
            if (held > 0) Frame(_frame + 1);
            var carrier = new IntentPacket { MatchId = NetSession.CurrentMatchId,
                AuthorityEpoch = NetSession.AuthorityEpoch,
                SlotGeneration = NetPlayerLifecycle.Generation(_shooter.SlotIndex),
                LifeId = NetPlayerLifecycle.Get(_shooter.SlotIndex), Frame = _frame, AckFrame = _frame,
                Position = _shooter.Position, Aim = -Vector3.UnitY, WeaponSelect = (byte)weapon,
                Buttons = IntentButtons.InPlayState | (held > 0 ? IntentButtons.Shoot : 0),
                HasFireEvents = true, ShotInputCount = 1 };
            carrier.ShotInputs[0] = ShotInputHistory.Sample((byte)weapon,
                shoot: held > 0, physicalZoom: false, eligible: true);
            admitted &= NetFireEvents.Accept(_shooter.SlotIndex, carrier);
        }
        Check(admitted, "charged Magmaul fixture supplies admitted reset and full-duration hold carriers");
        Frame(_frame + 1); Record();
        var beam = Emit(weapon, charge: charge, ray: -Vector3.UnitY, verifiedCharge: true);
        var reason = NetShotProof.EmissionReason(beam.ModLaunchKey);
        Check(reason == ShotProofReason.Verified,
            $"charged Magmaul release has independently verified source and pose before strict self-splash validation reason={reason}");
        return beam;
    }
    private static Vector3 ExpectedBlastVelocity(Vector3 incoming, Vector3 impulse, bool additive)
    {
        Vector3 result = incoming + impulse.WithY(0);
        result.Y = additive || impulse.Y <= 0 ? incoming.Y + impulse.Y
            : incoming.Y < .25f ? Math.Min(.25f, incoming.Y + impulse.Y) : incoming.Y;
        return result;
    }
    private static BeamProjectileEntity Emit(BeamType weapon = BeamType.Imperialist, byte charge = 0,
        uint modifiers = 0, Vector3? ray = null, bool preserve = false, Vector3? ownerAim = null, uint? sourceFrame = null,
        bool? affinity = null, bool verifiedCharge = false)
    {
        if (!preserve) foreach (var old in _shooter.EquipInfo.Beams) old.Lifespan = 0;
        var direction = ray ?? _direction;
        uint source = sourceFrame ?? _frame;
        var aim = ownerAim ?? direction.Normalized();
        if (affinity ?? Weapons.GetAffinityBeam(_shooter.Hunter) == weapon)
            modifiers |= FireEvent.AffinityStateBit;
        else if (affinity.HasValue) modifiers &= ~FireEvent.AffinityStateBit;
        var fire = new FireEvent(++_id, source, _frame, 0,
            verifiedCharge ? FireEventKind.ReleaseFire : FireEventKind.PressFire,
            (byte)weapon, charge, modifiers, FireEvent.FlagPose, _shooter.Position + Vector3.UnitY * .5f,
            direction, aim, aim, SourceLife: NetPlayerLifecycle.Get(_shooter.SlotIndex));
        var packet = new IntentPacket { MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
            SlotGeneration = NetPlayerLifecycle.Generation(_shooter.SlotIndex), LifeId = NetPlayerLifecycle.Get(_shooter.SlotIndex),
            Frame = source, AckFrame = _frame, Position = _shooter.Position, Aim = aim, WeaponSelect = (byte)weapon,
            Buttons = IntentButtons.InPlayState, HasFireEvents = true, FireEventCount = 1 };
        if (weapon == BeamType.Imperialist || charge <= 1)
        {
            // Ordinary fixtures provide a real press edge and a weapon reset for scope.
            // Charged Missile fixtures deliberately omit the unproven hold history.
            packet.ShotInputCount = 2;
            packet.ShotInputs[1] = ShotInputHistory.Sample((byte)BeamType.PowerBeam,
                shoot: false, physicalZoom: false, eligible: true);
            packet.ShotInputs[0] = ShotInputHistory.Sample((byte)weapon,
                shoot: true, physicalZoom: fire.ScopedAtFire, eligible: true);
            packet.Buttons |= IntentButtons.Shoot;
            if (fire.ScopedAtFire) packet.Buttons |= IntentButtons.Zoom;
        }
        else if (verifiedCharge)
        {
            packet.ShotInputCount = 1;
            packet.ShotInputs[0] = ShotInputHistory.Sample((byte)weapon,
                shoot: false, physicalZoom: false, eligible: true);
        }
        packet.FireEvents[0] = fire;
        NetShotProof.ObservePose(_shooter, packet);
        Check(NetFireEvents.Accept(_shooter.SlotIndex, packet), "actual carrier admitted");
        NetFireEvents.DrainAccepted(_shooter);
        var key = ShotKey.For(_shooter.SlotIndex, fire.ShotId);
        bool found = NetFireEvents.CaptureLedger(_shooter.SlotIndex).TryGet(key, out _, out var disposition, out var dispositionReason);
        Check(found && disposition == ShotDisposition.Fired,
            $"actual accepted event emitted once found={found} disposition={disposition} reason={dispositionReason}");
        var beam = _shooter.EquipInfo.Beams.FirstOrDefault(b => b.ModLaunchKey == key && b.ModComponentId != 0);
        Check(beam != null, "production projectile has shot and component identity");
        return beam!;
    }
    private static HitClaimPacket Claim(BeamProjectileEntity beam, bool direct = true, ushort damage = 1)
    {
        PlayerState state = default; NetDamage.Write(_victim.SlotIndex, ref state);
        return new HitClaimPacket
        {
        MatchId = beam.ModLaunchKey.MatchId, AuthorityEpoch = beam.ModLaunchKey.AuthorityEpoch,
        ShooterGeneration = beam.ModLaunchKey.Generation, ShooterLifeId = beam.ModLaunchKey.LifeId,
        VictimSlot = (byte)_victim.SlotIndex, VictimGeneration = NetPlayerLifecycle.Generation(_victim.SlotIndex), VictimLifeId = NetPlayerLifecycle.Get(_victim.SlotIndex),
        ClaimId = ++_claimId, ShotId = beam.ModShotId, ComponentId = beam.ModComponentId,
        Frame = _frame, AckFrame = _frame, LaunchFrame = beam.ModLaunchFrame,
        Beam = (byte)beam.Beam, Flags = direct ? HitClaimPacket.FlagDirect : (byte)0,
        Damage = damage, HitPoint = _victim.Position,
        Direction = _physicalShot == beam.ModLaunchKey && _physicalComponent == beam.ModComponentId ? state.HitDirection : Vector3.Zero
        };
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
    private static byte[] Bytes(in CombatAckEntry ack)
    { byte[] bytes = new byte[CombatAckEntry.Size]; ack.Write(bytes); return bytes; }
    private static HitClaimPacket SelfClaim(BeamProjectileEntity beam)
    {
        var claim = Claim(beam, direct: false); claim.VictimSlot = 0;
        claim.VictimGeneration = NetPlayerLifecycle.Generation(0); claim.VictimLifeId = NetPlayerLifecycle.Get(0);
        claim.HitPoint = _shooter.Position; return claim;
    }
    private static void SupportedPolicy()
    {
        foreach (BeamType weapon in Enum.GetValues<BeamType>())
            Check(NetSelfSplash.Supports(weapon) == (weapon is BeamType.Missile or BeamType.Magmaul
                or BeamType.Judicator or BeamType.OmegaCannon), $"constrained self policy weapon {weapon}");
    }
    private static void ForgedClaims(BeamType weapon = BeamType.Missile)
    {
        foreach (string variant in new[] { "no-witness", "missing-shot", "wrong-component", "remote-offset", "direct", "NoBeam", "burn", "turret" })
        {
            Prepare(); var beam = EmitFloorBlast(weapon); var claim = SelfClaim(beam);
            if (variant != "no-witness")
            {
                if (weapon == BeamType.Magmaul) ExplodeAtRealFloor(beam); else Explode(beam);
                Check(NetSelfSplash.Validate(0, claim, out var expected), "negative fixture starts from admitted source and actual terminal witness");
                var exaggerated = claim; exaggerated.Damage = ushort.MaxValue; exaggerated.Direction = Vector3.One * 100;
                Check(NetSelfSplash.Validate(0, exaggerated, out var derived) && derived == expected,
                    "self outcome derives damage and impulse from authority witness rather than claimant values");
            }
            if (variant == "missing-shot") claim.ShotId += 10000;
            if (variant == "wrong-component") claim.ComponentId += 10000;
            if (variant == "remote-offset") claim.HitPoint += Vector3.UnitX * 20;
            if (variant == "direct") claim.Flags |= HitClaimPacket.FlagDirect;
            if (variant == "NoBeam") claim.Beam = HitClaimPacket.NoBeam;
            if (variant == "burn") claim.Flags |= HitClaimPacket.FlagBurning;
            if (variant == "turret") claim.Flags |= HitClaimPacket.FlagHalfturret;
            int health = _shooter.Health; var speed = _shooter.Speed;
            Check(!NetSelfSplash.Validate(0, claim, out _), $"{_model} {weapon} self claim rejects forged {variant}");
            Check(_shooter.Health == health && _shooter.Speed == speed, "self validation cannot author health or impulse");
        }
    }
    private static void Explode(BeamProjectileEntity beam, bool protectedOwner = false)
    {
        int before = _shooter.Health;
        for (int i = 0; i < 90 && beam.Lifespan > 0; i++) beam.Process();
        Check(beam.Lifespan <= 0, $"actual {beam.Beam} floor explosion reaches terminal collision");
        Check(protectedOwner ? _shooter.Health == before : _shooter.Health < before,
            $"actual {beam.Beam} owner splash respects protection={protectedOwner}");
    }
    private static void ExplodeAtRealFloor(BeamProjectileEntity beam)
    {
        CollisionResult floor = default;
        Check(CollisionDetection.CheckBetweenPoints(beam.Position, beam.Position - Vector3.UnitY * 4,
            TestFlags.Beams, _shooter.OwningScene, ref floor),
            "charged explosive fixture resolves actual room floor collision");
        Check(beam.SplashDamage > 0 && beam.SplashRadius > 0,
            $"{beam.Beam} asset row supplies native splash damage={beam.SplashDamage} radius={beam.SplashRadius} flags={beam.Flags}");
        int before = _shooter.Health;
        // Drive the native terminal collision with the real geometry result; no damage is fabricated.
        beam.BackPosition = beam.Position; beam.Position = floor.Position + Vector3.UnitY * .05f;
        beam.OnCollision(floor, colWith: null);
        Check(_shooter.Health < before, $"actual charged {beam.Beam} floor splash damages owner");
    }
    private static void ActualExplosions()
    {
        foreach (var variant in new[] { (BeamType.Missile, false), (BeamType.Missile, true),
            (BeamType.Magmaul, false), (BeamType.Magmaul, true),
            (BeamType.Judicator, false), (BeamType.Judicator, true), (BeamType.OmegaCannon, false) })
        {
            foreach (bool claimFirst in new[] { false, true })
            {
                Prepare(); byte charge = variant.Item2
                    ? (byte)(_shooter.OwningScene.WeaponRules[NativeWeaponIndex(variant.Item1)].FullCharge * 2) : (byte)0;
                var beam = Emit(variant.Item1, charge: charge, ray: -Vector3.UnitY);
                bool directOnly = !variant.Item2 && variant.Item1 == BeamType.Magmaul;
                if (variant.Item1 == BeamType.Judicator)
                {
                    CollisionResult floor = default;
                    Check(CollisionDetection.CheckBetweenPoints(beam.Position, beam.Position - Vector3.UnitY * 4,
                        TestFlags.Beams, _shooter.OwningScene, ref floor), "tiny Judicator splash fixture resolves real floor");
                    // Native Judicator splash radius is only 0.25 units; put the actual owner body inside it.
                    _shooter.ModPlaceAt(floor.Position + Vector3.UnitY * .1f);
                    Record();
                }
                var claim = SelfClaim(beam); if (directOnly) claim.Flags |= HitClaimPacket.FlagDirect;
                Check(!directOnly || !NetSelfSplash.Validate(0, claim, out _),
                    "returning native self-direct remains acknowledgement-only before authority payment");
                int before = _shooter.Health;
                if (claimFirst)
                {
                    byte[] packet = new byte[1 + HitClaimPacket.Size]; packet[0] = 1; claim.Write(packet.AsSpan(1));
                    NetHitClaims.Receive(0, packet);
                    Check(NetHitClaims.ClaimsPendingCurrent == 1 && _shooter.Health == before,
                        "self claim waits for native authority explosion without speculative authority damage");
                }
                if (variant.Item1 == BeamType.Judicator || variant.Item2 && variant.Item1 == BeamType.Magmaul)
                    ExplodeAtRealFloor(beam);
                else Explode(beam);
                int health = _shooter.Health; var speed = _shooter.Speed;
                ushort sequence = NetDamage.Sequence(0);
                bool splashPaid = NetCombatPayments.TryGet(NetShotProof.PaymentKey(0, claim), claim.Beam, out var paid);
                var directClaim = claim; directClaim.Flags |= HitClaimPacket.FlagDirect;
                bool directPaid = NetCombatPayments.TryGet(NetShotProof.PaymentKey(0, directClaim), claim.Beam, out var directOutcome);
                Check(splashPaid,
                    $"actual {variant} explosion records exact owner component expectedKind={(directOnly ? "direct" : "splash")} payment splashPaid={splashPaid} directPaid={directPaid} directDamage={directOutcome.DamageApplied} totalNativeDamage={before-health} splash={beam.SplashDamage} radius={beam.SplashRadius}");
                byte result = Receive(claim);
                Check(result == HitVerdictPacket.ResultDuplicate && _lastAck.VictimSlot == 0
                    && _lastAck.ShotId == paid.ShotId && _lastAck.HealthAfter == paid.HealthAfter
                    && _lastAck.DamageApplied == paid.DamageApplied && _lastAck.DamageSequence == paid.DamageSequence
                    && _shooter.Health == health && _shooter.Speed == speed,
                    $"native exact self payment returns authoritative outcome {variant} claimFirst={claimFirst} result={result} ackhealth={_lastAck.HealthAfter}/{paid.HealthAfter} ackdamage={_lastAck.DamageApplied}/{paid.DamageApplied} totalNativeDamage={before-health} seq={_lastAck.DamageSequence}/{paid.DamageSequence}");
                byte[] receipt = Bytes(_lastAck);
                Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && Bytes(_lastAck).SequenceEqual(receipt)
                    && _shooter.Health == health && _shooter.Speed == speed && NetDamage.Sequence(0) == sequence,
                    "self terminal retry preserves health, knockback and immutable receipt");
            }
        }
    }
    private static void JudicatorAffinityPolicy()
    {
        Prepare(); _shooter.Hunter = Hunter.Noxus;
        var weapon = _shooter.OwningScene.WeaponRules[NativeWeaponIndex(BeamType.Judicator)];
        Check(weapon.SplashDamage > 0 && weapon.ChargedSplashDamage == 0
            && weapon.Flags.TestFlag(WeaponFlags.AoeCharged),
            "actual Noxus affinity row has uncharged splash and charged ice wave with no splash damage");
        var beam = Emit(BeamType.Judicator, charge: (byte)(weapon.FullCharge * 2), ray: -Vector3.UnitY);
        var claim = SelfClaim(beam); int health = _shooter.Health;
        Check(!NetSelfSplash.Validate(0, claim, out _) && _shooter.Health == health,
            "affinity charged Judicator cannot invent self splash rescue from zero-damage ice-wave profile");
        _shooter.Hunter = Hunter.Samus;
    }
    private static void ProtectedAndReplacement()
    {
        Prepare(); var beam = Emit(BeamType.Missile, ray: -Vector3.UnitY); var claim = SelfClaim(beam);
        typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_shooter, (ushort)1000);
        Explode(beam, protectedOwner: true);
        Check(!NetSelfSplash.Validate(0, claim, out _), "native terminal witness cannot override self spawn protection");
        typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_shooter, (ushort)0);
        Check(!NetSelfSplash.Validate(0, claim, out _), "waiting out protection cannot convert immune witnessed explosion into self rescue");
        Prepare(); beam = Emit(BeamType.Missile, ray: -Vector3.UnitY); claim = SelfClaim(beam);
        _shooter.Spawn(_shooter.Position, Vector3.UnitZ, Vector3.UnitY, _shooter.NodeRef, respawn: true);
        _shooter.ModReleaseSpawnProtection(); int health = _shooter.Health;
        Check(!NetSelfSplash.Validate(0, claim, out _) && _shooter.Health == health,
            "old self shot cannot rescue damage onto replacement owner life");
    }
    private static void MovingJumpAndSelfKill()
    {
        Prepare(); var beam = Emit(BeamType.Missile, ray: -Vector3.UnitY);
        _shooter.ModPlaceAt(_shooter.Position + _direction * .15f);
        _shooter.Speed = _direction * .05f;
        var claim = SelfClaim(beam); Explode(beam);
        int health = _shooter.Health; var speed = _shooter.Speed;
        Check(speed.Y > 0, "moving downward Missile explosion supplies upward rocket-jump impulse");
        Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && _shooter.Health == health
            && _shooter.Speed == speed, "moving self splash reconciles without health or impulse replay");
        Prepare(); beam = Emit(BeamType.Missile, ray: -Vector3.UnitY); claim = SelfClaim(beam);
        _shooter.Health = 1; Explode(beam);
        Check(_shooter.Health == 0, "near-floor low-health owner is killed by actual native self splash");
        Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && _lastAck.HealthAfter == 0,
            "self-kill exact receipt remains available after owner death");
    }
    private static void ChainedAndTeamSplash()
    {
        Prepare(); var first = Emit(BeamType.Missile, ray: -Vector3.UnitY);
        var second = Emit(BeamType.Missile, ray: -Vector3.UnitY, preserve: true);
        Check(first.ModLaunchKey != second.ModLaunchKey, "two explosive emissions retain distinct shot identities");
        Explode(first); int health = _shooter.Health; Explode(second);
        Check(_shooter.Health < health, "independent chained explosions each retain legal self payment");
        Prepare(); var state = _shooter.OwningScene.GameState;
        bool teams = state.Teams; int victimTeam = _victim.TeamIndex;
        try
        {
            state.Teams = true; state.TeamCount = 2; _victim.TeamIndex = _shooter.TeamIndex;
            var beam = Emit(BeamType.Missile, ray: -Vector3.UnitY); var claim = SelfClaim(beam);
            Explode(beam); health = _shooter.Health;
            Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && _shooter.Health == health,
                "team-mode self splash preserves native owner payment without extra claim damage");
        }
        finally { state.Teams = teams; _victim.TeamIndex = victimTeam; }
    }
    private static void SplitNativeHookRescue(BeamType weapon = BeamType.Missile)
    {
        Prepare(); var beam = EmitFloorBlast(weapon);
        CollisionResult floor = default;
        Check(CollisionDetection.CheckBetweenPoints(beam.Position, beam.Position - Vector3.UnitY * 4,
            TestFlags.Beams, _shooter.OwningScene, ref floor), "split-hook fixture captures real floor collision");
        beam.BackPosition = beam.Position; beam.Position = floor.Position + Vector3.UnitY * .05f;
        Vector3 origin = BeamProjectileEntity.ModSplashOrigin(_shooter.OwningScene, beam.Beam, beam.Position, floor.Position);
        // Deliberately isolate the native pre-owner-loop witness hook. This is not a claimed UDP miss.
        NetSelfSplash.Observe(beam, origin);
        var claim = SelfClaim(beam); claim.Damage = 1;
        if (AdditiveBlast(weapon)) claim.Direction = Vector3.UnitY * .9f;
        Check(NetSelfSplash.Validate(0, claim, out var expected) && expected.Damage > claim.Damage,
            "split native pre-payment hook supplies strict source and server-derived splash outcome");
        if (AdditiveBlast(weapon))
            Check(expected.Impulse != claim.Direction,
                $"{_model} {weapon} bounded claimant boost differs from independently derived authority impulse");
        var perturbed = claim; perturbed.HitPoint += Vector3.UnitY * .01f;
        Check(NetSelfSplash.Validate(0, perturbed, out var same) && same == expected,
            "inside-envelope client point cannot change canonical server-body damage or impulse");
        int before = _shooter.Health; var speed = _shooter.Speed;
        Check(Receive(claim) == HitVerdictPacket.ResultApplied && _lastAck.DamageApplied == expected.Damage
            && _shooter.Health == before - expected.Damage,
            "split-hook unpaid self rescue applies server-derived damage rather than claimed one");
        byte[] receipt = Bytes(_lastAck);
        Check(Vector3.Distance(_shooter.Speed, ExpectedBlastVelocity(speed, expected.Impulse, AdditiveBlast(weapon))) < .000001f,
            $"{_model} {weapon} split-hook unpaid self rescue applies server-derived impulse and selected vertical semantics");
        int health = _shooter.Health; speed = _shooter.Speed; ushort sequence = NetDamage.Sequence(0);
        beam.OnCollision(floor, colWith: null);
        Check(_shooter.Health == health && _shooter.Speed == speed && NetDamage.Sequence(0) == sequence,
            "later actual native floor splash cannot repay split-hook rescued component");
        Check(Receive(claim) == HitVerdictPacket.ResultApplied && Bytes(_lastAck).SequenceEqual(receipt),
            "rescued claim retry returns its immutable original applied receipt after native collision");
        Check(Receive(claim) == HitVerdictPacket.ResultApplied && Bytes(_lastAck).SequenceEqual(receipt)
            && _shooter.Health == health && _shooter.Speed == speed && NetDamage.Sequence(0) == sequence,
            "claim retry after rescued native collision preserves one payment and immutable receipt");
    }
    private static void RocketImpulsePayments(BeamType weapon = BeamType.Missile)
    {
        foreach (float incomingY in new[] { .4f, -.01f })
        {
            Prepare(); var beam = EmitFloorBlast(weapon);
            CollisionResult floor = default;
            Check(CollisionDetection.CheckBetweenPoints(beam.Position, beam.Position - Vector3.UnitY * 4,
                TestFlags.Beams, _shooter.OwningScene, ref floor), "rocket combat seam resolves actual room floor");
            beam.BackPosition = beam.Position; beam.Position = floor.Position + Vector3.UnitY * .05f;
            Vector3 origin = BeamProjectileEntity.ModSplashOrigin(_shooter.OwningScene, beam.Beam, beam.Position, floor.Position);
            // Incoming velocity is a combat-seam fixture; the terminal collision,
            // damage calculation, authority witness and payment are production paths.
            Vector3 incoming = _direction * .06f + Vector3.UnitY * incomingY;
            _shooter.Speed = incoming;
            bool additive = AdditiveBlast(weapon);
            Check(_shooter.ModMovementBlastWeaponEligible(weapon) == additive,
                $"admitted {_model} rule owns {weapon} blast eligibility on authority puppet body");
            NetSelfSplash.Observe(beam, origin);
            var claim = SelfClaim(beam);
            Check(NetSelfSplash.Validate(0, claim, out var expected), "pre-payment witness independently derives floor rocket outcome");
            if (additive)
            {
                Check(expected.Impulse.Y > .26f && expected.Impulse.Length <= Quake3MovementProfileV2.MaximumRocketImpulse + .000001f,
                    "actual floor rocket witness supplies bounded lift above native quarter-unit cap");
                var capsuleImpulse = Quake3MovementProfileV3.RocketImpulse((uint)beam.SplashDamage, origin,
                    _shooter.Position, beam.SplashRadius, _shooter.ModBipedBottom, _shooter.ModBipedTop,
                    Fixed.ToFloat(_shooter.Values.BipedColRadius));
                Check(Vector3.Distance(expected.Impulse, capsuleImpulse) < .000001f,
                    "current authority floor witness uses base splash strength and the canonical biped capsule");
            }
            int before = _shooter.Health;
            beam.OnCollision(floor, colWith: null);
            PlayerState state = default; NetDamage.Write(0, ref state);
            Check(before - _shooter.Health == expected.Damage
                && Vector3.Distance(state.HitDirection, expected.Impulse) < .000001f,
                "actual native damage and recorded impulse equal independent pre-payment self witness");
            Vector3 expectedSpeed = ExpectedBlastVelocity(incoming, expected.Impulse, additive);
            Check(Vector3.Distance(_shooter.Speed, expectedSpeed) < .000001f,
                $"{_model} actual {weapon} floor blast preserves horizontal momentum and {(incomingY > 0 ? "rising" : "falling")} vertical semantics");
            Check(additive ? _shooter.Speed.Y > .25f
                : incomingY > 0 ? _shooter.Speed.Y == incomingY : _shooter.Speed.Y <= .25f,
                $"{_model} actual {weapon} floor blast {(additive ? "adds uncapped lift" : "retains Classic vertical cap")}");
            int health = _shooter.Health; var speed = _shooter.Speed; ushort sequence = NetDamage.Sequence(0);
            Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && _shooter.Health == health
                && _shooter.Speed == speed && NetDamage.Sequence(0) == sequence,
                "floor rocket native payment cannot repay from exact self claim");
            byte[] receipt = Bytes(_lastAck);
            Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && Bytes(_lastAck).SequenceEqual(receipt)
                && _shooter.Health == health && _shooter.Speed == speed && NetDamage.Sequence(0) == sequence,
                $"{_model} {weapon} native-first claim retry preserves immutable receipt, health and velocity");
        }
    }
    private static void CapsuleBoundaryAndHandicapPayments(BeamType weapon = BeamType.Missile)
    {
        if (!AdditiveBlast(weapon)) return;
        foreach (float boundaryOffset in new[] { -.01f, .01f })
        {
            Prepare(); var beam = EmitFloorBlast(weapon);
            CollisionResult floor = default;
            Check(CollisionDetection.CheckBetweenPoints(beam.Position, beam.Position - Vector3.UnitY * 4,
                TestFlags.Beams, _shooter.OwningScene, ref floor), "capsule boundary resolves actual room floor");
            beam.BackPosition = beam.Position; beam.Position = floor.Position + Vector3.UnitY * .05f;
            Vector3 origin = BeamProjectileEntity.ModSplashOrigin(_shooter.OwningScene, beam.Beam, beam.Position, floor.Position);
            _shooter.ModPlaceAt(_shooter.Position.WithY(origin.Y + beam.SplashRadius - _shooter.ModBipedBottom + boundaryOffset));
            // Keep the horizontal source location exactly under its authority body.
            beam.Position = beam.Position.WithX(_shooter.Position.X).WithZ(_shooter.Position.Z);
            origin = BeamProjectileEntity.ModSplashOrigin(_shooter.OwningScene, beam.Beam, beam.Position, floor.Position);
            Check(Vector3.Distance(origin, _shooter.Position) > beam.SplashRadius,
                "capsule boundary fixture lies outside the historical centre-point radius");
            NetSelfSplash.Observe(beam, origin); var claim = SelfClaim(beam);
            bool inside = boundaryOffset < 0;
            Check(NetSelfSplash.Validate(0, claim, out var expected) == inside,
                "authority capsule radius admits its inside edge and refuses its outside edge");
            int health = _shooter.Health; var speed = _shooter.Speed;
            beam.OnCollision(floor, colWith: null);
            if (inside)
            {
                Check(health - _shooter.Health == expected.Damage && expected.Damage > 0
                    && Vector3.Distance(_shooter.Speed, speed + expected.Impulse) < .000001f,
                    "native inside-capsule edge pays the same captured authority damage and additive impulse");
                Check(Receive(claim) == HitVerdictPacket.ResultDuplicate,
                    "inside-capsule native payment cannot be repaid by its self claim");
                byte[] receipt = Bytes(_lastAck); int paidHealth = _shooter.Health; var paidSpeed = _shooter.Speed;
                Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && Bytes(_lastAck).SequenceEqual(receipt)
                    && _shooter.Health == paidHealth && _shooter.Speed == paidSpeed,
                    "capsule-edge retry preserves immutable receipt and exactly one physical impulse");
            }
            else Check(_shooter.Health == health && _shooter.Speed == speed,
                "outside-capsule native floor explosion grants neither health damage nor movement");
        }
        byte oldReduction = NetSession.SlotDamageReduction[0];
        try
        {
            Vector3? unmodifiedImpulse = null; ushort unmodifiedDamage = 0;
            foreach (byte reduction in new byte[] { 0, 10, 50 })
            {
                Prepare(); NetSession.SlotDamageReduction[0] = reduction;
                var beam = EmitFloorBlast(weapon); CollisionResult floor = default;
                Check(CollisionDetection.CheckBetweenPoints(beam.Position, beam.Position - Vector3.UnitY * 4,
                    TestFlags.Beams, _shooter.OwningScene, ref floor), "handicap fixture resolves actual room floor");
                beam.BackPosition = beam.Position; beam.Position = floor.Position + Vector3.UnitY * .05f;
                Vector3 origin = BeamProjectileEntity.ModSplashOrigin(_shooter.OwningScene, beam.Beam, beam.Position, floor.Position);
                NetSelfSplash.Observe(beam, origin); var claim = SelfClaim(beam);
                Check(NetSelfSplash.Validate(0, claim, out var expected), "handicap fixture retains an admitted source and floor witness");
                if (unmodifiedImpulse is not { } baseline)
                { unmodifiedImpulse = expected.Impulse; unmodifiedDamage = expected.Damage; }
                else Check(Vector3.Distance(baseline, expected.Impulse) < .000001f
                    && expected.Damage == PlayerHandicap.ScaleDamage(unmodifiedDamage, reduction),
                    "roster handicap changes native health cost while preserving the same physical blast");
                NetSession.SlotDamageReduction[0] = reduction == 0 ? (byte)50 : (byte)0;
                Check(NetSelfSplash.Validate(0, claim, out var retained) && retained == expected,
                    "later handicap changes cannot resample an authority explosion's captured cost or impulse");
                NetSession.SlotDamageReduction[0] = reduction;
                int health = _shooter.Health; var speed = _shooter.Speed;
                beam.OnCollision(floor, colWith: null);
                Check(health - _shooter.Health == expected.Damage
                    && Vector3.Distance(_shooter.Speed, speed + expected.Impulse) < .000001f,
                    "native handicap payment agrees with captured authority health and full additive impulse");
                Check(Receive(claim) == HitVerdictPacket.ResultDuplicate,
                    "handicap self claim acknowledges the existing native payment");
                byte[] receipt = Bytes(_lastAck); int paidHealth = _shooter.Health; var paidSpeed = _shooter.Speed;
                Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && Bytes(_lastAck).SequenceEqual(receipt)
                    && _shooter.Health == paidHealth && _shooter.Speed == paidSpeed,
                    "handicap receipt retry cannot debit health or apply blast twice");
            }
        }
        finally { NetSession.SlotDamageReduction[0] = oldReduction; }
    }
    private static void StaleAndInvalidRocketWitnesses(BeamType weapon = BeamType.Missile)
    {
        foreach (string variant in new[] { "match", "epoch", "generation", "life", "victim-life", "launch", "nonfinite-point", "expired", "nonfinite-direction", "oversized-direction" })
        {
            Prepare(); var beam = EmitFloorBlast(weapon);
            CollisionResult floor = default;
            Check(CollisionDetection.CheckBetweenPoints(beam.Position, beam.Position - Vector3.UnitY * 4,
                TestFlags.Beams, _shooter.OwningScene, ref floor), "invalid witness fixture captures actual floor");
            beam.BackPosition = beam.Position; beam.Position = floor.Position + Vector3.UnitY * .05f;
            NetSelfSplash.Observe(beam, BeamProjectileEntity.ModSplashOrigin(_shooter.OwningScene, beam.Beam, beam.Position, floor.Position));
            var claim = SelfClaim(beam);
            Check(NetSelfSplash.Validate(0, claim, out _), "invalid witness fixture begins with valid unpaid authority outcome");
            if (variant == "match") claim.MatchId++;
            if (variant == "epoch") claim.AuthorityEpoch++;
            if (variant == "generation") claim.ShooterGeneration++;
            if (variant == "life") claim.ShooterLifeId++;
            if (variant == "victim-life") claim.VictimLifeId++;
            if (variant == "launch") claim.LaunchFrame++;
            if (variant == "nonfinite-point") claim.HitPoint = new Vector3(float.NaN, 0, 0);
            if (variant == "expired") Frame(_frame + 513);
            int health = _shooter.Health; var speed = _shooter.Speed; ushort sequence = NetDamage.Sequence(0);
            if (variant == "nonfinite-direction")
            {
                // The wire uses signed integers, so it cannot carry NaN. Probe
                // the finite gate directly rather than claiming a codec outcome.
                var gate = typeof(NetHitClaims).GetMethod(AdditiveBlast(weapon)
                    ? "ValidQuake3RocketCandidate" : "ValidClaimImpulse", BindingFlags.Static | BindingFlags.NonPublic)!;
                object[] arguments = AdditiveBlast(weapon)
                    ? new object[] { new Vector3(float.NaN, 0, 0) }
                    : new object[] { (byte)weapon, _shooter.Hunter, new Vector3(float.NaN, 0, 0) };
                Check(!(bool)gate.Invoke(null, arguments)! && _shooter.Health == health
                    && _shooter.Speed == speed && NetDamage.Sequence(0) == sequence,
                    "direct candidate validator rejects unrepresentable nonfinite direction without mutation");
                continue;
            }
            if (variant == "oversized-direction")
            {
                claim.Direction = Vector3.UnitY * 1.3f;
                byte result = Receive(claim);
                Check(result != HitVerdictPacket.ResultApplied && result != HitVerdictPacket.ResultDuplicate
                    && _shooter.Health == health && _shooter.Speed == speed && NetDamage.Sequence(0) == sequence,
                    $"{_model} {weapon} wire claim rejects {variant} candidate without paying valid unpaid witness");
                continue;
            }
            Check(!NetSelfSplash.Validate(0, claim, out _) && _shooter.Health == health
                && _shooter.Speed == speed && NetDamage.Sequence(0) == sequence,
                $"{_model} {weapon} self blast rejects {variant} witness without health or impulse mutation");
        }
    }
    private static void BurnOwnership()
    {
        Prepare(); var beam = Emit(BeamType.Magmaul);
        var source = NetBurnSource.From(beam, 100, balanced: false);
        _victim.ModStartBurn(source);
        Check(_victim.ModBurnSource is { } captured && captured.Key == beam.ModLaunchKey
            && captured.Component == beam.ModComponentId && captured.LaunchFrame == beam.ModLaunchFrame,
            "burn captures original projectile shot, component and launch ownership");
        var timer = typeof(PlayerEntity).GetField("_burnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        timer.SetValue(_victim, (ushort)40); _victim.ModStartBurn(source);
        Check((ushort)timer.GetValue(_victim)! == 40,
            "same shot and component cannot refresh active burn duration");
        typeof(PlayerEntity).GetField("_doubleDmgTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_shooter, (ushort)1000);
        beam.Lifespan = 0; int health = _victim.Health;
        source.Apply(_victim);
        Check(_victim.Health == health - source.Damage && _victim.ModBurnSource == source,
            "burn tick retains captured damage after projectile removal and later owner double damage");
        ushort life = source.Key.LifeId;
        _shooter.Spawn(_shooter.Position, Vector3.UnitZ, Vector3.UnitY, _shooter.NodeRef, respawn: true);
        Check(_victim.ModBurnSource!.Value.Key.LifeId == life
            && NetPlayerLifecycle.Get(0) != life,
            "owner respawn cannot rewrite active burn launch life");
        Frame(_frame + 16); health = _victim.Health; source.Apply(_victim);
        PlayerState state = default; state.SlotIndex = 1;
        state.SlotGeneration = NetPlayerLifecycle.Generation(1); state.LifeId = NetPlayerLifecycle.Get(1);
        NetDamage.Write(1, ref state);
        Check(_victim.Health == health - source.Damage
            && NetDamage.ReplayAttackerLife(state, source.Key.Generation) == life,
            "burn after owner respawn retains original launch life in replay attribution");
        NetPlayerLifecycle.SetOccupant(0, (ushort)(source.Key.Generation + 1));
        Check(source.ResolveOwner(_victim.OwningScene) == null,
            "disconnected original owner cannot resolve to replacement occupant for burn credit");
        Frame(_frame + 16); _victim.Health = 1;
        int points = GameState.Points[0], kills = GameState.Kills[0]; source.Apply(_victim);
        var lastHit = (uint[])typeof(NetHitClaims).GetField("_lastHitFire", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Check(lastHit[1] == _frame && lastHit[1] != source.LaunchFrame,
            "lethal burn uses current authority damage time rather than backdating death to original shot");
        Check(_victim.Health == 0 && GameState.Points[0] == points && GameState.Kills[0] == kills,
            "burn survives owner replacement but cannot credit replacement occupant for lethal tick");
    }
    private static void BombLifecycleFence()
    {
        foreach (string variant in new[] { "current", "match", "epoch", "generation", "authority-old-life", "client-old-life" })
        {
            Prepare(); _shooter.Hunter = Hunter.Samus;
            var bomb = BombEntity.Spawn(_shooter, Matrix4.CreateTranslation(_shooter.Position), _shooter.OwningScene);
            Check(bomb != null && bomb.ModOwnerKey == ShotKey.For(0, 0),
                "real bomb placement captures full original owner lifecycle");
            bomb!.NodeRef = _shooter.NodeRef;
            bomb.Radius = Fixed.ToFloat(_shooter.Values.BombRadius);
            bomb.SelfRadius = Fixed.ToFloat(_shooter.Values.BombSelfRadius);
            bomb.Damage = (ushort)_shooter.Values.BombDamage;
            bomb.EnemyDamage = (ushort)_shooter.Values.BombEnemyDamage;
            var key = bomb.ModOwnerKey;
            if (variant == "match") bomb.ModOwnerKey = key with { MatchId = (ushort)(key.MatchId + 1) };
            if (variant == "epoch") bomb.ModOwnerKey = key with { AuthorityEpoch = key.AuthorityEpoch + 1 };
            if (variant == "generation") bomb.ModOwnerKey = key with { Generation = (ushort)(key.Generation + 1) };
            if (variant.Contains("old-life"))
            {
                _shooter.Spawn(_shooter.Position, Vector3.UnitZ, Vector3.UnitY, _shooter.NodeRef, respawn: true);
                Check(bomb.ModOwnerKey == key && NetPlayerLifecycle.Get(0) != key.LifeId,
                    "live bomb survives respawn retaining original owner life");
            }
            var role = NetSession.Role; bool authority = NetSession.IsAuthority; int local = NetSession.LocalSlot;
            try
            {
                if (variant == "client-old-life")
                {
                    typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Client);
                    typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, false);
                    typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, 0);
                }
                bool refused = variant is "match" or "epoch" or "generation" or "client-old-life";
                Check(NetDamage.Suppress(_victim, bomb, DamageFlags.NoDmgInvuln) == refused,
                    $"bomb captured lifecycle suppression policy {variant}");
                _victim.ModPlaceAt(bomb.Position + Vector3.UnitX * .1f);
                int health = _victim.Health; ushort sequence = NetDamage.Sequence(1);
                bomb.Countdown = 0; bomb.Process();
                Check(refused ? _victim.Health == health && NetDamage.Sequence(1) == sequence
                    : _victim.Health < health && NetDamage.Sequence(1) != sequence,
                    $"actual native bomb detonation respects lifecycle fence {variant}");
                _shooter.ModPlaceAt(bomb.Position); _shooter.Speed = Vector3.Zero;
                bool jump = _shooter.CheckHitByBomb(bomb, halfturret: false);
                Check(refused ? !jump && _shooter.Speed == Vector3.Zero : jump && _shooter.Speed.Y > 0,
                    $"actual owner bomb jump respects captured lifecycle fence {variant}");
            }
            finally
            {
                typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, role);
                typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, authority);
                typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, local);
            }
        }
    }
    public static int Run(string profile, string room, MovementModel model = MovementModel.Native)
    {
        bool accepted = NetFireEvents.AcceptedPlaybackEnabled; var mode = NetShotProof.Mode; bool selfEnabled = NetSelfSplash.Enabled; NetSelfSplash.Enabled = true;
        _checks = 0; _direction = Vector3.Zero; _model = model;
        var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
        for (byte i = 0; i < 2; i++) { roster.Slots[i] = i; roster.Generations[i] = 1; }
        var session = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, Revision = 1,
            Phase = SessionPhase.InMatch, MaxPlayers = 2, WorldProfile = MatchWorldProfile.Resolve(2),
            Match = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle,
                TimeLimitSeconds = 0, PointGoal = 10000, MovementModel = model } };
        var sim = new ServerSim();
        if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { }, roster, session))
        { NetFireEvents.AcceptedPlaybackEnabled = accepted; NetShotProof.Mode = mode; NetSelfSplash.Enabled = selfEnabled; return 1; }
        try
        {
            for (int i = 0; i < 120; i++) sim.Step();
            _shooter = PlayerEntity.Players[0]; _victim = PlayerEntity.Players[1]; _base = _shooter.Position;
            _shooter.OwningScene.GameState.EnhancedHunters = false;
            _shooter.OwningScene.GameState.BalancedMode = false;
            Check(NetSession.Active && NetSession.Role == NetRole.Server && NetSession.IsAuthority
                && NetSession.LocalSlot == -1, "actual ServerSim owns dedicated authority without role fabrication");
            Check(_shooter.OwningScene.GameState.MovementModel == model
                && NetSession.ActiveMatchDefinition?.MovementModel == model
                && NetSession.ServerMatch is { } match
                && MovementModelRules.FromRules((MatchModifierFlags)match.RuleBits) == model
                && NetRoomChange.GameplayReady && !NetSession.FreezeGameplay,
                "scene, admitted definition, MatchState and room readiness agree on selected movement");
            Check(_shooter.OwningScene.Quake3TechniquesActive == (model is MovementModel.Quake3 or MovementModel.Traversal),
                "actual foreground authority scene selects Q3 techniques only for admitted Q3 or Traversal rule");
            NetFireEvents.AcceptedPlaybackEnabled = true; NetShotProof.Mode = ShotProofMode.Shadow; _frame = NetSession.NetFrame;
            foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
                if (NetDynamicGeometryHistory.TryTraceDistanceAtFrame(_shooter.OwningScene,
                    _base + Vector3.UnitY * .5f, _base + direction * 4 + Vector3.UnitY * .5f, _frame, out float wall) && wall == 1)
                { _direction = direction; break; }
            Check(_direction.LengthSquared == 1, "asset fixture provides clear production collision lane");

            SelfPredictionTests.RunReplay(_shooter);
            SupportedPolicy(); ForgedClaims(); ActualExplosions(); JudicatorAffinityPolicy(); ProtectedAndReplacement(); MovingJumpAndSelfKill(); ChainedAndTeamSplash(); SplitNativeHookRescue(); RocketImpulsePayments(); CapsuleBoundaryAndHandicapPayments(); StaleAndInvalidRocketWitnesses();
            ForgedClaims(BeamType.Magmaul); RocketImpulsePayments(BeamType.Magmaul);
            CapsuleBoundaryAndHandicapPayments(BeamType.Magmaul);
            SplitNativeHookRescue(BeamType.Magmaul); StaleAndInvalidRocketWitnesses(BeamType.Magmaul);
            BombLifecycleFence(); BurnOwnership();
            long lifetimePayments = NetCombatPayments.LifetimePayments;
            long lifetimeSuppressed = NetCombatPayments.LifetimeSuppressed;
            Check(lifetimePayments > 0, "process evidence captures actual payments and suppressed repeats");
            NetHitClaims.Reset();
            Check(NetCombatPayments.LifetimePayments == lifetimePayments && NetCombatPayments.LifetimeSuppressed == lifetimeSuppressed
                && NetCombatPayments.Payments == 0 && NetCombatPayments.Suppressed == 0,
                "room teardown clears live ledger without erasing process payment evidence");
            Check(sim.StepFailures == 0, "no simulation failures");
            Console.WriteLine($"PASS: {_checks} self splash asset assertions ({profile}, {model}); admitted ServerSim combat seams, no live UDP qualification"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { sim.Stop(); NetSession.Stop(); NetFireEvents.AcceptedPlaybackEnabled = accepted; NetShotProof.Mode = mode; NetSelfSplash.Enabled = selfEnabled; }
    }
}
