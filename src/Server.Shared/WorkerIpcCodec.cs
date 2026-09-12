using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ProjectPrime.Server.Shared;

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
        typeof(MatchAdminResult),
        // Append-only: never insert a new Worker IPC type before an existing one.
        typeof(InstallAdmissionKey),
        typeof(AdmissionKeyInstalled),
        typeof(AdmissionKeyInstallFailed)
    ];

    public static byte[] Encode(WorkerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Validate(message);
        int index = Array.IndexOf(Types, message.GetType());
        if (index < 0) throw new ArgumentException("Unknown worker message.");

        // Serialize the envelope directly into one growable buffer. The old
        // path allocated a payload array, a MemoryStream backing array, a
        // second envelope array, and finally the framed result. Keeping the
        // type byte and envelope in the same writer preserves the exact wire
        // order while removing the intermediate payload/envelope copies.
        var buffer = new ArrayBufferWriter<byte>(256);
        Span<byte> header = buffer.GetSpan(5);
        header[4] = (byte)(index + 1);
        buffer.Advance(5);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject(); writer.WriteNumber("version", Version);
            writer.WritePropertyName("payload");
            JsonSerializer.Serialize(writer, message, message.GetType(), Options);
            writer.WriteEndObject();
        }
        int bodyLength = checked(buffer.WrittenCount - 4);
        if (bodyLength > MaxFrameLength) throw new InvalidDataException("IPC frame exceeds limit.");
        byte[] frame = buffer.WrittenSpan.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)bodyLength);
        return frame;
    }

    public static WorkerMessage Decode(byte[] frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return Decode((ReadOnlyMemory<byte>)frame);
    }

    public static WorkerMessage Decode(ReadOnlySpan<byte> frame)
        => Decode((ReadOnlyMemory<byte>)frame.ToArray());

    /// <summary>
    /// Decodes an already-owned frame without copying it before JSON parsing.
    /// The returned message is fully materialized before the document is
    /// disposed, so the caller may safely return pooled frame storage.
    /// </summary>
    public static WorkerMessage Decode(ReadOnlyMemory<byte> frame)
    {
        if (frame.Length < 5) throw new InvalidDataException("Truncated frame.");
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(frame.Span);
        if (length < 2 || length > MaxFrameLength || length != frame.Length - 4)
            throw new InvalidDataException("Invalid frame length.");
        byte type = frame.Span[4];
        if (type < 1 || type > Types.Length) throw new InvalidDataException("Unknown message type.");
        try
        {
            using var document = JsonDocument.Parse(frame.Slice(5), new JsonDocumentOptions { MaxDepth = 32 });
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
        // A single bounded pooled buffer holds both prefix and body. Invalid
        // lengths are rejected before any payload-sized storage is exposed,
        // and Decode(ReadOnlyMemory<byte>) parses it without another copy.
        byte[] frame = ArrayPool<byte>.Shared.Rent(MaxFrameLength + 4);
        int clearLength = 0;
        try
        {
            int first = await stream.ReadAsync(frame.AsMemory(0, 1), cancellationToken);
            if (first == 0) return null;
            clearLength = 4;
            await stream.ReadExactlyAsync(frame.AsMemory(1, 3), cancellationToken);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(frame);
            if (length < 2 || length > MaxFrameLength) throw new InvalidDataException("Invalid frame length.");
            int frameLength = checked((int)length + 4);
            clearLength = frameLength;
            await stream.ReadExactlyAsync(frame.AsMemory(4, (int)length), cancellationToken);
            return Decode(frame.AsMemory(0, frameLength));
        }
        finally
        {
            // Frames can contain short-lived admission secrets. Do not leave
            // them resident in the shared pool for a later IPC reader. Clear
            // only the bytes this read could have populated; clearing the
            // entire 64 KiB pool bucket on every control message defeats the
            // allocation optimization with avoidable memory bandwidth.
            if (clearLength > 0)
                Array.Clear(frame, 0, clearLength);
            ArrayPool<byte>.Shared.Return(frame, clearArray: false);
        }
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
            case MatchAdminCommand m:
                ContractGuard.Id(m.MatchId.Value); ContractGuard.Defined(m.Action);
                bool needsSeat = m.Action is AdminAction.KickSeat or AdminAction.LagCompHistory
                    or AdminAction.LagCompDynamic or AdminAction.LagCompClear;
                if (m.SeatId >= 32 || needsSeat != m.SeatId.HasValue) throw new ArgumentException("Invalid admin target.");
                break;
            case UpdateNodeSigningKey m: ContractGuard.Text(m.KeyId, 128); ContractGuard.Text(m.PublicKey, 8192); break;
            case InstallAdmissionKey m:
                ValidateAdmissionKeyBinding(m.AdmissionId, m.TicketId, m.NodeSessionId, m.NodeId, m.NodeIncarnation,
                    m.MatchId, m.WireMatchId, m.WorkerId, m.WorkerIncarnation, m.SeatId, m.JoinNonce, m.ExpiresAt);
                AdmissionKeyRules.Validate(m.AdmissionKey);
                break;
            case MatchStarted m: ContractGuard.Id(m.MatchId.Value); break;
            case MatchCompleted m: if (m.Summary is null) throw new ArgumentException("Missing completion summary."); m.Summary.Validate(); break;
            case MatchFailed m: ContractGuard.Id(m.MatchId.Value); ContractGuard.Text(m.Reason, 1024); break;
            case MatchInterrupted m: ContractGuard.Id(m.MatchId.Value); ContractGuard.Text(m.Reason, 1024); break;
            case MatchReportReady m: m.Validate(); break;
            case WorkerDraining m: ContractGuard.Id(m.WorkerId.Value); ContractGuard.Id(m.WorkerIncarnation); break;
            case WorkerFault m: ContractGuard.Id(m.WorkerId.Value); ContractGuard.Id(m.WorkerIncarnation); ContractGuard.Text(m.Reason, 1024); break;
            case AdmissionKeyInstalled m:
                ValidateAdmissionKeyBinding(m.AdmissionId, m.TicketId, m.NodeSessionId, m.NodeId, m.NodeIncarnation,
                    m.MatchId, m.WireMatchId, m.WorkerId, m.WorkerIncarnation, m.SeatId, m.JoinNonce, m.ExpiresAt);
                break;
            case AdmissionKeyInstallFailed m:
                ContractGuard.Id(m.AdmissionId); ContractGuard.Id(m.MatchId.Value); ContractGuard.Text(m.Reason, 256);
                break;
            default: throw new ArgumentException("Unknown message.");
        }
    }
    private static void ValidateAdmissionKeyBinding(Guid admissionId, Guid ticketId, Guid nodeSessionId,
        NodeId nodeId, Guid nodeIncarnation, MatchId matchId, WireMatchId wireMatchId,
        WorkerId workerId, Guid workerIncarnation, byte seatId, ulong joinNonce, long expiresAt)
    {
        ContractGuard.Id(admissionId); ContractGuard.Id(ticketId); ContractGuard.Id(nodeSessionId);
        ContractGuard.Id(nodeId.Value); ContractGuard.Id(nodeIncarnation); ContractGuard.Id(matchId.Value);
        ContractGuard.Id(workerId.Value); ContractGuard.Id(workerIncarnation);
        if (wireMatchId.Value == 0 || seatId >= 32 || joinNonce == 0 || expiresAt <= 0)
            throw new ArgumentException("Invalid admission-key binding.");
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
[JsonSerializable(typeof(InstallAdmissionKey))]
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
[JsonSerializable(typeof(AdmissionKeyInstalled))]
[JsonSerializable(typeof(AdmissionKeyInstallFailed))]
[JsonSerializable(typeof(MatchAdminResult))]
[JsonSerializable(typeof(NodeMatchSummary))]
internal partial class WorkerJsonContext : JsonSerializerContext;
