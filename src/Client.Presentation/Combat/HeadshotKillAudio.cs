using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Sound;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Metadata.Models;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace MphRead.Combat;

public enum HeadshotKillSoundStyle : byte
{
    Warzone,
    Boom,
    Announcer,
    Juicy,
    Cartoon
}

public static class HeadshotKillSoundCatalog
{
    private const string ResourcePrefix = "MphRead.Combat.HeadshotKills.";

    public static IReadOnlyList<string> StyleNames { get; } =
        ["Warzone", "Boom", "Announcer", "Juicy", "Cartoon"];

    public static HeadshotKillSoundStyle Parse(string? value)
        => Enum.TryParse(value, ignoreCase: true, out HeadshotKillSoundStyle style)
            && Enum.IsDefined(style) ? style : HeadshotKillSoundStyle.Warzone;

    public static string Format(HeadshotKillSoundStyle style)
        => Enum.IsDefined(style) ? style.ToString() : HeadshotKillSoundStyle.Warzone.ToString();

    internal static Stream? Open(HeadshotKillSoundStyle style)
    {
        style = Enum.IsDefined(style) ? style : HeadshotKillSoundStyle.Warzone;
        return typeof(HeadshotKillSoundCatalog).Assembly.GetManifestResourceStream(
            ResourcePrefix + style + ".mp3");
    }
}

/// <summary>Owns the single replaceable decoded headshot-kill clip for one scene.</summary>
internal sealed class HeadshotKillAudioPlayer : IDisposable
{
    private readonly object _stateGate = new();
    private StreamDataProvider? _provider;
    private Stream? _stream;
    private SoundPlayer? _player;
    private AudioPlaybackDevice? _device;
    private bool _disposed;

    internal bool TryPlay(HeadshotKillSoundStyle style, float volume)
    {
        Stream? unowned = HeadshotKillSoundCatalog.Open(style);
        if (unowned == null) return false;
        bool played = false;
        try
        {
            AudioService.Process.TryExecute(() =>
            {
                lock (_stateGate)
                {
                    StopCore();
                    if (_disposed || volume <= 0) return;
                    MiniAudioEngine? engine = MusicPlayer.Engine;
                    AudioPlaybackDevice? device = MusicPlayer.PlaybackDevice;
                    if (engine == null || device == null) return;
                    try
                    {
                        _device = device;
                        _stream = unowned;
                        unowned = null;
                        _provider = new StreamDataProvider(engine, _stream, new ReadOptions
                        {
                            ReadTags = false,
                            ReadAlbumArt = false,
                            ReadCueSheet = false,
                            DurationAccuracy = DurationAccuracy.FastEstimate
                        });
                        _player = new SoundPlayer(engine, MusicPlayer.Format, _provider)
                        {
                            Volume = volume
                        };
                        device.MasterMixer.AddComponent(_player);
                        device.Start();
                        _player.Play();
                        played = true;
                    }
                    catch (Exception)
                    {
                        StopCore();
                    }
                }
            });
        }
        catch (Exception)
        {
            played = false;
        }
        finally
        {
            try { unowned?.Dispose(); }
            catch (Exception) { }
        }
        return played;
    }

    private void Stop()
    {
        if (AudioService.Process.TryExecute(() =>
        {
            lock (_stateGate) StopCore();
        })) return;
        lock (_stateGate) StopCore();
    }

    private void StopCore()
    {
        SoundPlayer? player = _player;
        AudioPlaybackDevice? device = _device;
        _player = null;
        _device = null;
        if (player != null)
        {
            try
            {
                player.Stop();
                device?.MasterMixer.RemoveComponent(player);
            }
            catch (Exception) { }
            try { player.Dispose(); }
            catch (Exception) { }
        }
        try { _provider?.Dispose(); }
        catch (Exception) { }
        _provider = null;
        try { _stream?.Dispose(); }
        catch (Exception) { }
        _stream = null;
    }

    public void Dispose()
    {
        lock (_stateGate) _disposed = true;
        Stop();
    }
}
