using System.Text.Json;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio.Replay;

internal static class AudioAuthoringChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        string wave = Path.Combine(root, "authoring-cue.wav");
        var authored = new List<StudioAudioEventBinding> { new("WeaponFired", wave, StudioAudioBus.Replay, 7, .25f) };
        var snapshot = ReplayStudioAudioSettings.Freeze(new(Volumes: new(Replay: .5f), Bindings: authored, GameEvents: false, CombatFeedback: false));
        authored[0] = authored[0] with { Gain = 4 }; authored.Clear();
        check(snapshot.Bindings is { Count: 1 } && snapshot.Bindings[0].Gain == .25f && snapshot.Bindings[0].Value == 7,
            "queued audio preference snapshot retains its own cue list, gain and optional event value after authoring edits");
        bool immutable = false;
        try { ((IList<StudioAudioEventBinding>)snapshot.Bindings!)[0] = new("Kill", wave); }
        catch (NotSupportedException) { immutable = true; }
        check(immutable, "public cue snapshot cannot be mutated through a collection cast");
        var restored = ReplayStudioAudioSettings.Freeze(JsonSerializer.Deserialize<StudioReplayAudioOptions>(JsonSerializer.Serialize(snapshot))!);
        check(restored.Bindings!.SequenceEqual(snapshot.Bindings!) && restored.Volumes == snapshot.Volumes
            && !restored.CombatFeedback && !restored.GameEvents, "audio presentation JSON retains exact cue bindings, buses, values, gains and switches");
        check(!ReferenceEquals(snapshot.Bindings, ReplayStudioAudioSettings.Freeze(snapshot).Bindings), "each export obtains a fresh immutable cue collection");
        check(ReplayStudioAudioSettings.Freeze(new(MusicFile: "existing-relative-music.wav")).MusicFile == Path.GetFullPath("existing-relative-music.wav"),
            "typed relative music retains the canonical mixer's existing path resolution behavior");
        var sixtyFour = Enumerable.Range(0, 64).Select(index => new StudioAudioEventBinding("Kill", Path.Combine(root, $"cue-{index}.wav"), StudioAudioBus.Replay)).ToArray();
        check(ReplayStudioAudioSettings.Freeze(new(Bindings: sixtyFour)).Bindings!.Count == 64, "exactly 64 authored bindings and user WAV sources remain supported");
        Reject(new(Bindings: sixtyFour.Append(sixtyFour[0]).ToArray()), "65 cue bindings are rejected before mixer admission");
        Reject(new(Bindings: sixtyFour, MusicFile: wave), "music participates in the 64 distinct user WAV source budget");
        Reject(new(Bindings: sixtyFour, MusicFile: sixtyFour[0].WaveFile.Replace("cue-0.wav", "CUE-0.WAV", StringComparison.Ordinal)),
            "differently cased music follows the canonical decoder's ordinal source cache and cannot bypass the 64 source budget");
        check(ReplayStudioAudioSettings.Freeze(new(Bindings: sixtyFour, MusicFile: sixtyFour[0].WaveFile)).Bindings!.Count == 64,
            "a music WAV shared with a cue does not consume another source slot");
        foreach (float gain in new[] { float.NaN, float.PositiveInfinity, -.01f, 4.01f })
            Reject(new(Bindings: [new("Kill", wave, Gain: gain)]), "nonfinite, negative or excessive cue gain is rejected: " + gain);
        foreach (float gain in new[] { float.NaN, float.PositiveInfinity, -.01f, 2.01f })
            Reject(new(Volumes: new(Replay: gain)), "nonfinite, negative or excessive replay bus volume is rejected: " + gain);
        Reject(new(Bindings: [new("Kill", wave, (StudioAudioBus)100)]), "unknown bus is rejected");
        Reject(new(Bindings: [new("Kill", "relative.wav")]), "relative cue WAV references are rejected");
        Reject(new(Bindings: [new("Kill", Path.Combine(root, "not-wave.mp3"))]), "compressed cue audio must be converted to canonical supported WAV before authoring");
        Reject(new(Bindings: [new("", wave)]), "missing event type is rejected");
        Reject(new(Bindings: [null!]), "null cue records are rejected");
        string output = Path.Combine(root, "authored-replay-cue.wav");
        var pcm = new StudioPcmAudio(48000, 1, Enumerable.Repeat(.5f, 4800).ToArray());
        var binding = snapshot.Bindings![0];
        StudioOfflineAudio.WriteWave(output, 0, 6, 60, [new(0, binding.Bus, pcm, binding.Gain)], snapshot.Volumes);
        var audible = StudioPcmAudio.ReadWave(output);
        check(audible.Frames == 4800 && audible.Samples.All(sample => Math.Abs(sample - .0625f) < .0001),
            "canonical PCM mixer applies authored cue gain times replay bus gain at every sample");
        StudioOfflineAudio.WriteWave(output, 0, 6, 60, [new(0, binding.Bus, pcm, binding.Gain)], snapshot.Volumes! with { Replay = 0 });
        check(StudioPcmAudio.ReadWave(output).Samples.All(sample => sample == 0), "canonical PCM mixer produces exact silence when authored replay bus volume is zero");
        void Reject(StudioReplayAudioOptions value, string message)
        {
            bool rejected = false;
            try { ReplayStudioAudioSettings.Freeze(value); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException) { rejected = true; }
            check(rejected, message);
        }
    }
}
