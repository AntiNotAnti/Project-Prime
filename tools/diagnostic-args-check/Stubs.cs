// Inert metadata only. The test never invokes startup, preferences, native capture or game assets.
namespace MphRead { internal static class Program { public const int Version = 1; } }
namespace MphRead.Mods
{
    internal static class Branding { public const string Name = "Test"; }
    internal static class BuildVersion { public const string Display = "fixture"; }
    internal static class GameFiles { public const bool Ready = false; }
    internal static class RenderOptions { public const bool CelShading = false, Fog = false; public static string OnOff(bool value) => value ? "on" : "off"; }
}
namespace MphRead.Mods.Launcher { internal static class LauncherPrefs { public static bool DebugLogs => false; public static string Directory => "unused"; public static string WindowMode => "unused"; } }
namespace MphRead.Mods.Update { }
namespace MphRead.Mods.Network
{
    internal static class NetConfig { public const int ProtocolVersion = 53; }
    internal static class NetLag { public const bool Active = false; public static string Describe() => "unused"; }
}
