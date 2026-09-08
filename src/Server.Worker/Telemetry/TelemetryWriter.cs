using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MphRead.Telemetry
{
    /// <summary>Optional aggregate telemetry, bounded independently of authoritative match reporting.</summary>
    public sealed class TelemetryWriter : IDisposable
    {
        private readonly string _directory;
        private readonly Channel<MatchTelemetry> _queue = Channel.CreateBounded<MatchTelemetry>(new BoundedChannelOptions(2)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        private readonly Task _worker;
        public TelemetryWriter(string directory)
        {
            _directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(_directory);
            _worker = Task.Run(WriteAsync);
        }
        public bool TryWrite(MatchTelemetry match) => _queue.Writer.TryWrite(match);
        private async Task WriteAsync()
        {
            await foreach (MatchTelemetry match in _queue.Reader.ReadAllAsync())
            {
                string target = Path.Combine(_directory, $"{match.Id:D}.telemetry.json.gz");
                string temporary = target + ".partial";
                try
                {
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        65536, FileOptions.Asynchronous))
                    {
                        await using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
                            await JsonSerializer.SerializeAsync(gzip, match);
                        output.Flush(flushToDisk: true);
                    }
                    File.Move(temporary, target);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                { Console.Error.WriteLine($"[telemetry] export {match.Id:D} failed: {error.GetType().Name}; partial retained"); }
            }
        }
        public void Dispose()
        {
            _queue.Writer.TryComplete();
            if (!_worker.Wait(TimeSpan.FromSeconds(5))) Console.Error.WriteLine("[telemetry] writer still draining after shutdown deadline");
        }
    }
}
