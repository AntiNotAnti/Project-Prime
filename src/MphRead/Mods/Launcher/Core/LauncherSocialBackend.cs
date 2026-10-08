#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Copies the existing authenticated Social service read models; never copies auth tokens.</summary>
public sealed class LauncherSocialBackend : ISocialBackend
{
    private readonly object _sync = new();
    private SocialSnapshot _friends = new();
    private bool _connected, _disposed;
    private int _dirty = 1;
    private SocialData? _cached;
    private string _status = "SOCIAL OFFLINE";
    public event Action? Changed;
    public LauncherSocialBackend()
    {
        if (SocialClient.Current is { } cached) { _friends = cached; _connected = true; _status = "SOCIAL READY"; }
        SocialPresenceClient.Changed += PresenceChanged;
        SocialInviteClient.Changed += InvitesChanged;
        SocialPartyClient.Changed += PartyChanged;
    }
    private void Invalidate() { Interlocked.Exchange(ref _dirty, 1); Changed?.Invoke(); }
    private void PresenceChanged(SocialPresenceSnapshot _) => Invalidate();
    private void InvitesChanged(SocialInviteSnapshot _) => Invalidate();
    private void PartyChanged(SocialPartySnapshot _) => Invalidate();
    public SocialData Current()
    { lock (_sync) return Capture(); }
    private SocialData Capture()
    {
        bool sessionActive = NetSession.Active;
        bool canInvite = SocialInviteClient.CurrentLobby != null && sessionActive && NetSession.IsInLobby;
        if (Interlocked.Exchange(ref _dirty, 0) == 0 && _cached is { } cached
            && cached.IsSessionActive == sessionActive && cached.CanInviteToLobby == canInvite)
            return cached;
        SocialSnapshot friends; bool connected; string status;
        lock (_sync) { friends = _friends; connected = _connected; status = _status; }
        SocialPresenceSnapshot presence = SocialPresenceClient.Current;
        SocialInviteSnapshot invites = SocialInviteClient.Current;
        SocialPartySnapshot parties = SocialPartyClient.Current;
        SocialParty? party = parties.Party;
        var rows = ImmutableArray.CreateBuilder<SocialRow>();
        foreach (SocialPlayer friend in friends.Friends) rows.Add(Person(friend.PrimeId, friend.DisplayName, SocialTab.Friends));
        foreach (SocialOnlinePlayer player in presence.Players) rows.Add(Person(player.PrimeId, player.DisplayName, SocialTab.Players));
        foreach (SocialPlayer player in friends.IncomingRequests) rows.Add(Person(player.PrimeId, player.DisplayName, SocialTab.Requests));
        foreach (SocialPlayer player in friends.OutgoingRequests) rows.Add(Person(player.PrimeId, player.DisplayName, SocialTab.Requests));
        foreach (SocialPlayer player in friends.Blocked) rows.Add(Person(player.PrimeId, player.DisplayName, SocialTab.Blocked));
        foreach (SocialRecentPlayer player in parties.RecentPlayers)
            rows.Add(Person(player.PrimeId, player.DisplayName, SocialTab.Recent) with
            { Detail = $"{player.Encounters} encounters; last played {player.LastSeen.LocalDateTime:g}" });
        foreach (SocialGameInvite invite in invites.Incoming)
            rows.Add(GameInvite(invite, true));
        foreach (SocialGameInvite invite in invites.Outgoing)
            rows.Add(GameInvite(invite, false));
        if (party != null)
            foreach (SocialPartyMember member in party.Members)
            {
                var row = Person(member.PrimeId, member.DisplayName, SocialTab.Party);
                var commands = row.AllowedCommands.ToBuilder();
                if (member.IsSelf) commands.Clear();
                else if (party.IsLeader && !member.IsLeader)
                { commands.Add(SocialCommand.KickPartyMember); commands.Add(SocialCommand.PromotePartyMember); }
                string travelStatus = parties.Travel?.Members.FirstOrDefault(item => Id(item.PrimeId, member.PrimeId))?.Status ?? "";
                rows.Add(row with { Relation = member.IsSelf ? "SELF" : member.IsLeader ? "PARTY LEADER" : "PARTY MEMBER",
                    Detail = row.Detail + (travelStatus.Length > 0 ? " / " + travelStatus.Replace('_', ' ') : ""), AllowedCommands = commands.ToImmutable() });
            }
        foreach (SocialPartyInvite invite in parties.IncomingPartyInvites) rows.Add(PartyInvite(invite, true));
        foreach (SocialPartyInvite invite in parties.OutgoingPartyInvites) rows.Add(PartyInvite(invite, false));
        SocialTravelView? travelView = parties.Travel is { } travel && travel.TryAuthorityEpoch(out ulong travelEpoch)
            ? new(travel.TravelId, travel.Revision, travel.Reason, travel.LeaderDisplayName, travel.RoomKey,
                travel.ServerName, travelEpoch, travel.ExpiresAt, travel.IsLeader, travel.SelfStatus,
                travel.Members.Select(item => new SocialTravelMember(item.PrimeId, item.DisplayName, item.IsLeader, item.IsSelf, item.Status)).ToImmutableArray()) : null;
        SocialReservationView? reservationView = parties.Reservation is { } reservation && reservation.TryAuthorityEpoch(out ulong reservationEpoch)
            ? new(reservation.RequestId, reservationEpoch, reservation.IncludeLeader, reservation.RequestedCount,
                reservation.Status, reservation.ExpiresAt, reservation.Members.Select(item =>
                    new SocialReservedMember(item.PrimeId, item.DisplayName, item.IsSelf, item.Slot, item.Status)).ToImmutableArray()) : null;
        return _cached = new() { SelfPrimeId = friends.Self.PrimeId, Connected = connected, Status = status, Rows = rows.ToImmutable(),
            Party = party == null ? null : new(party.PartyId, party.LeaderPrimeId, party.IsLeader, party.Members.Count),
            Travel = travelView, Reservation = reservationView, Privacy = Privacy(), IsSessionActive = NetSession.Active,
            CanInviteToLobby = canInvite, OnlineCount = presence.Players.Count,
            FriendsOnline = presence.Players.Count(item => item.IsFriend), IncomingRequests = friends.IncomingRequests.Count,
            IncomingInvites = invites.Incoming.Count + parties.IncomingPartyInvites.Count };

        SocialRow Person(string primeId, string name, SocialTab tab)
        {
            string relation = friends.Blocked.Any(item => Id(item.PrimeId, primeId)) ? "BLOCKED"
                : friends.Friends.Any(item => Id(item.PrimeId, primeId)) ? "FRIEND"
                : friends.IncomingRequests.Any(item => Id(item.PrimeId, primeId)) ? "INCOMING REQUEST"
                : friends.OutgoingRequests.Any(item => Id(item.PrimeId, primeId)) ? "OUTGOING REQUEST" : "PLAYER";
            SocialOnlinePlayer? online = presence.Players.FirstOrDefault(item => Id(item.PrimeId, primeId));
            var commands = ImmutableArray.CreateBuilder<SocialCommand>();
            if (!Id(primeId, friends.Self.PrimeId))
            {
                if (relation == "BLOCKED") commands.Add(SocialCommand.UnblockPlayer);
                else
                {
                    commands.Add(SocialCommand.BlockPlayer);
                    switch (relation)
                    {
                        case "FRIEND": commands.Add(SocialCommand.RemoveFriend); break;
                        case "INCOMING REQUEST": commands.Add(SocialCommand.AcceptFriendRequest); commands.Add(SocialCommand.DeclineFriendRequest); break;
                        case "OUTGOING REQUEST": commands.Add(SocialCommand.CancelFriendRequest); break;
                        default: commands.Add(SocialCommand.SendFriendRequest); break;
                    }
                    if (canInvite) commands.Add(SocialCommand.SendGameInvite);
                    if (relation == "FRIEND" && !NetSession.Active && online is { Joinable: true } && !String.IsNullOrWhiteSpace(online.LobbyId))
                        commands.Add(SocialCommand.JoinFriend);
                    if (relation == "FRIEND" && party?.IsLeader != false && (party == null || party.Members.Count < 8)
                        && party?.Members.Any(item => Id(item.PrimeId, primeId)) != true
                        && !parties.OutgoingPartyInvites.Any(item => Id(item.PrimeId, primeId)))
                        commands.Add(SocialCommand.InviteToParty);
                }
            }
            return new($"{tab}:{primeId}", tab, primeId, name, relation, online?.Activity.Replace('_', ' ') ?? "OFFLINE",
                online?.RoomKey ?? "", AllowedCommands: commands.ToImmutable());
        }
        SocialRow GameInvite(SocialGameInvite invite, bool incoming)
        {
            var commands = ImmutableArray.CreateBuilder<SocialCommand>();
            if (invite.ExpiresAt > DateTimeOffset.UtcNow)
            {
                if (incoming)
                {
                    if (!NetSession.Active) commands.Add(SocialCommand.AcceptGameInvite);
                    if (invite.Status == "pending") commands.Add(SocialCommand.DeclineGameInvite);
                }
                else if (invite.Status == "pending") commands.Add(SocialCommand.CancelGameInvite);
            }
            return new("game:" + invite.InviteId, SocialTab.Invites, invite.PrimeId, invite.DisplayName,
                incoming ? "GAME INVITE" : "GAME INVITE SENT", invite.Status,
                invite.ServerName + " / " + invite.RoomKey, invite.InviteId, invite.ExpiresAt, commands.ToImmutable());
        }
        SocialRow PartyInvite(SocialPartyInvite invite, bool incoming)
        {
            var commands = ImmutableArray.CreateBuilder<SocialCommand>();
            if (invite.ExpiresAt > DateTimeOffset.UtcNow)
            {
                if (incoming) { commands.Add(SocialCommand.AcceptPartyInvite); commands.Add(SocialCommand.DeclinePartyInvite); }
                else commands.Add(SocialCommand.CancelPartyInvite);
            }
            return new("party-invite:" + invite.InviteId, SocialTab.Party, invite.PrimeId, invite.DisplayName,
                incoming ? "PARTY INVITE" : "PARTY INVITE SENT", "pending", "Party invitation", invite.InviteId, invite.ExpiresAt, commands.ToImmutable());
        }
    }
    public async Task<SocialData> LoadAsync(CancellationToken token)
    {
        try
        {
            SocialSnapshot friends = await SocialClient.LoadAsync(token).ConfigureAwait(false);
            lock (_sync) { _friends = friends; _connected = true; _status = "SOCIAL READY"; }
            Invalidate();
            await Task.WhenAll(SocialInviteClient.LoadAsync(token), SocialPartyClient.LoadAsync(token)).ConfigureAwait(false);
            SocialPresenceClient.RefreshNow(); return Current();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        { lock (_sync) { _connected = false; _status = Failure(ex); } Invalidate(); return Current(); }
    }
    public async Task<SocialRow?> LookupAsync(string primeId, CancellationToken token)
    {
        try
        {
            SocialLookupResult result = await SocialClient.LookupAsync(primeId, token).ConfigureAwait(false);
            if (!result.Found || result.Player == null) return null;
            SocialData data = Current();
            SocialRow? existing = data.Rows.FirstOrDefault(row => Id(row.PrimeId, primeId));
            return existing != null ? existing with { Key = "lookup:" + primeId, Tab = SocialTab.Players }
                : new("lookup:" + primeId, SocialTab.Players, primeId, result.Player.DisplayName, "PLAYER", "OFFLINE", "PRIME ID LOOKUP",
                    AllowedCommands: ImmutableArray.Create(SocialCommand.SendFriendRequest, SocialCommand.BlockPlayer));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new InvalidOperationException(Failure(ex)); }
    }
    public async Task<SocialBackendResult> ExecuteAsync(SocialIntent intent, SocialRow? target, CancellationToken token)
    {
        try
        {
            SocialPartySnapshot party = SocialPartyClient.Current;
            if (intent.PartyId != (party.Party?.PartyId ?? "") || intent.TravelId != (party.Travel?.TravelId ?? "")
                || intent.TravelRevision != (party.Travel?.Revision ?? 0) || intent.ReservationId != (party.Reservation?.RequestId ?? ""))
                return new(false, "Social state changed. Review the current party and try again.");
            string primeId = target?.PrimeId ?? "", inviteId = target?.InviteId ?? "";
            if (intent.Command is SocialCommand.JoinFriend or SocialCommand.AcceptGameInvite or SocialCommand.FollowPartyTravel or SocialCommand.JoinPartyLeader)
            {
                if (NetSession.Active) return new(false, "Leave your current session before joining another lobby.");
                SocialJoinResolution joined = intent.Command switch
                {
                    SocialCommand.JoinFriend => await SocialInviteClient.PrepareFriendJoinAsync(primeId, token).ConfigureAwait(false),
                    SocialCommand.AcceptGameInvite => await SocialInviteClient.PrepareInviteJoinAsync(inviteId, accept: true, token).ConfigureAwait(false),
                    SocialCommand.FollowPartyTravel => await SocialPartyClient.PrepareTravelJoinAsync(token).ConfigureAwait(false),
                    _ => await SocialPartyClient.PrepareLeaderJoinAsync(token).ConfigureAwait(false)
                };
                if (!joined.Success) { joined.PartyAdmission?.Dispose(); return new(false, joined.Error, Current()); }
                return new(true, "LOBBY VERIFIED", Current(), new(joined.Host, joined.Port, joined.ServerName,
                    joined.RoomKey, joined.PartyAdmission, joined.AuthorityEpoch));
            }
            if (intent.Command <= SocialCommand.UnblockPlayer)
            {
                SocialMutationResult result = intent.Command switch
                {
                    SocialCommand.SendFriendRequest => await SocialClient.SendFriendRequestAsync(primeId, token).ConfigureAwait(false),
                    SocialCommand.AcceptFriendRequest => await SocialClient.AcceptFriendRequestAsync(primeId, token).ConfigureAwait(false),
                    SocialCommand.DeclineFriendRequest => await SocialClient.DeclineFriendRequestAsync(primeId, token).ConfigureAwait(false),
                    SocialCommand.CancelFriendRequest => await SocialClient.CancelFriendRequestAsync(primeId, token).ConfigureAwait(false),
                    SocialCommand.RemoveFriend => await SocialClient.RemoveFriendAsync(primeId, token).ConfigureAwait(false),
                    SocialCommand.BlockPlayer => await SocialClient.BlockPlayerAsync(primeId, token).ConfigureAwait(false),
                    _ => await SocialClient.UnblockPlayerAsync(primeId, token).ConfigureAwait(false)
                };
                if (result.Snapshot != null) { lock (_sync) _friends = result.Snapshot; Invalidate(); }
                return new(result.Success, result.Status, Current());
            }
            if (intent.Command is SocialCommand.SendGameInvite or SocialCommand.DeclineGameInvite or SocialCommand.CancelGameInvite)
            {
                SocialInviteMutationResult result = intent.Command switch
                {
                    SocialCommand.SendGameInvite => await SocialInviteClient.SendInviteAsync(primeId, token).ConfigureAwait(false),
                    SocialCommand.DeclineGameInvite => await SocialInviteClient.DeclineInviteAsync(inviteId, token).ConfigureAwait(false),
                    _ => await SocialInviteClient.CancelInviteAsync(inviteId, token).ConfigureAwait(false)
                };
                return new(result.Success, result.Status, Current());
            }
            if (intent.Command == SocialCommand.InvitePartyToLobby)
            {
                PartyGameInviteResult result = await SocialPartyClient.InvitePartyToLobbyAsync(token).ConfigureAwait(false);
                return new(result.Success, $"{result.Status}: {result.Sent} sent / {result.Refused} refused", Current());
            }
            SocialPartyMutationResult mutation = intent.Command switch
            {
                SocialCommand.InviteToParty => await SocialPartyClient.InviteAsync(primeId, token).ConfigureAwait(false),
                SocialCommand.AcceptPartyInvite => await SocialPartyClient.AcceptInviteAsync(inviteId, token).ConfigureAwait(false),
                SocialCommand.DeclinePartyInvite => await SocialPartyClient.DeclineInviteAsync(inviteId, token).ConfigureAwait(false),
                SocialCommand.CancelPartyInvite => await SocialPartyClient.CancelInviteAsync(inviteId, token).ConfigureAwait(false),
                SocialCommand.LeaveParty => await SocialPartyClient.LeaveAsync(token).ConfigureAwait(false),
                SocialCommand.DisbandParty => await SocialPartyClient.DisbandAsync(token).ConfigureAwait(false),
                SocialCommand.KickPartyMember => await SocialPartyClient.KickAsync(primeId, token).ConfigureAwait(false),
                SocialCommand.PromotePartyMember => await SocialPartyClient.PromoteAsync(primeId, token).ConfigureAwait(false),
                SocialCommand.DeclinePartyTravel => await SocialPartyClient.DeclineTravelAsync(token).ConfigureAwait(false),
                SocialCommand.CancelReservation => await SocialPartyClient.CancelReservationAsync(intent.ReservationId, token).ConfigureAwait(false),
                _ => throw new InvalidOperationException("Unknown Social command.")
            };
            return new(mutation.Success, mutation.Status, Current());
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new(false, Failure(ex)); }
    }
    public void SavePrivacy(SocialPrivacy privacy)
    {
        LauncherPrefs.PresenceVisibility = (SocialPresenceVisibility)privacy.Presence;
        LauncherPrefs.ActivityVisibility = (SocialActivityVisibility)privacy.Activity;
        LauncherPrefs.InvitePolicy = (SocialInvitePolicy)privacy.Invites;
        LauncherPrefs.DoNotDisturb = privacy.DoNotDisturb;
        LauncherPrefs.SocialPrivacyConfigured = true; LauncherPrefs.Save(); SocialPresenceClient.NotifyPrivacyChanged();
        Invalidate();
    }
    private static SocialPrivacy Privacy() => new((int)LauncherPrefs.PresenceVisibility,
        (int)LauncherPrefs.ActivityVisibility, (int)LauncherPrefs.InvitePolicy, LauncherPrefs.DoNotDisturb);
    private static bool Id(string left, string right) => String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string Failure(Exception ex) => ex is OperationCanceledException ? "Social request cancelled."
        : ex.Message.Contains("auth", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("401", StringComparison.Ordinal)
            ? "Social authentication failed. Open Hunter License Account and refresh, then retry."
            : "Social service unavailable. Refresh to retry.";
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        SocialPresenceClient.Changed -= PresenceChanged; SocialInviteClient.Changed -= InvitesChanged; SocialPartyClient.Changed -= PartyChanged;
    }
}

/// <summary>One process lifecycle owner, independent of whether the Social page is open.</summary>
public static class SocialRuntime
{
    public static SocialHomeSummary Summary
    {
        get
        {
            SocialSnapshot? directory = SocialClient.Current;
            SocialPresenceSnapshot presence = SocialPresenceClient.Current;
            SocialInviteSnapshot invites = SocialInviteClient.Current;
            SocialPartySnapshot parties = SocialPartyClient.Current;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var friends = presence.Players.Where(player => player.IsFriend)
                .OrderByDescending(player => player.Joinable)
                .ThenBy(player => player.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
            SocialParty? party = parties.Party;
            return new(directory != null, directory?.IncomingRequests.Count ?? 0,
                invites.Incoming.Count(invite => invite.ExpiresAt > now && invite.Status is "pending" or "accepted"),
                parties.IncomingPartyInvites.Count(invite => invite.ExpiresAt > now), friends.Length,
                friends.Take(3).Select(player => new SocialHomeFriend(player.PrimeId, player.DisplayName,
                    player.Activity, player.RoomKey ?? "", player.Joinable)).ToImmutableArray(),
                party == null ? null : new(party.PartyId, party.LeaderPrimeId, party.IsLeader, party.Members.Count),
                parties.Travel is { IsLeader: false, SelfStatus: "pending" } travel && travel.ExpiresAt > now,
                parties.Reservation?.Status ?? "", LauncherPrefs.DoNotDisturb);
        }
    }
    public static void Start() { SocialPresenceClient.Start(); SocialInviteClient.Start(); SocialPartyClient.Start(); }
    public static void Stop() { SocialPartyClient.Stop(); SocialInviteClient.Stop(); SocialPresenceClient.Stop(); }
    public static void Suspend() => Stop();
    public static void Resume() => Start();
    public static void NotifySessionChanged() { SocialPresenceClient.RefreshNow(); SocialInviteClient.RefreshNow(); SocialPartyClient.RefreshNow(); }
}

public sealed record SocialLeaderAdmissionResult(bool Success, string Error, PartyReservedAdmission? Admission = null, string RequestId = "");

/// <summary>Shared Quick Play entry to the existing server-authoritative whole-party reservation.</summary>
public static class SocialMatchmaking
{
    public static SocialPartyView? CurrentParty
    {
        get
        {
            SocialParty? party = SocialPartyClient.Current.Party;
            return party == null ? null : new(party.PartyId, party.LeaderPrimeId, party.IsLeader, party.Members.Count);
        }
    }
    public static async Task<SocialLeaderAdmissionResult> PrepareLeaderReservationAsync(
        ServerBrowserEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            PartyReservationPreparation result = await SocialPartyClient.PrepareLeaderReservationAsync(entry, cancellationToken).ConfigureAwait(false);
            return new(result.Success, result.Error, result.Admission, result.Reservation?.RequestId ?? "");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return new(false, "Party reservation service unavailable. Refresh Social and retry."); }
    }
    public static void NoteQuickPlayTravel() => SocialPartyClient.NoteQuickPlayTravel();
    public static async Task<SocialActionResult> CancelReservationAsync(string requestId, CancellationToken cancellationToken = default)
    {
        try
        {
            SocialPartyMutationResult result = await SocialPartyClient.CancelReservationAsync(requestId, cancellationToken).ConfigureAwait(false);
            return new(result.Success, result.Status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return new(false, "Party reservation cancellation could not be confirmed; its server lease will expire."); }
    }
}
#endif
