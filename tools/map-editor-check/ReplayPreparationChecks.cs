using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

internal static class ReplayPreparationChecks
{
    private static readonly Vector2i Size = new(256, 192);
    internal static void WriteCoverageFixture(string source, string output)
    {
        Paths.UpdatePaths(); Paths.ChooseMphPath(); Headless.Enter();
        using var seed = new PassiveReplayScene(source, Size);
        if (!seed.Session.HasSimulatedFrame) seed.Step();
        if (!seed.State.TryGetPlayer(0, out var actor)) throw new InvalidDataException("Coverage source needs an accepted actor.");
        ReplayWorldCoverageCheck.Write(output, seed.State.Match!.Value.RoomKey, GameMode.Battle, actor.Position);
        Console.WriteLine("Wrote 1800-frame eight-hunter coverage fixture using the supplied asset room and accepted actor position.");
    }
    internal static void Run(Action<bool, string> check, string? source)
    {
        string folder = Path.Combine(Path.GetTempPath(), "prime-replay-preparation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "reader.ppdemo");
            var match = new MatchStatePacket { RoomKey = "MP1 SANCTORUS", NextRoomKey = "", Mode = (byte)GameMode.Battle,
                MatchId = 1, AuthorityEpoch = 1, PlayerCount = 8, Flags = MatchStatePacket.FlagInProgress };
            byte[] packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState; match.Write(packet.AsSpan(1));
            using (var writer = new ReplayWriterV3(path, new ReplayMetadata { RoomKey = match.RoomKey, Mode = GameMode.Battle,
                Bootstrap = new ReplayBootstrap { Packets = [packet] } })) writer.WriteRecord(1, packet);
            int owner = Environment.CurrentManagedThreadId;
            using (var job = ReplayPreparationJob.File(path))
            using (var prepared = job.WaitCompleted())
            {
                check(prepared.Session.IsActive, "detached preparation owns a complete private session");
                check(!Exclusive(path), "prepared source retains its reader until adoption/disposal");
                bool rejected = Task.Run(() =>
                { try { prepared.AdoptSession(owner); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
                check(rejected && prepared.Session.IsActive, "wrong-thread adoption fails before mutating prepared ownership");
            }
            check(Exclusive(path), "disposing a taken result releases the reader");
            var throwingHost = new ThrowingStopHost();
            var cleanupSession = new ReplayPlaybackSession(throwingHost);
            check(cleanupSession.JoinDetached(path, default) && !Exclusive(path), "host cleanup failure fixture owns an active reader");
            throwingHost.Throw = true; bool stopFailed = false;
            try { cleanupSession.Dispose(); } catch (IOException ex) { stopFailed = ex.Message == "fixture host stop"; }
            check(stopFailed && !cleanupSession.IsActive && Exclusive(path)
                && cleanupSession.Transport.State == ReplayState.Inactive, "host cleanup exception still stops transport and releases the reader");
            string missingOrigin = Path.Combine(folder, "missing-required-origin.ppdemo");
            using (var writer = new ReplayWriterV3(missingOrigin, new ReplayMetadata { FormatVersion = 4,
                Type = ReplayType.Clip, RoomKey = match.RoomKey, Mode = GameMode.Battle,
                Bootstrap = new ReplayBootstrap { Packets = [packet] } })) writer.WriteRecord(1, packet);
            using (var refused = new ReplayPlaybackSession(new PassiveReplaySessionHost()))
                check(!refused.JoinDetached(missingOrigin, default) && refused.LastResult == ReplayOpenResult.StateMismatch
                    && Exclusive(missingOrigin), "missing v4 world cannot masquerade as the explicit single-construction packet-origin contract");

            using (var opened = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                bool workerGuard = false;
                using var job = ReplayPreparationJob.Start(_ =>
                {
                    try { PreparedReplaySource.RequireOwner(Environment.CurrentManagedThreadId); }
                    catch (InvalidOperationException) { workerGuard = true; }
                    var prepared = PreparedReplaySource.File(path);
                    opened.Set(); release.Wait(); return prepared; // deliberately ignores cancellation after read
                });
                check(opened.Wait(TimeSpan.FromSeconds(10)), "late-cancellation fixture reaches its retained reader");
                check(workerGuard && !Exclusive(path), "worker cannot act as a scene owner and owns its detached reader");
                job.Dispose(); release.Set();
                Wait(() => job.Completed && Exclusive(path), "abandoned completed result cleanup");
                check(Exclusive(path), "cancellation that loses completion race disposes the result reader");
                job.Dispose();
            }

            using (var admitted = new CountdownEvent(2))
            using (var release = new ManualResetEventSlim())
            using (var first = BlockWorker(admitted, release, path))
            using (var second = BlockWorker(admitted, release, path))
            {
                check(admitted.Wait(TimeSpan.FromSeconds(10)), "two bounded workers admitted");
                bool queuedRan = false;
                using var queued = ReplayPreparationJob.Start(token => { queuedRan = true; return PreparedReplaySource.File(path, cancellation: token); });
                queued.Dispose(); Wait(() => queued.Completed, "queued cancellation");
                check(!queuedRan, "canceled queued preparation never enters its decoder");
                release.Set(); Wait(() => first.Completed && second.Completed, "worker release");
            }
            Wait(() => Exclusive(path), "bounded worker reader cleanup");
            check(Exclusive(path), "all private worker readers release after cancellation/admission");
            using (var failure = ReplayPreparationJob.Start(_ => throw new IOException("fixture failure")))
            {
                Wait(() => failure.Completed, "fault observation");
                bool observed = false; try { failure.TakeCompleted(); } catch (IOException ex) { observed = ex.Message == "fixture failure"; }
                check(observed, "decoder faults remain observable without publishing a result");
            }
            var record = new ReplayTimelineRecord(0, 0, ReplayFactKind.World, new byte[] { 4, 5, 6 });
            var restore = new ReplayRestorePoint(0, 0, ReplayRestoreKind.ReplicaCheckpoint, [record]);
            var clip = new ReplayTimelineClip(restore, [], 0, 0);
            var retained = PreparedReplaySource.Retain(clip);
            clip.Dispose(); restore.Dispose(); record.Release();
            check(retained.RestorePoint.Records[0].Payload.SequenceEqual(new byte[] { 4, 5, 6 }), "detached clip owns bytes after source leases close");
            retained.Dispose();
            bool returned = false; try { _ = record.Payload.Length; } catch (ObjectDisposedException) { returned = true; }
            check(returned, "final clip lease disposal returns the pooled payload");
            ReplayMapPreparationChecks.Run(check, folder);
            if (source != null)
            {
                AssetWorld(check, Path.GetFullPath(source), folder);
                ReplayAcceptedFireChecks.Run(check, Path.GetFullPath(source), folder);
            }
        }
        finally { Directory.Delete(folder, true); }
    }
    private static ReplayPreparationJob BlockWorker(CountdownEvent admitted, ManualResetEventSlim release, string path)
        => ReplayPreparationJob.Start(_ => { admitted.Signal(); release.Wait(); return PreparedReplaySource.File(path); });
    private sealed class ThrowingStopHost : IReplaySessionHost
    {
        private readonly PassiveReplaySessionHost _inner = new();
        internal bool Throw;
        public bool IsPassive => true;
        public MatchStatePacket? Match => _inner.Match;
        public void Prepare(string path, bool pathChanged) => _inner.Prepare(path, pathChanged);
        public void Start() => _inner.Start();
        public void Stop() { _inner.Stop(); if (Throw) throw new IOException("fixture host stop"); }
        public void Rewind() => _inner.Rewind();
        public void Inject(ReadOnlySpan<byte> packet, uint frame) => _inner.Inject(packet, frame);
        public void Advance(double seconds) => _inner.Advance(seconds);
        public void RestoreClock(uint frame) => _inner.RestoreClock(frame);
        public void ResetDiagnostics() => _inner.ResetDiagnostics();
        public void SeekTo(uint frame) => _inner.SeekTo(frame);
    }
    private static bool Exclusive(string path)
    { try { using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; } catch (IOException) { return false; } }
    private static void Wait(Func<bool> complete, string operation)
    {
        var timeout = Stopwatch.StartNew();
        while (!complete()) { if (timeout.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException(operation); Thread.Sleep(1); }
    }
    private static void Complete(PassiveReplayPlayer player)
        => Wait(() => { if (player.Ready) return true; player.Update(24, 1); return player.Ready; }, "staged replay completion");

    private static void AssetWorld(Action<bool, string> check, string path, string folder)
    {
        Paths.UpdatePaths(); Paths.ChooseMphPath();
        Headless.Enter();
        using var reference = new PassiveReplayPlayer(path, Size);
        using var job = ReplayPreparationJob.File(path);
        using var prepared = job.WaitCompleted();
        using var staged = new PassiveReplayPlayer(prepared, Size, new(EnableAsyncPreparation: true));
        Complete(reference); Complete(staged);
        check(ReplayStateHash.Compute(staged.Current.Scene, staged.Current.Session.CurrentFrame)
            == ReplayStateHash.Compute(reference.Current.Scene, reference.Current.Session.CurrentFrame), "detached initial preparation preserves actual asset world hash");
        uint target = Math.Min(1800u, staged.Current.Session.LastFrame);
        reference.Seek(target); Complete(reference); staged.Seek(target); Complete(staged);
        check(staged.Current.Session.CurrentFrame == target && ReplayStateHash.Compute(staged.Current.Scene, target)
            == ReplayStateHash.Compute(reference.Current.Scene, target), "staged indexed seek matches synchronous actual world");
        uint superseded = Math.Min(300u, target), final = Math.Min(600u, target);
        string publishedHash = ReplayStateHash.Compute(staged.Current.Scene, target); var published = staged.Current;
        using (var admitted = new CountdownEvent(2))
        using (var release = new ManualResetEventSlim())
        using (var first = BlockWorker(admitted, release, path))
        using (var second = BlockWorker(admitted, release, path))
        {
            check(admitted.Wait(TimeSpan.FromSeconds(10)), "seek supersession workers are deterministically blocked");
            staged.Seek(superseded); staged.Update(24, 1);
            check(staged.Preparing && staged.CanPresent && ReferenceEquals(staged.Current, published)
                && ReplayStateHash.Compute(staged.Current.Scene, target) == publishedHash, "pending seek preserves the published world and presentation");
            staged.Seek(final); staged.Update(24, 1);
            check(staged.Preparing && ReferenceEquals(staged.Current, published), "superseding seek cannot commit abandoned preparation");
            release.Set(); Wait(() => first.Completed && second.Completed, "supersession worker release");
            Complete(staged);
        }
        reference.Seek(final); Complete(reference);
        check(staged.Current.Session.CurrentFrame == final && ReplayStateHash.Compute(staged.Current.Scene, final)
            == ReplayStateHash.Compute(reference.Current.Scene, final), "latest staged seek alone commits the matching world hash");
        using var capsule = ReplayWorldCheckpoint.Capture(staged.Current);
        var worldRecord = new ReplayTimelineRecord(capsule.Frame, staged.Current.State.ServerTick, ReplayFactKind.World, capsule.Bytes);
        using var restore = new ReplayRestorePoint(capsule.Frame, staged.Current.State.ServerTick, ReplayRestoreKind.ReplicaCheckpoint, [worldRecord]);
        using var clip = new ReplayTimelineClip(restore, [], capsule.Frame, capsule.Frame);
        worldRecord.Release();
        using var clipJob = ReplayPreparationJob.Clip(clip); clip.Dispose();
        using var clipPrepared = clipJob.WaitCompleted();
        using var clipPlayer = new PassiveReplayPlayer(clipPrepared, Size, ReplayPlayerOptions.Linear);
        Complete(clipPlayer);
        check(ReplayStateHash.Compute(clipPlayer.Current.Scene, capsule.Frame) == ReplayStateHash.Compute(staged.Current.Scene, final),
            "staged frozen world retains its source lease and restores actual gameplay hash");
        check(clipPlayer.Current.Scene.ReplayPresentationHash(capsule.Frame) == staged.Current.Scene.ReplayPresentationHash(final),
            "staged frozen world restores actual animation/effect projection");
        var shell = new Scene(Size, MphRead.Mods.Input.SyntheticInput.CreateKeyboard(), MphRead.Mods.Input.SyntheticInput.CreateMouse(),
            _ => { }, () => { }, initializeRuntime: false);
        try
        {
            check(Task.Run(() => DemoPlayback.Join(path)).GetAwaiter().GetResult(), "production Studio Join prepares on a worker");
            // Historical clips can have a required hidden lead-in. Publication
            // follows bounded owner updates after that world finishes warming.
            Wait(() => { DemoPlayback.Update(shell); return DemoPlayback.PresentationScene != null; }, "production replay publication");
            DemoPlayback.Update(shell); var presented = DemoPlayback.PresentationScene;
            check(presented != null, "production owner update adopts the prepared world");
            DemoPlayback.Session.Transport.Pause();
            uint presentedFrame = DemoPlayback.CurrentFrame; string presentedHash = ReplayStateHash.Compute(presented!, presentedFrame);
            check(!Task.Run(() => DemoPlayback.Join(Path.Combine(folder, "missing.ppdemo"))).GetAwaiter().GetResult()
                && ReferenceEquals(DemoPlayback.PresentationScene, presented) && ReplayStateHash.Compute(presented!, presentedFrame) == presentedHash,
                "failed worker Join leaves the published scene and hash alive");
            var metadata = staged.Current.Session.Metadata!;
            var rejectedMatch = staged.Current.State.Match!.Value;
            byte[] rejectedMatchPacket = new byte[1 + MatchStatePacket.Size]; rejectedMatchPacket[0] = (byte)PacketType.MatchState;
            rejectedMatch.Write(rejectedMatchPacket.AsSpan(1));
            var currentBootstrap = new ReplayBootstrap
                { Packets = [.. ReplayBootstrap.FromConstruction(staged.Current.InitialState).Packets, rejectedMatchPacket] };
            string invalid = Path.Combine(folder, "wrong-map.ppdemo");
            using (var reader = DemoReader.Open(path)!)
            using (var writer = new ReplayWriterV3(invalid, new ReplayMetadata { RoomKey = metadata.RoomKey, Mode = metadata.Mode,
                MapHash = ReplayMapIdentity.Compute(metadata.RoomKey) ^ 1UL,
                Bootstrap = currentBootstrap }))
            {
                var firstRecord = reader.ReadNext()!.Value;
                writer.WriteRecord(firstRecord.Frame, ReplayIdentityCompatibility.Convert(firstRecord.Data, reader.ProtocolVersion));
            }
            // Map preparation can refuse a bad map before owner adoption.
            // Both paths must keep the previously published world intact.
            _ = Task.Run(() => DemoPlayback.Join(invalid)).GetAwaiter().GetResult();
            DemoPlayback.Update(shell);
            check(DemoPlayback.LastResult == ReplayOpenResult.MapHashMismatch && ReferenceEquals(DemoPlayback.PresentationScene, presented)
                && ReplayStateHash.Compute(presented!, presentedFrame) == presentedHash,
                "failed owner candidate validation preserves the old presented world: " + DemoPlayback.LastResult + " / " + DemoPlayback.LastError);
            string malformed = Path.Combine(folder, "malformed-origin.ppdemo");
            using (var writer = new ReplayWriterV3(malformed, new ReplayMetadata { FormatVersion = 4, RoomKey = metadata.RoomKey,
                Mode = metadata.Mode, MapHash = metadata.MapHash, Bootstrap = currentBootstrap,
                OriginRecordingFrame = capsule.Frame, LeadInFrames = 1, WorldCheckpoint = capsule.Bytes[..^1].ToArray() }))
            { writer.WriteRecord(1, rejectedMatchPacket); writer.WriteCheckpoint(0, capsule.Bytes); }
            bool syncRejected = false;
            try { using var invalidPlayer = new PassiveReplayPlayer(malformed, Size); }
            catch (IOException) { syncRejected = true; }
            check(syncRejected, "synchronous playback refuses valid-header origin with malformed late graph");
            bool stagedRejected = false;
            using (var malformedJob = ReplayPreparationJob.File(malformed, verifiedOrigin: capsule.Bytes.ToArray()))
            using (var malformedPrepared = malformedJob.WaitCompleted())
            {
                check(malformedPrepared.Durable.HasValue, "malformed-origin fixture carries a valid optional checkpoint");
                try { using var invalidPlayer = new PassiveReplayPlayer(malformedPrepared, Size, new(EnableAsyncPreparation: true)); }
                catch (IOException) { stagedRejected = true; }
            }
            check(stagedRejected, "changed required origin is fully bound before adopting a valid optional capsule");
            check(Task.Run(() => DemoPlayback.Join(malformed)).GetAwaiter().GetResult(), "malformed late origin reaches owner graph validation");
            DemoPlayback.Update(shell);
            check(ReferenceEquals(DemoPlayback.PresentationScene, presented) && ReplayStateHash.Compute(presented!, presentedFrame) == presentedHash,
                "required-origin failure with valid optional capsule preserves the old published world");
            string warming = Path.Combine(folder, "warming.ppdemo");
            using (var writer = new ReplayWriterV3(warming, new ReplayMetadata { FormatVersion = 4, RoomKey = metadata.RoomKey,
                Mode = metadata.Mode, MapHash = metadata.MapHash, Bootstrap = currentBootstrap,
                OriginRecordingFrame = capsule.Frame, LeadInFrames = 300, WorldCheckpoint = capsule.Bytes.ToArray() }))
                for (uint frame = 1; frame <= 301; frame++) writer.WriteRecord(frame, rejectedMatchPacket);
            check(Task.Run(() => DemoPlayback.Join(warming)).GetAwaiter().GetResult(), "supersession fixture prepares a required hidden lead-in");
            DemoPlayback.Update(shell);
            check(DemoPlayback.Session.IsWarming && ReferenceEquals(DemoPlayback.PresentationScene, presented),
                "unpublished owner candidate warms while the old world stays visible");
            check(!Task.Run(() => DemoPlayback.Join(Path.Combine(folder, "newer-missing.ppdemo"))).GetAwaiter().GetResult(),
                "newer failed join invalidates an older owner candidate generation");
            DemoPlayback.Update(shell);
            check(ReferenceEquals(DemoPlayback.PresentationScene, presented) && ReplayStateHash.Compute(presented!, presentedFrame) == presentedHash,
                "superseded owner candidate cannot publish after a newer request fails");
        }
        finally { try { DemoPlayback.Stop(); } finally { try { shell.DoCleanup(); } finally { shell.UnloadGl(); } } }
        Console.WriteLine("Staged asset-world initial/indexed/superseded seek and frozen clip gameplay/presentation hashes match.");
    }
}
