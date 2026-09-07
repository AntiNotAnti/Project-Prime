namespace MphRead
{
    // Legacy wire/content state values; runtime lifecycle is MatchPhase.
    public enum MatchState
    {
        InProgress = 0,
        GameOver = 1,
        Ending = 2,
        Disconnected = 3
    }
}
