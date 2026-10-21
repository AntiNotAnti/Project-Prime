using System;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Asset-backed native damage and arbitration regressions. Fixtures emit through
// accepted fire playback; native repeats use the actual public TakeDamage hook.
internal static class CombatArbitrationTests
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
    { _checks++; if (!ok) throw new InvalidOperationException(label); Console.WriteLine($"COMBAT PASS {label}"); }
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
    private static BeamProjectileEntity Emit(BeamType weapon = BeamType.Imperialist, byte charge = 0,
        uint modifiers = 0, Vector3? ray = null, bool preserve = false, Vector3? ownerAim = null, uint? sourceFrame = null,
        bool? affinity = null)
    {
        if (!preserve) foreach (var old in _shooter.EquipInfo.Beams) old.Lifespan = 0;
        var direction = ray ?? _direction;
        uint source = sourceFrame ?? _frame;
        var aim = ownerAim ?? direction.Normalized();
        if (affinity ?? Weapons.GetAffinityBeam(_shooter.Hunter) == weapon)
            modifiers |= FireEvent.AffinityStateBit;
        else if (affinity.HasValue) modifiers &= ~FireEvent.AffinityStateBit;
        var fire = new FireEvent(++_id, source, _frame, 0, FireEventKind.PressFire,
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
    private static byte[] Bytes(in CombatAckEntry ack)
    { byte[] bytes = new byte[CombatAckEntry.Size]; ack.Write(bytes); return bytes; }
    private static void Native(BeamProjectileEntity beam, bool direct = true, uint damage = 12)
    {
        beam.EnhancedDirectHit = direct;
        try { _victim.TakeDamage(damage, DamageFlags.NoDmgInvuln, Vector3.UnitY * .025f, beam); }
        finally { beam.EnhancedDirectHit = false; }
    }
    private static void Unchanged(BeamProjectileEntity beam, bool direct, string label)
    {
        int health = _victim.Health; ushort sequence = NetDamage.Sequence(1); var speed = _victim.Speed;
        var effects = (_victim.ModFrozen, _victim.ModBurning, _victim.ModDisrupted);
        Native(beam, direct);
        Check(_victim.Health == health && NetDamage.Sequence(1) == sequence && _victim.Speed == speed
            && (_victim.ModFrozen, _victim.ModBurning, _victim.ModDisrupted) == effects, label);
    }
    private static void RescueFirst()
    {
        NetShotProof.Mode = ShotProofMode.Shadow; Prepare(); var beam = Emit(BeamType.Judicator);
        var claim = Claim(beam, damage: 12); claim.Flags |= HitClaimPacket.FlagFrozen;
        int before = _victim.Health; uint arrived = _frame;
        Check(Receive(claim) == HitVerdictPacket.ResultApplied && _victim.Health == before - 12 && _victim.ModFrozen,
            "rescue-first actual receive/tick pays damage and affliction");
        Check(_frame - arrived >= NetHitClaims.MinGraceFrames && _frame - arrived <= NetHitClaims.MaxGraceFrames,
            "rescue retains 24-72 frame arbitration grace");
        Unchanged(beam, true, "rescued exact direct suppresses late native damage/effects/impulse");
        Unchanged(beam, true, "rescued exact direct remains protected after first native arrival");
        Unchanged(beam, false,
            "rescued direct refuses alternate splash for the same projectile/victim");
        var spawned = BeamProjectileEntity.Spawn(_shooter, beam.Equip!, beam.Position, _direction,
            BeamSpawnFlags.NoMuzzle, _shooter.NodeRef, _shooter.OwningScene, parent: beam);
        var child = _shooter.EquipInfo.Beams.FirstOrDefault(b => b != beam && b.Lifespan > 0
            && b.ModLaunchKey == beam.ModLaunchKey && b.ModComponentId != 0 && b.ModComponentId != beam.ModComponentId);
        Check(spawned != BeamResultFlags.NoSpawn && child != null, "native child has independent component identity");
        before = _victim.Health; Native(child!);
        Check(_victim.Health < before, "rescued parent does not suppress distinct native child");
        Unchanged(child!, true, "native child component pays only once");
    }
    private static void RicochetAlternateDamage()
    {
        foreach (BeamType weapon in new[] { BeamType.Judicator, BeamType.Magmaul })
        {
            foreach (bool rescuedDirect in new[] { true, false })
            {
                NetShotProof.Mode = ShotProofMode.Shadow; Prepare();
                var beam = Emit(weapon);
                var claim = Claim(beam, direct: rescuedDirect, damage: 12);
                int before = _victim.Health;
                Check(Receive(claim) == HitVerdictPacket.ResultApplied
                    && _victim.Health == before - 12,
                    $"{weapon} {(rescuedDirect ? "direct" : "splash")} rescue settles once");
                Unchanged(beam, !rescuedDirect,
                    $"{weapon} late native opposite kind cannot double rescued damage");
                Unchanged(beam, rescuedDirect,
                    $"{weapon} late native same kind remains idempotent");
            }
            NetShotProof.Mode = ShotProofMode.Shadow; Prepare();
            var physical = Emit(weapon);
            int beforeNative = _victim.Health;
            Native(physical, direct: true);
            int nativeHealth = _victim.Health;
            Check(nativeHealth < beforeNative,
                $"{weapon} actual native direct payment exists");
            ushort sequence = NetDamage.Sequence(_victim.SlotIndex);
            var splashClaim = Claim(physical, direct: false, damage: 12);
            Check(Receive(splashClaim) == HitVerdictPacket.ResultDuplicate
                && _victim.Health == nativeHealth
                && NetDamage.Sequence(_victim.SlotIndex) == sequence,
                $"{weapon} alternate splash claim acknowledges native payment without another hit");
        }
    }

    private static void DerivedCatchUpPool()
    {
        NetShotProof.Mode = ShotProofMode.Shadow; Prepare();
        var preExisting = Emit(BeamType.Missile);
        Frame(_frame + 100); Record();
        var pool = _shooter.EquipInfo.Beams;
        typeof(NetUnlagged).GetMethod("CaptureShotPool", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { pool });
        var root = Emit(BeamType.Judicator, preserve: true);
        int roots = NetUnlagged.MarkNewCatchUpFlights(pool, out var launched);
        Check(roots == 1 && launched == root.ModLaunchKey,
            "catch-up captures only the admitted new flight and leaves old missiles alone");
        Check(!NetUnlagged.CatchUpDerivedFrom(launched, launched, root.ModComponentId)
            && !NetUnlagged.CatchUpDerivedFrom(launched, preExisting.ModLaunchKey,
                NetShotProof.ComponentId(preExisting.ModComponentId, 1)),
            "catch-up descendants require an actual child and the exact originating shot");
        var spawn = BeamProjectileEntity.Spawn(_shooter, root.Equip!, root.Position, _direction,
            BeamSpawnFlags.NoMuzzle, _shooter.NodeRef, _shooter.OwningScene, parent: root);
        var child = pool.FirstOrDefault(b => b != root && b.Lifespan > 0
            && b.ModLaunchKey == launched && b.ModComponentId >> 4 == root.ModComponentId);
        Check(spawn != BeamResultFlags.NoSpawn && child != null,
            "actual native ricochet child inherits the shot key and hierarchical component");
        long beforeAdoption = NetUnlagged.DerivedCatchUpSpawns;
        Check(NetUnlagged.AdoptDerivedCatchUp(pool, launched) == 1
            && NetUnlagged.DerivedCatchUpSpawns == beforeAdoption + 1
            && NetUnlagged.AdoptDerivedCatchUp(pool, launched) == 0,
            "child enters catch-up exactly once after its spawning frame");
        var active = (bool[])typeof(NetUnlagged)
            .GetField("_beamsBefore", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Check(active[Array.IndexOf(pool, child!)]
            && !active[Array.IndexOf(pool, preExisting)],
            "catch-up excludes the previous shot even when it remains alive");
        NetUnlagged.AbortShot();
    }

    private static void PhysicalOnce()
    {
        Prepare(); var beam = Emit(BeamType.Judicator); Collide(beam);
        // Collide clears EnhancedDirectHit after returning; repeat the exact direct kind.
        Unchanged(beam, true, "actual physical collision cannot pay a repeated native direct");
        _victim.ModSetFrozen(false); beam.Afflictions |= Affliction.Freeze;
        Unchanged(beam, true, "paid native component cannot introduce a later affliction");
    }
    private static void PendingSeenCollision()
    {
        NetShotProof.Mode = ShotProofMode.Shadow; Prepare(); var beam = Emit(BeamType.Missile);
        var first = Claim(beam, damage: 12);
        Check(first.ClaimId <= ushort.MaxValue - NetHitClaims.SeenCapacity, "pending collision fixture IDs do not wrap");
        var second = first; second.ClaimId = (ushort)(first.ClaimId + NetHitClaims.SeenCapacity);
        var receipts = new System.Collections.Generic.List<CombatAckEntry>();
        var saved = NetHitClaims.CombatAckSink;
        void Send(in HitClaimPacket value)
        { byte[] packet = new byte[1 + HitClaimPacket.Size]; packet[0] = 1; value.Write(packet.AsSpan(1)); NetHitClaims.Receive(0, packet); }
        int before = _victim.Health;
        try
        {
            NetHitClaims.CombatAckSink = (slot, entries) =>
            { if (slot == 0) foreach (var entry in entries) receipts.Add(entry); };
            Send(first); NetHitClaims.Tick();
            Check(NetHitClaims.ClaimsPendingCurrent == 1 && receipts.Count == 0 && _victim.Health == before,
                "first full-key claim stays pending inside arbitration grace");
            Send(second); NetHitClaims.Tick();
            Check(receipts.Count == 0 && NetHitClaims.ClaimsPendingCurrent == 2 && _victim.Health == before,
                "claim IDs separated by current seen modulus coexist pending without early damage");
            Frame(_frame + (uint)NetHitClaims.MaxGraceFrames); NetHitClaims.Tick();
            var firstAck = receipts.Single(entry => entry.ClaimId == first.ClaimId);
            var secondAck = receipts.Single(entry => entry.ClaimId == second.ClaimId);
            Check(receipts.Count == 2 && firstAck.Result == HitVerdictPacket.ResultApplied
                && secondAck.Result == HitVerdictPacket.ResultDuplicate
                && firstAck.ShotId == first.ShotId && secondAck.ShotId == first.ShotId
                && firstAck.VictimGeneration == first.VictimGeneration && firstAck.VictimLife == first.VictimLifeId
                && secondAck.VictimGeneration == first.VictimGeneration && secondAck.VictimLife == first.VictimLifeId
                && firstAck.DamageApplied == 12 && secondAck.DamageApplied == 12
                && firstAck.HealthAfter == before - 12 && secondAck.HealthAfter == before - 12
                && _victim.Health == before - 12 && NetHitClaims.ClaimsPendingCurrent == 0,
                "same component pending claims settle Applied and AlreadyResolved with exactly one payment");
            byte[] canonicalFirst = Bytes(firstAck), canonicalSecond = Bytes(secondAck);
            Send(first); Send(second); NetHitClaims.Tick();
            Check(receipts.Count == 4 && Bytes(receipts[2]).SequenceEqual(canonicalFirst)
                && Bytes(receipts[3]).SequenceEqual(canonicalSecond) && _victim.Health == before - 12,
                "both formerly colliding claim IDs retain independent immutable terminal receipts");
        }
        finally { NetHitClaims.CombatAckSink = saved; }
    }
    private static void ImperialistCorrectionOnce()
    {
        NetShotProof.Mode = ShotProofMode.Shadow; Prepare();
        var beam = Emit(BeamType.Imperialist, modifiers: FireEvent.ScopedStateBit);
        int before = _victim.Health; Collide(beam);
        int body = before - _victim.Health;
        ushort head = (ushort)beam.HeadshotDamage;
        Check(head > body, "scoped native Imperialist body has correctable head damage difference");
        var claim = Claim(beam, damage: head); claim.Flags |= HitClaimPacket.FlagHeadshot;
        Check(Receive(claim) == (byte)CombatAckResult.Corrected && _victim.Health == before - head
            && NetHitClaims.HeadshotCorrections == 1 && _lastAck.DamageApplied == head
            && (_lastAck.Flags & CombatAckFlags.Headshot) != 0,
            "validated legacy Imperialist head claim adds only native body difference");
        byte[] canonical = Bytes(_lastAck); int health = _victim.Health; ushort sequence = NetDamage.Sequence(1);
        Check(Receive(claim) == (byte)CombatAckResult.Corrected && Bytes(_lastAck).SequenceEqual(canonical)
            && _victim.Health == health && NetDamage.Sequence(1) == sequence && NetHitClaims.HeadshotCorrections == 1,
            "corrected Imperialist exact retry repeats immutable outcome without another correction");
        var another = claim; another.ClaimId = ++_claimId;
        byte anotherResult = Receive(another);
        Check((anotherResult == (byte)CombatAckResult.Corrected || anotherResult == (byte)CombatAckResult.AlreadyResolved)
            && _lastAck.DamageApplied == head && (_lastAck.Flags & CombatAckFlags.Headshot) != 0
            && _victim.Health == health && NetDamage.Sequence(1) == sequence && NetHitClaims.HeadshotCorrections == 1,
            "corrected Imperialist new claim ID acknowledges corrected payment once");
        beam.Afflictions |= Affliction.Freeze;
        Unchanged(beam, true, "corrected Imperialist payment suppresses repeat native damage and added effects");
    }
    private static void TerminalRetries()
    {
        foreach (bool physical in new[] { false, true })
        {
            NetShotProof.Mode = ShotProofMode.Shadow; Prepare(); var beam = Emit(BeamType.Missile);
            if (physical) Collide(beam);
            var claim = Claim(beam, damage: 12);
            byte result = Receive(claim); byte[] canonical = Bytes(_lastAck);
            Check(result == (physical ? HitVerdictPacket.ResultDuplicate : HitVerdictPacket.ResultApplied),
                $"terminal fixture settles physical={physical}");
            int settledHealth = _victim.Health; ushort settledSequence = NetDamage.Sequence(1);
            Check(Receive(claim) == result && Bytes(_lastAck).SequenceEqual(canonical)
                && _victim.Health == settledHealth && NetDamage.Sequence(1) == settledSequence,
                $"exact original retry preserves terminal outcome physical={physical}");
            var another = claim; another.ClaimId = ++_claimId;
            Check(Receive(another) == HitVerdictPacket.ResultDuplicate
                && _victim.Health == settledHealth && NetDamage.Sequence(1) == settledSequence,
                $"new claim ID acknowledges same paid component without damage physical={physical}");
            var forged = claim; forged.ComponentId += 10000;
            NetShotProof.Mode = ShotProofMode.Enforce;
            Check(Receive(forged) != result && !Bytes(_lastAck).SequenceEqual(canonical)
                && _victim.Health == settledHealth && NetDamage.Sequence(1) == settledSequence,
                $"changed component cannot replay cached accepted outcome physical={physical}");
            NetShotProof.Mode = ShotProofMode.Shadow;
            Check(Receive(claim) == result && Bytes(_lastAck).SequenceEqual(canonical),
                $"identity mismatch cannot replace original terminal outcome physical={physical}");
            foreach (var player in new[] { _victim, _shooter })
            {
                player.Spawn(player.Position, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: true);
                int health = _victim.Health; ushort sequence = NetDamage.Sequence(1);
                Check(Receive(claim) == result && Bytes(_lastAck).SequenceEqual(canonical)
                    && _victim.Health == health && NetDamage.Sequence(1) == sequence,
                    $"terminal retry immutable after slot {player.SlotIndex} respawn physical={physical}");
            }
        }
    }
    private static void TerminalRefusalCacheChurn()
    {
        foreach (bool wrongLife in new[] { false, true })
        {
            NetShotProof.Mode = ShotProofMode.Shadow; Prepare(); var beam = Emit(BeamType.Missile);
            var claim = Claim(beam, damage: wrongLife ? (ushort)12 : ushort.MaxValue);
            if (wrongLife) claim.VictimLifeId++;
            byte expected = wrongLife ? HitVerdictPacket.ResultWrongLife : HitVerdictPacket.ResultDamageLimit;
            int health = _victim.Health; ushort sequence = NetDamage.Sequence(1); uint started = _frame;
            Check(Receive(claim) == expected, $"serialized claim receives terminal refusal wrongLife={wrongLife}");
            byte[] canonical = Bytes(_lastAck);
            var decoded = CombatAckEntry.Read(canonical);
            Check(decoded.ClaimId == claim.ClaimId && decoded.ShotId == claim.ShotId
                && decoded.VictimSlot == claim.VictimSlot && decoded.VictimGeneration == claim.VictimGeneration
                && decoded.VictimLife == claim.VictimLifeId && decoded.Result == expected
                && decoded.DamageApplied == 0 && (decoded.Flags & CombatAckFlags.OutcomePresent) == 0,
                $"refusal wire entry preserves request identity without a paid outcome wrongLife={wrongLife}");
            for (int i = 0; i < 256; i++)
            {
                var churn = claim; churn.ClaimId = ++_claimId;
                Check(Receive(churn) == expected && _lastAck.ShotId == churn.ShotId
                    && _lastAck.ClaimId == churn.ClaimId, "distinct refusal remains correlated during terminal cache churn");
            }
            Check(_frame - started < NetHitClaims.DeliveryMaxAge
                && Receive(claim) == expected && Bytes(_lastAck).SequenceEqual(canonical)
                && _victim.Health == health && NetDamage.Sequence(1) == sequence,
                $"original refusal survives more than the old 128-receipt cache without damage wrongLife={wrongLife}");
        }
    }
    private static void TerminalReceiptCapacity()
    {
        NetShotProof.Mode = ShotProofMode.Shadow; Prepare(); var beam = Emit(BeamType.Missile);
        var original = Claim(beam, damage: ushort.MaxValue);
        Check(Receive(original) == HitVerdictPacket.ResultDamageLimit, "full-cache fixture starts with actual serialized terminal refusal");
        byte[] canonical = Bytes(_lastAck); int health = _victim.Health; ushort sequence = NetDamage.Sequence(1);
        T Table<T>(string name) => (T)typeof(NetHitClaims).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var ids = Table<ushort[,]>("_seenIds"); var keys = Table<ShotKey[,]>("_seenKeys");
        var payments = Table<ShotPaymentKey[,]>("_seenPaymentKeys"); var beams = Table<BeamType[,]>("_seenBeams");
        var outcomes = Table<CombatAckEntry[,]>("_seenOutcomes"); var results = Table<byte[,]>("_seenResults");
        var frames = Table<uint[,]>("_seenTerminalFrames"); var newest = Table<ushort[]>("_newestId");
        Check(original.ClaimId < ushort.MaxValue - NetHitClaims.SeenCapacity - 2, "full-cache constructed IDs do not wrap");
        for (int n = 0; n < NetHitClaims.SeenCapacity; n++)
        {
            ushort id = (ushort)(original.ClaimId + n); int at = id % NetHitClaims.SeenCapacity;
            ids[0, at] = id; keys[0, at] = NetShotProof.Key(0, original);
            payments[0, at] = NetShotProof.PaymentKey(0, original); beams[0, at] = (BeamType)original.Beam;
            outcomes[0, at] = _lastAck; outcomes[0, at].ClaimId = id;
            results[0, at] = HitVerdictPacket.ResultDamageLimit; frames[0, at] = _frame;
        }
        newest[0] = (ushort)(original.ClaimId + NetHitClaims.SeenCapacity - 1);
        var overflow = original; overflow.ClaimId = (ushort)(newest[0] + 1); uint fullFrame = _frame;
        Check(Receive(overflow) == HitVerdictPacket.ResultClaimCapacity
            && Receive(original) == HitVerdictPacket.ResultDamageLimit && Bytes(_lastAck).SequenceEqual(canonical),
            "explicit constructed 4096-terminal cache refuses overflow without corrupting original wire outcome");
        int originalAt = original.ClaimId % NetHitClaims.SeenCapacity;
        Check(ids[0, originalAt] == original.ClaimId && keys[0, originalAt] == NetShotProof.Key(0, original)
            && payments[0, originalAt] == NetShotProof.PaymentKey(0, original)
            && beams[0, originalAt] == (BeamType)original.Beam && Bytes(outcomes[0, originalAt]).SequenceEqual(canonical),
            "capacity refusal preserves original full cache identity and immutable outcome");
        Frame(fullFrame + (uint)NetHitClaims.DeliveryMaxAge);
        Check(Receive(original) == HitVerdictPacket.ResultDamageLimit && Bytes(_lastAck).SequenceEqual(canonical)
            && Receive(overflow) == HitVerdictPacket.ResultClaimCapacity,
            "terminal remains pinned through delivery deadline and overflow retry extends quiet window");
        Frame(_frame + (uint)NetHitClaims.DeliveryMaxAge + 1);
        var fresh = overflow; fresh.ClaimId++; fresh.Frame = fresh.AckFrame = fresh.LaunchFrame = _frame;
        Check(Receive(fresh) == HitVerdictPacket.ResultDamageLimit && _lastAck.ShotId == fresh.ShotId
            && _victim.Health == health && NetDamage.Sequence(1) == sequence,
            "after complete quiet window new claim reaches ordinary damage judgement without any payment");

        Prepare(); beam = Emit(BeamType.Missile); original = Claim(beam, damage: ushort.MaxValue);
        Check(Receive(original) == HitVerdictPacket.ResultDamageLimit, "expired-ID fixture establishes original terminal result");
        Frame(_frame + (uint)NetHitClaims.DeliveryMaxAge + 1);
        var reused = original; reused.ShotId++; reused.AckFrame = 0;
        long tooOld = NetHitClaims.TooOldHere;
        Check(Receive(reused) == HitVerdictPacket.ResultTooOld && _lastAck.ShotId == reused.ShotId
            && NetHitClaims.TooOldHere == tooOld + 1,
            "expired same ClaimId with distinct constructed ShotId reaches fresh Judge rather than old cached refusal");
    }
    private static void PosthumousFlight()
    {
        Prepare(); var beam = Emit(BeamType.Missile);
        _shooter.TakeDamage(2000, DamageFlags.NoDmgInvuln, null, _victim);
        Check(_shooter.Health == 0, "native damage kills projectile owner before impact");
        Collide(beam);
        Check(NetPlayerLifecycle.CurrentProjectile(beam), "posthumous flight retains valid launch life");
        _shooter.Spawn(_shooter.Position, Vector3.UnitZ, Vector3.UnitY, _shooter.NodeRef, respawn: true);
    }
    private static void NativeSameFrameTrade()
    {
        Prepare(); var originalShooter = _shooter; var originalVictim = _victim; var direction = _direction;
        var first = Emit(BeamType.Missile); uint launch = first.ModLaunchFrame;
        (_shooter, _victim) = (_victim, _shooter); _direction = -direction;
        NetShotProof.Seed(_shooter, Source());
        var second = Emit(BeamType.Missile);
        Check(second.ModLaunchFrame == launch, "both trade projectiles launch in same authority world");
        _shooter.Health = _victim.Health = 1;
        try
        {
            Collide(second);
            Check(_victim.Health == 0, "first native lethal trade impact kills one shooter");
            (_shooter, _victim) = (_victim, _shooter); _direction = direction;
            Collide(first);
            Check(_victim.Health == 0 && _shooter.Health == 0,
                "same-world native projectiles preserve lethal trade after owner death");
        }
        finally { _shooter = originalShooter; _victim = originalVictim; _direction = direction; }
    }
    public static int Run(string profile, string room)
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

            RescueFirst(); RicochetAlternateDamage(); DerivedCatchUpPool(); PendingSeenCollision(); PhysicalOnce(); ImperialistCorrectionOnce(); TerminalRetries(); TerminalRefusalCacheChurn(); TerminalReceiptCapacity(); PosthumousFlight(); NativeSameFrameTrade();
            long lifetimePayments = NetCombatPayments.LifetimePayments;
            long lifetimeSuppressed = NetCombatPayments.LifetimeSuppressed;
            Check(lifetimePayments > 0 && lifetimeSuppressed > 0, "process evidence captures actual payments and suppressed repeats");
            NetHitClaims.Reset();
            Check(NetCombatPayments.LifetimePayments == lifetimePayments && NetCombatPayments.LifetimeSuppressed == lifetimeSuppressed
                && NetCombatPayments.Payments == 0 && NetCombatPayments.Suppressed == 0,
                "room teardown clears live ledger without erasing process payment evidence");
            Check(sim.StepFailures == 0, "no simulation failures");
            Console.WriteLine($"PASS: {_checks} combat arbitration asset assertions ({profile})"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { sim.Stop(); NetSession.Stop(); NetFireEvents.AcceptedPlaybackEnabled = accepted; NetShotProof.Mode = mode; }
    }
}
