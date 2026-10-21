using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

// Asset-free phase-session fixtures. No loaded Scene, cursor, player or theatre qualification.
internal static class CustomReplayPostWorldSessionChecks
{
    internal static int Run(string output)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        int recipientExit = RunCase(Path.Combine(output, "recipient"), authority: false);
        int authorityExit = RunCase(Path.Combine(output, "authority"), authority: true);
        int? Count(string name)
        {
            string path = Path.Combine(output, name, "phase-session-results.json");
            if (!File.Exists(path)) return null;
            using var result = JsonDocument.Parse(File.ReadAllBytes(path));
            return result.RootElement.GetProperty("checks").GetInt32();
        }
        int? recipientChecks = Count("recipient"), authorityChecks = Count("authority");
        int? totalChecks = recipientChecks.HasValue && authorityChecks.HasValue
            ? recipientChecks.Value + authorityChecks.Value : null;
        File.WriteAllText(Path.Combine(output, "phase-session-summary.json"), JsonSerializer.Serialize(new
        {
            recipientExit, authorityExit, recipientChecks, authorityChecks, totalChecks,
            scope = "Same asset-free private session/storage fixtures independently bound to recipient CWRB1 and authority CWRB2. No loaded Scene, server recording or damage authority claim."
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (recipientExit != 0 || authorityExit != 0) return 1;
        Console.WriteLine($"PASS {totalChecks} phase-aware private session checks across both origins");
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
            ulong sequence = baseline.CustomWeapons!.Sequence;
            var changed = (CustomReplicaSlot[])f.Slots.Clone();
            changed[1] = changed[1] with { State = changed[1].State with { Magazine = changed[1].State.Magazine - 1 } };
            var post = new Dictionary<uint, byte[][]>
            {
                [0] = new[] { ReplayCustomWeaponFact.State(context, ++sequence, 1002, changed).CopyBytes(), Intent(1002, 1), Shot(1) },
                [1] = new[] { ReplayCustomWeaponFact.Clock(context, ++sequence, 1003).CopyBytes(), Intent(1004, 3), Shot(2) },
                [2] = new[] { ReplayCustomWeaponFact.State(context, ++sequence, 1004, changed).CopyBytes() }
            };
            var ordinary = new Dictionary<uint, byte[][]>
            {
                [1] = new[] { Snapshot(1002, 90), Intent(1003, 2), Shot(3) },
                [2] = new[] { Snapshot(1003, 80) }
            };
            // Literal decoder oracle bypasses archive envelope decoding, pending queues and seeking.
            var oracle = new ReplayReplicaState(); oracle.RestoreCustomCheckpoint(construction);
            var expected = new Dictionary<uint, byte[]> { [0] = oracle.CaptureCheckpoint().Bytes.ToArray() };
            for (uint next = 1; next <= 3; next++)
            {
                if (post.TryGetValue(next - 1, out var deferred))
                    foreach (byte[] bytes in deferred) oracle.AcceptPostWorld(bytes, origin + next - 1, origin + next);
                if (ordinary.TryGetValue(next, out var current))
                    foreach (byte[] bytes in current) oracle.Accept(bytes, origin + next);
                oracle.Advance(origin + next);
                expected[next] = oracle.CaptureCheckpoint().Bytes.ToArray();
                if (next == 1) Check(oracle.TryGetIntent(1, out var held) && held.Position.X == 2,
                    "ordinary current intent follows prior deferred intent");
                if (next == 2) Check(oracle.IntentAge(1) == 1,
                    "deferred intent retains original source recording clock rather than relabeling application");
            }
            string path = Path.Combine(output, "phase-session.ppdemo");
            using (var writer = new ReplayWriterV3(path, Metadata(8)))
            {
                for (uint frame = 0; frame <= 2; frame++)
                {
                    if (ordinary.TryGetValue(frame, out var current))
                        foreach (byte[] bytes in current) writer.WriteRecord(frame, bytes);
                    foreach (byte[] bytes in post[frame]) writer.WriteRecord(frame, ReplayPostWorldRecord.Encode(bytes, context));
                    if (frame == 1) writer.WriteCheckpoint(frame, World(new(expected[frame]), origin + frame));
                }
                // A final-frame deferred suffix has no following fixed step and must remain inert.
                writer.WriteRecord(3, ReplayPostWorldRecord.Encode(
                    ReplayCustomWeaponFact.State(context, sequence + 1, 1005, f.Slots).CopyBytes(), context));
                writer.WriteRecord(3, new byte[] { 255 });
            }
            var store = new WeaponPackageStore(Path.Combine(output, "store")); store.Install(f.Package.CopyArchive());
            Reject(() => { using var denied = PreparedCustomReplayArchive.Prepare(path, store); },
                "ordinary preparation remains format7-only");
            string missingRoot = Path.Combine(output, "missing");
            Reject(() => { using var denied = PreparedCustomReplayArchive.PrepareInspection(path, new(missingRoot)); },
                "phase preparation refuses missing exact archive");
            Check(!Directory.Exists(missingRoot), "missing exact phase preparation creates no store");
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel(); bool cancelled = false;
                try { using var denied = PreparedCustomReplayArchive.PrepareInspection(path, store, cancellation: cancel.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "pre-cancelled phase preparation refuses before ownership transfer");
            }
            using (var gate = new FileStream(Path.Combine(Path.Combine(output, "store"), ".store.lock"),
                FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var cancellation = new CancellationTokenSource())
            using (var entered = new ManualResetEventSlim())
            {
                var worker = Task.Run(() =>
                {
                    entered.Set();
                    try
                    {
                        using var denied = PreparedCustomReplayArchive.PrepareInspection(path, store,
                            cancellation: cancellation.Token);
                        return false;
                    }
                    catch (OperationCanceledException) { return true; }
                });
                Check(entered.Wait(TimeSpan.FromSeconds(3)), "resource preparation worker starts");
                Check(!worker.Wait(TimeSpan.FromMilliseconds(100)),
                    "resource preparation remains pending while the actual OS package-store gate is held");
                cancellation.Cancel();
                Check(worker.Wait(TimeSpan.FromSeconds(5)) && worker.Result,
                    "cancelled contended preparation exits without waiting for gate release");
            }
            string format7 = Path.Combine(output, "unchanged-format7.ppdemo");
            using (var writer = new ReplayWriterV3(format7, Metadata(7))) writer.WriteRecord(1, new byte[] { 255 });
            using (var retained = PreparedCustomReplayArchive.PrepareInspection(format7, store))
                Check(retained.FormatVersion == 7, "explicit preparation preserves existing format7 capability");
            foreach (uint target in new uint[] { 0, 1, 2, 3 })
            {
                using var prepared = PreparedCustomReplayArchive.PrepareInspection(path, store, origin + target);
                Check(prepared.FormatVersion == 8, "preparation retains explicit format8 capability");
                using var selected = prepared.CopyWorld();
                var host = new PassiveReplaySessionHost();
                using var session = new ReplayPlaybackSession(host);
                session.JoinCustomInspection(selected.ConstructionState(), prepared.Resources, 123);
                session.RestoreCustomInspection(selected.DecoderState());
                session.AttachCustomArchive(prepared);
                uint selectedRelative = session.RecordingFrame - origin;
                byte[] paused = host.State.CaptureCheckpoint().Bytes.ToArray();
                Check(paused.SequenceEqual(expected[selectedRelative]),
                    "inclusive attach leaves completed checkpoint K unchanged despite pending lateK");
                while (session.RecordingFrame < origin + target)
                {
                    Check(session.AdvanceCustomArchiveInspection(), "bounded phase reconstruction advances");
                    host.State.Advance(session.RecordingFrame);
                }
                Check(host.State.CaptureCheckpoint().Bytes.SequenceEqual(expected[target]),
                    "phase target equals literal decoder oracle " + target);
                while (!session.AtEnd)
                {
                    Check(session.AdvanceCustomArchiveInspection(), "constructed phase fixture suffix advances");
                    host.State.Advance(session.RecordingFrame);
                    uint relative = session.RecordingFrame - origin;
                    Check(host.State.CaptureCheckpoint().Bytes.SequenceEqual(expected[relative]),
                        "phase suffix preserves exact native/custom decoder bytes " + relative);
                    if (relative == 1)
                        Check(host.State.ShotFacts.Select(fact => fact.ShotId).SequenceEqual(new uint[] { 1, 3 }),
                            "deferred and ordinary native ShotFacts share actual application batch through Advance");
                    if (relative == 2)
                        Check(host.State.ShotFacts.Count == 1 && host.State.ShotFacts[0].ShotId == 2,
                            "next application replaces old ephemeral batch without losing deferred native ShotFact");
                    if (relative == 3)
                        Check(host.State.ShotFacts.Count == 0, "quiet next application clears native ephemeral shot batch");
                }
                byte[] ended = host.State.CaptureCheckpoint().Bytes.ToArray();
                Check(!session.AdvanceCustomArchiveInspection() && host.State.CaptureCheckpoint().Bytes.SequenceEqual(ended),
                    "final post2 applies once before frame3 and EOF is inert");
            }
            // Independent container integrity adversary: first final deferred packet is valid,
            // but the following physical EOF chunk has a bad payload CRC header.
            var flush = typeof(ReplayWriterV3).GetMethod("FlushChunk",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            string terminal = Path.Combine(output, "terminal-split.ppdemo");
            byte[] finalDeferred = ReplayPostWorldRecord.Encode(
                ReplayCustomWeaponFact.State(context, sequence + 1, 1005, f.Slots).CopyBytes(), context);
            using (var writer = new ReplayWriterV3(terminal, Metadata(8)))
            {
                for (uint frame = 0; frame <= 2; frame++)
                {
                    if (ordinary.TryGetValue(frame, out var current))
                        foreach (byte[] bytes in current) writer.WriteRecord(frame, bytes);
                    foreach (byte[] bytes in post[frame])
                        writer.WriteRecord(frame, ReplayPostWorldRecord.Encode(bytes, context));
                }
                writer.WriteCheckpoint(3, World(new(expected[3]), origin + 3));
                writer.WriteRecord(3, finalDeferred);
                flush.Invoke(writer, null);
                writer.WriteRecord(3, finalDeferred);
                writer.WriteRecord(3, new byte[] { 255 });
            }
            byte[] corruptBytes = File.ReadAllBytes(terminal);
            int firstChunk = checked(14 + BinaryPrimitives.ReadInt32LittleEndian(corruptBytes.AsSpan(6)));
            Check(BinaryPrimitives.ReadUInt32LittleEndian(corruptBytes.AsSpan(firstChunk)) == 0x334b4843u,
                "independent terminal container locates first genuine chunk header");
            int secondChunk = checked(firstChunk + 28
                + BinaryPrimitives.ReadInt32LittleEndian(corruptBytes.AsSpan(firstChunk + 16)));
            Check(BinaryPrimitives.ReadUInt32LittleEndian(corruptBytes.AsSpan(secondChunk)) == 0x334b4843u
                && BinaryPrimitives.ReadUInt32LittleEndian(corruptBytes.AsSpan(secondChunk + 4)) == 3
                && BinaryPrimitives.ReadUInt32LittleEndian(corruptBytes.AsSpan(secondChunk + 12)) == 2,
                "second terminal chunk contains deferred EOF packet plus final quiet marker");
            corruptBytes[secondChunk + 24] ^= 1;
            string corruptTerminal = Path.Combine(output, "terminal-second-chunk-bad-crc.ppdemo");
            File.WriteAllBytes(corruptTerminal, corruptBytes);
            using (var reader = DemoReader.Open(corruptTerminal, out var opened))
                Check(reader != null && opened == ReplayOpenResult.Success && reader.DurationFrames == 3,
                    "unchanged valid footer still opens before corrupted terminal chunk is consumed");
            foreach (bool corrupt in new[] { false, true })
            {
                using var prepared = PreparedCustomReplayArchive.PrepareInspection(
                    corrupt ? corruptTerminal : terminal, store);
                using var world = prepared.CopyWorld();
                var host = new PassiveReplaySessionHost();
                using var session = new ReplayPlaybackSession(host);
                session.JoinCustomInspection(world.ConstructionState(), prepared.Resources, 123);
                session.RestoreCustomInspection(world.DecoderState());
                session.AttachCustomArchive(prepared);
                bool refused = false;
                try
                {
                    while (!session.AtEnd)
                    {
                        session.AdvanceCustomArchiveInspection();
                        host.State.Advance(session.RecordingFrame);
                    }
                }
                catch (InvalidDataException) { refused = true; }
                if (corrupt)
                    Check(refused && !session.IsActive && session.LastResult == ReplayOpenResult.Corrupt,
                        "advance-to-final validates later inert EOF chunks and stops Corrupt");
                else
                    Check(!refused && host.State.CaptureCheckpoint().Bytes.SequenceEqual(expected[3]),
                        "complete multi-chunk inert EOF suffix validates without applying any terminal fact");
            }
            using (var prepared = PreparedCustomReplayArchive.PrepareInspection(corruptTerminal, store, origin + 3))
            using (var selected = prepared.CopyWorld())
            {
                Check(selected.Frame == origin + 3, "terminal attach fixture selects actual constructed final checkpoint");
                var host = new PassiveReplaySessionHost();
                using var session = new ReplayPlaybackSession(host);
                session.JoinCustomInspection(selected.ConstructionState(), prepared.Resources, 123);
                session.RestoreCustomInspection(selected.DecoderState());
                Reject(() => session.AttachCustomArchive(prepared),
                    "attaching at final checkpoint scans complete inert EOF suffix and refuses later bad CRC");
            }
            string aborted = Path.Combine(output, "aborted-valid-phase.ppdemo");
            string partial;
            using (var writer = new ReplayWriterV3(aborted, Metadata(8)))
            {
                writer.WriteRecord(0, ReplayPostWorldRecord.Encode(post[0][0], context));
                flush.Invoke(writer, null);
                partial = writer.PartialPath;
                writer.Abort();
            }
            Check(File.Exists(partial) && !File.Exists(aborted), "aborted writer retains actual footerless phase part");
            using (var reader = DemoReader.Open(partial, out var opened))
                Check(reader != null && opened == ReplayOpenResult.Success
                    && reader.Metadata!.Integrity == ReplayIntegrity.Truncated,
                    "aborted phase part has valid storage metadata but explicit truncated integrity");
            string noPartStore = Path.Combine(output, "truncated-missing-store");
            Reject(() => { using var denied = PreparedCustomReplayArchive.PrepareInspection(partial, new(noPartStore)); },
                "private phase session preparation refuses footerless part before package lookup");
            Check(!Directory.Exists(noPartStore), "truncated preparation does not create or acquire a package store");

            using (var prepared = PreparedCustomReplayArchive.PrepareInspection(path, store))
            {
                Reject(() => { using var denied = prepared.CreateRequestSession(); },
                    "format8 ordinary theatre request admission stays closed");
                Reject(() => { using var denied = PassiveReplayPlayer.OpenCustomArchive(prepared,
                    Path.Combine(output, "store"), new Vector2i(640, 480)); },
                    "format8 bounded player refuses before Scene construction");
                Reject(() => { using var noScene = PassiveReplayScene.OpenCustomArchiveInspection(prepared, new Vector2i(640, 480)); },
                    "format8 Scene admission refuses before GL/resource Scene creation");
                Check(store.Uninstall(f.Package.PackageHash) && store.Prune() == 0,
                    "CPU phase preparation retains exact historical resources after uninstall");
            }
            var direct = new ReplayReplicaState(); direct.RestoreCustomCheckpoint(construction);
            Reject(() => direct.AcceptPostWorld(post[0][1], origin, origin + 2), "post application must be exactly source+1");
            direct.AcceptPostWorld(post[0][1], origin, origin + 1); direct.Advance(origin + 1);
            Check(direct.IntentAge(1) == 1 && direct.TryGetIntent(1, out var retainedIntent) && retainedIntent.Frame == 1002,
                "post accepts original intent source tick and age without future clock substitution");
            Check(store.Prune() == 1,
                "all completed preparation/session owners release exact CPU package leases");
            Check(ReferenceEquals(game, GameState.Current) && ReferenceEquals(random, Rng.Current)
                && rng1 == Rng.Rng1 && rng2 == Rng.Rng2 && network == NetSession.Active,
                "phase private sessions leave foreground game RNG and network globals unchanged");
            File.WriteAllText(Path.Combine(output, "phase-session-results.json"), JsonSerializer.Serialize(new
            {
                checks, origin, originKind = context.Bootstrap.OriginKind.ToString(), targets = expected.Keys,
                scope = "Constructed asset-free phase-aware private session with literal decoder ordering oracle. Shared AcceptPostWorld projection is separately checked for original intent age and ephemeral shot visibility. Content-free world12 is not a loaded Scene; format8 Scene/player/theatre admission stays closed."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS {checks} phase-aware private replay session checks"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("POST-WORLD SESSION FAIL " + error); return 1; }
    }
}
