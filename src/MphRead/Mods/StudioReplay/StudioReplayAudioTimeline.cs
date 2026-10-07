#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MphRead.Mods.Network;

namespace MphRead.Mods.StudioReplay;

internal static class StudioReplayAudioTimeline
{
    internal static void Write(string destination, StudioReplayExportRequest request, long videoFrames,
        IReadOnlyList<ReplayEvent> events, IReadOnlyList<ReplayPlayerInfo> players, CancellationToken cancellation)
    {
        if (request.Audio is not { Enabled: true } options) return;
        var clips = new List<StudioAudioClip>();
        var sources = new Dictionary<string, StudioPcmAudio>(StringComparer.Ordinal);
        long decodedBytes = 0;
        bool Overlaps(StudioAudioClip clip)
        {
            double end = clip.Loop ? double.PositiveInfinity : clip.Frame + clip.Audio.Frames * 60d / clip.Audio.SampleRate / clip.Pitch;
            if (clip.EndFrame.HasValue) end = Math.Min(end, clip.EndFrame.Value);
            return clip.Frame <= request.EndFrame && end > request.StartFrame;
        }
        void Append(StudioAudioClip clip)
        {
            if (!Overlaps(clip)) return;
            if (clips.Count >= 100000) throw new InvalidDataException("Offline audio selection exceeds the bounded event budget.");
            clips.Add(clip);
        }
        StudioPcmAudio Source(string file)
        {
            string path = Path.GetFullPath(file);
            if (!sources.TryGetValue(path, out var pcm))
            {
                cancellation.ThrowIfCancellationRequested();
                pcm = StudioPcmAudio.ReadWave(path);
                decodedBytes += pcm.Samples.LongLength * sizeof(float);
                if (sources.Count >= 64 || decodedBytes > 256L * 1024 * 1024) throw new InvalidDataException("Offline audio assets exceed the bounded source budget.");
                sources.Add(path, pcm);
            }
            return pcm;
        }
        StudioPcmAudio? headshot = null;
        using var game = options.GameEvents ? new StudioOfflineGameSounds(players, cancellation) : null;
        if (options.CombatFeedback)
        {
            var assembly = typeof(StudioReplayAudioTimeline).Assembly;
            string? name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("headshot-prime.wav", StringComparison.OrdinalIgnoreCase));
            if (name != null) { using var resource = assembly.GetManifestResourceStream(name)!; headshot = StudioPcmAudio.ReadWave(resource); }
        }
        foreach (ReplayEvent replayEvent in events)
        {
            cancellation.ThrowIfCancellationRequested();
            // Include earlier events: a source's tail can cross the selection in point.
            if (replayEvent.Frame > request.EndFrame) continue;
            int first = clips.Count;
            game?.Append(replayEvent, clips);
            for (int i = clips.Count - 1; i >= first; i--) if (!Overlaps(clips[i])) clips.RemoveAt(i);
            if (clips.Count > 100000) throw new InvalidDataException("Offline audio selection exceeds the bounded event budget.");
            if (replayEvent.Type == ReplayEventType.Headshot && headshot != null)
                Append(new(replayEvent.Frame, StudioAudioBus.Combat, headshot));
            foreach (var binding in options.Bindings ?? Array.Empty<StudioAudioEventBinding>())
                if (binding.EventType.Equals(replayEvent.Type.ToString(), StringComparison.Ordinal)
                    && (!binding.Value.HasValue || binding.Value == replayEvent.Value))
                    Append(new(replayEvent.Frame, binding.Bus, Source(binding.WaveFile), binding.Gain));
        }
        if (!string.IsNullOrWhiteSpace(options.MusicFile)) Append(new(request.StartFrame, StudioAudioBus.Music, Source(options.MusicFile), Loop: true));
        StudioOfflineAudio.WriteWave(destination, request.StartFrame, videoFrames, request.Fps, clips,
            options.Volumes, cancellation);
    }
}

#endif
