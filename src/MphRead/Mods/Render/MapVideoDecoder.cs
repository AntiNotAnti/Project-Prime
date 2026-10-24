using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Render;

/// <summary>Bounded, full-color CPU decoder. Never runs decode or pipe reads on the GL thread.</summary>
internal sealed class MapVideoDecoder : IDisposable
{
    internal sealed record Frame(long Index, byte[] Pixels);
    internal const int BufferCount = 5;
    internal const int QueueCapacity = 3;
    private readonly BlockingCollection<byte[]> _free = new(BufferCount);
    private readonly BlockingCollection<Frame> _ready = new(QueueCapacity);
    private readonly CancellationTokenSource _stop = new();
    private readonly ManualResetEventSlim _dimensionsSeen = new();
    private readonly MapVideoTexture _video;
    private readonly Task _worker;
    private readonly Func<string> _input;
    private readonly string _executable;
    private Process? _process;
    private Task? _stderrWorker;
    private volatile string? _failure;
    private volatile string? _failureStage;
    private string _stage = "decoder-discovery";
    private volatile bool _dimensionsValid;
    private int _disposed;
    private long _workingSetBytes;
    private double _processorSeconds;
    private volatile string? _metricsFailure;
    internal string? Failure => _failure;
    internal string? FailureStage => _failureStage;
    internal bool Completed => _ready.IsCompleted;
    internal long BufferBytes => (long)_video.Width * _video.Height * 3 * BufferCount;
    internal long FramesDecoded { get; private set; }
    internal int MaximumQueued { get; private set; }
    internal Task Completion => _worker;
    internal const long InitialCodecMemoryReserve = 32L * 1024 * 1024;
    internal long WorkingSetBytes => Interlocked.Read(ref _workingSetBytes);
    internal double ProcessorSeconds => Volatile.Read(ref _processorSeconds);
    internal string? MetricsFailure => _metricsFailure;

    internal MapVideoDecoder(MapVideoTexture video, Func<string> input, string? executable = null)
    {
        if (video.Width is <8 or >8192 || video.Height is <8 or >8192
            || (long)video.Width * video.Height > 16777216 || video.FramesPerSecond is <1 or >60
            || video.FrameCount is <1 or >36000) throw new InvalidDataException("Invalid video texture bounds.");
        _video = video; _input = input;
        _executable = executable ?? MapVideoTools.Resolve("ffmpeg");
        _worker = Task.Run(Decode);
    }

    internal bool TryTake(out Frame? frame) => _ready.TryTake(out frame);
    internal void Recycle(Frame frame)
    {
        if (!_stop.IsCancellationRequested) _free.TryAdd(frame.Pixels);
    }

    private void Decode()
    {
        try
        {
            CancellationToken stop = _stop.Token;
            if ((Path.IsPathRooted(_executable) || _executable.Contains(Path.DirectorySeparatorChar)
                || _executable.Contains(Path.AltDirectorySeparatorChar)) && !File.Exists(_executable))
                throw new FileNotFoundException("Video decoder is missing: " + _executable);
            _stage = "source-extraction";
            string input = _input(); stop.ThrowIfCancellationRequested();
            _stage = "frame-buffer-allocation";
            int bytes = checked(_video.Width * _video.Height * 3);
            for (int i = 0; i < BufferCount; i++) _free.Add(new byte[bytes], stop);
            var start = new ProcessStartInfo(_executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "info",
                "-protocol_whitelist", "file,pipe" }) start.ArgumentList.Add(arg);
            // FFmpeg keeps one decoder process open across repeats. The bounded
            // output queue provides backpressure, including while the game pauses.
            if (_video.Loop)
            {
                start.ArgumentList.Add("-stream_loop");
                start.ArgumentList.Add("-1");
            }
            foreach (string arg in new[] { "-threads", "2", "-filter_threads", "1", "-f", "mov", "-i", input,
                "-an", "-sn", "-dn", "-vf", "fps=" + _video.FramesPerSecond.ToString(CultureInfo.InvariantCulture) + ",showinfo",
                "-fps_mode", "passthrough" }) start.ArgumentList.Add(arg);
            if (!_video.Loop)
            {
                start.ArgumentList.Add("-frames:v");
                start.ArgumentList.Add(_video.FrameCount.ToString(CultureInfo.InvariantCulture));
            }
            foreach (string arg in new[] {
                "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1" }) start.ArgumentList.Add(arg);
            using var process = new Process { StartInfo = start };
            _process = process;
            _stage = "decoder-start";
            if (!process.Start()) throw new IOException("Could not start video decoder.");
            using var cancel = stop.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });
            try
            {
                string lastError = "";
                Task errors = _stderrWorker = Task.Run(() =>
                {
                    string? line;
                    while ((line = process.StandardError.ReadLine()) != null)
                    {
                        if (line.Length > 0) lastError = line.Length > 1024 ? line[^1024..] : line;
                        if (line.Contains("Parsed_showinfo", StringComparison.Ordinal))
                        {
                            Match dimensions = Regex.Match(line, @"\bs:(\d+)x(\d+)\b");
                            if (dimensions.Success)
                            {
                                _dimensionsValid = Int32.Parse(dimensions.Groups[1].Value) == _video.Width
                                    && Int32.Parse(dimensions.Groups[2].Value) == _video.Height;
                                _dimensionsSeen.Set();
                            }
                        }
                    }
                    _dimensionsSeen.Set();
                });
                Stream pipe = process.StandardOutput.BaseStream;
                var metricsClock = Stopwatch.StartNew();
                _stage = "frame-decode/dimensions";
                for (long index = 0; _video.Loop || index < _video.FrameCount; index++)
                {
                    byte[] buffer = _free.Take(stop);
                    int offset = 0;
                    while (offset < bytes)
                    {
                        stop.ThrowIfCancellationRequested();
                        int read = pipe.Read(buffer, offset, bytes - offset);
                        if (read == 0) break;
                        offset += read;
                    }
                    if (offset != bytes) throw new InvalidDataException("Video ended before the declared full-color frames: " + lastError);
                    _dimensionsSeen.Wait(stop);
                    if (!_dimensionsValid) throw new InvalidDataException("Video dimensions differ from the authored dimensions; refusing to resize.");
                    _ready.Add(new Frame(index, buffer), stop);
                    FramesDecoded = index + 1;
                    MaximumQueued = Math.Max(MaximumQueued, _ready.Count);
                    if (metricsClock.ElapsedMilliseconds >= 500)
                    {
                        // Process information is cached by .NET. Refresh on this
                        // worker, never perform OS/process-file queries while drawing.
                        try
                        {
                            process.Refresh();
                            Interlocked.Exchange(ref _workingSetBytes, process.WorkingSet64);
                            Volatile.Write(ref _processorSeconds, process.TotalProcessorTime.TotalSeconds);
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                        {
                            if (_metricsFailure == null)
                                DebugLog.Line("video", "source=" + _video.Source + " stage=decoder-resource-metrics: " + ex.Message);
                            _metricsFailure = ex.Message;
                        }
                        metricsClock.Restart();
                    }
                }
                process.WaitForExit(); errors.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new IOException("Video decoder failed: " + lastError);
            }
            finally
            {
                // Cancellation can wake the queue before its asynchronous kill
                // callback runs. Worker completion must still imply child exit.
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { } // Exit raced the callback.
                catch (System.ComponentModel.Win32Exception ex)
                { DebugLog.Line("video", "source=" + _video.Source + " stage=decoder-stop: " + ex.Message); }
                process.WaitForExit();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            if (!_stop.IsCancellationRequested) { _failureStage = _stage; _failure = ex.Message; }
            try { if (_process is { HasExited: false } running) running.Kill(entireProcessTree: true); } catch { }
        }
        finally
        {
            try { _stderrWorker?.GetAwaiter().GetResult(); } catch { }
            _ready.CompleteAdding(); _process = null;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // Process-tree termination can block on OS process enumeration. Signal
        // cancellation now, but run its registered kill callback off the GL thread.
        Task cancellation = _stop.CancelAsync();
        // The GL thread never waits for a decoder. Its worker owns the process and
        // finishes after cancellation closes the pipe. Collections are then released.
        _ = Task.WhenAll(_worker, cancellation).ContinueWith(completed => {
            while (_free.TryTake(out var unusedBytes)) { }
            while (_ready.TryTake(out var unusedFrame)) { }
            _free.Dispose(); _ready.Dispose(); _dimensionsSeen.Dispose(); _stop.Dispose();
        });
    }
}
