namespace MphRead;

/// <summary>Session-shell state. These values deliberately do not mirror match phases.</summary>
public enum LobbyPhase : byte
{
    Open,
    Starting,
    Locked
}
