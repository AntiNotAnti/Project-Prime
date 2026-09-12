using MphRead.Entities;

namespace MphRead.Mods.Input
{
    /// <summary>
    /// Presentation-owned eligibility gate layered over the portable gesture
    /// detectors. The authoritative simulation still validates the queued
    /// intent and the Samus ability.
    /// </summary>
    public static class MorphBallBoostEligibility
    {
        public static bool IsEligible(PlayerEntity? player, bool enabled = true)
            => enabled && player != null
                && player.IsMainPlayer
                && player.Hunter == Hunter.Samus
                && player.LoadFlags.TestFlag(LoadFlags.Active)
                && player.Health > 0
                && player.IsAltForm
                && !player.IsMorphing
                && !player.IsUnmorphing
                && MphRead.Mods.ClientInputState.WindowFocused
                && !MphRead.Mods.ClientInputState.PauseOpen
                && !MphRead.Mods.Chat.ChatBox.Composing
                && !MphRead.Mods.SpectatorMode.IsSpectating;
    }
}
