using System;

namespace MphRead;

[Flags]
public enum LobbyPermissions : ushort
{
    None = 0,
    SetReady = 1 << 0,
    SelectHunter = 1 << 1,
    RequestTeam = 1 << 2,
    SetMap = 1 << 3,
    SetMode = 1 << 4,
    SetRule = 1 << 5,
    StartMatch = 1 << 6,
    ReturnToLobby = 1 << 7,
    Rematch = 1 << 8,
    Moderate = 1 << 9,

    Player = SetReady | SelectHunter | RequestTeam,
    Host = Player | SetMap | SetMode | SetRule | StartMatch | ReturnToLobby | Rematch,
    Admin = Host | Moderate
}
