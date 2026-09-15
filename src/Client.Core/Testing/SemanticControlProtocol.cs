using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MphRead.Mods.Testing;

/// <summary>
/// Development-only semantic controls. The command set is closed so a test
/// client can never turn this transport into a generic property setter,
/// reflection bridge, code runner, or arbitrary filesystem API.
/// </summary>
public enum SemanticControlCommand
{
    GetShellState,
    EnterGuestMode,
    OpenPlay,
    CreateLobby,
    JoinLobby,
    SetReady,
    SelectHunter,
    StartMatch,
    SubmitMovement,
    SubmitFire,
    VoteRematch,
    VoteReturnLobby,
    CaptureFrame,
    GetDiagnostics
}

public readonly record struct SemanticControlIdentity(
    string SessionId,
    string MatchId,
    string PhaseId)
{
    internal void Validate()
    {
        SemanticControlProtocol.ValidateIdentity(SessionId, nameof(SessionId));
        SemanticControlProtocol.ValidateIdentity(MatchId, nameof(MatchId));
        SemanticControlProtocol.ValidateIdentity(PhaseId, nameof(PhaseId));
    }
}

public sealed record SemanticControlRequest(
    int SchemaVersion,
    string CommandId,
    string SessionId,
    string MatchId,
    string PhaseId,
    SemanticControlCommand Command,
    IReadOnlyDictionary<string, JsonElement> Arguments)
{
    public SemanticControlIdentity Identity => new(SessionId, MatchId, PhaseId);
}

public sealed record SemanticControlResponse(
    int SchemaVersion,
    string CommandId,
    bool Accepted,
    string? Error,
    JsonElement? Payload)
{
    public static SemanticControlResponse Success(string commandId,
        JsonElement? payload = null)
        => new(SemanticControlProtocol.SchemaVersion, commandId, true, null, payload);

    public static SemanticControlResponse Rejected(string commandId, string error)
    {
        SemanticControlProtocol.ValidateCommandId(commandId);
        SemanticControlProtocol.ValidateError(error);
        return new(SemanticControlProtocol.SchemaVersion, commandId, false, error, null);
    }
}

public sealed record SemanticControlServerOptions(
    bool Enabled,
    string Endpoint,
    string TokenFile,
    int QueueCapacity = 64,
    int MaxLineBytes = SemanticControlProtocol.MaximumRequestBytes,
    int RequestTimeoutMilliseconds = 10_000,
    int MaxCapturePathBytes = 256)
{
    internal void Validate()
    {
        if (!Enabled) throw new InvalidOperationException(
            "Semantic controls are disabled; set the explicit development enable switch first.");
        SemanticControlProtocol.ValidateEndpoint(Endpoint);
        if (String.IsNullOrWhiteSpace(TokenFile)) throw new ArgumentException(
            "Semantic control token file is required.", nameof(TokenFile));
        if (QueueCapacity is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(QueueCapacity));
        if (MaxLineBytes is < 256 or > SemanticControlProtocol.MaximumRequestBytes)
            throw new ArgumentOutOfRangeException(nameof(MaxLineBytes));
        if (RequestTimeoutMilliseconds is < 1 or > 60_000)
            throw new ArgumentOutOfRangeException(nameof(RequestTimeoutMilliseconds));
        if (MaxCapturePathBytes is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(MaxCapturePathBytes));
    }
}

/// <summary>
/// Current phase identity owned by the shell/match coordinator. Mutating
/// requests are checked against it at dispatch time, so a delayed command from
/// a prior match or rematch phase cannot be replayed after the UI advances.
/// <see cref="SemanticControlCommand.GetShellState"/> is the one read-only
/// bootstrap exception: transport authentication plus strict identity syntax
/// are sufficient for that query, even before the client knows the current
/// phase value.
/// </summary>
public sealed class SemanticControlPhaseGuard
{
    private readonly object _gate = new();
    private SemanticControlIdentity _current;

    public SemanticControlPhaseGuard(SemanticControlIdentity initial)
    {
        initial.Validate();
        _current = initial;
    }

    public SemanticControlIdentity Current
    {
        get { lock (_gate) return _current; }
    }

    public void Advance(SemanticControlIdentity identity)
    {
        identity.Validate();
        lock (_gate) _current = identity;
    }

    public bool IsCurrent(SemanticControlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate) return request.Identity == _current;
    }

    public SemanticControlResponse Validate(SemanticControlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // A newly connected coordinator must be able to discover the current
        // shell/match/phase state before it has observed the current phase
        // token. This is deliberately limited to the read-only state query;
        // ParseRequest has already required all three identity fields to match
        // the bounded identity grammar.
        return request.Command == SemanticControlCommand.GetShellState
            || IsCurrent(request)
            ? SemanticControlResponse.Success(request.CommandId)
            : SemanticControlResponse.Rejected(request.CommandId, "stale-phase");
    }
}

/// <summary>
/// Routes closed command categories to their existing owners. The callbacks
/// are supplied by the shell and scene hosts; this type never mutates a Scene,
/// PlayerEntity, health value, lobby phase, or network snapshot itself.
/// </summary>
public sealed class SemanticControlDispatcher
{
    private readonly Func<SemanticControlRequest, CancellationToken,
        ValueTask<SemanticControlResponse>> _ui;
    private readonly Func<SemanticControlRequest, CancellationToken,
        ValueTask<SemanticControlResponse>> _gameplay;

    public SemanticControlDispatcher(
        Func<SemanticControlRequest, CancellationToken,
            ValueTask<SemanticControlResponse>> ui,
        Func<SemanticControlRequest, CancellationToken,
            ValueTask<SemanticControlResponse>> gameplay)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _gameplay = gameplay ?? throw new ArgumentNullException(nameof(gameplay));
    }

    public ValueTask<SemanticControlResponse> DispatchAsync(
        SemanticControlRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IsGameplay(request.Command)
            ? _gameplay(request, cancellationToken)
            : _ui(request, cancellationToken);
    }

    public static bool IsGameplay(SemanticControlCommand command)
        => command is SemanticControlCommand.SubmitMovement
            or SemanticControlCommand.SubmitFire
            or SemanticControlCommand.CaptureFrame;
}

/// <summary>Strict line protocol and bounded argument helpers.</summary>
public static partial class SemanticControlProtocol
{
    public const int SchemaVersion = 1;
    public const int MaximumRequestBytes = 16 * 1024;
    public const int MaximumResponseBytes = 64 * 1024;
    public const int MaximumTokenBytes = 256;
    // macOS sockaddr_un paths are limited to 104 bytes (Linux is slightly
    // larger). Keep one portable bound so the configured endpoint cannot pass
    // validation and then fail at bind/connect time on a supported desktop.
    public const int MaximumUnixEndpointBytes = 104;
    private const int MaximumArguments = 16;
    private const int MaximumText = 256;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.:-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommandIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.:/-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityPattern();

    private static readonly IReadOnlyDictionary<SemanticControlCommand,
        IReadOnlySet<string>> AllowedArguments = BuildAllowedArguments();

    public static SemanticControlRequest ParseRequest(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length is < 2 or > MaximumRequestBytes)
            throw new InvalidDataException("Semantic control request exceeds its line bound.");
        using JsonDocument document = ParseDocument(utf8, "request");
        JsonElement root = RequireObject(document.RootElement, "request");
        EnsureFields(root, "request", "schemaVersion", "commandId", "sessionId",
            "matchId", "phaseId", "command", "arguments");
        int schema = Int32(root, "schemaVersion", "request schemaVersion", SchemaVersion, SchemaVersion);
        string commandId = String(root, "commandId", "request commandId", MaximumText);
        ValidateCommandId(commandId);
        string session = String(root, "sessionId", "request sessionId", MaximumText);
        string match = String(root, "matchId", "request matchId", MaximumText);
        string phase = String(root, "phaseId", "request phaseId", MaximumText);
        ValidateIdentity(session, "sessionId");
        ValidateIdentity(match, "matchId");
        ValidateIdentity(phase, "phaseId");
        string commandText = String(root, "command", "request command", 64);
        if (!Enum.TryParse(commandText, ignoreCase: false, out SemanticControlCommand command)
            || !Enum.IsDefined(command))
            throw new InvalidDataException("Semantic control command is not in the closed command set.");
        JsonElement argumentsValue = Property(root, "arguments");
        JsonElement argumentsObject = RequireObject(argumentsValue, "request arguments");
        if (argumentsObject.EnumerateObject().Count() > MaximumArguments)
            throw new InvalidDataException("Semantic control arguments exceed their bound.");
        IReadOnlySet<string> allowed = AllowedArguments[command];
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in argumentsObject.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                throw new InvalidDataException($"Argument '{property.Name}' is not allowed for {command}.");
            ValidateArgumentValue(property.Value, property.Name);
            if (!arguments.TryAdd(property.Name, property.Value.Clone()))
                throw new InvalidDataException($"Duplicate semantic control argument: {property.Name}");
        }
        if (command == SemanticControlCommand.CaptureFrame
            && arguments.TryGetValue("label", out JsonElement label)
            && label.ValueKind == JsonValueKind.String)
            ValidateCaptureLabel(label.GetString()!);
        return new SemanticControlRequest(schema, commandId, session, match, phase, command, arguments);
    }

    public static SemanticControlResponse ParseResponse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length is < 2 or > MaximumResponseBytes)
            throw new InvalidDataException("Semantic control response exceeds its line bound.");
        using JsonDocument document = ParseDocument(utf8, "response");
        JsonElement root = RequireObject(document.RootElement, "response");
        EnsureFields(root, "response", "schemaVersion", "commandId", "accepted", "error", "payload");
        int schema = Int32(root, "schemaVersion", "response schemaVersion", SchemaVersion, SchemaVersion);
        string commandId = String(root, "commandId", "response commandId", MaximumText);
        ValidateCommandId(commandId);
        JsonElement acceptedValue = Property(root, "accepted");
        if (acceptedValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Response accepted must be a boolean.");
        bool accepted = acceptedValue.GetBoolean();
        JsonElement errorValue = Property(root, "error");
        string? error = errorValue.ValueKind == JsonValueKind.Null
            ? null : StringValue(errorValue, "response error", MaximumText);
        JsonElement payloadValue = Property(root, "payload");
        JsonElement? payload = payloadValue.ValueKind == JsonValueKind.Null
            ? null : payloadValue.Clone();
        if (accepted && error != null) throw new InvalidDataException(
            "Accepted semantic control responses cannot contain an error.");
        if (!accepted && error == null) throw new InvalidDataException(
            "Rejected semantic control responses require an error.");
        if (payload.HasValue) ValidatePayload(payload.Value, 0);
        return new SemanticControlResponse(schema, commandId, accepted, error, payload);
    }

    public static string SerializeRequest(SemanticControlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequestModel(request);
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = request.SchemaVersion,
            commandId = request.CommandId,
            sessionId = request.SessionId,
            matchId = request.MatchId,
            phaseId = request.PhaseId,
            command = request.Command.ToString(),
            arguments = request.Arguments
        }, JsonOptions());
        return BoundedLine(json, MaximumRequestBytes);
    }

    public static string SerializeResponse(SemanticControlResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        ValidateResponseModel(response);
        string json = JsonSerializer.Serialize(response, JsonOptions());
        return BoundedLine(json, MaximumResponseBytes);
    }

    public static bool TryGetString(SemanticControlRequest request, string name,
        out string value)
    {
        if (request.Arguments.TryGetValue(name, out JsonElement element)
            && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString()!;
            return true;
        }
        value = "";
        return false;
    }

    public static bool TryGetNumber(SemanticControlRequest request, string name,
        out double value)
    {
        if (request.Arguments.TryGetValue(name, out JsonElement element)
            && element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value)
            && Double.IsFinite(value)) return true;
        value = 0;
        return false;
    }

    public static bool TryGetBoolean(SemanticControlRequest request, string name,
        out bool value)
    {
        if (request.Arguments.TryGetValue(name, out JsonElement element)
            && element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }
        value = false;
        return false;
    }

    internal static void ValidateCommandId(string value)
    {
        if (!CommandIdPattern().IsMatch(value))
            throw new ArgumentException("Semantic control command ID is invalid.", nameof(value));
    }

    internal static void ValidateIdentity(string value, string name)
    {
        if (!IdentityPattern().IsMatch(value) || value.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException($"Semantic control {name} is invalid.", name);
    }

    internal static void ValidateError(string value)
    {
        if (System.String.IsNullOrWhiteSpace(value) || value.Length > MaximumText
            || value.Any(Char.IsControl)) throw new ArgumentException("Semantic control error is invalid.", nameof(value));
    }

    internal static void ValidateEndpoint(string endpoint)
    {
        if (System.String.IsNullOrWhiteSpace(endpoint) || endpoint.Length > 256
            || endpoint.Any(Char.IsControl)) throw new ArgumentException("Semantic control endpoint is invalid.", nameof(endpoint));
        if (OperatingSystem.IsWindows())
        {
            if (!Regex.IsMatch(endpoint, "^[A-Za-z0-9_-]{1,80}$", RegexOptions.CultureInvariant))
                throw new ArgumentException("Windows semantic control endpoint must be a bounded pipe name.", nameof(endpoint));
            return;
        }
        if (!Path.IsPathFullyQualified(endpoint)) throw new ArgumentException(
            "Unix semantic control endpoint must be an absolute socket path.", nameof(endpoint));
        if (Encoding.UTF8.GetByteCount(endpoint) > MaximumUnixEndpointBytes)
            throw new ArgumentException("Unix semantic control endpoint exceeds its portable byte bound.", nameof(endpoint));
        string normalized = Path.GetFullPath(endpoint);
        if (!StringComparer.Ordinal.Equals(normalized, endpoint)
            || normalized.Split(Path.DirectorySeparatorChar).Any(part => part == ".."))
            throw new ArgumentException("Unix semantic control endpoint must be normalized.", nameof(endpoint));
    }

    private static IReadOnlyDictionary<SemanticControlCommand, IReadOnlySet<string>> BuildAllowedArguments()
    {
        static IReadOnlySet<string> Set(params string[] values)
            => new HashSet<string>(values, StringComparer.Ordinal);
        return new Dictionary<SemanticControlCommand, IReadOnlySet<string>>
        {
            [SemanticControlCommand.GetShellState] = Set(),
            [SemanticControlCommand.EnterGuestMode] = Set(),
            [SemanticControlCommand.OpenPlay] = Set(),
            [SemanticControlCommand.CreateLobby] = Set("name", "playerLimit", "observerLimit"),
            [SemanticControlCommand.JoinLobby] = Set("lobbyId"),
            [SemanticControlCommand.SetReady] = Set("ready"),
            [SemanticControlCommand.SelectHunter] = Set("hunter"),
            [SemanticControlCommand.StartMatch] = Set(),
            [SemanticControlCommand.SubmitMovement] = Set("x", "y", "durationMs"),
            [SemanticControlCommand.SubmitFire] = Set("pressed", "weapon", "durationMs"),
            [SemanticControlCommand.VoteRematch] = Set("accept"),
            [SemanticControlCommand.VoteReturnLobby] = Set("accept"),
            [SemanticControlCommand.CaptureFrame] = Set("label"),
            [SemanticControlCommand.GetDiagnostics] = Set()
        };
    }

    private static JsonDocument ParseDocument(ReadOnlySpan<byte> utf8, string kind)
    {
        try
        {
            JsonDocument document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions
            {
                MaxDepth = 16,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            ValidateUnique(document.RootElement, 0);
            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Semantic control {kind} JSON is malformed.", ex);
        }
    }

    private static void ValidateUnique(JsonElement element, int depth)
    {
        if (depth > 16) throw new InvalidDataException("Semantic control JSON nesting exceeds its bound.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException(
                    $"Duplicate semantic control property: {property.Name}");
                ValidateUnique(property.Value, depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray()) ValidateUnique(item, depth + 1);
        }
    }

    private static void ValidateArgumentValue(JsonElement element, string name)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                _ = StringValue(element, $"argument {name}", MaximumText);
                return;
            case JsonValueKind.Number:
                if (!element.TryGetDouble(out double number) || !Double.IsFinite(number))
                    throw new InvalidDataException($"Argument {name} must be a finite number.");
                return;
            case JsonValueKind.True:
            case JsonValueKind.False:
                return;
            default:
                throw new InvalidDataException($"Argument {name} must be a bounded scalar.");
        }
    }

    private static void ValidateCaptureLabel(string label)
    {
        if (label.Contains("..", StringComparison.Ordinal)
            || label.Contains('/', StringComparison.Ordinal)
            || label.Contains('\\', StringComparison.Ordinal))
            throw new InvalidDataException("CaptureFrame labels cannot contain path separators or traversal.");
    }

    private static void ValidateRequestModel(SemanticControlRequest request)
    {
        if (request.SchemaVersion != SchemaVersion)
            throw new ArgumentException("Unsupported semantic control request schema.", nameof(request));
        ValidateCommandId(request.CommandId);
        request.Identity.Validate();
        if (!Enum.IsDefined(request.Command))
            throw new ArgumentException("Semantic control command is not in the closed command set.", nameof(request));
        if (request.Arguments == null || request.Arguments.Count > MaximumArguments)
            throw new ArgumentException("Semantic control arguments exceed their bound.", nameof(request));
        IReadOnlySet<string> allowed = AllowedArguments[request.Command];
        foreach ((string name, JsonElement value) in request.Arguments)
        {
            if (!allowed.Contains(name))
                throw new ArgumentException($"Argument '{name}' is not allowed for {request.Command}.", nameof(request));
            ValidateArgumentValue(value, name);
        }
    }

    private static void ValidateResponseModel(SemanticControlResponse response)
    {
        if (response.SchemaVersion != SchemaVersion)
            throw new ArgumentException("Unsupported semantic control response schema.", nameof(response));
        ValidateCommandId(response.CommandId);
        if (response.Accepted && response.Error != null)
            throw new ArgumentException("Accepted semantic control responses cannot contain an error.", nameof(response));
        if (!response.Accepted && response.Error == null)
            throw new ArgumentException("Rejected semantic control responses require an error.", nameof(response));
        if (response.Error != null) ValidateError(response.Error);
        if (response.Payload.HasValue) ValidatePayload(response.Payload.Value, 0);
    }

    private static void ValidatePayload(JsonElement element, int depth)
    {
        if (depth > 8) throw new InvalidDataException("Semantic control payload nesting exceeds its bound.");
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                _ = StringValue(element, "response payload string", MaximumText);
                return;
            case JsonValueKind.Number:
                if (!element.TryGetDouble(out double number) || !Double.IsFinite(number))
                    throw new InvalidDataException("Response payload contains a non-finite number.");
                return;
            case JsonValueKind.Object:
                if (element.EnumerateObject().Count() > 64) throw new InvalidDataException(
                    "Response payload object exceeds its field bound.");
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name.Length > MaximumText || property.Name.Any(Char.IsControl))
                        throw new InvalidDataException("Response payload property name is invalid.");
                    ValidatePayload(property.Value, depth + 1);
                }
                return;
            case JsonValueKind.Array:
                if (element.GetArrayLength() > 256) throw new InvalidDataException(
                    "Response payload array exceeds its bound.");
                foreach (JsonElement item in element.EnumerateArray()) ValidatePayload(item, depth + 1);
                return;
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                return;
            default:
                throw new InvalidDataException("Response payload value is unsupported.");
        }
    }

    private static JsonElement RequireObject(JsonElement element, string kind)
        => element.ValueKind == JsonValueKind.Object ? element : throw new InvalidDataException(
            $"Semantic control {kind} must be an object.");

    private static void EnsureFields(JsonElement objectValue, string kind,
        params string[] expected)
    {
        var allowed = new HashSet<string>(expected, StringComparer.Ordinal);
        foreach (JsonProperty property in objectValue.EnumerateObject())
            if (!allowed.Contains(property.Name)) throw new InvalidDataException(
                $"Unknown semantic control {kind} field: {property.Name}");
        foreach (string name in expected)
            if (!objectValue.TryGetProperty(name, out _)) throw new InvalidDataException(
                $"Missing semantic control {kind} field: {name}");
    }

    private static JsonElement Property(JsonElement objectValue, string name)
        => objectValue.TryGetProperty(name, out JsonElement value) ? value
            : throw new InvalidDataException($"Missing semantic control field: {name}");

    private static int Int32(JsonElement objectValue, string name, string kind,
        int minimum, int maximum)
    {
        JsonElement value = Property(objectValue, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number)
            || number < minimum || number > maximum)
            throw new InvalidDataException($"{kind} must be an integer in range.");
        return number;
    }

    private static string String(JsonElement objectValue, string name, string kind, int maximum)
        => StringValue(Property(objectValue, name), kind, maximum);

    private static string StringValue(JsonElement value, string kind, int maximum)
    {
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException(
            $"{kind} must be a string.");
        string? text = value.GetString();
        if (System.String.IsNullOrWhiteSpace(text) || text.Length > maximum
            || text.Any(Char.IsControl)) throw new InvalidDataException(
                $"{kind} is empty, too long, or contains control characters.");
        return text;
    }

    private static string BoundedLine(string json, int maximum)
    {
        if (Encoding.UTF8.GetByteCount(json) + 1 > maximum)
            throw new InvalidDataException("Semantic control line exceeds its byte bound.");
        return json + "\n";
    }

    private static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.Strict
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}

/// <summary>
/// Token-file reader. The token is never included in request/response models
/// or logs, and the Unix path is required to be owner-readable only.
/// </summary>
public static class SemanticControlToken
{
    public static string ReadRestricted(string path)
    {
        string full = Path.GetFullPath(path);
        FileInfo info = new(full);
        if (!info.Exists || info.LinkTarget != null
            || (info.Attributes & FileAttributes.ReparsePoint) != 0
            || info.Length is < 1 or > SemanticControlProtocol.MaximumTokenBytes)
            throw new InvalidDataException("Semantic control token file must be a small regular file.");
        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode mode = File.GetUnixFileMode(full);
            UnixFileMode groupOrOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite
                | UnixFileMode.GroupExecute | UnixFileMode.OtherRead
                | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            if ((mode & groupOrOther) != 0) throw new UnauthorizedAccessException(
                "Semantic control token file must be owner-only on Unix.");
        }
        byte[] bytes = File.ReadAllBytes(full);
        string value;
        try { value = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Semantic control token is not UTF-8.", ex); }
        value = value.TrimEnd('\r', '\n');
        if (System.String.IsNullOrWhiteSpace(value) || value.Length > SemanticControlProtocol.MaximumTokenBytes
            || value.Any(Char.IsControl)) throw new InvalidDataException("Semantic control token is invalid.");
        return value;
    }

    public static bool Equals(string expected, ReadOnlySpan<byte> presentedUtf8)
    {
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, presentedUtf8);
    }
}

/// <summary>
/// One bounded local transport. It accepts one client at a time, authenticates
/// with the restricted per-run token, gives each request a bounded lifetime,
/// queues requests in a bounded channel, and cancels all pending work on
/// shutdown. Dispatch owners must honor the request cancellation token.
/// </summary>
public sealed class SemanticControlServer : IAsyncDisposable
{
    private sealed class Pending : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly CancellationToken _token;

        public Pending(SemanticControlRequest request,
            TaskCompletionSource<SemanticControlResponse> completion,
            TimeSpan timeout, CancellationToken shutdown)
        {
            Request = request;
            Completion = completion;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
            _token = _cancellation.Token;
            _cancellation.CancelAfter(timeout);
        }

        public SemanticControlRequest Request { get; }
        public TaskCompletionSource<SemanticControlResponse> Completion { get; }
        public CancellationToken CancellationToken => _token;

        public void Dispose() => _cancellation.Dispose();
    }

    private readonly SemanticControlServerOptions _options;
    private readonly SemanticControlPhaseGuard _phaseGuard;
    private readonly Func<SemanticControlRequest, CancellationToken,
        ValueTask<SemanticControlResponse>> _dispatch;
    private readonly string _token;
    private readonly Channel<Pending> _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private Task? _runTask;
    private Socket? _listener;
    private bool _ownsUnixSocket;

    public SemanticControlServer(SemanticControlServerOptions options,
        SemanticControlPhaseGuard phaseGuard,
        Func<SemanticControlRequest, CancellationToken,
            ValueTask<SemanticControlResponse>> dispatch)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _phaseGuard = phaseGuard ?? throw new ArgumentNullException(nameof(phaseGuard));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _token = SemanticControlToken.ReadRestricted(options.TokenFile);
        _queue = Channel.CreateBounded<Pending>(new BoundedChannelOptions(options.QueueCapacity)
        {
            // The transport must be able to report queue-full rather than
            // silently dropping a command whose caller is waiting for a
            // response.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public Task Completion => _runTask ?? Task.CompletedTask;

    public void Start()
    {
        lock (_gate)
        {
            if (_runTask != null) throw new InvalidOperationException("Semantic control server already started.");
            _runTask = Task.WhenAll(AcceptLoopAsync(_shutdown.Token), DispatchLoopAsync(_shutdown.Token));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _queue.Writer.TryComplete();
        _listener?.Dispose();
        CancelPending();
        Task? run;
        lock (_gate) run = _runTask;
        if (run != null)
        {
            try { await run.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        if (_ownsUnixSocket && !OperatingSystem.IsWindows())
        {
            try { File.Delete(_options.Endpoint); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        _shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(_options.Endpoint,
                    PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                await ServeStreamAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        string endpoint = _options.Endpoint;
        string? parent = Path.GetDirectoryName(endpoint);
        if (String.IsNullOrWhiteSpace(parent)) throw new InvalidDataException(
            "Semantic control Unix socket has no parent directory.");
        DirectoryInfo parentInfo = new(parent);
        if (!parentInfo.Exists || parentInfo.LinkTarget != null
            || (parentInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Semantic control socket parent must be an existing regular directory.");
        if (File.Exists(endpoint) || Directory.Exists(endpoint)) throw new IOException(
            "Semantic control socket endpoint already exists; refusing to replace it.");
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener = listener;
        listener.Bind(new UnixDomainSocketEndPoint(endpoint));
        _ownsUnixSocket = true;
        try { File.SetUnixFileMode(endpoint, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        catch (PlatformNotSupportedException) { }
        listener.Listen(1);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Socket client;
                try { client = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                await using NetworkStream stream = new(client, ownsSocket: true);
                await ServeStreamAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { listener.Dispose(); }
    }

    private async Task ServeStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[]? tokenLine = await ReadLineAsync(stream, SemanticControlProtocol.MaximumTokenBytes,
            cancellationToken).ConfigureAwait(false);
        if (tokenLine == null || !SemanticControlToken.Equals(_token, tokenLine)) return;
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[]? line = await ReadLineAsync(stream, _options.MaxLineBytes,
                cancellationToken).ConfigureAwait(false);
            if (line == null) return;
            SemanticControlRequest request;
            try { request = SemanticControlProtocol.ParseRequest(line); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
            {
                // No command ID is trusted until strict parsing succeeds, so
                // malformed lines receive no response that could be confused
                // with an accepted operation.
                return;
            }
            if (!ValidateCaptureLabel(request, _options.MaxCapturePathBytes,
                    out string? captureError))
            {
                SemanticControlResponse invalidCapture = SemanticControlResponse.Rejected(
                    request.CommandId, captureError!);
                await WriteLineAsync(stream, SemanticControlProtocol.SerializeResponse(invalidCapture),
                    cancellationToken).ConfigureAwait(false);
                continue;
            }
            SemanticControlResponse phase = _phaseGuard.Validate(request);
            if (!phase.Accepted)
            {
                await WriteLineAsync(stream, SemanticControlProtocol.SerializeResponse(phase),
                    cancellationToken).ConfigureAwait(false);
                continue;
            }
            var completion = new TaskCompletionSource<SemanticControlResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new Pending(request, completion,
                TimeSpan.FromMilliseconds(_options.RequestTimeoutMilliseconds),
                cancellationToken);
            if (!_queue.Writer.TryWrite(pending))
            {
                pending.Dispose();
                SemanticControlResponse full = SemanticControlResponse.Rejected(
                    request.CommandId, "queue-full");
                await WriteLineAsync(stream, SemanticControlProtocol.SerializeResponse(full),
                    cancellationToken).ConfigureAwait(false);
                continue;
            }
            SemanticControlResponse response;
            try
            {
                response = await completion.Task.WaitAsync(
                    pending.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
                when (pending.CancellationToken.IsCancellationRequested)
            {
                response = SemanticControlResponse.Rejected(request.CommandId, "dispatch-timeout");
            }
            await WriteLineAsync(stream, SemanticControlProtocol.SerializeResponse(response),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (Pending pending in _queue.Reader.ReadAllAsync(cancellationToken))
            {
                SemanticControlResponse response;
                try
                {
                    if (pending.CancellationToken.IsCancellationRequested)
                    {
                        response = SemanticControlResponse.Rejected(
                            pending.Request.CommandId, "dispatch-timeout");
                    }
                    else
                    {
                        // Re-check after queue delay: a rematch/phase transition
                        // may have happened while another command was dispatched.
                        SemanticControlResponse phase = _phaseGuard.Validate(pending.Request);
                        if (!phase.Accepted)
                        {
                            response = phase;
                        }
                        else
                        {
                            // Owner callbacks must observe this token. The wait
                            // also bounds a misbehaving callback so it cannot hold
                            // the single dispatch reader past the request deadline.
                            ValueTask<SemanticControlResponse> dispatch
                                = _dispatch(pending.Request, pending.CancellationToken);
                            response = await dispatch.AsTask().WaitAsync(
                                pending.CancellationToken).ConfigureAwait(false);
                            if (pending.CancellationToken.IsCancellationRequested)
                            {
                                response = SemanticControlResponse.Rejected(
                                    pending.Request.CommandId, "dispatch-timeout");
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    response = SemanticControlResponse.Rejected(pending.Request.CommandId, "shutdown");
                }
                catch (OperationCanceledException)
                    when (pending.CancellationToken.IsCancellationRequested)
                {
                    response = SemanticControlResponse.Rejected(
                        pending.Request.CommandId, "dispatch-timeout");
                }
                catch (Exception)
                {
                    response = SemanticControlResponse.Rejected(pending.Request.CommandId, "dispatch-failed");
                }
                pending.Completion.TrySetResult(response);
                pending.Dispose();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { CancelPending(); }
    }

    private void CancelPending()
    {
        while (_queue.Reader.TryRead(out Pending? pending))
        {
            pending.Dispose();
            pending.Completion.TrySetResult(SemanticControlResponse.Rejected(
                pending.Request.CommandId, "shutdown"));
        }
    }

    private static async Task<byte[]?> ReadLineAsync(Stream stream, int maximum,
        CancellationToken cancellationToken)
    {
        var result = new List<byte>(Math.Min(maximum, 1024));
        byte[] one = new byte[1];
        // The line bound includes its terminating newline. Reserve one byte
        // for it so a correctly bounded line is not rejected at the edge.
        while (result.Count < maximum - 1)
        {
            int count = await stream.ReadAsync(one.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (count == 0) return result.Count == 0 ? null : throw new InvalidDataException(
                "Semantic control stream ended mid-line.");
            if (one[0] == (byte)'\n')
            {
                if (result.Count > 0 && result[^1] == (byte)'\r') result.RemoveAt(result.Count - 1);
                return result.ToArray();
            }
            result.Add(one[0]);
        }
        throw new InvalidDataException("Semantic control line exceeds its byte bound.");
    }

    private static bool ValidateCaptureLabel(SemanticControlRequest request,
        int maximumBytes, out string? error)
    {
        error = null;
        if (request.Command != SemanticControlCommand.CaptureFrame
            || !SemanticControlProtocol.TryGetString(request, "label", out string label))
            return true;
        if (Encoding.UTF8.GetByteCount(label) > maximumBytes
            || label.Contains("..", StringComparison.Ordinal)
            || label.Contains('/', StringComparison.Ordinal)
            || label.Contains('\\', StringComparison.Ordinal))
        {
            error = "capture-label-invalid";
            return false;
        }
        return true;
    }

    private static async Task WriteLineAsync(Stream stream, string value,
        CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Small test/client helper for the same authenticated line protocol.</summary>
public static class SemanticControlClient
{
    public static async Task<SemanticControlResponse> SendAsync(
        SemanticControlServerOptions options, string token,
        SemanticControlRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(request);
        options.Validate();
        if (Encoding.UTF8.GetByteCount(token) > SemanticControlProtocol.MaximumTokenBytes)
            throw new ArgumentException("Semantic control token exceeds its bound.", nameof(token));
        if (String.IsNullOrWhiteSpace(token) || token.Any(Char.IsControl))
            throw new ArgumentException("Semantic control token is invalid.", nameof(token));
        await using Stream stream = await ConnectAsync(options.Endpoint, cancellationToken).ConfigureAwait(false);
        await WriteLineAsync(stream, token + "\n", SemanticControlProtocol.MaximumTokenBytes,
            cancellationToken).ConfigureAwait(false);
        await WriteLineAsync(stream, SemanticControlProtocol.SerializeRequest(request),
            options.MaxLineBytes, cancellationToken).ConfigureAwait(false);
        byte[]? responseLine = await ReadLineAsync(stream, SemanticControlProtocol.MaximumResponseBytes,
            cancellationToken).ConfigureAwait(false);
        if (responseLine == null) throw new InvalidDataException("Semantic control server closed the connection.");
        return SemanticControlProtocol.ParseResponse(responseLine);
    }

    private static async Task<Stream> ConnectAsync(string endpoint,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", endpoint, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return pipe;
        }
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint), cancellationToken)
            .ConfigureAwait(false);
        return new NetworkStream(socket, ownsSocket: true);
    }

    private static async Task WriteLineAsync(Stream stream, string value, int maximum,
        CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > maximum) throw new InvalidDataException(
            "Semantic control client line exceeds its byte bound.");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadLineAsync(Stream stream, int maximum,
        CancellationToken cancellationToken)
    {
        var result = new List<byte>(Math.Min(maximum, 1024));
        byte[] one = new byte[1];
        // The line bound includes its terminating newline. Reserve one byte
        // for it so a correctly bounded line is not rejected at the edge.
        while (result.Count < maximum - 1)
        {
            int count = await stream.ReadAsync(one.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (count == 0) return result.Count == 0 ? null : throw new InvalidDataException(
                "Semantic control response ended mid-line.");
            if (one[0] == (byte)'\n')
            {
                if (result.Count > 0 && result[^1] == (byte)'\r') result.RemoveAt(result.Count - 1);
                return result.ToArray();
            }
            result.Add(one[0]);
        }
        throw new InvalidDataException("Semantic control response exceeds its byte bound.");
    }
}
