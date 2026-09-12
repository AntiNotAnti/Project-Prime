using System;

namespace MphRead.Mods.Launcher;

public enum ClientSessionPhase
{
    Gateway, OnlineHome, Lobby, Launching, InMatch, Results,
    /// <summary>Old Scene ended for an intentional Node transition.</summary>
    PreparingContinuation,
    ReturningToLobby, Closing
}

/// <summary>Client flow only. Network, lobby and gameplay ownership stay with their existing owners.</summary>
public sealed class ClientSessionCoordinator
{
    public ClientSessionPhase Phase { get; private set; } = ClientSessionPhase.Gateway;
    public MatchRunResult? LastResult { get; private set; }
    public event Action<ClientSessionPhase>? Changed;

    public void ShowHome(bool hasIdentity, bool hasLobby)
    {
        Require(ClientSessionPhase.Gateway, ClientSessionPhase.OnlineHome, ClientSessionPhase.Lobby,
            ClientSessionPhase.ReturningToLobby, ClientSessionPhase.Results,
            ClientSessionPhase.PreparingContinuation);
        Move(hasLobby ? ClientSessionPhase.Lobby : hasIdentity ? ClientSessionPhase.OnlineHome : ClientSessionPhase.Gateway);
    }
    public void BeginLaunch()
    {
        Require(ClientSessionPhase.Gateway, ClientSessionPhase.OnlineHome, ClientSessionPhase.Lobby,
            ClientSessionPhase.Results, ClientSessionPhase.PreparingContinuation);
        LastResult = null;
        Move(ClientSessionPhase.Launching);
    }
    public void NotifyMatchStarted() { Require(ClientSessionPhase.Launching); Move(ClientSessionPhase.InMatch); }
    public void NotifyMatchEnded(MatchRunResult result)
    {
        Require(ClientSessionPhase.Launching, ClientSessionPhase.InMatch);
        LastResult = result;
        Move(result.Reason == MatchExitReason.QuitApplication ? ClientSessionPhase.Closing
            : result.Reason == MatchExitReason.Transitioning ? ClientSessionPhase.PreparingContinuation
            : result.Reason == MatchExitReason.Completed ? ClientSessionPhase.Results : ClientSessionPhase.ReturningToLobby);
    }
    public void ReturnToLobby() { Require(ClientSessionPhase.Results); Move(ClientSessionPhase.ReturningToLobby); }
    public void Quit() => Move(ClientSessionPhase.Closing);
    private void Require(params ClientSessionPhase[] allowed)
    {
        if (Array.IndexOf(allowed, Phase) < 0) throw new InvalidOperationException($"Invalid client transition from {Phase}.");
    }
    private void Move(ClientSessionPhase phase) { Phase = phase; Changed?.Invoke(phase); }
}
