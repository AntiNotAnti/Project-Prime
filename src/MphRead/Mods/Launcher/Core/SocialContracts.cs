using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Core;

public enum SocialTab { Friends, Players, Requests, Invites, Party, Recent, Blocked }
public enum SocialCommand
{
    SendFriendRequest, AcceptFriendRequest, DeclineFriendRequest, CancelFriendRequest,
    RemoveFriend, BlockPlayer, UnblockPlayer, SendGameInvite, JoinFriend, AcceptGameInvite,
    DeclineGameInvite, CancelGameInvite, InviteToParty, AcceptPartyInvite,
    DeclinePartyInvite, CancelPartyInvite, LeaveParty, DisbandParty, KickPartyMember,
    PromotePartyMember, InvitePartyToLobby, FollowPartyTravel, JoinPartyLeader,
    DeclinePartyTravel, CancelReservation
}

public readonly record struct SocialActionResult(bool Success, string Message)
{
    public static SocialActionResult Ok => new(true, "");
}
public sealed record SocialPrivacy(int Presence = 0, int Activity = 1, int Invites = 1, bool DoNotDisturb = false);
public sealed record SocialRow(string Key, SocialTab Tab, string PrimeId, string DisplayName,
    string Relation, string Activity, string Detail, string InviteId = "", DateTimeOffset? ExpiresAt = null,
    ImmutableArray<SocialCommand> AllowedCommands = default)
{
    public bool Allows(SocialCommand command) => !AllowedCommands.IsDefault && AllowedCommands.Contains(command);
}
public sealed record SocialPartyView(string PartyId, string LeaderPrimeId, bool IsLeader, int MemberCount);
public sealed record SocialTravelMember(string PrimeId, string DisplayName, bool IsLeader, bool IsSelf, string Status);
public sealed record SocialTravelView(string TravelId, int Revision, string Reason, string LeaderName,
    string RoomKey, string ServerName, ulong AuthorityEpoch, DateTimeOffset ExpiresAt,
    bool IsLeader, string SelfStatus, ImmutableArray<SocialTravelMember> Members);
public sealed record SocialReservedMember(string PrimeId, string DisplayName, bool IsSelf, int? Slot, string Status);
public sealed record SocialReservationView(string RequestId, ulong AuthorityEpoch, bool IncludeLeader,
    int RequestedCount, string Status, DateTimeOffset ExpiresAt, ImmutableArray<SocialReservedMember> Members);
public sealed record SocialData
{
    public string SelfPrimeId { get; init; } = "";
    public bool Connected { get; init; }
    public string Status { get; init; } = "SOCIAL OFFLINE";
    public ImmutableArray<SocialRow> Rows { get; init; } = ImmutableArray<SocialRow>.Empty;
    public SocialPartyView? Party { get; init; }
    public SocialTravelView? Travel { get; init; }
    public SocialReservationView? Reservation { get; init; }
    public SocialPrivacy Privacy { get; init; } = new();
    public bool IsSessionActive { get; init; }
    public bool CanInviteToLobby { get; init; }
    public int OnlineCount { get; init; }
    public int FriendsOnline { get; init; }
    public int IncomingRequests { get; init; }
    public int IncomingInvites { get; init; }
}
public readonly record struct SocialIntent(Guid Lifetime, ulong DataRevision, SocialCommand Command, string TargetKey,
    string PartyId, string TravelId, int TravelRevision, string ReservationId);
public sealed record SocialConfirmation(SocialIntent Intent, string Message);
public sealed record SocialViewSnapshot
{
    public Guid Lifetime { get; init; }
    public ulong Version { get; init; }
    public ulong DataRevision { get; init; }
    public SocialTab Tab { get; init; }
    public string Filter { get; init; } = "";
    public int PageIndex { get; init; }
    public int PageCount { get; init; } = 1;
    public int TotalRows { get; init; }
    public ImmutableArray<SocialRow> VisibleRows { get; init; } = ImmutableArray<SocialRow>.Empty;
    public SocialRow? SelectedRow { get; init; }
    public SocialData Data { get; init; } = new();
    public bool Busy { get; init; }
    public bool CanCancel { get; init; }
    public ImmutableArray<SocialCommand> AvailableCommands { get; init; } = ImmutableArray<SocialCommand>.Empty;
    public string Status { get; init; } = "SOCIAL OFFLINE";
    public string CommandError { get; init; } = "";
    public SocialConfirmation? PendingConfirmation { get; init; }
    public SocialPartyView? Party => Data.Party;
    public SocialTravelView? Travel => Data.Travel;
    public SocialReservationView? Reservation => Data.Reservation;
    public SocialPrivacy Privacy => Data.Privacy;
    public bool IsSessionActive => Data.IsSessionActive;
    public bool CanInviteToLobby => Data.CanInviteToLobby;
    public int OnlineCount => Data.OnlineCount;
    public int FriendsOnline => Data.FriendsOnline;
    public int IncomingRequests => Data.IncomingRequests;
    public int IncomingInvites => Data.IncomingInvites;
}

/// <summary>Verified locator and a transferable admission handle; no auth token or proof is presented.</summary>
public sealed class SocialJoinRequest : IDisposable
{
    private PartyReservedAdmission? _admission;
    private bool _admissionTaken;
    public bool IsDisposed { get; private set; }
    public string Host { get; }
    public int Port { get; }
    public string ServerName { get; }
    public string RoomKey { get; }
    public ulong AuthorityEpoch { get; }
    public SocialJoinRequest(string host, int port, string serverName, string roomKey, PartyReservedAdmission? admission = null, ulong authorityEpoch = 0)
    { Host = host; Port = port; ServerName = serverName; RoomKey = roomKey; _admission = admission; AuthorityEpoch = authorityEpoch; }
    public PartyReservedAdmission? TakeAdmission()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_admissionTaken) throw new InvalidOperationException("Social admission was already transferred.");
        _admissionTaken = true; var admission = _admission; _admission = null; return admission;
    }
    public void Dispose() { if (IsDisposed) return; IsDisposed = true; _admission?.Dispose(); _admission = null; }
}
public sealed record SocialBackendResult(bool Success, string Status, SocialData? Data = null, SocialJoinRequest? Join = null);
public interface ISocialBackend : IDisposable
{
    event Action? Changed;
    SocialData Current();
    Task<SocialData> LoadAsync(CancellationToken cancellationToken);
    Task<SocialRow?> LookupAsync(string primeId, CancellationToken cancellationToken);
    Task<SocialBackendResult> ExecuteAsync(SocialIntent intent, SocialRow? target, CancellationToken cancellationToken);
    void SavePrivacy(SocialPrivacy privacy);
}
