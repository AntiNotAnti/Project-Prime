#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

internal static partial class SocialClient
{
    internal sealed record InvitationHomeState(NativeSocialDirectory Directory, DateTimeOffset ReadAt);
    private static InvitationHomeState? _invitationHome;
    internal static InvitationHomeState? InvitationHome
    {
        get
        {
            var state = Volatile.Read(ref _invitationHome);
            return state != null && HunterLicenseClient.IsNativeAccountLeaseCurrent(state.Directory.AccountWitness)
                && state.ReadAt <= DateTimeOffset.UtcNow && state.ReadAt.AddSeconds(45) > DateTimeOffset.UtcNow ? state : null;
        }
    }
    internal static void PublishInvitations(NativeSocialDirectory directory)
    {
        if (HunterLicenseClient.IsNativeAccountLeaseCurrent(directory.AccountWitness))
            Volatile.Write(ref _invitationHome, new(directory, DateTimeOffset.UtcNow));
    }
    internal static async Task<SocialSnapshot> LoadForAsync(object expectedLease, CancellationToken token)
    {
        var envelope = await InvokeAsync("snapshot", null, token, expectedLease: expectedLease).ConfigureAwait(false);
        if (!envelope.Ok || envelope.Snapshot is not { } snapshot)
            throw new InvalidOperationException("Social directory unavailable.");
        Volatile.Write(ref _current, snapshot);
        return snapshot;
    }

    internal static async Task<InvitationEnvelope> InvokeInvitationAsync(string action, object expectedLease,
        CancellationToken token, Dictionary<string, object?>? fields = null)
    {
        var body = fields ?? new Dictionary<string, object?>();
        body["action"] = action;
        body["display_name"] = String.IsNullOrWhiteSpace(LauncherPrefs.PlayerName) ? "Player" : LauncherPrefs.PlayerName.Trim();
        body["favorite_hunter"] = Math.Clamp((int)Hunters.Resolve(LauncherPrefs.LastHunter), 0, 6);
        var result = await HunterLicenseClient.InvokeNativeFunctionWithLeaseAsync<InvitationEnvelope>(
            "social-invites", body, token, expectedLease).ConfigureAwait(false);
        if (!HunterLicenseClient.IsNativeAccountLeaseCurrent(result.Lease)
            || !ReferenceEquals(expectedLease, result.Lease))
            throw new InvalidOperationException("Social account changed.");
        if (!result.Value.Ok) throw new InvalidOperationException("Lobby invitation was refused.");
        return result.Value;
    }

    internal sealed class InvitationEnvelope
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("snapshot")] public InvitationSnapshot? Snapshot { get; set; }
        [JsonPropertyName("lobby")] public InvitationLocator? Lobby { get; set; }
        [JsonPropertyName("locator")] public InvitationLocator? Locator { get; set; }
    }
    internal sealed class InvitationSnapshot
    {
        [JsonPropertyName("incoming")] public InvitationRow[] Incoming { get; set; } = Array.Empty<InvitationRow>();
        [JsonPropertyName("outgoing")] public InvitationRow[] Outgoing { get; set; } = Array.Empty<InvitationRow>();
    }
    internal sealed class InvitationRow
    {
        [JsonPropertyName("invite_id")] public string InviteId { get; set; } = "";
        [JsonPropertyName("prime_id")] public string PrimeId { get; set; } = "";
        [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
        [JsonPropertyName("lobby_id")] public string LobbyId { get; set; } = "";
        [JsonPropertyName("room_key")] public string RoomKey { get; set; } = "";
        [JsonPropertyName("server_name")] public string ServerName { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
        internal NativeSocialInvitation Parse(bool incoming) => new(InviteId, PrimeId, DisplayName,
            incoming, LobbyId, RoomKey, ServerName, Status, CreatedAt, ExpiresAt);
    }
    internal sealed class InvitationLocator
    {
        [JsonPropertyName("lobby_id")] public string LobbyId { get; set; } = "";
        [JsonPropertyName("host")] public string Host { get; set; } = "";
        [JsonPropertyName("port")] public int Port { get; set; }
        [JsonPropertyName("authority_epoch")] public string AuthorityEpoch { get; set; } = "";
        [JsonPropertyName("protocol")] public byte Protocol { get; set; }
        [JsonPropertyName("room_key")] public string RoomKey { get; set; } = "";
        [JsonPropertyName("server_name")] public string ServerName { get; set; } = "";
        [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
        internal NativeSocialJoinTarget Target(string self, object lease)
        {
            if (!NativeSocialInvitationValidation.Epoch(AuthorityEpoch, out ulong epoch))
                throw new InvalidOperationException("Lobby authority is invalid.");
            return new(self, LobbyId, Host, Port, Protocol, epoch, RoomKey, ServerName, ExpiresAt)
                { AccountWitness = lease };
        }
    }
}
#endif
