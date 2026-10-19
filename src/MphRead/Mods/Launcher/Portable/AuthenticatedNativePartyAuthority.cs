#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

/// <summary>
/// First-party Party adapter. Controls require a verified backend snapshot.
/// Credentials stay inside HunterLicenseClient; no bearer tokens cross this API.
/// </summary>
internal sealed partial class AuthenticatedNativePartyAuthority : INativePartyTravelAuthority
{
    private const string FunctionName = "party";

    public bool IsCurrent(NativePartySnapshot snapshot)
        => HunterLicenseClient.IsNativeAccountLeaseCurrent(snapshot.AccountWitness);

    public async Task<NativePartySnapshot> ReadAsync(CancellationToken cancellation)
    {
        var result = await InvokeAsync(new PartyWireRequest
        {
            Action = "snapshot"
        }, cancellation).ConfigureAwait(false);
        if (!result.Value.Ok || result.Value.Snapshot == null)
            throw new InvalidOperationException("Party snapshot was not authorized.");
        return ParseLeased(result.Value.Snapshot, result.Lease);
    }

    internal static async Task<NativeSocialRecentPlayer[]> ReadRecentAsync(object expectedLease,
        string expectedSelf, CancellationToken token)
    {
        if (!HunterLicenseClient.IsNativeAccountLeaseCurrent(expectedLease)
            || !NativeSocialFriendsController.ValidPrimeId(expectedSelf))
            throw new InvalidOperationException("Social account changed.");
        var result = await InvokeAsync(new PartyWireRequest { Action = "snapshot" }, token, expectedLease).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!result.Value.Ok || result.Value.Snapshot is not { } snapshot
            || !ReferenceEquals(result.Lease, expectedLease)
            || !HunterLicenseClient.IsNativeAccountLeaseCurrent(expectedLease)
            || !String.Equals(snapshot.SelfPrimeId, expectedSelf, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Social account changed.");
        return NativeSocialRecentPlayers.Verify(expectedSelf, snapshot.RecentPlayers);
    }

    public async Task<bool> ExecuteAsync(NativePartyCommand command, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ExpectedRevision < 0 || command.PartyId == null || command.InviteId == null
            || !NativeSocialFriendsController.ValidPrimeId(command.ExpectedSelfPrimeId)
            || command.TargetPrimeId == null || command.PartyId.Length > 128
            || command.InviteId.Length > 128 || command.TargetPrimeId.Length > 27)
            return false;
        if (command.TargetPrimeId.Length != 0 &&
            !NativeSocialFriendsController.ValidPrimeId(command.TargetPrimeId))
            return false;
        string action = command.Action switch
        {
            NativePartyAction.Invite => "invite",
            NativePartyAction.AcceptInvite => "accept_invite",
            NativePartyAction.DeclineInvite => "decline_invite",
            NativePartyAction.CancelInvite => "cancel_invite",
            NativePartyAction.Leave => "leave",
            NativePartyAction.Kick => "kick",
            NativePartyAction.Promote => "promote",
            NativePartyAction.Disband => "disband",
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        var result = await InvokeAsync(new PartyWireRequest
        {
            Action = action,
            ExpectedRevision = command.ExpectedRevision,
            ExpectedSelfPrimeId = command.ExpectedSelfPrimeId,
            PartyId = command.PartyId,
            TargetPrimeId = command.TargetPrimeId,
            InviteId = command.InviteId
        }, cancellation).ConfigureAwait(false);
        return result.Value.Ok;
    }

    private static async Task<(PartyWireEnvelope Value, object Lease)> InvokeAsync(PartyWireRequest body,
        CancellationToken cancellation, object? expectedLease = null)
    {
        string name = LauncherPrefs.PlayerName.Trim();
        body.DisplayName = name.Length == 0 ? "Player" : name;
        body.FavoriteHunter = Math.Clamp((int)Hunters.Resolve(LauncherPrefs.LastHunter), 0, 6);
        var result = await HunterLicenseClient.InvokeNativeFunctionWithLeaseAsync<PartyWireEnvelope>(
            FunctionName, body, cancellation, expectedLease).ConfigureAwait(false);
        if (!HunterLicenseClient.IsNativeAccountLeaseCurrent(result.Lease))
            throw new InvalidOperationException("Party account changed.");
        return result;
    }

    internal static NativePartySnapshot ParseLeased(PartyWireSnapshot value, object lease)
    {
        if (!HunterLicenseClient.IsNativeAccountLeaseCurrent(lease))
            throw new InvalidOperationException("Party account changed.");
        return Parse(value) with { AccountWitness = lease };
    }

    internal static NativePartySnapshot Parse(PartyWireSnapshot value)
    {
        if (!NativeSocialFriendsController.ValidPrimeId(value.SelfPrimeId)
            || value.ServerRevision < 0 || value.Members == null
            || value.Incoming == null || value.Outgoing == null
            || value.Members.Count > 8 || value.Incoming.Count > 256
            || value.Outgoing.Count > 256
            || value.PartyId == null || value.LeaderPrimeId == null
            || value.PartyId.Length > 128 || value.LeaderPrimeId.Length > 27)
            throw new InvalidOperationException("Party service returned an invalid snapshot.");
        if (value.PartyId.Length > 0 &&
            !NativeSocialFriendsController.ValidPrimeId(value.LeaderPrimeId))
            throw new InvalidOperationException("Party leader identity is invalid.");
        NativePartyMember[] members = value.Members.Select(m =>
        {
            if (m == null || !NativeSocialFriendsController.ValidPrimeId(m.PrimeId)
                || String.IsNullOrWhiteSpace(m.DisplayName) || m.DisplayName.Length > 128)
                throw new InvalidOperationException("Party member identity is invalid.");
            return new NativePartyMember(m.PrimeId.ToUpperInvariant(), m.DisplayName,
                m.Presence switch
                {
                    "offline" => NativePresence.Offline,
                    "online" => NativePresence.Online,
                    "lobby" => NativePresence.InLobby,
                    "match" => NativePresence.InMatch,
                    _ => NativePresence.Unknown
                });
        }).ToArray();
        if (members.Select(m => m.PrimeId).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != members.Length)
            throw new InvalidOperationException("Party service returned duplicate members.");
        NativePartyInvite[] Convert(List<PartyWireInvite> invites) => invites.Select(i =>
        {
            if (i == null || String.IsNullOrWhiteSpace(i.InviteId) || i.InviteId.Length > 128
                || String.IsNullOrWhiteSpace(i.PartyId) || i.PartyId.Length > 128
                || !NativeSocialFriendsController.ValidPrimeId(i.FromPrimeId)
                || !NativeSocialFriendsController.ValidPrimeId(i.ToPrimeId))
                throw new InvalidOperationException("Party invite identity is invalid.");
            return new NativePartyInvite(i.InviteId, i.PartyId,
                i.FromPrimeId.ToUpperInvariant(), i.ToPrimeId.ToUpperInvariant(), i.ExpiresAt);
        }).ToArray();
        var snapshot = new NativePartySnapshot(value.SelfPrimeId.ToUpperInvariant(),
            value.PartyId, value.LeaderPrimeId.ToUpperInvariant(), value.ServerRevision,
            members, Convert(value.Incoming), Convert(value.Outgoing)) { Travel = ParseTravel(value.Travel), Reservation = ParseReservation(value.Reservation) };
        if (!NativePartyAuthorityController.ValidSnapshot(snapshot))
            throw new InvalidOperationException("Party service returned inconsistent membership or invitations.");
        return snapshot;
    }

    internal sealed class PartyWireEnvelope
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("snapshot")] public PartyWireSnapshot? Snapshot { get; set; }
        [JsonPropertyName("locator")] public PartyWireLocator? Locator { get; set; }
        [JsonPropertyName("reservation")] public PartyWireReservation? Reservation { get; set; }
    }

    internal sealed partial class PartyWireRequest
    {
        [JsonPropertyName("action")] public string Action { get; set; } = "";
        [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
        [JsonPropertyName("favorite_hunter")] public int FavoriteHunter { get; set; }
        [JsonPropertyName("expected_revision")] public long? ExpectedRevision { get; set; }
        [JsonPropertyName("expected_self_prime_id")] public string ExpectedSelfPrimeId { get; set; } = "";
        [JsonPropertyName("party_id")] public string PartyId { get; set; } = "";
        [JsonPropertyName("target_prime_id")] public string TargetPrimeId { get; set; } = "";
        [JsonPropertyName("invite_id")] public string InviteId { get; set; } = "";
    }

    internal sealed class PartyWireSnapshot
    {
        [JsonPropertyName("self_prime_id")] public string SelfPrimeId { get; set; } = "";
        [JsonPropertyName("party_id")] public string PartyId { get; set; } = "";
        [JsonPropertyName("leader_prime_id")] public string LeaderPrimeId { get; set; } = "";
        [JsonPropertyName("server_revision")] public long ServerRevision { get; set; } = -1;
        [JsonPropertyName("members")] public List<PartyWireMember> Members { get; set; } = null!;
        [JsonPropertyName("incoming")] public List<PartyWireInvite> Incoming { get; set; } = null!;
        [JsonPropertyName("outgoing")] public List<PartyWireInvite> Outgoing { get; set; } = null!;
        [JsonPropertyName("recent_players")] public List<NativeSocialRecentPlayer>? RecentPlayers { get; set; }
        [JsonPropertyName("travel")] public PartyWireTravel? Travel { get; set; }
        [JsonPropertyName("reservation")] public PartyWireReservation? Reservation { get; set; }
    }
    internal sealed class PartyWireMember
    {
        [JsonPropertyName("prime_id")] public string PrimeId { get; set; } = "";
        [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
        [JsonPropertyName("presence")] public string Presence { get; set; } = "";
    }
    internal sealed class PartyWireInvite
    {
        [JsonPropertyName("invite_id")] public string InviteId { get; set; } = "";
        [JsonPropertyName("party_id")] public string PartyId { get; set; } = "";
        [JsonPropertyName("from_prime_id")] public string FromPrimeId { get; set; } = "";
        [JsonPropertyName("to_prime_id")] public string ToPrimeId { get; set; } = "";
        [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
    }
}
#endif
