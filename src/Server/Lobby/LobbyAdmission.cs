using System;
using MphRead.Identity;

namespace MphRead.Mods.Network;

public enum LobbyAdmissionCode
{
    Accepted,
    Reconnected,
    LobbyDisabled,
    WrongPhase,
    Capacity,
    Conflict,
    Invalid
}

/// <summary>
/// Identity proof is established by the networking/ticket layer. A previous
/// connection id alone is never reconnect authority.
/// </summary>
public readonly record struct LobbyAdmissionRequest(
    ulong ConnectionId,
    PlayerId? PlayerId,
    string DisplayName,
    Hunter Hunter,
    bool Observer = false,
    bool Bot = false,
    bool Admin = false,
    ushort PingMs = 0,
    byte StarTier = 0,
    byte RequestedTeam = byte.MaxValue,
    ulong PreviousConnectionId = 0,
    bool ReconnectAuthorized = false,
    bool? HostAuthorized = null);

public readonly record struct LobbyAdmissionResult(
    LobbyAdmissionCode Code, byte Slot, string Message)
{
    public bool Accepted => Code is LobbyAdmissionCode.Accepted or LobbyAdmissionCode.Reconnected;
    public bool Reconnected => Code == LobbyAdmissionCode.Reconnected;

    internal static LobbyAdmissionResult Reject(LobbyAdmissionCode code, string message)
        => new(code, byte.MaxValue, message);
}

internal readonly record struct LobbyReconnectReservation(
    LobbyPlayer Player, byte Slot, uint ExpiresAtTick);

public static class LobbyAdmission
{
    public static bool IsValid(in LobbyAdmissionRequest request, out string reason)
    {
        if (request.ConnectionId == 0)
        {
            reason = "A connection identity is required.";
            return false;
        }
        if (String.IsNullOrWhiteSpace(request.DisplayName)
            || request.DisplayName.Length > LobbyRuntime.MaximumDisplayNameLength)
        {
            reason = "The display name is invalid.";
            return false;
        }
        foreach (char character in request.DisplayName)
        {
            if (character is < ' ' or > '~')
            {
                reason = "The display name is invalid.";
                return false;
            }
        }
        if (request.Hunter > Hunter.Guardian || request.PlayerId is { IsEmpty: true }
            || request.Bot && (request.Observer || request.PlayerId.HasValue || request.PreviousConnectionId != 0)
            || request.Observer && (request.RequestedTeam != byte.MaxValue || request.HostAuthorized == true)
            || request.Bot && request.HostAuthorized == true)
        {
            reason = "The requested lobby identity is invalid.";
            return false;
        }
        reason = "";
        return true;
    }

    internal static bool ReconnectMatches(in LobbyAdmissionRequest request,
        in LobbyReconnectReservation reservation)
    {
        if (!request.ReconnectAuthorized || request.PreviousConnectionId == 0
            || request.PreviousConnectionId != reservation.Player.ConnectionId
            || request.Observer != reservation.Player.Observer) return false;
        // The caller proves possession of the old session. If either side has an
        // account identity it must also be the same authenticated account.
        return request.PlayerId.HasValue || reservation.Player.PlayerId.HasValue
            ? request.PlayerId == reservation.Player.PlayerId
            : String.Equals(request.DisplayName, reservation.Player.DisplayName, StringComparison.Ordinal);
    }
}
