using System;
using MphRead.Entities;

namespace MphRead.Mods.Campaign
{
    /// <summary>
    /// Delano 7 is the Sylux-authored gunship already shipped with the user's
    /// extracted game files. Visual-only platform selection: the original
    /// SamusShip rig/collision/messages are preserved for story scripts.
    /// Never substitutes the model in multiplayer, replay replicas, or for a
    /// non-Sylux hunter.
    /// </summary>
    internal static class CampaignShipVisualPolicy
    {
        internal const string StoryShipModel = "SamusShip";
        internal const string DelanoModel = "SyluxShip";
        internal const string DelanoTurretModel = "SyluxTurret";

        internal static bool UseDelano7(GameMode mode, PlatformFlags flags,
            string? originalModel, Hunter? hunter, bool replica)
        {
            return mode == GameMode.SinglePlayer && !replica && hunter == Hunter.Sylux
                && (flags & PlatformFlags.SamusShip) != 0
                && String.Equals(originalModel, StoryShipModel, StringComparison.Ordinal);
        }
    }
}
