using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MphRead.Mods.StudioReplay;

public enum StudioAudioBus { Game, Combat, Replay, Music }
public sealed record StudioAudioVolumes(float Game = 1, float Combat = 1, float Replay = 1, float Music = .5f)
{
    internal float Gain(StudioAudioBus bus) => bus switch
    { StudioAudioBus.Game => Game, StudioAudioBus.Combat => Combat, StudioAudioBus.Replay => Replay, _ => Music };
}
public sealed record StudioAudioClip(uint Frame, StudioAudioBus Bus, StudioPcmAudio Audio, float Gain = 1, bool Loop = false,
    float Pan = 0, float Pitch = 1, uint? EndFrame = null);
public sealed record StudioPcmAudio(int SampleRate, int Channels, float[] Samples)
{
    public int Frames => Samples.Length / Channels;
    /// <summary>Accepts PCM already decoded by the canonical game asset reader.</summary>
    public static StudioPcmAudio FromMonoPcm(ReadOnlySpan<byte> data, int sampleRate, bool sixteenBit)
    {
        if (sampleRate < 8000 || sampleRate > 192000 || sixteenBit && (data.Length & 1) != 0 || data.Length > 128 * 1024 * 1024)
            throw new InvalidDataException("Invalid decoded game PCM source.");
        var samples = new float[data.Length / (sixteenBit ? 2 : 1)];
        for (int i = 0; i < samples.Length; i++) samples[i] = sixteenBit
            ? System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(data.Slice(i * 2, 2)) / 32768f
            : (data[i] - 128) / 128f;
        return new(sampleRate, 1, samples);
    }
    public static StudioPcmAudio ReadWave(string path) { using var stream = File.OpenRead(path); return ReadWave(stream); }
    public static StudioPcmAudio ReadWave(Stream source)
    {
        using var reader = new BinaryReader(source, Encoding.ASCII, leaveOpen: true);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Offline audio requires a RIFF WAV file.");
        uint length = reader.ReadUInt32();
        if (length < 4 || length > 128 * 1024 * 1024 || new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Offline WAV exceeds the supported size.");
        ushort format = 0, channels = 0, bits = 0; int rate = 0; byte[]? data = null;
        long remaining = length - 4;
        while (remaining >= 8)
        {
            string id = new(reader.ReadChars(4)); uint count = reader.ReadUInt32(); remaining -= 8;
            if (count > remaining || count > 128 * 1024 * 1024) throw new InvalidDataException("WAV chunk is truncated.");
            byte[] chunk = reader.ReadBytes(checked((int)count));
            if (chunk.Length != count) throw new InvalidDataException("WAV audio is truncated.");
            if (id == "fmt ")
            {
                using var value = new BinaryReader(new MemoryStream(chunk));
                if (count < 16) throw new InvalidDataException("WAV format is truncated.");
                format = value.ReadUInt16(); channels = value.ReadUInt16(); rate = value.ReadInt32();
                value.ReadUInt32(); value.ReadUInt16(); bits = value.ReadUInt16();
            }
            else if (id == "data") data = chunk;
            remaining -= count;
            if ((count & 1) != 0) { reader.ReadByte(); remaining--; }
        }
        if (data == null || channels is not (1 or 2) || rate < 8000 || rate > 192000
            || !(format == 1 && bits == 16 || format == 3 && bits == 32))
            throw new InvalidDataException("Offline audio supports mono/stereo PCM16 or float32 WAV at 8–192 kHz.");
        int bytes = bits / 8;
        if (data.Length % (channels * bytes) != 0) throw new InvalidDataException("WAV samples are incomplete.");
        float[] samples = new float[data.Length / bytes];
        for (int i = 0; i < samples.Length; i++)
        {
            float sample = format == 1 ? System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i * bytes)) / 32768f
                : BitConverter.Int32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(i * bytes)));
            samples[i] = float.IsFinite(sample) ? sample : 0;
        }
        return new(rate, channels, samples);
    }
}

/// <summary>Bounded, deterministic PCM rendering from replay time and explicit source assets. Never opens an audio device.</summary>
public static class StudioOfflineAudio
{
    public static long SampleCount(long videoFrames, int videoFps, int sampleRate = 48000)
    {
        if (videoFrames < 1 || videoFps < 1 || sampleRate < 8000 || sampleRate > 192000) throw new ArgumentOutOfRangeException(nameof(videoFrames));
        return checked((videoFrames * sampleRate + videoFps - 1) / videoFps);
    }
    public static void WriteWave(string destination, uint selectionStart, long videoFrames, int videoFps,
        IEnumerable<StudioAudioClip> timeline, StudioAudioVolumes? volumes = null, CancellationToken cancellation = default,
        int sampleRate = 48000)
    {
        volumes ??= new();
        var clips = timeline.ToArray();
        if (clips.Length > 100000) throw new InvalidDataException("Offline audio event count exceeds the limit.");
        foreach (var clip in clips)
            if (!float.IsFinite(clip.Gain) || clip.Gain < 0 || !float.IsFinite(volumes.Gain(clip.Bus)) || volumes.Gain(clip.Bus) < 0
                || !float.IsFinite(clip.Pan) || clip.Pan < -1 || clip.Pan > 1 || !float.IsFinite(clip.Pitch) || clip.Pitch <= 0 || clip.Pitch > 16
                || clip.EndFrame.HasValue && clip.EndFrame < clip.Frame
                || clip.Audio.SampleRate < 8000 || clip.Audio.SampleRate > 192000 || clip.Audio.Channels is not (1 or 2)
                || clip.Audio.Samples.Length % clip.Audio.Channels != 0)
                throw new InvalidDataException("Offline audio contains an invalid gain or PCM source.");
        long count = SampleCount(videoFrames, videoFps, sampleRate), bytes = checked(count * 4);
        if (bytes > uint.MaxValue - 36) throw new InvalidDataException("PCM export exceeds the WAV file limit.");
        string staging = destination + "." + Guid.NewGuid().ToString("N") + ".staging";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write((uint)(bytes + 36)); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16u); writer.Write((ushort)1); writer.Write((ushort)2); writer.Write(sampleRate);
                writer.Write(sampleRate * 4); writer.Write((ushort)4); writer.Write((ushort)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write((uint)bytes);
                double absoluteStart = selectionStart * sampleRate / 60d;
                const int blockSize = 4096;
                var offsets = clips.Select(c => (Clip: c, Start: c.Frame * sampleRate / 60d,
                    Gain: (double)c.Gain * volumes.Gain(c.Bus))).ToArray();
                for (long block = 0; block < count; block += blockSize)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int blockCount = (int)Math.Min(blockSize, count - block);
                    var mixed = new double[blockCount * 2];
                    int visited = 0;
                    foreach (var entry in offsets)
                    {
                        if ((visited++ & 63) == 0) cancellation.ThrowIfCancellationRequested();
                        var audio = entry.Clip.Audio;
                        if (audio.Frames == 0 || entry.Gain == 0) continue;
                        double start = absoluteStart + block;
                        double end = entry.Clip.EndFrame.HasValue ? entry.Clip.EndFrame.Value * sampleRate / 60d : double.PositiveInfinity;
                        double duration = audio.Frames * (double)sampleRate / audio.SampleRate / entry.Clip.Pitch;
                        if (entry.Start >= start + blockCount || end <= start || !entry.Clip.Loop && entry.Start + duration <= start) continue;
                        for (int i = 0; i < blockCount; i++)
                        {
                            if (start + i >= end) break;
                            double source = (start + i - entry.Start) * audio.SampleRate / sampleRate * entry.Clip.Pitch;
                            if (source < 0 || !entry.Clip.Loop && source >= audio.Frames) continue;
                            if (entry.Clip.Loop) source %= audio.Frames;
                            int first = (int)source, next = entry.Clip.Loop ? (first + 1) % audio.Frames : Math.Min(first + 1, audio.Frames - 1);
                            double alpha = source - first;
                            for (int channel = 0; channel < 2; channel++)
                            {
                                int c = audio.Channels == 1 ? 0 : channel;
                                double sample = audio.Samples[first * audio.Channels + c] * (1 - alpha) + audio.Samples[next * audio.Channels + c] * alpha;
                                double pan = channel == 0 ? 1 - Math.Max(0, entry.Clip.Pan) : 1 + Math.Min(0, entry.Clip.Pan);
                                mixed[i * 2 + channel] += double.IsFinite(sample) ? sample * entry.Gain * pan : 0;
                            }
                        }
                    }
                    foreach (double sample in mixed) writer.Write((short)Math.Clamp(Math.Round(sample * 32767, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue));
                }
                writer.Flush(); output.Flush(flushToDisk: true);
            }
            cancellation.ThrowIfCancellationRequested(); File.Move(staging, destination, overwrite: true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}
