using System.Buffers.Binary;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using NCSFCommon.NC;
using NCSFPlayer;

namespace NCSF123;

// Partially based on my XSFPlayer and XSFPlayer_NCSF classes from my in_xsf C++ project.
public class NCSFPlayerStream : Stream
{
    const float CheckSilenceBias = 4096; // Equivalent of 0x8000000 for a 16-bit integer sample
    const float CheckSilenceLevel = 0.000213623046875f; // Equivalent of 7 for a 16-bit integer sample

    readonly NCSFFile ncsf;
    readonly uint sampleRate;
    readonly Interpolation interpolation;
    uint detectedSilenceSample = 0;
    uint detectedSilenceSec = 0;
    uint skipSilenceOnStartSec;
    readonly uint initialSkipSilenceOnStartSec;
    int lengthSample;
    int fadeSample;
    float prevSampleL = NCSFPlayerStream.CheckSilenceBias;
    float prevSampleR = NCSFPlayerStream.CheckSilenceBias;
    readonly int defaultLengthInMS;
    readonly int defaultFadeInMS;
    int lengthInMS;
    int fadeInMS;
    readonly VolumeType volumeType;
    readonly PeakType peakType;
    readonly bool playForever;
    readonly float volumeMultiplier;
    readonly ushort channelMutes;
    readonly ushort trackMutes;
    readonly bool ignoreVolume;
    SDAT sdat = null!;
    readonly List<byte> sdatData = [];
    uint sseq;
    readonly Player player = new();
    public Player Player => player;
    float secondsPerSample;
    int samplesIntoPlayback;
    long samplesGenerated;
    long position;
    bool disposed;

    public NCSFCommon.TagList Tags => this.ncsf.Tags;
    public float VolumeModification { get; set; }

    public NCSFPlayerStream(string path, uint sampleRate, Interpolation interpolation, uint skipSilenceOnStartSec, int defaultLengthInMS,
        int defaultFadeInMS, VolumeType volumeType, PeakType peakType, bool playForever, float volumeMultiplier, ushort channelMutes,
        ushort trackMutes, bool ignoreVolume)
    {
        this.ncsf = new(path);
        this.sampleRate = sampleRate;
        this.interpolation = interpolation;
        this.initialSkipSilenceOnStartSec = skipSilenceOnStartSec;
        this.defaultLengthInMS = defaultLengthInMS;
        this.defaultFadeInMS = defaultFadeInMS;
        this.volumeType = volumeType;
        this.peakType = peakType;
        this.playForever = playForever;
        this.volumeMultiplier = volumeMultiplier;
        this.channelMutes = channelMutes;
        this.trackMutes = trackMutes;
        this.ignoreVolume = ignoreVolume;
        this.Load();
    }

    public override bool CanRead => !this.disposed;

    public override bool CanSeek => !this.disposed && !this.playForever;

    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            this.ThrowIfDisposed();
            return this.playForever ? throw new NotSupportedException()
                : ((long)this.lengthSample + this.fadeSample) << 3;
        }
    }

    public override long Position
    {
        get
        {
            this.ThrowIfDisposed();
            if (this.playForever)
                throw new NotSupportedException();
            return this.position;
        }
        set
        {
            this.ThrowIfDisposed();
            if (this.playForever)
                throw new NotSupportedException();
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if ((value & 0x07) != 0)
                throw new ArgumentException("NCSF samples are positioned as 8-byte stereo frames.", nameof(value));
            this.position = value;
        }
    }

    public override void Flush() => this.ThrowIfDisposed();

    void GenerateSamples(Span<float> buf)
    {
        int offset = 0;
        int samples = buf.Length >> 1;
        for (int smpl = 0; smpl < samples; ++smpl)
        {
            ++this.samplesIntoPlayback;
            ++this.samplesGenerated;

            float leftChannel = 0;
            float rightChannel = 0;

            // I need to advance the sound channels here.
            for (int i = 0; i < Player.ChannelCount; ++i)
            {
                bool muted = (this.channelMutes & (1 << i)) != 0;
                var chn = this.player.Channels[i];

                if (chn.IsActive() && chn.Register.Enable)
                {
                    // The conditional is for handling channel muting, no sense generating a sample if the channel is muted.
                    float sample = muted ? 0 : chn.GenerateSample();
                    // However, regardless of muting, we much increment the channel, just in case the channel mute is removed later.
                    chn.IncrementSample();

                    // If the channel isn't muted, then we process the sample like normal.
                    if (!muted)
                    {
                        sample = Player.MulDiv7(sample, chn.Register.VolumeMultiplier) * chn.Register.VolumeDivisor switch
                        {
                            1 => 0.5f,
                            2 => 0.25f,
                            3 => 0.0625f,
                            _ => 1
                        };

                        leftChannel += Player.MulDiv7(sample, (byte)(127 - chn.Register.Panning));
                        rightChannel += Player.MulDiv7(sample, chn.Register.Panning);
                    }
                }
            }

            buf[offset] = leftChannel;
            buf[offset + 1] = rightChannel;
            offset += 2;

            if (this.samplesIntoPlayback * this.secondsPerSample >= Player.SecondsPerClockCycle)
            {
                this.player.SequenceMain();
                this.samplesIntoPlayback = 0;
            }
        }
    }

    void Load()
    {
        this.LoadNCSF();

        this.sdat = new();
        this.sdat.Read(this.ncsf.FilePath, this.sdatData.AsSpan(), this.sseq);
        this.player.ChannelMask = this.sdat.Player?.ChannelMask ?? 0xFFFF;
        this.player.SampleRate = this.sampleRate;
        this.player.Interpolation = this.interpolation;
        this.player.TrackMutes = this.trackMutes;
        var sseqToPlay = this.sdat.SSEQs[0];
        this.player.PrepareSequence(sseqToPlay, 0,
            NCSFCommon.NCSF.ConvertScale(sseqToPlay.Info!.Volume == 0 ? 0x7F : sseqToPlay.Info!.Volume));
        this.player.SBNK = this.sdat.SBNKs[0];
        for (int i = 0, j = 0; i < 4; ++i)
            if (this.player.SBNK.Info!.WaveArchives[i] != 0xFFFF)
                this.player.SetSWAR(i, this.sdat.SWARs[j++]);
        // One tick at the start just to skip having a bunch of silent samples before the first player tick.
        this.player.SequenceMain();
        this.secondsPerSample = 1.0f / this.sampleRate;

        this.lengthInMS = this.ncsf.GetLengthMS(this.defaultLengthInMS);
        this.fadeInMS = this.ncsf.GetFadeMS(this.defaultFadeInMS);
        this.lengthSample = (int)(this.lengthInMS * this.sampleRate / 1000);
        this.fadeSample = (int)(this.fadeInMS * this.sampleRate / 1000);

        this.VolumeModification = this.ignoreVolume ? 1 : this.ncsf.GetVolume(this.volumeType, this.peakType) * this.volumeMultiplier;

        this.skipSilenceOnStartSec = this.initialSkipSilenceOnStartSec;
        this.detectedSilenceSample = this.detectedSilenceSec = 0;
        this.samplesGenerated = 0;
        this.position = 0;
        this.prevSampleL = this.prevSampleR = NCSFPlayerStream.CheckSilenceBias;
    }

    void LoadNCSF() => this.RecursiveLoadNCSF(this.ncsf, 1);

    void MapNCSF(NCSFFile ncsfToLoad)
    {
        var reservedSection = ncsfToLoad.ReservedSection;
        var programSection = ncsfToLoad.ProgramSection;

        if (reservedSection.Length != 0)
            this.sseq = BinaryPrimitives.ReadUInt32LittleEndian(reservedSection);

        if (programSection.Length != 0)
            this.MapNCSFSection(programSection);
    }

    void MapNCSFSection(ReadOnlySpan<byte> section)
    {
        int size = BinaryPrimitives.ReadInt32LittleEndian(section[0x08..]);
        if (this.sdatData.Count < size)
        {
            bool empty = this.sdatData.Count == 0;
            CollectionsMarshal.SetCount(this.sdatData, size);
            if (empty)
                this.sdatData.AsSpan().Clear();
        }
        section.CopyTo(this.sdatData.AsSpan());
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        this.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
            throw new ArgumentException("The offset and count exceed the buffer length.", nameof(count));

        // SoundFlow consumes interleaved stereo float samples. Never cast or
        // return a partial frame: a caller may provide a short final buffer,
        // but the stream's unit is always one left/right pair (8 bytes).
        int frameBytes = count & ~0x07;
        if (frameBytes == 0)
            return 0;

        var bufFloat = buffer.AsSpan(offset, frameBytes).Cast<byte, float>();
        int bufSize = frameBytes >> 3;
        long currentSample = this.position >> 3;
        long totalSamples = (long)this.lengthSample + this.fadeSample;
        if (!this.playForever)
        {
            if (currentSample >= totalSamples || this.samplesGenerated >= totalSamples)
                return 0;
            bufSize = (int)Math.Min(bufSize, Math.Min(totalSamples - currentSample,
                totalSamples - this.samplesGenerated));
            if (bufSize == 0)
                return 0;
        }

        int pos = 0;
        while (pos < bufSize)
        {
            int remain = bufSize - pos;
            if (!this.playForever)
            {
                long sourceRemain = totalSamples - this.samplesGenerated;
                if (sourceRemain <= 0)
                    break;
                remain = (int)Math.Min(remain, sourceRemain);
            }

            this.GenerateSamples(bufFloat.Slice(pos << 1, remain << 1));
            if (this.skipSilenceOnStartSec != 0)
            {
                int skipOffset = 0;
                for (int ofs = 0; ofs < remain; ++ofs)
                {
                    float sampleL = bufFloat[2 * (pos + ofs)];
                    float sampleR = bufFloat[2 * (pos + ofs) + 1];
                    bool silence = sampleL + NCSFPlayerStream.CheckSilenceBias + NCSFPlayerStream.CheckSilenceLevel - this.prevSampleL <=
                            NCSFPlayerStream.CheckSilenceLevel * 2 &&
                        sampleR + NCSFPlayerStream.CheckSilenceBias + NCSFPlayerStream.CheckSilenceLevel - this.prevSampleR <=
                            NCSFPlayerStream.CheckSilenceLevel * 2;

                    if (silence)
                    {
                        if (++this.detectedSilenceSample >= this.sampleRate)
                        {
                            this.detectedSilenceSample -= this.sampleRate;
                            ++this.detectedSilenceSec;
                            if (this.skipSilenceOnStartSec != 0 && this.detectedSilenceSec >= this.skipSilenceOnStartSec)
                            {
                                this.skipSilenceOnStartSec = this.detectedSilenceSec = 0;
                                skipOffset = ofs;
                            }
                        }
                    }
                    else
                    {
                        this.detectedSilenceSample = this.detectedSilenceSec = 0;
                        if (this.skipSilenceOnStartSec != 0)
                        {
                            this.skipSilenceOnStartSec = 0;
                            skipOffset = ofs;
                        }
                    }

                    this.prevSampleL = sampleL + NCSFPlayerStream.CheckSilenceBias;
                    this.prevSampleR = sampleR + NCSFPlayerStream.CheckSilenceBias;
                }

                if (this.skipSilenceOnStartSec == 0)
                {
                    if (skipOffset != 0)
                    {
                        bufFloat.Slice((pos + skipOffset) << 1, (remain - skipOffset) << 1)
                            .CopyTo(bufFloat.Slice(pos << 1, (remain - skipOffset) << 1));
                        pos += remain - skipOffset;
                    }
                    else
                        pos += remain;
                }
            }
            else
                pos += remain;
        }

        if (pos == 0)
            return 0;

        // The source may end while the initial silence skip is still active.
        // Only the frames actually produced into the destination are visible
        // to the caller and advance the logical stream position.
        bufSize = pos;

        for (int ofs = 0; ofs < bufSize; ++ofs)
        {
            bufFloat[2 * ofs] = float.Clamp(bufFloat[2 * ofs] * this.VolumeModification, -1, 1);
            bufFloat[2 * ofs + 1] = float.Clamp(bufFloat[2 * ofs + 1] * this.VolumeModification, -1, 1);
        }

        // Fading
        if (!this.playForever && this.fadeSample != 0 && currentSample + bufSize >= this.lengthSample)
            for (int ofs = 0; ofs < bufSize; ++ofs)
                if (currentSample + ofs >= this.lengthSample && currentSample + ofs < this.lengthSample + this.fadeSample)
                {
                    int scale = (int)((this.lengthSample + this.fadeSample - (currentSample + ofs)) * 0x10000 / this.fadeSample);
                    bufFloat[2 * ofs] = float.ScaleB(bufFloat[2 * ofs] * scale, -16);
                    bufFloat[2 * ofs + 1] = float.ScaleB(bufFloat[2 * ofs + 1] * scale, -16);
                }
                else if (currentSample + ofs >= this.lengthSample + this.fadeSample)
                    bufFloat.Slice(2 * ofs, 2).Clear();

        this.position += (long)bufSize << 3;
        return bufSize << 3;
    }

    void RecursiveLoadNCSF(NCSFFile ncsfToLoad, int level)
    {
        if (level <= 10 && ncsfToLoad.Tags.Contains("_lib"))
            this.RecursiveLoadNCSF(new(Path.Combine(Path.GetDirectoryName(ncsfToLoad.FilePath)!, ncsfToLoad.Tags["_lib"].Value)),
                level + 1);
        this.MapNCSF(ncsfToLoad);

        int n = 2;
        bool found;
        do
        {
            found = false;
            string libTag = $"_lib{n++}";
            if (ncsfToLoad.Tags.Contains(libTag))
            {
                found = true;
                this.RecursiveLoadNCSF(new(Path.Combine(Path.GetDirectoryName(ncsfToLoad.FilePath)!, ncsfToLoad.Tags[libTag].Value)),
                    level + 1);
            }
        } while (found);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        this.ThrowIfDisposed();
        if (this.playForever)
            throw new NotSupportedException();
        else
        {
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(this.Position + offset),
                SeekOrigin.End => checked(this.Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (target < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Cannot seek before the start of an NCSF stream.");

            // Align the byte position to a complete stereo frame. This keeps
            // compatibility with the previous seek behavior while making the
            // Position contract explicit and deterministic.
            target &= ~0x07L;
            if (target < this.Position)
            {
                this.Terminate();
                this.Load();
            }
            Span<byte> dummyBuffer = stackalloc byte[0x1000];
            while (target - this.Position > dummyBuffer.Length)
            {
                if (this.Read(dummyBuffer) == 0)
                    break;
            }
            long remaining = target - this.Position;
            if (remaining is > 0 and <= 0x1000)
                _ = this.Read(dummyBuffer[..(int)remaining]);
            if (this.Position < target)
                this.Position = target;
            return this.Position;
        }
    }

    public override void SetLength(long value)
    {
        this.ThrowIfDisposed();
        throw new NotSupportedException();
    }

    void Terminate() => this.player.Stop();

    public override void Write(byte[] buffer, int offset, int count)
    {
        this.ThrowIfDisposed();
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !this.disposed)
        {
            this.disposed = true;
            this.player.Stop();
        }
        base.Dispose(disposing);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(this.disposed, this);
}
