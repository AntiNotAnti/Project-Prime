using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Replay;

internal static class ReplayEncoderErrorDrain
{
    internal static Task Drain(StreamReader reader) => Drain(reader, default);
    internal static async Task Drain(StreamReader reader, CancellationToken cancellation)
    {
        // Capability-probe stderr is intentionally discarded. Keep draining it
        // alongside stdout without retaining an output-sized string.
        char[] buffer = new char[2048];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false) > 0)
            cancellation.ThrowIfCancellationRequested();
    }
}
