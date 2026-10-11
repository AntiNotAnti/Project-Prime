using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class SelfPredictionTests
{
    private static int _checks;
    private static PlayerEntity _owner = null!;
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Prepare()
    {
        HealthShotTests.Session(); NetHitPrediction.Reset(); NetHitPrediction.Enabled = true;
        _owner = HealthShotTests.Player(0); HealthShotTests.Field(_owner, "_healthMax", 99);
        PlayerEntity._players[0] = _owner;
        var state = HealthShotTests.State(2); state.SlotIndex = 0; state.SlotGeneration = 9;
        NetSession.RemoteStates[0] = state; NetSession.RemoteStateValid[0] = true;
    }
    private static HitClaimPacket Predict(uint shot = 501, uint damage = 20)
    {
        DamageFlags flags = 0; Vector3 impulse = Vector3.UnitY * .1f;
        NetHitPrediction.NoteHit(_owner, _owner, ref flags, ref damage, BeamType.Missile,
            launchFrame: 1, direction: impulse, shotId: shot, shotKey: ShotKey.For(0, shot), componentId: 17);
        _owner.Health -= (int)damage; _owner.Speed += impulse;
        byte[] bytes = new byte[4096]; Check(NetHitClaims.Compose(bytes) > 0, "legitimate self prediction emits claim");
        var claim = HitClaimPacket.Read(bytes.AsSpan(1));
        Check(claim.ShotId == shot && claim.ComponentId == 17 && claim.VictimSlot == 0
            && claim.ShooterGeneration == 9 && claim.ShooterLifeId == 2,
            "self claim preserves emitted component and original owner identity");
        return claim;
    }
    private static CombatAckEntry Ack(HitClaimPacket claim) => new()
    {
        ClaimId = claim.ClaimId, ShotId = claim.ShotId, VictimSlot = 0,
        VictimGeneration = 9, VictimLife = 2, DamageSequence = 11, DamageApplied = 20,
        HealthAfter = 79, Result = (byte)CombatAckResult.AlreadyResolved, Flags = CombatAckFlags.OutcomePresent
    };
    private static void Deliver(CombatAckEntry ack)
    {
        byte[] bytes = new byte[HitVerdictPacket.HeaderSize + CombatAckEntry.Size];
        HitVerdictPacket.Write(bytes, new[] { ack }, 51, 4, 9, 2); NetHitClaims.ApplyVerdicts(bytes);
    }
    private static void ReceiptSnapshot()
    {
        var state = NetSession.RemoteStates[0]; state.Health = 79; state.DamageEventId = 11;
        NetSession.RemoteStates[0] = state;
    }
    private static void Ordering(bool snapshotFirst)
    {
        Prepare(); var claim = Predict();
        Check(NetHitPrediction.HasSelfClaims((byte)BeamType.Missile)
            && NetHitPrediction.LocalHealthFor(_owner, 99) == 79,
            "unpaid local self prediction holds immediate health without rebound");
        var wrong = Ack(claim); wrong.ShotId++;
        Deliver(wrong);
        Check(NetHitPrediction.HasSelfClaims((byte)BeamType.Missile)
            && !NetHitPrediction.HasEventReceipt(0, 11), "wrong shot ACK cannot settle self debit or register event");
        if (snapshotFirst)
        {
            ReceiptSnapshot();
            Check(NetHitPrediction.LocalHealthFor(_owner, 79) == 79,
                "paid snapshot before self ACK cannot subtract prediction twice");
        }
        Deliver(Ack(claim));
        Check(!NetHitPrediction.HasSelfClaims((byte)BeamType.Missile)
            && NetHitPrediction.LocalHealthFor(_owner, snapshotFirst ? 79 : 99) == 79,
            "exact self ACK settles debit without health dip or rebound");
        ReceiptSnapshot();
        Check(NetHitPrediction.LocalHealthFor(_owner, 79) == 79, "self ACK and canonical snapshot converge once");
        Vector3 before = _owner.Speed;
        Check(!NetHitPrediction.ConfirmSelfEvent(0, 9, 2, 12, Vector3.UnitY * .2f)
            && _owner.Speed == before, "unrelated event cannot correct predicted self impulse");
        Check(!NetHitPrediction.ConfirmSelfEvent(0, 9, 3, 11, Vector3.UnitY * .2f)
            && _owner.Speed == before, "wrong-life event cannot consume exact self receipt");
        Check(NetHitPrediction.ConfirmSelfEvent(0, 9, 2, 11, Vector3.UnitY * .2f)
            && Vector3.Distance(_owner.Speed, before + Vector3.UnitY * .1f) < .000001f,
            "exact event applies only authority-minus-predicted impulse delta");
        before = _owner.Speed;
        Check(!NetHitPrediction.ConfirmSelfEvent(0, 9, 2, 11, Vector3.UnitY * .2f)
            && _owner.Speed == before, "repeated self event cannot apply impulse twice");
    }
    private static void MultipleOutstanding()
    {
        Prepare(); var first = Predict(501); var second = Predict(502);
        Check(NetHitPrediction.LocalHealthFor(_owner, 99) == 59, "two self predictions retain cumulative health floor");
        Deliver(Ack(first));
        Check(NetHitPrediction.HasSelfClaims((byte)BeamType.Missile)
            && NetHitPrediction.LocalHealthFor(_owner, 99) == 59,
            "accepted first self ACK cannot refund still-pending second splash");
        var secondAck = Ack(second); secondAck.DamageSequence = 12; secondAck.HealthAfter = 59;
        Deliver(secondAck);
        Check(!NetHitPrediction.HasSelfClaims((byte)BeamType.Missile)
            && NetHitPrediction.LocalHealthFor(_owner, 99) == 59,
            "two accepted self ACKs retain final authority health until snapshot");

        Prepare(); first = Predict(601); second = Predict(602);
        var correction = Ack(first); correction.DamageApplied = 10; correction.HealthAfter = 89;
        Deliver(correction);
        Check(NetHitPrediction.LocalHealthFor(_owner, 99) == 69,
            "partial first self correction restores only excess debit while holding later splash");
        var rejected = Ack(second); rejected.Result = HitVerdictPacket.ResultInvalidLaunch;
        rejected.Flags = 0; rejected.DamageSequence = 0; rejected.DamageApplied = 0;
        Deliver(rejected);
        Check(!NetHitPrediction.HasSelfClaims((byte)BeamType.Missile)
            && NetHitPrediction.LocalHealthFor(_owner, 99) == 89,
            "later self rejection retires only its debit and retains accepted corrected health");
    }
    private static void LethalAndEnvironment()
    {
        Prepare(); var claim = Predict(damage: 120);
        Check(_owner.Health == 1 && (claim.Flags & HitClaimPacket.FlagLethal) != 0
            && NetHitPrediction.LocalHealthFor(_owner, 99) == 1,
            "legitimate lethal self claim holds one HP pending authority death");
        Check(!NetHitPrediction.Predicts(_owner, null, DamageFlags.Death),
            "environmental death cannot enter client prediction");
        var beam = (BeamProjectileEntity)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(BeamProjectileEntity));
        beam.Owner = _owner; beam.Beam = BeamType.Missile;
        Check(!NetHitPrediction.Predicts(_owner, beam, DamageFlags.Burn),
            "burn ticks remain authority resolved even for local original owner");
    }
    internal static void RunReplay(PlayerEntity owner)
    {
        var role = NetSession.Role; bool authority = NetSession.IsAuthority; int local = NetSession.LocalSlot;
        var savedState = NetSession.RemoteStates[owner.SlotIndex]; bool valid = NetSession.RemoteStateValid[owner.SlotIndex];
        try
        {
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Client);
            typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, false);
            typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, owner.SlotIndex);
            foreach (string order in new[] { "event-before-prediction", "snapshot-before-ACK", "ACK-before-snapshot" })
            {
                NetHitClaims.Reset(); NetHitPrediction.Reset(); NetDamage.ForgetSlot(owner.SlotIndex);
                owner.Health = 99; owner.Speed = Vector3.Zero; owner.ModReleaseSpawnProtection();
                HealthShotTests.Field(owner, "_spawnInvulnTimer", (ushort)0);
                HealthShotTests.Field(owner, "_damageInvulnTimer", (ushort)0);
                ushort generation = NetPlayerLifecycle.Generation(owner.SlotIndex), life = NetPlayerLifecycle.Get(owner.SlotIndex);
                var baseline = HealthShotTests.State(life); baseline.SlotIndex = (byte)owner.SlotIndex;
                baseline.SlotGeneration = generation; baseline.AttackerSlot = (byte)owner.SlotIndex;
                NetSession.RemoteStates[owner.SlotIndex] = baseline; NetSession.RemoteStateValid[owner.SlotIndex] = true;
                NetDamage.BeginLife(owner.SlotIndex, baseline);
                var hit = baseline; hit.Health = 79; hit.DamageEventId = 11;
                hit.DamageBeam = (byte)BeamType.Missile; hit.HitDirection = Vector3.UnitY * .2f;
                hit.DamageFlags = (byte)DamageFlags.NoSfx;
                hit.Damage0 = new DamageEvent { EventId = 11, AttackerSlot = (byte)owner.SlotIndex,
                    AttackerGeneration = generation, Beam = (byte)BeamType.Missile,
                    Damage = 20, Direction = hit.HitDirection, Flags = hit.DamageFlags };
                if (order == "event-before-prediction")
                {
                    NetDamage.Replay(owner, hit); NetSession.RemoteStates[owner.SlotIndex] = hit;
                    Check(owner.Health == 79, "event-first actual Replay applies authority health once");
                    Check(NetDamage.HasUnclaimedSelfEvent((byte)BeamType.Missile),
                        "actual Replay retains self event arriving before any local prediction");
                    Check(Vector3.Distance(owner.Speed, hit.HitDirection) < .000001f,
                        "event-first actual Replay applies authority impulse once");
                }
                bool defer = NetDamage.HasUnclaimedSelfEvent((byte)BeamType.Missile);
                uint damage = 20; DamageFlags flags = 0; var predictedImpulse = defer ? Vector3.Zero : Vector3.UnitY * .1f;
                uint shot = 701;
                NetHitPrediction.NoteHit(owner, owner, ref flags, ref damage, BeamType.Missile,
                    launchFrame: 1, direction: predictedImpulse, shotId: shot,
                    shotKey: ShotKey.For(owner.SlotIndex, shot), componentId: 17, deferSelf: defer);
                owner.Health -= (int)damage; owner.Speed += predictedImpulse;
                Check(owner.Health == 79, $"{order}: local collision cannot double-debit event-first health");
                byte[] claims = new byte[4096]; Check(NetHitClaims.Compose(claims) > 0, "real Replay race still emits exact self claim");
                var claim = HitClaimPacket.Read(claims.AsSpan(1));
                Check(claim.Damage == 20 && claim.ShotId == shot && claim.ComponentId == 17,
                    "deferred prediction preserves full claim damage and emitted identity");
                if (order == "snapshot-before-ACK")
                {
                    NetSession.RemoteStates[owner.SlotIndex] = hit; NetDamage.Replay(owner, hit);
                    Check(Vector3.Distance(owner.Speed, predictedImpulse) < .000001f,
                        "snapshot before ACK defers exact feedback rather than doubling predicted impulse");
                }
                var ack = Ack(claim); ack.VictimSlot = (byte)owner.SlotIndex;
                ack.VictimGeneration = generation; ack.VictimLife = life;
                byte[] verdict = new byte[HitVerdictPacket.HeaderSize + CombatAckEntry.Size];
                HitVerdictPacket.Write(verdict, new[] { ack }, NetSession.CurrentMatchId, NetSession.AuthorityEpoch, generation, life);
                NetHitClaims.ApplyVerdicts(verdict);
                if (order == "ACK-before-snapshot")
                { NetSession.RemoteStates[owner.SlotIndex] = hit; NetDamage.Replay(owner, hit); }
                NetDamage.FlushSelfFeedback();
                Check(NetHitPrediction.LocalHealthFor(owner, 79) == 79 && owner.Health == 79,
                    $"{order}: exact ACK and full Replay converge without health dip or refund");
                Check(Vector3.Distance(owner.Speed, hit.HitDirection) < .000001f,
                    $"{order}: exact full Replay converges to one authority impulse");
                var speed = owner.Speed; NetDamage.Replay(owner, hit); NetDamage.FlushSelfFeedback();
                Check(owner.Speed == speed && owner.Health == 79,
                    "duplicate snapshot and feedback flush cannot repay event-first impulse or health");
            }
        }
        finally
        {
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, role);
            typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, authority);
            typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, local);
            NetSession.RemoteStates[owner.SlotIndex] = savedState; NetSession.RemoteStateValid[owner.SlotIndex] = valid;
            NetHitClaims.Reset(); NetHitPrediction.Reset(); NetDamage.ForgetSlot(owner.SlotIndex);
        }
    }
    public static int Run()
    {
        var saved = (PlayerEntity[])PlayerEntity._players.Clone();
        try
        {
            Ordering(false); Ordering(true); MultipleOutstanding(); LethalAndEnvironment();
            Console.WriteLine($"PASS: {_checks} self prediction assertions"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { NetSession.Stop(); Array.Copy(saved, PlayerEntity._players, saved.Length); }
    }
}
