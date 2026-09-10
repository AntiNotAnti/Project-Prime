using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace FruityPrime.Server.Shared;

public static class NodeControlCodec
{
    public const int Version = 1;
    public const int MaximumFrameBytes = 32 * 1024;
    public static NodeControlRequest Read(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 2 or > MaximumFrameBytes) throw new JsonException("Invalid frame length.");
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
        RejectDuplicates(doc.RootElement);
        var e = doc.RootElement;
        if (e.ValueKind != JsonValueKind.Object || e.EnumerateObject().Count() != 4
            || e.GetProperty("version").GetInt32() != Version
            || !Guid.TryParseExact(e.GetProperty("requestId").GetString(), "D", out Guid requestId) || requestId == Guid.Empty)
            throw new JsonException("Invalid control envelope.");
        var p = e.GetProperty("payload");
        NodeCommand command = e.GetProperty("type").GetString() switch
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
            "node.ping" => Decode(p, NodeJsonContext.Default.NodePing),
            "match.rejoin" => Decode(p, NodeJsonContext.Default.NodeMatchRejoin),
            "lobby.create" => Decode(p, NodeJsonContext.Default.LobbyCreate),
            "lobby.list" => Decode(p, NodeJsonContext.Default.LobbyList),
            "lobby.join" => Decode(p, NodeJsonContext.Default.LobbyJoin),
            "lobby.queue.join" => Decode(p, NodeJsonContext.Default.LobbyQueueJoin),
            "lobby.queue.leave" => Decode(p, NodeJsonContext.Default.LobbyQueueLeave),
            "lobby.queue.accept" => Decode(p, NodeJsonContext.Default.LobbyQueueAccept),
            "lobby.queue.decline" => Decode(p, NodeJsonContext.Default.LobbyQueueDecline),
            "lobby.leave" => Decode(p, NodeJsonContext.Default.LobbyLeave),
            "lobby.ready.set" => Decode(p, NodeJsonContext.Default.LobbySetReady),
            "lobby.hunter.select" => Decode(p, NodeJsonContext.Default.LobbySelectHunter),
            "lobby.team.request" => Decode(p, NodeJsonContext.Default.LobbyRequestTeam),
            "lobby.chat" => Decode(p, NodeJsonContext.Default.LobbyChat),
            "lobby.configure" => Decode(p, NodeJsonContext.Default.LobbyConfigure),
            "lobby.start" => Decode(p, NodeJsonContext.Default.LobbyStart),
            "lobby.rematch" => Decode(p, NodeJsonContext.Default.LobbyRematch),
            "lobby.return" => Decode(p, NodeJsonContext.Default.LobbyReturn),
            _ => throw new JsonException("Unknown control type.")
        };
        return new(requestId, command);
    }
    private static T Decode<T>(JsonElement value, JsonTypeInfo<T> type) where T : NodeCommand
        => value.Deserialize(type) ?? throw new JsonException("Missing command.");
    public static byte[] Write<T>(string type, long eventId, Guid? requestId, T payload)
    {
        ValidateEventPayload(payload);
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
                        && o.Choice == resolved.Choice && o.MapKey == resolved.MapKey && o.Mode == resolved.Mode))
                        throw new ArgumentException("Invalid resolved option.");
                }
                break;
            case NodeSessionSnapshot session: session.Validate(); break;
            case LobbySnapshot lobby:
                ContractGuard.Id(lobby.LobbyId); ContractGuard.Id(lobby.OwnerSessionId);
                ContractGuard.Text(lobby.Name, 64); ContractGuard.Defined(lobby.Visibility); ContractGuard.Defined(lobby.Phase);
                if (lobby.Revision < 0 || lobby.PlayerLimit is < 1 or > 8 || lobby.ObserverLimit is < 0 or > 16
                    || lobby.BotCount < 0 || lobby.BotCount > lobby.PlayerLimit
                    || lobby.TimeLimitSeconds is < 1 or > 3600 || lobby.PointGoal is < 1 or > ushort.MaxValue
                    || !Enum.IsDefined(lobby.SeatPolicy) || !Enum.IsDefined(lobby.DuelQueuePolicy)
                    || lobby.Members.IsDefault || lobby.Chat.IsDefault) throw new ArgumentException("Invalid lobby snapshot.");
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
                if (list.Lobbies.IsDefault || list.Lobbies.Length > 64 || list.Lobbies.Any(l => l is null
                    || l.LobbyId == Guid.Empty || l.Revision < 1 || l.Players < 0 || l.Players > l.PlayerLimit
                    || l.PlayerLimit is < 1 or > 8 || l.Observers < 0 || l.ObserverLimit is < 0 or > 128
                    || l.BotCount < 0 || l.BotCount > l.PlayerLimit || l.WaitlistCount < 0 || l.WaitlistCount > 1024))
                    throw new ArgumentException("Invalid lobby list.");
                break;
        }
    }
    private static void ValidateVoteEntry(LobbyVoteEntry option)
    {
        if (option == null || option.Id is < 1 or > 8 || option.Votes is < 0 or > 8)
            throw new ArgumentException("Invalid vote option.");
        ContractGuard.Defined(option.Choice); ContractGuard.Defined(option.Mode); ContractGuard.Text(option.MapKey, 128);
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
[JsonSerializable(typeof(NodeRoundSnapshot))]
[JsonSerializable(typeof(LobbyCreate))]
[JsonSerializable(typeof(LobbyList))]
[JsonSerializable(typeof(LobbyJoin))]
[JsonSerializable(typeof(LobbyQueueJoin))]
[JsonSerializable(typeof(LobbyQueueLeave))]
[JsonSerializable(typeof(LobbyQueueAccept))]
[JsonSerializable(typeof(LobbyQueueDecline))]
[JsonSerializable(typeof(LobbyLeave))]
[JsonSerializable(typeof(LobbySetReady))]
[JsonSerializable(typeof(LobbySelectHunter))]
[JsonSerializable(typeof(LobbyRequestTeam))]
[JsonSerializable(typeof(LobbyChat))]
[JsonSerializable(typeof(LobbyConfigure))]
[JsonSerializable(typeof(LobbyStart))]
[JsonSerializable(typeof(LobbyRematch))]
[JsonSerializable(typeof(LobbyReturn))]
[JsonSerializable(typeof(NodeMatchHandoff))]
[JsonSerializable(typeof(NodeMatchEnded))]
[JsonSerializable(typeof(NodeMatchRejoin))]
[JsonSerializable(typeof(LobbySnapshot))]
[JsonSerializable(typeof(LobbyListSnapshot))]
[JsonSerializable(typeof(NodeSessionSnapshot))]
[JsonSerializable(typeof(NodePing))]
[JsonSerializable(typeof(NodePong))]
[JsonSerializable(typeof(NodeControlError))]
[JsonSerializable(typeof(LobbyLeft))]
[JsonSerializable(typeof(NodeControlEvent))]
public partial class NodeJsonContext : JsonSerializerContext;
