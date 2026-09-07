using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Formats.Sound;
using static MphRead.Formats.Sound.SoundRead;

namespace MphRead.Export
{
    public static class SoundExport
    {
        public static void ExportSamples(bool adpcmRoundingError = false)
        {
            ExportSamples(ReadSoundSamples(), adpcmRoundingError, prefix: "mph_");
        }

        public static void ExportWfsSamples(bool adpcmRoundingError = false)
        {
            ExportSamples(ReadWfsSoundSamples(), adpcmRoundingError, prefix: "mph_wfs_");
        }

        private static void ExportSamples(IReadOnlyList<SoundSample> samples, bool adpcmRoundingError, string? prefix = null)
        {
            foreach (SoundSample sample in samples)
            {
                try
                {
                    ExportSample(sample, adpcmRoundingError, prefix);
                }
                catch (WaveExportException ex)
                {
                    Console.WriteLine($"[{sample.Id}] {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        public static void ExportSample(int id, bool adpcmRoundingError = false)
        {
            IReadOnlyList<SoundSample> samples = ReadSoundSamples();
            ExportSample(samples[id], adpcmRoundingError);
        }

        public static void ExportWfsSample(int id, bool adpcmRoundingError = false)
        {
            IReadOnlyList<SoundSample> samples = ReadWfsSoundSamples();
            ExportSample(samples[id], adpcmRoundingError);
        }

        private static void ExportSample(SoundSample sample, bool adpcmRoundingError = false, string? prefix = null)
        {
            string id = sample.Id.ToString().PadLeft(3, '0');
            byte[] waveData = GetWaveData(sample, adpcmRoundingError);
            ExportAudio(waveData, GetSampleCount(sample), sample.SampleRate, sample.Format, id, prefix);
        }

        public static void WriteWavHeader(BinaryWriter writer, uint sampleCount, ushort sampleRate, WaveFormat format)
        {
            uint bps = format == WaveFormat.PCM8 ? 8u : 16u;
            uint headerSize = 0x2C;
            uint waveSize = sampleCount * bps / 8 + headerSize;
            uint decodedSize = sampleCount * (bps / 8);
            writer.WriteC("RIFF");
            writer.Write4(waveSize - 8);
            writer.WriteC("WAVE");
            writer.WriteC("fmt ");
            writer.Write4(16);
            writer.Write2(1);
            writer.Write2(1);
            writer.Write4(sampleRate);
            writer.Write4(sampleRate * (bps / 8));
            writer.Write2(bps / 8);
            writer.Write2(bps);
            writer.WriteC("data");
            writer.Write4(decodedSize);
        }

        private static void ExportAudio(ReadOnlySpan<byte> waveData, uint sampleCount, ushort sampleRate,
            WaveFormat format, string name, string? prefix = null)
        {
            if (waveData.Length == 0)
            {
                throw new WaveExportException($"Sample {name} contains no data.");
            }
            if (format != WaveFormat.ADPCM && format != WaveFormat.PCM8 && format != WaveFormat.PCM16)
            {
                throw new WaveExportException($"Format {format} is unsupported.");
            }
            string path = Paths.Combine(Paths.Export, "_SFX");
            Directory.CreateDirectory(path);
            path = Paths.Combine(path, $"{prefix + name}.wav");
            using FileStream file = File.Create(path);
            using var writer = new BinaryWriter(file);
            WriteWavHeader(writer, sampleCount, sampleRate, format);
            for (int i = 0; i < waveData.Length; i++)
            {
                writer.Write(waveData[i]);
            }
        }

        public static void ExportStreams()
        {
            SoundData soundData = ReadSdat();
            foreach (SoundStream stream in soundData.Streams)
            {
                ExportStream(stream);
            }
        }

        public static void ExportStream(SoundStream stream)
        {
            for (int i = 0; i < stream.Channels.Count; i++)
            {
                byte[] channel = stream.Channels[i];
                string id = stream.Id.ToString().PadLeft(3, '0');
                string suffix = "";
                if (stream.Channels.Count == 2)
                {
                    suffix = i == 0 ? "_L" : "_R";
                }
                string filename = $"{id}_{stream.Name}{suffix}";
                int length = channel.Length;
                if (stream.Format == WaveFormat.ADPCM)
                {
                    length /= 2;
                }
                ExportAudio(channel, (uint)length, stream.SampleRate, stream.Format, filename);
            }
        }

        public static void ExportAllFh(bool adpcmRoundingError = false)
        {
            ExportFhBgm(adpcmRoundingError);
            ExportFhGlobalSfx(adpcmRoundingError);
            ExportFhMenuSfx(adpcmRoundingError);
            ExportFhSfx(adpcmRoundingError);
        }

        public static void ExportFhSfx(bool adpcmRoundingError = false)
        {
            ExportSamples(ReadFhSfx(), adpcmRoundingError, prefix: "fh_");
        }

        public static void ExportFhBgm(bool adpcmRoundingError = false)
        {
            ExportSamples(ReadFhBgm(), adpcmRoundingError, prefix: "fh_bgm_");
        }

        public static void ExportFhMenuSfx(bool adpcmRoundingError = false)
        {
            ExportSamples(ReadFhMenuSfx(), adpcmRoundingError, prefix: "fh_menu_");
        }

        public static void ExportFhGlobalSfx(bool adpcmRoundingError = false)
        {
            ExportSamples(ReadFhGlobalSfx(), adpcmRoundingError, prefix: "fh_lid_");
        }
    }

    public class WaveExportException : ProgramException
    {
        public WaveExportException(string message) : base(message) { }
    }
}
