using System;
using Android.Content;
using Android.Media;

namespace MphRead.Droid;

/// <summary>
/// Queries Android's reported native audio format when audio first initializes.
/// This is an advisory property, not proof of a FAST/AAudio output stream.
/// </summary>
internal static class AndroidAudioProfile
{
    private static bool _read;
    private static int _deviceRate, _burstFrames;

    private static void Read()
    {
        if (_read) return;
        _read = true;
        try
        {
            Context? context = MainActivity.Instance;
            if (context?.GetSystemService(Context.AudioService) is not AudioManager manager) return;
            int.TryParse(manager.GetProperty(AudioManager.PropertyOutputSampleRate), out _deviceRate);
            int.TryParse(manager.GetProperty(AudioManager.PropertyOutputFramesPerBuffer), out _burstFrames);
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("PrimeAudio", "native audio query unavailable: " + ex.Message);
        }
    }

    internal static int SelectOutputRate(int originalRate)
    {
        Read();
        return AndroidAudioRatePolicy.Select(originalRate, _deviceRate, nativeEnabled: true);
    }

    internal static void Report(int requestedRate)
    {
        Read();
        Android.Util.Log.Info("PrimeAudio",
            $"requested mixer {requestedRate}Hz; native property {_deviceRate}Hz; "
            + $"native burst {_burstFrames} frames ({AndroidAudioRatePolicy.BufferPeriodMs(_burstFrames, _deviceRate):0.00}ms); "
            + "device properties do not prove actual callback latency");
    }
}
