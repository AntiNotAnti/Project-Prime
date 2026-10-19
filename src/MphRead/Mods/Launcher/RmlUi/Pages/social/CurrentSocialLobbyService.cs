#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Social;

internal sealed partial class CurrentSocialFriendsService
{
    internal static NativePartyLocalLobby? CaptureLocalLobby()
        => NetSession.Active && NetSession.IsClient && NetSession.PersistentLobby
            && !NetSession.SemanticLegacyPlayback && NetSession.LocalSlot >= 0
            && NetSession.ServerSession is { Phase: SessionPhase.Lobby } session
                ? new(session.MatchId, session.AuthorityEpoch, session.Match.RoomKey, "") : null;

    public async Task<NativeSocialLobbyResult> ExecuteLobbyAsync(NativeSocialLobbyCommand command,
        CancellationToken token)
    {
        var directory = command.Directory;
        if (!IsCurrent(directory) || directory.AccountWitness == null || !directory.InvitationsAvailable)
            throw new InvalidOperationException("Social account changed.");
        object lease = directory.AccountWitness;
        var body = new Dictionary<string, object?>();
        string action;
        if (command.Action is NativeSocialLobbyAction.Invite or NativeSocialLobbyAction.JoinFriend)
        {
            if (command.Friend is not { Relation: NativeSocialRelation.Friend } friend
                || !directory.People.Contains(friend)) throw new InvalidOperationException("Selected friend changed.");
            body["target_prime_id"] = friend.PrimeId;
            action = command.Action == NativeSocialLobbyAction.Invite ? "send_invite" : "join_friend";
            if (command.Action == NativeSocialLobbyAction.Invite)
            {
                if (command.LocalLobby is not { } local || !AuthenticatedNativePartyAuthority.MatchesLocalLobby(local))
                    throw new InvalidOperationException("The current lobby changed.");
                var listed = await ServerBrowserService.FindListedLobbyAsync(local.AuthorityEpoch, token).ConfigureAwait(false);
                if (!listed.Verified || !AuthenticatedNativePartyAuthority.MatchesLocalLobby(local)
                    || listed.Entry.Status.Protocol != NetConfig.ProtocolVersion
                    || listed.Entry.Status.RoomKey != local.RoomKey)
                    throw new InvalidOperationException("The current lobby could not be verified.");
                var registered = await SocialClient.InvokeInvitationAsync("register_lobby", lease, token,
                    new() { ["host"] = listed.Entry.Listing.Address, ["port"] = listed.Entry.Listing.Port,
                        ["authority_epoch"] = local.AuthorityEpoch.ToString(CultureInfo.InvariantCulture),
                        ["protocol"] = NetConfig.ProtocolVersion, ["room_key"] = local.RoomKey,
                        ["server_name"] = listed.Entry.Name.Length > 96 ? listed.Entry.Name[..96] : listed.Entry.Name })
                    .ConfigureAwait(false);
                var target = registered.Lobby?.Target(directory.SelfPrimeId, lease);
                if (!NativeSocialInvitationValidation.ValidTarget(target, DateTimeOffset.UtcNow)
                    || target!.AuthorityEpoch != local.AuthorityEpoch || target.RoomKey != local.RoomKey
                    || target.Protocol != NetConfig.ProtocolVersion || target.Host != listed.Entry.Listing.Address
                    || target.Port != listed.Entry.Listing.Port || !AuthenticatedNativePartyAuthority.MatchesLocalLobby(local))
                    throw new InvalidOperationException("Registered lobby changed.");
                body["lobby_id"] = target.LobbyId;
            }
        }
        else
        {
            if (command.Invitation is not { } invite || !directory.Invitations.Contains(invite)
                || !NativeSocialInvitationValidation.Actionable(invite, DateTimeOffset.UtcNow))
                throw new InvalidOperationException("The invitation changed or expired.");
            action = command.Action switch
            {
                NativeSocialLobbyAction.Accept when invite.Incoming
                    => invite.Status == "accepted" ? "resolve_invite" : "accept_invite",
                NativeSocialLobbyAction.Decline when invite.Incoming && invite.Status == "pending" => "decline_invite",
                NativeSocialLobbyAction.Cancel when !invite.Incoming && invite.Status == "pending" => "cancel_invite",
                _ => throw new InvalidOperationException("The invitation action is unavailable.")
            };
            body["invite_id"] = invite.InviteId;
        }
        token.ThrowIfCancellationRequested();
        if (command.LocalLobby is { } captured && !AuthenticatedNativePartyAuthority.MatchesLocalLobby(captured))
            throw new InvalidOperationException("The current lobby changed.");
        var result = await SocialClient.InvokeInvitationAsync(action, lease, token, body).ConfigureAwait(false);
        NativeSocialJoinTarget? join = null;
        if (command.Action is NativeSocialLobbyAction.Accept or NativeSocialLobbyAction.JoinFriend)
        {
            join = result.Locator?.Target(directory.SelfPrimeId, lease);
            if (command.Invitation is { } invitation && join != null)
            {
                if (!String.Equals(join.LobbyId, invitation.LobbyId, StringComparison.OrdinalIgnoreCase)
                    || join.RoomKey != invitation.RoomKey) throw new InvalidOperationException("Invitation destination changed.");
                if (invitation.ExpiresAt < join.ExpiresAt) join = join with { ExpiresAt = invitation.ExpiresAt };
            }
            if (!NativeSocialInvitationValidation.ValidTarget(join, DateTimeOffset.UtcNow)
                || join!.Protocol != NetConfig.ProtocolVersion)
                throw new InvalidOperationException("The destination expired or changed.");
        }
        var reread = await ReadDirectoryAsync(lease, token).ConfigureAwait(false);
        if (!IsCurrent(reread) || reread.SelfPrimeId != directory.SelfPrimeId)
            throw new InvalidOperationException("Social account changed.");
        if (command.LocalLobby is { } current && !AuthenticatedNativePartyAuthority.MatchesLocalLobby(current))
            throw new InvalidOperationException("The current lobby changed.");
        return new(reread, join);
    }
}
#endif
