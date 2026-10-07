using System;

namespace MphRead.Mods;

// The source-linked fixture does not initialize the game's logging/session
// graph. Only the unused native-picker error sink needs this diagnostic seam.
internal static class DebugLog
{
    internal static void Exception(string category, Exception error)
        => Console.WriteLine($"[studio-check] {category}: {error.GetType().Name}");
}
