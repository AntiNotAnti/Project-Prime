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
    internal static class SocialClient
    {
        public static async Task<SocialSnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            SocialEnvelope envelope = await InvokeAsync(
                "snapshot", targetPrimeId: null, cancellationToken).ConfigureAwait(false);
            return envelope.Snapshot
                ?? throw new InvalidOperationException("Social service returned no snapshot.");
        }

        public static async Task<SocialLookupResult> LookupAsync(
            string primeId, CancellationToken cancellationToken = default)
        {
            SocialEnvelope envelope = await InvokeAsync(
                "lookup", primeId, cancellationToken).ConfigureAwait(false);
            return new SocialLookupResult(
                envelope.Status.Equals("found", StringComparison.Ordinal),
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

        private static async Task<SocialMutationResult> MutateAsync(
            string action, string primeId, CancellationToken cancellationToken)
        {
            SocialEnvelope envelope = await InvokeAsync(
                action, primeId, cancellationToken).ConfigureAwait(false);
            if (envelope.Ok)
                SocialPresenceClient.RefreshNow();
            return new SocialMutationResult
            {
                Success = envelope.Ok,
                Status = envelope.Status,
                Snapshot = envelope.Snapshot
            };
        }

        private static async Task<SocialEnvelope> InvokeAsync(
            string action, string? targetPrimeId, CancellationToken cancellationToken)
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

            return await HunterLicenseClient.InvokeAuthenticatedFunctionAsync<SocialEnvelope>(
                "social", body, cancellationToken).ConfigureAwait(false);
        }
    }

    internal sealed class SocialSnapshot
    {
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
