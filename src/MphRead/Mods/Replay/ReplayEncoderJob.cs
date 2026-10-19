using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Replay;

internal sealed record ReplayEncodingResult(bool Cancelled, int? ExitCode, string? Error, string Stderr);

/// <summary>One owned encoder. Completion includes process exit and both pipe readers' retirement.</summary>
internal sealed class ReplayEncoderJob : IDisposable
{
    private readonly CancellationTokenSource _cancel = new();
    private int _frames;
    internal int Frames => Volatile.Read(ref _frames);
    internal Task<ReplayEncodingResult> Completion { get; }
    internal ReplayEncoderJob(string executable, string arguments, string directory)
        => Completion = Task.Run(() => Run(executable, arguments, directory));
    internal ReplayEncoderJob(string executable,IReadOnlyList<string> arguments,string directory)
        =>Completion=Task.Run(()=>Run(executable,"",directory,arguments));
    internal void Cancel() => _cancel.Cancel();
    private async Task<ReplayEncodingResult> Run(string executable, string arguments, string directory,IReadOnlyList<string>? argumentList=null)
    {
        var tail = new StringBuilder();
        try
        {
            _cancel.Token.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(Path.Combine(directory, "encode.txt"), executable + " " + (argumentList==null?arguments:String.Join(" ",argumentList.Select(a=>System.Text.Json.JsonSerializer.Serialize(a)))), _cancel.Token).ConfigureAwait(false);
            using var process = new Process { StartInfo = new ProcessStartInfo(executable, arguments)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
                RedirectStandardOutput = true, WorkingDirectory = directory } };
            if(argumentList!=null)foreach(string argument in argumentList)process.StartInfo.ArgumentList.Add(argument);
            if (!process.Start()) return new(false, null, "Encoder did not start.", "");
            // Cancellation only signals from the render thread. Kill/wait occur here.
            using var readCancellation = new CancellationTokenSource();
            Task stdout = ReadProgress(process.StandardOutput, readCancellation.Token);
            Task stderr = ReadError(process.StandardError, tail, readCancellation.Token);
            string? terminationError = null;
            try { await process.WaitForExitAsync(_cancel.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception ex)
                { terminationError = "Could not terminate encoder: " + ex.Message + "\n"; }
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            Task readers = Task.WhenAll(stdout, stderr);
            try { await readers.WaitAsync(_cancel.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_cancel.IsCancellationRequested)
            {
                // The owned process has exited. A descendant may still hold its
                // inherited pipes, so cancellation retires our reads rather than
                // waiting for an EOF that belongs to that separate process.
                await RetireReaders(readers, readCancellation,
                    process.StandardOutput, process.StandardError).ConfigureAwait(false);
            }
            string log = terminationError + tail.ToString();
            try { await File.WriteAllTextAsync(Path.Combine(directory, "encode.log"), log).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log += "\nLog: " + ex.Message; }
            return new(_cancel.IsCancellationRequested, process.ExitCode,
                process.ExitCode == 0 ? null : $"FFmpeg exited with code {process.ExitCode}. {log}", log);
        }
        catch (OperationCanceledException) { return new(true, null, null, tail.ToString()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { return new(_cancel.IsCancellationRequested, null, ex.Message, tail.ToString()); }
    }
    private Task ReadProgress(StreamReader reader, CancellationToken cancellation)
        => ReplayEncoderProgress.Read(reader, frame => Volatile.Write(ref _frames, frame), cancellation);
    internal static async Task RetireReaders(Task readers, CancellationTokenSource cancellation,
        StreamReader stdout, StreamReader stderr)
    {
        cancellation.Cancel();
        try
        {
            try { await readers.ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        finally
        {
            // WhenAll has retired both reads even when one reports an I/O fault.
            try { stdout.Dispose(); }
            finally { stderr.Dispose(); }
        }
    }
    private static async Task ReadError(StreamReader reader, StringBuilder tail, CancellationToken cancellation)
    {
        char[] buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) > 0)
        {
            tail.Append(buffer, 0, count);
            if (tail.Length > 16384) tail.Remove(0, tail.Length - 16384);
        }
    }
    public void Dispose()
    {
        Cancel();
        _ = Completion.ContinueWith(_ => _cancel.Dispose(), TaskScheduler.Default);
    }
}
