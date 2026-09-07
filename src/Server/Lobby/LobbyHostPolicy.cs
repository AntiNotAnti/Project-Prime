using System.Collections.Generic;

namespace MphRead.Mods.Network;

public static class LobbyHostPolicy
{
    /// <summary>Stable successor selection: authenticated join order, then guests.</summary>
    public static ulong SelectSuccessor(IReadOnlyList<LobbyPlayer> players, ulong excludedConnectionId = 0)
    {
        foreach (LobbyPlayer player in players)
            if (!player.Bot && !player.Observer && player.Authenticated && player.ConnectionId != excludedConnectionId)
                return player.ConnectionId;
        foreach (LobbyPlayer player in players)
            if (!player.Bot && !player.Observer && !player.Authenticated && player.ConnectionId != excludedConnectionId)
                return player.ConnectionId;
        return 0;
    }

    public static bool ShouldAssignInitialHost(LobbyRuntime runtime, in LobbyAdmissionRequest request)
        => runtime.HostConnectionId == 0 && !request.Bot && !request.Observer;
}
