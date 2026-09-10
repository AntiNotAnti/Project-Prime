using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests.Client;
public sealed class ReplayArchiveBoundsTests
{
    [Fact]
    public void EveryTornTailAndCorruptHeaderOrPayloadKeepsOnlyTheVerifiedPrefix()
    {
        string path = Temporary();
        try
        {
            long prefix;
            using (var writer = new ReplayWriter(path, indexed: true))
            {
                writer.WriteRecord(0, new byte[] { 1, 2, 3 });
                prefix = new FileInfo(path).Length;
                writer.WriteRecord(10, new byte[] { 4, 5, 6 }, ReplayMarker.Kill);
            }
            byte[] full = File.ReadAllBytes(path);
            for (int end = (int)prefix; end < full.Length; end++)
            {
                File.WriteAllBytes(path, full.AsSpan(0, end).ToArray());
                using ReplayReader reader = ReplayReader.Open(path)!;
                Assert.Equal(new byte[] { 1, 2, 3 }, reader.ReadNext()!.Value.Data);
                Assert.Null(reader.ReadNext()); Assert.Empty(reader.Index);
                Assert.Equal(end != prefix, reader.RecoveredTail);
            }
            for (int offset = (int)prefix; offset < full.Length; offset++)
            {
                byte[] corrupt = (byte[])full.Clone(); corrupt[offset] ^= 1; File.WriteAllBytes(path, corrupt);
                using ReplayReader reader = ReplayReader.Open(path)!;
                Assert.Equal(new byte[] { 1, 2, 3 }, reader.ReadNext()!.Value.Data);
                Assert.Null(reader.ReadNext()); Assert.True(reader.RecoveredTail); Assert.Empty(reader.Index);
            }
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void CheckpointsAreSkippedSequentiallyAndNearestIndexRestoresWholeBoundedBatch()
    {
        string path = Temporary();
        try
        {
            using (var writer = new ReplayWriter(path, indexed: true))
            {
                writer.WriteRecord(0, new byte[] { 1 });
                writer.WriteKeyframe(0, new[] { new byte[] { 2, 3 }, new byte[] { 4 } });
                writer.WriteRecord(5, new byte[] { 5 }, ReplayMarker.NodeCapture);
                writer.WriteKeyframe(300, new[] { new byte[] { 6 } });
                writer.WriteRecord(301, new byte[] { 7 });
            }
            using ReplayReader reader = ReplayReader.Open(path)!;
            Assert.Equal((byte)3, reader.FormatVersion);
            Assert.Equal(3, reader.Index.Count);
            Assert.Equal(301u, reader.LastFrame);
            Assert.Equal((byte)1, reader.ReadNext()!.Value.Data[0]);
            Assert.Equal((byte)5, reader.ReadNext()!.Value.Data[0]);
            Assert.Equal((byte)7, reader.ReadNext()!.Value.Data[0]);
            Assert.Null(reader.ReadNext());
            var checkpoint = reader.Seek(299, out uint frame)!;
            Assert.Equal(0u, frame); Assert.Equal(2, checkpoint.Length);
            Assert.Equal(new byte[] { 2, 3 }, checkpoint[0].Data);
            Assert.Equal(5u, reader.ReadNext()!.Value.Frame);
            checkpoint = reader.Seek(300, out frame)!;
            Assert.Equal(300u, frame); Assert.Single(checkpoint);
            Assert.Equal((byte)6, checkpoint[0].Data[0]);
            Assert.Equal(301u, reader.ReadNext()!.Value.Frame);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void LengthFrameAndFileLimitsRejectWithoutLargeAllocations()
    {
        string path = Temporary();
        try
        {
            using (var writer = new ReplayWriter(path, indexed: true))
            {
                Assert.Throws<InvalidDataException>(() => writer.WriteRecord(ReplayArchive.MaximumFrame + 1, new byte[] { 1 }));
                Assert.Throws<InvalidDataException>(() => writer.WriteKeyframe(0, Array.Empty<byte[]>()));
                Assert.Throws<InvalidDataException>(() => writer.WriteKeyframe(0, new[] { new byte[NetConfig.MaxPacketSize + 1] }));
                writer.WriteRecord(10, new byte[] { 1 });
                Assert.Throws<InvalidDataException>(() => writer.WriteRecord(9, new byte[] { 1 }));
            }
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) file.SetLength(ReplayArchive.MaximumFileBytes + 1);
            Assert.Null(ReplayReader.Open(path));
            Assert.Null(ReplayArchive.Unpack(0, new byte[] { 255, 255, 255, 127 }));
        }
        finally { File.Delete(path); }
    }
    private static string Temporary() => Path.Combine(Path.GetTempPath(), $"replay-bounds-{Guid.NewGuid():N}.fpreplay");
}
