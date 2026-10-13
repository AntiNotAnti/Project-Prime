namespace MphRead.Mods.EnhancedHunters;

// Historical reconstruction must not emit retired gameplay telemetry. The enum
// value and aggregate readers remain reserved for previously recorded telemetry.
internal static class EnhancedHunterTelemetry
{
    internal static void Event(Hunter hunter, string name, int amount = 1) { }
    internal static void Damage(Hunter hunter, int amount) { }
    internal static long Count(Hunter hunter, string name) => 0;
}
