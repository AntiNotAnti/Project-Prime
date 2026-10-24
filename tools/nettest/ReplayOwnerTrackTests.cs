using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using MphRead.Mods.Replay;
using MphRead.Mods.Cosmetics;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ReplayOwnerTrackTests
{
    private static int _checks;
    private static void Check(bool ok, string message)
    { if (!ok) throw new InvalidDataException(message); _checks++; }
    private static ReplayOwnerPose Pose(double frame = 10) => new(frame, frame / 60, 7, 99, 2, 3, 4,
        Matrix4.Identity, 1.2f, 1.1f, Vector3.One, -Vector3.UnitZ, Vector3.UnitY);
    public static int Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "prime-owner-track-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            _checks = 0;
            string source = Path.Combine(directory, "synthetic.ppdemo"), sidecar = source + ReplayOwnerTrack.Extension;
            byte[] replay = { 1, 3, 5, 7, 9 }; File.WriteAllBytes(source, replay);
            Check(ReplayOwnerTrack.Load(source) == null, "missing track falls back");
            var track = new ReplayOwnerTrack(2);
            Check(track.Add(Pose()) && track.Add(Pose(11) with { Life = 5, GunPosition = new Vector3(8) }), "two real picture samples");
            Check(!track.Add(Pose(12)) && track.Dropped == 1 && track.Count == 2, "capacity budget drops rather than grows");
            Check(!track.Add(Pose(11)) && !track.Add(Pose(9)), "duplicate/reversed canonical clocks refused");
            Check(track.Sample(10.75, 7, 99, 2, 3, 4, out var held) && held == Pose(), "nearest previous picture held without interpolation");
            Check(track.Sample(11, 7, 99, 2, 3, 5, out var next) && next.GunPosition.X == 8, "next life has its own picture");
            Check(!track.Sample(11, 7, 99, 2, 3, 4, out _), "no interpolation or hold across life boundary");
            Check(track.Sample(10, 7, 99, 2, 3, 4, out held) && held == Pose(), "backward sampling independent of previous cursor");
            Check(!track.Sample(9, 7, 99, 2, 3, 4, out _) && !track.Sample(12.01, 7, 99, 2, 3, 5, out _), "missing time coverage falls back");
            Check(!track.Sample(double.NaN, 7, 99, 2, 3, 4, out _), "nonfinite sample time refused");
            Check(!track.Sample(10, 8, 99, 2, 3, 4, out _) && !track.Sample(10, 7, 100, 2, 3, 4, out _)
                && !track.Sample(10, 7, 99, 1, 3, 4, out _) && !track.Sample(10, 7, 99, 2, 4, 4, out _)
                && !track.Sample(10, 7, 99, 2, 3, 5, out _), "all match epoch slot generation life identities required");
            foreach (var bad in new[] { Pose() with { Frame = double.PositiveInfinity }, Pose() with { RenderSeconds = double.NaN },
                Pose() with { WorldFov = float.NaN }, Pose() with { GunPosition = new Vector3(float.PositiveInfinity) },
                Pose() with { GunFacing = Vector3.Zero }, Pose() with { GunUp = -Vector3.UnitZ }, Pose() with { Slot = 8 },
                Pose() with { Match = 0 }, Pose() with { Epoch = 0 }, Pose() with { Generation = 0 }, Pose() with { Life = 0 } })
                Check(!new ReplayOwnerTrack(1).Add(bad), "invalid presentation sample rejected");
            var matrix = Matrix4.Identity; matrix.M11 = 2;
            Check(!ReplayOwnerTrack.Valid(Pose() with { View = matrix }), "scaled camera refused");
            matrix = Matrix4.Identity; matrix.M12 = .2f;
            Check(!ReplayOwnerTrack.Valid(Pose() with { View = matrix }), "sheared camera refused");
            matrix = Matrix4.Identity; matrix.M11 = float.NaN;
            Check(!ReplayOwnerTrack.Valid(Pose() with { View = matrix }), "nonfinite matrix refused");
            var clock = new ReplayOwnerTrack(2); clock.Add(Pose());
            Check(!clock.Add(Pose(11) with { RenderSeconds = 0 }), "render time cannot rewind");
            // Empty synthetic model exercises the real animation capture/apply code without ROM assets.
            var model = new ModelInstance(new Model("owner-test", false, default, [], [], [], [], [],
                new AnimationResults(), [], [], [], [], [], [], []));
            var originalAnimation = ReplayOwnerAnimation.Capture(model);
            Check(originalAnimation.Valid && originalAnimation.CanApply(model), "asset-free animation snapshot can apply");
            var changedAnimation = originalAnimation with {
                A = new(7, 2, 8, AnimFlags.Reverse | AnimFlags.Paused, 3),
                B = new(8, 4, 9, AnimFlags.NoLoop, 2), Node = new(-1, 1),
                Material = new(-1, 1), Texture = new(-1, 1), Texcoord = new(-1, 1), From = 4, To = 4 };
            changedAnimation.Apply(model);
            Check(ReplayOwnerAnimation.Capture(model) == changedAnimation, "animation apply installs both slots and all group bindings");
            originalAnimation.Apply(model);
            Check(ReplayOwnerAnimation.Capture(model) == originalAnimation, "animation restoration returns exact original state");
            Check(!(changedAnimation with { Node = new(0, 0) }).CanApply(model), "unavailable animation asset group refused");
            for (int i = 0; i < 100; i++) _ = ReplayOwnerAnimation.Capture(model);
            long captureAllocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) _ = ReplayOwnerAnimation.Capture(model);
            Check(GC.GetAllocatedBytesForCurrentThread() == captureAllocated, "warm animation capture allocates zero bytes");
            var animation = new ReplayOwnerAnimation(new(4, 3, 12, AnimFlags.None, 2),
                new(5, 7, 20, AnimFlags.None, 1), new(2, 0), new(3, 1), new(-1, 0), new(1, 1), 3, 4, .625f);
            var visual = new ReplayOwnerVisual(true, 6, 8, 15, .75f, animation,
                new CosmeticSurface(12, 34, 5.25f, new Vector3(.1f, .2f, .3f), new Vector3(.4f, .5f, .6f),
                    .7f, .8f, .9f, .125f, true));
            Check(visual.Valid, "fully populated visual state is valid");
            using (var buffer = new MemoryStream())
            {
                using var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true);
                visual.Write(writer); writer.Flush();
                Check(buffer.Length == ReplayOwnerVisual.Bytes && ReplayOwnerVisual.Bytes == 121,
                    "visual codec writes exactly the schema byte count");
                buffer.Position = 0;
                using var reader = new BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true);
                Check(ReplayOwnerVisual.Read(reader) == visual && buffer.Position == buffer.Length,
                    "every visual, animation and surface field roundtrips exactly");
            }
            foreach (var invalid in new[] {
                visual with { Alpha = float.NaN }, visual with { Hunter = 7 }, visual with { Weapon = 9 },
                visual with { Recolor = 16 }, visual with { Surface = visual.Surface with { Time = float.NaN } },
                visual with { Surface = visual.Surface with { Primary = new Vector3(float.PositiveInfinity) } },
                visual with { Animation = animation with { Fraction = float.NaN } },
                visual with { Animation = animation with { Node = new(2, 2) } },
                visual with { Animation = animation with { Material = new(-2, 0) } },
                visual with { Animation = animation with { A = animation.A with { Frame = 12 } } } })
                Check(!new ReplayOwnerTrack(1).Add(Pose() with { Visual = invalid }), "invalid visual state rejected");
            foreach (float aspect in new[] { float.NaN, float.PositiveInfinity, -.1f, .19f, 8.01f })
                Check(!ReplayOwnerTrack.Valid(Pose() with { Aspect = aspect }), "invalid aspect rejected");
            track.Save(source); byte[] good = File.ReadAllBytes(sidecar);
            Check(File.ReadAllBytes(source).SequenceEqual(replay), "saving optional track preserves canonical replay");
            Check(BinaryPrimitives.ReadUInt16LittleEndian(good.AsSpan(4)) == 3
                && good.Length == 44 + 2 * ReplayOwnerTrack.RecordBytes + 32
                && ReplayOwnerTrack.RecordBytes == 269, "schema three version and exact record length");
            var visualTrack = new ReplayOwnerTrack(1);
            var visualPose = Pose() with { Aspect = 16f / 9, Visual = visual };
            Check(visualTrack.Add(visualPose), "visual picture accepted");
            visualTrack.Save(source);
            byte[] visualBytes = File.ReadAllBytes(sidecar);
            var visualReloaded = ReplayOwnerTrack.Load(source);
            Check(visualReloaded != null && visualReloaded.Sample(10, 7, 99, 2, 3, 4, out var visualLoaded)
                && visualLoaded == visualPose, "schema two preserves aspect and complete visual picture");
            byte[] version2 = new byte[44 + ReplayOwnerTrack.Version2RecordBytes + 32];
            visualBytes.AsSpan(0,44).CopyTo(version2); BinaryPrimitives.WriteUInt16LittleEndian(version2.AsSpan(4),2);
            visualBytes.AsSpan(44,ReplayOwnerTrack.Version2RecordBytes).CopyTo(version2.AsSpan(44));
            SHA256.HashData(version2.AsSpan(0,version2.Length-32)).CopyTo(version2.AsSpan(version2.Length-32));
            File.WriteAllBytes(sidecar,version2);
            using (var version2Track = ReplayOwnerTrack.Load(source))
                Check(version2Track != null && version2Track.Sample(10,7,99,2,3,4,out var v2Pose) && v2Pose==visualPose,
                    "schema two remains readable without scope blend");
            // Build a genuine schema-one file: each old record omits the new aspect/visual tail.
            byte[] legacy = new byte[44 + 2 * ReplayOwnerTrack.LegacyRecordBytes + 32];
            good.AsSpan(0, 44).CopyTo(legacy);
            BinaryPrimitives.WriteUInt16LittleEndian(legacy.AsSpan(4), 1);
            for (int i = 0; i < 2; i++) good.AsSpan(44 + i * ReplayOwnerTrack.RecordBytes,
                ReplayOwnerTrack.LegacyRecordBytes).CopyTo(legacy.AsSpan(44 + i * ReplayOwnerTrack.LegacyRecordBytes));
            SHA256.HashData(legacy.AsSpan(0, legacy.Length - 32)).CopyTo(legacy.AsSpan(legacy.Length - 32));
            File.WriteAllBytes(sidecar, legacy);
            var legacyReloaded = ReplayOwnerTrack.Load(source);
            Check(legacyReloaded != null && legacyReloaded.Sample(10, 7, 99, 2, 3, 4, out var legacyLoaded)
                && legacyLoaded == Pose() && legacyLoaded.Aspect == 0 && !legacyLoaded.Visual.Present,
                "schema one remains readable with absent visual and aspect defaults");
            File.WriteAllBytes(sidecar, good);
            Check(good.AsSpan(12, 32).SequenceEqual(SHA256.HashData(replay)), "sidecar binds SHA256 source bytes");
            var loaded = ReplayOwnerTrack.Load(source);
            Check(loaded?.Count == 2 && loaded.Sample(10.5, 7, 99, 2, 3, 4, out held) && held == Pose(), "binary save/load exact pose");
            void Reject(byte[] bytes, string why, bool resign = false)
            {
                if (resign) SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes.AsSpan(bytes.Length - 32));
                File.WriteAllBytes(sidecar, bytes); Check(ReplayOwnerTrack.Load(source) == null, why);
            }
            // Corrupt an otherwise authentic visual record and recompute its checksum;
            // semantic validation must reject these independently of file integrity.
            const int visualOffset = 44 + 139 + 4;
            foreach (int offset in new[] { visualOffset + 4, visualOffset + 64, visualOffset + 76 })
            {
                var invalidVisual = (byte[])visualBytes.Clone();
                BinaryPrimitives.WriteInt32LittleEndian(invalidVisual.AsSpan(offset), BitConverter.SingleToInt32Bits(float.NaN));
                Reject(invalidVisual, "checksum-valid nonfinite alpha/animation/surface falls back", true);
            }
            var invalidGroup = (byte[])visualBytes.Clone(); invalidGroup[visualOffset + 46] = 2;
            Reject(invalidGroup, "checksum-valid animation group slot falls back", true);
            var broken = (byte[])good.Clone(); broken[^1] ^= 1; Reject(broken, "bad checksum falls back");
            Reject(good[..^1], "truncation falls back");
            broken = (byte[])good.Clone(); broken[4] = 255; Reject(broken, "unsupported schema falls back", true);
            broken = (byte[])good.Clone(); broken[6] = 30; Reject(broken, "unsupported timebase falls back", true);
            foreach (int count in new[] { -1, 0, int.MaxValue, ReplayOwnerTrack.MaximumSamples + 1, 1 })
            { broken = (byte[])good.Clone(); BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(8), count); Reject(broken, "invalid count/length refused before sample allocation", true); }
            // Header=44, per-record clocks/identity=31, then the first matrix float.
            broken = (byte[])good.Clone(); BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(75), BitConverter.SingleToInt32Bits(float.NaN));
            Reject(broken, "checksum-valid nonfinite matrix falls back", true);
            broken = (byte[])good.Clone(); BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(75), BitConverter.SingleToInt32Bits(2));
            Reject(broken, "checksum-valid nonrigid matrix falls back", true);
            broken = (byte[])good.Clone(); BinaryPrimitives.WriteInt64LittleEndian(broken.AsSpan(44 + ReplayOwnerTrack.RecordBytes), BitConverter.DoubleToInt64Bits(10));
            Reject(broken, "checksum-valid duplicate frame falls back", true);
            broken = (byte[])good.Clone(); BinaryPrimitives.WriteInt64LittleEndian(broken.AsSpan(44 + ReplayOwnerTrack.RecordBytes + 8), BitConverter.DoubleToInt64Bits(0));
            Reject(broken, "checksum-valid reversed render clock falls back", true);
            using (var file = File.Create(sidecar)) file.SetLength(44L + 65537L * ReplayOwnerTrack.RecordBytes + 32);
            Check(ReplayOwnerTrack.Load(source) == null, "oversized file rejected before sample allocation");
            File.WriteAllBytes(sidecar, good);
            try
            {
                ReplayOwnerExport.Begin(source);
                Check(SpinWait.SpinUntil(() => ReplayOwnerExport.Ready, TimeSpan.FromSeconds(10)),
                    "exact export owner track preparation completes");
                var frozenTrack = ReplayOwnerExport.Track;
                Check(frozenTrack?.Count == 2, "exact export freezes prepared track");
                File.WriteAllBytes(sidecar, new byte[] { 1, 2, 3 });
                Check(ReferenceEquals(frozenTrack, ReplayOwnerExport.Track)
                    && frozenTrack!.Sample(10, 7, 99, 2, 3, 4, out var frozenPose) && frozenPose == Pose(),
                    "sidecar changes during export cannot change frozen owner pictures");
            }
            finally { ReplayOwnerExport.End(); }
            Check(ReplayOwnerExport.Ready && ReplayOwnerExport.Track == null, "ending export releases frozen owner track");
            File.WriteAllBytes(sidecar, good); File.WriteAllBytes(source, new byte[] { 9, 3, 5, 7, 1 });
            Check(ReplayOwnerTrack.Load(source) == null, "same-length changed source fails SHA identity");
            File.WriteAllBytes(source, replay);
            Check(ReplayOwnerTrack.Load(source, new CancellationToken(true)) == null, "cancelled load gracefully falls back");
            File.Delete(source); Check(ReplayOwnerTrack.Load(source) == null, "missing source falls back");
            foreach (int capacity in new[] { 0, ReplayOwnerTrack.MaximumSamples + 1 })
            { bool rejected = false; try { _ = new ReplayOwnerTrack(capacity); } catch (ArgumentOutOfRangeException) { rejected = true; } Check(rejected, "constructor enforces capacity budget"); }
            File.WriteAllBytes(source,replay); File.WriteAllBytes(sidecar,good);
            using (var release = new ManualResetEventSlim(false))
            using (var started = new CountdownEvent(4))
            {
                var jobs = new System.Threading.Tasks.Task<ReplayOwnerTrack?>[4];
                ReplayOwnerPresentationSource? queuedSource = null;
                try
                {
                    for (int i = 0; i < jobs.Length; i++)
                        jobs[i] = ReplayOwnerTrack.LoadAsyncBounded(source, beforeLoadForCheck: () => { started.Signal(); release.Wait(); });
                    Check(started.Wait(TimeSpan.FromSeconds(10)),"four file load workers acquired admission");
                    using var cancellation = new CancellationTokenSource();
                    var cancelled = ReplayOwnerTrack.LoadAsyncBounded(source,cancellation.Token);
                    queuedSource = new ReplayOwnerPresentationSource(source);
                    Check(!cancelled.IsCompleted && !queuedSource.Load.IsCompleted,"saturated loads await admission without losing source fidelity");
                    cancellation.Cancel();
                    Check(cancelled.Wait(TimeSpan.FromSeconds(10)) && cancelled.Result == null,"queued retired source cancels before IO admission");
                }
                finally { release.Set(); }
                Check(System.Threading.Tasks.Task.WaitAll(jobs, TimeSpan.FromSeconds(10)),"bounded loader workers release admission slots");
                using (queuedSource)
                    Check(queuedSource!.Load.GetAwaiter().GetResult()?.Count == 2,"queued shared source eventually retains verified owner pictures");
                foreach(var job in jobs)job.Result?.Dispose();
            }
            using (var drain = new ManualResetEventSlim(false))
            using (var bounded = new ReplayOwnerStream(2, () => drain.Wait()))
            {
                Check(bounded.TryWrite(Pose(0)) && bounded.TryWrite(Pose(1)) && !bounded.TryWrite(Pose(2))
                    && bounded.Coverage.Dropped == 1, "bounded producer overflow is deterministic and explicit");
                drain.Set();
                bounded.Complete(source,System.Threading.Tasks.Task.CompletedTask,()=>false).GetAwaiter().GetResult();
            }
            using (var releaseFailure = new ManualResetEventSlim(false))
            using (var failedWriter = new ReplayOwnerStream(1, () => { releaseFailure.Wait(); throw new IOException("Injected owner storage failure"); }))
            {
                Check(failedWriter.TryWrite(Pose(0)),"offline failure fixture fills bounded producer queue");
                var blocked = System.Threading.Tasks.Task.Run(() => failedWriter.WriteOffline(Pose(1),CancellationToken.None));
                releaseFailure.Set();
                Check(SpinWait.SpinUntil(()=>blocked.IsCompleted,TimeSpan.FromSeconds(10)),"storage worker failure releases offline backpressure waiter");
                bool failed = false;try { blocked.GetAwaiter().GetResult(); } catch(IOException) { failed = true; }
                Check(failed,"offline writer reports original storage failure instead of dropping or hanging");
            }
            File.WriteAllBytes(source, replay);
            using (var streaming = new ReplayOwnerStream())
            {
                // Ten minutes of 240 Hz pictures exceeds the removed lifetime cap twice.
                const int pictures = 144001;
                for (int i = 0; i < pictures; i++)
                {
                    var picture = Pose(i / 4d) with { Life = (ushort)(i < 72000 ? 4 : 5) };
                    while (!streaming.TryWrite(picture)) Thread.Yield();
                }
                streaming.Skip();
                streaming.Complete(source, System.Threading.Tasks.Task.CompletedTask, () => true).GetAwaiter().GetResult();
                using var streamed = ReplayOwnerTrack.Load(source);
                Check(streamed?.Count == pictures && streamed.Coverage.Samples == pictures && streamed.Coverage.Skipped == 1,
                    "ten minute 240 Hz streamed source has no lifetime sample cap and preserves telemetry");
                string provenance = streamed!.Provenance(true,false);
                for (int i = 0; i < 100; i++) _ = streamed.Provenance(true,false);
                long provenanceAllocated = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++) provenance = streamed.Provenance(true,false);
                Check(GC.GetAllocatedBytesForCurrentThread() == provenanceAllocated && provenance.Contains("144001 pictures"),
                    "source provenance reuses prepared labels without per-picture formatting allocations");
                Check(streamed!.Sample(36000, 7, 99, 2, 3, 5, out var finalPicture) && finalPicture.Frame == 36000,
                    "streamed tail remains seekable beyond the legacy cap");
                Check(streamed.Sample(0,7,99,2,3,4,out var beginning) && beginning == Pose(0),
                    "indexed backward seek retrieves the exact first source picture");
                Check(!streamed.Sample(18000,7,99,2,3,4,out _) && streamed.Sample(18000,7,99,2,3,5,out _),
                    "streamed chunk boundary fences lifecycle identity");
                using var shared = new ReplayOwnerPresentationSource(source);
                Check(shared.Load.GetAwaiter().GetResult()?.Count == pictures && ReferenceEquals(shared.Load,shared.Load),
                    "one playback source retains its verified loading task for replacement scenes");
                string croppedPath=Path.Combine(directory,"cropped.ppdemo"); File.WriteAllBytes(croppedPath,replay);
                ReplayOwnerTrack.CopyRange(source,croppedPath,0,36000,CancellationToken.None);
                using(var cropped=ReplayOwnerTrack.Load(croppedPath))
                {
                    Check(cropped?.Count==pictures && cropped.Coverage.Dropped==0,"offline range copy preserves more than65536 pictures with no artificial queue drops");
                    bool equal=true;
                    using var expected=streamed.Pictures().GetEnumerator(); using var actual=cropped!.Pictures().GetEnumerator();
                    while(expected.MoveNext())if(!actual.MoveNext()||expected.Current!=actual.Current){equal=false;break;}
                    Check(equal&&!actual.MoveNext(),"offline crop preserves every source picture exactly");
                }
                byte[] corrupt = File.ReadAllBytes(sidecar); corrupt[80] ^= 1; File.WriteAllBytes(sidecar,corrupt);
                Check(ReplayOwnerTrack.Load(source) == null, "streamed corrupted payload rejected");
                Check(streamed.Sample(36000,7,99,2,3,5,out _), "verified private snapshot survives source sidecar edits");
            }
            Console.WriteLine($"PASS: {_checks} owner presentation track assertions; synthetic codec/file/pose validation; asset animation rendering not exercised");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
