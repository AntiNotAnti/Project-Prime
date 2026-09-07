using System;

namespace MphRead;

public enum LobbyPolicyKind : byte
{
    NoLobby,
    IntermissionLobby,
    PersistentLobby
}

/// <summary>Bounded server policy for admitting a lobby start request.</summary>
public sealed record LobbyPolicy
{
    public LobbyPolicyKind Kind { get; }
    public bool ReadyRequired { get; }
    public byte MinimumPlayers { get; }
    public bool HostMayForceStart { get; }

    public static LobbyPolicy PrivateHosted { get; } = new(
        LobbyPolicyKind.PersistentLobby, readyRequired: true, minimumPlayers: 1,
        hostMayForceStart: true);

    public LobbyPolicy(LobbyPolicyKind kind, bool readyRequired, byte minimumPlayers,
        bool hostMayForceStart)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (minimumPlayers is < 1 or > LobbyRuntime.MaximumPlayers)
            throw new ArgumentOutOfRangeException(nameof(minimumPlayers));
        Kind = kind;
        ReadyRequired = readyRequired;
        MinimumPlayers = minimumPlayers;
        HostMayForceStart = hostMayForceStart;
    }
}
