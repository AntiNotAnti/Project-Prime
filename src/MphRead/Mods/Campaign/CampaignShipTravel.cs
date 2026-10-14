using System;

namespace MphRead.Mods.Campaign
{
    /// <summary>
    /// One verified ship boarding, captured as values before its Scene is
    /// disposed and exposed only after the existing save handoff succeeds.
    /// Not serialized, not a gameplay authority and never a StorySave owner.
    /// </summary>
    public readonly record struct CampaignDockReceipt(byte Slot, CampaignPlanet Planet);

    /// <summary>
    /// A game can only launch from the star map after boarding a ship in an
    /// actual campaign session. A saved unlock alone does not authorize
    /// arbitrary mid-mission fast travel from the launcher.
    /// </summary>
    public static class CampaignShipTravel
    {
        /// <summary>
        /// Initialize the destination's in-memory spawn checkpoint only
        /// after TryChooseLanding has authorized a departure. If moving to a
        /// different planet, the old room's entity ID cannot name a spawn
        /// point on the new planet. The already-persisted save is untouched
        /// until the ordinary Adventure commit.
        /// </summary>
        public static void PrepareLandingSpawn(StorySave save, CampaignDockReceipt dock,
            CampaignPlanet destination)
        {
            ArgumentNullException.ThrowIfNull(save);
            if (destination == dock.Planet) return; // Exit Ship: retain hatch position.
            save.CheckpointEntityId = -1;
            save.CheckpointRoomId = CampaignNavigation.Get(destination).LandingRoomId;
        }

        public static bool TryChooseLanding(StorySave? save, CampaignDockReceipt? dock,
            byte slot, bool newGame, CampaignPlanet destination,
            out string roomName, out string reason)
        {
            roomName = "";
            if (newGame)
            {
                reason = "A new expedition starts at Celestial Archives.";
                return false;
            }
            if (save == null)
            {
                reason = "This save slot cannot be loaded.";
                return false;
            }
            if (dock is not { } receipt || receipt.Slot != slot)
            {
                reason = "Board the gunship before using its star map.";
                return false;
            }
            if (!CampaignNavigation.TryLandingRoom(save, destination, out roomName))
            {
                reason = "This planet has not been unlocked in the selected save.";
                return false;
            }
            reason = "";
            return true;
        }
    }
}
