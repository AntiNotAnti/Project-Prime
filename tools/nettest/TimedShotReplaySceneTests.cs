using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Buffers.Binary;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Actual accepted admission and producer callbacks; only persisted fact fidelity
// is asserted. Resource and A1 reservation seeds are declared fixture setup.
internal static class TimedShotReplaySceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Timed shot replay scene: " + label);
        _checks++;
        Console.WriteLine("TIMED REPLAY SCENE PASS " + label);
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

    private sealed class NativeFixture : IDisposable
    {
        private readonly ServerSim _sim = new();
        internal readonly Scene Scene;
        internal readonly PlayerEntity Owner, Victim;
        internal readonly Vector3 Origin, Direction;
        private uint _shot;
        internal NativeFixture(string room, TimedGrantProofMode mode)
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
                Victim.ModPlaceAt(Origin - Direction * 4); Record();
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
                Position = slot == 0 ? Origin : Origin - Direction * 4, Aim = Direction,
                WeaponSelect = (byte)BeamType.PowerBeam, Buttons = IntentButtons.InPlayState,
                HasFireEvents = true, ShotInputCount = 1 };
            packet.ShotInputs[0] = ShotInputHistory.Sample((byte)BeamType.PowerBeam, down, false, true);
            if (down) packet.Buttons |= IntentButtons.Shoot;
            if (fire is { } value) { packet.FireEventCount = 1; packet.FireEvents[0] = value; }
            return packet;
        }
        private void Submit(int slot, in IntentPacket packet)
            => Check(NetSession.AcceptSlotIntent(slot, packet), "actual lifecycle/event carrier intake");
        internal void Step(bool down = false, FireEvent? fire = null)
        {
            uint expectedFrame = unchecked(NetSession.NetFrame + 1);
            Submit(0, Packet(0, expectedFrame, down, fire)); Submit(1, Packet(1, expectedFrame));
            long before = _sim.StepFailures; _sim.Step();
            Check(_sim.StepFailures == before && NetSession.NetFrame == expectedFrame
                && Owner.ModIsInPlay && Victim.ModIsInPlay,
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
        internal FireEvent Fire()
            => new(++_shot, unchecked(NetSession.NetFrame + 1), unchecked(NetSession.NetFrame + 1),
                0, FireEventKind.PressFire, (byte)BeamType.PowerBeam, 0, FireEvent.DoubleStateBit,
                FireEvent.FlagPose, Owner.Position + Vector3.UnitY * .5f, Direction, Direction,
                Direction, SourceLife: NetPlayerLifecycle.Get(0));
        public void Dispose() { _sim.Stop(); NetSession.Stop(); }
    }

    private static void AssertFact(ReplayReplicaState state, ReplayTimedShotFact expected, string label)
    {
        Check(state.TimedShots.TryGet(expected.Key, expected.Fire.SourceFrame, expected.Room,
            expected.AuthorityFrame, out var found) && found == expected, label);
        Check(!state.ShotFacts.Any(value => value.ShotId == expected.Key.ShotId && value.ShooterSlot == expected.Key.ShooterSlot
            && value.ShooterLifeId == expected.Key.LifeId),
            "miss has no resolved damage hint and still retains independent timed fact");
    }

    private static void RecordedAdmission(string room, TimedGrantProofMode mode, bool archive, string directory)
    {
        ReplayTimedShotRecording.Enabled = archive;
        var records = new List<Produced>();
        ReplayMetadata metadata;
        ReplayTimedShotFact expected = default;
        void Capture(ReplayTimelineRecord record)
            => records.Add(new(record.RecordingFrame, record.ServerTick, record.Kind, record.Marker, record.Payload.ToArray()));
        using (var fixture = new Fixture(room, ShotProofMode.Off, mode))
        {
            fixture.Prepare();
            var reservation = fixture.SeedTimedReservation(1000);
            ReplayCapture.Recorder.Accepted += Capture;
            try
            {
                // Begin a fresh recording from the actual current authority roster/match.
                ReplayCapture.Recorder.Reset();
                var match = NetSession.ServerMatch ?? throw new InvalidOperationException("authority match missing");
                ReplayCapture.Recorder.AcceptMatch(match, NetSession.NetFrame);
                ReplayCapture.Recorder.AcceptRoster(NetSession.LobbyRoster(), NetSession.NetFrame);
                metadata = ReplayCapture.Capture(ReplayType.FullMatch, hashMap: false);
                var shot = fixture.Emit(fixture.Fire(1001));
                Check(shot.Outcome.Disposition == ShotDisposition.Fired && shot.Outcome.Components > 0,
                    "actual private accepted drain spawns native component before file capture");
                if (mode == TimedGrantProofMode.Shadow)
                    Check(NetTimedShotProof.TryGet(shot.Key, shot.Fire.SourceFrame, out _),
                        "actual Shadow shot proof completes independently of replay opt-in");
                if (archive)
                {
                    Check(metadata.FormatVersion == 6 && metadata.TimedShotCertificates,
                        "explicit replay opt-in advertises format6 certificate capability");
                    Check(NetTimedShotProof.TryGet(shot.Key, shot.Fire.SourceFrame, out var certificate),
                        "actual complete accepted shot supplies retained certificate");
                    expected = ReplayTimedShotFact.From(certificate, mode);
                    Check(expected.Fire == shot.Fire && expected.LedgerAnswer.Knowledge == TimedGrantKnowledge.Granted
                        && expected.Knowledge == TimedGrantKnowledge.Granted && expected.Funding?.Sequence == reservation.Sequence
                        && expected.Funding?.Spawn == reservation.Spawn && expected.Channels.Present
                        && expected.Channels.Body == shot.Outcome.Damage
                        && expected.AuthorityFrame == 1001 && expected.Fire.SourceFrame == 1001
                        && expected.Fire.AckFrame == 1001 && expected.Lane == TimedShotSource.AcceptedFire,
                        "recorded source clocks/raw answer/full funding/native channels bind actual admitted shot");
                    Check(records.Count(r => r.Bytes.Length > 0 && r.Bytes[0] == ReplayTimedShotScope.Tag) == 1
                        && records.Count(r => r.Bytes.Length > 0 && r.Bytes[0] == ReplayTimedShotFact.Tag) == 1,
                        "real producer emits one explicit scope and one firstStoredComplete fact");
                    var packet = records.Single(r => r.Bytes.Length > 0 && r.Bytes[0] == ReplayTimedShotFact.Tag).Bytes;
                    Check(ReplayTimedShotFact.TryRead(packet, out var produced) && produced == expected,
                        "producer callback owns exact final certificate value");
                    Check(!records.Any(r => r.Bytes.Length > 0 && r.Bytes[0] == (byte)PacketType.ReplayShotFact),
                        "actual launch-only miss emits no resolved damage hint packet");
                    // Mutating current gameplay inputs cannot refill a stored flight.
                    fixture.Owner.EquipInfo.ChargeLevel = 99;
                    fixture.Owner.EquipInfo.Weapon = fixture.Scene.WeaponRules[(int)BeamType.Missile];
                    Frame(2800); NetPowerUpLedger.ObserveInput(fixture.Owner);
                    var renewal = fixture.SeedTimedReservation(2801);
                    Check(reservation.EndReason == TimedGrantEnd.Expired && renewal.Sequence != reservation.Sequence,
                        "actual A1 expiry and renewal replace current reservation before archived lookup");
                    Check(ReplayTimedShotFact.TryRead(packet, out produced) && produced == expected,
                        "current weapon/expiry/new reservation cannot rewrite captured flight bytes");
                }
                else
                {
                    Check(!metadata.TimedShotCertificates && metadata.FormatVersion == 3
                        && !records.Any(r => r.Bytes.Length > 0 && r.Bytes[0] is ReplayTimedShotScope.Tag or ReplayTimedShotFact.Tag),
                        "ordinary " + mode + " authority recording retains published metadata and no timed facts");
                }
            }
            finally { ReplayCapture.Recorder.Accepted -= Capture; }
        }
        Check(!NetSession.Active, "authority is closed before detached reader/playback tests");
        string path = Path.Combine(directory, mode + "-archive-" + archive + ".ppdemo");
        uint origin = records.Where(r => r.Bytes.Length > 0).Min(r => r.Frame);
        // Keep the actual recorder intervals, offset by one so bootstrap precedes
        // first emission; authored source/authority clocks stay inside fact bytes.
        using (var writer = new ReplayWriterV3(path, metadata))
        {
            foreach (var record in records.Where(r => r.Bytes.Length > 0))
                writer.WriteRecord(record.Frame - origin + 1, record.Bytes);
            ReplayTimelineArchive.EndFrame(writer, records.Max(r => r.Frame) - origin + 2);
        }
        using var reader = DemoReader.Open(path) ?? throw new InvalidDataException("recorded fixture did not reopen");
        var detached = new ReplayReplicaState();
        foreach (var packet in reader.Metadata!.Bootstrap.Packets) detached.Accept(packet, 0);
        DemoRecord? next;
        while ((next = reader.ReadNext()) is { } record) detached.Accept(record.Data, record.Frame);
        if (!archive)
        {
            var defaultCheckpoint = detached.CaptureCheckpoint();
            Check(!detached.TimedShots.Enabled && detached.TimedShots.Count == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(defaultCheckpoint.Bytes[4..]) == 14
                && defaultCheckpoint.Bytes[6] == 82 && defaultCheckpoint.Bytes[7] == 0x17,
                "reopened default file keeps protocol82 checkpoint v14 and exact native-touch/pickup/grant/grapple features without timed capability");
            var restoredDefault = new ReplayReplicaState(); restoredDefault.RestoreCheckpoint(defaultCheckpoint);
            Check(!restoredDefault.TimedShots.Enabled && restoredDefault.TimedShots.Count == 0,
                "default checkpoint v14 restore cannot invent timed capability");
            return;
        }
        AssertFact(detached, expected, "reopened miss retains exact recorded fact without authority session");
        var checkpoint = detached.CaptureCheckpoint();
        var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(checkpoint);
        AssertFact(restored, expected, "detached checkpoint restore retains immutable pending miss");
        using var seekReader = DemoReader.Open(path) ?? throw new InvalidDataException("seek reopen failed");
        var after = seekReader.SeekAfter(reader.DurationFrames);
        Check(after == null && seekReader.LastResult == ReplayOpenResult.Success,
            "actual indexed reader seeks beyond final file frame without live state");
        using var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
        Check(session.Join(path), "actual private passive playback session reopens authority recording");
        for (uint i = 0; i <= session.LastFrame + 1; i++) session.PumpFrame();
        AssertFact(((PassiveReplaySessionHost)session.Host).State, expected,
            "actual private session decoder preserves complete timed fact");
        Check(ReplayArchive.Recover(path, out var copied, out var copyResult)
            && copyResult == ReplayOpenResult.Success && copied != null,
            "actual archive full-file copy succeeds after authority closes");
        AssertRecordedFile(copied!, expected, lead: 0);
        string clip = Path.Combine(directory, "funded-miss-clip.ppdemo");
        Check(ReplayArchive.Extract(path, 1, reader.DurationFrames, clip) == ReplayOpenResult.Success,
            "actual archive range extraction preserves required baseline and warmup facts");
        AssertRecordedFile(clip, expected, lead: 1);
    }

    private static void NativeRecordedAdmission(string room, string directory)
    {
        ReplayTimedShotRecording.Enabled = true;
        var records = new List<Produced>();
        ReplayMetadata metadata;
        ReplayTimedShotFact expected;
        void Capture(ReplayTimelineRecord record)
            => records.Add(new(record.RecordingFrame, record.ServerTick, record.Kind, record.Marker, record.Payload.ToArray()));
        using (var fixture = new NativeFixture(room, TimedGrantProofMode.Shadow))
        {
            // No resource/source/grant reset or seed: the real native spawn and
            // birth journals from ServerSim own the initial resources.
            var grant = fixture.NativeGrant();
            ReplayCapture.Recorder.Accepted += Capture;
            try
            {
                ReplayCapture.Recorder.Reset();
                var match = NetSession.ServerMatch ?? throw new InvalidOperationException("native authority match missing");
                ReplayCapture.Recorder.AcceptMatch(match, NetSession.NetFrame);
                ReplayCapture.Recorder.AcceptRoster(NetSession.LobbyRoster(), NetSession.NetFrame);
                metadata = ReplayCapture.Capture(ReplayType.FullMatch, hashMap: false);
                var fire = fixture.Fire();
                var key = ShotKey.For(0, fire.ShotId);
                int victimHealth = fixture.Victim.Health;
                fixture.Step(down: true, fire: fire);
                Check(NetFireEvents.CaptureLedger(0).TryGet(key, out var admitted, out var disposition, out _)
                    && admitted.Fire == fire && disposition == ShotDisposition.Fired,
                    "full native C+1 simulation admits and completes exact discrete fire");
                Check(NetTimedShotProof.TryGet(key, fire.SourceFrame, out var certificate),
                    "native-contact funded shot has complete retained certificate");
                expected = ReplayTimedShotFact.From(certificate, TimedGrantProofMode.Shadow);
                var beam = fixture.Owner.EquipInfo.Beams.Single(b => b.ModLaunchKey == key && b.ModComponentId != 0);
                Check(expected.Source.Source == ShotProofReason.Verified && expected.Source.Pose == ShotProofReason.Verified
                    && expected.LedgerAnswer.Knowledge == TimedGrantKnowledge.Granted
                    && expected.Knowledge == TimedGrantKnowledge.Granted
                    && expected.LedgerAnswer.Sequence == grant.Sequence
                    && expected.Fire.SourceFrame == grant.ContactFrame + 1
                    && expected.AuthorityFrame == grant.ContactFrame + 1
                    && expected.Funding?.Sequence == grant.Sequence && expected.Funding?.Spawn == grant.Spawn
                    && expected.Funding?.ContactFrame == grant.ContactFrame
                    && expected.Funding?.SourceContactFrame == grant.SourceContactFrame
                    && expected.Funding?.Life == grant.Life
                    && expected.Funding?.ExpiresAtFrame == grant.ExpiresAtFrame
                    && expected.Channels.Present && expected.Channels.Body == beam.Damage,
                    "genuine native contact/input/private drain records exact raw grant/funding/clocks/native channels");
                Check(fixture.Victim.Health == victimHealth
                    && !records.Any(r => r.Bytes.Length > 0 && r.Bytes[0] == (byte)PacketType.ReplayShotFact),
                    "genuine native funded launch misses the separate victim and emits no damage hint");
                Check(records.Count(r => r.Bytes.Length > 0 && r.Bytes[0] == ReplayTimedShotScope.Tag) == 1
                    && records.Count(r => r.Bytes.Length > 0 && r.Bytes[0] == ReplayTimedShotFact.Tag) == 1,
                    "genuine native funded first complete store emits one scope and one immutable fact");
            }
            finally { ReplayCapture.Recorder.Accepted -= Capture; }
        }
        Check(!NetSession.Active, "native authority closes before archival native-funding decode");
        string path = Path.Combine(directory, "native-funded-miss.ppdemo");
        uint origin = records.Where(r => r.Bytes.Length > 0).Min(r => r.Frame);
        using (var writer = new ReplayWriterV3(path, metadata))
        {
            foreach (var record in records.Where(r => r.Bytes.Length > 0))
                writer.WriteRecord(record.Frame - origin + 1, record.Bytes);
            ReplayTimelineArchive.EndFrame(writer, records.Max(r => r.Frame) - origin + 2);
        }
        AssertRecordedFile(path, expected, lead: 0);
        using var reader = DemoReader.Open(path) ?? throw new InvalidDataException("native file seek reopen failed");
        Check(reader.SeekAfter(reader.DurationFrames) == null && reader.LastResult == ReplayOpenResult.Success,
            "native-funded authority recording supports actual indexed seek after close");
        Check(ReplayArchive.Recover(path, out var copy, out var copyResult)
            && copyResult == ReplayOpenResult.Success && copy != null, "native-funded actual archive copy succeeds");
        AssertRecordedFile(copy!, expected, lead: 0);
        string clip = Path.Combine(directory, "native-funded-miss-clip.ppdemo");
        Check(ReplayArchive.Extract(path, 1, reader.DurationFrames, clip) == ReplayOpenResult.Success,
            "native-funded actual archive range extraction succeeds");
        AssertRecordedFile(clip, expected, lead: 1);
    }

    private static void AssertRecordedFile(string path, ReplayTimedShotFact expected, uint lead)
    {
        using var reader = DemoReader.Open(path) ?? throw new InvalidDataException("archive output did not reopen");
        Check(reader.Metadata is { TimedShotCertificates: true, FormatVersion: 6 } metadata
            && metadata.LeadInFrames == lead,
            "reopened archive copy/clip preserves explicit capability and visible clock rebase");
        var state = new ReplayReplicaState();
        foreach (var packet in reader.Metadata!.Bootstrap.Packets) state.Accept(packet, 0);
        int certificates = 0;
        while (reader.ReadNext() is { } record)
        {
            if (record.Data.Length > 0 && record.Data[0] == ReplayTimedShotFact.Tag)
            {
                certificates++;
                Check(ReplayTimedShotFact.TryRead(record.Data, out var found) && found == expected
                    && found.Fire.SourceFrame == expected.Fire.SourceFrame && found.AuthorityFrame == expected.AuthorityFrame,
                    "actual copied/extracted record retains exact original source/authority clocks and funding");
            }
            state.Accept(record.Data, record.Frame);
        }
        Check(reader.LastResult == ReplayOpenResult.Success && certificates == 1,
            "actual archive output reopens to exactly one complete missed-flight record");
        AssertFact(state, expected, "actual copied/extracted detached file resolves immutable missed flight");
    }

    internal static int Run(string room)
    {
        var old = NetShotProof.Mode; var mode = NetTimedShotProof.Mode;
        bool accepted = NetFireEvents.AcceptedPlaybackEnabled, script = NetTestScript.Enabled;
        bool archive = ReplayTimedShotRecording.Enabled;
        _checks = 0;
        string directory = Path.Combine(Path.GetTempPath(), "prime-timed-scene-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Check(ServerSim.Available(out string reason), reason);
            RecordedAdmission(room, TimedGrantProofMode.Off, false, directory);
            RecordedAdmission(room, TimedGrantProofMode.Shadow, false, directory);
            RecordedAdmission(room, TimedGrantProofMode.Shadow, true, directory);
            NativeRecordedAdmission(room, directory);
            Console.WriteLine($"PASS: {_checks} authority-recorded timed fact fidelity assertions.");
            Console.WriteLine("SCOPE: real accepted private drain/native terminals/first-store recorder callback/file reopen/private passive decoder/checkpoint restore/indexed file seek/archive full-file copy/range extraction with hidden lead-in. Seeded and genuine native-contact arms explicitly separated. NOT_RUN: PassiveReplayScene private world seek, Studio UI/private world clip playback, NativeBeam playback, graphics/audio, damage/payment, authenticated UDP, natural campaign.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("TIMED REPLAY SCENE FAIL " + error); return 1; }
        finally
        {
            NetSession.Stop(); NetShotProof.Mode = old; NetTimedShotProof.Mode = mode;
            NetFireEvents.AcceptedPlaybackEnabled = accepted; NetTestScript.Enabled = script;
            ReplayTimedShotRecording.Enabled = archive;
            Directory.Delete(directory, recursive: true);
        }
    }
}
