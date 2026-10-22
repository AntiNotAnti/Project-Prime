namespace MphRead.Mods.Multiplayer;

public readonly record struct MatchPointCue(string Text, bool KillVoice);

public static class MatchPresentation
{
    public static string? GetTimeWarning(float previousRemaining, float remaining)
    {
        if (previousRemaining > 10 && remaining <= 10) return "10 SECONDS TO VICTORY";
        if (previousRemaining > 30 && remaining <= 30) return "30 SECONDS TO VICTORY";
        return null;
    }

    public static MatchPointCue GetMatchPointCue(GameMode mode) => mode switch
    {
        GameMode.KillConfirmed or GameMode.KillConfirmedTeams => new("ONE CONFIRM TO WIN", false),
        GameMode.Headhunter => new("ONE TOKEN TO WIN", false),
        GameMode.Battle or GameMode.BattleTeams or GameMode.GunGame => new("ONE KILL TO WIN", true),
        GameMode.Capture => new("ONE CAPTURE TO WIN", false),
        GameMode.Bounty or GameMode.BountyTeams => new("ONE SCORE TO WIN", false),
        GameMode.Nodes or GameMode.NodesTeams => new("MATCH POINT", false),
        _ => default
    };
}
