using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

public enum NativeSocialLobbyAction { Invite, JoinFriend, Accept, Decline, Cancel }
public sealed record NativeSocialInvitation(string InviteId, string PrimeId, string DisplayName,
    bool Incoming, string LobbyId, string RoomKey, string ServerName, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
public sealed record NativeSocialJoinTarget(string SelfPrimeId, string LobbyId, string Host,
    int Port, byte Protocol, ulong AuthorityEpoch, string RoomKey, string ServerName,
    DateTimeOffset ExpiresAt)
{
    [JsonIgnore] internal object? AccountWitness { get; init; }
}
public sealed record NativeSocialLobbyCommand(NativeSocialLobbyAction Action,
    NativeSocialDirectory Directory, NativeSocialPerson? Friend, NativeSocialInvitation? Invitation,
    NativePartyLocalLobby? LocalLobby);
public sealed record NativeSocialLobbyResult(NativeSocialDirectory Directory, NativeSocialJoinTarget? JoinTarget);

public static class NativeSocialInvitationValidation
{
    private static bool Uuid(string value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty;
    public static bool Valid(NativeSocialInvitation? value) => value != null
        && Uuid(value.InviteId) && Uuid(value.LobbyId)
        && NativeSocialFriendsController.ValidPrimeId(value.PrimeId)
        && value.DisplayName is { Length: > 0 and <= 128 }
        && value.RoomKey is { Length: > 0 and <= 128 } && value.ServerName is { Length: <= 96 }
        && value.Status is "pending" or "accepted" or "declined" or "cancelled" or "expired"
        && value.CreatedAt != default && value.ExpiresAt > value.CreatedAt;
    public static bool Actionable(NativeSocialInvitation? value, DateTimeOffset now)
        => Valid(value) && value!.ExpiresAt > now && value.CreatedAt <= now
            && value.Status is "pending" or "accepted";
    public static bool ValidTarget(NativeSocialJoinTarget? value, DateTimeOffset now)
        => value != null && NativeSocialFriendsController.ValidPrimeId(value.SelfPrimeId)
        && Uuid(value.LobbyId) && IPAddress.TryParse(value.Host, out var address)
        && address.AddressFamily == AddressFamily.InterNetwork && value.Host.Split('.').Length == 4
        && value.Host.Split('.').All(p => p.Length is > 0 and <= 3 && p.All(Char.IsAsciiDigit))
        && address.ToString() == value.Host
        && value.Port is > 0 and <= 65535 && value.Protocol > 0 && value.AuthorityEpoch > 0
        && value.RoomKey is { Length: > 0 and <= 128 } && value.ServerName is { Length: <= 96 }
        && value.ExpiresAt > now;
    internal static bool Epoch(string text, out ulong epoch) => UInt64.TryParse(text,
        NumberStyles.None, CultureInfo.InvariantCulture, out epoch) && epoch > 0
        && epoch.ToString(CultureInfo.InvariantCulture) == text;
}

public sealed partial class NativeSocialFriendsController
{
    private NativeSocialJoinTarget? _pendingJoin;
    private string _selectedInvite = "";
    private readonly TimeProvider _clock;
    private DateTimeOffset _invitationBoundary = DateTimeOffset.MaxValue;
    private void NextInvitationBoundary()
    {
        var now = _clock.GetUtcNow();
        _invitationBoundary = (_directory?.Invitations ?? Array.Empty<NativeSocialInvitation>())
            .SelectMany(i => new[] { i.CreatedAt, i.ExpiresAt }).Where(t => t > now)
            .DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
    }
    private NativeSocialInvitation[] Invitations() => _directory?.Invitations
        .Where(i => NativeSocialInvitationValidation.Actionable(i, _clock.GetUtcNow())
            && (_filter.Length == 0 || i.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || i.PrimeId.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || i.RoomKey.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || LauncherRoomDisplayName.Get(i.RoomKey).Contains(_filter, StringComparison.OrdinalIgnoreCase)))
        .OrderByDescending(i => i.CreatedAt).ThenBy(i => i.InviteId, StringComparer.Ordinal).ToArray()
        ?? Array.Empty<NativeSocialInvitation>();

    public bool CanLobby(NativeSocialLobbyAction action, NativePartyLocalLobby? local = null,
        NativePresenceDirectory? presence = null)
    {
        CheckOwner();
        if (_busy || !_connected || _directory?.InvitationsAvailable != true || _pendingJoin != null) return false;
        var view = Current();
        bool visibleFriend = view.Selected != null && view.Rows.Contains(view.Selected);
        if (action == NativeSocialLobbyAction.Invite)
            return visibleFriend && view.Selected?.Relation == NativeSocialRelation.Friend && local != null;
        if (action == NativeSocialLobbyAction.JoinFriend)
            return visibleFriend && view.Selected?.Relation == NativeSocialRelation.Friend
                && NativePresenceController.ValidDirectory(presence)
                && presence!.SelfPrimeId == _selfId
                && ReferenceEquals(presence.AccountWitness, _directory.AccountWitness)
                && presence.People.Any(p => p.PrimeId == view.Selected.PrimeId && p.Joinable
                    && p.LastSeen <= _clock.GetUtcNow() && p.LastSeen.AddSeconds(45) > _clock.GetUtcNow());
        var invitation = view.SelectedInvitation;
        if (!NativeSocialInvitationValidation.Actionable(invitation, _clock.GetUtcNow())) return false;
        return action switch
        {
            NativeSocialLobbyAction.Accept => invitation!.Incoming,
            NativeSocialLobbyAction.Decline => invitation!.Incoming && invitation.Status == "pending",
            NativeSocialLobbyAction.Cancel => !invitation!.Incoming && invitation.Status == "pending",
            _ => false
        };
    }

    public bool LobbyCommand(NativeSocialLobbyAction action, NativePartyLocalLobby? local = null,
        NativePresenceDirectory? presence = null)
    {
        CheckOwner();
        if (!CanLobby(action, local, presence)) return false;
        var view = Current();
        var friend = view.Selected is { } selected
            ? _directory!.People.FirstOrDefault(p => p.PrimeId.Equals(selected.PrimeId, StringComparison.OrdinalIgnoreCase)
                && p.Relation == selected.Relation) : null;
        var command = new NativeSocialLobbyCommand(action, _directory!, friend, view.SelectedInvitation, local);
        Begin("VERIFYING LOBBY INVITATION", async token =>
        {
            var result = await _service.ExecuteLobbyAsync(command, token).ConfigureAwait(false);
            if (result.Directory.SelfPrimeId != command.Directory.SelfPrimeId
                || !ReferenceEquals(result.Directory.AccountWitness, command.Directory.AccountWitness))
                throw new InvalidOperationException("Social account changed.");
            return (result.Directory, null, result.JoinTarget);
        });
        return true;
    }

    public bool TryTakeJoin(out NativeSocialJoinTarget target)
    {
        CheckOwner();
        target = _pendingJoin!; _pendingJoin = null;
        return target != null && IsJoinCurrent(target);
    }

    public bool IsJoinCurrent(NativeSocialJoinTarget target)
    {
        CheckOwner();
        return _directory != null && _service.IsCurrent(_directory)
            && ReferenceEquals(target.AccountWitness, _directory.AccountWitness)
            && target.SelfPrimeId == _selfId
            && NativeSocialInvitationValidation.ValidTarget(target, _clock.GetUtcNow());
    }
}
