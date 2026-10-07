#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MphRead.Formats;
using MphRead.Formats.Sound;
using MphRead.Mods.Network;
using MphRead.Sound;

namespace MphRead.Mods.StudioReplay;

/// <summary>Recorded event cues use the selected game's canonical sound tables and decoder, without an OpenAL device.</summary>
internal sealed class StudioOfflineGameSounds : IDisposable
{
    private readonly IReadOnlyList<SoundSample> _samples = SoundRead.ReadSoundSamples();
    private readonly SoundTable _table = SoundRead.ReadSoundTables();
    private readonly IReadOnlyList<DgnFile> _dynamic = SoundRead.ReadDgnFiles();
    private readonly IReadOnlyList<SfxScriptFile> _scripts = SoundRead.ReadSfxScriptFiles();
    private readonly Dictionary<int, StudioPcmAudio> _decoded = new();
    private readonly Dictionary<byte, byte> _hunters;
    private readonly CancellationToken _cancellation;
    private long _bytes;
    internal StudioOfflineGameSounds(IReadOnlyList<ReplayPlayerInfo> players, CancellationToken cancellation)
    {
        _hunters = players.GroupBy(p => p.Slot).ToDictionary(p => p.Key, p => p.Last().Hunter);
        _cancellation = cancellation;
        if (_table.Entries.Count != _samples.Count) throw new InvalidDataException("Game sound samples and table do not match.");
    }
    internal void Append(ReplayEvent marker, List<StudioAudioClip> output)
    {
        int sound = -1;
        if (marker.Type == ReplayEventType.WeaponFired && Metadata.BeamSfx is { } beams
            && marker.Value >= 0 && marker.Value < beams.GetLength(0))
            sound = beams[marker.Value, (int)BeamSfx.Shot];
        else
        {
            byte slot = marker.Type == ReplayEventType.Damage ? marker.TargetSlot : marker.ActorSlot;
            if (_hunters.TryGetValue(slot, out byte hunter) && Metadata.HunterSfx is { } hunters && hunter < hunters.GetLength(0))
            {
                int column = marker.Type switch { ReplayEventType.PlayerSpawn => (int)HunterSfx.Spawn,
                    ReplayEventType.PlayerDeath => (int)HunterSfx.DeathEnemy, ReplayEventType.Damage => (int)HunterSfx.DamageEnemy, _ => -1 };
                if (column >= 0) sound = hunters[hunter, column];
            }
        }
        if (sound >= 0) AppendSound(sound, marker.Frame, output);
    }
    private void AppendSound(int sound, uint frame, List<StudioAudioClip> output)
    {
        _cancellation.ThrowIfCancellationRequested();
        if ((sound & 0x8000) != 0)
        {
            int id = sound & 0x3fff;
            if (id >= _dynamic.Count) throw new InvalidDataException("Invalid recorded game dynamic sound.");
            var file = _dynamic[id];
            foreach (var entry in file.Entries)
            {
                // Replay event markers do not record continuous-beam amplitude parameters.
                // The deterministic event cue uses the canonical dynamic sound's zero-input onset.
                static float Onset(IReadOnlyList<DgnData> data) => data.Count == 0 ? 0 : data[0].Value & 0x3fff;
                float gain = Onset(entry.Data1) / 127f * Onset(entry.Data2) / 127f * file.Header.InitialVolume / 127f;
                float pitch = Sfx.CalculatePitchDiv(Math.Min(0x3fff, Onset(entry.Data3) / 0x2000 * Onset(entry.Data4)));
                Add((int)entry.SfxId, frame, output, gain, pitch: pitch);
            }
        }
        else if ((sound & 0x4000) != 0)
        {
            int id = sound & 0x3fff;
            if (id >= _scripts.Count) throw new InvalidDataException("Invalid recorded game sound script.");
            var playing = new Dictionary<int, List<int>>();
            foreach (var entry in _scripts[id].Entries)
            {
                uint when = checked(frame + (uint)Math.Round(entry.Delay * 60, MidpointRounding.AwayFromZero));
                int sample = entry.SfxData & 0x3fff;
                if ((entry.SfxData & 0x8000) != 0)
                {
                    if (playing.TryGetValue(sample, out var indices))
                        foreach (int index in indices) output[index] = output[index] with { EndFrame = when };
                    playing.Remove(sample); continue;
                }
                int next = output.Count;
                Add(sample, when, output, entry.Volume, entry.Pan > -1 ? Math.Clamp(entry.Pan, -1, 1) : 0, entry.Pitch);
                if (output.Count == next) continue;
                if (!playing.TryGetValue(sample, out var list)) playing[sample] = list = new();
                list.Add(next);
            }
        }
        else Add(sound, frame, output);
    }
    private void Add(int sampleId, uint frame, List<StudioAudioClip> output, float gain = 1, float pan = 0, float pitch = 1)
    {
        if (gain <= 0 || pitch <= 0) return;
        if (sampleId < 0 || sampleId >= _samples.Count) throw new InvalidDataException("Invalid recorded game sound sample.");
        if (!_decoded.TryGetValue(sampleId, out var pcm))
        {
            _cancellation.ThrowIfCancellationRequested();
            var sample = _samples[sampleId];
            if (sample.Format == WaveFormat.None) return;
            pcm = StudioPcmAudio.FromMonoPcm(sample.WaveData.Value, sample.SampleRate, sample.Format != WaveFormat.PCM8);
            _bytes += pcm.Samples.LongLength * sizeof(float);
            if (_decoded.Count >= 256 || _bytes > 256L * 1024 * 1024) throw new InvalidDataException("Game event audio exceeds the bounded decoded source budget.");
            _decoded.Add(sampleId, pcm);
        }
        output.Add(new(frame, StudioAudioBus.Game, pcm, gain * _table.Entries[sampleId].InitialVolume / 127f,
            Pan: pan, Pitch: Math.Clamp(pitch, .001f, 16)));
    }
    public void Dispose() => _decoded.Clear();
}
#endif
