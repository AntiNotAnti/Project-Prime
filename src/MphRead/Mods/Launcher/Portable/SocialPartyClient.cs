#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// Account-level social party and recent-player read model.
    ///
    /// Party membership coordinates people; it never replaces the dedicated
    /// lobby. Any actual game join still goes through SocialInviteClient's
    /// authenticated locator + public-directory verification.
    /// </summary>
    public static class SocialPartyClient
    {
        private static readonly object Sync = new();
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan TravelRefreshInterval = TimeSpan.FromSeconds(30);
        private static CancellationTokenSource? _lifetime;
        private static Task? _loop;
        private static int _generation;
        private static int _forceRefresh;
        private static SocialPartySnapshot _current = new();
        private static string _lastFailure = "";
        private static string _nextTravelReason = "leader_lobby";

        internal static event Action<SocialPartySnapshot>? Changed;

        internal static SocialPartySnapshot Current
        {
            get { lock (Sync) return _current; }
        }

        internal static bool Running
        {
            get { lock (Sync) return _lifetime != null; }
        }

        public static void Start()
        {
            lock (Sync)
            {
                if (_lifetime != null) return;
                var lifetime = new CancellationTokenSource();
                int generation = ++_generation;
                _lifetime = lifetime;
                _loop = Task.Run(() => RunAsync(generation, lifetime));
            }
        }

        public static void Stop()
        {
            CancellationTokenSource? lifetime;
            lock (Sync)
            {
                lifetime = _lifetime;
                if (lifetime == null) return;
                _lifetime = null;
                _loop = null;
                ++_generation;
            }
            lifetime.Cancel();
        }

        public static void Suspend() => Stop();
        public static void Resume() => Start();

        internal static void RefreshNow()
            => Interlocked.Exchange(ref _forceRefresh, 1);

        internal static async Task<SocialPartySnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            SocialPartyEnvelope envelope = await InvokeAsync(
                Body("snapshot"), cancellationToken).ConfigureAwait(false);
            SocialPartySnapshot snapshot = envelope.Snapshot ?? new();
            PublishSnapshot(snapshot);
            return snapshot;
        }

        internal static Task<SocialPartyMutationResult> InviteAsync(
            string primeId, CancellationToken cancellationToken = default)
            => TargetMutationAsync("invite", primeId, cancellationToken);

        internal static Task<SocialPartyMutationResult> KickAsync(
            string primeId, CancellationToken cancellationToken = default)
            => TargetMutationAsync("kick", primeId, cancellationToken);

        internal static Task<SocialPartyMutationResult> PromoteAsync(
            string primeId, CancellationToken cancellationToken = default)
            => TargetMutationAsync("promote", primeId, cancellationToken);

        internal static Task<SocialPartyMutationResult> AcceptInviteAsync(
            string inviteId, CancellationToken cancellationToken = default)
            => InviteMutationAsync("accept", inviteId, cancellationToken);

        internal static Task<SocialPartyMutationResult> DeclineInviteAsync(
            string inviteId, CancellationToken cancellationToken = default)
            => InviteMutationAsync("decline", inviteId, cancellationToken);

        internal static Task<SocialPartyMutationResult> CancelInviteAsync(
            string inviteId, CancellationToken cancellationToken = default)
            => InviteMutationAsync("cancel", inviteId, cancellationToken);

        internal static Task<SocialPartyMutationResult> LeaveAsync(
            CancellationToken cancellationToken = default)
            => MutateAsync(Body("leave"), cancellationToken);

        internal static Task<SocialPartyMutationResult> DisbandAsync(
            CancellationToken cancellationToken = default)
            => MutateAsync(Body("disband"), cancellationToken);

        internal static async Task<PartyGameInviteResult> InvitePartyToLobbyAsync(
            CancellationToken cancellationToken = default)
        {
            SocialParty? party = Current.Party;
            if (party == null)
                return new(false, 0, 0, "not_in_party");
            if (!party.IsLeader)
                return new(false, 0, 0, "leader_only");
            if (SocialInviteClient.CurrentLobby == null)
                return new(false, 0, 0, "lobby_unavailable");

            int sent = 0, refused = 0;
            string last = "";
            foreach (SocialPartyMember member in party.Members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (member.IsSelf) continue;
                SocialInviteMutationResult result =
                    await SocialInviteClient.SendInviteAsync(
                        member.PrimeId, cancellationToken).ConfigureAwait(false);
                if (result.Success) sent++;
                else { refused++; last = result.Status; }
            }

            return new(sent > 0 && refused == 0, sent, refused,
                refused == 0 ? "party_invites_sent"
                : sent > 0 ? "party_invites_partial"
                : last.Length > 0 ? last : "party_invites_refused");
        }

        internal static async Task<SocialJoinResolution> PrepareLeaderJoinAsync(
            CancellationToken cancellationToken = default)
        {
            SocialPartyTravel? travel = Current.Travel;
            if (travel != null && !travel.IsLeader)
                return await PrepareTravelJoinAsync(cancellationToken).ConfigureAwait(false);

            SocialParty? party = Current.Party;
            if (party == null)
                return SocialJoinResolution.Fail("not_in_party");
            if (party.IsLeader)
                return SocialJoinResolution.Fail("already_party_leader");

            string leader = party.LeaderPrimeId;
            if (leader.Length == 0)
                return SocialJoinResolution.Fail("party_leader_unavailable");
            return await SocialInviteClient.PrepareFriendJoinAsync(
                leader, cancellationToken).ConfigureAwait(false);
        }

        internal static void NoteQuickPlayTravel()
        {
            lock (Sync) _nextTravelReason = "quick_play";
            Interlocked.Exchange(ref _forceRefresh, 1);
        }

        internal static async Task<SocialJoinResolution> PrepareTravelJoinAsync(
            CancellationToken cancellationToken = default)
        {
            SocialPartyTravel? travel = Current.Travel;
            if (travel == null)
                return SocialJoinResolution.Fail("party_travel_unavailable");
            if (travel.IsLeader)
                return SocialJoinResolution.Fail("leader_already_there");

            var body = Body("follow_travel");
            body["travel_id"] = travel.TravelId;
            body["revision"] = travel.Revision;
            SocialPartyEnvelope envelope =
                await InvokeAsync(body, cancellationToken).ConfigureAwait(false);
            if (envelope.Snapshot != null)
                PublishSnapshot(envelope.Snapshot);
            if (!envelope.Ok || envelope.Locator == null)
                return SocialJoinResolution.Fail(envelope.Status);

            SocialLobbyLocator locator = envelope.Locator;
            if (!locator.TryAuthorityEpoch(out ulong epoch))
                return SocialJoinResolution.Fail("invalid_authority_epoch");

            SocialLobbyVerification verified =
                await ServerBrowserService.VerifySocialLobbyAsync(
                    locator.Host, locator.Port, epoch, locator.Protocol,
                    locator.RoomKey, cancellationToken).ConfigureAwait(false);
            if (!verified.Verified)
                return SocialJoinResolution.Fail(verified.Error);

            ServerBrowserEntry entry = verified.Entry;
            return new SocialJoinResolution(
                true,
                entry.Listing.Address,
                entry.Listing.Port,
                entry.Name,
                entry.Status.RoomKey,
                "");
        }

        internal static Task<SocialPartyMutationResult> DeclineTravelAsync(
            CancellationToken cancellationToken = default)
        {
            SocialPartyTravel? travel = Current.Travel;
            if (travel == null)
                return Task.FromResult(new SocialPartyMutationResult(
                    false, "party_travel_unavailable", Current));
            var body = Body("decline_travel");
            body["travel_id"] = travel.TravelId;
            body["revision"] = travel.Revision;
            return MutateAsync(body, cancellationToken);
        }

        private static Task<SocialPartyMutationResult> TargetMutationAsync(
            string action, string primeId, CancellationToken cancellationToken)
        {
            var body = Body(action);
            body["target_prime_id"] = primeId.Trim().ToUpperInvariant();
            return MutateAsync(body, cancellationToken);
        }

        private static Task<SocialPartyMutationResult> InviteMutationAsync(
            string action, string inviteId, CancellationToken cancellationToken)
        {
            var body = Body(action);
            body["invite_id"] = inviteId;
            return MutateAsync(body, cancellationToken);
        }

        private static async Task<SocialPartyMutationResult> MutateAsync(
            Dictionary<string, object?> body, CancellationToken cancellationToken)
        {
            SocialPartyEnvelope envelope =
                await InvokeAsync(body, cancellationToken).ConfigureAwait(false);
            if (envelope.Snapshot != null)
                PublishSnapshot(envelope.Snapshot);
            Interlocked.Exchange(ref _forceRefresh, 1);
            return new SocialPartyMutationResult(
                envelope.Ok, envelope.Status, envelope.Snapshot);
        }

        private static async Task RunAsync(
            int generation, CancellationTokenSource lifetime)
        {
            CancellationToken token = lifetime.Token;
            DateTimeOffset nextPoll = DateTimeOffset.MinValue;
            DateTimeOffset nextTravelRefresh = DateTimeOffset.MinValue;
            bool sawPartyMatch = false;
            string publishedLobby = "";

            try
            {
                while (!token.IsCancellationRequested)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    SocialPartySnapshot current = Current;
                    SocialParty? party = current.Party;

                    if (party is { IsLeader: true }
                        && NetSession.Active
                        && (NetSession.IsPlaying || NetSession.IsPostMatch))
                    {
                        sawPartyMatch = true;
                    }

                    SocialLobbyLocator? lobby = SocialInviteClient.CurrentLobby;
                    bool leaderInVerifiedLobby = party is { IsLeader: true }
                        && party.Members.Count > 1
                        && lobby != null
                        && lobby.ExpiresAt > now
                        && NetSession.Active
                        && NetSession.PersistentLobby
                        && NetSession.IsInLobby;

                    string requestedReason;
                    lock (Sync) requestedReason = _nextTravelReason;
                    bool explicitTravelReason =
                        !requestedReason.Equals(
                            "leader_lobby", StringComparison.Ordinal);

                    if (leaderInVerifiedLobby
                        && (lobby!.LobbyId != publishedLobby
                            || now >= nextTravelRefresh
                            || current.Travel == null
                            || explicitTravelReason))
                    {
                        try
                        {
                            string reason = requestedReason;
                            if (reason == "leader_lobby" && sawPartyMatch)
                                reason = "regroup";

                            var body = Body("publish_travel");
                            body["lobby_id"] = lobby.LobbyId;
                            body["reason"] = reason;
                            SocialPartyEnvelope envelope =
                                await InvokeAsync(body, token).ConfigureAwait(false);
                            if (envelope.Snapshot != null)
                                PublishSnapshot(generation, envelope.Snapshot);
                            if (envelope.Ok)
                            {
                                publishedLobby = lobby.LobbyId;
                                nextTravelRefresh = DateTimeOffset.UtcNow
                                    + TravelRefreshInterval;
                                sawPartyMatch = false;
                                lock (Sync) _nextTravelReason = "leader_lobby";
                            }
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            NoteFailure(ex);
                            nextTravelRefresh = DateTimeOffset.UtcNow.AddSeconds(10);
                        }
                    }
                    else if (party is { IsLeader: true }
                        && current.Travel != null
                        && (!NetSession.Active
                            || !NetSession.PersistentLobby
                            || !NetSession.IsInLobby))
                    {
                        try
                        {
                            SocialPartyEnvelope envelope = await InvokeAsync(
                                Body("clear_travel"), token).ConfigureAwait(false);
                            if (envelope.Snapshot != null)
                                PublishSnapshot(generation, envelope.Snapshot);
                            publishedLobby = "";
                            nextTravelRefresh = DateTimeOffset.MinValue;
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            NoteFailure(ex);
                        }
                    }

                    SocialPartyTravel? travel = Current.Travel;
                    if (travel is { IsLeader: false }
                        && !travel.SelfStatus.Equals(
                            "joined", StringComparison.OrdinalIgnoreCase)
                        && travel.TryAuthorityEpoch(out ulong travelEpoch)
                        && NetSession.Active
                        && NetSession.AuthorityEpoch == travelEpoch)
                    {
                        try
                        {
                            var body = Body("joined_travel");
                            body["travel_id"] = travel.TravelId;
                            body["revision"] = travel.Revision;
                            SocialPartyEnvelope envelope =
                                await InvokeAsync(body, token).ConfigureAwait(false);
                            if (envelope.Snapshot != null)
                                PublishSnapshot(generation, envelope.Snapshot);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            NoteFailure(ex);
                        }
                    }

                    bool force = Interlocked.Exchange(ref _forceRefresh, 0) != 0;
                    if (force || now >= nextPoll)
                    {
                        try
                        {
                            SocialPartyEnvelope envelope = await InvokeAsync(
                                Body("snapshot"), token).ConfigureAwait(false);
                            PublishSnapshot(generation, envelope.Snapshot ?? new());
                            nextPoll = DateTimeOffset.UtcNow + PollInterval;
                            ClearFailure();
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            NoteFailure(ex);
                            nextPoll = DateTimeOffset.UtcNow.AddSeconds(20);
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                lifetime.Dispose();
            }
        }

        private static Dictionary<string, object?> Body(string action)
        {
            string name = LauncherPrefs.PlayerName.Trim();
            if (name.Length == 0) name = "Player";
            Hunter preferred = Hunters.Resolve(LauncherPrefs.LastHunter);
            return new Dictionary<string, object?>
            {
                ["action"] = action,
                ["display_name"] = name,
                ["favorite_hunter"] = Math.Clamp((int)preferred, 0, 6)
            };
        }

        private static async Task<SocialPartyEnvelope> InvokeAsync(
            Dictionary<string, object?> body, CancellationToken cancellationToken)
            => await HunterLicenseClient.InvokeAuthenticatedFunctionAsync<SocialPartyEnvelope>(
                "social-party", body, cancellationToken).ConfigureAwait(false);

        private static void PublishSnapshot(SocialPartySnapshot snapshot)
        {
            Action<SocialPartySnapshot>? changed;
            lock (Sync)
            {
                _current = snapshot;
                changed = Changed;
            }
            changed?.Invoke(snapshot);
        }

        private static void PublishSnapshot(
            int generation, SocialPartySnapshot snapshot)
        {
            Action<SocialPartySnapshot>? changed;
            lock (Sync)
            {
                if (generation != _generation || _lifetime == null) return;
                _current = snapshot;
                changed = Changed;
            }
            changed?.Invoke(snapshot);
        }

        private static void NoteFailure(Exception ex)
        {
            string message = ex.Message.Trim();
            if (message.Length == 0) message = ex.GetType().Name;
            lock (Sync)
            {
                if (String.Equals(_lastFailure, message, StringComparison.Ordinal))
                    return;
                _lastFailure = message;
            }
            Console.WriteLine("[social] party service unavailable: " + message);
        }

        private static void ClearFailure()
        {
            lock (Sync) _lastFailure = "";
        }
    }

    internal sealed class SocialPartyEnvelope
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";
        [JsonPropertyName("snapshot")]
        public SocialPartySnapshot? Snapshot { get; set; }
        [JsonPropertyName("locator")]
        public SocialLobbyLocator? Locator { get; set; }
    }

    internal sealed class SocialPartySnapshot
    {
        [JsonPropertyName("party")]
        public SocialParty? Party { get; set; }
        [JsonPropertyName("incoming_party_invites")]
        public List<SocialPartyInvite> IncomingPartyInvites { get; set; } = new();
        [JsonPropertyName("outgoing_party_invites")]
        public List<SocialPartyInvite> OutgoingPartyInvites { get; set; } = new();
        [JsonPropertyName("recent_players")]
        public List<SocialRecentPlayer> RecentPlayers { get; set; } = new();
        [JsonPropertyName("travel")]
        public SocialPartyTravel? Travel { get; set; }
    }

    internal sealed class SocialParty
    {
        [JsonPropertyName("party_id")]
        public string PartyId { get; set; } = "";
        [JsonPropertyName("leader_prime_id")]
        public string LeaderPrimeId { get; set; } = "";
        [JsonPropertyName("is_leader")]
        public bool IsLeader { get; set; }
        [JsonPropertyName("members")]
        public List<SocialPartyMember> Members { get; set; } = new();
    }

    internal sealed class SocialPartyMember
    {
        [JsonPropertyName("prime_id")]
        public string PrimeId { get; set; } = "";
        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "Player";
        [JsonPropertyName("is_leader")]
        public bool IsLeader { get; set; }
        [JsonPropertyName("is_self")]
        public bool IsSelf { get; set; }
        [JsonPropertyName("joined_at")]
        public DateTimeOffset JoinedAt { get; set; }
    }

    internal sealed class SocialPartyInvite
    {
        [JsonPropertyName("invite_id")]
        public string InviteId { get; set; } = "";
        [JsonPropertyName("party_id")]
        public string PartyId { get; set; } = "";
        [JsonPropertyName("prime_id")]
        public string PrimeId { get; set; } = "";
        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "Player";
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }
    }

    internal sealed class SocialRecentPlayer
    {
        [JsonPropertyName("prime_id")]
        public string PrimeId { get; set; } = "";
        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "Player";
        [JsonPropertyName("last_seen")]
        public DateTimeOffset LastSeen { get; set; }
        [JsonPropertyName("encounters")]
        public int Encounters { get; set; }
    }

    internal sealed class SocialPartyTravel
    {
        [JsonPropertyName("travel_id")]
        public string TravelId { get; set; } = "";
        [JsonPropertyName("revision")]
        public int Revision { get; set; }
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = "leader_lobby";
        [JsonPropertyName("leader_prime_id")]
        public string LeaderPrimeId { get; set; } = "";
        [JsonPropertyName("leader_display_name")]
        public string LeaderDisplayName { get; set; } = "Player";
        [JsonPropertyName("lobby_id")]
        public string LobbyId { get; set; } = "";
        [JsonPropertyName("room_key")]
        public string RoomKey { get; set; } = "";
        [JsonPropertyName("server_name")]
        public string ServerName { get; set; } = "";
        [JsonPropertyName("authority_epoch")]
        public string AuthorityEpoch { get; set; } = "";
        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }
        [JsonPropertyName("is_leader")]
        public bool IsLeader { get; set; }
        [JsonPropertyName("self_status")]
        public string SelfStatus { get; set; } = "pending";
        [JsonPropertyName("members")]
        public List<SocialPartyTravelMember> Members { get; set; } = new();

        public bool TryAuthorityEpoch(out ulong epoch)
            => UInt64.TryParse(AuthorityEpoch,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out epoch) && epoch != 0;
    }

    internal sealed class SocialPartyTravelMember
    {
        [JsonPropertyName("prime_id")]
        public string PrimeId { get; set; } = "";
        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "Player";
        [JsonPropertyName("is_leader")]
        public bool IsLeader { get; set; }
        [JsonPropertyName("is_self")]
        public bool IsSelf { get; set; }
        [JsonPropertyName("status")]
        public string Status { get; set; } = "pending";
    }

    internal readonly record struct SocialPartyMutationResult(
        bool Success,
        string Status,
        SocialPartySnapshot? Snapshot);

    internal readonly record struct PartyGameInviteResult(
        bool Success,
        int Sent,
        int Refused,
        string Status);
}
#endif
