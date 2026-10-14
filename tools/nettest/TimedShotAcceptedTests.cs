using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Controlled asset-backed remote carrier chronology. Resource ownership and
// timed reservations are explicitly seeded fixtures; they are NOT native pickup
// witnesses. Actual lifecycle intake, accepted ledger and projectile drain run.
// No socket authentication, GL/audio or natural remote campaign is claimed.
internal static class TimedShotAcceptedTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Timed accepted shot: " + label);
        _checks++;
        Console.WriteLine("TIMED ACCEPTED PASS " + label);
    }
    private static void Frame(uint value)
        => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, value);
    private readonly record struct Produced(uint Frame, uint Tick, ReplayFactKind Kind,
        ReplayMarker? Marker, byte[] Bytes);
    private readonly record struct Outcome(ShotDisposition Disposition, float Damage, int Components,
        int AmmoBefore, int AmmoAfter, int SemanticDelta);
    private sealed record Shot(ShotKey Key, FireEvent Fire, Outcome Outcome, Produced[] Records);

    private sealed class Fixture : IDisposable
    {
        private readonly ServerSim _sim = new();
        private uint _id;
        internal readonly Scene Scene;
        internal readonly PlayerEntity Owner;
        internal readonly Vector3 Base;
        internal Vector3 Direction;
        internal Fixture(string room, ShotProofMode oldMode, TimedGrantProofMode mode)
        {
            NetShotProof.Mode = oldMode; NetTimedShotProof.Mode = mode;
            NetFireEvents.AcceptedPlaybackEnabled = true; NetTestScript.Enabled = false;
            Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
            var roster = RosterPacket.Create();
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte i = 0; i < 2; i++)
            { roster.Slots[i] = i; roster.Generations[i] = 1; roster.Hunters[i] = (byte)Hunter.Samus; roster.Names[i] = "Accepted fixture " + i; }
            try
            {
                Check(_sim.Start(room, GameMode.Battle, 2, _ => { }, () => { }, roster: roster), "asset authority starts");
                NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                    RoomKey = room, Mode = (byte)GameMode.Battle }, false);
                NetSession.ApplyRoster(roster);
                Scene = (Scene)typeof(ServerSim).GetField("_scene", Hidden)!.GetValue(_sim)!;
                Scene.GameState.MatchTime = -1; Scene.GameState.PointGoal = 10000;
                Scene.GameState.EnhancedHunters = false; Scene.GameState.BalancedMode = false;
                Scene.GameState.PrimeHunter = -1;
                foreach (var item in Scene.GetItemInstanceEntities()) item.DespawnTimer = 0;
                for (int i = 0; i < 120; i++) _sim.Step();
                Owner = Scene.Players.Items[0]; Base = Owner.Position;
                Check(!Owner.IsBot && Owner.ModIsInPlay && Owner.Health > 0 && NetSession.IsAuthority
                    && NetSession.LocalSlot == -1 && !Scene.Services.IsReplica,
                    "actual remote nonbot authority body has a genuine current life");
                Frame(1000); Record();
                foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
                    if (NetDynamicGeometryHistory.TryTraceDistanceAtFrame(Scene, Base + Vector3.UnitY * .5f,
                        Base + direction * 4 + Vector3.UnitY * .5f, NetSession.NetFrame, out float wall) && wall == 1)
                    { Direction = direction; break; }
                Check(Direction.LengthSquared == 1, "real asset world supplies a clear original muzzle lane");
            }
            catch { _sim.Stop(); NetSession.Stop(); throw; }
        }
        internal void Record()
        { NetUnlagged.Record(NetSession.NetFrame); NetDynamicGeometryHistory.RecordWorld(NetSession.NetFrame); }
        internal void Prepare(int ammo0 = 10000, int ammo1 = 10000)
        {
            foreach (var beam in Owner.EquipInfo.Beams) beam.Lifespan = 0;
            NetFireEvents.Reset(); NetHitClaims.Reset(); NetPowerUpLedger.Reset(); NetGrantNetwork.Reset();
            Frame(1000); Owner.Health = 999; Owner.ModForceForm(false); Owner.Controls.ClearAll();
            Owner.ModPlaceAt(Base); Owner.ModReleaseSpawnProtection();
            typeof(PlayerEntity).GetField("_spawnInvulnTimer", Hidden)!.SetValue(Owner, (ushort)0);
            typeof(PlayerEntity).GetField("_damageInvulnTimer", Hidden)!.SetValue(Owner, (ushort)0);
            Record();
            // Declared resource fixture, as in ShotProofTests. This is not a
            // natural inventory/pickup witness; carrier/input proof stays real.
            NetShotProof.Seed(Owner, new ShotProofSource(0x1FF, 0x1FF, ammo0, ammo1,
                false, Owner.Hunter, false, false));
            var neutral = Packet(null, 1000);
            Submit(neutral);
            Check(NetPowerUpLedger.Ledger is { } ledger && ledger.Room != 0,
                "actual A1 input adapter creates the authority room ledger");
        }
        internal IntentPacket Packet(FireEvent? fire, uint carrier, bool previousShoot = false)
        {
            var life = ShotKey.For(0, 0);
            var packet = new IntentPacket { MatchId = life.MatchId, AuthorityEpoch = life.AuthorityEpoch,
                SlotGeneration = life.Generation, LifeId = life.LifeId, Frame = carrier, AckFrame = carrier,
                Position = Owner.Position, Aim = Direction, WeaponSelect = fire?.Weapon ?? (byte)BeamType.PowerBeam,
                Buttons = IntentButtons.InPlayState };
            if (fire is { } value)
            {
                packet.HasFireEvents = true; packet.FireEventCount = 1; packet.FireEvents[0] = value;
                packet.ShotInputCount = 2;
                packet.ShotInputs[1] = ShotInputHistory.Sample((byte)BeamType.PowerBeam, previousShoot, false, true);
                packet.ShotInputs[0] = ShotInputHistory.Sample(value.Weapon, true, value.ScopedAtFire, true);
                packet.Buttons |= IntentButtons.Shoot;
                if (value.ScopedAtFire) packet.Buttons |= IntentButtons.Zoom;
            }
            return packet;
        }
        internal void Submit(in IntentPacket packet)
        {
            Check(NetSession.AcceptSlotIntent(0, packet), "actual lifecycle/event carrier intake succeeds");
            NetShotProof.ObservePose(Owner, packet);
            NetPowerUpLedger.ObserveInput(Owner);
        }
        internal FireEvent Fire(uint source, BeamType weapon = BeamType.PowerBeam, uint modifiers = 0,
            ushort? life = null, bool pose = true)
            => new(++_id, source, source, 0, FireEventKind.PressFire, (byte)weapon, 0, modifiers,
                pose ? FireEvent.FlagPose : (byte)0, Owner.Position + Vector3.UnitY * .5f,
                Direction, Direction, Direction, SourceLife: life ?? NetPlayerLifecycle.Get(0));
        internal Shot Emit(FireEvent fire, uint? carrier = null, ShotDisposition expected = ShotDisposition.Fired, bool previousShoot = false)
        {
            Frame(carrier ?? fire.SourceFrame); Record();
            foreach (var beam in Owner.EquipInfo.Beams) beam.Lifespan = 0;
            int ammo = Owner.EquipInfo.Ammo;
            int semantics = Scene.MatchEvents.Bus.Events.Count(e => e.Type == MphRead.Mods.MatchEvents.MatchSemanticEventType.WeaponFired);
            var records = new List<Produced>();
            void Capture(ReplayTimelineRecord record)
                => records.Add(new(record.RecordingFrame, record.ServerTick, record.Kind, record.Marker, record.Payload.ToArray()));
            ReplayCapture.Recorder.Accepted += Capture;
            var key = ShotKey.For(0, fire.ShotId) with { LifeId = fire.SourceLife };
            try
            {
                Submit(Packet(fire, carrier ?? fire.SourceFrame, previousShoot));
                var ledger = NetFireEvents.CaptureLedger(0);
                Check(ledger.TryGet(key, out var pending, out var disposition, out _)
                    && disposition == ShotDisposition.Pending && pending.Fire == fire,
                    "exact authored event is actually Pending before private drain");
                Check(NetTimedShotProof.AcceptedAttempt(Owner, key, fire) == null,
                    "pending identity outside the private drain is not a certificate admission");
                NetFireEvents.DrainAccepted(Owner);
                Check(ledger.TryGet(key, out var completed, out disposition, out var reason)
                    && completed.Fire == fire && disposition == expected,
                    "actual drain has expected terminal disposition " + expected + ": " + reason);
                var beams = Owner.EquipInfo.Beams.Where(b => b.ModLaunchKey == key && b.Lifespan > 0 && b.ModComponentId != 0).ToArray();
                Check(expected != ShotDisposition.Fired || beams.Length > 0,
                    "successful accepted drain produced actual native projectile components");
                return new(key, fire, new(disposition, beams.FirstOrDefault()?.Damage ?? 0, beams.Length,
                    ammo, Owner.EquipInfo.Ammo, Scene.MatchEvents.Bus.Events.Count(e => e.Type == MphRead.Mods.MatchEvents.MatchSemanticEventType.WeaponFired) - semantics), records.ToArray());
            }
            finally { ReplayCapture.Recorder.Accepted -= Capture; }
        }
        internal TimedGrantRecord SeedTimedReservation(uint contact)
        {
            Frame(contact);
            var ledger = NetPowerUpLedger.Ledger!;
            Check(ledger.RegisterSpawn(9000, TimedPowerUp.DoubleDamage, out var spawn), "declared timed reservation fixture registers");
            Check(ledger.TryGrant(spawn, ShotKey.For(0, 0), contact, contact, out var grant) == TimedGrantResult.Granted,
                "declared A1 reservation seed uses real ledger creation semantics; not a native contact claim");
            return grant!;
        }
        public void Dispose() { _sim.Stop(); NetSession.Stop(); }
    }

    private static TimedShotCertificate Certificate(Shot shot)
    {
        Check(NetTimedShotProof.TryGet(shot.Key, shot.Fire.SourceFrame, out var value)
            && value.Key == shot.Key && value.Fire == shot.Fire && value.Lane == TimedShotSource.AcceptedFire
            && value.Components == shot.Outcome.Components && value.Components > 0,
            "certificate binds actual admitted fire and complete native component terminals");
        return value;
    }
    private static void HintsAndIndependentModifiers(string room)
    {
        foreach (bool grant in new[] { false, true }) foreach (bool hint in new[] { false, true })
        {
            using var fixture = new Fixture(room, ShotProofMode.Off, TimedGrantProofMode.Shadow);
            fixture.Prepare();
            TimedGrantRecord? seeded = grant ? fixture.SeedTimedReservation(1000) : null;
            var shot = fixture.Emit(fixture.Fire(1001, modifiers: hint ? FireEvent.DoubleStateBit : 0));
            var value = Certificate(shot);
            Check(value.TrustedSource && value.Unavailable == TimedShotUnavailable.None
                && value.Knowledge == (grant ? TimedGrantKnowledge.Granted : TimedGrantKnowledge.NotGranted)
                && value.AuthorizedDouble == grant && value.DoubleHintAgrees == (hint == grant),
                "actual admitted Double hint cannot manufacture or remove A1 ownership: grant=" + grant + " hint=" + hint);
            Check(grant ? value.Funding?.Sequence == seeded!.Sequence && value.Funding?.Spawn == seeded.Spawn : value.Funding == null,
                "copied funding follows exact seeded reservation only, never owner modifier bits");
            Check(NetShotProof.Mode == ShotProofMode.Off && NetShotProof.SourceReason(shot.Key) == ShotProofReason.UnknownShot,
                "new Shadow does not enable the old shot-proof admission store");
        }
        foreach (uint wrong in new[] { FireEvent.PrimeStateBit, FireEvent.AffinityStateBit })
        {
            using var fixture = new Fixture(room, ShotProofMode.Off, TimedGrantProofMode.Shadow);
            fixture.Prepare(); fixture.SeedTimedReservation(1000);
            var value = Certificate(fixture.Emit(fixture.Fire(1001, modifiers: wrong | FireEvent.DoubleStateBit)));
            Check(value.Source.Source == ShotProofReason.ModifierMismatch && !value.TrustedSource
                && value.Unavailable == TimedShotUnavailable.Source && !value.AuthorizedDouble,
                "independent Prime/PowerBeam-affinity mismatch remains a source failure despite seeded funding");
        }
    }

    private static void BoundariesAndImmutableFunding(string room)
    {
        // Independent emission fixtures: firing at C and C+1 in one life
        // would deliberately violate the real shared source cooldown.
        using (var contactFixture = new Fixture(room, ShotProofMode.Off, TimedGrantProofMode.Shadow))
        {
            contactFixture.Prepare(); contactFixture.SeedTimedReservation(1000);
            var atContact = Certificate(contactFixture.Emit(contactFixture.Fire(1000)));
            Check(atContact.Knowledge == TimedGrantKnowledge.NotGranted && !atContact.AuthorizedDouble,
                "immutable actual contact-input observation cannot retroactively use the seeded grant");
        }
        using var fixture = new Fixture(room, ShotProofMode.Off, TimedGrantProofMode.Shadow);
        fixture.Prepare(); var first = fixture.SeedTimedReservation(1000);
        var early = fixture.Emit(fixture.Fire(1001)); var funded = Certificate(early);
        Check(funded.AuthorizedDouble && funded.Funding?.Sequence == first.Sequence, "first subsequent admitted input owns exact reservation");
        // A large clock gap exceeds certificate retention; use a late genuine
        // accepted emission to test immutable retrieval after grant expiry.
        var late = fixture.Emit(fixture.Fire(2799)); var lateValue = Certificate(late);
        Check(lateValue.AuthorizedDouble && lateValue.Funding?.Sequence == first.Sequence, "last independent authority input before expiry owns grant");
        var expired = Certificate(fixture.Emit(fixture.Fire(2800), previousShoot: true));
        Check(first.EndReason == TimedGrantEnd.Expired && expired.Knowledge == TimedGrantKnowledge.NotGranted
            && !expired.AuthorizedDouble && NetTimedShotProof.TryGet(late.Key, late.Fire.SourceFrame, out var retained)
            && retained == lateValue, "exclusive expiry never changes retained late-fire funding");
        // The adjacent 2799/2800 pair above intentionally keeps the legacy
        // emission while checking only ownership absence, not trusted cadence.
        Check(!expired.TrustedSource && expired.Source.Source == ShotProofReason.FireRate,
            "adjacent post-expiry fixture emission explicitly remains a source cooldown negative");
        var renewal = fixture.SeedTimedReservation(2801);
        uint renewedSource = 2800 + (uint)fixture.Scene.WeaponRules[(int)BeamType.PowerBeam].ShotCooldown * 2 + 1;
        var renewed = Certificate(fixture.Emit(fixture.Fire(renewedSource)));
        Console.WriteLine("RENEWAL DIAGNOSTIC " + JsonSerializer.Serialize(new { renewedSource, source = renewed.Source.Source.ToString(), pose = renewed.Source.Pose.ToString(), cooldown = renewed.Source.Cooldown, knowledge = renewed.Knowledge.ToString(), unavailable = renewed.Unavailable.ToString(), renewed.AuthorizedDouble, fundingSequence = renewed.Funding?.Sequence, renewalSequence = renewal.Sequence, found = NetTimedShotProof.TryGet(late.Key, late.Fire.SourceFrame, out retained), equal = retained == lateValue }));
        Check(renewed.AuthorizedDouble && renewed.Funding?.Sequence == renewal.Sequence
            && NetTimedShotProof.TryGet(late.Key, late.Fire.SourceFrame, out retained) && retained == lateValue,
            "later reservation has a new sequence without refilling a prior flight certificate");
    }

    private static void NonadmittedAndIncomplete(string room)
    {
        using var fixture = new Fixture(room, ShotProofMode.Off, TimedGrantProofMode.Shadow);
        fixture.Prepare(); Frame(1001); fixture.Record(); fixture.Submit(fixture.Packet(null, 1001));
        var fire = fixture.Fire(1001); var key = ShotKey.For(0, fire.ShotId);
        long stored = NetTimedShotProof.Stored;
        Check(!NetFireEvents.CaptureLedger(0).TryGet(key, out _, out _, out _), "direct helper has no actual fire admission");
        Check(NetFireEvents.PlayAccepted(fixture.Owner, key, fire) != BeamResultFlags.NoSpawn
            && NetTimedShotProof.Stored == stored && !NetTimedShotProof.TryGet(key, fire.SourceFrame, out _),
            "actual direct accepted-helper projectile cannot certify without private drain admission");
        Frame(1002); fixture.Record(); fire = fixture.Fire(1002); key = ShotKey.For(0, fire.ShotId);
        fixture.Submit(fixture.Packet(fire, 1002));
        Check(NetFireEvents.CaptureLedger(0).TryGet(key, out _, out var disposition, out _) && disposition == ShotDisposition.Pending,
            "separate direct-helper negative has a real pending ledger entry");
        Check(NetFireEvents.PlayAccepted(fixture.Owner, key, fire) != BeamResultFlags.NoSpawn
            && NetTimedShotProof.Stored == stored && !NetTimedShotProof.TryGet(key, fire.SourceFrame, out _),
            "direct helper cannot borrow pending identity as private drain eligibility");
        // Clear this deliberately undrained helper witness before the independent
        // missing-pose negative; Reset is fixture setup, not an admission bypass.
        fixture.Prepare();
        var incomplete = fixture.Emit(fixture.Fire(1001, pose: false), expected: ShotDisposition.Rejected);
        Check(NetTimedShotProof.Stored == 0 && !NetTimedShotProof.TryGet(incomplete.Key, incomplete.Fire.SourceFrame, out _),
            "actually admitted missing-pose fire is rejected before any component/certificate");
    }

    private static void UnsupportedAccounting(string room)
    {
        using var fixture = new Fixture(room, ShotProofMode.Off, TimedGrantProofMode.Shadow);
        var judicator = fixture.Scene.WeaponRules[(int)BeamType.Judicator];
        var volt = fixture.Scene.WeaponRules[(int)BeamType.VoltDriver];
        Check(judicator.AmmoType == volt.AmmoType && judicator.AmmoCost > 0 && volt.AmmoCost > 0,
            "actual asset rows share positive ammo expenditure for unsupported accounting falsifier");
        fixture.Prepare(ammo0: Math.Max(judicator.AmmoCost, volt.AmmoCost));
        var unsupported = fixture.Emit(fixture.Fire(1001, BeamType.Judicator));
        Check(!NetTimedShotProof.TryGet(unsupported.Key, unsupported.Fire.SourceFrame, out _), "admitted unsupported native Judicator emission has no ordinary certificate");
        var next = Certificate(fixture.Emit(fixture.Fire(1002 + (uint)judicator.ShotCooldown * 2, BeamType.VoltDriver)));
        Check(!next.TrustedSource && next.Source.Source == ShotProofReason.SourceChanged
            && next.Unavailable == TimedShotUnavailable.Source && !next.AuthorizedDouble,
            "unsupported admitted expenditure cannot leave later same-ammo supported fire trusted (initial0c falsifier)");
    }

    private static void ExactLifeCollision(string room)
    {
        using var fixture = new Fixture(room, ShotProofMode.Off, TimedGrantProofMode.Shadow);
        fixture.Prepare(); var old = ShotKey.For(0, 0); uint oldSource = NetSession.NetFrame;
        for (int i = 0; i < 8; i++)
        {
            Frame(NetSession.NetFrame + 1);
            fixture.Owner.Spawn(fixture.Owner.Position, Vector3.UnitZ, Vector3.UnitY, fixture.Owner.NodeRef, respawn: true);
        }
        var current = ShotKey.For(0, 0);
        Check(current.Generation == old.Generation && current.LifeId == old.LifeId + 8
            && current.LifeId % 8 == old.LifeId % 8, "actual respawns produce distinct old/current identities in the same bounded life bucket");
        NetShotProof.Seed(fixture.Owner, new ShotProofSource(0x1FF, 0x1FF, 10000, 10000, false, fixture.Owner.Hunter, false, false));
        Check(!NetShotSourceJournal.Birth(old).HasValue && NetShotSourceJournal.Birth(current).HasValue,
            "old resource bucket is genuinely evicted while current-life fixture resources are explicitly seeded");
        var taint = fixture.Emit(fixture.Fire(1009, BeamType.Judicator));
        Check(!NetTimedShotProof.TryGet(taint.Key, taint.Fire.SourceFrame, out _), "current-life unsupported emission is excluded");
        var delayed = fixture.Emit(fixture.Fire(oldSource, BeamType.Judicator, life: old.LifeId), carrier: 1010);
        Check(delayed.Key.LifeId == old.LifeId && delayed.Outcome.Disposition == ShotDisposition.Fired,
            "actual current carrier and private drain retain an admitted delayed old-life unsupported shot");
        var supported = Certificate(fixture.Emit(fixture.Fire(1040, BeamType.VoltDriver)));
        Check(supported.Key.LifeId == current.LifeId && !supported.TrustedSource
            && supported.Source.Source == ShotProofReason.SourceChanged && supported.Unavailable == TimedShotUnavailable.Source,
            "evicted old-life unsupported shot cannot overwrite newer same-bucket uncertainty (pre-d6 falsifier)");
    }

    private static Shot ParityArm(string room, ShotProofMode old, TimedGrantProofMode mode, bool spoof)
    {
        using var fixture = new Fixture(room, old, mode); fixture.Prepare();
        return fixture.Emit(fixture.Fire(1001, modifiers: spoof ? FireEvent.DoubleStateBit : 0),
            expected: old == ShotProofMode.Enforce && spoof ? ShotDisposition.Rejected : ShotDisposition.Fired);
    }
    private static void ModeAndProducerParity(string room)
    {
        foreach (var old in new[] { ShotProofMode.Off, ShotProofMode.Shadow, ShotProofMode.Enforce })
        foreach (bool spoof in new[] { false, true })
        {
            var off = ParityArm(room, old, TimedGrantProofMode.Off, spoof);
            var shadow = ParityArm(room, old, TimedGrantProofMode.Shadow, spoof);
            Check(off.Key == shadow.Key && off.Fire == shadow.Fire && off.Outcome == shadow.Outcome,
                "new Off/Shadow preserve actual legacy accepted terminal/component/damage/ammo/semantic outcome " + old + "/" + spoof);
            Check(off.Records.Length == shadow.Records.Length
                && (off.Outcome.Disposition != ShotDisposition.Fired || off.Records.Length > 0),
                "actual production recorder streams agree; successful emissions require nonempty facts, rejected emissions may be empty");
            for (int i = 0; i < off.Records.Length; i++)
            {
                var a = off.Records[i]; var b = shadow.Records[i];
                Check(a.Frame == b.Frame && a.Tick == b.Tick && a.Kind == b.Kind && a.Marker == b.Marker
                    && a.Bytes.SequenceEqual(b.Bytes), "exact production replay packet/frame/order parity without normalization " + old + "/" + spoof + "/" + i);
            }
        }
    }

    internal static int Run(string room)
    {
        var old = NetShotProof.Mode; var mode = NetTimedShotProof.Mode;
        bool accepted = NetFireEvents.AcceptedPlaybackEnabled, script = NetTestScript.Enabled;
        _checks = 0;
        try
        {
            Check(ServerSim.Available(out string reason), reason);
            HintsAndIndependentModifiers(room); BoundariesAndImmutableFunding(room);
            NonadmittedAndIncomplete(room); UnsupportedAccounting(room); ExactLifeCollision(room);
            ModeAndProducerParity(room);
            Console.WriteLine($"PASS: {_checks} controlled admitted-remote timed-shot assertions.");
            Console.WriteLine("SCOPE: actual asset authority body/lifecycle carrier/accepted ledger/private drain/projectile terminals/A1 input adapter/production replay packets. Explicit resource/reservation seeds and fixed carrier chronology. NOT_RUN: native pickup proof, authenticated UDP, GL/audio, partial-spawn exception injection, natural campaign.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("TIMED ACCEPTED FAIL " + error);
            Console.Error.WriteLine(JsonSerializer.Serialize(new { _checks, NetSession.NetFrame,
                NetTimedShotProof.Stored, NetTimedShotProof.Granted, NetTimedShotProof.NotGranted,
                NetTimedShotProof.Unavailable, NetTimedShotProof.Unsupported, NetTimedShotProof.NoEmission }));
            return 1;
        }
        finally
        {
            NetSession.Stop(); NetShotProof.Mode = old; NetTimedShotProof.Mode = mode;
            NetFireEvents.AcceptedPlaybackEnabled = accepted; NetTestScript.Enabled = script;
        }
    }
}
