using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

// Asset-free capture-phase fixtures. No loaded Scene, cursor, player or theatre qualification.
internal static class CustomReplayCapturePhaseChecks
{
    internal static int Run(string output)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        int recipientExit = RunCase(Path.Combine(output, "recipient"), authority: false);
        int authorityExit = RunCase(Path.Combine(output, "authority"), authority: true);
        int? Count(string name)
        {
            string path = Path.Combine(output, name, "capture-phase-results.json");
            if (!File.Exists(path)) return null;
            using var result = JsonDocument.Parse(File.ReadAllBytes(path));
            return result.RootElement.GetProperty("checks").GetInt32();
        }
        int? recipientChecks = Count("recipient"), authorityChecks = Count("authority");
        int? totalChecks = recipientChecks.HasValue && authorityChecks.HasValue
            ? recipientChecks.Value + authorityChecks.Value : null;
        File.WriteAllText(Path.Combine(output, "capture-phase-summary.json"), JsonSerializer.Serialize(new
        {
            recipientExit, authorityExit, recipientChecks, authorityChecks, totalChecks,
            scope = "Same asset-free private session/storage fixtures independently bound to recipient CWRB1 and authority CWRB2. No loaded Scene, server recording or damage authority claim."
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (recipientExit != 0 || authorityExit != 0) return 1;
        Console.WriteLine($"PASS {totalChecks} typed capture phase checks across both origins");
        return 0;
    }

    private static int RunCase(string output, bool authority)
    {
        int checks = 0;
        output = Path.GetFullPath(output);
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidDataException(label);
            checks++;
        }
        void Reject(Action action, string label)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
            { Check(true, label); return; }
            throw new InvalidDataException("Expected refusal: " + label);
        }
        try
        {
            Directory.CreateDirectory(output);
            string loaded = typeof(ReplayReplicaState).Assembly.Location;
            Console.WriteLine("Loaded ProjectPrime: " + loaded + " SHA256="
                + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(loaded))));
            var game = GameState.Current;
            var random = Rng.Current;
            uint rng1 = Rng.Rng1, rng2 = Rng.Rng2;
            bool network = NetSession.Active;
            using var f = new CustomReplayStateChecks.Fixture();
            var context = authority
                ? new ReplayCustomWeaponContext(ReplayCustomWeaponBootstrap.ForAuthority(f.World.Start,
                    f.World.AuthorityFrame, 42, f.Context.Layout.Digest, f.Context.Bootstrap.Packages),
                    f.Context.Definition, f.Context.Layout)
                : f.Context;
            var descriptor = ReplayCustomWeaponDescriptor.ForInspection(context);
            var baseline = new ReplayReplicaState();
            baseline.EnableCustomInspection(descriptor);
            var match = new MatchStatePacket
            {
                MatchId = 7, AuthorityEpoch = f.World.Start.AuthorityEpoch,
                RoomKey = "MP3 PROVING GROUND", NextRoomKey = "", Mode = (byte)GameMode.Battle,
                RuleBits = (uint)(MatchModifierFlags.RiftlineM7Available | MatchModifierFlags.PackagedWeaponAvailable)
            };
            byte[] Packet(PacketType kind, int size, Action<byte[]> write)
            {
                byte[] body = new byte[size]; write(body);
                return new[] { (byte)kind }.Concat(body).ToArray();
            }
            byte[] Snapshot(uint tick, ushort health) => Packet(PacketType.Snapshot,
                SnapshotHeader.Size + PlayerState.Size + NetMatchTimeSync.Size + NetHealthSync.HeaderSize,
                bytes =>
                {
                    new SnapshotHeader { MatchId = 7, AuthorityEpoch = f.World.Start.AuthorityEpoch,
                        Frame = tick, PlayerCount = 1, Rng1 = 1234, Rng2 = 5678 }.Write(bytes);
                    new PlayerState { SlotIndex = 1, SlotGeneration = 5, LifeId = 10, Health = health,
                        Flags = PlayerState.FlagActive | PlayerState.FlagSpawned, Facing = Vector3.UnitZ }
                        .Write(bytes.AsSpan(SnapshotHeader.Size));
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - NetHealthSync.HeaderSize), 7);
                });
            byte[] Intent(uint tick, float x) => Packet(PacketType.SlotIntent,
                1 + IntentPacket.TouchCanonicalSize, bytes =>
                {
                    bytes[0] = 1;
                    new IntentPacket { MatchId = 7, AuthorityEpoch = f.World.Start.AuthorityEpoch,
                        SlotGeneration = 5, LifeId = 10, Frame = tick, Position = new(x, 0, 0),
                        Aim = Vector3.UnitZ, Target = NetTargetIdentity.None, Buttons = IntentButtons.InPlayState }
                        .Write(bytes.AsSpan(1));
                });
            byte[] Shot(uint id) => Packet(PacketType.ReplayShotFact, ReplayShotFactPacket.Size,
                bytes => ReplayShotFactPacket.Write(new(7, f.World.Start.AuthorityEpoch, 1002, 1001,
                    id, (ushort)id, 1, 5, 10, 2, 6, 11, (byte)BeamType.PowerBeam,
                    ReplayShotFactFlags.Direct, 10, 90, 0, 0, Vector3.UnitZ), bytes));
            baseline.Accept(Packet(PacketType.MatchState, MatchStatePacket.Size, bytes => match.Write(bytes)), 1);
            var roster = RosterPacket.Create();
            roster.MatchId = 7; roster.AuthorityEpoch = f.World.Start.AuthorityEpoch; roster.Revision = 1;
            roster.Count = 1; roster.Slots[0] = 1; roster.Generations[0] = 5; roster.Names[0] = "phase-fixture";
            baseline.Accept(Packet(PacketType.Roster, RosterPacket.Size, bytes => roster.Write(bytes)), 2);
            baseline.Accept(Snapshot(1001, 100), 3);
            baseline.Accept(ReplayCustomWeaponFact.Bootstrap(context).CopyBytes(), 10);
            baseline.Accept(ReplayCustomWeaponFact.Baseline(context, 2,
                new(7, f.World.Start.AuthorityEpoch, context.Bootstrap.Packages.MechanicsDigest, 1001, 8, 0),
                f.Slots, Array.Empty<CustomReplicaFlight>()).CopyBytes(), 11);
            for (int i = 0; i < context.Layout.Count; i++)
                baseline.Accept(ReplayCustomWeaponFact.Pickup(context, baseline.CustomWeapons!.Sequence + 1,
                    1001, i, new(new(context.PickupWorld, context.Layout.Placements[i].SpawnId, 1),
                        990, true, 0)).CopyBytes(), 12);
            var construction = baseline.CaptureCheckpoint();
            uint origin = baseline.RecordingFrame;
            byte[] World(ReplayReplicaCheckpoint current, uint frame)
            {
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);
                void Component(byte[] bytes) { writer.Write(bytes.Length); writer.Write(bytes); }
                writer.Write(0x43575050u); writer.Write((ushort)12); writer.Write((byte)15);
                writer.Write(ReplayWorldCheckpoint.ComputeContract(stable: true)); writer.Write(match.RoomKey);
                writer.Write((int)GameMode.Battle); writer.Write(123ul); writer.Write(frame);
                writer.Write(123u); writer.Write(456u);
                Component(construction.Bytes.ToArray()); Component(current.Bytes.ToArray());
                writer.Write(0);
                for (int i = 0; i < 5; i++) Component(Array.Empty<byte>());
                // These are intentionally empty native-input/pickup fixtures, not Scene object state.
                Component(new byte[8 * 57]);
                Component(new byte[4]);
                var decoded = new ReplayReplicaState();
                decoded.RestoreCustomCheckpoint(current);
                byte[] grapple = new byte[8 * (4 + PlayerGrappleState.Size)];
                for (int slot = 0; slot < 8; slot++)
                {
                    if (!decoded.TryGetPlayer(slot, out var player)
                        || player.SlotGeneration == 0 || player.LifeId == 0) continue;
                    Span<byte> row = grapple.AsSpan(slot * (4 + PlayerGrappleState.Size),
                        4 + PlayerGrappleState.Size);
                    BinaryPrimitives.WriteUInt16LittleEndian(row, player.SlotGeneration);
                    BinaryPrimitives.WriteUInt16LittleEndian(row[2..], player.LifeId);
                    if (!player.Grapple.TryWrite(row[4..], out int written)
                        || written != PlayerGrappleState.Size)
                        throw new InvalidDataException("Fixture grapple projection could not encode accepted state.");
                }
                Component(grapple);
                Component(ReplayWorldCheckpoint.CustomWorldAppendix(current)); writer.Write(false);
                return stream.ToArray();
            }
            byte[] baselineWorld = World(construction, origin);
            ReplayMetadata Metadata(byte version) => new()
            {
                FormatVersion = version, ProtocolVersion = 85, Type = ReplayType.FullMatch,
                RoomKey = match.RoomKey, Mode = GameMode.Battle, MapHash = 123,
                OriginRecordingFrame = origin, CustomWeapons = descriptor,
                Bootstrap = new() { Packets = new[] { ReplayTimelineArchive.Construction(construction) } },
                WorldCheckpoint = baselineWorld, IndexedFireContinuation = false
            };
            var clock = new ReplayCapturePhaseClock(origin);
            Check(clock.Classify(origin) == ReplayCapturePhase.PostWorld, "completed source is post-world");
            Check(clock.Classify(origin + 1) == ReplayCapturePhase.Ordinary, "next source is ordinary");
            Reject(() => clock.Classify(origin - 1), "stale source refuses");
            Reject(() => clock.Classify(origin + 2), "future source refuses");
            Reject(() => clock.Complete(origin), "duplicate completion refuses");
            Reject(() => clock.Complete(origin + 2), "completion gap refuses");
            clock.Complete(origin + 1);
            Check(clock.Classify(origin + 1) == ReplayCapturePhase.PostWorld, "completion advances classification");
            Check(clock.CompletedFrame == origin + 1, "clock completed frontier is explicit");
            var lastClock = new ReplayCapturePhaseClock(uint.MaxValue);
            Check(lastClock.Classify(uint.MaxValue) == ReplayCapturePhase.PostWorld, "terminal uint source remains representable");
            Reject(() => lastClock.Classify(0), "classification cannot wrap");
            Reject(() => lastClock.Complete(0), "completion cannot wrap");

            var store = new WeaponPackageStore(Path.Combine(output, "store"));
            store.Install(f.Package.CopyArchive());
            using var resources = PreparedReplayWeaponPackage.Prepare(descriptor, store);
            ReplayTimelineRecord Record(uint frame, byte[] bytes) => new(frame, 1002,
                bytes[0] == ReplayCustomWeaponFact.Tag ? ReplayFactKind.CustomWeapon : ReplayFactKind.Presentation, bytes);
            ReplayCaptureRecord Typed(uint frame, byte[] bytes, ReplayCapturePhase phase)
                => new(Record(frame, bytes), phase);
            void Release(IEnumerable<ReplayCaptureRecord> records)
            { foreach (var record in records) record.Record.Release(); }
            byte[] postIntent = Intent(1002, 1), currentIntent = Intent(1003, 2);
            byte[] postShot = Shot(1), currentShot = Shot(2);
            var host = new PassiveReplaySessionHost();
            using (var session = new ReplayPlaybackSession(host))
            {
                session.JoinPostWorldInspection(construction, resources, 123);
                session.AdvancePostWorldInspection(origin, Array.Empty<ReplayCaptureRecord>());
                Check(host.State.CaptureCheckpoint().Bytes.SequenceEqual(construction.Bytes), "empty baseline initialization is inert");
                var pending = new[] { Typed(origin, postIntent, ReplayCapturePhase.PostWorld),
                    Typed(origin, postShot, ReplayCapturePhase.PostWorld),
                    Typed(origin + 1, currentIntent, ReplayCapturePhase.Ordinary),
                    Typed(origin + 1, currentShot, ReplayCapturePhase.Ordinary) };
                try
                {
                    session.AdvancePostWorldInspection(origin + 1, pending);
                    host.State.Advance(origin + 1);
                    var oracle = new ReplayReplicaState(); oracle.RestoreCustomCheckpoint(construction);
                    oracle.AcceptPostWorld(postIntent, origin, origin + 1);
                    oracle.AcceptPostWorld(postShot, origin, origin + 1);
                    oracle.Accept(currentIntent, origin + 1); oracle.Accept(currentShot, origin + 1);
                    oracle.Advance(origin + 1);
                    Check(host.State.CaptureCheckpoint().Bytes.SequenceEqual(oracle.CaptureCheckpoint().Bytes),
                        "typed order equals literal original-clock decoder oracle");
                    Check(host.State.ShotFacts.Select(item => item.ShotId).SequenceEqual(new uint[] { 1, 2 }),
                        "post and ordinary native shots survive same application batch");
                    var nextPost = Typed(origin + 1, Intent(1004, 3), ReplayCapturePhase.PostWorld);
                    try { session.AdvancePostWorldInspection(origin + 2, new[] { nextPost }); }
                    finally { nextPost.Record.Release(); }
                    host.State.Advance(origin + 2);
                    oracle.AcceptPostWorld(Intent(1004, 3), origin + 1, origin + 2); oracle.Advance(origin + 2);
                    Check(host.State.CaptureCheckpoint().Bytes.SequenceEqual(oracle.CaptureCheckpoint().Bytes),
                        "pending current post applies once before following frame");
                    Check(host.State.IntentAge(1) == 1 && host.State.ShotFacts.Count == 0,
                        "source age is preserved and old ephemeral batch clears");
                }
                finally { Release(pending); }
            }
            using (var restoredSession = new ReplayPlaybackSession(new PassiveReplaySessionHost()))
            {
                restoredSession.JoinPostWorldInspection(construction, resources, 123);
                restoredSession.RestoreCustomInspection(construction);
                Reject(() => restoredSession.AdvancePostWorldInspection(origin, Array.Empty<ReplayCaptureRecord>()),
                    "restored complete checkpoint cannot initialize its same frame again");
            }
            using (var wrongMethod = new ReplayPlaybackSession(new PassiveReplaySessionHost()))
            {
                wrongMethod.JoinPostWorldInspection(construction, resources, 123);
                Reject(() => wrongMethod.AdvanceCustomInspection(origin + 1, Array.Empty<ReplayTimelineRecord>()),
                    "format7 live advancement refuses typed format8 scope");
            }
            // Every adversary gets a fresh private session; failures must close it without touching globals.
            foreach (var adversary in new[] { "stale", "future", "early-post", "ordinary-before-post", "bad-phase", "budget", "byte-budget" })
            {
                var privateHost = new PassiveReplaySessionHost();
                using var session = new ReplayPlaybackSession(privateHost);
                session.JoinPostWorldInspection(construction, resources, 123);
                byte[] large = ReplayCustomWeaponFact.Baseline(context, 2,
                    new(7, f.World.Start.AuthorityEpoch, context.Bootstrap.Packages.MechanicsDigest, 1001, 8, 64),
                    f.Slots, Enumerable.Range(1, 64).Select(i => f.Flight((uint)i)).ToArray()).CopyBytes();
                ReplayCaptureRecord[] records = adversary switch
                {
                    "stale" => new[] { Typed(origin - 1, postIntent, ReplayCapturePhase.PostWorld) },
                    "future" => new[] { Typed(origin + 2, postIntent, ReplayCapturePhase.Ordinary) },
                    "early-post" => new[] { Typed(origin + 1, postIntent, ReplayCapturePhase.PostWorld) },
                    "ordinary-before-post" => new[] { Typed(origin + 1, currentIntent, ReplayCapturePhase.Ordinary), Typed(origin, postIntent, ReplayCapturePhase.PostWorld) },
                    "bad-phase" => new[] { Typed(origin, postIntent, (ReplayCapturePhase)255) },
                    "byte-budget" => Enumerable.Range(0, ReplayPostWorldFrameState.MaximumBytes / (large.Length + 8) + 1)
                        .Select(_ => Typed(origin, large, ReplayCapturePhase.PostWorld)).ToArray(),
                    _ => Enumerable.Range(0, ReplayPostWorldFrameState.MaximumRecords + 1)
                        .Select(_ => Typed(origin, postIntent, ReplayCapturePhase.PostWorld)).ToArray()
                };
                try { Reject(() => session.AdvancePostWorldInspection(origin + 1, records), adversary); }
                finally { Release(records); }
                Check(!session.IsActive && session.LastResult == ReplayOpenResult.Corrupt, "typed failure closes scope " + adversary);
            }

            void Finish(ReplayWritePump pump, bool success, string label)
            {
                pump.Complete();
                Check(pump.Completion.Wait(TimeSpan.FromSeconds(15)), "bounded writer completion " + label);
                pump.Completion.GetAwaiter().GetResult();
                Check((pump.Error == null) == success && pump.QueuedBytes == 0, "writer result and queue ownership " + label);
            }
            List<DemoRecord> Read(string path, uint duration)
            {
                using var reader = DemoReader.Open(path, out var result);
                Check(reader != null && result == ReplayOpenResult.Success && reader.DurationFrames == duration, "closed format8 duration");
                var records = new List<DemoRecord>();
                while (reader!.ReadNext() is { } record) records.Add(record);
                Check(reader.LastResult == ReplayOpenResult.Success && records.Count(item => item.Data.SequenceEqual(new byte[] { 255 })) == 1,
                    "one final quiet marker after clean read");
                Check(records[^1].Frame == duration && records[^1].Data.SequenceEqual(new byte[] { 255 }), "quiet marker is final frontier");
                return records;
            }
            string emptyPath = Path.Combine(output, "empty-zero.ppdemo");
            var emptyPump = new ReplayWritePump(emptyPath, Metadata(8), origin);
            Finish(emptyPump, true, "empty completed origin");
            Check(Read(emptyPath, 0).Count == 1, "empty format8 publishes exactly final quiet zero");
            foreach (bool advance in new[] { false, true })
            {
                string path = Path.Combine(output, advance ? "fifo.ppdemo" : "final-post-zero.ppdemo");
                using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
                var pump = new ReplayWritePump(path, Metadata(8), origin, beforeWrite: () =>
                { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Bounded writer barrier"); });
                byte[] original = postIntent.ToArray();
                var post = Record(origin, original);
                try
                {
                    Check(pump.RecordPostWorld(post), "post at completed origin queues explicitly");
                    Check(entered.Wait(TimeSpan.FromSeconds(3)), "worker actually waits before writing retained record");
                    post.Release(); original.AsSpan().Clear();
                    if (advance)
                    {
                        var ordinary = Record(origin + 1, currentIntent);
                        try { Check(pump.Record(ordinary) && pump.EndFrame(1), "ordinary next then contiguous completion queues"); }
                        finally { ordinary.Release(); }
                    }
                }
                finally { release.Set(); }
                Finish(pump, true, "FIFO");
                var records = Read(path, advance ? 1u : 0u);
                Check(records[0].Frame == 0 && ReplayPostWorldRecord.Read(records[0].Data, context).SequenceEqual(postIntent),
                    "queued post retains immutable exact inner bytes after caller release");
                if (advance) Check(records[1].Frame == 1 && records[1].Data.SequenceEqual(currentIntent), "ordinary exact FIFO bytes retained");
            }
            string checkpointPath = Path.Combine(output, "checkpoint-then-post.ppdemo");
            var checkpointState = new ReplayReplicaState(); checkpointState.RestoreCustomCheckpoint(construction);
            checkpointState.Advance(origin + 1);
            byte[] checkpointBytes = World(checkpointState.CaptureCheckpoint(), origin + 1);
            var checkpointPump = new ReplayWritePump(checkpointPath, Metadata(8), origin);
            Check(checkpointPump.EndFrame(1), "empty next frame can complete before checkpoint");
            var checkpointRecord = new ReplayTimelineRecord(origin + 1, 1002, ReplayFactKind.World, checkpointBytes);
            var checkpointPost = Record(origin + 1, postIntent);
            try
            {
                Check(checkpointPump.Checkpoint(checkpointRecord), "completed frontier checkpoint queues");
                Check(checkpointPump.RecordPostWorld(checkpointPost), "late packet follows same completed checkpoint");
            }
            finally { checkpointRecord.Release(); checkpointPost.Release(); }
            Finish(checkpointPump, true, "checkpoint and late packet");
            var checkpointRecords = Read(checkpointPath, 1);
            using (var reader = DemoReader.Open(checkpointPath, out _))
            {
                Check(reader!.Checkpoints.Count == 1 && reader.ReadCheckpoint(reader.Checkpoints[0]).SequenceEqual(checkpointBytes),
                    "exact completed checkpoint bytes survive async ownership");
            }
            Check(checkpointRecords.Count == 2 && ReplayPostWorldRecord.Read(checkpointRecords[0].Data, context).SequenceEqual(postIntent),
                "same-frontier post remains visible after checkpoint and before final quiet");
            foreach (string invalid in new[] { "format7", "ordinary-zero", "post-future", "frame-gap", "world-as-post" })
            {
                string path = Path.Combine(output, "invalid-" + invalid + ".ppdemo");
                var pump = new ReplayWritePump(path, Metadata(invalid == "format7" ? (byte)7 : (byte)8), origin);
                var record = invalid == "world-as-post"
                    ? new ReplayTimelineRecord(origin, 0, ReplayFactKind.World, baselineWorld)
                    : Record(invalid == "post-future" ? origin + 1 : origin, postIntent);
                try
                {
                    bool accepted = invalid switch
                    {
                        "ordinary-zero" => pump.Record(record),
                        "frame-gap" => pump.EndFrame(2),
                        _ => pump.RecordPostWorld(record)
                    };
                    Check(!accepted, "invalid explicit enqueue refuses " + invalid);
                }
                finally { record.Release(); }
                Finish(pump, false, invalid);
                Check(!File.Exists(path), "invalid enqueue publishes no final file " + invalid);
            }
            var timedState = new ReplayReplicaState(); timedState.RestoreCustomCheckpoint(construction);
            Check(timedState.TimedShots.Begin(f.World.Start.AuthorityEpoch, 7, 42, TimedGrantProofMode.Shadow),
                "explicit combined timed/custom checkpoint enables typed capability");
            var timedConstruction = timedState.CaptureCheckpoint();
            var timedMetadata = new ReplayMetadata
            {
                FormatVersion = 8, ProtocolVersion = NetConfig.ProtocolVersion, Type = ReplayType.FullMatch,
                RoomKey = match.RoomKey, Mode = GameMode.Battle, MapHash = 123, OriginRecordingFrame = origin,
                CustomWeapons = descriptor, TimedShotCertificates = true,
                Bootstrap = new() { Packets = new[] { ReplayTimelineArchive.Construction(timedConstruction) } }
            };
            var key = new ShotKey(f.World.Start.AuthorityEpoch, 7, 1, 5, 10, 1);
            var fire = new FireEvent(1, 1001, 1000, 17, FireEventKind.PressFire,
                (byte)BeamType.PowerBeam, 0, 0, FireEvent.FlagPose, Vector3.Zero,
                Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, SourceLife: 10);
            var certificate = new TimedShotCertificate(key, fire, 42, 1002, TimedShotSource.AcceptedFire,
                new(ShotProofReason.Verified, ShotProofReason.Verified, true, 0, 1, 8),
                new(TimedGrantKnowledge.NotGranted, 0), TimedGrantKnowledge.NotGranted,
                null, TimedShotUnavailable.None, 1,
                new(6, 9, 2, Vector3.Zero, Vector3.UnitZ, .25f, 1, 50, 0, false, 0, true));
            byte[][] timedPackets = {
                new ReplayTimedShotScope(f.World.Start.AuthorityEpoch, 7, 42, TimedGrantProofMode.Shadow, 1002).Packet(),
                ReplayTimedShotFact.From(certificate, TimedGrantProofMode.Shadow).Packet()
            };
            Check(ReplayTimedShotScope.TryRead(timedPackets[0], out _) && ReplayTimedShotFact.TryRead(timedPackets[1], out _),
                "timed capability adversaries begin from canonical valid 251 and 252 values");
            foreach (byte[] packet in timedPackets)
            {
                var timedHost = new PassiveReplaySessionHost();
                using var session = new ReplayPlaybackSession(timedHost);
                session.JoinPostWorldInspection(construction, resources, 123);
                var batch = new[] { Typed(origin, postIntent, ReplayCapturePhase.PostWorld),
                    Typed(origin, packet, ReplayCapturePhase.PostWorld) };
                try { Reject(() => session.AdvancePostWorldInspection(origin + 1, batch), "undeclared timed fact rejects whole batch"); }
                finally { Release(batch); }
                Check(!session.IsActive && !timedHost.State.TryGetIntent(1, out _),
                    "rejected batch exposes no held prefix intent after fail-closed retirement");
                string path = Path.Combine(output, "undeclared-timed-" + packet[0] + ".ppdemo");
                var pump = new ReplayWritePump(path, Metadata(8), origin);
                var record = Record(origin, packet);
                try { Check(!pump.RecordPostWorld(record), "producer refuses timed tag without metadata capability"); }
                finally { record.Release(); }
                Finish(pump, false, "undeclared timed tag");
                Check(!File.Exists(path), "undeclared timed fact publishes no file");
            }
            var positiveTimedHost = new PassiveReplaySessionHost();
            using (var session = new ReplayPlaybackSession(positiveTimedHost))
            {
                session.JoinPostWorldInspection(timedConstruction, resources, 123);
                var batch = timedPackets.Select(bytes => Typed(origin, bytes, ReplayCapturePhase.PostWorld)).ToArray();
                try { session.AdvancePostWorldInspection(origin + 1, batch); positiveTimedHost.State.Advance(origin + 1); }
                finally { Release(batch); }
                Check(session.IsActive && positiveTimedHost.State.TimedShots.Count == 1,
                    "declared timed capability admits scope and certificate with same original clocks");
            }
            string timedPath = Path.Combine(output, "declared-timed.ppdemo");
            var timedPump = new ReplayWritePump(timedPath, timedMetadata, origin);
            foreach (byte[] packet in timedPackets)
            {
                var record = Record(origin, packet);
                try { Check(timedPump.RecordPostWorld(record), "declared timed fact queues explicitly"); }
                finally { record.Release(); }
            }
            Finish(timedPump, true, "declared timed capability");
            var timedRecords = Read(timedPath, 0);
            Check(timedRecords.Count == 3 && timedPackets.Select((packet, i) =>
                ReplayPostWorldRecord.Read(timedRecords[i].Data, context).SequenceEqual(packet)).All(value => value),
                "declared timed archive preserves both exact file-only inner families");
            string incomplete = Path.Combine(output, "incomplete-future.ppdemo");
            var incompletePump = new ReplayWritePump(incomplete, Metadata(8), origin);
            var future = Record(origin + 1, currentIntent);
            try { Check(incompletePump.Record(future), "future ordinary command accepted pending completion"); }
            finally { future.Release(); }
            Finish(incompletePump, false, "incomplete frontier");
            Check(!File.Exists(incomplete), "uncompleted future never publishes final file");

            string overflow = Path.Combine(output, "overflow.ppdemo");
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var first = Record(origin, postIntent);
                var pump = new ReplayWritePump(overflow, Metadata(8), origin,
                    maximumBytes: first.PayloadBytes * 2 - 1, beforeWrite: () =>
                    { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Bounded overflow barrier"); });
                try
                {
                    Check(pump.RecordPostWorld(first) && entered.Wait(TimeSpan.FromSeconds(3)), "overflow barrier holds actual retained command");
                    Check(!pump.RecordPostWorld(first), "byte budget rejects additional retained command");
                }
                finally { release.Set(); first.Release(); }
                Finish(pump, false, "byte budget");
                Check(!File.Exists(overflow), "failed delayed marker path publishes no file");
            }
            Check(ReferenceEquals(game, GameState.Current) && ReferenceEquals(random, Rng.Current)
                && rng1 == Rng.Rng1 && rng2 == Rng.Rng2 && network == NetSession.Active, "pure tests leave live globals unchanged");
            File.WriteAllText(Path.Combine(output, "capture-phase-results.json"), JsonSerializer.Serialize(new
            {
                checks, authority, scope = "Constructed asset-free typed capture/session and asynchronous format8 storage fixtures. No live producer, Scene, combat or authority recording activation claim."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS {checks} typed replay capture phase checks");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("CUSTOM REPLAY CAPTURE PHASE FAIL " + error);
            return 1;
        }
    }
}
