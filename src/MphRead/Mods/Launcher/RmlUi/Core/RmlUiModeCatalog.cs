using System;
using System.Linq;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>The native mode carousel exposes both hero objectives directly.</summary>
internal static class RmlUiModeCatalog
{
    internal static readonly MatchTypeDefinition[] GameTypes = MatchTypeCatalog.GameTypes
        .Append(new MatchTypeDefinition("Hero Shooter / Push the Payload", GameMode.PushThePayload,
            GameMode.PushThePayload, TeamOnly: true)).ToArray();
    internal static int BaseModeIndex(GameMode mode) => mode == GameMode.PushThePayload
        ? GameTypes.Length - 1 : MatchTypeCatalog.BaseModeIndex(mode);
    internal static string BaseLabel(GameMode mode) => mode == GameMode.PushThePayload
        ? "Hero Shooter / Push the Payload" : mode == GameMode.CaptureTheZone
        ? "Hero Shooter / Capture the Zone" : MatchTypeCatalog.BaseLabel(mode);
}
