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
    /// Slow control-plane presence for the launcher and live game.
    ///
    /// No gameplay frame waits on this service. A one-second local observer
    /// detects activity transitions; network heartbeats are sent on a state
    /// change or every fifteen seconds. Server reads expire sessions after
    /// forty-five seconds, so crashes and lost networks heal without requiring
    /// a perfect logout packet.
    /// </summary>
    internal static class SocialPresenceClient
    {
        private static readonly object Sync = new();
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan ObserverInterval = TimeSpan.FromSeconds(1);
        private static CancellationTokenSource? _lifetime;
        private static Task? _loop;
        private static Guid _sessionId;
        private static int _generation;
        private static int _privacyDirty;
        private static int _forceRefresh;
        private static SocialPresenceSnapshot _current = new();
        private static string _lastFailure = "";

        /// <summary>
        /// Raised on the presence worker thread. UI consumers must marshal to
        /// their own dispatcher.
        /// </summary>
        public static event Action<SocialPresenceSnapshot>? Changed;

        public static SocialPresenceSnapshot Current
        {
            get { lock (Sync) return _current; }
        }

        public static bool Running
        {
            get { lock (Sync) return _lifetime != null; }
        }

        public static void Start()
        {
            CancellationTokenSource lifetime;
            Guid session;
            int generation;
            lock (Sync)
            {
                if (_lifetime != null) return;
                lifetime = new CancellationTokenSource();
                session = Guid.NewGuid();
                generation = ++_generation;
                _lifetime = lifetime;
                _sessionId = session;
                _loop = Task.Run(() => RunAsync(generation, session, lifetime));
            }
        }

        /// <summary>
        /// Stop heartbeats immediately. Logout delivery is best-effort only;
        /// the server-side TTL remains the authoritative crash/disconnect path.
        /// </summary>
        public static void Stop()
        {
            CancellationTokenSource? lifetime;
            Guid session;
            lock (Sync)
            {
                lifetime = _lifetime;
                if (lifetime == null) return;
                session = _sessionId;
                _lifetime = null;
                _loop = null;
                _sessionId = Guid.Empty;
                ++_generation;
            }
            lifetime.Cancel();
            _ = BestEffortLeaveAsync(session);
        }

        public static void Suspend() => Stop();
        public static void Resume() => Start();

        public static void NotifyPrivacyChanged()
        {
            Interlocked.Exchange(ref _privacyDirty, 1);
            Interlocked.Exchange(ref _forceRefresh, 1);
        }

        public static void RefreshNow()
            => Interlocked.Exchange(ref _forceRefresh, 1);

        private static async Task RunAsync(
            int generation, Guid sessionId, CancellationTokenSource lifetime)
        {
            CancellationToken cancellationToken = lifetime.Token;
            LocalPresence lastSent = default;
            bool sent = false;
            DateTimeOffset nextHeartbeat = DateTimeOffset.MinValue;
            DateTimeOffset nextAttempt = DateTimeOffset.MinValue;
            int retrySeconds = 5;

            try
            {
                await SynchronizePrivacyAsync(generation, cancellationToken).ConfigureAwait(false);

                while (!cancellationToken.IsCancellationRequested)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    LocalPresence local = CaptureLocal();
                    bool privacyDirty = Volatile.Read(ref _privacyDirty) != 0;
                    bool force = Interlocked.Exchange(ref _forceRefresh, 0) != 0;
                    bool heartbeatDue = !sent || local != lastSent || force || now >= nextHeartbeat;

                    if (now >= nextAttempt && (privacyDirty || heartbeatDue))
                    {
                        try
                        {
                            if (privacyDirty)
                            {
                                SocialPrivacySettings desired = LocalPrivacy();
                                PresenceEnvelope privacy = await InvokeAsync(
                                    PrivacyBody(desired), cancellationToken).ConfigureAwait(false);
                                Publish(generation, privacy.Snapshot);
                                if (LocalPrivacy() == desired)
                                    Interlocked.Exchange(ref _privacyDirty, 0);
                            }

                            PresenceEnvelope heartbeat = await InvokeAsync(
                                HeartbeatBody(sessionId, local), cancellationToken).ConfigureAwait(false);
                            Publish(generation, heartbeat.Snapshot);
                            sent = true;
                            lastSent = local;
                            nextHeartbeat = DateTimeOffset.UtcNow + HeartbeatInterval;
                            nextAttempt = DateTimeOffset.MinValue;
                            retrySeconds = 5;
                            ClearFailure();
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            NoteFailure(ex);
                            nextAttempt = DateTimeOffset.UtcNow.AddSeconds(retrySeconds);
                            retrySeconds = Math.Min(60, retrySeconds * 2);
                        }
                    }

                    await Task.Delay(ObserverInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                NoteFailure(ex);
            }
            finally
            {
                lifetime.Dispose();
            }
        }

        private static async Task SynchronizePrivacyAsync(
            int generation, CancellationToken cancellationToken)
        {
            try
            {
                PresenceEnvelope envelope = await InvokeAsync(
                    CommonBody("snapshot"), cancellationToken).ConfigureAwait(false);
                Publish(generation, envelope.Snapshot);
                if (envelope.Snapshot?.Settings is not { } remote) return;

                if (!LauncherPrefs.SocialPrivacyConfigured)
                {
                    LauncherPrefs.PresenceVisibility = ParsePresence(remote.PresenceVisibility);
                    LauncherPrefs.ActivityVisibility = ParseActivity(remote.ActivityVisibility);
                    LauncherPrefs.InvitePolicy = ParseInvite(remote.InvitePolicy);
                }
                else if (LocalPrivacy() != remote)
                {
                    Interlocked.Exchange(ref _privacyDirty, 1);
                }
                ClearFailure();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Failure here is not terminal. The first heartbeat will retry
                // identity/session acquisition through the normal backoff path.
                NoteFailure(ex);
            }
        }

        private static async Task BestEffortLeaveAsync(Guid sessionId)
        {
            if (sessionId == Guid.Empty) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                var body = CommonBody("leave");
                body["session_id"] = sessionId.ToString("D");
                await InvokeAsync(body, timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                // TTL expiry is the reliable fallback for shutdown, crashes,
                // suspend races, and networks disappearing underneath us.
            }
        }

        private static LocalPresence CaptureLocal()
        {
            if (DemoPlayback.IsActive || !NetSession.Active)
                return new("menu", "", false);

            string room = NetSession.ActiveMatchDefinition?.RoomKey
                ?? NetSession.ServerMatch?.RoomKey
                ?? "";

            if (NetSession.PersistentLobby && NetSession.IsInLobby)
            {
                bool joinable = !NetSession.Refused && !NetSession.SessionTimedOut;
                return new("lobby", room, joinable);
            }

            if (SpectatorMode.IsSpectating
                || (NetSession.IsPlaying && SpectatorMode.PreferSpectator))
                return new("spectating", room, false);

            if (NetSession.IsStarting || NetSession.IsPlaying || NetSession.IsPostMatch)
                return new("in_match", room, false);

            return new("online", "", false);
        }

        private static Dictionary<string, object?> CommonBody(string action)
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

        private static Dictionary<string, object?> HeartbeatBody(
            Guid sessionId, LocalPresence local)
        {
            var body = CommonBody("heartbeat");
            body["session_id"] = sessionId.ToString("D");
            body["activity"] = local.Activity;
            body["room_key"] = local.RoomKey.Length == 0 ? null : local.RoomKey;
            body["joinable"] = local.Joinable;
            return body;
        }

        private static Dictionary<string, object?> PrivacyBody(SocialPrivacySettings privacy)
        {
            var body = CommonBody("set_privacy");
            body["presence_visibility"] = privacy.PresenceVisibility;
            body["activity_visibility"] = privacy.ActivityVisibility;
            body["invite_policy"] = privacy.InvitePolicy;
            return body;
        }

        private static async Task<PresenceEnvelope> InvokeAsync(
            Dictionary<string, object?> body, CancellationToken cancellationToken)
            => await HunterLicenseClient.InvokeAuthenticatedFunctionAsync<PresenceEnvelope>(
                "presence", body, cancellationToken).ConfigureAwait(false);

        private static SocialPrivacySettings LocalPrivacy() => new()
        {
            PresenceVisibility = PresenceWire(LauncherPrefs.PresenceVisibility),
            ActivityVisibility = ActivityWire(LauncherPrefs.ActivityVisibility),
            InvitePolicy = InviteWire(LauncherPrefs.InvitePolicy)
        };

        private static void Publish(int generation, SocialPresenceSnapshot? snapshot)
        {
            if (snapshot == null) return;
            Action<SocialPresenceSnapshot>? changed;
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
                if (message == _lastFailure) return;
                _lastFailure = message;
            }
            Console.WriteLine("[social] presence unavailable: " + message);
        }

        private static void ClearFailure()
        {
            lock (Sync) _lastFailure = "";
        }

        private static string PresenceWire(SocialPresenceVisibility value) => value switch
        {
            SocialPresenceVisibility.Friends => "friends",
            SocialPresenceVisibility.Hidden => "hidden",
            _ => "everyone"
        };

        private static string ActivityWire(SocialActivityVisibility value) => value switch
        {
            SocialActivityVisibility.Everyone => "everyone",
            SocialActivityVisibility.Private => "private",
            _ => "friends"
        };

        private static string InviteWire(SocialInvitePolicy value) => value switch
        {
            SocialInvitePolicy.Everyone => "everyone",
            SocialInvitePolicy.Nobody => "nobody",
            _ => "friends"
        };

        private static SocialPresenceVisibility ParsePresence(string value)
            => value.Equals("friends", StringComparison.OrdinalIgnoreCase)
                ? SocialPresenceVisibility.Friends
                : value.Equals("hidden", StringComparison.OrdinalIgnoreCase)
                    ? SocialPresenceVisibility.Hidden
                    : SocialPresenceVisibility.Everyone;

        private static SocialActivityVisibility ParseActivity(string value)
            => value.Equals("everyone", StringComparison.OrdinalIgnoreCase)
                ? SocialActivityVisibility.Everyone
                : value.Equals("private", StringComparison.OrdinalIgnoreCase)
                    ? SocialActivityVisibility.Private
                    : SocialActivityVisibility.Friends;

        private static SocialInvitePolicy ParseInvite(string value)
            => value.Equals("everyone", StringComparison.OrdinalIgnoreCase)
                ? SocialInvitePolicy.Everyone
                : value.Equals("nobody", StringComparison.OrdinalIgnoreCase)
                    ? SocialInvitePolicy.Nobody
                    : SocialInvitePolicy.Friends;

        private readonly record struct LocalPresence(string Activity, string RoomKey, bool Joinable);
    }

    internal sealed class PresenceEnvelope
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";
        [JsonPropertyName("snapshot")]
        public SocialPresenceSnapshot? Snapshot { get; set; }
    }

    internal sealed class SocialPresenceSnapshot
    {
        [JsonPropertyName("settings")]
        public SocialPrivacySettings Settings { get; set; } = new();
        [JsonPropertyName("players")]
        public List<SocialOnlinePlayer> Players { get; set; } = new();
        [JsonPropertyName("expires_after_seconds")]
        public int ExpiresAfterSeconds { get; set; } = 45;
    }

    internal sealed class SocialPrivacySettings : IEquatable<SocialPrivacySettings>
    {
        [JsonPropertyName("presence_visibility")]
        public string PresenceVisibility { get; set; } = "everyone";
        [JsonPropertyName("activity_visibility")]
        public string ActivityVisibility { get; set; } = "friends";
        [JsonPropertyName("invite_policy")]
        public string InvitePolicy { get; set; } = "friends";
        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }

        public bool Equals(SocialPrivacySettings? other)
            => other != null
                && String.Equals(PresenceVisibility, other.PresenceVisibility,
                    StringComparison.OrdinalIgnoreCase)
                && String.Equals(ActivityVisibility, other.ActivityVisibility,
                    StringComparison.OrdinalIgnoreCase)
                && String.Equals(InvitePolicy, other.InvitePolicy,
                    StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => Equals(obj as SocialPrivacySettings);
        public override int GetHashCode() => HashCode.Combine(
            PresenceVisibility.ToLowerInvariant(),
            ActivityVisibility.ToLowerInvariant(),
            InvitePolicy.ToLowerInvariant());

        public static bool operator ==(SocialPrivacySettings? left, SocialPrivacySettings? right)
            => Equals(left, right);
        public static bool operator !=(SocialPrivacySettings? left, SocialPrivacySettings? right)
            => !Equals(left, right);
    }

    internal sealed class SocialOnlinePlayer
    {
        [JsonPropertyName("prime_id")]
        public string PrimeId { get; set; } = "";
        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "Player";
        [JsonPropertyName("activity")]
        public string Activity { get; set; } = "online";
        [JsonPropertyName("room_key")]
        public string? RoomKey { get; set; }
        [JsonPropertyName("joinable")]
        public bool Joinable { get; set; }
        [JsonPropertyName("is_friend")]
        public bool IsFriend { get; set; }
        [JsonPropertyName("last_seen")]
        public DateTimeOffset? LastSeen { get; set; }
    }
}
#endif
