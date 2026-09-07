using MphRead.Identity;

namespace MphRead;

/// <summary>Lobby-visible identity and selection state. A null PlayerId is an explicit guest or bot.</summary>
public readonly record struct LobbyPlayer(
    PlayerId? PlayerId,
    ulong ConnectionId,
    string DisplayName,
    Hunter Hunter,
    byte Team,
    bool Ready,
    bool Observer,
    bool Bot,
    bool Host,
    bool Admin,
    ushort PingMs,
    byte StarTier,
    bool Loading = false,
    bool DisconnectedGrace = false)
{
    public bool Authenticated => PlayerId.HasValue;
}
