using System;
using MphRead.Mods.Network;
namespace MphRead.Mods.HeroModes;
/// <summary>Presentation-only hierarchy; concrete objective IDs remain the durable runtime/wire values.</summary>
public static class HeroModeMenu
{
    public static readonly string[] Labels = ["Capture the Zone", "Push the Payload"];
    public static bool IsFamily(MatchTypeDefinition type) => type.Team == GameMode.CaptureTheZone;
    public static int Index(GameMode mode) => mode == GameMode.PushThePayload ? 1 : 0;
    public static GameMode Resolve(MatchTypeDefinition type, MatchFormat format, int objective) =>
        IsFamily(type) ? objective == 1 ? GameMode.PushThePayload : GameMode.CaptureTheZone : type.Resolve(format);
}
