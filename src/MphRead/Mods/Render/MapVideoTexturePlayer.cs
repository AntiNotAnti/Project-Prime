#if !MPHREAD_SERVER
using System;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Render;

/// <summary>Repeatable presentation clock independent of the game's native 30-Hz simulation.</summary>
internal sealed class MapVideoTexturePlayer : IDisposable
{
    private readonly MapVideoTexture _video;
    private readonly MapVideoDecoder _decoder;
    private MapVideoDecoder.Frame? _pending;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _previous, _elapsed;
    private bool _started, _allocated;
    private readonly Func<bool>? _linearColor;
    private bool _allocatedLinear;
    private string? _uploadFailure;
    internal int Binding { get; }
    internal int PresentedIndex { get; private set; } = -1;
    internal long PresentedSequenceIndex { get; private set; } = -1;
    internal long LoopsCompleted => PresentedSequenceIndex < 0 ? 0 : PresentedSequenceIndex / _video.FrameCount;
    internal long FramesUploaded { get; private set; }
    internal long SkippedPresentationFrames { get; private set; }
    internal MapVideoDecoder Decoder => _decoder;
    internal string? Failure => _uploadFailure ?? _decoder.Failure;
    internal string? FailureStage => _uploadFailure != null ? "gpu-upload" : _decoder.FailureStage;

    internal MapVideoTexturePlayer(int binding, MapVideoTexture video, Func<string> input, string? executable = null, Func<bool>? linearColor = null)
    {
        _linearColor = linearColor;
        Binding = binding; _video = video; _decoder = new(video, input, executable);
    }

    // Caller is the GL thread. Upload at most one selected due image per render.
    internal bool Update(bool paused)
    {
        if (Failure != null) return false;
        double now = _clock.Elapsed.TotalSeconds;
        if (_started && !paused) _elapsed += Math.Max(0, now - _previous);
        _previous = now;
        if (_started && paused) return false;
        long target = (long)(_elapsed * _video.FramesPerSecond);
        if (!_video.Loop) target = Math.Min(_video.FrameCount - 1, target);
        MapVideoDecoder.Frame? selected = null;
        while (true)
        {
            if (_pending == null && !_decoder.TryTake(out _pending)) break;
            if (_pending == null || _pending.Index > target) break;
            if (selected != null) { _decoder.Recycle(selected); SkippedPresentationFrames++; }
            selected = _pending; _pending = null;
        }
        if (selected == null) return false;
        try
        {
            int active = GraphicsApi.GetInteger(GetPName.ActiveTexture);
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            int previousBinding = GraphicsApi.GetInteger(GetPName.TextureBinding2D);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, Binding);
            int alignment = GraphicsApi.GetInteger(GetPName.UnpackAlignment);
            GraphicsApi.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            try
            {
                bool linear = _linearColor?.Invoke() == true;
                if (!_allocated || linear != _allocatedLinear)
                {
                    GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0,
                        linear ? PixelInternalFormat.Srgb8Alpha8 : PixelInternalFormat.Rgba8,
                        _video.Width, _video.Height, 0, PixelFormat.Rgb, PixelType.UnsignedByte, selected.Pixels);
                    GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
                    GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel,
                        (int)Math.Floor(Math.Log2(Math.Max(_video.Width, _video.Height))));
                    GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                    GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                    GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                    GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                    _allocated = true; _allocatedLinear = linear;
                }
                else GraphicsApi.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0,
                    _video.Width, _video.Height, PixelFormat.Rgb, PixelType.UnsignedByte, selected.Pixels);
                GraphicsApi.GenerateMipmap(GenerateMipmapTarget.Texture2D);
                ErrorCode error = GL.GetError();
                if (error != ErrorCode.NoError) throw new InvalidOperationException("OpenGL video upload error: " + error);
            }
            finally
            {
                GraphicsApi.PixelStore(PixelStoreParameter.UnpackAlignment, alignment);
                GraphicsApi.BindTexture(TextureTarget.Texture2D, previousBinding);
                GraphicsApi.ActiveTexture((TextureUnit)active);
            }
            PresentedSequenceIndex = selected.Index;
            PresentedIndex = (int)(selected.Index % _video.FrameCount);
            FramesUploaded++; _started = true;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { _uploadFailure = ex.Message; return false; }
        finally { _decoder.Recycle(selected); }
    }

    public void Dispose() { _pending = null; _decoder.Dispose(); }
}
#endif
