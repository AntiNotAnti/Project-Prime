using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Asset-backed unpaid rescue fixture. Source carriers, body placement and
// withholding the actual authority beam's Process are declared fixture inputs.
// Claims enter the real serialized receiver/geometry/grace/payment route.
// No direct grant, damage, physical-payment or outcome injection supplies a positive.
internal static class TimedShotRescueTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException("Timed rescue: " + label); }
    private static void Frame(uint value) => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, value);
    private readonly record struct Shot(FireEvent Fire, BeamProjectileEntity Beam, TimedShotCertificate Certificate);

    private sealed class Fixture : IDisposable
    {
        private readonly ServerSim _sim = new();
        internal readonly Scene Scene;
        internal readonly PlayerEntity Owner, Victim;
        internal readonly Vector3 Origin, Direction;
        private uint _shot;
        private ushort _claim;
        internal Fixture(string room)
        {
            NetShotProof.Mode = ShotProofMode.Shadow;
            NetTimedShotProof.Mode = TimedGrantProofMode.Enforce;
            NetHitClaims.Enabled = true; NetFireEvents.AcceptedPlaybackEnabled = true; NetTestScript.Enabled = false;
            Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1;
            roster.Revision = 1; roster.Count = 2;
            for (byte slot = 0; slot < 2; slot++)
            { roster.Slots[slot] = slot; roster.Generations[slot] = 1; roster.Hunters[slot] = (byte)Hunter.Samus; roster.Names[slot] = "Unpaid rescue " + slot; }
            try
            {
                Check(_sim.Start(room, GameMode.Battle, 2, _ => { }, () => { }, roster: roster), "actual native authority starts");
                NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                    RoomKey = room, Mode = (byte)GameMode.Battle }, false);
                NetSession.ApplyRoster(roster);
                Scene = (Scene)typeof(ServerSim).GetField("_scene", Hidden)!.GetValue(_sim)!;
                Scene.GameState.MatchTime = -1; Scene.GameState.PointGoal = 10000;
                Scene.GameState.EnhancedHunters = false; Scene.GameState.BalancedMode = false; Scene.GameState.PrimeHunter = -1;
                foreach (var item in Scene.GetItemInstanceEntities()) item.DespawnTimer = 0;
                for (int i = 0; i < 120; i++) _sim.Step();
                Owner = Scene.Players.Items[0]; Victim = Scene.Players.Items[1]; Origin = Owner.Position;
                Check(Owner.ModIsInPlay && Victim.ModIsInPlay && !Owner.IsBot && !Victim.IsBot
                    && NetSession.IsAuthority && NetSession.LocalSlot == -1 && !Scene.Services.IsReplica,
                    "native lifecycle supplies genuine remote authority lives");
                foreach (var player in new[] { Owner, Victim })
                {
                    player.Health = 999; player.ModForceForm(false); player.Controls.ClearAll(); player.ModReleaseSpawnProtection();
                    typeof(PlayerEntity).GetField("_spawnInvulnTimer", Hidden)!.SetValue(player, (ushort)0);
                    typeof(PlayerEntity).GetField("_damageInvulnTimer", Hidden)!.SetValue(player, (ushort)0);
                }
                Frame(1000); Record();
                foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
                    if (NetDynamicGeometryHistory.TryTraceDistanceAtFrame(Scene, Origin + Vector3.UnitY * .5f,
                        Origin + direction * 4 + Vector3.UnitY * .5f, NetSession.NetFrame, out float wall) && wall == 1)
                    { Direction = direction; break; }
                Check(Direction.LengthSquared == 1, "real room supplies unobstructed body claim lane");
                Victim.ModPlaceAt(Origin + Direction * 3); Record();
                Submit(0, Packet(0, NetSession.NetFrame)); Submit(1, Packet(1, NetSession.NetFrame));
                Check(NetFireEvents.UsesEvents(Owner) && !NetFireEvents.CanFire(Owner),
                    "admitted remote event stream prevents native held-input fallback");
                Step();
                Check(NetPowerUpLedger.Ledger is { } observedLedger
                    && observedLedger.TryReadObservation(ShotKey.For(0, 0), NetSession.NetFrame,
                        TimedPowerUp.DoubleDamage, out _, out _),
                    "normal full input step establishes actual A1 observation owner before later missing-frame negative");
            }
            catch { _sim.Stop(); NetSession.Stop(); throw; }
        }
        internal void Record()
        { NetUnlagged.Record(NetSession.NetFrame); NetDynamicGeometryHistory.RecordWorld(NetSession.NetFrame); }
        private IntentPacket Packet(int slot, uint source, bool down = false, FireEvent? fire = null)
        {
            var life = ShotKey.For(slot, 0);
            var packet = new IntentPacket { MatchId = life.MatchId, AuthorityEpoch = life.AuthorityEpoch,
                SlotGeneration = life.Generation, LifeId = life.LifeId, Frame = source, AckFrame = source,
                Position = slot == 0 ? Origin : Origin + Direction * 3, Aim = Direction,
                WeaponSelect = (byte)BeamType.PowerBeam, HasFireEvents = true,
                Buttons = IntentButtons.InPlayState | (down ? IntentButtons.Shoot : 0), ShotInputCount = 1 };
            packet.ShotInputs[0] = ShotInputHistory.Sample((byte)BeamType.PowerBeam, down, false, true);
            if (fire is { } value)
            {
                packet.FireEventCount = 1; packet.FireEvents[0] = value; packet.ShotInputCount = 2;
                packet.ShotInputs[1] = ShotInputHistory.Sample((byte)BeamType.PowerBeam, false, false, true);
            }
            return packet;
        }
        private void Submit(int slot, in IntentPacket packet)
            => Check(NetSession.AcceptSlotIntent(slot, packet), "real lifecycle/event carrier intake accepts fixture input");
        internal void Step()
        {
            Frame(unchecked(NetSession.NetFrame + 1));
            Submit(0, Packet(0, NetSession.NetFrame)); Submit(1, Packet(1, NetSession.NetFrame));
            long failures = _sim.StepFailures; _sim.Step();
            Check(_sim.StepFailures == failures && Owner.ModIsInPlay && Victim.ModIsInPlay,
                "actual timer/input/native world step has no simulation failure");
        }
        internal TimedGrantRecord Grant()
        {
            var item = ItemSpawnEntity.SpawnItem(ItemType.DoubleDamage, Owner.Position, Owner.NodeRef, 6000, Scene)
                ?? throw new InvalidOperationException("Native item factory refused Double.");
            Check(NetPowerUpLedger.TrySpawnKey(item, out var spawn), "genuine native Double incarnation registers");
            Step();
            var grant = NetPowerUpLedger.Ledger!.History.Single(g => g.Spawn == spawn);
            Check(item.DespawnTimer == 0 && Owner.DoubleDamage && grant.Life == ShotKey.For(0, 0)
                && grant.ContactFrame == NetSession.NetFrame && grant.SourceContactFrame == NetSession.NetFrame,
                "actual full native contact grants retained source ownership");
            return grant;
        }
        internal Shot Emit(bool hint, bool applySourceInput = true)
        {
            Frame(unchecked(NetSession.NetFrame + 1)); Record();
            var fire = new FireEvent(++_shot, NetSession.NetFrame, NetSession.NetFrame, 0,
                FireEventKind.PressFire, (byte)BeamType.PowerBeam, 0,
                hint ? FireEvent.DoubleStateBit : 0, FireEvent.FlagPose,
                Origin + Vector3.UnitY * .5f, Direction, Direction, Direction,
                SourceLife: NetPlayerLifecycle.Get(0));
            var key = ShotKey.For(0, fire.ShotId);
            int health = Victim.Health;
            Submit(0, Packet(0, NetSession.NetFrame, true, fire));
            Check(NetFireEvents.CaptureLedger(0).TryGet(key, out var admitted, out var before, out _)
                && before == ShotDisposition.Pending && admitted.Fire == fire, "actual private lane owns pending immutable event");
            // Missing-observation arm deliberately omits normal source input/
            // pose application, but still drains the genuine Pending owner.
            if (applySourceInput) NetHooks.TryApplyRemoteInput(Owner, 0);
            else NetFireEvents.DrainAccepted(Owner);
            Check(NetFireEvents.CaptureLedger(0).TryGet(key, out admitted, out var disposition, out _)
                && disposition == ShotDisposition.Fired && admitted.Fire == fire,
                "private admitted discrete drain completes actual native emission");
            var beam = Owner.EquipInfo.Beams.Single(b => b.ModLaunchKey == key && b.ModComponentId != 0);
            Check(NetTimedShotProof.TryGet(key, fire.SourceFrame, out var certificate)
                && certificate.Fire == fire && certificate.Components == 1 && certificate.Damage.Present,
                "actual successful native emission retains immutable timed damage template");
            // Controlled absence of authority collision: remove from traversal,
            // keep its genuine source/component identity and never call Process.
            Scene.RemoveEntity(beam);
            var probe = Claim(new(fire, beam, certificate), 1);
            Check(Victim.Health == health && !NetCombatPayments.TryGet(NetShotProof.PaymentKey(0, probe),
                probe.Beam, out _), "claim begins genuinely unpaid; no native collision or payment injected");
            return new(fire, beam, certificate);
        }
        internal HitClaimPacket Claim(Shot shot, ushort damage, bool head = false)
            => new() { MatchId = shot.Certificate.Key.MatchId, AuthorityEpoch = shot.Certificate.Key.AuthorityEpoch,
                ShooterGeneration = shot.Certificate.Key.Generation, ShooterLifeId = shot.Certificate.Key.LifeId,
                VictimSlot = 1, VictimGeneration = NetPlayerLifecycle.Generation(1), VictimLifeId = NetPlayerLifecycle.Get(1),
                ClaimId = ++_claim, ShotId = shot.Fire.ShotId, ComponentId = shot.Beam.ModComponentId,
                Frame = NetSession.NetFrame, AckFrame = NetSession.NetFrame, LaunchFrame = shot.Fire.AckFrame,
                Beam = (byte)BeamType.PowerBeam, Flags = (byte)(HitClaimPacket.FlagDirect | (head ? HitClaimPacket.FlagHeadshot : 0)),
                Damage = damage, HitPoint = Victim.Position, Direction = Vector3.Zero };

        internal CombatAckEntry Receive(HitClaimPacket claim, bool realTimerSteps = false, bool evictProfile = false)
        {
            CombatAckEntry outcome = default; bool found = false; var previous = NetHitClaims.CombatAckSink;
            NetHitClaims.CombatAckSink = (slot, entries) => {
                foreach (var entry in entries) if (slot == 0 && entry.ClaimId == claim.ClaimId) { outcome = entry; found = true; }
            };
            try
            {
                var bytes = new byte[1 + HitClaimPacket.Size]; bytes[0] = 1; claim.Write(bytes.AsSpan(1));
                NetHitClaims.Receive(0, bytes);
                if (evictProfile)
                {
                    Check(!found && NetTimedDamageProfile.TryGet(1, claim.VictimGeneration, claim.VictimLifeId,
                        claim.AckFrame, out _), "unpaid admission retains actual victim profile before grace");
                    // Explicit admission-copy falsifier: withhold Tick while
                    // recording real stationary fixture bodies past128 cells.
                    for (int i = 0; i <= NetTimedDamageProfile.HistoryFrames; i++)
                    { Frame(unchecked(NetSession.NetFrame + 1)); Record(); }
                    Check(!NetTimedDamageProfile.TryGet(1, claim.VictimGeneration, claim.VictimLifeId,
                        claim.AckFrame, out _), "live profile ring has genuinely evicted the admitted frame");
                }
                for (int i = 0; i <= NetHitClaims.MaxGraceFrames * 2 + 2 && !found; i++)
                {
                    if (realTimerSteps) Step();
                    else { if (!evictProfile) Frame(unchecked(NetSession.NetFrame + 1)); NetHitClaims.Tick(); }
                }
                Check(found && outcome.ClaimId == claim.ClaimId && outcome.ShotId == claim.ShotId,
                    "actual serialized claim receives finite production terminal outcome");
                return outcome;
            }
            finally { NetHitClaims.CombatAckSink = previous; }
        }
        internal CombatAckEntry Pay(Shot shot, ushort claimed, int expected, bool head = false,
            bool realTimerSteps = false, bool evictProfile = false)
        {
            var claim = Claim(shot, claimed, head); int health = Victim.Health;
            ushort sequence = NetDamage.Sequence(1); long payments = NetCombatPayments.Payments;
            Check(!NetCombatPayments.TryGet(NetShotProof.PaymentKey(0, claim), claim.Beam, out _), "positive rescue starts without an existing payment");
            var ack = Receive(claim, realTimerSteps, evictProfile);
            Check(ack.Result == HitVerdictPacket.ResultApplied && ack.DamageApplied == expected
                && health - Victim.Health == expected && ack.HealthAfter == Victim.Health
                && ack.DamageSequence == NetDamage.Sequence(1) && NetDamage.Sequence(1) != sequence
                && NetCombatPayments.Payments == payments + 1,
                "unpaid actual rescue pays exact once-only " + expected + "; ack=" + JsonSerializer.Serialize(ack));
            if (head) Check((ack.Flags & CombatAckFlags.Headshot) == 0, "unverified PowerBeam head hint cannot grant head/armour channel");
            Check(NetCombatPayments.TryGet(NetShotProof.PaymentKey(0, claim), claim.Beam, out var paid)
                && paid.DamageApplied == ack.DamageApplied && paid.HealthAfter == ack.HealthAfter
                && paid.DamageSequence == ack.DamageSequence, "actual rescue commits exact component payment outcome");
            var speed = Victim.Speed; var effects = (Victim.ModFrozen, Victim.ModBurning, Victim.ModDisrupted);
            health = Victim.Health; sequence = NetDamage.Sequence(1); payments = NetCombatPayments.Payments;
            for (int retry = 0; retry < 2; retry++)
            {
                if (retry == 1) claim.ClaimId = ++_claim;
                claim.Damage = ushort.MaxValue;
                var duplicate = Receive(claim);
                Check(duplicate.Result == HitVerdictPacket.ResultDuplicate && duplicate.DamageApplied == ack.DamageApplied
                    && duplicate.HealthAfter == ack.HealthAfter && duplicate.DamageSequence == ack.DamageSequence
                    && duplicate.VictimSlot == ack.VictimSlot && duplicate.VictimGeneration == ack.VictimGeneration
                    && duplicate.VictimLife == ack.VictimLife && duplicate.Flags == ack.Flags,
                    "same/new-ID oversize retry receives original canonical rescued outcome");
                Check(Victim.Health == health && NetDamage.Sequence(1) == sequence && NetCombatPayments.Payments == payments
                    && Victim.Speed == speed && (Victim.ModFrozen, Victim.ModBurning, Victim.ModDisrupted) == effects,
                    "rescued duplicate never repeats health/sequence/effects/impulse");
            }
            return ack;
        }
        public void Dispose() { _sim.Stop(); NetSession.Stop(); }
    }

    private static void Ordinary(string room, bool grant, bool hint, bool missingSource = false, bool head = false)
    {
        using var fixture = new Fixture(room);
        var funding = grant ? fixture.Grant() : null;
        var shot = fixture.Emit(hint, applySourceInput: !missingSource);
        int expected = grant ? 12 : 6;
        Check(shot.Certificate.Damage.Body == expected && shot.Beam.Damage == expected,
            "actual native ordinary body template is exact " + expected);
        if (missingSource) Check(shot.Certificate.Knowledge == TimedGrantKnowledge.Unavailable
            && !shot.Certificate.TrustedSource && !shot.Certificate.AuthorizedDouble,
            "actual missing source/ownership observation remains explicit while ordinary body survives");
        else Check(shot.Certificate.AuthorizedDouble == grant && (!grant || shot.Certificate.Funding?.Sequence == funding!.Sequence),
            "real source certificate binds actual funding independently of the typed Double hint");
        var paid = fixture.Pay(shot, 40, expected, head);
        Console.WriteLine("RESCUE ARM " + JsonSerializer.Serialize(new { grant, hint, missingSource, head,
            source = shot.Fire.SourceFrame, shot.Certificate.Knowledge, shot.Certificate.Unavailable, expected,
            paid.DamageApplied, paid.HealthAfter, paid.DamageSequence, paid.Flags }));
    }
    private static void OversizeRejected(string room)
    {
        using var fixture = new Fixture(room); var shot = fixture.Emit(true);
        var claim = fixture.Claim(shot, ushort.MaxValue); int health = fixture.Victim.Health;
        var ack = fixture.Receive(claim);
        Check(ack.Result == HitVerdictPacket.ResultDamageLimit && fixture.Victim.Health == health
            && !NetCombatPayments.TryGet(NetShotProof.PaymentKey(0, claim), claim.Beam, out _),
            "first claim beyond legacy geometry weapon ceiling is refused without fake payment");
        fixture.Pay(shot, 40, 6);
    }
    private static void ExpiredFunding(string room)
    {
        using var fixture = new Fixture(room); var grant = fixture.Grant();
        while (NetSession.NetFrame < unchecked(grant.ExpiresAtFrame - 20)) fixture.Step();
        var shot = fixture.Emit(false);
        Check(shot.Certificate.AuthorizedDouble && shot.Certificate.Funding?.Sequence == grant.Sequence,
            "late actual launch retains genuine unexpired native funding");
        fixture.Pay(shot, 40, 12, realTimerSteps: true);
        Check(grant.EndReason == TimedGrantEnd.Expired && !fixture.Owner.DoubleDamage,
            "real timer expired before rescued payment and canonical duplicates completed");
        Check(NetTimedShotProof.TryGet(shot.Certificate.Key, shot.Fire.SourceFrame, out var retained)
            && retained == shot.Certificate, "expiry cannot rewrite paid launch template/source certificate");
    }
    private static void CopiedProfile(string room)
    {
        using var fixture = new Fixture(room); var shot = fixture.Emit(false);
        fixture.Pay(shot, 40, 6, evictProfile: true);
    }
    internal static int Run(string room)
    {
        _checks = 0; var oldProof = NetShotProof.Mode; var timed = NetTimedShotProof.Mode;
        bool script = NetTestScript.Enabled, claims = NetHitClaims.Enabled, playback = NetFireEvents.AcceptedPlaybackEnabled;
        try
        {
            Check(ServerSim.Available(out string reason), reason);
            Ordinary(room, false, false);
            Ordinary(room, false, true);
            Ordinary(room, true, true);
            Ordinary(room, true, false);
            Ordinary(room, false, true, missingSource: true);
            Ordinary(room, false, true, head: true);
            OversizeRejected(room); ExpiredFunding(room); CopiedProfile(room);
            Console.WriteLine($"PASS: {_checks} actual unpaid rescued timed-shot assertions.");
            Console.WriteLine("SCOPE: native emitted ordinary template, actual admitted carrier/private drain and serialized geometry/grace/payment. Explicit body placement, withheld flight and profile-eviction timing fixture. NOT_RUN: authenticated UDP, natural movement/AI, GL/audio, special families, confirmed head geometry.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("RESCUE FAIL " + error); Console.Error.WriteLine("Completed checks: " + _checks); return 1; }
        finally { NetSession.Stop(); NetShotProof.Mode = oldProof; NetTimedShotProof.Mode = timed;
            NetTestScript.Enabled = script; NetHitClaims.Enabled = claims; NetFireEvents.AcceptedPlaybackEnabled = playback; }
    }
}
