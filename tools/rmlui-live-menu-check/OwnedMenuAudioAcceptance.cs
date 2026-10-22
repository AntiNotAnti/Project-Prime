using System.Buffers.Binary;
using System.Security.Cryptography;
using MphRead;
using MphRead.Formats.Sound;
using MphRead.Mods.Sound;

internal static class OwnedMenuAudioAcceptance
{
    internal const string Argument = "--owned-menu-audio";

    // Explicit private filesystem path only: no device, global path mutation,
    // exports, or ROM-derived files in the test output.
    internal static void Run(string fileSystem)
    {
        if (!Path.IsPathFullyQualified(fileSystem))
            throw new ArgumentException("Owned filesystem path must be absolute.");
        var samples = SoundRead.ReadSoundSamplesFromFileSystem(fileSystem);
        var cases = new[] {
            (UiFeedbackCue.Navigate, SfxId.MENU_CURSOR),
            (UiFeedbackCue.Confirm, SfxId.MENU_CONFIRM),
            (UiFeedbackCue.Back, SfxId.MENU_CANCEL),
            (UiFeedbackCue.Tab, SfxId.TOUCH_MENU_IN),
            (UiFeedbackCue.Error, SfxId.TOUCH_NEGATIVE)
        };
        foreach (var (cue, id) in cases)
        {
            Require(UiFeedbackAudio.OwnedId(cue) == id, "cue identity");
            var sample = samples[(int)id];
            byte[] pcm = SoundRead.GetWaveData(sample);
            byte[] wav = UiFeedbackAudio.BuildOwnedWave(sample);
            ushort bits = sample.Format == WaveFormat.PCM8 ? (ushort)8 : (ushort)16;
            Require(wav.Length == pcm.Length + 44, "WAV size");
            Require(wav.AsSpan(0, 4).SequenceEqual("RIFF"u8)
                && wav.AsSpan(8, 4).SequenceEqual("WAVE"u8)
                && wav.AsSpan(12, 4).SequenceEqual("fmt "u8)
                && wav.AsSpan(36, 4).SequenceEqual("data"u8), "WAV chunks");
            Require(U32(wav, 4) == wav.Length - 8 && U32(wav, 16) == 16,
                "RIFF and fmt lengths");
            Require(U16(wav, 20) == 1 && U16(wav, 22) == 1
                && U16(wav, 34) == bits, "mono linear PCM format");
            Require(U32(wav, 24) == sample.SampleRate
                && U16(wav, 32) == bits / 8
                && U32(wav, 28) == sample.SampleRate * bits / 8,
                "sample rate and alignment");
            Require(U32(wav, 40) == pcm.Length
                && wav.AsSpan(44).SequenceEqual(pcm), "exact native decoder PCM bytes");
            double energy = 0, peak = 0;
            int count = pcm.Length / (bits / 8);
            for (int i = 0; i < count; i++)
            {
                double value = bits == 8 ? (pcm[i] - 128) / 128.0
                    : BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 2, 2)) / 32768.0;
                energy += value * value;
                peak = Math.Max(peak, Math.Abs(value));
            }
            double rms = count > 0 ? Math.Sqrt(energy / count) : 0;
            Require(count > 0 && rms > 0 && peak > 0, "non-silent decoded cue");
            Console.WriteLine($"OWNED MENU AUDIO PASS id={(int)id} rate={sample.SampleRate} frames={count} rms={rms:F6} sha256={Convert.ToHexString(SHA256.HashData(pcm))}");
        }
    }

    private static ushort U16(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint U32(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static void Require(bool pass, string detail)
    {
        if (!pass) throw new InvalidOperationException("Owned menu audio: " + detail);
    }
}
