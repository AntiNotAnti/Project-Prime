using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Replay;

internal static class ReplayEncoderCapabilities
{
    private static readonly string[] Allowed = { "libx264", "libx265", "h264_nvenc", "hevc_nvenc",
        "h264_qsv", "hevc_qsv", "h264_videotoolbox", "hevc_videotoolbox", "h264_amf", "hevc_amf" };
    private static readonly Lazy<Task<IReadOnlyList<string>>> Cached = new(() => Task.Run(Probe));
    internal static Task<IReadOnlyList<string>> Available => Cached.Value;
    private static async Task<IReadOnlyList<string>> Probe()
    {
        try
        {
            using var process = new Process { StartInfo = new("ffmpeg", "-hide_banner -encoders")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            if (!process.Start()) return new[] { "libx264" };
            var stdoutReader = process.StandardOutput;
            var stderrReader = process.StandardError;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var supported = new bool[Allowed.Length];
            Task stdout = ReadCapabilities(stdoutReader, index => supported[index] = true, readCancellation.Token);
            Task stderr = ReplayEncoderErrorDrain.Drain(stderrReader, readCancellation.Token);
            Task readers = Task.WhenAll(stdout, stderr);
            try
            {
                try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync().ConfigureAwait(false); }
                try { await readers.WaitAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
            }
            finally
            {
                // A separate descendant can retain inherited pipes after our
                // process exits. Retire both reads before closing owned readers.
                await RetireReaders(readers, readCancellation, stdoutReader, stderrReader).ConfigureAwait(false);
            }
            return Allowed.Where((name, index) => supported[index] || name == "libx264").ToArray();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException or NotSupportedException)
        { return new[] { "libx264" }; }
    }
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
            try { stdout.Dispose(); }
            finally { stderr.Dispose(); }
        }
    }
    internal static async Task ReadCapabilities(StreamReader reader, Action<int> publish,
        CancellationToken cancellation = default)
    {
        // Match the original Split('\n') + Split(' ', RemoveEmptyEntries): only
        // literal spaces delimit tokens; CR, tabs and other whitespace do not.
        char[] buffer = new char[2048];
        int column = 0, position = 0;
        bool inToken = false;
        uint matches = 0;
        void FinishToken()
        {
            if (inToken && column == 2)
                for (int i = 0; i < Allowed.Length; i++)
                    if ((matches & (1U << i)) != 0 && position == Allowed[i].Length) publish(i);
        }
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int j = 0; j < count; j++)
            {
                char value = buffer[j];
                if (value is ' ' or '\n')
                {
                    FinishToken(); inToken = false;
                    if (value == '\n') column = 0;
                }
                else
                {
                    if (!inToken)
                    {
                        inToken = true; column = Math.Min(column + 1, 3);
                        position = 0; matches = (1U << Allowed.Length) - 1;
                    }
                    if (column == 2 && matches != 0)
                    {
                        for (int i = 0; i < Allowed.Length; i++)
                            if (position >= Allowed[i].Length || value != Allowed[i][position]) matches &= ~(1U << i);
                        position++;
                    }
                }
            }
        }
        cancellation.ThrowIfCancellationRequested();
        FinishToken();
    }
    internal static ReplayVideoExportManifest Apply(ReplayVideoExportManifest job, string encoder)
    {
        if (!Allowed.Contains(encoder)) throw new ArgumentException("Unsupported encoder.", nameof(encoder));
        string options = encoder is "libx264" or "libx265" ? $"-c:v {encoder} -preset slow -crf 18"
            : $"-c:v {encoder} -b:v 20M";
        return job with { EncoderCodec=encoder,FfmpegArguments = job.FfmpegArguments.Replace("-c:v libx264 -preset slow -crf 18", options, StringComparison.Ordinal) };
    }
}
