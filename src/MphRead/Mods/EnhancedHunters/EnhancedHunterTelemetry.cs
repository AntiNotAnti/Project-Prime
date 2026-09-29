using System.Collections.Generic;
using MphRead.Mods.Network.Telemetry;

namespace MphRead.Mods.EnhancedHunters;
internal static class EnhancedHunterTelemetry
{
    private static readonly Dictionary<(Hunter, string), long> _counters = new();
    internal static void Event(Hunter hunter, string name, int amount = 1)
    {
        var key = (hunter, name);
        _counters.TryGetValue(key, out long value); _counters[key] = value + amount;
        uint id = 2166136261;
        foreach (char character in name) id = unchecked((id ^ character) * 16777619);
        ProductionTelemetry.Emit(new NetTelemetryEvent(TelemetryEventType.EnhancedHunter, 0,
            Weapon: (byte)hunter, Id: id, Result: amount));
    }
    internal static void Damage(Hunter hunter, int amount) => Event(hunter, "bonus-damage", amount);
    internal static long Count(Hunter hunter, string name) => _counters.TryGetValue((hunter, name), out long value) ? value : 0;
}
