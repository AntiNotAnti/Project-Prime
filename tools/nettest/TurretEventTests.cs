using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class TurretEventTests
{
    private static int _checks;
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new InvalidOperationException(name);
        _checks++; Console.WriteLine($"TURRET PASS {name}");
    }
    public static int Run(string room)
    {
        _checks = 0;
        Check(NetFireEvents.TurretRecoveryElapsed(100, 164, false), "full authority recovery interval permits unavailable proof");
        Check(!NetFireEvents.TurretRecoveryElapsed(100, 163, false), "partial authority interval cannot classify unavailable");
        Check(!NetFireEvents.TurretRecoveryElapsed(100, 200, true), "any earlier live availability prevents unavailable classification");
        Check(!NetFireEvents.TurretRecoveryElapsed(100, 99, false), "reordered authority clock cannot classify unavailable");
        Check(NetFireEvents.TurretRecoveryElapsed(uint.MaxValue - 31, 32, false), "authority recovery interval handles wrap");
        var current = new ShotKey(8, 7, 1, 2, 3, 0); var expired = current with { ShotId = 4 };
        var unavailable = new PlayerFormState { Frame = 1000, StartedFrame = 950, Phase = FormPhase.Alt };
        Check(NetFireEvents.ExpiredTurretUnavailable(expired, current, unavailable), "expiry classifies exact settled unavailable turret");
        var pendingMorph = unavailable; pendingMorph.Phase = FormPhase.Morphing;
        Check(!NetFireEvents.ExpiredTurretUnavailable(expired, current, pendingMorph), "expiry cannot retire pending morph creation");
        var live = unavailable; live.TurretActive = true; live.TurretHealth = 1;
        Check(!NetFireEvents.ExpiredTurretUnavailable(expired, current, live), "expiry does not bypass live turret targeting or cadence");
        live.TurretActive = false;
        Check(!NetFireEvents.ExpiredTurretUnavailable(expired, current, live), "positive turret health cannot prove retirement");
        foreach (var wrong in new[] { expired with { LifeId = 4 }, expired with { Generation = 3 },
            expired with { AuthorityEpoch = 9 }, expired with { MatchId = 8 }, expired with { ShooterSlot = 0 } })
            Check(!NetFireEvents.ExpiredTurretUnavailable(wrong, current, unavailable), "unavailable terminal preserves complete owner identity");
        Check(!NetFireEvents.ExpiredTurretUnavailable(current, current, unavailable), "missing event sequence cannot produce terminal proof");
        if (!ServerSim.Available(out string reason)) { Console.Error.WriteLine(reason); return 1; }
        var sim = new ServerSim();
        if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { })) return 1;
        bool acceptedPlayback = NetFireEvents.AcceptedPlaybackEnabled;
        try
        {
            NetFireEvents.AcceptedPlaybackEnabled = false; // Keep legacy selection policy as an explicit control.
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create();
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte i = 0; i < 2; i++) { roster.Slots[i] = i; roster.Generations[i] = 1;
                roster.Hunters[i] = (byte)(i == 0 ? Hunter.Weavel : Hunter.Samus); roster.Names[i] = $"TURRET{i}"; }
            NetSession.ApplyRoster(roster);
            for (int i = 0; i < 120; i++) sim.Step();
            var owner = PlayerEntity.Players[0];
            var scene = owner.OwningScene;
            owner.Health = 199; owner.ModStartFormSwitch(); owner.ModForceForm(true);
            Check(owner.Hunter == Hunter.Weavel && owner.IsAltForm
                && owner.Flags2.TestFlag(PlayerFlags2.Halfturret), "real Weavel native transition creates turret source");
            owner.ModArmWeapon(BeamType.PowerBeam);
            NetSession.RemoteIntentValid[0] = false; NetFireEvents.Reset();
            var turret = owner.Halfturret;
            turret.EquipInfo.Weapon = scene.WeaponRules[(int)BeamType.Battlehammer];
            turret.EquipInfo.Beams = owner.EquipInfo.Beams;
            turret.EquipInfo.InfiniteAmmo = true;
            foreach (var beam in owner.EquipInfo.Beams) beam.Lifespan = 0;
            Check(BeamProjectileEntity.Spawn(turret, turret.EquipInfo, owner.Position + Vector3.UnitY,
                Vector3.UnitY, BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene) != BeamResultFlags.NoSpawn,
                "actual turret author spawn succeeds");
            var authored = new IntentPacket { Frame = NetSession.NetFrame }; NetFireEvents.Fill(ref authored, 0);
            Check(authored.FireEventCount == 1 && authored.FireEvents[0].Kind == FireEventKind.TurretFire
                && authored.FireEvents[0].Weapon == (byte)BeamType.Battlehammer
                && owner.CurrentWeapon == BeamType.PowerBeam,
                "turret event captures Battlehammer equipment while owner selects Power Beam");
            var fire = authored.FireEvents[0];
            NetFireEvents.Reset();
            var carrier = new IntentPacket { MatchId = NetSession.CurrentMatchId,
                AuthorityEpoch = NetSession.AuthorityEpoch, SlotGeneration = NetPlayerLifecycle.Generation(0),
                LifeId = NetPlayerLifecycle.Get(0), Frame = NetSession.NetFrame, AckFrame = NetSession.NetFrame,
                HasFireEvents = true, FireEventCount = 1 };
            carrier.FireEvents[0] = fire;
            NetSession.RemoteIntents[0] = carrier; NetSession.RemoteIntentValid[0] = true;
            Check(NetFireEvents.Accept(0, carrier), "remote turret event admitted");
            NetFireEvents.Prepare(owner, carrier);
            Check(NetFireEvents.CanFireTurret(owner) && !NetFireEvents.HasPending(owner)
                && NetFireEvents.CaptureLedger(0).Accepted == 0,
                "turret policy stays distinct from discrete ledger and player trigger");
            foreach (var beam in owner.EquipInfo.Beams) beam.Lifespan = 0;
            Check(BeamProjectileEntity.Spawn(turret, turret.EquipInfo, owner.Position + Vector3.UnitY,
                Vector3.UnitY, BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene) != BeamResultFlags.NoSpawn,
                "selected remote turret event spawns through actual owner");
            Check(!NetFireEvents.CanFireTurret(owner), "successful turret commit consumes selected event");
            Check(NetFireEvents.Accept(0, carrier), "duplicate turret carrier accepted as history");
            NetFireEvents.Prepare(owner, carrier);
            Check(!NetFireEvents.CanFireTurret(owner)
                && BeamProjectileEntity.Spawn(turret, turret.EquipInfo, owner.Position + Vector3.UnitY,
                    Vector3.UnitY, BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene) == BeamResultFlags.NoSpawn,
                "duplicate turret event cannot emit a second projectile");
            NetFireEvents.AcceptedPlaybackEnabled = true;
            carrier.FireEventCount = 2;
            carrier.FireEvents[0] = fire with { ShotId = fire.ShotId + 1 };
            carrier.FireEvents[1] = fire with { ShotId = fire.ShotId + 2, Kind = FireEventKind.PressFire,
                Weapon = (byte)BeamType.PowerBeam };
            Check(NetFireEvents.Accept(0, carrier), "pending turret and discrete event admitted together");
            NetFireEvents.Prepare(owner, carrier);
            var mixedLedger = NetFireEvents.CaptureLedger(0);
            Check(NetFireEvents.CanFireTurret(owner) && mixedLedger.Pending == 1 && mixedLedger.Accepted == 1
                && mixedLedger.TryPeek(out var discrete) && discrete.Fire.Kind == FireEventKind.PressFire,
                "authority turret retains native selection while discrete event remains retained");
            owner.ExitAltForm(); owner.ModForceForm(false); scene.RemoveEntity(turret);
            Check(CountEntity(scene, turret) == 0, "fixture retires native turret before next-life spawn");
            owner.Spawn(owner.Position, Vector3.UnitZ, Vector3.UnitY, owner.NodeRef, respawn: true);
            var ledger = NetFireEvents.CaptureLedger(0);
            Check(ledger.Pending == 1 && !NetFireEvents.CanFireTurret(owner),
                "respawn clears selected turret policy and preserves discrete event");
            carrier.LifeId = NetPlayerLifecycle.Get(0); carrier.FireEventCount = 0;
            NetSession.RemoteIntents[0] = carrier; NetSession.RemoteIntentValid[0] = true;
            NetFireEvents.Prepare(owner, carrier);
            Check(!NetFireEvents.CanFireTurret(owner), "old-life pending turret cannot reselect after respawn");
            AcceptedAfterUnmorph(owner);
            RetiredTurretClaims(owner);
            RetiredRegistryBounds(owner);
            Check(sim.StepFailures == 0, "turret fixture has no engine step failures");
            Console.WriteLine($"PASS: {_checks} turret event assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"TURRET FAIL {ex}"); return 1; }
        finally { NetFireEvents.Reset(); NetFireEvents.AcceptedPlaybackEnabled = acceptedPlayback;
            NetSession.RemoteIntentValid[0] = false; sim.Stop(); }
    }

    private static void AcceptedAfterUnmorph(PlayerEntity owner)
    {
        var scene = owner.OwningScene;
        var turret = owner.Halfturret;
        NetFireEvents.AcceptedPlaybackEnabled = false;
        NetSession.RemoteIntentValid[owner.SlotIndex] = false; NetFireEvents.Reset();
        owner.Health = 199; owner.ModStartFormSwitch(); owner.ModForceForm(true);
        owner.ModArmWeapon(BeamType.PowerBeam);
        foreach (var beam in owner.EquipInfo.Beams) beam.Lifespan = 0;
        Vector3 origin = turret.Position + Vector3.UnitY;
        for (int i = 0; i < 2; i++)
            Check(BeamProjectileEntity.Spawn(turret, turret.EquipInfo, origin, Vector3.UnitY,
                BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene) != BeamResultFlags.NoSpawn,
                $"native turret equipment authors delayed Battlehammer shot {i + 1}");
        var authored = new IntentPacket { Frame = NetSession.NetFrame };
        NetFireEvents.Fill(ref authored, owner.SlotIndex);
        Check(authored.FireEventCount == 2 && authored.FireEvents[0].Kind == FireEventKind.TurretFire
            && authored.FireEvents[1].Kind == FireEventKind.TurretFire
            && authored.FireEvents[0].HasPose && authored.FireEvents[1].HasPose,
            "native spawns produce two posed turret events");
        var fire = authored.FireEvents[0]; var oldLifeFire = authored.FireEvents[1];
        foreach (var beam in owner.EquipInfo.Beams) beam.Lifespan = 0;
        NetFireEvents.Reset(); NetFireEvents.AcceptedPlaybackEnabled = true;
        var carrier = new IntentPacket { MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
            SlotGeneration = NetPlayerLifecycle.Generation(owner.SlotIndex), LifeId = NetPlayerLifecycle.Get(owner.SlotIndex),
            Frame = NetSession.NetFrame, AckFrame = NetSession.NetFrame, HasFireEvents = true, FireEventCount = 1 };
        carrier.FireEvents[0] = fire;
        owner.ExitAltForm(); owner.ModForceForm(false);
        // Retire the inert entity without stepping the client playback queue first.
        scene.RemoveEntity(turret);
        owner.TimeSinceShot = 17;
        int health = owner.Health; var form = owner.ModCaptureForm(); BeamType weapon = owner.CurrentWeapon;
        EntityBase? target = turret.Target;
        var cooldown = typeof(HalfturretEntity).GetField("_cooldownTimer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        object? cooldownBefore = cooldown.GetValue(turret);
        bool OwnerUnchanged() => owner.Health == health && owner.ModCaptureForm().Equals(form)
            && owner.CurrentWeapon == weapon && owner.TimeSinceShot == 17 && ReferenceEquals(turret.Target, target)
            && Equals(cooldownBefore, cooldown.GetValue(turret)) && !ContainsEntity(scene, turret);
        Check(NetSession.IsAuthority, "helper refusal control runs in actual authority role");
        Check(owner.ModSpawnAcceptedTurret(fire) == BeamResultFlags.NoSpawn,
            "authority cannot bypass native turret gates through accepted observer helper");
        Check(owner.EquipInfo.Beams.Count(beam => beam.Lifespan > 0) == 0,
            "refused authority helper creates no projectile");
        Console.WriteLine($"TURRET authority refusal state health={owner.Health == health} form={owner.ModCaptureForm().Equals(form)} "
            + $"weapon={owner.CurrentWeapon == weapon} shotTimer={owner.TimeSinceShot == 17} target={ReferenceEquals(turret.Target, target)} "
            + $"cooldown={Equals(cooldownBefore, cooldown.GetValue(turret))} retiredReferences={CountEntity(scene, turret)}");
        Check(OwnerUnchanged(), "refused authority helper preserves owner and retired turret state");
        NetRole role = NetSession.Role; bool authority = NetSession.IsAuthority; int local = NetSession.LocalSlot;
        void Session(string property, object value) => typeof(NetSession).GetProperty(property)!.SetValue(null, value);
        try
        {
            Session(nameof(NetSession.Role), NetRole.Client);
            Session(nameof(NetSession.IsAuthority), false); Session(nameof(NetSession.LocalSlot), 1);
            NetSession.RemoteIntents[owner.SlotIndex] = carrier; NetSession.RemoteIntentValid[owner.SlotIndex] = true;
            var key = new ShotKey(carrier.AuthorityEpoch, carrier.MatchId, owner.SlotIndex,
                carrier.SlotGeneration, carrier.LifeId, fire.ShotId);
            Check(NetFireEvents.Accept(owner.SlotIndex, carrier)
                && NetFireEvents.CaptureLedger(owner.SlotIndex).Pending == 1
                && NetFireEvents.CaptureLedger(owner.SlotIndex).TryGet(key, out var accepted, out var disposition, out _)
                && accepted.Fire == fire && disposition == ShotDisposition.Pending,
                "observer turret event enters full-key accepted ledger after unmorph");
            NetFireEvents.Prepare(owner, carrier);
            Check(!NetFireEvents.CanFireTurret(owner), "observer ledger does not select native target or cooldown policy");
            NetFireEvents.DrainAccepted(owner);
            var emitted = owner.EquipInfo.Beams.Where(beam => beam.Lifespan > 0).ToArray();
            Check(emitted.Length == 1 && emitted[0].ModLaunchKey == key && emitted[0].ModShotId == fire.ShotId
                && emitted[0].Beam == BeamType.Battlehammer && ReferenceEquals(emitted[0].Owner, turret),
                "observer emits exactly one full-key Battlehammer projectile from retained halfturret source");
            Check(emitted[0].SpawnPosition == fire.Origin && emitted[0].Direction == fire.Direction,
                "observer turret projectile retains authored origin and direction after unmorph");
            Check(OwnerUnchanged(), "observer playback preserves body health form weapon target cadence and absent turret");
            Check(NetFireEvents.Accept(owner.SlotIndex, carrier), "repeated observer turret carrier is valid history");
            NetFireEvents.DrainAccepted(owner);
            Check(owner.EquipInfo.Beams.Count(beam => beam.Lifespan > 0) == 1,
                "duplicate observer turret event cannot emit another projectile");

            // Author a real next-life spawn, then resume the observer role before playback.
            Session(nameof(NetSession.IsAuthority), true);
            owner.Spawn(owner.Position, Vector3.UnitZ, Vector3.UnitY, owner.NodeRef, respawn: true);
            Session(nameof(NetSession.IsAuthority), false);
            owner.ModArmWeapon(BeamType.PowerBeam); owner.TimeSinceShot = 17;
            health = owner.Health; form = owner.ModCaptureForm(); weapon = owner.CurrentWeapon;
            target = turret.Target; cooldownBefore = cooldown.GetValue(turret);
            var oldLife = carrier;
            oldLife.LifeId = NetPlayerLifecycle.Get(owner.SlotIndex);
            oldLife.FireEvents[0] = oldLifeFire;
            NetSession.RemoteIntents[owner.SlotIndex] = oldLife;
            NetSession.RemoteIntentValid[owner.SlotIndex] = true;
            Check(oldLifeFire.SourceLife != oldLife.LifeId && NetFireEvents.Accept(owner.SlotIndex, oldLife)
                && NetFireEvents.CaptureLedger(owner.SlotIndex).Pending == 1,
                "actual prior-life turret event remains in observer accepted ledger after body respawn");
            NetFireEvents.DrainAccepted(owner);
            emitted = owner.EquipInfo.Beams.Where(beam => beam.Lifespan > 0).ToArray();
            Check(emitted.Length == 2 && emitted.Count(beam => beam.ModLaunchKey ==
                (key with { ShotId = oldLifeFire.ShotId }) && beam.ModLaunchKey.LifeId == oldLifeFire.SourceLife
                && ReferenceEquals(beam.Owner, turret) && beam.SpawnPosition == oldLifeFire.Origin
                && beam.Direction == oldLifeFire.Direction) == 1,
                "observer prior-life turret event emits distinct exact historical key and pose");
            Check(OwnerUnchanged() && !owner.IsAltForm,
                "prior-life observer playback preserves new biped body and never resurrects turret");
            Check(NetFireEvents.Accept(owner.SlotIndex, oldLife), "duplicate prior-life observer carrier is valid history");
            NetFireEvents.DrainAccepted(owner);
            Check(owner.EquipInfo.Beams.Count(beam => beam.Lifespan > 0) == 2 && OwnerUnchanged(),
                "duplicate prior-life turret playback preserves exact projectile count and new body state");
        }
        finally
        {
            Session(nameof(NetSession.Role), role); Session(nameof(NetSession.IsAuthority), authority);
            Session(nameof(NetSession.LocalSlot), local);
        }
    }

    // Client authoring and authority playback are isolated fixture worlds. Actual native
    // turret Spawn supplies the offer; only the authoring-world projectile is discarded.
    // Damage assertions enter Receive/Tick, native ExitAltForm/DrainAccepted and turret.Process.
    private static void RetiredTurretClaims(PlayerEntity owner)
    {
        var victim = PlayerEntity.Players[1]; var scene = owner.OwningScene;
        var savedMode = NetShotProof.Mode; var savedSink = NetHitClaims.CombatAckSink;
        var answers = new Dictionary<ushort, CombatAckEntry>();
        ushort nextClaim = 100;
        void Frame(uint frame) => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, frame);
        void Record() { NetUnlagged.Record(NetSession.NetFrame); NetDynamicGeometryHistory.RecordWorld(NetSession.NetFrame); }
        HitClaimPacket Claim(in IntentPacket carrier, uint component = 1) => new()
        {
            MatchId = carrier.MatchId, AuthorityEpoch = carrier.AuthorityEpoch,
            ShooterGeneration = carrier.SlotGeneration, ShooterLifeId = carrier.LifeId,
            VictimSlot = (byte)victim.SlotIndex, VictimGeneration = NetPlayerLifecycle.Generation(victim.SlotIndex),
            VictimLifeId = NetPlayerLifecycle.Get(victim.SlotIndex), ClaimId = ++nextClaim,
            ShotId = carrier.FireEvents[0].ShotId, ComponentId = component,
            Frame = NetSession.NetFrame, AckFrame = NetSession.NetFrame, LaunchFrame = carrier.FireEvents[0].AckFrame,
            Beam = (byte)BeamType.Battlehammer, Damage = 1, Flags = HitClaimPacket.FlagDirect,
            HitPoint = victim.Position, Direction = Vector3.Zero
        };
        void Receive(in HitClaimPacket claim)
        {
            var packet = new byte[1 + HitClaimPacket.Size]; packet[0] = 1;
            claim.Write(packet.AsSpan(1)); NetHitClaims.Receive(owner.SlotIndex, packet); NetHitClaims.Tick();
        }
        IntentPacket Offer(bool zeroSourceLife = false, bool positiveFlight = false)
        {
            foreach (var beam in owner.EquipInfo.Beams) beam.Lifespan = 0;
            if (owner.IsAltForm || owner.Flags2.TestFlag(PlayerFlags2.Halfturret)) owner.ExitAltForm();
            owner.ModForceForm(false); scene.RemoveEntity(owner.Halfturret);
            NetSession.RemoteIntentValid[owner.SlotIndex] = false; NetFireEvents.Reset(); NetHitClaims.Reset(); answers.Clear();
            Frame(NetSession.NetFrame + 300);
            owner.Health = 199; victim.Health = 999;
            foreach (var player in new[] { owner, victim })
            {
                player.ModReleaseSpawnProtection(); player.Controls.ClearAll();
                typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, (ushort)0);
                typeof(PlayerEntity).GetField("_damageInvulnTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, (ushort)0);
            }
            owner.ModStartFormSwitch(); owner.ModForceForm(true);
            var turret = owner.Halfturret;
            Check(owner.IsAltForm && turret.Health > 0 && ContainsEntity(scene, turret), "retirement regression starts with actual native turret");
            Vector3 authorOrigin = turret.Position + Vector3.UnitY * .4f, authorDirection = Vector3.UnitY;
            if (positiveFlight)
            {
                Vector3 lane = Vector3.Zero;
                Record();
                foreach (var ray in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
                    if (NetDynamicGeometryHistory.TryTraceDistanceAtFrame(scene, authorOrigin, authorOrigin + ray * 3,
                        NetSession.NetFrame, out float wall) && wall == 1) { lane = ray; break; }
                Check(lane.LengthSquared == 1, "native turret regression finds clear asset collision lane before authoring");
                victim.ModPlaceAt(authorOrigin + lane * 3 - Vector3.UnitY * .5f);
                Check(HalfturretEntity.UpdateAim(authorOrigin, victim.Position, turret.EquipInfo, out authorDirection),
                    "native turret ballistic aiming supplies positive authored launch");
            }
            NetRole role = NetSession.Role; bool authority = NetSession.IsAuthority; int local = NetSession.LocalSlot;
            void Session(string property, object value) => typeof(NetSession).GetProperty(property)!.SetValue(null, value);
            FireEvent fire;
            try
            {
                Session(nameof(NetSession.Role), NetRole.Client); Session(nameof(NetSession.IsAuthority), false);
                Session(nameof(NetSession.LocalSlot), owner.SlotIndex);
                Check(BeamProjectileEntity.Spawn(turret, turret.EquipInfo, authorOrigin,
                    authorDirection, BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene) != BeamResultFlags.NoSpawn,
                    "actual client native turret authors the offered projectile");
                var outgoing = new IntentPacket { Frame = NetSession.NetFrame }; NetFireEvents.Fill(ref outgoing, owner.SlotIndex);
                Check(outgoing.FireEventCount == 1 && outgoing.FireEvents[0].Kind == FireEventKind.TurretFire,
                    "actual client launch supplies exact turret offer");
                fire = outgoing.FireEvents[0];
                if (zeroSourceLife) fire = fire with { SourceLife = 0 };
            }
            finally { Session(nameof(NetSession.Role), role); Session(nameof(NetSession.IsAuthority), authority); Session(nameof(NetSession.LocalSlot), local); }
            foreach (var beam in owner.EquipInfo.Beams) beam.Lifespan = 0;
            NetFireEvents.Reset(); NetHitClaims.Reset(); NetFireEvents.AcceptedPlaybackEnabled = true;
            var carrier = new IntentPacket { MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
                SlotGeneration = NetPlayerLifecycle.Generation(owner.SlotIndex), LifeId = NetPlayerLifecycle.Get(owner.SlotIndex),
                Frame = NetSession.NetFrame, AckFrame = NetSession.NetFrame, HasFireEvents = true, FireEventCount = 1 };
            carrier.FireEvents[0] = fire;
            NetSession.RemoteIntents[owner.SlotIndex] = carrier; NetSession.RemoteIntentValid[owner.SlotIndex] = true;
            Check(NetFireEvents.Accept(owner.SlotIndex, carrier), "exact authored offer enters authority native turret queue");
            Record(); return carrier;
        }
        void Retire()
        {
            Frame(NetSession.NetFrame + 13);
            owner.ExitAltForm(); owner.ModForceForm(false); NetFireEvents.DrainAccepted(owner);
            scene.RemoveEntity(owner.Halfturret);
        }
        try
        {
            NetShotProof.Mode = ShotProofMode.Shadow;
            NetHitClaims.CombatAckSink = (slot, entries) => { if (slot == owner.SlotIndex) foreach (var ack in entries) answers[ack.ClaimId] = ack; };
            var carrier = Offer(); var parked = Claim(carrier);
            int health = victim.Health; ushort sequence = NetDamage.Sequence(victim.SlotIndex);
            Receive(parked);
            Check(NetHitClaims.ClaimsPendingCurrent == 1 && !answers.ContainsKey(parked.ClaimId),
                "valid historical Shadow claim parks before turret retirement");
            var key = NetShotProof.Key(owner.SlotIndex, parked);
            Check(NetFireEvents.IsPendingTurret(key), "accepted un-emitted turret full key is pending");
            foreach (var wrong in new[] { key with { LifeId = (ushort)(key.LifeId + 1) },
                key with { Generation = (ushort)(key.Generation + 1) }, key with { AuthorityEpoch = key.AuthorityEpoch + 1 },
                key with { ShotId = key.ShotId + 1 } })
                Check(!NetFireEvents.IsPendingTurret(wrong), "pending turret cannot alias another full source identity");
            Frame(NetSession.NetFrame + NetHitClaims.MaxGraceFrames + 1); NetHitClaims.Tick();
            Check(NetHitClaims.ClaimsPendingCurrent == 1 && !answers.ContainsKey(parked.ClaimId)
                && victim.Health == health && NetDamage.Sequence(victim.SlotIndex) == sequence
                && NetHitClaims.AppliedHere == 0 && NetHitClaims.RescuedDamage == 0,
                "pending native turret cannot Shadow-pay beyond normal claim grace before emission or retirement");
            Retire();
            Check(NetFireEvents.IsRetiredUnemittedTurret(key) && owner.EquipInfo.Beams.All(beam => beam.Lifespan <= 0),
                "native unmorph drains un-emitted exact offer into terminal refusal");
            NetHitClaims.Tick();
            Check(answers.TryGetValue(parked.ClaimId, out var ack) && ack.Result == HitVerdictPacket.ResultInvalidLaunch
                && ack.DamageApplied == 0 && NetHitClaims.ClaimsPendingCurrent == 0,
                "parked retired offer receives InvalidLaunch after grace");
            Check(victim.Health == health && NetDamage.Sequence(victim.SlotIndex) == sequence
                && NetHitClaims.AppliedHere == 0 && NetHitClaims.RescuedDamage == 0 && NetHitClaims.RescuedLedgerCurrent == 0
                && !NetShotProof.TryPhysicalOutcome(owner.SlotIndex, parked, out _),
                "parked un-emitted retired key creates no health damage sequence rescue or physical payment");
            foreach (var mode in new[] { ShotProofMode.Off, ShotProofMode.Shadow, ShotProofMode.Enforce })
            {
                NetShotProof.Mode = mode; Record(); var late = Claim(carrier); Receive(late);
                Check(answers.TryGetValue(late.ClaimId, out ack) && ack.Result == HitVerdictPacket.ResultInvalidLaunch
                    && ack.DamageApplied == 0 && victim.Health == health && NetDamage.Sequence(victim.SlotIndex) == sequence,
                    $"claim arriving after retirement cannot pay in {mode}");
            }
            NetShotProof.Mode = ShotProofMode.Shadow;
            carrier = Offer(); var timedOut = Claim(carrier); health = victim.Health; sequence = NetDamage.Sequence(victim.SlotIndex);
            Receive(timedOut);
            Frame(NetSession.NetFrame + 2 * NetHitClaims.MaxGraceFrames - 1); NetHitClaims.Tick();
            Check(NetHitClaims.ClaimsPendingCurrent == 1 && !answers.ContainsKey(timedOut.ClaimId)
                && victim.Health == health, "pending turret remains unpaid through last bounded arbitration frame");
            Frame(NetSession.NetFrame + 1); NetHitClaims.Tick();
            Check(answers.TryGetValue(timedOut.ClaimId, out ack) && ack.Result == HitVerdictPacket.ResultInvalidLaunch
                && ack.DamageApplied == 0 && NetHitClaims.ClaimsPendingCurrent == 0 && victim.Health == health
                && NetDamage.Sequence(victim.SlotIndex) == sequence && NetHitClaims.AppliedHere == 0
                && NetHitClaims.RescuedDamage == 0, "un-emitted pending turret refuses at exact bounded horizon without damage");
            Retire(); NetHitClaims.Tick();
            Check(victim.Health == health && NetDamage.Sequence(victim.SlotIndex) == sequence,
                "retiring source after bounded refusal cannot create a later payment");
            // Evidence is an immutable process option. Execute this fixture once per
            // environment; no reflection mutates readonly production evidence state.
            {
                bool evidence = NetCampaignEvidence.Enabled;
                carrier = Offer(); key = ShotKey.For(owner.SlotIndex, carrier.FireEvents[0].ShotId);
                Frame(NetSession.NetFrame + NetIncomingFireHistory.RecoveryFrames + 1);
                var empty = carrier; empty.Frame = NetSession.NetFrame; empty.FireEventCount = 0;
                Check(NetFireEvents.Accept(owner.SlotIndex, empty) && NetFireEvents.IsRetiredUnemittedTurret(key),
                    $"unconsumed native turret offer expiry veto is identical with evidence={evidence}");
                Record(); var expired = Claim(carrier); health = victim.Health; sequence = NetDamage.Sequence(victim.SlotIndex); Receive(expired);
                Check(answers.TryGetValue(expired.ClaimId, out ack) && ack.Result == HitVerdictPacket.ResultInvalidLaunch
                    && ack.DamageApplied == 0 && victim.Health == health && NetDamage.Sequence(victim.SlotIndex) == sequence,
                    $"expired un-emitted offer cannot pay with evidence={evidence}");
            }
            carrier = Offer(zeroSourceLife: true); key = ShotKey.For(owner.SlotIndex, carrier.FireEvents[0].ShotId);
            Retire(); Record(); var legacyLife = Claim(carrier); health = victim.Health; sequence = NetDamage.Sequence(victim.SlotIndex);
            Receive(legacyLife);
            Check(NetFireEvents.IsRetiredUnemittedTurret(key) && !NetFireEvents.IsRetiredUnemittedTurret(key with { LifeId = 0 })
                && answers.TryGetValue(legacyLife.ClaimId, out ack) && ack.Result == HitVerdictPacket.ResultInvalidLaunch
                && victim.Health == health && NetDamage.Sequence(victim.SlotIndex) == sequence,
                "legacy zero source-life offer retires exact actual current life and cannot pay");
            carrier = Offer(); key = ShotKey.For(owner.SlotIndex, carrier.FireEvents[0].ShotId);
            owner.TakeDamage(0, DamageFlags.Death | DamageFlags.IgnoreInvuln, null, victim);
            scene.RemoveEntity(owner.Halfturret); owner.ModForceForm(false);
            owner.Spawn(owner.Position, Vector3.UnitZ, Vector3.UnitY, owner.NodeRef, respawn: true);
            Check(NetPlayerLifecycle.Get(owner.SlotIndex) != key.LifeId && NetFireEvents.IsRetiredUnemittedTurret(key),
                "native death and respawn cancel pending old-life offer without recursive queue transition");
            Check(!NetFireEvents.IsRetiredUnemittedTurret(ShotKey.For(owner.SlotIndex, key.ShotId)),
                "same numeric shot ID in actual new life is isolated from canceled old offer");
            carrier = Offer(positiveFlight: true);
            var source = carrier.FireEvents[0].Origin; Record();
            owner.TimeSinceShot = 1000; NetFireEvents.Prepare(owner, carrier);
            Check(NetFireEvents.CanFireTurret(owner), "native authority selects committed-flight control offer");
            owner.Halfturret.Process();
            key = ShotKey.For(owner.SlotIndex, carrier.FireEvents[0].ShotId);
            var emitted = owner.EquipInfo.Beams.Where(beam => beam.Lifespan > 0 && beam.ModLaunchKey == key).ToArray();
            Check(emitted.Length == 1 && ReferenceEquals(emitted[0].Owner, owner.Halfturret)
                && emitted[0].ModComponentId != 0 && !NetFireEvents.CanFireTurret(owner)
                && !NetFireEvents.IsPendingTurret(key),
                "actual native target and cooldown path commits exact turret component once");
            // A genuinely old flight has left its own body before native unmorph.
            // Advance actual projectile physics while the live turret owner is skipped;
            // do not teleport the projectile or move the victim to make a hit.
            health = victim.Health;
            for (int i = 0; i < 8 && (emitted[0].Position - source).LengthSquared < 1; i++) emitted[0].Process();
            Check((emitted[0].Position - source).LengthSquared >= 1 && victim.Health == health
                && emitted[0].Lifespan > 0 && !emitted[0].Flags.TestFlag(BeamFlags.Collided),
                "actual committed turret flight leaves shooter body before unmorph and before victim payment");
            Console.WriteLine($"TURRET old flight source={source} authoredRay={carrier.FireEvents[0].Direction} "
                + $"nativeRay={emitted[0].Direction} position={emitted[0].Position} victim={victim.Position}");
            Retire();
            Check(!NetFireEvents.IsRetiredUnemittedTurret(key) && NetPlayerLifecycle.CurrentProjectile(emitted[0]),
                "unmorph preserves genuinely committed old turret flight");
            health = victim.Health;
            for (int i = 0; i < 60 && victim.Health == health && emitted[0].Lifespan > 0; i++) emitted[0].Process();
            Check(victim.Health < health, $"committed old turret flight actually collides after native unmorph "
                + $"position={emitted[0].Position} flags={emitted[0].Flags} lifespan={emitted[0].Lifespan} victim={victim.Position}");
            var committed = Claim(carrier, emitted[0].ModComponentId); Record();
            Check(NetShotProof.TryPhysicalOutcome(owner.SlotIndex, committed, out var physical) && physical.DamageApplied > 0,
                "native old flight has exact component physical payment");
            health = victim.Health; sequence = NetDamage.Sequence(victim.SlotIndex); Receive(committed);
            Check(answers.TryGetValue(committed.ClaimId, out ack) && ack.Result == HitVerdictPacket.ResultDuplicate
                && ack.ShotId == committed.ShotId && ack.DamageSequence == physical.DamageSequence
                && ack.HealthAfter == physical.HealthAfter && ack.DamageApplied == physical.DamageApplied
                && victim.Health == health && NetDamage.Sequence(victim.SlotIndex) == sequence,
                "committed old flight ACK preserves native payment without another damage application");
            Receive(committed);
            Check(answers[committed.ClaimId].Result == HitVerdictPacket.ResultDuplicate && victim.Health == health
                && NetDamage.Sequence(victim.SlotIndex) == sequence, "committed native payment receipt remains idempotent");
        }
        finally { NetHitClaims.CombatAckSink = savedSink; NetShotProof.Mode = savedMode;
            foreach (var beam in owner.EquipInfo.Beams) beam.Lifespan = 0; NetHitClaims.Reset(); NetFireEvents.Reset(); }
    }

    // Registry-only bounds are separate from the native payment regression above.
    private static void RetiredRegistryBounds(PlayerEntity owner)
    {
        uint frame = NetSession.NetFrame;
        void Frame(uint value) => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, value);
        try
        {
            NetFireEvents.Reset(); var key = ShotKey.For(owner.SlotIndex, 20000);
            NetFireEvents.RememberRetiredUnemittedTurret(key);
            Check(NetFireEvents.IsRetiredUnemittedTurret(key), "terminal registry preserves exact full key");
            foreach (var wrong in new[] { key with { AuthorityEpoch = key.AuthorityEpoch + 1 }, key with { MatchId = (ushort)(key.MatchId + 1) },
                key with { ShooterSlot = 1 }, key with { Generation = (ushort)(key.Generation + 1) },
                key with { LifeId = (ushort)(key.LifeId + 1) }, key with { ShotId = key.ShotId + 1 } })
                Check(!NetFireEvents.IsRetiredUnemittedTurret(wrong), "terminal registry cannot alias another full shot identity");
            owner.Spawn(owner.Position, Vector3.UnitZ, Vector3.UnitY, owner.NodeRef, respawn: true);
            Check(NetPlayerLifecycle.Get(owner.SlotIndex) != key.LifeId && NetFireEvents.IsRetiredUnemittedTurret(key),
                "actual native respawn retains terminal identity across life boundary");
            NetFireEvents.Reset(); Frame(uint.MaxValue - 15); key = ShotKey.For(owner.SlotIndex, 21000);
            NetFireEvents.RememberRetiredUnemittedTurret(key);
            Frame(unchecked(uint.MaxValue - 15 + (uint)NetFireEvents.TurretTerminalRetentionFrames));
            Check(NetFireEvents.IsRetiredUnemittedTurret(key), "terminal retention survives authority-frame wrap through exact horizon");
            Frame(unchecked(NetSession.NetFrame + 1));
            Check(!NetFireEvents.IsRetiredUnemittedTurret(key), "terminal retention expires only after authority horizon");
            NetFireEvents.Reset(); Frame(frame);
            for (uint i = 1; i <= 257; i++) NetFireEvents.RememberRetiredUnemittedTurret(ShotKey.For(owner.SlotIndex, i));
            key = ShotKey.For(owner.SlotIndex, 99999);
            Check(NetFireEvents.IsRetiredUnemittedTurret(key), "bounded registry exhaustion fails closed for unreceipted same-incarnation keys");
            foreach (var wrong in new[] { key with { AuthorityEpoch = key.AuthorityEpoch + 1 }, key with { MatchId = (ushort)(key.MatchId + 1) },
                key with { ShooterSlot = 1 }, key with { Generation = (ushort)(key.Generation + 1) } })
                Check(!NetFireEvents.IsRetiredUnemittedTurret(wrong), "capacity closure cannot poison another stream occupant or slot");
            Frame(frame + (uint)NetFireEvents.TurretTerminalRetentionFrames + 1);
            Check(!NetFireEvents.IsRetiredUnemittedTurret(key), "capacity closure expires at bounded authority horizon");
        }
        finally { Frame(frame); NetFireEvents.Reset(); }
    }

    private static bool ContainsEntity(Scene scene, EntityBase entity)
        => CountEntity(scene, entity) != 0;

    private static int CountEntity(Scene scene, EntityBase entity)
    {
        int count = 0;
        foreach (var candidate in scene.Entities)
            if (ReferenceEquals(candidate, entity)) count++;
        return count;
    }
}
