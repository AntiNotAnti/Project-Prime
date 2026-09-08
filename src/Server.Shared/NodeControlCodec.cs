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
        var info = (JsonTypeInfo<T>?)NodeJsonContext.Default.GetTypeInfo(typeof(T)) ?? throw new ArgumentException("Unknown event payload.");
        var message = new NodeControlEvent(Version, type, eventId, requestId, JsonSerializer.SerializeToElement(payload, info));
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, NodeJsonContext.Default.NodeControlEvent);
        if (bytes.Length > MaximumFrameBytes) throw new InvalidOperationException("Control event exceeds frame limit.");
        return bytes;
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
