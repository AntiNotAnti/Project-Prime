#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// Authenticated social read/mutation client. Identity and session storage
    /// remain owned by HunterLicenseClient; this layer never receives or
    /// persists the Supabase access token.
    /// </summary>
    internal static partial class SocialClient
    {
        private static SocialSnapshot? _current;
        internal static SocialSnapshot? Current
        {
            get
            {
                SocialSnapshot? current = Volatile.Read(ref _current);
                return current != null && HunterLicenseClient.IsNativeAccountLeaseCurrent(current.AccountWitness)
                    ? current : null;
            }
        }
        public static async Task<SocialSnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                SocialEnvelope envelope = await InvokeAsync(
                    "snapshot", targetPrimeId: null, cancellationToken).ConfigureAwait(false);
                if (!envelope.Ok)
                    throw new InvalidOperationException("Social service did not authorize this directory read.");
                SocialSnapshot snapshot = envelope.Snapshot
                    ?? throw new InvalidOperationException("Social service returned no snapshot.");
                Volatile.Write(ref _current, snapshot);
                return snapshot;
            }
            catch
            {
                // Never render a different account's cached friends after
                // credential refresh or loss of the verified connection.
                Volatile.Write(ref _current, null);
                throw;
            }
        }

        public static async Task<SocialLookupResult> LookupAsync(
            string primeId, CancellationToken cancellationToken = default)
        {
            SocialEnvelope envelope = await InvokeAsync(
                "lookup", primeId, cancellationToken).ConfigureAwait(false);
            return new SocialLookupResult(
                envelope.Ok && envelope.Status.Equals("found", StringComparison.Ordinal),
                envelope.Player);
        }

        public static Task<SocialMutationResult> SendFriendRequestAsync(
            string primeId, CancellationToken cancellationToken = default)
            => MutateAsync("send_request", primeId, cancellationToken);

        public static Task<SocialMutationResult> AcceptFriendRequestAsync(
            string primeId, CancellationToken cancellationToken = default)
            => MutateAsync("accept_request", primeId, cancellationToken);

        public static Task<SocialMutationResult> DeclineFriendRequestAsync(
            string primeId, CancellationToken cancellationToken = default)
            => MutateAsync("decline_request", primeId, cancellationToken);

        public static Task<SocialMutationResult> CancelFriendRequestAsync(
            string primeId, CancellationToken cancellationToken = default)
            => MutateAsync("cancel_request", primeId, cancellationToken);

        public static Task<SocialMutationResult> RemoveFriendAsync(
            string primeId, CancellationToken cancellationToken = default)
            => MutateAsync("remove_friend", primeId, cancellationToken);

        public static Task<SocialMutationResult> BlockPlayerAsync(
            string primeId, CancellationToken cancellationToken = default)
            => MutateAsync("block_player", primeId, cancellationToken);

        public static Task<SocialMutationResult> UnblockPlayerAsync(
            string primeId, CancellationToken cancellationToken = default)
            => MutateAsync("unblock_player", primeId, cancellationToken);

        internal static Task<SocialMutationResult> MutateExpectedAsync(
            NativeSocialFriendAction action, string primeId, string expectedSelf,
            CancellationToken cancellationToken)
        {
            if (!NativeSocialFriendsController.ValidPrimeId(expectedSelf)
                || expectedSelf != expectedSelf.ToUpperInvariant())
                throw new ArgumentException("A verified canonical account ID is required.", nameof(expectedSelf));
            string wireAction = action switch
            {
                NativeSocialFriendAction.SendRequest => "send_request",
                NativeSocialFriendAction.AcceptRequest => "accept_request",
                NativeSocialFriendAction.DeclineRequest => "decline_request",
                NativeSocialFriendAction.CancelRequest => "cancel_request",
                NativeSocialFriendAction.RemoveFriend => "remove_friend",
                NativeSocialFriendAction.BlockPlayer => "block_player",
                NativeSocialFriendAction.UnblockPlayer => "unblock_player",
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
            return MutateAsync(wireAction, primeId, cancellationToken, expectedSelf);
        }

        private static async Task<SocialMutationResult> MutateAsync(
            string action, string primeId, CancellationToken cancellationToken,
            string? expectedSelf = null, object? expectedLease = null)
        {
            SocialEnvelope envelope = await InvokeAsync(
                action, primeId, cancellationToken, expectedSelf, expectedLease).ConfigureAwait(false);
            if (expectedSelf != null && envelope.Snapshot != null
                && !String.Equals(envelope.Snapshot.Self.PrimeId, expectedSelf, StringComparison.Ordinal))
            {
                Volatile.Write(ref _current, null);
                throw new InvalidOperationException("Social account changed.");
            }
            // Presence/party clients are not part of this first friends slice.
            // The native page refreshes its authoritative read model after
            // each accepted mutation rather than manufacturing an activity state.
            if (envelope.Snapshot != null) Volatile.Write(ref _current, envelope.Snapshot);
            return new SocialMutationResult
            {
                Success = envelope.Ok,
                Status = envelope.Status,
                Snapshot = envelope.Snapshot
            };
        }

        private static async Task<SocialEnvelope> InvokeAsync(
            string action, string? targetPrimeId, CancellationToken cancellationToken,
            string? expectedSelf = null, object? expectedLease = null)
        {
            string name = LauncherPrefs.PlayerName.Trim();
            if (name.Length == 0) name = "Player";
            Hunter preferred = Hunters.Resolve(LauncherPrefs.LastHunter);
            int hunter = Math.Clamp((int)preferred, 0, 6);

            var body = new Dictionary<string, object?>
            {
                ["action"] = action,
                ["display_name"] = name,
                ["favorite_hunter"] = hunter
            };
            if (!String.IsNullOrWhiteSpace(targetPrimeId))
                body["target_prime_id"] = targetPrimeId.Trim().ToUpperInvariant();
            if (expectedSelf != null)
                body["expected_self_prime_id"] = expectedSelf;

            var result = await HunterLicenseClient.InvokeNativeFunctionWithLeaseAsync<SocialEnvelope>(
                "social", body, cancellationToken, expectedLease).ConfigureAwait(false);
            if (!HunterLicenseClient.IsNativeAccountLeaseCurrent(result.Lease))
                throw new InvalidOperationException("Social account changed.");
            if (result.Value.Snapshot is { } snapshot) snapshot.AccountWitness = result.Lease;
            return result.Value;
        }
    }

    internal sealed class SocialSnapshot
    {
        [JsonIgnore]
        internal object? AccountWitness { get; set; }
        [JsonPropertyName("self")]
        public SocialPlayer Self { get; set; } = new();
        [JsonPropertyName("friends")]
        public List<SocialPlayer> Friends { get; set; } = new();
        [JsonPropertyName("incoming_requests")]
        public List<SocialPlayer> IncomingRequests { get; set; } = new();
        [JsonPropertyName("outgoing_requests")]
        public List<SocialPlayer> OutgoingRequests { get; set; } = new();
        [JsonPropertyName("blocked")]
        public List<SocialPlayer> Blocked { get; set; } = new();
    }

    internal sealed class SocialPlayer
    {
        [JsonPropertyName("prime_id")]
        public string PrimeId { get; set; } = "";
        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "Player";
        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }
    }

    internal readonly record struct SocialLookupResult(bool Found, SocialPlayer? Player);

    internal sealed class SocialMutationResult
    {
        public bool Success { get; set; }
        public string Status { get; set; } = "";
        public SocialSnapshot? Snapshot { get; set; }
    }

    internal sealed class SocialEnvelope
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";
        [JsonPropertyName("snapshot")]
        public SocialSnapshot? Snapshot { get; set; }
        [JsonPropertyName("player")]
        public SocialPlayer? Player { get; set; }
    }
}
#endif
