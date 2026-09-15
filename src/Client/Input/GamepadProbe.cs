using System;

namespace MphRead.Mods.Input;

/// <summary>
/// Compatibility name for older launch scripts. The probe is now owned by
/// the SDL host; keeping this tiny forwarding type avoids a second GLFW event
/// pump while existing command lines continue to work.
/// </summary>
[Obsolete("Use SdlGamepadProbe; this type is retained for launch compatibility.")]
internal static class GamepadProbe
{
    public static int Run(double seconds) => global::MphRead.SdlGamepadProbe.Run(seconds);
}
