using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Socket-free fault fixture for the production outbox, pending arbitration,
// terminal cache and owner receipt application. No fabricated damage payment.
internal static class TerminalRetryTests
{
    private static int _checks;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;
    private static void Check(bool ok, string label)
    { _checks++; if (!ok) throw new InvalidOperationException("Terminal retries: " + label); }
    private static void Session(string property, object value)
        => typeof(NetSession).GetProperty(property)!.SetValue(null, value);
    private static void Role(bool authority)
    { Session(nameof(NetSession.Role), authority ? NetRole.Server : NetRole.Client); Session(nameof(NetSession.IsAuthority), authority); }
    private static void Frame(uint value) => Session(nameof(NetSession.NetFrame), value);
    private static readonly byte[] Buffer = new byte[2048];
    private static HitClaimPacket? Compose()
    {
        int size = NetHitClaims.Compose(Buffer);
        Check(size == 0 || size == 1 + HitClaimPacket.Size && Buffer[0] == 1, "one exact request or no request");
        return size == 0 ? null : HitClaimPacket.Read(Buffer.AsSpan(1, HitClaimPacket.Size));
    }
    private static byte[] Packet(HitClaimPacket claim)
    { var bytes = new byte[1 + HitClaimPacket.Size]; bytes[0] = 1; claim.Write(bytes.AsSpan(1)); return bytes; }
    private static ShotKey Key(HitClaimPacket claim)
        => new(claim.AuthorityEpoch, claim.MatchId, 0, claim.ShooterGeneration, claim.ShooterLifeId, claim.ShotId);
    private static byte[] Ack(CombatAckEntry ack, HitClaimPacket claim)
    {
        var bytes = new byte[HitVerdictPacket.HeaderSize + CombatAckEntry.Size];
        HitVerdictPacket.Write(bytes, new[] { ack }, claim.MatchId, claim.AuthorityEpoch,
            claim.ShooterGeneration, claim.ShooterLifeId);
        return bytes;
    }
    private static (PlayerEntity Shooter, PlayerEntity Victim, ShotKey Key) Setup(uint frame, int ping)
    {
        HealthShotTests.Session(); Role(false); Frame(frame);
        NetHitClaims.Enabled = NetHitPrediction.Enabled = true;
        NetHitClaims.Reset(); NetHitPrediction.Reset(); NetFireEvents.Reset();
        NetSession.SlotPing[0] = ping;
        var shooter = HealthShotTests.Player(0); var victim = HealthShotTests.Player(1);
        for (int i = 0; i < PlayerEntity._players.Length; i++) PlayerEntity._players[i] = HealthShotTests.Player(i, 0);
        PlayerEntity._players[0] = shooter; PlayerEntity._players[1] = victim;
        shooter.Hunter = Hunter.Weavel;
        shooter.LoadFlags = victim.LoadFlags = LoadFlags.Active | LoadFlags.Spawned;
        return (shooter, victim, ShotKey.For(0, 5));
    }
    private static void Declare(PlayerEntity shooter, PlayerEntity victim, ShotKey key)
    {
        uint damage = 12; DamageFlags flags = 0;
        NetHitPrediction.NoteHit(victim, shooter, ref flags, ref damage, BeamType.Battlehammer,
            launchFrame: 1, direct: true, shotId: key.ShotId, shotKey: key, componentId: 1);
        Check(NetHitClaims.OutgoingReceipts == 1 && NetHitPrediction.Outstanding == 1,
            "real owner prediction declares one correlated pending receipt");
    }
    private static void TickOwner(uint frame)
    { Role(false); Frame(frame); NetHitClaims.Tick(); }

    private static void Budget(int ping, uint start)
    {
        var fixture = Setup(start, ping); Declare(fixture.Shooter, fixture.Victim, fixture.Key);
        var sends = new List<int>(); HitClaimPacket? original = null;
        for (int age = 0; age <= NetHitClaims.DeliveryMaxAge; age++)
        {
            if (age > 0) TickOwner(unchecked(start + (uint)age));
            var request = Compose();
            if (request is { } claim)
            {
                sends.Add(age); original ??= claim;
                Check(Key(claim) == fixture.Key && Packet(claim).SequenceEqual(Packet(original.Value)),
                    "all retries retain original full shot/payment identity and clocks");
            }
            if (age == 60) Check(NetHitPrediction.Outstanding == 1, "prediction survives through frame 60");
            if (age == 61) Check(NetHitPrediction.Outstanding == 0 && NetHitClaims.OutgoingReceipts == 1,
                "prediction expires independently while receipt remains live");
        }
        Check(sends.Count == 6, "exact six-send budget under complete loss");
        Check(sends[0] == 0 && sends[3] < 184 && sends[4] >= 184 && sends[5] < NetHitClaims.DeliveryMaxAge,
            "four early attempts and two reserved terminal attempts before deadline");
        Check(NetHitClaims.DeliveryMaxAge == 424 && NetHitClaims.OutgoingReceipts == 1 && NetHitClaims.Unanswered == 0,
            "receipt remains live through original 424-frame boundary");
        TickOwner(unchecked(start + 425));
        Check(NetHitClaims.OutgoingReceipts == 0 && NetHitClaims.Unanswered == 1 && Compose() == null,
            "one unanswered receipt after original deadline, never a seventh send");
        Console.WriteLine($"terminal retry schedule ping={ping} start={start}: {string.Join(',', sends)}");
    }

    private static void DelayedTerminal(int terminalAge, int ping, bool lifeChange, bool death = false)
    {
        const uint start = 100;
        var fixture = Setup(start, ping); Declare(fixture.Shooter, fixture.Victim, fixture.Key);
        var responses = new List<CombatAckEntry>();
        NetHitClaims.CombatAckSink = (_, entries) => { foreach (var entry in entries) responses.Add(entry); };
        var original = Compose() ?? throw new InvalidOperationException("initial request missing");
        Role(true); Frame(start);
        NetUnlagged.Record(original.AckFrame);
        var fire = new FireEvent(fixture.Key.ShotId, start, original.AckFrame, 0, FireEventKind.TurretFire,
            (byte)BeamType.Battlehammer, 0, 0, FireEvent.FlagPose,
            Vector3.Zero, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY, default, fixture.Key.LifeId);
        var intent = new IntentPacket { MatchId = fixture.Key.MatchId, AuthorityEpoch = fixture.Key.AuthorityEpoch,
            SlotGeneration = fixture.Key.Generation, LifeId = fixture.Key.LifeId, Frame = start,
            AckFrame = original.AckFrame, Aim = Vector3.UnitZ, WeaponSelect = 255,
            HasFireEvents = true, FireEventCount = 1 };
        intent.FireEvents[0] = fire;
        Check(NetFireEvents.Accept(0, intent) && NetFireEvents.IsPendingTurret(fixture.Key),
            "actual turret source is admitted and pending");
        NetHitClaims.Receive(0, Packet(original)); NetHitClaims.Tick();
        Check(NetHitClaims.ClaimsPendingCurrent == 1 && responses.Count == 0,
            "actual authority claim parks behind pending turret source");
        CombatAckEntry terminal = default;
        bool delivered = false; int retriesAfterTerminal = 0;
        long payments = NetCombatPayments.LifetimePayments;
        for (int age = 1; age < 300; age++)
        {
            TickOwner(start + (uint)age);
            var request = Compose();
            Role(true); Frame(start + (uint)age);
            if (request is { } claim)
            {
                Check(Packet(claim).SequenceEqual(Packet(original)), "retry cannot alter original claim clocks/payment identity");
                NetHitClaims.Receive(0, Packet(claim));
            }
            if (age == terminalAge)
            {
                // This is the production retirement seam used by native recovery
                // expiry. The test controls when it fires, as the fault did.
                NetFireEvents.RememberRetiredUnemittedTurret(fixture.Key);
            }
            int before = responses.Count;
            NetHitClaims.Tick();
            if (age == terminalAge)
            {
                Check(responses.Count == before + 1 && NetHitClaims.ClaimsPendingCurrent == 0,
                    "pending source retirement authors exactly one terminal response");
                terminal = responses[^1];
                Check(terminal.ClaimId == original.ClaimId && terminal.ShotId == original.ShotId
                    && terminal.Result == HitVerdictPacket.ResultInvalidLaunch && terminal.VictimLife == original.VictimLifeId,
                    "terminal rejection retains original correlation and lives");
                // Drop this sole initial response. Pending retries sent before it
                // cannot elicit another terminal until a later request arrives.
                if (lifeChange || death)
                {
                    var next = HealthShotTests.State(lifeChange ? (ushort)3 : (ushort)2, death ? (ushort)0 : (ushort)99); next.SlotIndex = 0; next.SlotGeneration = 9;
                    Check(NetPlayerLifecycle.AcceptState(next, start + (uint)age), "owner lifecycle transition accepted");
                    if (death) fixture.Shooter.Health = 0;
                }
                continue;
            }
            if (age > terminalAge && request is not null)
            {
                retriesAfterTerminal++;
                Check(responses.Count > before && responses[^1].Equals(terminal),
                    "late original request elicits the immutable cached terminal");
                Role(false);
                long denied = NetHitPrediction.Denied, confirmed = NetHitPrediction.Confirmed;
                NetHitClaims.ApplyVerdicts(Ack(responses[^1], original));
                if (lifeChange)
                {
                    Check(NetHitClaims.OutgoingReceipts == 1 && NetHitPrediction.Outstanding == 0,
                        "new owner life still rejects old-life receipt without prediction revival");
                    break;
                }
                Check(NetHitClaims.OutgoingReceipts == 0 && NetHitClaims.Unanswered == 0,
                    "lost sole terminal is recovered before receipt timeout");
                Check(NetHitPrediction.Outstanding == 0 && NetHitPrediction.Denied == denied
                    && NetHitPrediction.Confirmed == confirmed,
                    "late receipt cannot revive an expired prediction");
                NetHitClaims.ApplyVerdicts(Ack(responses[^1], original));
                Check(NetHitClaims.OutgoingReceipts == 0 && NetHitPrediction.Denied == denied,
                    "duplicate terminal remains once-only");
                delivered = true; break;
            }
        }
        Check(retriesAfterTerminal > 0 && (delivered || lifeChange), "reserved request occurs after delayed terminal");
        Check(fixture.Shooter.Health == (death ? 0 : 99) && fixture.Victim.Health == 99
            && NetCombatPayments.LifetimePayments == payments,
            "rejection/retries never revive damage or author a payment");
        Console.WriteLine($"terminal recovered delay={terminalAge} ping={ping} lifeChange={lifeChange} death={death}");
        NetHitClaims.CombatAckSink = null;
    }

    public static int Run(bool lostTerminalOnly = false)
    {
        var savedPlayers = (PlayerEntity[])PlayerEntity._players.Clone();
        bool savedPlayback = NetFireEvents.AcceptedPlaybackEnabled;
        var savedSink = NetHitClaims.CombatAckSink;
        try
        {
            NetFireEvents.AcceptedPlaybackEnabled = true;
            if (!lostTerminalOnly) { Budget(0, 100); Budget(250, 100); Budget(1000, 100); Budget(0, uint.MaxValue - 100); }
            DelayedTerminal(59, 0, false); DelayedTerminal(144, 0, false);
            DelayedTerminal(144, 250, false); DelayedTerminal(144, 0, false, death: true);
            DelayedTerminal(144, 0, true);
            Console.WriteLine($"PASS: {_checks} terminal retry reservation assertions");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            NetHitClaims.CombatAckSink = savedSink;
            NetFireEvents.AcceptedPlaybackEnabled = savedPlayback;
            NetSession.Stop(); Array.Copy(savedPlayers, PlayerEntity._players, savedPlayers.Length);
        }
    }
}
