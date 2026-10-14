using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Controlled asset-backed remote fixture, actual lifecycle intake/private drain/
// native collision/payment. Positive grants use full native item contact steps,
// never direct TryGrant/Physical/TakeDamage or fabricated payment outcomes.
// This does not claim authenticated UDP, natural input, GL/audio or replay parity.
internal static class TimedShotModifierTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Timed modifier: " + label);
        _checks++; Console.WriteLine("MODIFIER PASS " + label);
    }
    private static void Frame(uint value)
        => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, value);
    private readonly record struct Hit(float Body, float Head, float Splash, int Debit, CombatAckEntry Paid);

    private sealed class Fixture : IDisposable
    {
        private readonly ServerSim _sim = new();
        internal readonly Scene Scene;
        internal readonly PlayerEntity Owner, Victim;
        internal readonly Vector3 Origin, Direction;
        private uint _shot;
        private ushort _claim;
        internal Fixture(string room, TimedGrantProofMode mode)
        {
            NetShotProof.Mode = ShotProofMode.Shadow; NetTimedShotProof.Mode = mode;
            NetFireEvents.AcceptedPlaybackEnabled = true; NetTestScript.Enabled = false;
            Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
            var roster = RosterPacket.Create();
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte i = 0; i < 2; i++)
            { roster.Slots[i] = i; roster.Generations[i] = 1; roster.Hunters[i] = (byte)Hunter.Samus; roster.Names[i] = "Modifier " + i; }
            try
            {
                Check(_sim.Start(room, GameMode.Battle, 2, _ => { }, () => { }, roster: roster), "real asset authority starts");
                NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1, RoomKey = room, Mode = (byte)GameMode.Battle }, false);
                NetSession.ApplyRoster(roster);
                Scene = (Scene)typeof(ServerSim).GetField("_scene", Hidden)!.GetValue(_sim)!;
                Scene.GameState.MatchTime = -1; Scene.GameState.PointGoal = 10000;
                Scene.GameState.EnhancedHunters = false; Scene.GameState.BalancedMode = false; Scene.GameState.PrimeHunter = -1;
                foreach (var item in Scene.GetItemInstanceEntities()) item.DespawnTimer = 0;
                for (int i = 0; i < 120; i++) _sim.Step();
                Owner = Scene.Players.Items[0]; Victim = Scene.Players.Items[1]; Origin = Owner.Position;
                Check(Owner.ModIsInPlay && Victim.ModIsInPlay && !Owner.IsBot && !Victim.IsBot
                    && NetSession.IsAuthority && NetSession.LocalSlot == -1 && !Scene.Services.IsReplica,
                    "actual remote authority bodies have genuine lives");
                Frame(1000);
                foreach (var player in new[] { Owner, Victim })
                {
                    player.Health = 999; player.Controls.ClearAll(); player.ModForceForm(false); player.ModReleaseSpawnProtection();
                    typeof(PlayerEntity).GetField("_spawnInvulnTimer", Hidden)!.SetValue(player, (ushort)0);
                    typeof(PlayerEntity).GetField("_damageInvulnTimer", Hidden)!.SetValue(player, (ushort)0);
                }
                Record();
                foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
                    if (NetDynamicGeometryHistory.TryTraceDistanceAtFrame(Scene, Origin + Vector3.UnitY * .5f,
                        Origin + direction * 4 + Vector3.UnitY * .5f, NetSession.NetFrame, out float wall) && wall == 1)
                    { Direction = direction; break; }
                Check(Direction.LengthSquared == 1, "real world collision lane is clear");
                Victim.ModPlaceAt(Origin + Direction * 3); Record();
                Submit(0, Packet(0, NetSession.NetFrame)); Submit(1, Packet(1, NetSession.NetFrame));
                Check(NetFireEvents.UsesEvents(Owner) && !NetFireEvents.CanFire(Owner),
                    "actual admitted event-stream carrier excludes native control fallback without a discrete event");
            }
            catch { _sim.Stop(); NetSession.Stop(); throw; }
        }
        private void Record()
        { NetUnlagged.Record(NetSession.NetFrame); NetDynamicGeometryHistory.RecordWorld(NetSession.NetFrame); }
        private IntentPacket Packet(int slot, uint source, bool down = false, FireEvent? fire = null)
        {
            var life = ShotKey.For(slot, 0);
            var packet = new IntentPacket { MatchId = life.MatchId, AuthorityEpoch = life.AuthorityEpoch,
                SlotGeneration = life.Generation, LifeId = life.LifeId, Frame = source, AckFrame = source,
                Position = slot == 0 ? Origin : Origin + Direction * 3, Aim = Direction,
                WeaponSelect = (byte)BeamType.PowerBeam, Buttons = IntentButtons.InPlayState,
                HasFireEvents = true, ShotInputCount = 1 };
            packet.ShotInputs[0] = ShotInputHistory.Sample((byte)BeamType.PowerBeam, down, false, true);
            if (down) packet.Buttons |= IntentButtons.Shoot;
            if (fire is { } value) { packet.FireEventCount = 1; packet.FireEvents[0] = value; }
            return packet;
        }
        private void Submit(int slot, in IntentPacket packet)
            => Check(NetSession.AcceptSlotIntent(slot, packet), "actual lifecycle/event carrier intake");
        internal void Step(bool down = false)
        {
            Frame(unchecked(NetSession.NetFrame + 1));
            Submit(0, Packet(0, NetSession.NetFrame, down)); Submit(1, Packet(1, NetSession.NetFrame));
            long before = _sim.StepFailures; _sim.Step();
            Check(_sim.StepFailures == before && Owner.ModIsInPlay && Victim.ModIsInPlay,
                "complete native step retains both live bodies without simulation failure");
        }
        internal TimedGrantRecord NativeGrant()
        {
            var item = ItemSpawnEntity.SpawnItem(ItemType.DoubleDamage, Owner.Position, Owner.NodeRef, 6000, Scene)
                ?? throw new InvalidOperationException("Native Double factory refused item.");
            Check(NetPowerUpLedger.TrySpawnKey(item, out var spawn), "actual native item incarnation registers");
            Step();
            var ledger = NetPowerUpLedger.Ledger!;
            var grant = ledger.History.Single(g => g.Spawn == spawn);
            Check(item.DespawnTimer == 0 && Owner.DoubleDamage && grant.Life == ShotKey.For(0, 0)
                && grant.ContactFrame == NetSession.NetFrame && grant.SourceContactFrame == NetSession.NetFrame,
                "complete native item contact produces genuine source/authority reservation");
            Check(ledger.TryReadObservation(grant.Life, grant.ContactFrame, TimedPowerUp.DoubleDamage,
                out var answer, out var observed) && observed == grant.ContactFrame
                && answer.Knowledge == TimedGrantKnowledge.NotGranted,
                "real contact input remains immutable NotGranted before C+1");
            return grant;
        }
        internal FireEvent Author(uint source, bool hint)
            => new(++_shot, source, source, 0, FireEventKind.PressFire, (byte)BeamType.PowerBeam, 0,
                hint ? FireEvent.DoubleStateBit : 0, FireEvent.FlagPose, Origin + Vector3.UnitY * .5f,
                Direction, Direction, Direction, SourceLife: NetPlayerLifecycle.Get(0));
        internal (FireEvent Fire, BeamProjectileEntity Beam, int HealthBefore) Emit(bool hint, uint? heldSource = null)
        {
            uint source = heldSource ?? unchecked(NetSession.NetFrame + 1);
            var fire = Author(source, hint); var key = ShotKey.For(0, fire.ShotId);
            Frame(heldSource.HasValue ? NetSession.NetFrame : source); Record();
            int health = Victim.Health;
            Submit(0, Packet(0, NetSession.NetFrame, !heldSource.HasValue, fire));
            // Current input/pose adapter supplies normal source truth. A delayed
            // event instead uses the earlier full-step observation and pose.
            NetHooks.TryApplyRemoteInput(Owner, 0);
            var ledger = NetFireEvents.CaptureLedger(0);
            Check(ledger.TryGet(key, out var admitted, out var disposition, out _)
                && admitted.Fire == fire && disposition == ShotDisposition.Fired,
                "private accepted drain emits original immutable authored event once");
            var beam = Owner.EquipInfo.Beams.Single(b => b.ModLaunchKey == key && b.ModComponentId != 0);
            Check(beam.Damage > 0, "real native terminal has positive body damage");
            return (fire, beam, health);
        }
        internal Hit Collide(BeamProjectileEntity beam, int healthBefore)
        {
            for (int i = 0; i < 60 && Victim.Health == healthBefore && beam.Lifespan > 0; i++) beam.Process();
            Check(Victim.Health < healthBefore, "actual native projectile collision debits victim health");
            var claim = Claim(beam, 1);
            Check(NetCombatPayments.TryGet(NetShotProof.PaymentKey(0, claim), (byte)beam.Beam, out var paid)
                && paid.DamageApplied == healthBefore - Victim.Health && paid.HealthAfter == Victim.Health
                && paid.DamageSequence == NetDamage.Sequence(1), "real native payment captures exact health/sequence/debit");
            return new(beam.Damage, beam.HeadshotDamage, beam.SplashDamage, healthBefore - Victim.Health, paid);
        }
        private HitClaimPacket Claim(BeamProjectileEntity beam, ushort damage)
        {
            PlayerState state = default; NetDamage.Write(1, ref state);
            return new HitClaimPacket { MatchId = beam.ModLaunchKey.MatchId, AuthorityEpoch = beam.ModLaunchKey.AuthorityEpoch,
                ShooterGeneration = beam.ModLaunchKey.Generation, ShooterLifeId = beam.ModLaunchKey.LifeId,
                VictimSlot = 1, VictimGeneration = NetPlayerLifecycle.Generation(1), VictimLifeId = NetPlayerLifecycle.Get(1),
                ClaimId = ++_claim, ShotId = beam.ModShotId, ComponentId = beam.ModComponentId,
                Frame = NetSession.NetFrame, AckFrame = NetSession.NetFrame, LaunchFrame = beam.ModLaunchFrame,
                Beam = (byte)beam.Beam, Flags = HitClaimPacket.FlagDirect, Damage = damage,
                HitPoint = Victim.Position, Direction = state.HitDirection };
        }
        internal void OversizeRetries(BeamProjectileEntity beam, Hit native)
        {
            var claim = Claim(beam, ushort.MaxValue); int health = Victim.Health;
            ushort sequence = NetDamage.Sequence(1); long paid = NetCombatPayments.Payments;
            var speed = Victim.Speed; var effects = (Victim.ModFrozen, Victim.ModBurning, Victim.ModDisrupted);
            for (int i = 0; i < 3; i++)
            {
                if (i == 2) claim.ClaimId = ++_claim;
                CombatAckEntry ack = default; bool found = false; var old = NetHitClaims.CombatAckSink;
                NetHitClaims.CombatAckSink = (slot, entries) =>
                { foreach (var entry in entries) if (slot == 0 && entry.ClaimId == claim.ClaimId) { ack = entry; found = true; } };
                try
                {
                    byte[] bytes = new byte[1 + HitClaimPacket.Size]; bytes[0] = 1; claim.Write(bytes.AsSpan(1));
                    NetHitClaims.Receive(0, bytes);
                    for (int step = 0; step <= NetHitClaims.MaxGraceFrames * 2 + 2 && !found; step++) NetHitClaims.Tick();
                }
                finally { NetHitClaims.CombatAckSink = old; }
                Check(found && ack.Result == HitVerdictPacket.ResultDuplicate && ack.ClaimId == claim.ClaimId
                    && ack.ShotId == claim.ShotId && ack.DamageApplied == native.Paid.DamageApplied
                    && ack.HealthAfter == native.Paid.HealthAfter && ack.DamageSequence == native.Paid.DamageSequence
                    && ack.VictimSlot == native.Paid.VictimSlot && ack.VictimGeneration == native.Paid.VictimGeneration
                    && ack.VictimLife == native.Paid.VictimLife && ack.Flags == native.Paid.Flags,
                    "oversize same/new-ID claim receives canonical once-only native outcome");
                Check(Victim.Health == health && NetDamage.Sequence(1) == sequence && NetCombatPayments.Payments == paid
                    && Victim.Speed == speed && (Victim.ModFrozen, Victim.ModBurning, Victim.ModDisrupted) == effects,
                    "oversize retry cannot alter actual payment/health/sequence/effects/impulse");
            }
        }
        public void Dispose() { _sim.Stop(); NetSession.Stop(); }
    }

    private static Hit Arm(string room, TimedGrantProofMode mode, bool nativeGrant, bool hint, bool delayed = false)
    {
        using var fixture = new Fixture(room, mode);
        TimedGrantRecord? grant = nativeGrant ? fixture.NativeGrant() : null;
        uint? source = null;
        if (delayed)
        {
            if (grant == null) throw new InvalidOperationException("Delayed positive requires genuine native grant.");
            while (NetSession.NetFrame != unchecked(grant.ExpiresAtFrame - 2)) fixture.Step();
            fixture.Step(down: true); source = NetSession.NetFrame;
            Check(!fixture.Owner.EquipInfo.Beams.Any(b => b.Lifespan > 0),
                "withheld discrete event cannot use remote held-input fallback to emit a projectile");
            fixture.Step(); fixture.Step();
            Check(grant.EndReason == TimedGrantEnd.Expired && !fixture.Owner.DoubleDamage,
                "actual native timer expires before delayed accepted delivery");
        }
        var emitted = fixture.Emit(hint, source);
        if (mode == TimedGrantProofMode.Enforce)
        {
            Check(NetTimedShotProof.TryGet(emitted.Beam.ModLaunchKey, emitted.Fire.SourceFrame, out var certificate)
                && certificate.Fire == emitted.Fire && certificate.AuthorizedDouble == nativeGrant
                && (!nativeGrant || certificate.Funding?.Sequence == grant!.Sequence),
                "original immutable source event retains exact genuine grant certificate despite modifier resolution");
        }
        var hit = fixture.Collide(emitted.Beam, emitted.HealthBefore);
        fixture.OversizeRetries(emitted.Beam, hit);
        Console.WriteLine("MODIFIER ARM " + JsonSerializer.Serialize(new { mode = mode.ToString(), nativeGrant, hint, delayed,
            sourceFrame = emitted.Fire.SourceFrame, authorityFrame = NetSession.NetFrame,
            hintInOriginalFire = emitted.Fire.DoubleAtFire, hit.Body, hit.Head, hit.Splash, hit.Debit, paidDamage = hit.Paid.DamageApplied }));
        return hit;
    }
    internal static int Run(string room)
    {
        var oldProof = NetShotProof.Mode; var mode = NetTimedShotProof.Mode;
        bool playback = NetFireEvents.AcceptedPlaybackEnabled, script = NetTestScript.Enabled; _checks = 0;
        try
        {
            Check(ServerSim.Available(out string reason), reason);
            var ordinary = Arm(room, TimedGrantProofMode.Enforce, false, false);
            var forged = Arm(room, TimedGrantProofMode.Enforce, false, true);
            Check(forged.Body == ordinary.Body && forged.Head == ordinary.Head && forged.Splash == ordinary.Splash
                && forged.Debit == ordinary.Debit, "forged Double flag cannot inflate first real native projectile or victim payment");
            var funded = Arm(room, TimedGrantProofMode.Enforce, true, false);
            Check(funded.Body == ordinary.Body * 2 && funded.Head == ordinary.Head * 2 && funded.Splash == ordinary.Splash * 2
                && funded.Debit == ordinary.Debit * 2, "genuine A1 funding supplies native Double damage despite false hint");
            var delayed = Arm(room, TimedGrantProofMode.Enforce, true, true, delayed: true);
            Check(delayed.Body == funded.Body && delayed.Head == funded.Head && delayed.Splash == funded.Splash
                && delayed.Debit == funded.Debit, "genuine pre-expiry source funding survives actual expired-owner delayed delivery");
            Console.WriteLine($"PASS: {_checks} controlled native modifier/payment assertions.");
            Console.WriteLine("NOT_RUN: authenticated UDP, natural source input, unpaid rescue cap, GL/audio/private replay parity. Positive ownership uses actual native item contact; health/placement and remote carrier chronology are declared fixture inputs.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("MODIFIER FAIL " + error); return 1; }
        finally { NetSession.Stop(); NetShotProof.Mode = oldProof; NetTimedShotProof.Mode = mode;
            NetFireEvents.AcceptedPlaybackEnabled = playback; NetTestScript.Enabled = script; }
    }
}
