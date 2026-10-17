#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

/// <summary>
/// Candidate first-party Party edge-function adapter. Not instantiated by the
/// launcher until the backend Party service is deployed and verified.
/// Credentials stay inside HunterLicenseClient; no bearer tokens cross this API.
/// </summary>
internal sealed class AuthenticatedNativePartyAuthority : INativePartyAuthority
{
    private const string FunctionName = "party";

    public async Task<NativePartySnapshot> ReadAsync(CancellationToken cancellation)
    {
        PartyWireEnvelope result = await InvokeAsync(new PartyWireRequest
        {
            Action = "snapshot"
        }, cancellation).ConfigureAwait(false);
        if (!result.Ok || result.Snapshot == null)
            throw new InvalidOperationException("Party snapshot was not authorized.");
        return Parse(result.Snapshot);
    }

    public async Task<bool> ExecuteAsync(NativePartyCommand command, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ExpectedRevision < 0 || command.PartyId == null || command.InviteId == null
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
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        PartyWireEnvelope result = await InvokeAsync(new PartyWireRequest
        {
            Action = action,
            ExpectedRevision = command.ExpectedRevision,
            PartyId = command.PartyId,
            TargetPrimeId = command.TargetPrimeId,
            InviteId = command.InviteId
        }, cancellation).ConfigureAwait(false);
        return result.Ok;
    }

    private static Task<PartyWireEnvelope> InvokeAsync(PartyWireRequest body,
        CancellationToken cancellation)
        => HunterLicenseClient.InvokeAuthenticatedFunctionAsync<PartyWireEnvelope>(
            FunctionName, body, cancellation);

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
        return new NativePartySnapshot(value.SelfPrimeId.ToUpperInvariant(),
            value.PartyId, value.LeaderPrimeId.ToUpperInvariant(), value.ServerRevision,
            members, Convert(value.Incoming), Convert(value.Outgoing));
    }

    internal sealed class PartyWireEnvelope
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("snapshot")] public PartyWireSnapshot? Snapshot { get; set; }
    }

    internal sealed class PartyWireRequest
    {
        [JsonPropertyName("action")] public string Action { get; set; } = "";
        [JsonPropertyName("expected_revision")] public long? ExpectedRevision { get; set; }
        [JsonPropertyName("party_id")] public string PartyId { get; set; } = "";
        [JsonPropertyName("target_prime_id")] public string TargetPrimeId { get; set; } = "";
        [JsonPropertyName("invite_id")] public string InviteId { get; set; } = "";
    }

    internal sealed class PartyWireSnapshot
    {
        [JsonPropertyName("self_prime_id")] public string SelfPrimeId { get; set; } = "";
        [JsonPropertyName("party_id")] public string PartyId { get; set; } = "";
        [JsonPropertyName("leader_prime_id")] public string LeaderPrimeId { get; set; } = "";
        [JsonPropertyName("server_revision")] public long ServerRevision { get; set; }
        [JsonPropertyName("members")] public List<PartyWireMember> Members { get; set; } = new();
        [JsonPropertyName("incoming")] public List<PartyWireInvite> Incoming { get; set; } = new();
        [JsonPropertyName("outgoing")] public List<PartyWireInvite> Outgoing { get; set; } = new();
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
