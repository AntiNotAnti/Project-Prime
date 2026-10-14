using System;

namespace MphRead.Mods.Campaign
{
    public enum CampaignEndingKind
    {
        Incomplete,
        Complete
    }

    /// <summary>
    /// Immutable, value-only record of a genuine Gorea ending. The final
    /// movie and save still belong to the existing Scene/MatchStart owner;
    /// the launcher can present these facts after the world is destroyed.
    /// No duplicate StorySave instance, ROM asset or audio resource escapes.
    /// </summary>
    public readonly record struct CampaignEndingSummary(
        byte Slot, CampaignEndingKind Kind, bool SaveCommitted,
        int? CompletionPercent, int Octoliths, uint Deaths,
        uint HunterKills, uint EnemyKills)
    {
        public string Heading => Kind == CampaignEndingKind.Complete
            ? "CONGRATULATIONS // MISSION COMPLETE"
            : "MISSION COMPLETE...?";

        public string GoreaResult => Kind == CampaignEndingKind.Complete
            ? "GOREA DEFEATED" : "GOREA BATTLE INCOMPLETE";

        /// <summary>
        /// Only original ending rooms create a result. The completion
        /// calculator is optional because headless tests and broken game
        /// files must not invent a percentage or retain game assets.
        /// </summary>
        public static CampaignEndingSummary? Capture(byte slot, int roomId,
            StorySave? save, Func<StorySave, int>? calculateCompletion = null)
        {
            if (slot == 0 || save == null || roomId is not
                (CampaignNavigation.GoreaFirstRoomId or CampaignNavigation.GoreaSecondRoomId))
                return null;

            int? completion = null;
            if (calculateCompletion != null)
            {
                try
                {
                    int value = calculateCompletion(save);
                    if (value >= 0 && value <= 100) completion = value;
                }
                catch (Exception)
                {
                    // The optional percentage is presentation-only. A
                    // missing/broken logbook must not prevent final save.
                }
            }

            return new CampaignEndingSummary(slot,
                roomId == CampaignNavigation.GoreaSecondRoomId
                    ? CampaignEndingKind.Complete : CampaignEndingKind.Incomplete,
                SaveCommitted: false, completion, save.CountFoundOctoliths(),
                save.Stats?.Deaths ?? 0, save.Stats?.HunterKills ?? 0,
                save.Stats?.EnemyKills ?? 0);
        }
    }
}
