namespace MphRead.Mods.Network
{
    /// <summary>Server-owned round progression, with passive legacy-replay playback.</summary>
    public static class NetMatchEnd
    {
        private static bool LiveMatch => ClientOnlineRuntime.Current?.Match != null;
        public static bool MayEndOnScore => !LiveMatch && !NetSession.Active;

        public static bool InIntermission(Scene scene) => (LiveMatch || NetSession.Active)
            && (scene.Match.LegacyState != MatchState.InProgress
                || NetSession.Active && NetSession.ServerMatch?.Ending == true);

        public static void Sync(Scene scene)
        {
            // Live match state is supplied by the authoritative world stream.
            // Only a recorded legacy ending is applied through this adapter.
            if (NetSession.Active && NetSession.ServerMatch?.Ending == true
                && scene.Match.LegacyState == MatchState.InProgress)
            {
                scene.Match.MatchTime = 0;
            }
        }

        public static bool ShouldLeaveAfterMatch => !LiveMatch && !NetSession.Active;
    }
}
