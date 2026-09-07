namespace MphRead.Mods.Network
{
    /// <summary>Server-owned round progression, with passive legacy-demo playback.</summary>
    public static class NetMatchEnd
    {
        public static bool MayEndOnScore => !AuthoritativePlay.Active && !NetSession.Active;

        public static bool InIntermission => (AuthoritativePlay.Active || NetSession.Active)
            && (GameState.MatchState != MatchState.InProgress
                || NetSession.Active && NetSession.ServerMatch?.Ending == true);

        public static void Sync()
        {
            // Live match state is supplied by the authoritative world stream.
            // Only a recorded legacy ending is applied through this adapter.
            if (NetSession.Active && NetSession.ServerMatch?.Ending == true
                && GameState.MatchState == MatchState.InProgress)
            {
                GameState.MatchTime = 0;
            }
        }

        public static bool ShouldLeaveAfterMatch => !AuthoritativePlay.Active && !NetSession.Active;
    }
}
