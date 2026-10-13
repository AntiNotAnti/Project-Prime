using System;
using System.IO;
using System.Threading.Tasks;

namespace MphRead.Mods.Replay;

internal static class ReplayEncoderErrorDrain
{
    internal static async Task Drain(StreamReader reader)
    {
        // Capability-probe stderr is intentionally discarded. Keep draining it
        // alongside stdout without retaining an output-sized string.
        char[] buffer = new char[2048];
        while (await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false) > 0) { }
    }
}
