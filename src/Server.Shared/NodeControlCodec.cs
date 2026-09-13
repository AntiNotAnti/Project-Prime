using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MphRead;

namespace ProjectPrime.Server.Shared;

public static class NodeControlCodec
{
    public const int Version = 3;
    public const string UnsupportedVersionMessage = "Unsupported control envelope version.";
    public const int MaximumFrameBytes = 32 * 1024;
    public const int MaximumLobbyListEntries = 16;
    // LobbyConfigure.Rules is intentionally additive within envelope v3:
    // older Nodes use UnmappedMemberHandling.Disallow, so an advanced command
    // fails closed instead of silently dropping host rules. Missing optional
    // fields remain readable for legacy-shaped v3 payloads.
    public static NodeControlRequest Read(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 2 or > MaximumFrameBytes) throw new JsonException("Invalid frame length.");
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
        RejectDuplicates(doc.RootElement);
        var e = doc.RootElement;
        if (e.ValueKind != JsonValueKind.Object || e.EnumerateObject().Count() != 4)
            throw new JsonException("Invalid control envelope.");
        if (!e.TryGetProperty("version", out JsonElement version)
            || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int envelopeVersion))
            throw new JsonException("Invalid control envelope.");
        if (envelopeVersion != Version)
            throw new JsonException($"{UnsupportedVersionMessage} Received {envelopeVersion}; expected {Version}.");
        if (!Guid.TryParseExact(e.GetProperty("requestId").GetString(), "D", out Guid requestId) || requestId == Guid.Empty)
            throw new JsonException("Invalid control envelope.");
        var p = e.GetProperty("payload");
        string? type = e.GetProperty("type").GetString();
        NodeCommand command = type switch
        {
            "lobby.tournament.team" => Decode(p, NodeJsonContext.Default.LobbyTournamentAssignTeam),
            "lobby.tournament.observer" => Decode(p, NodeJsonContext.Default.LobbyTournamentSetObserver),
            "lobby.round.status" => Decode(p, NodeJsonContext.Default.LobbyRoundStatus),
            "lobby.tournament.identity" => Decode(p, NodeJsonContext.Default.LobbyTournamentIdentity),
            "lobby.tournament.control" => Decode(p, NodeJsonContext.Default.LobbyTournamentControl),
            "lobby.tournament.next" => Decode(p, NodeJsonContext.Default.LobbyTournamentSelectNext),
            "lobby.vote.open" => Decode(p, NodeJsonContext.Default.LobbyVoteOpen),
            "lobby.vote.cast" => Decode(p, NodeJsonContext.Default.LobbyVoteCast),
            "lobby.vote.resolve" => Decode(p, NodeJsonContext.Default.LobbyVoteResolve),
            "match.transition.propose" => Decode(p, NodeJsonContext.Default.LobbyMatchTransitionPropose),
            "match.transition.vote" => Decode(p, NodeJsonContext.Default.LobbyMatchTransitionVote),
            "node.ping" => Decode(p, NodeJsonContext.Default.NodePing),
            "node.presence.visibility" => Decode(p, NodeJsonContext.Default.NodeSetPresenceVisibility),
            // Keep a short source-compatible alias for clients that named the
            // action "set" before the canonical route was documented.
            "node.presence.set" => Decode(p, NodeJsonContext.Default.NodeSetPresenceVisibility),
            "node.catalog" => Decode(p, NodeJsonContext.Default.NodeCatalogRequest),
            "match.rejoin" => Decode(p, NodeJsonContext.Default.NodeMatchRejoin),
            "lobby.create" => Decode(p, NodeJsonContext.Default.LobbyCreate),
            "lobby.list" => Decode(p, NodeJsonContext.Default.LobbyList),
            "quickplay.join" => Decode(p, NodeJsonContext.Default.QuickPlayJoin),
            "lobby.join" => Decode(p, NodeJsonContext.Default.LobbyJoin),
            "lobby.queue.join" => Decode(p, NodeJsonContext.Default.LobbyQueueJoin),
            "lobby.queue.leave" => Decode(p, NodeJsonContext.Default.LobbyQueueLeave),
            "lobby.queue.accept" => Decode(p, NodeJsonContext.Default.LobbyQueueAccept),
            "lobby.queue.decline" => Decode(p, NodeJsonContext.Default.LobbyQueueDecline),
            "lobby.leave" => Decode(p, NodeJsonContext.Default.LobbyLeave),
            "lobby.ready.set" => Decode(p, NodeJsonContext.Default.LobbySetReady),
            "lobby.hunter.select" => Decode(p, NodeJsonContext.Default.LobbySelectHunter),
            "lobby.cosmetics.select" => Decode(p, NodeJsonContext.Default.LobbySelectCosmetics),
            "lobby.team.request" => Decode(p, NodeJsonContext.Default.LobbyRequestTeam),
            "lobby.chat" => Decode(p, NodeJsonContext.Default.LobbyChat),
            "lobby.configure" => Decode(p, NodeJsonContext.Default.LobbyConfigure),
            "lobby.start" => Decode(p, NodeJsonContext.Default.LobbyStart),
            "lobby.rematch" => Decode(p, NodeJsonContext.Default.LobbyRematch),
            "lobby.return" => Decode(p, NodeJsonContext.Default.LobbyReturn),
            _ => throw new JsonException("Unknown control type.")
        };
        try { ValidateCommand(type!, command); }
        catch (ArgumentException ex) { throw new JsonException("Invalid control command.", ex); }
        return new(requestId, command);
    }
    private static T Decode<T>(JsonElement value, JsonTypeInfo<T> type) where T : NodeCommand
        => value.Deserialize(type) ?? throw new JsonException("Missing command.");
    public static byte[] Write<T>(string type, long eventId, Guid? requestId, T payload)
    {
        ValidateEventPayload(payload);
        ValidateRoute(type, payload);
        var info = (JsonTypeInfo<T>?)NodeJsonContext.Default.GetTypeInfo(typeof(T)) ?? throw new ArgumentException("Unknown event payload.");
        var message = new NodeControlEvent(Version, type, eventId, requestId, JsonSerializer.SerializeToElement(payload, info));
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, NodeJsonContext.Default.NodeControlEvent);
        if (bytes.Length > MaximumFrameBytes) throw new InvalidOperationException("Control event exceeds frame limit.");
        return bytes;
    }
    public static void ValidateEventPayload<T>(T payload)
    {
        switch (payload)
        {
            case NodeCatalogRequest catalog:
                if (catalog.Revision <= 0 || catalog.Page is < 0 or > 31 || catalog.PageSize is < 1 or > 32)
                    throw new ArgumentException("Invalid Node catalog request.");
                break;
            case NodeCatalogPage catalog:
                if (catalog.Revision <= 0 || catalog.Page is < 0 or > 31
                    || catalog.PageCount is < 1 or > 32 || catalog.Page >= catalog.PageCount
                    || catalog.TotalEntries is < 0 or > 256
                    || catalog.CatalogHash is not { Length: 64 } || !catalog.CatalogHash.All(char.IsAsciiHexDigit)
                    || catalog.Entries.IsDefault || catalog.Entries.Length > 32)
                    throw new ArgumentException("Invalid Node catalog page.");
                var pageKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (ContentIdentity identity in catalog.Entries)
                {
                    ValidateCatalogIdentity(identity);
                    if (!pageKeys.Add(identity.MapKey)) throw new ArgumentException("Node catalog page contains a duplicate map.");
                }
                break;
            case NodeMatchHandoff handoff: handoff.Validate(); break;
            case NodeMatchTransitionVoteSnapshot snapshot: snapshot.Validate(); break;
            case NodeMatchTransitionStarted started: started.Validate(); break;
            case LobbyConfigure configure:
                if (configure.ExpectedRevision < 0 || configure.MapKey is not { Length: > 0 and <= 128 }
                    || configure.MapKey.Any(c => c is < ' ' or > '~') || !Enum.IsDefined(configure.Mode)
                    || configure.BotCount is < 0 or > 8
                    || !Enum.IsDefined(configure.BotDifficulty))
                    throw new ArgumentException("Invalid lobby configuration.");
                try { _ = configure.NormalizeRules(); }
                catch (ArgumentException ex) { throw new ArgumentException("Invalid lobby rules.", ex); }
                break;
            case NodeRoundSnapshot round:
                if (round.Lobby == null) throw new ArgumentException("Missing round lobby.");
                ValidateEventPayload(round.Lobby);
                if (round.ConfigurationRevision < 0 || round.Options.IsDefault || round.Options.Length > 8 || round.Options.Any(o => o == null)
                    || round.TournamentId == Guid.Empty || round.RoundId == Guid.Empty
                    || round.Options.Select(o => o?.Id).Distinct().Count() != round.Options.Length
                    || round.OwnVote != 0 && !round.Options.Any(o => o.Id == round.OwnVote)
                    || !round.Options.IsEmpty && (round.BallotRevision == 0 || round.VoteDeadline == null
                        || round.Lobby.Phase != LobbyPhase.PostMatch)
                    || round.Options.IsEmpty && round.VoteDeadline != null)
                    throw new ArgumentException("Invalid round snapshot.");
                foreach (var option in round.Options) ValidateVoteEntry(option);
                if (round.ResolvedOption is { } resolved)
                {
                    ValidateVoteEntry(resolved);
                    if (!round.Options.IsEmpty && !round.Options.Any(o => o.Id == resolved.Id
                        && o.Choice == resolved.Choice && o.MapKey == resolved.MapKey && o.Mode == resolved.Mode
                        && o.RequiredMap == resolved.RequiredMap
                        && o.SpawnPolicy == resolved.SpawnPolicy))
                        throw new ArgumentException("Invalid resolved option.");
                }
                break;
            case NodeSessionSnapshot session: session.Validate(); break;
            case NodePresenceVisibilityChanged visibility:
                if (visibility.Revision < 0) throw new ArgumentException("Invalid presence visibility acknowledgement.");
                break;
            case NodeControlDeliveryOverflow overflow:
                if (overflow.Code != "delivery_overflow" || !overflow.ResumeRequired)
                    throw new ArgumentException("Invalid control delivery overflow.");
                break;
            case LobbySnapshot lobby:
                ContractGuard.Id(lobby.LobbyId); ContractGuard.Id(lobby.OwnerSessionId);
                ContractGuard.Text(lobby.Name, 64); ContractGuard.Defined(lobby.Visibility); ContractGuard.Defined(lobby.Phase);
                if (lobby.Revision < 0 || lobby.PlayerLimit is < 1 or > 8 || lobby.ObserverLimit is < 0 or > 16
                    || lobby.BotCount < 0 || lobby.BotCount > lobby.PlayerLimit
                    || lobby.TimeLimitSeconds is < 1 or > 3600 || lobby.PointGoal is < 1 or > ushort.MaxValue
                    || lobby.MapKey is null || lobby.MapKey.Length > 128 || lobby.MapKey.Any(c => c is < ' ' or > '~') || !Enum.IsDefined(lobby.Mode)
                    || !Enum.IsDefined(lobby.BotDifficulty)
                    || !Enum.IsDefined(lobby.SeatPolicy) || !Enum.IsDefined(lobby.DuelQueuePolicy)
                    || lobby.Members.IsDefault || lobby.Chat.IsDefault) throw new ArgumentException("Invalid lobby snapshot.");
                try
                {
                    LobbyRulesOptions normalized = (lobby.Rules ?? LobbyRulesOptions.Empty)
                        .Normalize(lobby.Mode, lobby.TimeLimitSeconds, lobby.PointGoal);
                    // A current snapshot carries both the canonical object and
                    // legacy projections for older clients. If the object is
                    // present, those projections must agree with it; an absent
                    // object is the permitted old-payload form.
                    if (lobby.Rules is not null && normalized != lobby.Rules)
                        throw new ArgumentException("Lobby rule projections conflict with canonical rules.");
                }
                catch (ArgumentException ex) { throw new ArgumentException("Invalid lobby rules.", ex); }
                lobby.RequiredMap?.Validate();
                foreach (LobbyMember member in lobby.Members)
                {
                    if (member is null) throw new ArgumentException("Null lobby member.");
                    member.Validate();
                }
                if (lobby.Waitlist is { } waitlist)
                {
                    if (waitlist.Count < 0 || waitlist.Count > 1024 || waitlist.Entries.IsDefault || waitlist.Entries.Length > 1024
                        || waitlist.Entries.Any(e => e is null || e.Position < 1 || e.QueueSequence < 1
                            || e.DisplayName is not { Length: >= 1 and <= 16 } || e.DisplayName.Any(char.IsControl)
                            || !Enum.IsDefined(e.State))) throw new ArgumentException("Invalid lobby waitlist snapshot.");
                    if (waitlist.SelfQueueSequence is < 1 || waitlist.SelfOffer is { OfferId: var offer } && offer == Guid.Empty
                        || waitlist.SelfOffer is { ExpiresAt: var expires } && expires <= DateTimeOffset.MinValue)
                        throw new ArgumentException("Invalid lobby waitlist offer.");
                }
                break;
            case LobbyListSnapshot list:
                if (list.Lobbies.IsDefault || list.Lobbies.Length > MaximumLobbyListEntries || list.Lobbies.Any(l => l is null
                    || l.LobbyId == Guid.Empty || l.Revision < 1 || l.Players < 0 || l.Players > l.PlayerLimit
                    || l.PlayerLimit is < 1 or > 8 || l.Observers < 0 || l.ObserverLimit is < 0 or > 128
                    || l.BotCount < 0 || l.BotCount > l.PlayerLimit || l.WaitlistCount < 0 || l.WaitlistCount > 1024
                    || l.Name is not { Length: > 0 and <= 64 } || l.Name.Any(char.IsControl)
                    || l.MapKey is null || l.MapKey.Length > 128 || l.MapKey.Any(c => c is < ' ' or > '~') || !Enum.IsDefined(l.Phase)
                    || !Enum.IsDefined(l.Mode) || !Enum.IsDefined(l.BotDifficulty)
                    || l.TimeLimitSeconds is < 1 or > 3600
                    || l.PointGoal is < 1 or > ushort.MaxValue || l.ObjectiveTimeGoalSeconds is < 1 or > 3600
                    || !Enum.IsDefined(l.SeatPolicy) || InvalidListRuleMetadata(l)))
                    throw new ArgumentException("Invalid lobby list.");
                break;
        }
    }

    private static void ValidateCommand(string type, NodeCommand command)
    {
        switch (command)
        {
            case LobbyConfigure configure:
                ValidateEventPayload(configure);
                break;
            case LobbyMatchTransitionPropose propose:
                if (type != "match.transition.propose") throw new ArgumentException("Transition command route does not match payload.");
                propose.Validate();
                break;
            case LobbyMatchTransitionVote vote:
                if (type != "match.transition.vote") throw new ArgumentException("Transition command route does not match payload.");
                vote.Validate();
                break;
        }
    }

    private static void ValidateRoute<T>(string type, T payload)
    {
        if (string.IsNullOrWhiteSpace(type) || type.Length > 64 || type.Any(char.IsControl))
            throw new ArgumentException("Invalid control route.", nameof(type));
        bool valid = type switch
        {
            "match.transition.state" => payload is NodeMatchTransitionVoteSnapshot,
            "match.transition.started" => payload is NodeMatchTransitionStarted,
            "match.transition.propose" => payload is LobbyMatchTransitionPropose,
            "match.transition.vote" => payload is LobbyMatchTransitionVote,
            "node.presence.visibility" => payload is NodePresenceVisibilityChanged,
            "control.delivery_overflow" => payload is NodeControlDeliveryOverflow,
            _ => true
        };
        if (!valid) throw new ArgumentException("Control route does not match payload.", nameof(type));
    }
    private static void ValidateVoteEntry(LobbyVoteEntry option)
    {
        if (option == null || option.Id is < 1 or > 8 || option.Votes is < 0 or > 8)
            throw new ArgumentException("Invalid vote option.");
        ContractGuard.Defined(option.Choice); ContractGuard.Defined(option.Mode); ContractGuard.Text(option.MapKey, 128);
        if (option.Choice == LobbyVoteChoice.SpawnPolicy)
        {
            if (option.SpawnPolicy is not { } policy || !Enum.IsDefined(policy))
                throw new ArgumentException("Spawn-policy vote requires a valid policy.");
            if (policy == SpawnPolicy.Duel && option.Mode != MatchMode.Battle)
                throw new ArgumentException("Duel spawn-policy vote requires Battle mode.");
        }
        else if (option.SpawnPolicy.HasValue)
            throw new ArgumentException("Only spawn-policy votes may carry a spawn policy.");
        option.RequiredMap?.Validate();
    }
    private static bool InvalidListRuleMetadata(LobbyListEntry entry)
    {
        bool objective = entry.Mode is MatchMode.Defender or MatchMode.TeamDefender or MatchMode.PrimeHunter;
        return objective ? entry.PointGoal is not null : entry.ObjectiveTimeGoalSeconds is not null;
    }

    private static void ValidateCatalogIdentity(ContentIdentity identity)
    {
        if (identity is null || identity.MapKey is not { Length: > 0 and <= 128 }
            || identity.MapKey.Any(c => c is < ' ' or > '~')
            || identity.ContentHash is not { Length: > 0 and <= 128 }
            || identity.ContentHash.Any(char.IsControl)
            || identity.ContentVersion is not { Length: > 0 and <= 128 }
            || identity.ContentVersion.Any(char.IsControl)
            || identity.BuildVersion is not { Length: > 0 and <= 128 }
            || identity.BuildVersion.Any(char.IsControl)
            || identity.ProtocolVersion == 0)
            throw new ArgumentException("Invalid Node catalog identity.");
        identity.RequiredMap?.Validate();
    }
    public static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject())
            { if (!names.Add(p.Name)) throw new JsonException("Duplicate JSON property."); RejectDuplicates(p.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) RejectDuplicates(child);
    }
}
public sealed record NodeControlEvent(int Version, string Type, long EventId, Guid? RequestId, JsonElement Payload);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true,
    UseStringEnumConverter = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(LobbyTournamentAssignTeam))]
[JsonSerializable(typeof(LobbyTournamentSetObserver))]
[JsonSerializable(typeof(LobbyRoundStatus))]
[JsonSerializable(typeof(LobbyTournamentIdentity))]
[JsonSerializable(typeof(LobbyTournamentControl))]
[JsonSerializable(typeof(LobbyTournamentSelectNext))]
[JsonSerializable(typeof(LobbyVoteOpen))]
[JsonSerializable(typeof(LobbyVoteCast))]
[JsonSerializable(typeof(LobbyVoteResolve))]
[JsonSerializable(typeof(LobbyMatchTransitionPropose))]
[JsonSerializable(typeof(LobbyMatchTransitionVote))]
[JsonSerializable(typeof(NodeMatchTransitionVoteSnapshot))]
[JsonSerializable(typeof(NodeMatchTransitionStarted))]
[JsonSerializable(typeof(NodeRoundSnapshot))]
[JsonSerializable(typeof(LobbyCreate))]
[JsonSerializable(typeof(LobbyRulesOptions))]
[JsonSerializable(typeof(LobbyList))]
[JsonSerializable(typeof(QuickPlayJoin))]
[JsonSerializable(typeof(LobbyJoin))]
[JsonSerializable(typeof(LobbyQueueJoin))]
[JsonSerializable(typeof(LobbyQueueLeave))]
[JsonSerializable(typeof(LobbyQueueAccept))]
[JsonSerializable(typeof(LobbyQueueDecline))]
[JsonSerializable(typeof(LobbyLeave))]
[JsonSerializable(typeof(LobbySetReady))]
[JsonSerializable(typeof(LobbySelectHunter))]
[JsonSerializable(typeof(LobbySelectCosmetics))]
[JsonSerializable(typeof(LobbyRequestTeam))]
[JsonSerializable(typeof(LobbyChat))]
[JsonSerializable(typeof(LobbyConfigure))]
[JsonSerializable(typeof(LobbyStart))]
[JsonSerializable(typeof(LobbyRematch))]
[JsonSerializable(typeof(LobbyReturn))]
[JsonSerializable(typeof(NodeMatchHandoff))]
[JsonSerializable(typeof(NodeMatchEnded))]
[JsonSerializable(typeof(NodeMatchCompletion))]
[JsonSerializable(typeof(NodeMatchRejoin))]
[JsonSerializable(typeof(LobbySnapshot))]
[JsonSerializable(typeof(LobbyListSnapshot))]
[JsonSerializable(typeof(NodeSessionSnapshot))]
[JsonSerializable(typeof(NodePing))]
[JsonSerializable(typeof(NodeSetPresenceVisibility))]
[JsonSerializable(typeof(NodePresenceVisibilityChanged))]
[JsonSerializable(typeof(NodeCatalogRequest))]
[JsonSerializable(typeof(NodeCatalogPage))]
[JsonSerializable(typeof(ContentIdentity))]
[JsonSerializable(typeof(MapRequirement))]
[JsonSerializable(typeof(NodePong))]
[JsonSerializable(typeof(NodeControlError))]
[JsonSerializable(typeof(NodeControlDeliveryOverflow))]
[JsonSerializable(typeof(LobbyLeft))]
[JsonSerializable(typeof(NodeControlEvent))]
public partial class NodeJsonContext : JsonSerializerContext;
