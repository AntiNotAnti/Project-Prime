using System;

namespace MphRead.Mods.Network
{
    public static class ServerOvertimeOptions
    {
        public static OvertimePolicy Parse(string? value, bool present = false) => value?.ToLowerInvariant() switch
        {
            null when !present => OvertimePolicy.Disabled,
            "disabled" => OvertimePolicy.Disabled,
            "mode" => OvertimePolicy.ModeDefault,
            _ => throw new ArgumentException("-overtime requires disabled or mode.")
        };
    }
}
