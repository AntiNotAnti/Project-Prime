namespace MphRead.Mods.UI.State;

public enum PlayEntry
{
    None,
    QuickPlay,
    Ranked,
    ServerBrowser,
    PrivateMatch,
    Practice
}

public sealed class PlayState
{
    public PlayEntry Selection { get; set; }
    public bool RankedAvailable { get; set; }
    public string RankedUnavailableReason { get; set; } = "Ranked play is unavailable.";
}
