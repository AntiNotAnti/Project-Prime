#if MPHREAD_STUDIO
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using MphRead.Mods.Network;
using MphRead.Mods.StudioReplay;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

internal static partial class ReplayVideoExporter
{
    // The same canonical player, rational sampler, capture and encoder kernels
    // as Game export, with instance ownership and no foreground playback state.
    internal static ReplayDetachedExport CreateDetached(ReplayVideoExportManifest manifest,
        string directory, string destination, ReplayCameraTrack camera, ReplayCameraMode mode, int slot,
        string encoder = "ffmpeg", string preset = "medium", bool audio = true, bool multiview = false, ReplayCameraMode comparisonMode = ReplayCameraMode.Chase, int comparisonSlot = 1, IReadOnlyList<string>? protectedInputs = null)
        => new(manifest, directory, destination, camera, mode, slot, encoder, preset, audio, multiview, comparisonMode, comparisonSlot, protectedInputs);
}

internal sealed class ReplayDetachedExport : IDisposable
{
    private PassiveReplayPlayer? _player;
    private ReplayEncoderJob? _encoder;
    private StudioReplayAudioCapture? _audioCapture;
    private readonly bool _includeAudio, _multiview;
    private readonly ReplayCameraMode _comparisonMode;
    private readonly int _comparisonSlot;
    private readonly StudioReplayViewTarget _primaryView = new(), _comparisonView = new();
    private readonly ReplayCameraTrack _emptyTrack = new();
    private readonly ReplayVideoExportManifest _manifest;
    private readonly string[] _protectedInputs;
    private readonly ReplayCameraTrack _camera;
    private readonly ReplayCameraMode _mode;
    private readonly int _slot;
    private readonly string _directory, _destination, _executable, _preset;
    private ReplayExportSampler _sampler;
    private readonly ReplayVideoSegment[] _segments;
    private readonly ReplayDirectorState _director = new();
    private StudioReplayOverlay? _overlay;
    private readonly long _totalFrames;
    private long _segmentFrames;
    private int _segmentIndex, _audioIndex, _encodedFrames;
    private int _framebuffer, _texture;
    private bool _disposed;
    private readonly bool _showFps=Mods.RenderOptions.ShowFps;
    internal long Frames { get; private set; }
    internal long TotalFrames => _totalFrames;
    internal ReplayExportState State { get; private set; } = ReplayExportState.Seeking;
    internal string? Error { get; private set; }
    internal bool Terminal => State is ReplayExportState.Completed or ReplayExportState.Cancelled or ReplayExportState.Failed;
    internal int EncodedFrames => _encoder?.Frames ?? _encodedFrames;
    internal ReplayDetachedExport(ReplayVideoExportManifest manifest, string directory, string destination,
        ReplayCameraTrack camera, ReplayCameraMode mode, int slot, string encoder, string preset, bool audio, bool multiview, ReplayCameraMode comparisonMode, int comparisonSlot, IReadOnlyList<string>? protectedInputs)
    {
        if (!ReplayExportRates.IsSupported(manifest.Fps) || manifest.Width is < 64 or > 3840 || manifest.Height is < 64 or > 2160
            || manifest.Width % 2 != 0 || manifest.Height % 2 != 0 || manifest.StartFrame >= manifest.EndFrame)
            throw new ArgumentOutOfRangeException(nameof(manifest), "Invalid video size, frame rate or selection.");
        _segments = manifest.Segments is { Count: > 0 } ? manifest.Segments.ToArray()
            : new[] { new ReplayVideoSegment(manifest.StartFrame, manifest.EndFrame, "Selection", ReplaySegmentCamera.Current) };
        if (_segments.Length > 32 || _segments.Any(s => s.StartFrame >= s.EndFrame || !Enum.IsDefined(s.Camera)))
            throw new ArgumentException("A replay export supports at most 32 valid ordered ranges.", nameof(manifest));
        if (preset is not ("ultrafast" or "medium" or "slow")) throw new ArgumentException("Unsupported encoding preset.", nameof(preset));
        _protectedInputs=(protectedInputs??Array.Empty<string>()).Append(manifest.Replay).ToArray();
        StudioReplayOutputGuard.ValidateVideo(Path.GetFullPath(destination),_protectedInputs);
        _manifest = manifest; _directory = Path.GetFullPath(directory); _destination = Path.GetFullPath(destination);
        _includeAudio = audio; _multiview = multiview; _comparisonMode = comparisonMode; _comparisonSlot = comparisonSlot; _camera = camera; _mode = mode; _slot = slot; _executable = encoder; _preset = preset;
        _sampler = new(_segments[0].StartFrame, _segments[0].EndFrame, manifest.Fps);
        _totalFrames = _segments.Sum(s => new ReplayExportSampler(s.StartFrame,s.EndFrame,manifest.Fps).Count);
        try
        {
            Directory.CreateDirectory(_directory);
            // Offline samples have a fixed clock; the host's render throughput is not video content.
            Mods.RenderOptions.ShowFps=false;
            _player = new(manifest.Replay, new Vector2i(manifest.Width, manifest.Height));
            if (_segments.Any(segment => segment.EndFrame > _player.Transport.DurationFrames)) throw new ArgumentOutOfRangeException(nameof(manifest), "Selection exceeds this replay.");
            if (_player.Current.Session.Metadata is {} metadata && ReplayMapIdentity.Validate(metadata) != ReplayOpenResult.Success)
                throw new InvalidDataException("The exact historical replay map is unavailable.");
            _player.Transport.Pause(); _player.Seek(_segments[0].StartFrame);
            _texture = GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D, _texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, manifest.Width, manifest.Height, 0, PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _framebuffer = GL.GenFramebuffer(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _texture, 0);
            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
                throw new InvalidOperationException("Replay export framebuffer is incomplete.");
        }
        catch { Dispose(); throw; }
    }
    // Must be driven only on the worker's current GL context. Preview frequency
    // changes work throughput, never sample times or the number of simulation steps.
    internal void Advance(CancellationToken cancellation)
    {
        if (Terminal) return;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (_audioCapture != null)
            {
                _audioCapture.Advance(cancellation);
                if (!_audioCapture.Complete) return;
                _audioCapture.Dispose(); _audioCapture = null;
                _audioIndex++;
                if (_audioIndex < _segments.Length) StartAudio();
                else StartEncoder();
                return;
            }
            if (_encoder != null)
            {
                if (!_encoder.Completion.IsCompleted) return;
                var result = _encoder.Completion.GetAwaiter().GetResult();
                _encodedFrames=_encoder.Frames;
                _encoder.Dispose(); _encoder = null;
                if (result.Cancelled) { State = ReplayExportState.Cancelled; return; }
                if (result.Error != null || result.ExitCode != 0) throw new IOException(result.Error ?? "Video encoder failed.");
                string output = Path.Combine(_directory, "encoded.mp4");
                if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new IOException("Encoder produced no video.");
                // Work directory is a sibling of the final file: publication is
                // one same-volume atomic rename, only after successful encoding.
                StudioReplayOutputGuard.ValidateVideo(_destination,_protectedInputs);
                File.Move(output, _destination, overwrite: true);
                State = ReplayExportState.Completed; return;
            }
            var player = _player ?? throw new InvalidOperationException("Replay export lost its private player.");
            var sample = _sampler.At(_segmentFrames);
            if (!player.Transport.IsSeeking && player.Transport.CurrentFrame != sample.SimulationFrame) player.Seek(sample.SimulationFrame);
            if (!player.Ready || player.Transport.CurrentFrame != sample.SimulationFrame)
            { player.Update(120, 8); State = ReplayExportState.Seeking; return; }
            cancellation.ThrowIfCancellationRequested();
            var scene = player.Current.Scene;
            var segment = _segments[_segmentIndex];
            var mode = SegmentMode(segment.Camera);
            int slot = _slot;
            bool directing = segment.Camera == ReplaySegmentCamera.Director || segment.Camera == ReplaySegmentCamera.Current && _manifest.Director;
            if (directing)
            {
                _director.Tick(scene,player.Current.Session);
                mode = _director.CurrentMode;
                if (_director.CurrentSlot >= 0) slot = _director.CurrentSlot;
            }
            var track = segment.Camera == ReplaySegmentCamera.Current && _manifest.CameraTrack ? _camera : _emptyTrack;
            if (_manifest.IncludeReplayOverlay)
            {
                _overlay ??= new StudioReplayOverlay(player.Current.Session);
                scene.StudioReplayOverlay = _overlay.Draw;
            }
            if (_multiview)
            {
                var size = new Vector2i(_manifest.Width / 2, _manifest.Height);
                _primaryView.Render(scene,track,sample.Frame,sample.Alpha,mode,slot,_manifest.IncludeGameHud,size);
                _primaryView.Blit(_framebuffer,0,0,size.X,size.Y);
                _comparisonView.Render(scene,_emptyTrack,sample.Frame,sample.Alpha,_comparisonMode,_comparisonSlot,_manifest.IncludeGameHud,size);
                _comparisonView.Blit(_framebuffer,size.X,0,size.X,size.Y);
                scene.Size = new(_manifest.Width,_manifest.Height);
                scene.StudioOutputFramebuffer = _framebuffer;
            }
            else
            {
                scene.StudioOutputFramebuffer = _framebuffer; scene.StudioReplayGameHud = _manifest.IncludeGameHud;
                scene.StudioReplayPrepare(track, sample.Frame, sample.Alpha, mode, slot);
                scene.OnDrawFrame(); scene.OnRenderFrame();
            }
            cancellation.ThrowIfCancellationRequested();
            if (!Mods.ScreenCapture.SaveWindow(scene, Path.Combine(_directory, $"frame_{Frames:D8}.png")))
                throw new IOException("Replay frame capture failed.");
            Frames++; _segmentFrames++; State = ReplayExportState.Rendering;
            if (_segmentFrames != _sampler.Count) return;
            _segmentIndex++; _segmentFrames = 0; _director.Reset(); _overlay = null;
            if (_segmentIndex < _segments.Length)
            {
                var next = _segments[_segmentIndex];
                _sampler = new(next.StartFrame,next.EndFrame,_manifest.Fps);
                player.Seek(next.StartFrame); return;
            }
            ReleaseRenderer();
            if (_includeAudio)
            {
                StartAudio();
            }
            else StartEncoder();
        }
        catch (OperationCanceledException) { _encoder?.Cancel(); ReleaseRenderer(); State = ReplayExportState.Cancelled; }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { _encoder?.Cancel(); ReleaseRenderer(); Error = ex.Message; State = ReplayExportState.Failed; }
    }
    private ReplayCameraMode SegmentMode(ReplaySegmentCamera camera) => camera switch
    {
        ReplaySegmentCamera.FirstPerson => ReplayCameraMode.FirstPerson,
        ReplaySegmentCamera.Chase => ReplayCameraMode.Chase,
        ReplaySegmentCamera.Orbit => ReplayCameraMode.Orbit,
        _ => _mode
    };
    private void StartAudio()
    {
        var segment = _segments[_audioIndex];
        var count = new ReplayExportSampler(segment.StartFrame,segment.EndFrame,_manifest.Fps).Count;
        var track = segment.Camera == ReplaySegmentCamera.Current && _manifest.CameraTrack ? _camera : _emptyTrack;
        bool directing = segment.Camera == ReplaySegmentCamera.Director || segment.Camera == ReplaySegmentCamera.Current && _manifest.Director;
        _audioCapture = new StudioReplayAudioCapture(_manifest.Replay,Path.Combine(_directory,$"audio_{_audioIndex}.wav"),
            segment.StartFrame,segment.EndFrame,_manifest.Fps,count,track,SegmentMode(segment.Camera),_slot,_executable,directing);
        State = ReplayExportState.MixingAudio;
    }
    private void StartEncoder()
    {
        string audio = "-an ";
        if (_includeAudio)
        {
            audio = String.Join(" ",Enumerable.Range(0,_segments.Length).Select(i=>$"-i audio_{i}.wav"))+" ";
            if (_segments.Length > 1)
                audio += "-filter_complex \""+String.Concat(Enumerable.Range(1,_segments.Length).Select(i=>$"[{i}:a]"))+$"concat=n={_segments.Length}:v=0:a=1[a]\" -map 0:v -map \"[a]\" ";
            audio += "-c:a aac -b:a 192k ";
        }
        string arguments = $"-y -framerate {_manifest.Fps} -i frame_%08d.png {audio}-c:v libx264 -preset {_preset} -crf 18 -pix_fmt yuv420p -movflags +faststart -progress pipe:1 -nostats encoded.mp4";
        _encoder = new(_executable, arguments, _directory); State = ReplayExportState.Encoding;
    }
    private void ReleaseRenderer()
    {
        try { _primaryView.Dispose(); _comparisonView.Dispose(); _player?.Dispose(); _player = null; }
        finally
        {
            if (_framebuffer != 0) GL.DeleteFramebuffer(_framebuffer);
            if (_texture != 0) GL.DeleteTexture(_texture);
            _framebuffer = _texture = 0;
        }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try
        {
            if (_encoder != null) { _encoder.Cancel(); _encoder.Completion.GetAwaiter().GetResult(); _encoder.Dispose(); _encoder = null; }
        }
        finally { try { _audioCapture?.Dispose(); _audioCapture = null; } finally { try { ReleaseRenderer(); } finally { Mods.RenderOptions.ShowFps=_showFps; } } }
    }
}

#endif
