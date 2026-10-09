using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Benign policy regressions. Policy checks perform no sends or process
/// starts; constructor checks bind local ephemeral sockets. Combat checks use the
/// real private headless simulation and local values.</summary>
public static class NetworkAuthorityPolicyCheck
{
    private static int _checks;
    private static void Check(bool value, string name)
    { _checks++; if (!value) throw new InvalidOperationException(name); }
    public static int Run()
    {
        _checks = 0;
        try
        {
            Intent(); AtomicIntent(); Hosting(); Directory(); Resolver(); NativeTiming(); TransportStartup();
            Console.WriteLine($"PASS: {_checks} network authority/admission policy assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Intent()
    {
        var packet = new IntentPacket { Position = new(1, 2, 3), Aim = Vector3.UnitZ, WeaponSelect = 255 };
        Check(NetIntentPolicy.Validate(packet), "finite owner position remains accepted");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 100000f })
        {
            packet.Position.X = invalid;
            Check(!NetIntentPolicy.Validate(packet), "entire invalid position rejected");
            packet.Position = new(1, 2, 3); packet.Aim.Y = invalid;
            Check(!NetIntentPolicy.Validate(packet), "entire invalid aim rejected"); packet.Aim = Vector3.UnitZ;
        }
        packet.Frame = 100; packet.FireEventCount = 1;
        packet.FireEvents[0] = new(1, 100, 90, 0, FireEventKind.PressFire, 0, 0, 0,
            FireEvent.FlagPose, new(1, 2, 3), Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ);
        Check(NetIntentPolicy.Validate(packet), "bounded finite authored pose accepted");
        packet.FireEvents[0] = packet.FireEvents[0] with { Origin = new(100000, 0, 0) };
        Check(!NetIntentPolicy.Validate(packet), "shot origin follows finite world bound");
    }
    private static void AtomicIntent()
    {
        NetSession.StartPlayback();
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 12, AuthorityEpoch = 4 }, false);
            var roster = RosterPacket.Create(); roster.MatchId = 12; roster.AuthorityEpoch = 4; roster.Revision = 1; roster.Count = 1;
            roster.Slots[0] = 1; roster.Generations[0] = 7;
            NetSession.ApplyRoster(roster);
            NetPlayerLifecycle.AcceptState(new PlayerState { SlotIndex = 1, SlotGeneration = 7, LifeId = 1,
                Flags = PlayerState.FlagActive | PlayerState.FlagSpawned }, 1);
            var good = new IntentPacket { MatchId = 12, AuthorityEpoch = 4, SlotGeneration = 7, LifeId = 1,
                Frame = 1, Position = new(1, 2, 3), Aim = Vector3.UnitZ, WeaponSelect = 255 };
            Check(NetSession.AcceptSlotIntent(1, good), "whole valid intent stored atomically");
            var invalid = good; invalid.Frame = 2; invalid.Position.X = float.NaN;
            Check(!NetSession.AcceptSlotIntent(1, invalid) && NetSession.RemoteIntents[1].Frame == 1,
                "invalid newer intent cannot replace accepted history");
            good.Frame = 2;
            Check(NetSession.AcceptSlotIntent(1, good), "invalid frame does not poison ordering baseline");
        }
        finally { NetSession.Stop(); }
    }
    private static void Hosting()
    {
        var cache = new HostedRequestCache(); int starts = 0;
        var sender = new IPEndPoint(IPAddress.Parse("192.0.2.1"), 10001);
        var request = new HostRequestPacket { Protocol = NetConfig.ProtocolVersion, HostNonce = 123,
            RoomKey = "MP1 SANCTORUS", ServerName = "Policy check", MaxPlayers = 2 };
        Guid token = Guid.NewGuid();
        HostReplyPacket Start() { starts++; return new() { Started = true, Port = 27890, OwnerToken = token }; }
        var first = cache.GetOrStart(request, sender, 1, Start);
        request.HostCookie = 999;
        var retry = cache.GetOrStart(request, sender, 10, Start);
        Check(starts == 1 && retry.Started && retry.Port == first.Port && retry.OwnerToken == token,
            "retry retains allocation and owner token across cookie refresh");
        request.ServerName = "Different request";
        Check(!cache.GetOrStart(request, sender, 11, Start).Started && starts == 1,
            "nonce cannot change exact request");
        request.ServerName = "Policy check";
        cache.GetOrStart(request, new(sender.Address, 10002), 12, Start);
        Check(starts == 2, "separate NAT endpoint gets independent allocation");
        cache.GetOrStart(request, sender, 182, Start);
        Check(starts == 3, "expired retry can allocate anew");
        var bounded = new HostedRequestCache();
        for (int i = 0; i < HostedRequestCache.Capacity; i++)
        { request.HostNonce = (ulong)i + 1; bounded.GetOrStart(request, sender, 0, Start); }
        request.HostNonce++;
        Check(!bounded.GetOrStart(request, sender, 1, Start).Started
            && bounded.Count == HostedRequestCache.Capacity, "host result cache has hard capacity");
    }
    private static MasterDirectory.Entry Entry(int address, int port) => new()
    {
        Key = new(IPAddress.Parse($"198.51.100.{address}"), port),
        Reporter = new(IPAddress.Parse($"198.51.100.{address}"), 30000),
        Port = (ushort)port, MaxPlayers = 8, Protocol = NetConfig.ProtocolVersion
    };
    private static void Directory()
    {
        var directory = new MasterDirectory(); var entry = Entry(1, 27888);
        Check(directory.Begin(entry, 0, out ulong nonce) && directory.Entries.Count == 0,
            "unverified heartbeat is not publicly listed");
        Check(!directory.Complete(new(entry.Key.Address, 27889), nonce, 1)
            && !directory.Complete(entry.Key, nonce + 1, 1), "proof fences port and nonce");
        Check(directory.Complete(entry.Key, nonce, 1) && directory.Entries.Count == 1,
            "verified advertised game port enters directory");
        Check(!directory.Farewell(new(entry.Reporter.Address, 30001), entry.Port),
            "other endpoint behind same address cannot remove registration");
        Check(directory.Farewell(entry.Reporter, entry.Port), "registered reporter farewell removes listing");
        for (int i = 0; i < MasterDirectory.AddressCapacity; i++)
        { entry = Entry(1, 28000 + i); Check(directory.Begin(entry, 2, out nonce) && directory.Complete(entry.Key, nonce, 2), "per-address bounded admission"); }
        Check(!directory.Begin(Entry(1, 29000), 2, out _), "per-address capacity enforced");
        for (int i = 0; i < 20; i++)
        {
            entry = new MasterDirectory.Entry { Key = new(IPAddress.Loopback, 30000 + i),
                Reporter = new(IPAddress.Loopback, 20000 + i), Port = (ushort)(30000 + i), MaxPlayers = 8 };
            Check(directory.Begin(entry, 2, out nonce) && directory.Complete(entry.Key, nonce, 2),
                "operator-owned loopback host pool can use global bounded capacity");
        }
        var pending = new MasterDirectory();
        for (int i = 1; i <= MasterDirectory.PendingCapacity; i++)
            Check(pending.Begin(Entry(i, 27888), 0, out _), "bounded pending slot available");
        Check(!pending.Begin(Entry(200, 27888), 1, out _) && pending.PendingCount == MasterDirectory.PendingCapacity,
            "pending proof table has hard capacity");
        Check(pending.Begin(Entry(200, 27888), 6, out _) && pending.PendingCount == 1,
            "expired proof reservations release capacity");
        var full = new MasterDirectory();
        for (int i = 0; i < MasterDirectory.Capacity; i++)
        { entry = Entry(i / 16 + 1, 28000 + i % 16); Check(full.Begin(entry, 0, out nonce) && full.Complete(entry.Key, nonce, 0), "global directory bounded admission"); }
        Check(!full.Begin(Entry(30, 29000), 0, out _) && full.Entries.Count == 255, "global active capacity matches wire total");
        full.Expire(51); Check(full.Entries.Count == 0, "silence expires verified rows");
        IPAddress ip = IPAddress.Parse("192.0.2.1");
        Check(full.AllowQuery(ip, 0) && !full.AllowQuery(ip, .1) && full.AllowQuery(ip, .25), "list bytes rate-bounded by address");
        Check(!full.AllowQuery(IPAddress.Parse("192.0.2.2"), .26)
            && full.AllowQuery(IPAddress.Parse("192.0.2.2"), .282), "list bytes rate-bounded globally");
    }
    private static void Resolver()
    {
        int resolves = 0; CancellationToken token = default;
        var blocked = new TaskCompletionSource<IPAddress[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reporter = new MasterReporter("policy.invalid", 27889, (_, cancel) =>
        { resolves++; token = cancel; return blocked.Task; });
        reporter.Beat(0, "check", 27888, 0, 8, 0, "room");
        reporter.Beat(15, "check", 27888, 0, 8, 0, "room");
        Check(resolves == 1 && !blocked.Task.IsCompleted, "pending DNS never waits or multiplies workers");
        reporter.Dispose();
        Check(token.IsCancellationRequested, "disposal cancels resolver without waiting");
        blocked.SetException(new InvalidOperationException("benign resolver fault"));
        reporter.Beat(30, "check", 27888, 0, 8, 0, "room");
        Check(resolves == 1, "disposed reporter cannot restart DNS");
    }
    private static void NativeTiming()
    {
        var fire = new FireEvent(1, 100, 100, 0, FireEventKind.PressFire, 0, 0, 0);
        Check(NetAttackPaths.TimingSupports(fire, 100, 1), "native first source-frame step supported");
        Check(NetAttackPaths.TimingSupports(fire, 105, 6), "native source-relative travel supported");
        Check(!NetAttackPaths.TimingSupports(fire, 99, 1), "pre-fire time cannot reuse native path");
        Check(!NetAttackPaths.TimingSupports(fire, 100, 10), "later path cannot reuse old source contact time");
        Check(!NetAttackPaths.TimingSupports(fire, 200, 1), "old path cannot reuse new source contact time");
        fire = fire with { Kind = FireEventKind.ContinuousTick, ContinuousPhase = 7 };
        Check(NetAttackPaths.TimingSupports(fire, 7, 1) && !NetAttackPaths.TimingSupports(fire, 8, 1),
            "continuous phase identity is independent of source travel clock");
        Check(NetAttackPaths.NativeHeadRange(new(0, 0, 15), Vector3.Zero)
            && !NetAttackPaths.NativeHeadRange(new(0, 0, 15.001f), Vector3.Zero),
            "non-Imperialist head damage uses exact native current-position 15-unit boundary");
    }
    private static void TransportStartup()
    {
        // Exercises shutdown before the receive worker has entered its first
        // poll, as well as shutdown during a poll. A worker fault terminates
        // the check process; completing each cycle proves safe ownership.
        for (int i = 0; i < 32; i++)
        {
            using (var transient = new NetTransport(0))
                Check(transient.LocalPort > 0, "immediate transport disposal has a valid acquired endpoint");
        }
        int port;
        using (var occupied = new NetTransport(0))
        {
            port = occupied.LocalPort;
            for (int i = 0; i < 8; i++)
            {
                bool refused = false;
                try { using var unexpected = new NetTransport(port); }
                catch (SocketException) { refused = true; }
                Check(refused, "occupied endpoint refuses each constructor attempt");
            }
        }
        using (var rebound = new NetTransport(port))
            Check(rebound.LocalPort == port, "failed constructors do not prevent owner release and rebind");
        bool startRefused = false;
        try
        {
            using var unexpected = new NetTransport(port, playbackOnly: false,
                _ => throw new InvalidOperationException("benign unstarted worker fixture"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "benign unstarted worker fixture")
        { startRefused = true; }
        Check(startRefused, "unstarted worker cleanup preserves the original startup failure");
        using var afterFailure = new NetTransport(port);
        Check(afterFailure.LocalPort == port, "failed thread startup immediately releases its bound socket");
    }
    public static int RunCombat(string room)
    {
        Headless.Enter();
        _checks = 0; var sim = new ServerSim();
        if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { })) return 1;
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle, Flags = MatchStatePacket.FlagInProgress }, false);
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte slot = 0; slot < 2; slot++) { roster.Slots[slot] = slot; roster.Generations[slot] = 1; roster.Hunters[slot] = (byte)Hunter.Samus; roster.Names[slot] = "Policy"; }
            NetSession.ApplyRoster(roster); NetSlotManager.Sync();
            for (int i = 0; i < 120; i++) sim.Step();
            PlayerEntity shooter = PlayerEntity.Players[0], victim = PlayerEntity.Players[1];
            void ResetEvidence()
            {
                // A fixture resets IDs without rebuilding the world. Retire its
                // prior native pool occupants too, as real lifecycle reset does.
                foreach (var player in PlayerEntity.Players)
                {
                    if (player?.EquipInfo.Beams == null) continue;
                    foreach (var beam in player.EquipInfo.Beams)
                        if (beam != null) { beam.Lifespan = 0; beam.Flags |= BeamFlags.Collided; }
                }
                NetFireEvents.Reset();
            }
            Check(shooter.ModIsInPlay && victim.ModIsInPlay, "real authority actors spawned");
            shooter.ModSetWeapon(BeamType.VoltDriver);
            Check(!shooter.ModOwnsAttackWeapon(BeamType.VoltDriver), "owner selection cannot grant unavailable weapon");
            int ammo = shooter.ModAmmo.Missiles;
            shooter.ModSetAmmo(65535, 65535);
            Check(shooter.ModAmmo.Missiles == ammo, "owner ammo cannot refill authority pool");
            typeof(PlayerEntity).GetField("_doubleDmgTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shooter, (ushort)0);
            shooter.ModSetShotState(255, 0, true, false);
            Check(!shooter.DoubleDamage, "owner bit cannot grant authority double damage");
            ResetEvidence();
            uint now = NetSession.NetFrame;
            void SetFrame(uint frame) => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, frame);
            var aimVecs = typeof(PlayerEntity).GetMethod("UpdateAimVecs", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Vector3 NativeMuzzle(Vector3 direction)
            {
                shooter.ModSetAim(direction); aimVecs.Invoke(shooter, null); return shooter.ModMuzzlePos;
            }
            void PlaceNativeMuzzle(Vector3 position)
            {
                shooter.ModPlaceAt(shooter.Position + position - NativeMuzzle(Vector3.UnitZ));
                NativeMuzzle(Vector3.UnitZ);
            }
            IntentPacket Carrier(uint frame, uint id, uint source, BeamType beam)
            {
                Vector3 origin = NativeMuzzle(Vector3.UnitZ);
                shooter.ModCaptureFireSource(false, out Vector3 sourceBody, out Vector3 sourceUp, out byte sourceFlags);
                return new()
                {
                    MatchId = 1, AuthorityEpoch = 1, SlotGeneration = NetPlayerLifecycle.Generation(0), LifeId = NetPlayerLifecycle.Get(0),
                    Frame = frame, AckFrame = source, Position = shooter.Position, Aim = Vector3.UnitZ, WeaponSelect = (byte)beam,
                    HasFireEvents = true, FireEventCount = 1,
                    FireEvents = Events(new(id, source, source, 0, FireEventKind.PressFire, (byte)beam, 0, 0,
                        FireEvent.FlagPose, origin, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
                        default, sourceBody, sourceUp, sourceFlags))
                };
            }
            victim.ModPlaceAt(NativeMuzzle(Vector3.UnitZ) + Vector3.UnitZ
                - Vector3.UnitY * (Fixed.ToFloat(victim.Values.MinPickupHeight) + .05f));
            void NativeWitness()
            {
                // Collision-independent native path fixture: retain a healthy
                // historical body, detach only its current collision participation,
                // then traverse a real admitted source-body/muzzle shot. This
                // helper validates admission/path arbitration, not a live network
                // owner prediction or a current authority collision.
                int health = victim.Health; victim.Health = 0;
                try
                {
                    NetAcceptedAttacks.EmitPending();
                    foreach (var beam in shooter.EquipInfo.Beams)
                        if (beam.Lifespan > 0 && beam.Age == 0 && !beam.Flags.TestFlag(BeamFlags.Collided)) beam.Process();
                }
                finally { victim.Health = health; }
            }
            var first = Carrier(now, 1, now, BeamType.PowerBeam);
            Check(NetSession.AcceptSlotIntent(0, first) && NetAcceptedAttacks.Authorized(0, 1), "accepted successful-fire evidence authorized before authority collision");
            NetUnlagged.Record(now);
            NativeWitness();
            SetFrame(now + 1); var retry = Carrier(now + 1, 1, now, BeamType.PowerBeam);
            NetSession.AcceptSlotIntent(0, retry);
            Check(NetAcceptedAttacks.Accepted == 1, "repeated event does not reserve a second attack");
            var claim = new HitClaimPacket { Beam = (byte)BeamType.PowerBeam, ShotId = 1, Damage = 1,
                MatchId = 1, AuthorityEpoch = 1,
                ShooterGeneration = NetPlayerLifecycle.Generation(0), ShooterLifeId = NetPlayerLifecycle.Get(0),
                VictimGeneration = NetPlayerLifecycle.Generation(1), VictimLifeId = NetPlayerLifecycle.Get(1),
                VictimSlot = 1, AckFrame = now, LaunchFrame = now, Frame = now, HitPoint = victim.Position, Flags = HitClaimPacket.FlagDirect };
            Check(NetAcceptedAttacks.ValidateClaim(0, claim), "producer-shaped victim.Position claim remains available without server collision");
            var invalidPoint = claim; invalidPoint.HitPoint += Vector3.UnitX * 3;
            Check(!NetAcceptedAttacks.ValidateClaim(0, invalidPoint), "reported point stays bound to historical victim pose");
            var invalidTime = claim; invalidTime.Frame += 32;
            Check(!NetAcceptedAttacks.ValidateClaim(0, invalidTime), "a later owner hit cannot borrow an earlier native path");
            invalidTime = claim; invalidTime.Frame -= 1;
            Check(!NetAcceptedAttacks.ValidateClaim(0, invalidTime), "a pre-fire hit cannot borrow future native path");
            var invalidHead = claim; invalidHead.Flags |= HitClaimPacket.FlagHeadshot;
            Check(!NetAcceptedAttacks.ValidateClaim(0, invalidHead), "body-height native witness cannot authorize a head flag");
            var wrong = claim; wrong.ShotId = 100;
            Check(!NetAcceptedAttacks.ValidateClaim(0, wrong), "claim requires accepted attack identity");
            wrong = claim; wrong.Beam = (byte)BeamType.Missile;
            Check(!NetAcceptedAttacks.ValidateClaim(0, wrong), "claim cannot substitute authorized weapon");
            wrong = claim; wrong.Flags = HitClaimPacket.FlagFrozen;
            Check(!NetAcceptedAttacks.ValidateClaim(0, wrong), "uncharged power beam cannot declare freeze");
            wrong = claim; wrong.Damage = 1000;
            Check(!NetAcceptedAttacks.ValidateClaim(0, wrong), "claim damage follows exact shot resource state");
            SetFrame(now + 2);
            NetSession.AcceptSlotIntent(0, Carrier(now + 2, 2, now + 2, BeamType.PowerBeam));
            Check(!NetAcceptedAttacks.Authorized(0, 2), "source cadence rejects an early second fire");
            shooter.Health = 0;
            Check(NetAcceptedAttacks.ValidateClaim(0, claim), "pre-death authorization survives shooter death");
            Check(NetAcceptedAttacks.ConsumeClaim(0, claim) && !NetAcceptedAttacks.ConsumeClaim(0, claim),
                "one non-splash projectile has one legal victim outcome");
            shooter.Health = 99;
            ResetEvidence(); shooter.ModArmWeapon(BeamType.Missile);
            int missilesBefore = shooter.ModAmmo.Missiles;
            SetFrame(now + 100); var missile = Carrier(now + 100, 1, now + 100, BeamType.Missile);
            NetSession.AcceptSlotIntent(0, missile);
            Check(NetAcceptedAttacks.Authorized(0, 1) && shooter.ModAmmo.Missiles == missilesBefore,
                "attack reservation preserves native launch ammo until consumed");
            var missileClaim = claim; missileClaim.Beam = (byte)BeamType.Missile;
            missileClaim.AckFrame = missileClaim.LaunchFrame = now + 100;
            missileClaim.Frame = now + 102;
            NetUnlagged.Record(now + 100);
            NativeWitness();
            // Missiles are deliberately slow; retain native flight until it
            // reaches the detached historical target.
            victim.Health = 0;
            for (int i = 0; i < 8; i++) foreach (var beam in shooter.EquipInfo.Beams)
                if (beam.Lifespan > 0 && !beam.Flags.TestFlag(BeamFlags.Collided)) beam.Process();
            victim.Health = 99;
            var inflatedImpulse = missileClaim; inflatedImpulse.Direction = Vector3.UnitX;
            Check(!NetAcceptedAttacks.ValidateClaim(0, inflatedImpulse), "claimed impulse cannot borrow a universal maximum instead of actual native component");
            Check(NetAcceptedAttacks.ConsumeClaim(0, missileClaim) && shooter.ModAmmo.Missiles < missilesBefore,
                "native fallback emission and rescue share one authority ammo payment");
            var wrongSplash = missileClaim; wrongSplash.Flags = 0;
            Check(!NetAcceptedAttacks.ValidateClaim(0, wrongSplash), "direct native component cannot produce same-victim splash rescue");
            int paidAmmo = shooter.ModAmmo.Missiles;
            NetAcceptedAttacks.ConsumeClaim(0, missileClaim);
            Check(shooter.ModAmmo.Missiles == paidAmmo, "direct/splash outcomes do not charge ammo twice");
            ResetEvidence();
            SetFrame(now + 200); var charged = Carrier(now + 200, 1, now + 200, BeamType.Missile);
            charged.FireEvents[0] = charged.FireEvents[0] with { Kind = FireEventKind.ReleaseFire, Charge = 255 };
            NetSession.AcceptSlotIntent(0, charged);
            Check(!NetAcceptedAttacks.Authorized(0, 1), "full charge requires observed source hold time");
            // Multiple recovered continuous events retain their original phase
            // even after the scene clock has consumed a newer owner pulse.
            ResetEvidence(); shooter.ModArmWeapon(BeamType.ShockCoil);
            uint coilFrame = now + 300; SetFrame(coilFrame); NetUnlagged.Record(coilFrame);
            var coil = Carrier(coilFrame, 1, coilFrame, BeamType.ShockCoil);
            coil.Buttons = IntentButtons.Shoot; coil.HasContinuousFireTick = true; coil.ContinuousFireTick = 101;
            coil.FireEvents[0] = coil.FireEvents[0] with { Kind = FireEventKind.ContinuousTick, ContinuousPhase = 101 };
            NetSession.AcceptSlotIntent(0, coil);
            SetFrame(coilFrame + 1);
            var laterCoil = Carrier(coilFrame + 1, 2, coilFrame + 1, BeamType.ShockCoil);
            laterCoil.Buttons = IntentButtons.Shoot; laterCoil.HasContinuousFireTick = true; laterCoil.ContinuousFireTick = 102;
            laterCoil.FireEvents[0] = laterCoil.FireEvents[0] with { Kind = FireEventKind.ContinuousTick, ContinuousPhase = 102 };
            NetSession.AcceptSlotIntent(0, laterCoil);
            shooter.OwningScene.WeaponPhase.Resolve(0, coilFrame + 1, true, false, coilFrame + 1, true, coilFrame + 1, 0, 102, out _, out _);
            FireEvent oldPulse = coil.FireEvents[0];
            NetTargetIdentity pending = NetTargetIdentity.ForSlot(1); shooter.ModSetPendingHomingTarget(pending);
            using (var accepted = NetFireEvents.BeginAcceptedEmission(shooter, oldPulse, default))
            {
                Check(NetFireEvents.TryAcceptedContinuousPhase(shooter, out ulong phase, out bool fresh) && phase == 101 && fresh,
                    "recovered old continuous phase is fresh once independent of newer clock");
                Check(NetFireEvents.TryAcceptedContinuousPhase(shooter, out phase, out fresh) && !fresh,
                    "same scoped pulse cannot pay or damage twice");
            }
            Check(shooter.ModConsumePendingHomingTarget() == pending && !NetFireEvents.TryAcceptedContinuousPhase(shooter, out _, out _),
                "accepted emission disposal restores target and ends exact-phase override");
            shooter.ModSetPendingHomingTarget(pending);
            try { using var failedScope = NetFireEvents.BeginAcceptedEmission(shooter, oldPulse, default);
                throw new InvalidOperationException("benign native fixture failure"); }
            catch (InvalidOperationException) { }
            Check(shooter.ModConsumePendingHomingTarget() == pending && !NetFireEvents.TryAcceptedContinuousPhase(shooter, out _, out _),
                "failed native emission restores target and ends exact-phase override");
            long pathsBefore = NetAttackPaths.Recorded; NativeWitness();
            Check(NetAttackPaths.Recorded >= pathsBefore + 2, "both deferred continuous native pulses execute before pooled replacement");
            // Legitimate delivery reordering through production claim admission:
            // the hit arrives one owner frame before its repeated successful fire.
            NetHitClaims.Reset(); ResetEvidence();
            shooter.ModArmWeapon(BeamType.PowerBeam); shooter.Health = 99; victim.Health = 99;
            typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(victim, (ushort)0);
            uint baseline = now + 400; SetFrame(baseline); NetUnlagged.Record(baseline);
            var deferredClaim = new HitClaimPacket
            {
                ClaimId = 1, MatchId = 1, AuthorityEpoch = 1,
                ShooterGeneration = NetPlayerLifecycle.Generation(0), ShooterLifeId = NetPlayerLifecycle.Get(0),
                VictimSlot = 1, VictimGeneration = NetPlayerLifecycle.Generation(1), VictimLifeId = NetPlayerLifecycle.Get(1),
                Beam = (byte)BeamType.PowerBeam, ShotId = 7, Damage = 1,
                AckFrame = baseline, LaunchFrame = baseline, Frame = baseline,
                HitPoint = victim.Position, Flags = HitClaimPacket.FlagDirect
            };
            byte[] wire = new byte[1 + HitClaimPacket.Size]; wire[0] = 1; deferredClaim.Write(wire.AsSpan(1));
            int previousPing = NetSession.SlotPing[0]; NetSession.SlotPing[0] = 1000;
            NetHitClaims.Receive(0, wire);
            Check(NetHitClaims.ClaimsPendingCurrent == 1 && victim.Health == 99,
                "claim-before-intent remains pending during bounded grace");
            SetFrame(baseline + 1); var arrived = Carrier(baseline + 1, 7, baseline, BeamType.PowerBeam);
            arrived.AckFrame = baseline;
            arrived.FireEvents[0] = arrived.FireEvents[0] with { AckFrame = baseline };
            NetSession.AcceptSlotIntent(0, arrived);
            shooter.Health = 0;
            NativeWitness();
            for (uint i = 1; i <= NetHitClaims.MaxGraceFrames + 2; i++)
            { SetFrame(baseline + i); NetUnlagged.Record(baseline + i); NetHitClaims.Tick(); }
            NetSession.SlotPing[0] = previousPing;
            Check(NetHitClaims.AppliedHere == 1 && victim.Health == 98,
                "retained native witness rescues reordered pre-death claim after grace exceeds rewind budget");
            NetHitClaims.Receive(0, wire); NetHitClaims.Tick();
            Check(victim.Health == 98, "claim retry remains idempotent after terminal verdict");
            // A source-admitted bomb survives shooter death and pays once via
            // the actual native bomb pool. Victim contact uses the native sphere.
            ResetEvidence(); shooter.Health = victim.Health = 99;
            uint bombFrame = baseline + 100; SetFrame(bombFrame);
            Vector3 bombSource = shooter.Position;
            float contactOffset = PlayerEntity.PlayerVolumes[(int)victim.Hunter, 0].SpherePosition.Y;
            victim.ModPlaceAt(bombSource.AddY(Fixed.ToFloat(-1000) - contactOffset));
            NetUnlagged.Record(bombFrame);
            var bombIntent = Carrier(bombFrame, 0, bombFrame, BeamType.PowerBeam);
            bombIntent.HasFireEvents = false; bombIntent.FireEventCount = 0;
            bombIntent.Buttons = IntentButtons.AltFormState | IntentButtons.AltAttack;
            int bombsBefore = shooter.ModBombAmmo;
            Check(NetSession.AcceptSlotIntent(0, bombIntent), "bomb alt source admitted");
            shooter.Health = 0; NetAcceptedAttacks.EmitPending(); NetAcceptedAttacks.AfterNativePickups(bombFrame);
            Check(shooter.ModBombAmmo == bombsBefore - 1, "native deferred bomb pays one authority-owned resource after death");
            var bombClaim = deferredClaim; bombClaim.ClaimId = 20; bombClaim.ShotId = 0;
            bombClaim.Beam = HitClaimPacket.NoBeam; bombClaim.LaunchFrame = 0; bombClaim.Flags = 0; bombClaim.Damage = 1;
            bombClaim.Frame = bombClaim.AckFrame = bombFrame; bombClaim.HitPoint = victim.Position;
            Check(NetAcceptedAttacks.ValidateClaim(0, bombClaim), "native bomb sphere supports producer-shaped historical claim");
            SetFrame(bombFrame + 1);
            BombEntity? nativeBomb = null;
            foreach (var candidateBomb in shooter.OwningScene.GetBombEntities()) if (candidateBomb.Owner == shooter) nativeBomb = candidateBomb;
            Check(nativeBomb != null, "deferred placement owns a real native pooled bomb");
            victim.ModPlaceAt(victim.Position + Vector3.UnitX * (nativeBomb!.Radius + .1f)); NetUnlagged.Record(bombFrame + 1);
            var outsideBomb = bombClaim; outsideBomb.Frame = outsideBomb.AckFrame = bombFrame + 1; outsideBomb.HitPoint = victim.Position;
            Check(!NetAcceptedAttacks.ValidateClaim(0, outsideBomb), "old radius-plus-two envelope cannot authorize outside native bomb sphere");
            Check(NetAcceptedAttacks.ConsumeClaim(0, bombClaim) && !NetAcceptedAttacks.ConsumeClaim(0, bombClaim),
                "native bomb result pays one anonymous victim outcome");
            NetAcceptedAttacks.EmitPending(); Check(shooter.ModBombAmmo == bombsBefore - 1, "deferred bomb does not repeat on later snapshot");
            // Alt edges recovered in a human carrier cannot mint contact.
            ResetEvidence(); shooter.Health = 99;
            uint contactFrame = bombFrame + 100; SetFrame(contactFrame);
            victim.ModPlaceAt(shooter.Position); NetUnlagged.Record(contactFrame);
            var humanEdge = Carrier(contactFrame, 0, contactFrame, BeamType.PowerBeam);
            humanEdge.FireEventCount = 0; humanEdge.HasFireEvents = false; humanEdge.Buttons = 0;
            humanEdge.Presses[0] = InputEdgeHistory.Encode(1, IntentButtons.AltAttack, 0);
            NetSession.AcceptSlotIntent(0, humanEdge);
            var contactClaim = bombClaim; contactClaim.Frame = contactClaim.AckFrame = contactFrame; contactClaim.HitPoint = victim.Position;
            Check(!NetAcceptedAttacks.ValidateClaim(0, contactClaim), "human-form recovered alt edge cannot authorize contact");
            SetFrame(contactFrame + 1); NetUnlagged.Record(contactFrame + 1);
            var boostIntent = humanEdge; boostIntent.Frame = boostIntent.AckFrame = contactFrame + 1;
            boostIntent.Buttons = IntentButtons.AltFormState; boostIntent.ShotFlags = IntentPacket.FlagBoosting;
            boostIntent.BoostDamage = 10;
            NetSession.AcceptSlotIntent(0, boostIntent);
            contactClaim.Frame = contactClaim.AckFrame = contactFrame + 1;
            Check(NetAcceptedAttacks.ValidateClaim(0, contactClaim), "accepted boost uses native contact sphere rather than body proximity");
            var excessiveBoost = contactClaim; excessiveBoost.Damage = 1000;
            Check(!NetAcceptedAttacks.ValidateClaim(0, excessiveBoost), "boost damage follows accepted source resource/rule ceiling");
            SetFrame(contactFrame + 2); victim.ModPlaceAt(victim.Position + Vector3.UnitX * 3.9f); NetUnlagged.Record(contactFrame + 2);
            var outsideContact = contactClaim; outsideContact.Frame = outsideContact.AckFrame = contactFrame + 2; outsideContact.HitPoint = victim.Position;
            Check(!NetAcceptedAttacks.ValidateClaim(0, outsideContact), "contact cannot borrow former four-unit intent envelope");
            // An actual native room obstacle trims the path witness. A
            // historical victim just behind it cannot borrow in-range damage.
            ResetEvidence(); shooter.ModArmWeapon(BeamType.PowerBeam); shooter.Health = victim.Health = 99;
            Vector3 wallOrigin = shooter.Position.AddY(.5f), wallDirection = default, behindWall = default;
            foreach (Vector3 direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
            {
                CollisionResult worldContact = default;
                Vector3 endpoint = wallOrigin + direction * 30;
                if (CollisionDetection.CheckBetweenPoints(wallOrigin, endpoint, TestFlags.Beams, shooter.OwningScene, ref worldContact)
                    && worldContact.Distance > .01f && worldContact.Distance < .9f)
                { wallDirection = direction; behindWall = worldContact.Position + direction; break; }
            }
            Check(wallDirection != default, "asset-backed room offers native obstruction fixture");
            victim.ModPlaceAt(behindWall.AddY(-.5f));
            uint wallFrame = contactFrame + 50; SetFrame(wallFrame); NetUnlagged.Record(wallFrame);
            var wallCarrier = Carrier(wallFrame, 1, wallFrame, BeamType.PowerBeam);
            wallCarrier.FireEvents[0] = wallCarrier.FireEvents[0] with { Origin = wallOrigin, Direction = wallDirection, Aim = wallDirection, View = wallDirection };
            NetSession.AcceptSlotIntent(0, wallCarrier); victim.Health = 0;
            long wallPaths = NetAttackPaths.Recorded; NetAcceptedAttacks.EmitPending();
            BeamProjectileEntity? wallBeam = null;
            foreach (var candidate in shooter.EquipInfo.Beams) if (candidate.ModShotId == 1 && candidate.Beam == BeamType.PowerBeam) wallBeam = candidate;
            Check(wallBeam != null, "obstruction fixture emitted native projectile");
            for (int i = 0; i < 100 && !wallBeam!.Flags.TestFlag(BeamFlags.Collided) && wallBeam.Lifespan > 0; i++) wallBeam.Process();
            victim.Health = 99;
            Check(NetAttackPaths.Recorded > wallPaths && wallBeam!.Flags.TestFlag(BeamFlags.Collided), "native engine records real collision-trimmed path");
            var throughWall = claim; throughWall.AckFrame = throughWall.LaunchFrame = wallFrame;
            throughWall.Frame = wallFrame + Math.Max(0u, wallBeam!.ModClaimTravelFrames - 1); throughWall.HitPoint = victim.Position;
            Check(!NetAcceptedAttacks.ValidateClaim(0, throughWall), "in-range historical victim beyond native wall boundary receives no rescue");
            // Imperialist must hit the declared owner/turret body. The wire
            // point remains owner Position for both kinds of native prediction.
            ResetEvidence(); shooter.ModArmWeapon(BeamType.Imperialist);
            Hunter previousHunter = victim.Hunter; var previousValues = victim.Values;
            var turretField = typeof(PlayerEntity).GetField("_halfturret", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? previousTurret = turretField.GetValue(victim);
            PlayerFlags2 previousFlags = victim.Flags2;
            try
            {
                victim.Hunter = Hunter.Weavel;
                typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Values))!.SetValue(victim, Metadata.PlayerValues[(int)Hunter.Weavel]);
                var turret = new HalfturretEntity(victim, shooter.OwningScene) { Health = 40 };
                turretField.SetValue(victim, turret);
                typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!.SetValue(victim, previousFlags | PlayerFlags2.Halfturret);
                victim.ModPlaceAt(NativeMuzzle(Vector3.UnitZ) + Vector3.UnitZ
                    - Vector3.UnitY * (Fixed.ToFloat(victim.Values.MinPickupHeight) + .05f));
                turret.Reposition(victim.Position + Vector3.UnitY * 2, victim.NodeRef);
                uint bodyFrame = contactFrame + 100; SetFrame(bodyFrame); NetUnlagged.Record(bodyFrame);
                var ownerRay = Carrier(bodyFrame, 1, bodyFrame, BeamType.Imperialist); NetSession.AcceptSlotIntent(0, ownerRay); NativeWitness();
                var bodyClaim = claim; bodyClaim.Beam = (byte)BeamType.Imperialist; bodyClaim.ShotId = 1;
                bodyClaim.Frame = bodyClaim.AckFrame = bodyClaim.LaunchFrame = bodyFrame; bodyClaim.HitPoint = victim.Position;
                Check(NetAcceptedAttacks.ValidateClaim(0, bodyClaim), "Imperialist owner ray supports declared body");
                var turretFlag = bodyClaim; turretFlag.Flags |= HitClaimPacket.FlagHalfturret;
                Check(!NetAcceptedAttacks.ValidateClaim(0, turretFlag), "off-ray turret cannot borrow owner ray intersection");
                PlaceNativeMuzzle(turret.Position - Vector3.UnitZ);
                SetFrame(bodyFrame + 300); NetUnlagged.Record(bodyFrame + 300);
                var turretRay = Carrier(bodyFrame + 300, 2, bodyFrame + 300, BeamType.Imperialist);
                NetSession.AcceptSlotIntent(0, turretRay); NativeWitness();
                Check(NetAcceptedAttacks.Authorized(0, 2), "second native Imperialist source admitted after legal cadence");
                turretFlag.ShotId = 2; turretFlag.Frame = turretFlag.AckFrame = turretFlag.LaunchFrame = bodyFrame + 300;
                Check(NetAcceptedAttacks.ValidateClaim(0, turretFlag), "producer-shaped owner Position still permits independently proved turret hit");
                var wrongOwner = turretFlag; wrongOwner.Flags &= unchecked((byte)~HitClaimPacket.FlagHalfturret);
                Check(!NetAcceptedAttacks.ValidateClaim(0, wrongOwner), "off-ray owner cannot borrow turret ray intersection");
                PlaceNativeMuzzle(victim.Position.AddY(Fixed.ToFloat(victim.Values.MaxPickupHeight) - .1f) - Vector3.UnitZ);
                SetFrame(bodyFrame + 600); NetUnlagged.Record(bodyFrame + 600);
                var headRay = Carrier(bodyFrame + 600, 3, bodyFrame + 600, BeamType.Imperialist);
                NetSession.AcceptSlotIntent(0, headRay); NativeWitness();
                var headClaim = bodyClaim; headClaim.ShotId = 3; headClaim.Flags |= HitClaimPacket.FlagHeadshot;
                headClaim.Frame = headClaim.AckFrame = headClaim.LaunchFrame = bodyFrame + 600; headClaim.Damage = 200;
                bool half = Features.HalfDamageUnscoped; Features.HalfDamageUnscoped = true;
                try
                {
                    Check(!NetAcceptedAttacks.ValidateClaim(0, headClaim), "unscoped head claim cannot exceed native optional half damage");
                    headClaim.Damage = 100;
                    Check(NetAcceptedAttacks.ValidateClaim(0, headClaim), "actual unscoped head ray supports native 100-damage precision");
                    NativeWitness();
                    BeamProjectileEntity? unscoped = null;
                    foreach (var beam in shooter.EquipInfo.Beams) if (beam.ModShotId == 3) unscoped = beam;
                    Check(unscoped != null && unscoped.HeadshotDamage == shooter.EquipInfo.Weapon.HeadshotDamage / 2f
                        && unscoped.Damage == shooter.EquipInfo.Weapon.UnchargedDamage / 2f,
                        "native unscoped body and head fields match claim policy");
                    SetFrame(bodyFrame + 900); NetUnlagged.Record(bodyFrame + 900);
                    var scopedRay = Carrier(bodyFrame + 900, 4, bodyFrame + 900, BeamType.Imperialist);
                    scopedRay.FireEvents[0] = headRay.FireEvents[0] with { ShotId = 4, SourceFrame = bodyFrame + 900,
                        AckFrame = bodyFrame + 900, ContinuousPhase = FireEvent.ScopedStateBit };
                    NetSession.AcceptSlotIntent(0, scopedRay); NativeWitness();
                    headClaim.ShotId = 4; headClaim.Damage = 200; headClaim.Frame = headClaim.AckFrame = headClaim.LaunchFrame = bodyFrame + 900;
                    Check(NetAcceptedAttacks.ValidateClaim(0, headClaim), "scoped head ray supports native full precision through unscoped carrier");
                    NativeWitness(); BeamProjectileEntity? scoped = null;
                    foreach (var beam in shooter.EquipInfo.Beams) if (beam.ModShotId == 4) scoped = beam;
                    Check(scoped != null && scoped.HeadshotDamage == shooter.EquipInfo.Weapon.HeadshotDamage
                        && scoped.Damage == shooter.EquipInfo.Weapon.UnchargedDamage,
                        "native scoped body and head fields match authored source scope");
                }
                finally { Features.HalfDamageUnscoped = half; }
            }
            finally
            {
                victim.Hunter = previousHunter; typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Values))!.SetValue(victim, previousValues);
                turretField.SetValue(victim, previousTurret); typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!.SetValue(victim, previousFlags);
            }
            Console.WriteLine($"PASS: {_checks} real authority attack/resource policy assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { sim.Stop(); }
    }
    private static FireEventHistory Events(FireEvent fire) { var events = new FireEventHistory(); events[0] = fire; return events; }
}
