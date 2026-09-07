namespace MphRead.Mods.Network
{
    /// <summary>The client-facing lifetime of one server session.</summary>
    public enum ClientSessionState
    {
        Disconnected,
        Connecting,
        Lobby,
        LoadingMatch,
        InMatch,
        PostMatch,
        Leaving,
        Failed
    }
}
