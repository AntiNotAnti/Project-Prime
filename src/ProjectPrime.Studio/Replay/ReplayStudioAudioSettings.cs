using MphRead.Mods.StudioReplay;

namespace ProjectPrime.Studio.Replay;

/// <summary>Validated immutable presentation preferences for the canonical offline mixer.</summary>
public static class ReplayStudioAudioSettings
{
    public const int MaximumBindings = 64;
    public static StudioReplayAudioOptions Freeze(StudioReplayAudioOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var volumes = value.Volumes ?? new();
        foreach (float gain in new[] { volumes.Game, volumes.Combat, volumes.Replay, volumes.Music })
            if (!float.IsFinite(gain) || gain is < 0 or > 2)
                throw new ArgumentException("Audio bus volumes must be between 0 and 2.");
        if (value.Bindings is { Count: > MaximumBindings })
            throw new InvalidOperationException("A replay can have at most 64 audio cue bindings.");
        var bindings = (value.Bindings ?? []).Select(ValidateBinding).ToArray();
        // Preserve the existing music-path behavior: the canonical mixer resolves
        // typed relative paths, while authored cue entries use explicit local paths.
        string? music = string.IsNullOrWhiteSpace(value.MusicFile) ? null : Path.GetFullPath(value.MusicFile);
        // Match the canonical timeline's decoded-source cache keys exactly,
        // including differently cased references on Windows.
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings) sources.Add(binding.WaveFile);
        if (music != null) sources.Add(music);
        if (sources.Count > 64) throw new InvalidOperationException("Offline audio supports at most 64 different WAV sources, including music.");
        return value with { Volumes = volumes, MusicFile = music, Bindings = Array.AsReadOnly(bindings) };
    }
    public static StudioAudioEventBinding ValidateBinding(StudioAudioEventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (string.IsNullOrWhiteSpace(binding.EventType) || binding.EventType.Length > 64
            || binding.EventType.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new ArgumentException("Choose a recorded replay event type.");
        if (!Enum.IsDefined(binding.Bus) || !float.IsFinite(binding.Gain) || binding.Gain is < 0 or > 4)
            throw new ArgumentException("Audio cue gain must be between 0 and 4, with a valid audio bus.");
        return binding with { WaveFile = WavePath(binding.WaveFile) };
    }
    private static string WavePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32768 || !Path.IsPathFullyQualified(value)
            || !value.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a local PCM WAV file with an absolute path.");
        return Path.GetFullPath(value);
    }
}
