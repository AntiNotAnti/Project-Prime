namespace MphRead.Mods.Network;
internal static class UnifiedModeRules
{
    internal const uint Capability = 0x31464d55; // UMF1: filtered custom modes, authority world v10; no competitive or source-model extensions.
    internal static bool Custom(GameMode mode) => mode is GameMode.CaptureTheZone or GameMode.PushThePayload or GameMode.CaptureTheFlag or GameMode.Oddball;
}
