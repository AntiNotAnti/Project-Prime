using System.Collections.Immutable;

namespace MphRead.Mods.Network;

public enum LobbyCommandAction
{
    None,
    StartMatch,
    ReturnToLobby,
    Rematch
}

public readonly record struct LobbyCommandResult(
    LobbyFeedbackPacket Feedback, LobbyCommandAction Action = LobbyCommandAction.None,
    MatchRules? FrozenRules = null)
{
    public bool Accepted => Feedback.Accepted;
}

public readonly record struct LobbyChatDispatch(
    LobbyFeedbackPacket Feedback, LobbyChatPacket? Chat, ImmutableArray<ulong> Recipients)
{
    public bool Accepted => Feedback.Accepted;
}

internal sealed class LobbyPeerProjection
{
    public uint PublishedRevision;
    public uint PublishedSummaryMatchId;
}
