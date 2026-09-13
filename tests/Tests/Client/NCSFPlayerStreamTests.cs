using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using NCSF123;
using NCSFPlayer;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class NCSFPlayerStreamTests
{
    [Fact]
    public void ReadHonorsOffsetAndOnlyReturnsCompleteStereoFrames()
    {
        using var fixture = new NCSFFixture();
        using NCSFPlayerStream stream = fixture.Open(defaultLengthInMS: 5);

        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Equal(40, stream.Length);

        byte[] buffer = new byte[32];
        Array.Fill(buffer, (byte)0xA5);

        Assert.Equal(24, stream.Read(buffer, offset: 3, count: 25));
        Assert.Equal(24, stream.Position);
        Assert.Equal(0xA5, buffer[2]);
        Assert.Equal(0xA5, buffer[27]);

        Assert.Equal(0, stream.Read(buffer, offset: 0, count: 7));
        Assert.Equal(24, stream.Position);
    }

    [Fact]
    public void FiniteStreamStopsAtEofAndSupportsFrameAlignedSeekAndPosition()
    {
        using var fixture = new NCSFFixture();
        using NCSFPlayerStream stream = fixture.Open(defaultLengthInMS: 5);
        byte[] buffer = new byte[64];

        Assert.Equal(40, stream.Read(buffer, 0, buffer.Length));
        Assert.Equal(stream.Length, stream.Position);
        Assert.Equal(0, stream.Read(buffer, 0, buffer.Length));

        Assert.Equal(16, stream.Seek(-24, SeekOrigin.End));
        Assert.Equal(16, stream.Position);
        Assert.Equal(8, stream.Read(buffer, 0, 8));
        Assert.Equal(24, stream.Position);

        stream.Position = 8;
        Assert.Equal(8, stream.Position);
        Assert.Equal(8, stream.Read(buffer, 0, 8));
        Assert.Equal(16, stream.Position);
        Assert.Equal(0, stream.Seek(7, SeekOrigin.Begin));
    }

    [Fact]
    public void InvalidArgumentsAndReadOnlyOperationsUseStreamContracts()
    {
        using var fixture = new NCSFFixture();
        using NCSFPlayerStream stream = fixture.Open(defaultLengthInMS: 5);
        byte[] buffer = new byte[8];

        Assert.Throws<ArgumentNullException>(() => stream.Read(null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(buffer, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(buffer, 0, -1));
        Assert.Throws<ArgumentException>(() => stream.Read(buffer, 3, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
        Assert.Throws<ArgumentException>(() => stream.Position = 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(-1, SeekOrigin.Begin));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(0, (SeekOrigin)99));
        Assert.Throws<NotSupportedException>(() => stream.Write(buffer, 0, buffer.Length));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(0));

        stream.Flush();
    }

    [Fact]
    public void DisposedStreamRejectsOperationsWithObjectDisposedException()
    {
        using var fixture = new NCSFFixture();
        NCSFPlayerStream stream = fixture.Open(defaultLengthInMS: 5);
        stream.Dispose();

        Assert.False(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.Throws<ObjectDisposedException>(() => stream.Read(new byte[8], 0, 8));
        Assert.Throws<ObjectDisposedException>(() => _ = stream.Length);
        Assert.Throws<ObjectDisposedException>(() => _ = stream.Position);
        Assert.Throws<ObjectDisposedException>(() => stream.Position = 0);
        Assert.Throws<ObjectDisposedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<ObjectDisposedException>(() => stream.Flush());
        Assert.Throws<ObjectDisposedException>(() => stream.Write(new byte[8], 0, 8));
        Assert.Throws<ObjectDisposedException>(() => stream.SetLength(0));
    }

    [Fact]
    public void PlayForeverIsReadableButNonSeekable()
    {
        using var fixture = new NCSFFixture();
        using NCSFPlayerStream stream = fixture.Open(defaultLengthInMS: 1, playForever: true);
        byte[] buffer = new byte[16];

        Assert.True(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.Throws<NotSupportedException>(() => _ = stream.Length);
        Assert.Throws<NotSupportedException>(() => _ = stream.Position);
        Assert.Throws<NotSupportedException>(() => stream.Position = 0);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Equal(16, stream.Read(buffer, 0, buffer.Length));
        Assert.Equal(16, stream.Read(buffer, 0, buffer.Length));
        stream.Flush();
    }

    private sealed class NCSFFixture : IDisposable
    {
        readonly string path = Path.Combine(Path.GetTempPath(), $"project-prime-ncsf-{Guid.NewGuid():N}.minincsf");

        public NCSFFixture()
        {
            WriteNcsf(this.path, CreateMinimalSdat());
        }

        public NCSFPlayerStream Open(int defaultLengthInMS, bool playForever = false) => new(
            this.path,
            sampleRate: 1000,
            interpolation: Interpolation.None,
            skipSilenceOnStartSec: 0,
            defaultLengthInMS,
            defaultFadeInMS: 0,
            volumeType: NCSF123.VolumeType.None,
            peakType: NCSF123.PeakType.None,
            playForever,
            volumeMultiplier: 1,
            channelMutes: 0,
            trackMutes: 0,
            ignoreVolume: false);

        public void Dispose()
        {
            try
            {
                File.Delete(this.path);
            }
            catch (IOException)
            {
                // The stream owns no file handle after construction; cleanup
                // failure should not hide the contract assertions.
            }
        }

        static byte[] CreateMinimalSdat()
        {
            // One terminating SSEQ and an empty SBNK are enough to exercise
            // stream lifetime/position semantics without shipping audio data.
            const int infoOffset = 0x40;
            const int infoSize = 0x80;
            const int fatOffset = infoOffset + infoSize;
            const int fatSize = 0x2C;
            const int fileOffset = fatOffset + fatSize;
            const int sseqOffset = fileOffset + 0x10;
            const int sseqSize = 0x1D;
            const int sbnkOffset = sseqOffset + sseqSize;
            const int sbnkSize = 0x3C;
            const int fileSize = 0x10 + sseqSize + sbnkSize;
            const int totalSize = fileOffset + fileSize;
            byte[] data = new byte[totalSize];

            WriteAscii(data, 0x00, "SDAT");
            WriteUInt32(data, 0x04, 0x0100FEFF);
            WriteUInt32(data, 0x08, totalSize);
            WriteUInt16(data, 0x0C, 0x40);
            WriteUInt16(data, 0x0E, 3);
            WriteUInt32(data, 0x18, infoOffset);
            WriteUInt32(data, 0x1C, infoSize);
            WriteUInt32(data, 0x20, fatOffset);
            WriteUInt32(data, 0x24, fatSize);
            WriteUInt32(data, 0x28, fileOffset);
            WriteUInt32(data, 0x2C, fileSize);

            WriteAscii(data, infoOffset, "INFO");
            WriteUInt32(data, infoOffset + 0x04, infoSize);
            WriteUInt32(data, infoOffset + 0x08, 0x40);
            WriteUInt32(data, infoOffset + 0x0C, 0x48);
            WriteUInt32(data, infoOffset + 0x10, 0x4C);
            WriteUInt32(data, infoOffset + 0x14, 0x54);
            WriteUInt32(data, infoOffset + 0x18, 0x58);
            WriteUInt32(data, infoOffset + 0x1C, 0x5C);
            WriteUInt32(data, infoOffset + 0x20, 0x60);
            WriteUInt32(data, infoOffset + 0x24, 0x64);
            WriteUInt32(data, infoOffset + 0x40, 1);
            WriteUInt32(data, infoOffset + 0x44, 0x68);
            WriteUInt32(data, infoOffset + 0x48, 0);
            WriteUInt32(data, infoOffset + 0x4C, 1);
            WriteUInt32(data, infoOffset + 0x50, 0x74);
            WriteUInt32(data, infoOffset + 0x54, 0);
            WriteUInt32(data, infoOffset + 0x58, 0);
            WriteUInt32(data, infoOffset + 0x5C, 0);
            WriteUInt32(data, infoOffset + 0x60, 0);
            WriteUInt32(data, infoOffset + 0x64, 0);
            WriteUInt32(data, infoOffset + 0x68, 0);
            WriteUInt16(data, infoOffset + 0x6C, 0);
            data[infoOffset + 0x6E] = 0x7F;
            WriteUInt32(data, infoOffset + 0x74, 1);
            for (int i = 0; i < 4; ++i)
                WriteUInt16(data, infoOffset + 0x78 + i * 2, ushort.MaxValue);

            WriteAscii(data, fatOffset, "FAT ");
            WriteUInt32(data, fatOffset + 0x04, fatSize);
            WriteUInt32(data, fatOffset + 0x08, 2);
            WriteUInt32(data, fatOffset + 0x0C, sseqOffset);
            WriteUInt32(data, fatOffset + 0x10, sseqSize);
            WriteUInt32(data, fatOffset + 0x1C, sbnkOffset);
            WriteUInt32(data, fatOffset + 0x20, sbnkSize);

            WriteAscii(data, fileOffset, "FILE");
            WriteUInt32(data, fileOffset + 0x04, fileSize);
            WriteUInt32(data, fileOffset + 0x08, 2);

            WriteAscii(data, sseqOffset, "SSEQ");
            WriteUInt32(data, sseqOffset + 0x04, 0x0100FEFF);
            WriteUInt32(data, sseqOffset + 0x08, sseqSize);
            WriteUInt16(data, sseqOffset + 0x0C, 0x10);
            WriteUInt16(data, sseqOffset + 0x0E, 1);
            WriteAscii(data, sseqOffset + 0x10, "DATA");
            WriteUInt32(data, sseqOffset + 0x14, 0x0D);
            WriteUInt32(data, sseqOffset + 0x18, 0x1C);
            data[sseqOffset + 0x1C] = 0xFF;

            WriteAscii(data, sbnkOffset, "SBNK");
            WriteUInt32(data, sbnkOffset + 0x04, 0x0100FEFF);
            WriteUInt32(data, sbnkOffset + 0x08, sbnkSize);
            WriteUInt16(data, sbnkOffset + 0x0C, 0x10);
            WriteUInt16(data, sbnkOffset + 0x0E, 1);
            WriteAscii(data, sbnkOffset + 0x10, "DATA");
            WriteUInt32(data, sbnkOffset + 0x14, 0x2C);
            WriteUInt32(data, sbnkOffset + 0x38, 0);
            return data;
        }

        static void WriteNcsf(string path, byte[] program)
        {
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                zlib.Write(program);

            byte[] compressedBytes = compressed.ToArray();
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(file);
            writer.Write("PSF"u8.ToArray());
            writer.Write((byte)0x25);
            writer.Write(4);
            writer.Write(compressedBytes.Length);
            writer.Write(0U);
            writer.Write(new byte[4]);
            writer.Write(compressedBytes);
        }

        static void WriteAscii(byte[] data, int offset, string value)
            => valueu8(value).CopyTo(data.AsSpan(offset));

        static ReadOnlySpan<byte> valueu8(string value) => value switch
        {
            "SDAT" => "SDAT"u8,
            "INFO" => "INFO"u8,
            "FAT " => "FAT "u8,
            "FILE" => "FILE"u8,
            "SSEQ" => "SSEQ"u8,
            "DATA" => "DATA"u8,
            "SBNK" => "SBNK"u8,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

        static void WriteUInt16(byte[] data, int offset, ushort value)
            => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);

        static void WriteUInt32(byte[] data, int offset, int value)
            => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), unchecked((uint)value));
    }
}
