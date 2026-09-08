using System;
namespace MphRead.Mods.Network;
internal static class ServerObserverConfiguration
{
    internal static ObserverOptions Parse(string? count, string? delay)
    {
        if ((count != null && !int.TryParse(count, out _)) || (delay != null && !int.TryParse(delay, out _)))
            throw new ProgramException("Observer count and delay must be integers.");
        var options = new ObserverOptions(count == null ? 4 : int.Parse(count), delay == null ? 0 : int.Parse(delay));
        options.Validate(); return options;
    }
}
