namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// The one place launcher presentations turn an Adventure choice into a
    /// launch plan. Loading/new-game state still belongs to AdventureSave and
    /// MatchStart; this only persists the player's menu choice.
    /// </summary>
    public static class AdventureLaunch
    {
        public static LaunchPlan Create(byte slot, bool newGame, Hunter hunter,
            Campaign.CampaignPlanet? destination = null, bool exitShipToSurface = false)
        {
            if (newGame && destination.HasValue)
                throw new System.ArgumentException("A new game cannot bypass its starting planet.", nameof(destination));
            if (exitShipToSurface && (newGame || !destination.HasValue))
                throw new System.ArgumentException("Exiting the gunship requires a selected existing planet.", nameof(exitShipToSurface));
            LauncherPrefs.LastHunter = hunter;
            LauncherPrefs.LastKind = (int)LaunchKind.Adventure;
            LauncherPrefs.Save();
            return new LaunchPlan
            {
                Kind = LaunchKind.Adventure,
                Hunter = hunter,
                PlayerName = LauncherPrefs.PlayerName,
                RoomKey = "",
                SaveSlot = slot,
                NewGame = newGame,
                AdventureDestination = destination,
                ExitShipToSurface = exitShipToSurface
            };
        }
    }
}
