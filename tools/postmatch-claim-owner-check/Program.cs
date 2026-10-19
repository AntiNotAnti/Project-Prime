using System.Collections;
using System.Net;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

internal static class Program
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
    private static int checks, failures;
    private static readonly List<CombatAckEntry> Answers = new();
    private static void Check(bool ok, string name)
    { checks++; if (!ok) { failures++; Console.Error.WriteLine("FAIL " + name); } }
    private static void Field(object owner, string name, object value) => owner.GetType().GetField(name, Hidden)!.SetValue(owner, value);
    private static void Property(string name, object value) => typeof(NetSession).GetProperty(name)!.SetValue(null, value);
    private static object? Call(string name, params object[] args) => typeof(NetHitClaims).GetMethod(name, Static)!.Invoke(null, args);
    private static void Flush() => Call("FlushVerdicts");
    private static byte[] Payload(in HitClaimPacket claim)
    { byte[] bytes = new byte[1 + HitClaimPacket.Size]; bytes[0] = 1; claim.Write(bytes.AsSpan(1)); return bytes; }
    private static HitClaimPacket Claim(ushort id, uint shot, BeamType beam = BeamType.Missile) => new()
    {
        MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
        ShooterGeneration = NetPlayerLifecycle.Generation(0), ShooterLifeId = NetPlayerLifecycle.Get(0),
        VictimSlot = 1, VictimGeneration = NetPlayerLifecycle.Generation(1), VictimLifeId = NetPlayerLifecycle.Get(1),
        ClaimId = id, ShotId = shot, ComponentId = 1, Beam = (byte)beam, Damage = 128,
        Flags = HitClaimPacket.FlagDirect | HitClaimPacket.FlagHeadshot,
        Frame = 90, AckFrame = 90, LaunchFrame = 90
    };
    private static void Match(ushort match, bool ending)
        => NetSession.ApplyMatchState(new MatchStatePacket { MatchId = match, AuthorityEpoch = 8001,
            RoomKey = "controlled-no-assets", Flags = ending ? MatchStatePacket.FlagEnding : MatchStatePacket.FlagInProgress }, false);
    private static void Receive(in HitClaimPacket claim) { NetHitClaims.Receive(0, Payload(claim)); Flush(); }
    private static CombatAckEntry Outcome(in HitClaimPacket claim) => new()
    {
        ShotId = claim.ShotId, VictimSlot = claim.VictimSlot, VictimGeneration = claim.VictimGeneration,
        VictimLife = claim.VictimLifeId, DamageApplied = 32, HealthAfter = 67, DamageSequence = 3,
        Flags = CombatAckFlags.OutcomePresent
    };
    private static bool Correlated(in HitClaimPacket claim, in CombatAckEntry ack) => ack.ClaimId == claim.ClaimId
        && ack.ShotId == claim.ShotId && ack.VictimSlot == claim.VictimSlot
        && ack.VictimGeneration == claim.VictimGeneration && ack.VictimLife == claim.VictimLifeId;
    private static void Park(in HitClaimPacket claim)
    {
        int at = (int)Call("SeenIndex", 0, claim.ClaimId)!;
        ((ShotKey[,])typeof(NetHitClaims).GetField("_seenKeys", Static)!.GetValue(null)!)[0, at] = NetShotProof.Key(0, claim);
        ((ShotPaymentKey[,])typeof(NetHitClaims).GetField("_seenPaymentKeys", Static)!.GetValue(null)!)[0, at] = NetShotProof.PaymentKey(0, claim);
        ((BeamType[,])typeof(NetHitClaims).GetField("_seenBeams", Static)!.GetValue(null)!)[0, at] = (BeamType)claim.Beam;
        ((CombatAckEntry[,])typeof(NetHitClaims).GetField("_seenOutcomes", Static)!.GetValue(null)!)[0, at] = new CombatAckEntry
        { ShotId = claim.ShotId, VictimSlot = claim.VictimSlot, VictimGeneration = claim.VictimGeneration, VictimLife = claim.VictimLifeId };
        Call("Remember", 0, claim.ClaimId, byte.MaxValue);
        Call("Park", 0, claim);
    }
    private static int Main()
    {
        try
        {
            string userdata = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
            Check(Path.IsPathFullyQualified(userdata) && userdata == Directory.GetCurrentDirectory(), "private absolute userdata/cwd");
            Headless.Enter();
            var scene = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, initializeRuntime: false);
            PlayerEntity.Construct(scene);
            GameState.Mode = GameMode.Battle;
            NetSession.StartServerAuthority((ReadOnlySpan<byte> _) => { }, () => { });
            NetHitClaims.CombatAckSink = (slot, entries) => { Check(slot == 0, "existing writer retains shooter slot"); foreach (var ack in entries) Answers.Add(ack); };
            Check(NetHitClaims.Arbitrating && NetSession.EvidenceTransport == null, "actual authority owner, no network socket");
            HitClaimPacket stale = default;
            for (ushort round = 1; round <= 6; round++)
            {
                Match(round, false); Property(nameof(NetSession.NetFrame), 100u);
                for (int slot = 0; slot < 2; slot++)
                {
                    NetPlayerLifecycle.SetOccupant(slot, round);
                    Check(NetPlayerLifecycle.AcceptState(new PlayerState { SlotIndex = (byte)slot, SlotGeneration = round,
                        LifeId = 1, Health = 99, Flags = PlayerState.FlagActive | PlayerState.FlagSpawned }, 100), "controlled current lifecycle admitted");
                    PlayerEntity.Players[slot].Health = 99;
                    PlayerEntity.Players[slot].LoadFlags = LoadFlags.Active | LoadFlags.Spawned;
                }
                Answers.Clear();
                var paid = Claim(1, 71);
                using (var payment = NetCombatPayments.Begin(NetShotProof.PaymentKey(0, paid), paid.Beam))
                { Check(payment.Allowed, "actual payment cell reserved"); payment.Commit(Outcome(paid)); }
                // Pending in the final live frame, with a paid body hit and a
                // larger claimed headshot: ending must not promote its damage.
                var parked = Claim(2, 72, BeamType.Imperialist);
                using (var payment = NetCombatPayments.Begin(NetShotProof.PaymentKey(0, parked), parked.Beam))
                { Check(payment.Allowed, "pending actual payment cell reserved"); payment.Commit(Outcome(parked)); }
                Park(parked);
                var unpaidPending = Claim(3, 73); Park(unpaidPending);
                Check(NetHitClaims.ClaimsPendingCurrent == 2, "controlled pending entries use actual bounded owner");
                Match(round, true);
                int[] health = PlayerEntity.Players.Select(p => p.Health).ToArray();
                int[] points = (int[])GameState.Points.Clone();
                long beforePayments = NetCombatPayments.Payments;
                NetHitClaims.Tick();
                Check(NetHitClaims.ClaimsPendingCurrent == 0 && NetHitClaims.AppliedHere == 0,
                    "Ending retires pending claims before grace/rescue/head correction");
                Check(Answers.Any(a => Correlated(parked, a) && a.Result == HitVerdictPacket.ResultDuplicate
                    && a.DamageApplied == 32 && a.HealthAfter == 67), "pending paid outcome is immutable");
                Check(Answers.Any(a => Correlated(unpaidPending, a) && a.Result == HitVerdictPacket.ResultRefused
                    && a.DamageApplied == 0 && a.Flags == CombatAckFlags.None), "pending unpaid receives terminal refusal without invented outcome");
                Answers.Clear(); Receive(paid);
                Check(Answers.Count == 1 && Correlated(paid, Answers[0]) && Answers[0].Result == HitVerdictPacket.ResultDuplicate
                    && Answers[0].DamageApplied == 32, "delayed paid claim receives exact terminal outcome");
                var original = Answers.LastOrDefault(); Answers.Clear(); Receive(paid);
                Check(Answers.Count == 1 && Answers[0].Equals(original), "lost terminal verdict retry replays exact cached result/outcome");
                var conflict = paid; conflict.ShotId++; Answers.Clear(); Receive(conflict);
                Check(Answers.Count == 1 && Correlated(conflict, Answers[0]) && Answers[0].Result == HitVerdictPacket.ResultRefused,
                    "same ClaimId with different shot cannot acquire cached paid outcome");
                Answers.Clear(); Receive(paid);
                Check(Answers.Count == 1 && Answers[0].Equals(original), "conflicting request cannot poison original terminal cache");
                conflict = paid; conflict.ComponentId++; Answers.Clear(); Receive(conflict);
                Check(Answers.Count == 1 && Correlated(conflict, Answers[0]) && Answers[0].Result == HitVerdictPacket.ResultRefused,
                    "same shot key with changed payment component cannot acquire paid outcome");
                Answers.Clear(); Receive(paid);
                Check(Answers.Count == 1 && Answers[0].Equals(original), "same-key payment conflict cannot poison terminal cache");
                var unpaid = Claim(4, 74); Answers.Clear(); Receive(unpaid);
                Check(Answers.Count == 1 && Correlated(unpaid, Answers[0]) && Answers[0].Result == HitVerdictPacket.ResultRefused
                    && NetHitClaims.ClaimsPendingCurrent == 0, "new unpaid Ending claim is correlated and never parked");
                var continuous = Claim(5, 75, BeamType.ShockCoil); continuous.Flags = HitClaimPacket.FlagContinuousTick | HitClaimPacket.FlagDirect;
                Check(NetHitClaims.ValidateWireClaims(Payload(continuous)), "positive continuous fixture meets actual wire invariant");
                using (var payment = NetContinuousPayments.Begin(NetHitClaims.ContinuousKey(0, continuous)))
                { Check(payment.Allowed, "actual continuous payment cell reserved"); payment.Commit(Outcome(continuous), 0, 0); }
                Answers.Clear(); Receive(continuous);
                Check(Answers.Count == 1 && Correlated(continuous, Answers[0]) && Answers[0].Result == HitVerdictPacket.ResultDuplicate,
                    "boundary ShockCoil acknowledges exact immutable continuous payment");
                var malformed = continuous; malformed.ClaimId = 8; malformed.Flags = HitClaimPacket.FlagContinuousTick;
                Check(!NetHitClaims.ValidateWireClaims(Payload(malformed)), "malformed continuous shape stays wire-rejected after Ending");
                Answers.Clear(); Receive(malformed);
                Check(Answers.Count == 1 && Answers[0].Result == HitVerdictPacket.ResultInvalidLaunch,
                    "direct owner call cannot rescue malformed continuous identity from paid receipt");
                Check(PlayerEntity.Players.Select(p => p.Health).SequenceEqual(health) && GameState.Points.SequenceEqual(points)
                    && NetCombatPayments.Payments == beforePayments && NetHitClaims.AppliedHere == 0,
                    "terminal receive/settlement never mutates health/score or makes another payment");
                if (round > 1) { Answers.Clear(); Receive(stale); Check(Answers.Count == 0, "old stream cannot answer replacement round"); }
                var wrong = unpaid; wrong.ClaimId = 6; wrong.ShooterGeneration++; Answers.Clear(); Receive(wrong);
                Check(Answers.Count == 0, "wrong shooter generation is dropped");
                wrong = unpaid; wrong.ClaimId = 7; wrong.VictimLifeId++; Answers.Clear(); Receive(wrong);
                Check(Answers.Count == 1 && Correlated(wrong, Answers[0]) && Answers[0].Result == HitVerdictPacket.ResultWrongLife,
                    "wrong victim life retains correlated rejection");
                Check(NetPlayerLifecycle.AcceptState(new PlayerState { SlotIndex = 0, SlotGeneration = round,
                    LifeId = 2, Health = 99, Flags = PlayerState.FlagActive | PlayerState.FlagSpawned }, 101), "controlled same-occupant life advances");
                Answers.Clear(); Receive(paid);
                Check(Answers.Count == 1 && Answers[0].Equals(original), "exact terminal cache retains original matched-lives outcome after respawn");
                Check(NetPlayerLifecycle.AcceptState(new PlayerState { SlotIndex = 1, SlotGeneration = round,
                    LifeId = 2, Health = 99, Flags = PlayerState.FlagActive | PlayerState.FlagSpawned }, 102), "controlled victim life advances");
                Answers.Clear(); Receive(paid);
                Check(Answers.Count == 1 && Answers[0].Equals(original), "exact cached outcome names original victim after both lives retire");
                stale = paid;
            }
            // Real handler, controlled already-admitted Peer and running-scene
            // witness; this is not an ordinary UDP admission test.
            var server = new DedicatedServer(); var sim = new ServerSim(); Field(sim, "_scene", scene); Field(server, "_sim", sim);
            Field(server, "_phase", SessionPhase.PostMatch);
            var peerType = typeof(DedicatedServer).GetNestedType("Peer", BindingFlags.NonPublic)!;
            object peer = Activator.CreateInstance(peerType, nonPublic: true)!;
            var sender = new IPEndPoint(IPAddress.Loopback, 32123);
            peerType.GetField("EndPoint")!.SetValue(peer, sender); peerType.GetField("SlotIndex")!.SetValue(peer, 0);
            peerType.GetField("MatchReady")!.SetValue(peer, true);
            ((IList)typeof(DedicatedServer).GetField("_peers", Hidden)!.GetValue(server)!).Add(peer);
            var final = Claim(20, 99); var payload = Payload(final); var bytes = new byte[payload.Length + 1];
            bytes[0] = (byte)PacketType.HitClaim; payload.CopyTo(bytes, 1);
            var handler = typeof(DedicatedServer).GetMethod("HandleHitClaim", Hidden)!;
            long before = NetHitClaims.Received; Answers.Clear();
            handler.Invoke(server, new object[] { new ReceivedPacket(sender, bytes, bytes.Length), 2d }); Flush();
            Check(NetHitClaims.Received == before + 1 && Answers.Count == 1 && Correlated(final, Answers[0]),
                "actual DedicatedServer handler passes delayed PostMatch claim to existing owner");
            Field(server, "_phase", SessionPhase.Lobby); before = NetHitClaims.Received;
            handler.Invoke(server, new object[] { new ReceivedPacket(sender, bytes, bytes.Length), 3d });
            Check(NetHitClaims.Received == before, "idle Lobby cannot process match claims");
            Field(server, "_phase", SessionPhase.PostMatch); peerType.GetField("MatchReady")!.SetValue(peer, false);
            handler.Invoke(server, new object[] { new ReceivedPacket(sender, bytes, bytes.Length), 4d });
            Check(NetHitClaims.Received == before, "unready Peer cannot process retained-world claims");
            peerType.GetField("MatchReady")!.SetValue(peer, true);
            handler.Invoke(server, new object[] { new ReceivedPacket(new IPEndPoint(IPAddress.Loopback, 32124), bytes, bytes.Length), 5d });
            Check(NetHitClaims.Received == before, "unregistered endpoint cannot process retained-world claims");
            Console.WriteLine($"{(failures == 0 ? "PASS" : "FAIL")} {checks} actual compiled PostMatch claim checks, {failures} failures;6 streams, controlled Peer/payment fixtures; no UDP/assets/GL/backend/gameplay acceptance");
            return failures == 0 ? 0 : 1;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { NetHitClaims.CombatAckSink = null; NetSession.Stop(); }
    }
}
