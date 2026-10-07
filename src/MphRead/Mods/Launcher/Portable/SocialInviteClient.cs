#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// Durable social lobby/invite client. Supabase stores authenticated intent;
    /// Project Prime's public directory and live status endpoint remain the
    /// authority for whether a resolved UDP target may actually be joined.
    /// </summary>
    public static class SocialInviteClient
    {
        private static readonly object Sync = new();
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan LobbyRefreshInterval = TimeSpan.FromSeconds(30);
        private static CancellationTokenSource? _lifetime;
        private static Task? _loop;
        private static int _generation;
        private static int _forceRefresh;
        private static SocialInviteSnapshot _current = new();
        private static SocialLobbyLocator? _currentLobby;
        private static string _lastFailure = "";

        internal static event Action<SocialInviteSnapshot>? Changed;

        internal static SocialInviteSnapshot Current
        {
            get { lock (Sync) return _current; }
        }

        internal static SocialLobbyLocator? CurrentLobby
        {
            get { lock (Sync) return _currentLobby; }
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
            bool hadLobby;
            lock (Sync)
            {
                lifetime = _lifetime;
                if (lifetime == null) return;
                _lifetime = null;
                _loop = null;
                ++_generation;
                hadLobby = _currentLobby != null;
                _currentLobby = null;
            }
            lifetime.Cancel();
            if (hadLobby) SocialPresenceClient.RefreshNow();
        }

        public static void Suspend() => Stop();
        public static void Resume() => Start();

        internal static void RefreshNow()
            => Interlocked.Exchange(ref _forceRefresh, 1);

        internal static async Task<SocialInviteSnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            SocialInviteEnvelope envelope = await InvokeAsync(
                Body("snapshot"), cancellationToken).ConfigureAwait(false);
            SocialInviteSnapshot snapshot = envelope.Snapshot ?? new();
            PublishSnapshot(snapshot);
            return snapshot;
        }

        internal static async Task<SocialInviteMutationResult> SendInviteAsync(
            string primeId, CancellationToken cancellationToken = default)
        {
            SocialLobbyLocator? lobby = CurrentLobby;
            if (lobby == null || lobby.ExpiresAt <= DateTimeOffset.UtcNow)
                return SocialInviteMutationResult.Fail("lobby_unavailable");

            var body = Body("send_invite");
            body["target_prime_id"] = primeId.Trim().ToUpperInvariant();
            body["lobby_id"] = lobby.LobbyId;
            return await MutateAsync(body, cancellationToken).ConfigureAwait(false);
        }

        internal static Task<SocialInviteMutationResult> AcceptInviteAsync(
            string inviteId, CancellationToken cancellationToken = default)
            => InviteMutationAsync("accept_invite", inviteId, cancellationToken);

        internal static Task<SocialInviteMutationResult> DeclineInviteAsync(
            string inviteId, CancellationToken cancellationToken = default)
            => InviteMutationAsync("decline_invite", inviteId, cancellationToken);

        internal static Task<SocialInviteMutationResult> CancelInviteAsync(
            string inviteId, CancellationToken cancellationToken = default)
            => InviteMutationAsync("cancel_invite", inviteId, cancellationToken);

        internal static async Task<SocialJoinResolution> PrepareInviteJoinAsync(
            string inviteId, bool accept, CancellationToken cancellationToken = default)
        {
            SocialLobbyLocator? locator;
            if (accept)
            {
                SocialInviteMutationResult accepted =
                    await AcceptInviteAsync(inviteId, cancellationToken).ConfigureAwait(false);
                if (!accepted.Success || accepted.Locator == null)
                    return SocialJoinResolution.Fail(accepted.Status);
                locator = accepted.Locator;
            }
            else
            {
                var body = Body("resolve_invite");
                body["invite_id"] = inviteId;
                SocialInviteEnvelope envelope =
                    await InvokeAsync(body, cancellationToken).ConfigureAwait(false);
                if (!envelope.Ok || envelope.Locator == null)
                    return SocialJoinResolution.Fail(envelope.Status);
                locator = envelope.Locator;
            }
            return await VerifyAsync(locator, cancellationToken).ConfigureAwait(false);
        }

        internal static async Task<SocialJoinResolution> PrepareFriendJoinAsync(
            string primeId, CancellationToken cancellationToken = default)
        {
            var body = Body("join_friend");
            body["target_prime_id"] = primeId.Trim().ToUpperInvariant();
            SocialInviteEnvelope envelope =
                await InvokeAsync(body, cancellationToken).ConfigureAwait(false);
            if (!envelope.Ok || envelope.Locator == null)
                return SocialJoinResolution.Fail(envelope.Status);
            return await VerifyAsync(envelope.Locator, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<SocialJoinResolution> VerifyAsync(
            SocialLobbyLocator locator, CancellationToken cancellationToken)
        {
            if (!locator.TryAuthorityEpoch(out ulong epoch)
                || locator.Protocol != NetConfig.ProtocolVersion
                || locator.ExpiresAt <= DateTimeOffset.UtcNow)
                return SocialJoinResolution.Fail("invite_expired");

            SocialLobbyVerification verified =
                await ServerBrowserService.VerifySocialLobbyAsync(
                    locator.Host, locator.Port, epoch, locator.Protocol,
                    locator.RoomKey, cancellationToken).ConfigureAwait(false);
            if (!verified.Verified)
                return SocialJoinResolution.Fail(verified.Error);

            return new SocialJoinResolution(
                true,
                verified.Entry.Listing.Address,
                verified.Entry.Listing.Port,
                verified.Entry.Name,
                verified.Entry.Status.RoomKey,
                "", AuthorityEpoch: epoch);
        }

        private static async Task<SocialInviteMutationResult> InviteMutationAsync(
            string action, string inviteId, CancellationToken cancellationToken)
        {
            var body = Body(action);
            body["invite_id"] = inviteId;
            return await MutateAsync(body, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<SocialInviteMutationResult> MutateAsync(
            Dictionary<string, object?> body, CancellationToken cancellationToken)
        {
            SocialInviteEnvelope envelope =
                await InvokeAsync(body, cancellationToken).ConfigureAwait(false);
            if (envelope.Snapshot != null)
                PublishSnapshot(envelope.Snapshot);
            Interlocked.Exchange(ref _forceRefresh, 1);
            return new SocialInviteMutationResult(
                envelope.Ok, envelope.Status, envelope.Snapshot,
                envelope.Locator, envelope.InviteId);
        }

        private static async Task RunAsync(
            int generation, CancellationTokenSource lifetime)
        {
            CancellationToken token = lifetime.Token;
            Task realtime = SocialRealtimeClient.RunAsync(
                () =>
                {
                    Interlocked.Exchange(ref _forceRefresh, 1);
                    SocialPartyClient.RefreshNow();
                }, token);
            DateTimeOffset nextPoll = DateTimeOffset.MinValue;
            DateTimeOffset nextLobbyRefresh = DateTimeOffset.MinValue;
            ulong registeredEpoch = 0;
            int retrySeconds = 5;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    ulong localEpoch = NetSession.Active
                        && NetSession.PersistentLobby && NetSession.IsInLobby
                        ? NetSession.AuthorityEpoch : 0;

                    if (localEpoch == 0)
                    {
                        if (registeredEpoch != 0 || CurrentLobby != null)
                        {
                            registeredEpoch = 0;
                            ClearLobby(generation);
                        }
                        nextLobbyRefresh = DateTimeOffset.MinValue;
                    }
                    else if (localEpoch != registeredEpoch || now >= nextLobbyRefresh)
                    {
                        try
                        {
                            await RefreshLobbyAsync(generation, localEpoch, token)
                                .ConfigureAwait(false);
                            SocialLobbyLocator? live = CurrentLobby;
                            registeredEpoch = live != null
                                && live.TryAuthorityEpoch(out ulong epoch) ? epoch : 0;
                            nextLobbyRefresh = DateTimeOffset.UtcNow + LobbyRefreshInterval;
                            retrySeconds = 5;
                            ClearFailure();
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            NoteFailure(ex);
                            ClearLobby(generation);
                            nextLobbyRefresh = DateTimeOffset.UtcNow.AddSeconds(retrySeconds);
                            retrySeconds = Math.Min(60, retrySeconds * 2);
                        }
                    }

                    bool force = Interlocked.Exchange(ref _forceRefresh, 0) != 0;
                    if (force || now >= nextPoll)
                    {
                        try
                        {
                            SocialInviteEnvelope envelope = await InvokeAsync(
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
                            nextPoll = DateTimeOffset.UtcNow.AddSeconds(15);
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                try { await realtime.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception) { }
                lifetime.Dispose();
            }
        }

        private static async Task RefreshLobbyAsync(
            int generation, ulong authorityEpoch, CancellationToken cancellationToken)
        {
            SocialLobbyVerification listed =
                await ServerBrowserService.FindListedLobbyAsync(
                    authorityEpoch, cancellationToken).ConfigureAwait(false);
            if (!listed.Verified)
                throw new InvalidOperationException(listed.Error);

            ServerBrowserEntry entry = listed.Entry;
            var body = Body("register_lobby");
            body["host"] = entry.Listing.Address;
            body["port"] = entry.Listing.Port;
            body["authority_epoch"] = authorityEpoch.ToString(CultureInfo.InvariantCulture);
            body["protocol"] = NetConfig.ProtocolVersion;
            body["room_key"] = entry.Status.RoomKey;
            body["server_name"] = entry.Name;

            SocialInviteEnvelope envelope =
                await InvokeAsync(body, cancellationToken).ConfigureAwait(false);
            if (!envelope.Ok || envelope.Lobby == null)
                throw new InvalidOperationException(
                    "Lobby registration failed: " + envelope.Status);

            SocialLobbyLocator lobby = envelope.Lobby;
            if (!lobby.TryAuthorityEpoch(out ulong returned)
                || returned != authorityEpoch)
                throw new InvalidOperationException(
                    "Social lobby service returned a different authority identity.");
            PublishLobby(generation, lobby);
        }

        private static void PublishLobby(int generation, SocialLobbyLocator lobby)
        {
            bool changed;
            lock (Sync)
            {
                if (generation != _generation || _lifetime == null) return;
                changed = _currentLobby?.LobbyId != lobby.LobbyId;
                _currentLobby = lobby;
            }
            if (changed) SocialPresenceClient.RefreshNow();
        }

        private static void ClearLobby(int generation)
        {
            bool changed;
            lock (Sync)
            {
                if (generation != _generation && _lifetime != null) return;
                changed = _currentLobby != null;
                _currentLobby = null;
            }
            if (changed) SocialPresenceClient.RefreshNow();
        }

        private static void PublishSnapshot(SocialInviteSnapshot snapshot)
        {
            Action<SocialInviteSnapshot>? changed;
            lock (Sync)
            {
                _current = snapshot;
                changed = Changed;
            }
            changed?.Invoke(snapshot);
        }

        private static void PublishSnapshot(
            int generation, SocialInviteSnapshot snapshot)
        {
            Action<SocialInviteSnapshot>? changed;
            lock (Sync)
            {
                if (generation != _generation || _lifetime == null) return;
                _current = snapshot;
                changed = Changed;
            }
            changed?.Invoke(snapshot);
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

        private static async Task<SocialInviteEnvelope> InvokeAsync(
            Dictionary<string, object?> body, CancellationToken cancellationToken)
            => await HunterLicenseClient.InvokeAuthenticatedFunctionAsync<SocialInviteEnvelope>(
                "social-invites", body, cancellationToken).ConfigureAwait(false);

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
            Console.WriteLine("[social] invite service unavailable: " + message);
        }

        private static void ClearFailure()
        {
            lock (Sync) _lastFailure = "";
        }
    }

    internal sealed class SocialInviteSnapshot
    {
        [JsonPropertyName("incoming")]
        public List<SocialGameInvite> Incoming { get; set; } = new();
        [JsonPropertyName("outgoing")]
        public List<SocialGameInvite> Outgoing { get; set; } = new();
    }

    internal sealed class SocialGameInvite
    {
        [JsonPropertyName("invite_id")]
        public string InviteId { get; set; } = "";
        [JsonPropertyName("prime_id")]
        public string PrimeId { get; set; } = "";
        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "Player";
        [JsonPropertyName("lobby_id")]
        public string LobbyId { get; set; } = "";
        [JsonPropertyName("room_key")]
        public string RoomKey { get; set; } = "";
        [JsonPropertyName("server_name")]
        public string ServerName { get; set; } = "";
        [JsonPropertyName("status")]
        public string Status { get; set; } = "pending";
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }
    }

    internal sealed class SocialLobbyLocator
    {
        [JsonPropertyName("lobby_id")]
        public string LobbyId { get; set; } = "";
        [JsonPropertyName("host")]
        public string Host { get; set; } = "";
        [JsonPropertyName("port")]
        public int Port { get; set; }
        [JsonPropertyName("authority_epoch")]
        public string AuthorityEpoch { get; set; } = "";
        [JsonPropertyName("protocol")]
        public int Protocol { get; set; }
        [JsonPropertyName("room_key")]
        public string RoomKey { get; set; } = "";
        [JsonPropertyName("server_name")]
        public string ServerName { get; set; } = "";
        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }

        public bool TryAuthorityEpoch(out ulong epoch)
            => UInt64.TryParse(AuthorityEpoch, NumberStyles.None,
                CultureInfo.InvariantCulture, out epoch) && epoch != 0;
    }

    internal sealed class SocialInviteEnvelope
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";
        [JsonPropertyName("snapshot")]
        public SocialInviteSnapshot? Snapshot { get; set; }
        [JsonPropertyName("lobby")]
        public SocialLobbyLocator? Lobby { get; set; }
        [JsonPropertyName("locator")]
        public SocialLobbyLocator? Locator { get; set; }
        [JsonPropertyName("invite_id")]
        public string InviteId { get; set; } = "";
    }

    internal readonly record struct SocialInviteMutationResult(
        bool Success,
        string Status,
        SocialInviteSnapshot? Snapshot,
        SocialLobbyLocator? Locator,
        string InviteId)
    {
        public static SocialInviteMutationResult Fail(string status)
            => new(false, status, null, null, "");
    }

    internal readonly record struct SocialJoinResolution(
        bool Success,
        string Host,
        int Port,
        string ServerName,
        string RoomKey,
        string Error,
        PartyReservedAdmission? PartyAdmission = null,
        ulong AuthorityEpoch = 0)
    {
        public static SocialJoinResolution Fail(string error)
            => new(false, "", 0, "", "", error, null);
    }
}
#endif
