using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace FruityPrime.Server.Shared;

/// <summary>V1: little-endian uint32 body length (excludes prefix), byte type,
/// then JSON {version,payload}. Caller serializes writes and authenticates the local pipe.
/// Invalid frames are terminal: close the connection, do not attempt resynchronization.</summary>
public static class WorkerIpcCodec
{
    public const int Version = 1;
    public const int MaxFrameLength = 64 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        MaxDepth = 32,
        TypeInfoResolver = WorkerJsonContext.Default
    };
    private static readonly Type[] Types = [
        typeof(WorkerConfigure),
        typeof(CreateMatch),
        typeof(CancelMatch),
        typeof(Drain),
        typeof(Shutdown),
        typeof(MatchAdminCommand),
        typeof(UpdateNodeSigningKey),
        typeof(WorkerHello),
        typeof(WorkerReady),
        typeof(WorkerHeartbeat),
        typeof(MatchReady),
        typeof(MatchStarted),
        typeof(MatchCompleted),
        typeof(MatchFailed),
        typeof(MatchInterrupted),
        typeof(MatchReportReady),
        typeof(WorkerDraining),
        typeof(WorkerFault),
        typeof(MatchAdminResult)
    ];

    public static byte[] Encode(WorkerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Validate(message);
        int index = Array.IndexOf(Types, message.GetType());
        if (index < 0) throw new ArgumentException("Unknown worker message.");
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), Options);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject(); writer.WriteNumber("version", Version);
            writer.WritePropertyName("payload"); writer.WriteRawValue(payload); writer.WriteEndObject();
        }
        if (buffer.Length + 1 > MaxFrameLength) throw new InvalidDataException("IPC frame exceeds limit.");
        var frame = new byte[checked((int)buffer.Length + 5)];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)buffer.Length + 1);
        frame[4] = (byte)(index + 1); buffer.ToArray().CopyTo(frame, 5);
        return frame;
    }

    public static WorkerMessage Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 5) throw new InvalidDataException("Truncated frame.");
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(frame);
        if (length < 2 || length > MaxFrameLength || length != frame.Length - 4)
            throw new InvalidDataException("Invalid frame length.");
        byte type = frame[4];
        if (type < 1 || type > Types.Length) throw new InvalidDataException("Unknown message type.");
        try
        {
            using var document = JsonDocument.Parse(frame[5..].ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            RejectDuplicates(document.RootElement);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2
                || !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out int value)
                || value != Version || !root.TryGetProperty("payload", out var payload))
                throw new InvalidDataException("Invalid envelope or IPC version mismatch.");
            var message = (WorkerMessage?)payload.Deserialize(Types[type - 1], Options)
                ?? throw new InvalidDataException("Null worker message.");
            Validate(message);
            return message;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        { throw new InvalidDataException("Malformed worker payload.", error); }
    }

    /// <summary>Clean EOF before a prefix returns null. Any partial frame fails.
    /// Cancellation/deadlines are controlled by the authenticated transport owner.</summary>
    public static async ValueTask<WorkerMessage?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] prefix = new byte[4];
        int first = await stream.ReadAsync(prefix.AsMemory(0, 1), cancellationToken);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(prefix.AsMemory(1), cancellationToken);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
        if (length < 2 || length > MaxFrameLength) throw new InvalidDataException("Invalid frame length.");
        byte[] frame = new byte[checked((int)length + 4)];
        prefix.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(4), cancellationToken);
        return Decode(frame);
    }

    public static async ValueTask WriteAsync(Stream stream, WorkerMessage message, CancellationToken cancellationToken = default)
        => await stream.WriteAsync(Encode(message), cancellationToken);

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
    }

    private static void Validate(WorkerMessage message)
    {
        switch (message)
        {
            case CreateMatch m: if (m.Spec is null) throw new ArgumentException("Missing spec."); m.Spec.Validate(); break;
            case WorkerConfigure m: ContractGuard.Id(m.NodeId.Value); ContractGuard.Id(m.NodeIncarnation); Capacity(m.Capacity); break;
            case WorkerHello m: ContractGuard.Id(m.WorkerId.Value); ContractGuard.Id(m.WorkerIncarnation); ContractGuard.Id(m.NodeId.Value); ContractGuard.Text(m.StartupToken, 256); ContractGuard.Text(m.BuildVersion, 128); m.Content?.Validate(); break;
            case WorkerReady m: ContractGuard.Id(m.WorkerId.Value); ContractGuard.Id(m.WorkerIncarnation); Capacity(m.Capacity); break;
            case WorkerHeartbeat m: ContractGuard.Id(m.WorkerId.Value); ContractGuard.Id(m.WorkerIncarnation); Capacity(m.Capacity); if (m.Health is null) throw new ArgumentException("Missing health."); m.Health.Validate(); break;
            case MatchReady m:
                if (m.Placement is null) throw new ArgumentException("Missing placement.");
                m.Placement.Validate(); break;
            case CancelMatch m: ContractGuard.Id(m.MatchId.Value); ContractGuard.Text(m.Reason, 1024); break;
            case Drain m: ContractGuard.Text(m.Reason, 1024); break;
            case Shutdown m: ContractGuard.Text(m.Reason, 1024); break;
            case MatchAdminResult m: ContractGuard.Id(m.MatchId.Value); ContractGuard.Defined(m.Action);
                if (m.Code != null) ContractGuard.Text(m.Code, 128);
                if (m.Message != null) ContractGuard.Text(m.Message, 1024);
                break;
            case MatchAdminCommand m: ContractGuard.Id(m.MatchId.Value); ContractGuard.Defined(m.Action); if (m.SeatId >= 32 || (m.Action == AdminAction.KickSeat) != m.SeatId.HasValue) throw new ArgumentException("Invalid admin target."); break;
            case UpdateNodeSigningKey m: ContractGuard.Text(m.KeyId, 128); ContractGuard.Text(m.PublicKey, 8192); break;
            case MatchStarted m: ContractGuard.Id(m.MatchId.Value); break;
            case MatchCompleted m: if (m.Summary is null) throw new ArgumentException("Missing completion summary."); m.Summary.Validate(); break;
            case MatchFailed m: ContractGuard.Id(m.MatchId.Value); ContractGuard.Text(m.Reason, 1024); break;
            case MatchInterrupted m: ContractGuard.Id(m.MatchId.Value); ContractGuard.Text(m.Reason, 1024); break;
            case MatchReportReady m: m.Validate(); break;
            case WorkerDraining m: ContractGuard.Id(m.WorkerId.Value); ContractGuard.Id(m.WorkerIncarnation); break;
            case WorkerFault m: ContractGuard.Id(m.WorkerId.Value); ContractGuard.Id(m.WorkerIncarnation); ContractGuard.Text(m.Reason, 1024); break;
            default: throw new ArgumentException("Unknown message.");
        }
    }
    private static void Capacity(WorkerCapacity? capacity)
    { if (capacity is null) throw new ArgumentException("Missing capacity."); capacity.Validate(); }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WorkerConfigure))]
[JsonSerializable(typeof(CreateMatch))]
[JsonSerializable(typeof(CancelMatch))]
[JsonSerializable(typeof(Drain))]
[JsonSerializable(typeof(Shutdown))]
[JsonSerializable(typeof(MatchAdminCommand))]
[JsonSerializable(typeof(UpdateNodeSigningKey))]
[JsonSerializable(typeof(WorkerHello))]
[JsonSerializable(typeof(WorkerReady))]
[JsonSerializable(typeof(WorkerHeartbeat))]
[JsonSerializable(typeof(MatchReady))]
[JsonSerializable(typeof(MatchStarted))]
[JsonSerializable(typeof(MatchCompleted))]
[JsonSerializable(typeof(MatchFailed))]
[JsonSerializable(typeof(MatchInterrupted))]
[JsonSerializable(typeof(MatchReportReady))]
[JsonSerializable(typeof(WorkerDraining))]
[JsonSerializable(typeof(WorkerFault))]
[JsonSerializable(typeof(MatchAdminResult))]
[JsonSerializable(typeof(NodeMatchSummary))]
internal partial class WorkerJsonContext : JsonSerializerContext;
