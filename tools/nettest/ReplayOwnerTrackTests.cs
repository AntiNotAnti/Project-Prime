using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using MphRead.Mods.Replay;
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
            track.Save(source); byte[] good = File.ReadAllBytes(sidecar);
            Check(File.ReadAllBytes(source).SequenceEqual(replay), "saving optional track preserves canonical replay");
            Check(good.AsSpan(12, 32).SequenceEqual(SHA256.HashData(replay)), "sidecar binds SHA256 source bytes");
            var loaded = ReplayOwnerTrack.Load(source);
            Check(loaded?.Count == 2 && loaded.Sample(10.5, 7, 99, 2, 3, 4, out held) && held == Pose(), "binary save/load exact pose");
            void Reject(byte[] bytes, string why, bool resign = false)
            {
                if (resign) SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes.AsSpan(bytes.Length - 32));
                File.WriteAllBytes(sidecar, bytes); Check(ReplayOwnerTrack.Load(source) == null, why);
            }
            var broken = (byte[])good.Clone(); broken[^1] ^= 1; Reject(broken, "bad checksum falls back");
            Reject(good[..^1], "truncation falls back");
            broken = (byte[])good.Clone(); broken[4] = 2; Reject(broken, "unsupported schema falls back", true);
            broken = (byte[])good.Clone(); broken[6] = 30; Reject(broken, "unsupported timebase falls back", true);
            foreach (int count in new[] { -1, 0, int.MaxValue, ReplayOwnerTrack.MaximumSamples + 1, 1 })
            { broken = (byte[])good.Clone(); BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(8), count); Reject(broken, "invalid count/length refused before sample allocation", true); }
            // Header=44, per-record clocks/identity=31, then the first matrix float.
            broken = (byte[])good.Clone(); BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(75), BitConverter.SingleToInt32Bits(float.NaN));
            Reject(broken, "checksum-valid nonfinite matrix falls back", true);
            broken = (byte[])good.Clone(); BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(75), BitConverter.SingleToInt32Bits(2));
            Reject(broken, "checksum-valid nonrigid matrix falls back", true);
            broken = (byte[])good.Clone(); BinaryPrimitives.WriteInt64LittleEndian(broken.AsSpan(44 + 139), BitConverter.DoubleToInt64Bits(10));
            Reject(broken, "checksum-valid duplicate frame falls back", true);
            broken = (byte[])good.Clone(); BinaryPrimitives.WriteInt64LittleEndian(broken.AsSpan(44 + 139 + 8), BitConverter.DoubleToInt64Bits(0));
            Reject(broken, "checksum-valid reversed render clock falls back", true);
            using (var file = File.Create(sidecar)) file.SetLength(44L + 65537L * 139 + 32);
            Check(ReplayOwnerTrack.Load(source) == null, "oversized file rejected before sample allocation");
            File.WriteAllBytes(sidecar, good); File.WriteAllBytes(source, new byte[] { 9, 3, 5, 7, 1 });
            Check(ReplayOwnerTrack.Load(source) == null, "same-length changed source fails SHA identity");
            File.WriteAllBytes(source, replay);
            Check(ReplayOwnerTrack.Load(source, new CancellationToken(true)) == null, "cancelled load gracefully falls back");
            File.Delete(source); Check(ReplayOwnerTrack.Load(source) == null, "missing source falls back");
            foreach (int capacity in new[] { 0, ReplayOwnerTrack.MaximumSamples + 1 })
            { bool rejected = false; try { _ = new ReplayOwnerTrack(capacity); } catch (ArgumentOutOfRangeException) { rejected = true; } Check(rejected, "constructor enforces capacity budget"); }
            using (var release = new ManualResetEventSlim(false))
            {
                var jobs = new System.Threading.Tasks.Task<ReplayOwnerTrack?>[4];
                try
                {
                    for (int i = 0; i < jobs.Length; i++)
                        jobs[i] = ReplayOwnerTrack.LoadAsyncBounded(source, beforeLoadForCheck: () => release.Wait());
                    var saturated = ReplayOwnerTrack.LoadAsyncBounded(source);
                    Check(saturated.IsCompletedSuccessfully && saturated.Result == null,
                        "rapid seek loader saturation falls back without queue growth");
                }
                finally { release.Set(); }
                Check(System.Threading.Tasks.Task.WaitAll(jobs, TimeSpan.FromSeconds(10)),
                    "bounded loader workers release their admission slots");
                Check(ReplayOwnerTrack.LoadAsyncBounded(source).GetAwaiter().GetResult() == null,
                    "loader accepts subsequent missing-source fallback after saturation");
            }
            Console.WriteLine($"PASS: {_checks} owner presentation track assertions; synthetic file/pose only, animation remains reconstructed");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
