#if MPHREAD_STUDIO
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MphRead.Sound;
using OpenTK.Audio.OpenAL;

namespace MphRead.Mods.StudioReplay;

/// <summary>Studio does not enter Game's ModEntry, which normally selects bundled OpenAL Soft.</summary>
internal static class StudioReplayAudioRuntime
{
    internal static void Configure()
    {
        if(OperatingSystem.IsMacOS())
            OpenALLibraryNameContainer.OverridePath=Path.Combine(AppContext.BaseDirectory,"libopenal.1.dylib");
    }
}

/// <summary>OpenAL Soft's explicitly clocked stereo mixer. SfxResourceOwner owns its handles.</summary>
internal sealed class StudioLoopbackAudioBackend : ISfxNativeBackend
{
    internal const int SampleRate = 48000;
    internal const int Channels = 2;
    internal const int SamplesPerStep = SampleRate / 60;
    private const int StereoSoft = 0x1501, ShortSoft = 0x1402;
    private readonly SfxOpenAlBackend _native = SfxOpenAlBackend.Instance;
    private ALDevice _device;
    private RenderSamplesDelegate? _render;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr OpenLoopbackDelegate(IntPtr name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte SupportsFormatDelegate(IntPtr device, int frequency, int channels, int type);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RenderSamplesDelegate(IntPtr device, IntPtr buffer, int samples);

    private static T Resolve<T>(ALDevice device, string name) where T : Delegate
    {
        IntPtr address = ALC.GetProcAddress(device, name);
        if (address == IntPtr.Zero)
            throw new NotSupportedException($"OpenAL Soft loopback export requires {name}.");
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    public ALDevice OpenDevice()
    {
        if (_device.Handle != IntPtr.Zero) throw new InvalidOperationException("Loopback device is already open.");
        var open = Resolve<OpenLoopbackDelegate>(ALDevice.Null, "alcLoopbackOpenDeviceSOFT");
        var device = new ALDevice(open(IntPtr.Zero));
        if (device.Handle == IntPtr.Zero) throw new NotSupportedException("OpenAL Soft loopback device is unavailable.");
        // Publish the handle only after extension resolution. Otherwise a failed
        // OpenDevice call cannot hand ownership back to SfxResourceOwner.
        try
        {
            var supports = Resolve<SupportsFormatDelegate>(device, "alcIsRenderFormatSupportedSOFT");
            if (supports(device.Handle, SampleRate, StereoSoft, ShortSoft) == 0)
                throw new NotSupportedException("OpenAL Soft cannot export stereo 16-bit audio at 48 kHz.");
            _render = Resolve<RenderSamplesDelegate>(device, "alcRenderSamplesSOFT");
            _device = device;
            return device;
        }
        catch { _native.CloseDevice(device); throw; }
    }

    public ALContext CreateContext(ALDevice device)
        => ALC.CreateContext(device, new[] { 0x1007, SampleRate, 0x1990, StereoSoft, 0x1991, ShortSoft, 0 });
    public bool MakeCurrent(ALContext context) => _native.MakeCurrent(context);
    public void GenBuffers(Span<int> ids) => _native.GenBuffers(ids);
    public void GenSources(Span<int> ids) => _native.GenSources(ids);
    public void DeleteBuffer(int id) => _native.DeleteBuffer(id);
    public void DeleteSource(int id) => _native.DeleteSource(id);
    public void DestroyContext(ALContext context) => _native.DestroyContext(context);
    public void CloseDevice(ALDevice device)
    {
        _native.CloseDevice(device);
        if (_device.Handle == device.Handle) { _device = ALDevice.Null; _render = null; }
    }

    /// <summary>Advances by sampleFrames, writing two interleaved shorts per frame.</summary>
    internal void Render(short[] interleaved, int sampleFrames)
    {
        ArgumentNullException.ThrowIfNull(interleaved);
        if (sampleFrames < 0 || sampleFrames > interleaved.Length / Channels)
            throw new ArgumentOutOfRangeException(nameof(sampleFrames));
        if (_device.Handle == IntPtr.Zero || _render == null)
            throw new InvalidOperationException("Loopback audio device is closed.");
        if (sampleFrames == 0) return;
        var pin = GCHandle.Alloc(interleaved, GCHandleType.Pinned);
        try
        {
            _render(_device.Handle, pin.AddrOfPinnedObject(), sampleFrames);
            var deviceError = ALC.GetError(_device);
            if (deviceError != AlcError.NoError)
                throw new InvalidOperationException($"OpenAL loopback render failed: {deviceError}");
            var sourceError = AL.GetError();
            if (sourceError != ALError.NoError)
                throw new InvalidOperationException($"OpenAL audio export failed: {sourceError}");
        }
        finally { pin.Free(); }
    }
}

/// <summary>Streaming little-endian stereo PCM with a finalized RIFF size.</summary>
internal sealed class StudioPcmWaveWriter : IDisposable
{
    private const long MaximumDataBytes = uint.MaxValue - 36L;
    private readonly BinaryWriter _writer;
    private long _dataBytes;
    private bool _disposed;
    internal long SampleFrames => _dataBytes / 4;

    internal StudioPcmWaveWriter(string path, long expectedSampleFrames = 0)
    {
        if (expectedSampleFrames < 0 || expectedSampleFrames > MaximumDataBytes / 4)
            throw new ArgumentOutOfRangeException(nameof(expectedSampleFrames), "Audio exceeds the PCM RIFF size limit.");
        _writer = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None), Encoding.ASCII);
        try
        {
            _writer.Write(Encoding.ASCII.GetBytes("RIFF")); _writer.Write(36u);
            _writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); _writer.Write(16u);
            _writer.Write((ushort)1); _writer.Write((ushort)2);
            _writer.Write(StudioLoopbackAudioBackend.SampleRate);
            _writer.Write(StudioLoopbackAudioBackend.SampleRate * 4);
            _writer.Write((ushort)4); _writer.Write((ushort)16);
            _writer.Write(Encoding.ASCII.GetBytes("data")); _writer.Write(0u);
        }
        catch { _writer.Dispose(); throw; }
    }

    internal void Append(short[] interleaved, int sampleFrames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(interleaved);
        if (sampleFrames < 0 || sampleFrames > interleaved.Length / 2)
            throw new ArgumentOutOfRangeException(nameof(sampleFrames));
        long bytes = (long)sampleFrames * 4;
        if (bytes > MaximumDataBytes - _dataBytes) throw new IOException("Audio exceeds the PCM RIFF size limit.");
        for (int i = 0; i < sampleFrames * 2; i++) _writer.Write(interleaved[i]);
        _dataBytes += bytes;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _writer.BaseStream.Position = 4; _writer.Write(checked((uint)(_dataBytes + 36)));
            _writer.BaseStream.Position = 40; _writer.Write(checked((uint)_dataBytes));
            _writer.Flush();
        }
        finally { _writer.Dispose(); }
    }
}

#endif
