using System.Collections.Generic;

namespace MphRead.Mods.Network;

public readonly record struct LobbyAuthorityDecision(LobbyFeedbackCode Code, string Message)
{
    public bool Accepted => Code == LobbyFeedbackCode.None;
    public static LobbyAuthorityDecision Allow => new(LobbyFeedbackCode.None, "");
}

/// <summary>Single-writer validation and request replay protection.</summary>
public sealed class LobbyAuthority
{
    private readonly Dictionary<ulong, uint> _lastRequests = new();

    public LobbyAuthorityDecision Validate(LobbyRuntime runtime, ulong connectionId,
        uint sessionId, uint revision, uint requestId, LobbyPermissions required,
        bool openPhaseRequired = true)
    {
        if (requestId == 0) return new(LobbyFeedbackCode.InvalidRequest, "A request identity is required.");
        if (_lastRequests.TryGetValue(connectionId, out uint previous)
            && !Sequence32.IsNewer(requestId, previous))
            return new(LobbyFeedbackCode.DuplicateRequest, "This request was already processed.");
        _lastRequests[connectionId] = requestId;

        if (sessionId != runtime.SessionId)
            return new(LobbyFeedbackCode.StaleSession, "The lobby session changed; refresh and try again.");
        if (revision != runtime.Revision)
            return new(LobbyFeedbackCode.StaleRevision, "The lobby changed; refresh and try again.");
        if (!runtime.TryFind(connectionId, out LobbyPlayer player))
            return new(LobbyFeedbackCode.NotPermitted, "The sender is not a lobby member.");
        if (openPhaseRequired && runtime.Phase != LobbyPhase.Open)
            return new(LobbyFeedbackCode.WrongPhase, "That action is unavailable after lobby selections lock.");
        LobbyPermissions effective = player.Observer
            ? (player.Admin ? LobbyPermissions.Moderate : LobbyPermissions.None)
            : runtime.PermissionsFor(connectionId);
        if ((effective & required) != required)
            return new(LobbyFeedbackCode.NotPermitted, "The sender does not have permission for that action.");
        return LobbyAuthorityDecision.Allow;
    }

    public void Forget(ulong connectionId) => _lastRequests.Remove(connectionId);
}
