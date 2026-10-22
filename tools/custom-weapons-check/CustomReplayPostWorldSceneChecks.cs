using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Replay;
using MphRead.Sound;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Actual retained packets, deliberately reassigned to an explicit post-world phase.
// The literal oracle shares engine/cursor projection but bypasses file phase/session handling.
internal static class CustomReplayPostWorldSceneChecks
{
    private sealed record SourceRecord(uint Frame, byte[] Bytes);
    private sealed record Target(string Name, uint Frame);
    private sealed record ShotEvidence(uint Source, ReplayShotFact Fact);
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static int Run(string input, string output, bool mixed = false, bool requireIndexed = false, bool livePhase = false)
    {
        string cwd = Directory.GetCurrentDirectory();
        NativeWindow? window = null;
        DesktopGraphicsSession? graphics = null;
        int checks = 0, blockedFutureFire = 0, visibleAuthoredFire = 0, sourcePoseRows = 0, authoredClockChecks = 0;
        int futurePoseRows = 0, intentClockRows = 0, capabilityGuards = 0, drawings = 0, ackAvailabilityGuards = 0, nativeIntentAgeChecks = 0, mixedInheritedRows = 0;
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
            input = Path.GetFullPath(input);
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            string loaded = typeof(Scene).Assembly.Location;
            Console.WriteLine("Loaded ProjectPrime: " + loaded + " SHA256="
                + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(loaded))));
            string sourcePath = Path.Combine(input, "actual-full-custom" + DemoFile.Extension);
            if (new FileInfo(sourcePath).Length > 128 * 1024 * 1024)
                throw new InvalidDataException("Source archive bound.");
            string sourceHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(sourcePath)));
            byte[] packageBytes = File.ReadAllBytes(Path.Combine(input, "variant.ppweapon"));
            if (packageBytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("Package bound.");
            var package = WeaponPackage.Parse(packageBytes);
            using var targetsJson = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(input, "file-expected-worlds.json")));
            Target[] targets = targetsJson.RootElement.EnumerateArray().Select(row => new Target(
                row.GetProperty("name").GetString()!, row.GetProperty("frame").GetUInt32())).ToArray();
            Check(targets.Length is > 0 and <= 8 && targets.Select(row => row.Frame).Distinct().Count() == targets.Length,
                "bounded distinct retained source target labels");
            var records = new List<SourceRecord>();
            var shots = new List<ShotEvidence>();
            var sourcePoses = new Dictionary<(int Slot, uint Source, uint Tick), PlayerState>();
            ReplayMetadata metadata;
            uint duration;
            long payloadBytes = 0;
            using (var reader = DemoReader.Open(sourcePath, out var opened))
            {
                Check(reader != null && opened == ReplayOpenResult.Success
                    && reader.Metadata is { FormatVersion: 7, LeadInFrames: 0, CustomWeapons: not null },
                    "genuine retained format7 custom source opens");
                metadata = reader!.Metadata!;
                duration = reader.DurationFrames;
                Check(metadata.ProtocolVersion == 85 && metadata.CustomWeapons!.Bootstrap.OriginKind
                    == ReplayCustomWeaponOriginKind.Recipient, "actual protocol85 recipient source required");
                while (reader.ReadNext() is { } record)
                {
                    if (records.Count >= 32768 || (payloadBytes += record.Data.Length) > 64 * 1024 * 1024)
                        throw new InvalidDataException("Source accepted record bound.");
                    records.Add(new(record.Frame, record.Data));
                    uint absolute = checked(metadata.OriginRecordingFrame + record.Frame);
                    if (record.Data.Length > 0 && record.Data[0] == (byte)PacketType.ReplayShotFact
                        && ReplayShotFactPacket.TryRead(record.Data.AsSpan(1), out var fact))
                        shots.Add(new(absolute, fact));
                    if (record.Data.Length > 0 && record.Data[0] == (byte)PacketType.Snapshot)
                    {
                        var header = SnapshotHeader.Read(record.Data.AsSpan(1));
                        for (int i = 0; i < header.PlayerCount; i++)
                        {
                            var player = PlayerState.Read(record.Data.AsSpan(1 + SnapshotHeader.Size + i * PlayerState.Size));
                            sourcePoses[(player.SlotIndex, absolute, header.Frame)] = player;
                        }
                    }
                }
                Check(reader.LastResult == ReplayOpenResult.Success, "entire genuine source stream validates");
            }
            uint origin = metadata.OriginRecordingFrame, end = checked(origin + duration);
            Check(targets.Single(row => row.Name == "baseline").Frame == origin
                && targets.Single(row => row.Name == "end").Frame == end, "retained source target clocks match actual archive");
            using var baseline = ReplayWorldCheckpoint.FromCustomBytes(metadata.WorldCheckpoint, metadata.BuildId);
            bool indexed = baseline.IndexedFirePresentation;
            int worldVersion = baseline.HasHeadHistoryContract ? 13 : 12;
            Check(baseline.Frame == origin, "actual world12/13 baseline clock retained without capability retagging");
            if (requireIndexed) Check(indexed, "explicit native indexed qualification requires genuine saved indexed producer");
            var context = metadata.CustomWeapons!.InspectContext();
            ReplayMetadata phaseMetadata = ReplayFormatV3.DecodeMetadata(metadata.ProtocolVersion,
                ReplayFormatV3.EncodeMetadata(metadata), 8);
            Check(phaseMetadata.WorldCheckpoint.SequenceEqual(metadata.WorldCheckpoint)
                && phaseMetadata.CustomWeapons!.CopyBytes().SequenceEqual(metadata.CustomWeapons.CopyBytes())
                && phaseMetadata.IndexedFireContinuation == metadata.IndexedFireContinuation,
                "derived8 changes only declared storage/application contract, never baseline/resources/firing bool");
            bool IsPost(uint frame) => !mixed || (frame & 1) == 0;
            bool IsQuiet(SourceRecord row) => row.Bytes.Length == 1 && row.Bytes[0] == 255;
            var post = records.Where(row => !IsQuiet(row) && IsPost(row.Frame))
                .GroupBy(row => row.Frame).ToDictionary(group => group.Key, group => group.ToArray());
            var ordinary = records.Where(row => !IsQuiet(row) && !IsPost(row.Frame))
                .GroupBy(row => row.Frame).ToDictionary(group => group.Key, group => group.ToArray());
            var availabilityBySource = new Dictionary<uint, uint>();
            uint availabilityFloor = 0;
            foreach (var row in records.Where(row => !IsQuiet(row)))
            {
                uint source = checked(origin + row.Frame);
                if (IsPost(row.Frame)) availabilityFloor = checked(source + 1);
                availabilityBySource[source] = availabilityFloor;
            }
            Check(records.Count(row => row.Bytes.Length == 1 && row.Bytes[0] == 255) == 1
                && records.Last().Frame == duration && records.Last().Bytes.SequenceEqual(new byte[] { 255 }),
                "source final quiet marker remains single final marker");
            void WriteDerived(string path, IReadOnlyDictionary<uint, byte[]>? worlds = null)
            {
                using var writer = new ReplayWriterV3(path, phaseMetadata);
                foreach (var row in records)
                {
                    byte[] bytes = IsQuiet(row) || !IsPost(row.Frame)
                        ? row.Bytes : ReplayPostWorldRecord.Encode(row.Bytes, context);
                    writer.WriteRecord(row.Frame, bytes);
                }
                if (worlds != null)
                    foreach (var row in worlds.OrderBy(pair => pair.Key))
                        if (row.Key != origin) writer.WriteCheckpoint(row.Key - origin, row.Value);
                foreach (var item in metadata.Events) writer.WriteEvent(item);
            }
            string seedPath = Path.Combine(output, "derived-phase-seed.ppdemo");
            WriteDerived(seedPath);
            var store = new WeaponPackageStore(Path.Combine(output, "store"));
            Reject(() => { using var denied = PreparedCustomReplayArchive.PrepareInspection(seedPath, store); },
                "missing exact package refuses explicit phase Scene preparation");
            store.Install(packageBytes);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                bool cancelled = false;
                try { using var denied = PreparedCustomReplayArchive.PrepareInspection(seedPath, store, cancellation: cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "pre-cancelled actual-derived phase preparation refuses");
            }
            Directory.SetCurrentDirectory(File.ReadAllText(Path.Combine(input, "replay-fixture-data.txt")));
            Paths.UpdatePaths(); Paths.ChooseMphPath(); MapAudit.ForceEveryone = true;
            var settings = DesktopGlContext.Settings(background: true);
            settings.ClientSize = new(640, 480); settings.StartVisible = true;
            settings.Title = "Explicit phase-aware custom replay Scene qualification";
            window = new NativeWindow(settings); graphics = new DesktopGraphicsSession(window); GLFW.SwapInterval(0);
            var foreground = GameState.Current; var random = Rng.Current;
            uint rng1 = Rng.Rng1, rng2 = Rng.Rng2;
            bool network = NetSession.Active;
            using var seed = PreparedCustomReplayArchive.PrepareInspection(seedPath, store);
            Check(seed.FormatVersion == 8 && seed.Resources.Context.Bootstrap.OriginKind == ReplayCustomWeaponOriginKind.Recipient,
                "exact historical CPU preparation retains explicit recipient format8");
            Reject(() => { using var denied = PassiveReplayScene.OpenCustomArchiveInspection(seed, new(640, 480)); },
                "ordinary format7 Scene factory remains closed for8");
            Reject(() => { using var denied = seed.CreateRequestSession(); }, "ordinary theatre request remains closed for8");
            var expected = new Dictionary<uint, byte[]>();
            var visibility = new List<object>();
            T Member<T>(object value, string name) => (T)(value.GetType().GetProperty(name, Hidden)?.GetValue(value)
                ?? value.GetType().GetField(name, Hidden)?.GetValue(value)
                ?? throw new InvalidDataException("Missing explicit cursor evidence member " + name));
            IEnumerable<object> Rows(object owner, string field)
            {
                object collection = owner.GetType().GetField(field, Hidden)!.GetValue(owner)!;
                foreach (object row in (IEnumerable)collection) yield return row;
            }
            void Availability(PassiveReplayScene world)
            {
                var cursor = world.Scene.ReplayPoses ?? throw new InvalidDataException("Explicit phase cursor missing.");
                Check(cursor.Prepare(), "explicit phase pose/fire readers prepare: " + cursor.LastError);
                uint frame = world.Session.RecordingFrame;
                foreach (object list in Rows(cursor, "_poses"))
                {
                    object[] samples = ((IEnumerable)list).Cast<object>().ToArray();
                    foreach (object row in samples)
                    {
                        uint available = Member<uint>(row, "Available");
                        if (available == 0) continue;
                        uint selected = Member<uint>(row, "RecordingFrame");
                        if (available > frame) futurePoseRows++;
                        var player = Member<PlayerState>(row, "State");
                        uint tick = Member<uint>(row, "ServerTick");
                        var candidates = new List<uint>();
                        foreach (uint source in selected == 0 ? new uint[] { 0 } : new[] { selected, selected - 1 })
                            if (sourcePoses.TryGetValue((player.SlotIndex, source, tick), out var raw)
                                && raw.Equals(player)) candidates.Add(source);
                        if (candidates.Count > 0)
                        {
                            Check(candidates.Any(source => availabilityBySource.TryGetValue(source, out uint floor)
                                && available == floor && selected == Math.Max(source, floor)),
                                "pose selection/source bytes match independent retained-clock availability floor");
                            sourcePoseRows++;
                        }
                    }
                    // Independent ACK-target oracle uses only already available source positions.
                    // Request a future sample tick; it must hold at the latest admitted sample,
                    // or refuse when this life has no admitted sample at all.
                    var future = samples.FirstOrDefault(row => Member<uint>(row, "Available") > frame);
                    if (future != null)
                    {
                        var player = Member<PlayerState>(future, "State");
                        uint tick = Member<uint>(future, "ServerTick");
                        var admitted = samples.Where(row => Member<uint>(row, "Available") <= frame
                            && Member<PlayerState>(row, "State").SlotGeneration == player.SlotGeneration
                            && Member<PlayerState>(row, "State").LifeId == player.LifeId).ToArray();
                        if (!admitted.Any(row => Member<uint>(row, "ServerTick") > tick))
                        {
                            var last = admitted.OrderBy(row => Member<uint>(row, "ServerTick")).LastOrDefault();
                            object?[] arguments = { (int)player.SlotIndex, (double)tick,
                                player.SlotGeneration, player.LifeId, Vector3.Zero };
                            bool sampled = (bool)typeof(ReplayPoseStream).GetMethod("TrySampleServerLifeAt", Hidden)!
                                .Invoke(cursor, arguments)!;
                            Check(last == null ? !sampled : sampled && (Vector3)arguments[4]!
                                == Member<PlayerState>(last, "State").Position,
                                "diagnostic ACK-target sampling cannot borrow unavailable pose endpoints");
                            ackAvailabilityGuards++;
                        }
                    }
                }
                foreach (object list in Rows(cursor, "_intents"))
                    foreach (object row in (IEnumerable)list)
                    {
                        uint available = Member<uint>(row, "Available");
                        if (available == 0) continue;
                        uint source = Member<uint>(row, "SourceRecordingFrame");
                        Check(availabilityBySource.TryGetValue(source, out uint floor) && available == floor
                            && Member<uint>(row, "RecordingFrame") == Math.Max(source, available),
                            "intent source clock and selection retain independent phase availability floor");
                        if (mixed && source > origin && !IsPost(source - origin)
                            && post.ContainsKey(source - origin - 1))
                        {
                            Check(available == source && available != 0,
                                "ordinary native carrier following post-world inherits current-source availability");
                            mixedInheritedRows++;
                        }
                        intentClockRows++;
                    }
                if (indexed)
                {
                    foreach (var shot in shots)
                    {
                        bool authored = cursor.TryAuthoredFire(shot.Fact, out _);
                        uint earliest = uint.MaxValue;
                        foreach (object pair in Rows(cursor, "_fires"))
                            foreach (object row in (IEnumerable)Member<object>(pair, "Value"))
                            {
                                var fire = Member<FireEvent>(row, "Event");
                                uint available = Member<uint>(row, "Available");
                                if (!mixed && available > 0 && fire.Kind != FireEventKind.TurretFire)
                                {
                                    var carrier = Member<IntentPacket>(row, "Carrier");
                                    uint authoredSource = ReplayPoseStream.FireSourceRecordingFrame(available - 1,
                                        carrier.Frame, fire.SourceFrame);
                                    Check(Member<uint>(row, "RecordingFrame") == authoredSource
                                        && Member<uint>(pair, "Key") == Math.Max(authoredSource, available),
                                        "native authored source clock stays original while schedule clamps to availability");
                                    authoredClockChecks++;
                                }
                                if (Member<int>(row, "Slot") == shot.Fact.ShooterSlot
                                    && Member<ushort>(row, "Generation") == shot.Fact.ShooterGeneration
                                    && Member<ushort>(row, "Life") == shot.Fact.ShooterLifeId
                                    && fire.ShotId == shot.Fact.ShotId)
                                    earliest = Math.Min(earliest, Member<uint>(row, "Available"));
                            }
                        if (earliest != uint.MaxValue && earliest > frame)
                        {
                            Check(!authored, "future authored native fire cannot leak through paused/current picture");
                            blockedFutureFire++;
                        }
                        if (authored) visibleAuthoredFire++;
                    }
                    foreach (object pair in Rows(cursor, "_fireCapable"))
                    {
                        object key = Member<object>(pair, "Key");
                        int slot = Member<int>(key, "Slot");
                        uint available = Member<uint>(pair, "Value");
                        if (available <= frame || !world.State.TryGetPlayer(slot, out var player)
                            || player.SlotGeneration != Member<ushort>(key, "Generation")) continue;
                        bool earlier = Rows(cursor, "_fireCapable").Any(other =>
                            Member<int>(Member<object>(other, "Key"), "Slot") == slot
                            && Member<ushort>(Member<object>(other, "Key"), "Generation") == player.SlotGeneration
                            && Member<uint>(other, "Value") <= frame);
                        if (!earlier)
                        {
                            Check(!cursor.UsesFireEvents(world.Scene.Players.Items[slot]),
                                "future native fire capability cannot suppress live fallback before availability");
                            capabilityGuards++;
                        }
                    }
                }
                if (frame == end)
                {
                    Check(Member<uint>(cursor, "_poseAvailability") <= end
                        && Member<uint>(cursor, "_shotAvailability") <= end,
                        "terminal post-world suffix cannot advance either reader availability beyond EOF");
                    if (indexed)
                        Check(typeof(ReplayPoseStream).GetField("_reader", Hidden)!.GetValue(cursor) != null
                            && typeof(ReplayPoseStream).GetField("_shotReader", Hidden)!.GetValue(cursor) != null
                            && Member<uint>(cursor, "_shotAvailability") > 0,
                            "actual indexed source exercised both independent phase lookahead readers");
                }
                if (visibility.Count < 8 && targets.Any(row => row.Frame == frame))
                    visibility.Add(new { frame, indexed, futurePoseRows, sourcePoseRows, intentClockRows,
                        blockedFutureFire, visibleAuthoredFire, capabilityGuards });
            }
            void SaveExpected(PassiveReplayScene oracle)
            {
                if (!targets.Any(row => row.Frame == oracle.Session.RecordingFrame)) return;
                using var capsule = ReplayWorldCheckpoint.CaptureCustom(oracle);
                Check(capsule.HasHeadHistoryContract == baseline.HasHeadHistoryContract
                    && capsule.IndexedFirePresentation == indexed,
                    "new phase oracle preserves actual world12/13 head-history and saved firing contracts");
                expected.Add(capsule.Frame, capsule.Bytes.ToArray());
            }
            using (var oracle = PassiveReplayScene.RestoreCustomInspection(baseline, seed.Resources, new(640, 480)))
            {
                oracle.Scene.ReplayPoses = new ReplayPoseStream(oracle, seed, indexed);
                oracle.Scene.BeginOwnerPresentation(seed.Path);
                var received = new Dictionary<int, (uint Carrier, uint Tick, ushort Generation, ushort Life)>();
                for (int slot = 0; slot < 8; slot++)
                    if (oracle.State.TryGetIntent(slot, out var held) && oracle.State.IntentAge(slot) != uint.MaxValue)
                        received[slot] = (origin - oracle.State.IntentAge(slot), held.Frame, held.SlotGeneration, held.LifeId);
                Availability(oracle); SaveExpected(oracle);
                void Apply(SourceRecord row, uint source, uint application, bool deferred)
                {
                    if (row.Bytes[0] == 254)
                    {
                        var marker = ReplayTimelineArchive.DecodeMarker(source, row.Bytes); marker.Release();
                        return;
                    }
                    var normalized = ReplayIdentityCompatibility.NormalizeLegacyParkourPose(row.Bytes, metadata.ProtocolVersion);
                    if (normalized.IsEmpty) return;
                    int slot = normalized[0] == (byte)PacketType.SlotIntent ? normalized[1] : -1;
                    bool wasHeld = slot >= 0 && oracle.State.TryGetIntent(slot, out _);
                    IntentPacket previous = default;
                    if (wasHeld) oracle.State.TryGetIntent(slot, out previous);
                    if (deferred) oracle.State.AcceptPostWorld(normalized, source, application);
                    else oracle.Session.Host.Inject(normalized, source);
                    if (slot >= 0 && oracle.State.TryGetIntent(slot, out var held)
                        && (!wasHeld || held.Frame != previous.Frame
                            || held.SlotGeneration != previous.SlotGeneration || held.LifeId != previous.LifeId))
                        received[slot] = (source, held.Frame, held.SlotGeneration, held.LifeId);
                }
                for (uint next = origin + 1; next <= end; next++)
                {
                    if (post.TryGetValue(next - origin - 1, out var deferred))
                        foreach (var row in deferred) Apply(row, next - 1, next, deferred: true);
                    if (ordinary.TryGetValue(next - origin, out var current))
                        foreach (var row in current) Apply(row, next, next, deferred: false);
                    oracle.StepCustomInspection(next, Array.Empty<ReplayTimelineRecord>());
                    foreach (var pair in received)
                        if (oracle.State.TryGetIntent(pair.Key, out var held) && held.Frame == pair.Value.Tick
                            && held.SlotGeneration == pair.Value.Generation && held.LifeId == pair.Value.Life)
                        {
                            Check(oracle.State.IntentAge(pair.Key) == next - pair.Value.Carrier,
                                "actual native intent age retains original recording clock across delayed application");
                            nativeIntentAgeChecks++;
                        }
                    Availability(oracle); SaveExpected(oracle);
                }
            }
            Check(expected.Count == targets.Length, "all bounded new phase expected targets captured from literal application oracle");
            foreach (var target in targets)
                File.WriteAllBytes(Path.Combine(output, "phase-expected-" + target.Name + "-world" + worldVersion + ".bin"), expected[target.Frame]);
            string phasePath = Path.Combine(output, "derived-phase-indexed.ppdemo");
            if (!livePhase) WriteDerived(phasePath, expected);
            else
            {
                // This comparison producer uses the typed live API, while the earlier
                // literal oracle bypassed its batch validation and phase application.
                using var live = PassiveReplayScene.RestorePostWorldInspection(baseline, seed.Resources, new(640, 480));
                live.Scene.ReplayPoses = new ReplayPoseStream(live, seed, indexed);
                live.Scene.BeginOwnerPresentation(seed.Path);
                Check(ReplayTimelineArchive.Metadata(live, ReplayType.FullMatch).FormatVersion == 8,
                    "typed live Scene metadata explicitly selects format8");
                var pump = new ReplayWritePump(phasePath, phaseMetadata, origin);
                var clock = new ReplayCapturePhaseClock(origin);
                bool finished = false;
                ReplayCaptureRecord Typed(SourceRecord row, ReplayCapturePhase phase)
                {
                    uint source = checked(origin + row.Frame);
                    ReplayTimelineRecord record = row.Bytes[0] == 254
                        ? ReplayTimelineArchive.DecodeMarker(source, row.Bytes)
                        : new(source, 0, row.Bytes[0] == ReplayCustomWeaponFact.Tag
                            ? ReplayFactKind.CustomWeapon : ReplayFactKind.Presentation, row.Bytes);
                    return new(record, phase);
                }
                void LiveCompare()
                {
                    if (!targets.Any(row => row.Frame == live.Session.RecordingFrame)) return;
                    using var desired = ReplayWorldCheckpoint.FromCustomBytes(expected[live.Session.RecordingFrame], metadata.BuildId);
                    using var actual = ReplayWorldCheckpoint.CaptureCustom(live);
                    var diff = desired.CanonicalGraphDifferences(actual);
                    string label = targets.Single(row => row.Frame == live.Session.RecordingFrame).Name;
                    File.WriteAllText(Path.Combine(output, "live-" + label + "-differences.json"), JsonSerializer.Serialize(diff));
                    Check(diff.Length == 0, "typed live capture matches literal phase oracle " + label);
                    if (actual.Frame != origin)
                    {
                        var checkpoint = new ReplayTimelineRecord(actual.Frame, 0, ReplayFactKind.World, actual.Bytes);
                        try { Check(pump.Checkpoint(checkpoint), "completed live target checkpoint queues"); }
                        finally { checkpoint.Release(); }
                    }
                }
                try
                {
                    LiveCompare();
                    for (uint next = origin + 1; next <= end; next++)
                    {
                        var batch = new List<ReplayCaptureRecord>();
                        try
                        {
                            if (post.TryGetValue(next - origin - 1, out var deferred))
                                batch.AddRange(deferred.Select(row => Typed(row, ReplayCapturePhase.PostWorld)));
                            if (ordinary.TryGetValue(next - origin, out var current))
                                batch.AddRange(current.Select(row => Typed(row, ReplayCapturePhase.Ordinary)));
                            foreach (var item in batch)
                            {
                                Check(clock.Classify(item.Record.RecordingFrame) == item.Phase,
                                    "single completed-world clock classifies original source");
                                Check(pump.Record(item), "same typed fact queues for format8 storage");
                            }
                            live.StepPostWorldInspection(next, batch);
                            clock.Complete(next);
                            Check(pump.EndFrame(next - origin), "only completed live world commits writer frontier");
                            LiveCompare();
                        }
                        finally { foreach (var item in batch) item.Release(); }
                    }
                    // Genuine final post suffix is stored, never applied to the held EOF world.
                    if (post.TryGetValue(duration, out var terminal))
                        foreach (var row in terminal)
                        {
                            var item = Typed(row, ReplayCapturePhase.PostWorld);
                            try { Check(pump.Record(item), "final inert post retains original source clock"); }
                            finally { item.Release(); }
                        }
                    pump.Complete();
                    Check(pump.Completion.Wait(TimeSpan.FromSeconds(20)), "actual async phase writer closes within bound");
                    pump.Completion.GetAwaiter().GetResult();
                    Check(pump.Error == null && pump.QueuedBytes == 0, "live phase writer releases all retained records");
                    using (var closed = DemoReader.Open(phasePath, out var openedLive))
                    {
                        int projectedEvents = 0;
                        foreach (var row in records.Where(row => row.Bytes[0] == 254))
                        {
                            var marker = ReplayTimelineArchive.DecodeMarker(origin + row.Frame, row.Bytes);
                            try { if (ReplayTimelineArchive.EventType(marker.Marker!.Value.Kind).HasValue) projectedEvents++; }
                            finally { marker.Release(); }
                        }
                        Check(closed != null && openedLive == ReplayOpenResult.Success
                            && closed.Metadata!.Events.Count == projectedEvents,
                            "typed semantic records project footer once without copying duplicate coarse source index");
                    }
                    finished = true;
                }
                finally
                {
                    if (!finished) { pump.Abort(); pump.Completion.Wait(TimeSpan.FromSeconds(20)); }
                }
            }
            Check(store.Uninstall(package.PackageHash) && store.Prune() == 0,
                "independent seed owner retains exact historical bytes after uninstall");
            void Compare(PassiveReplayScene scene, Target target, string label)
            {
                using var desired = ReplayWorldCheckpoint.FromCustomBytes(expected[target.Frame], metadata.BuildId);
                using var actual = ReplayWorldCheckpoint.CaptureCustom(scene);
                var differences = desired.CanonicalGraphDifferences(actual);
                File.WriteAllText(Path.Combine(output, label + "-differences.json"), JsonSerializer.Serialize(differences));
                if (differences.Length != 0) File.WriteAllBytes(Path.Combine(output, label + "-actual-world" + worldVersion + ".bin"), actual.Bytes.ToArray());
                Check(differences.Length == 0 && scene.Session.RecordingFrame == target.Frame,
                    "strict phase world/clock parity " + label);
            }
            foreach (var target in targets.Reverse())
            {
                using var prepared = PreparedCustomReplayArchive.PrepareInspection(phasePath, store, target.Frame);
                using var chosen = prepared.CopyWorld();
                Check(chosen.Frame == target.Frame, "phase target selects exact independent durable world K");
                using var world = PassiveReplayScene.OpenPostWorldArchiveInspection(prepared, new(640, 480));
                prepared.Dispose(); Compare(world, target, "cold-" + target.Name);
                byte[] paused = world.State.CaptureCheckpoint().Bytes.ToArray();
                Availability(world); Availability(world);
                Check(world.State.CaptureCheckpoint().Bytes.SequenceEqual(paused),
                    "paused checkpoint K pose lookahead leaves deferred K decoder unchanged");
                for (int draw = 0; draw < 2; draw++)
                {
                    Vector3 point = world.Scene.Players.Items[0].Position + Vector3.UnitY;
                    world.Scene.SetReplicaCamera(point + (draw == 0 ? new Vector3(3, 2, 4) : new Vector3(70, 30, 90)), point, 55);
                    using var before = ReplayWorldCheckpoint.CaptureCustom(world);
                    world.Scene.OnDrawFrame();
                    Check(world.Scene.OnRenderFrame() && GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError,
                        "actual Windows GL explicit phase Scene draw");
                    if (draw == 0) Check(ScreenCapture.SaveWindow(world.Scene,
                        Path.Combine(output, target.Name + "-window.png")), "actual phase picture retained");
                    DesktopGraphicsSession.Present(window); world.Scene.AfterRenderFrame();
                    using var after = ReplayWorldCheckpoint.CaptureCustom(world);
                    var differences = before.CanonicalGraphDifferences(after);
                    File.WriteAllText(Path.Combine(output, target.Name + "-draw" + draw + "-differences.json"), JsonSerializer.Serialize(differences));
                    Check(differences.Length == 0, "strict canonical phase graph unchanged by repeated near/far draw");
                    Check(!ReplayAudioOwner.MayPlay(world.Scene), "explicit phase replay remains silent");
                    drawings++;
                }
                while (!world.Session.AtEnd)
                {
                    Check(world.StepCustomArchiveInspection(), "explicit phase genuine-payload suffix advances");
                    if (targets.FirstOrDefault(row => row.Frame == world.Session.RecordingFrame) is { } reached)
                        Compare(world, reached, "suffix-" + target.Name + "-to-" + reached.Name);
                }
                Compare(world, targets.Single(row => row.Name == "end"), "final-" + target.Name);
                byte[] ended = world.State.CaptureCheckpoint().Bytes.ToArray();
                Check(!world.StepCustomArchiveInspection() && world.State.CaptureCheckpoint().Bytes.SequenceEqual(ended),
                    "terminal post-world payloads never apply without a following step");
            }
            // Cropped EOF controls retain actual native bytes and their unchanged outer clocks.
            // Every packet at the cropped final frame is deliberately post-world and must
            // never enter either reader's identity collections without a following step.
            var eofControls = new List<object>();
            void TerminalControl(string label, uint cut, ReplayShotFact? terminalFact = null,
                int fireSlot = -1, ushort generation = 0, ushort life = 0, uint shotId = 0)
            {
                Check(cut > 0 && cut <= duration, "bounded actual native terminal control frontier");
                string cropped = Path.Combine(output, label + "-terminal-phase.ppdemo");
                using (var writer = new ReplayWriterV3(cropped, phaseMetadata))
                {
                    foreach (var row in records.Where(row => row.Frame <= cut && !IsQuiet(row)))
                        writer.WriteRecord(row.Frame, ReplayPostWorldRecord.Encode(row.Bytes, context));
                    writer.WriteRecord(cut, new byte[] { 255 });
                }
                using var prepared = PreparedCustomReplayArchive.PrepareInspection(cropped, store);
                using var world = PassiveReplayScene.OpenPostWorldArchiveInspection(prepared, new(640, 480));
                while (!world.Session.AtEnd)
                    Check(world.StepCustomArchiveInspection(), "cropped actual-payload EOF control advances");
                var cursor = world.Scene.ReplayPoses!;
                using var before = ReplayWorldCheckpoint.CaptureCustom(world);
                for (int retry = 0; retry < 3; retry++)
                {
                    Check(cursor.Prepare(), "repeated terminal cursor Prepare remains healthy");
                    if (terminalFact is { } fact)
                    {
                        bool SameFact(ReplayShotFact value) => value.MatchId == fact.MatchId
                            && value.AuthorityEpoch == fact.AuthorityEpoch && value.DamageEventId == fact.DamageEventId
                            && value.ShooterSlot == fact.ShooterSlot && value.ShooterGeneration == fact.ShooterGeneration
                            && value.ShooterLifeId == fact.ShooterLifeId && value.VictimSlot == fact.VictimSlot;
                        Check(!Rows(cursor, "_seenShotFacts").Any(identity =>
                                Member<ushort>(identity, "MatchId") == fact.MatchId
                                && Member<ulong>(identity, "Epoch") == fact.AuthorityEpoch
                                && Member<ushort>(identity, "DamageEventId") == fact.DamageEventId
                                && Member<byte>(identity, "Shooter") == fact.ShooterSlot),
                            "actual first terminal native ShotFact never enters seen identity set");
                        Check(!Rows(cursor, "_pendingShotFacts").Any(row => SameFact(Member<ReplayShotFact>(row, "Fact")))
                            && !Rows(cursor, "_resolvedShotFacts").Any(pair =>
                                ((IEnumerable)Member<object>(pair, "Value")).Cast<ReplayShotFact>().Any(SameFact)),
                            "actual terminal native ShotFact remains absent from pending and resolved collections");
                    }
                    if (fireSlot >= 0)
                    {
                        Check(!Rows(cursor, "_fires").Any(pair => ((IEnumerable)Member<object>(pair, "Value"))
                            .Cast<object>().Any(row => Member<int>(row, "Slot") == fireSlot
                                && Member<ushort>(row, "Generation") == generation
                                && Member<ushort>(row, "Life") == life
                                && Member<FireEvent>(row, "Event").ShotId == shotId)),
                            "actual first terminal native authored fire never enters scheduled identity index");
                        object index = ((Array)typeof(ReplayPoseStream).GetField("_fireIndex", Hidden)!
                            .GetValue(cursor)!).GetValue(fireSlot)!;
                        Check(!Rows(index, "Histories").Where(pair => Member<ushort>(pair, "Key") == life)
                            .Any(pair => Rows(Member<object>(pair, "Value"), "_entries").Any(entry =>
                                Member<bool>(entry, "Present") && Member<FireEvent>(entry, "Fire").ShotId == shotId)),
                            "actual terminal fire is absent even from consumed/pending dedupe history");
                        uint? priorCapability = null;
                        var literal = new ReplayReplicaState();
                        literal.RestoreCustomCheckpoint(baseline.DecoderState());
                        literal.RewindCustomPresentation();
                        foreach (var row in records.Where(row => row.Frame < cut))
                        {
                            PacketType type = (PacketType)row.Bytes[0];
                            if (type is not (PacketType.MatchState or PacketType.Roster
                                or PacketType.Snapshot or PacketType.SlotIntent)) continue;
                            long accepted = literal.AcceptedPackets;
                            var normalized = ReplayIdentityCompatibility.NormalizeLegacyParkourPose(row.Bytes, metadata.ProtocolVersion);
                            if (normalized.IsEmpty) continue;
                            literal.Accept(normalized, origin + row.Frame);
                            if (literal.AcceptedPackets == accepted || type != PacketType.SlotIntent
                                || row.Bytes[1] != fireSlot) continue;
                            var intent = IntentPacket.Read(normalized[2..]);
                            if (!intent.HasFireEvents || !NetFireEvents.Validate(intent)
                                || intent.SlotGeneration != generation || intent.LifeId != life) continue;
                            uint available = origin + row.Frame + 1;
                            priorCapability = priorCapability.HasValue ? Math.Min(priorCapability.Value, available) : available;
                        }
                        var capabilities = Rows(cursor, "_fireCapable").Where(pair =>
                            Member<int>(Member<object>(pair, "Key"), "Slot") == fireSlot
                            && Member<ushort>(Member<object>(pair, "Key"), "Generation") == generation
                            && Member<ushort>(Member<object>(pair, "Key"), "Life") == life).ToArray();
                        File.WriteAllText(Path.Combine(output, label + "-terminal-capability-provenance.json"),
                            JsonSerializer.Serialize(new { cut, fireSlot, generation, life, priorCapability,
                                actual = capabilities.Select(pair => Member<uint>(pair, "Value")).ToArray(),
                                oracle = "Literal admitted native-four-family decoder; ignores unknown-life and stale carriers." }));
                        Check(priorCapability.HasValue
                            ? capabilities.Any(pair => Member<uint>(pair, "Value") == priorCapability.Value)
                            : capabilities.Length == 0,
                            "terminal native carrier cannot invent or change pre-EOF capability provenance");
                    }
                    Check(!world.StepCustomArchiveInspection(), "cropped EOF cannot advance into terminal payloads");
                }
                using var after = ReplayWorldCheckpoint.CaptureCustom(world);
                var differences = before.CanonicalGraphDifferences(after);
                File.WriteAllText(Path.Combine(output, label + "-terminal-differences.json"), JsonSerializer.Serialize(differences));
                Check(differences.Length == 0, "terminal native cursor retries leave strict canonical world held");
                eofControls.Add(new { label, cut, absolute = origin + cut,
                    fact = terminalFact?.DamageEventId, fireSlot, generation, life, shotId });
            }
            // Indexed source is required for native identity-index assertions; unindexed
            // input still exercises terminal canonical hold and its pose reader.
            if (indexed)
            {
                if (shots.FirstOrDefault() is { } firstFact)
                    TerminalControl("actual-native-fact", firstFact.Source - origin, terminalFact: firstFact.Fact);
                bool nativeFire = false;
                foreach (var row in records)
                {
                    if (row.Bytes.Length < 2 + IntentPacket.TouchCanonicalSize
                        || row.Bytes[0] != (byte)PacketType.SlotIntent) continue;
                    var intent = IntentPacket.Read(row.Bytes.AsSpan(2));
                    if (intent.FireEventCount == 0) continue;
                    TerminalControl("actual-native-fire", row.Frame, fireSlot: row.Bytes[1],
                        generation: intent.SlotGeneration, life: intent.LifeId, shotId: intent.FireEvents[0].ShotId);
                    nativeFire = true;
                    break;
                }
                Check(shots.Count > 0 && nativeFire && eofControls.Count == 2,
                    "actual first native fact and authored-fire EOF controls both exercised");
            }
            seed.Dispose();
            Check(store.Prune() == 1, "last explicit phase owner releases exact CPU model/audio archive lease");
            Check(sourcePoseRows > 0 && futurePoseRows > 0 && intentClockRows > 0 && ackAvailabilityGuards > 0
                && nativeIntentAgeChecks > 0,
                "actual source snapshot/intent and future-lookahead availability evidence observed");
            if (mixed) Check(mixedInheritedRows > 0,
                "mixed ordinary native carriers exercised inherited availability after prior post-world records");
            if (indexed) Check(shots.Count > 0 && blockedFutureFire > 0 && visibleAuthoredFire > 0
                && (mixed || authoredClockChecks > 0),
                "actual indexed native fire exercised unavailable and admitted authored identity paths");
            Check(ReferenceEquals(foreground, GameState.Current) && ReferenceEquals(random, Rng.Current)
                && rng1 == Rng.Rng1 && rng2 == Rng.Rng2 && network == NetSession.Active,
                "explicit phase Scene inspection preserves foreground game RNG and network owners");
            Check(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(sourcePath))) == sourceHash,
                "genuine source archive remains byte-identical");
            File.WriteAllText(Path.Combine(output, "phase-scene-results.json"), JsonSerializer.Serialize(new
            {
                checks, sourcePath, sourceHash, records = records.Count, payloadBytes, origin, end, indexed, mixed,
                worldVersion, requireIndexed, livePhase,
                drawings, sourcePoseRows, futurePoseRows, intentClockRows, blockedFutureFire,
                terminalNonquietSourceRecords = records.Count(row => row.Frame == duration && !IsQuiet(row)),
                visibleAuthoredFire, capabilityGuards, ackAvailabilityGuards, authoredClockChecks, nativeIntentAgeChecks, mixedInheritedRows, visibility, eofControls,
                targetWorlds = targets.Select(row => new { row.Name, row.Frame,
                    filename = "phase-expected-" + row.Name + "-world" + worldVersion + ".bin" }),
                omittedSourceExpectedHashes = metadata.ExpectedHashes.Count,
                scope = "Actual retained native/custom payloads with unchanged outer/source clocks, deliberately wrapped post-world arrival. Literal decoder ordering oracle bypasses file phase/session handling but shares engine and pose projection. New expected worlds are phase-derived, not original server worlds. Historical world12 remains unseeded legacy history; actual world13 retains its seeded contract. Strict cold/durable/suffix/GL parity plus independent source/availability guards; recipient explicit inspection only, no authority producer/player/theatre admission."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS {checks} explicit phase-aware custom replay Scene checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("POST-WORLD SCENE FAIL " + error); return 1; }
        finally
        {
            AudioLifetime.Shutdown(); graphics?.Dispose(); window?.Dispose();
            Directory.SetCurrentDirectory(cwd);
        }
    }
}
