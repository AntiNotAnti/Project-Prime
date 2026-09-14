using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;

namespace ProjectPrime.Server.Node.Admin;

/// <summary>
/// Optional local-operator credential for host-only HTTPS administration.
/// Only a SHA-256 digest is retained after startup and the endpoint is not
/// mapped at all when no explicit token file is configured.
/// </summary>
internal sealed class NodeHostAdminAuthorization
{
    private readonly byte[]? _tokenHash;
    private NodeHostAdminAuthorization(byte[]? tokenHash) => _tokenHash = tokenHash;
    internal bool Enabled => _tokenHash != null;

    internal static NodeHostAdminAuthorization Load(IConfiguration configuration)
    {
        string? path = configuration["Node:HostAdmin:TokenFile"];
        if (string.IsNullOrWhiteSpace(path)) return new(null);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 32 or > 512)
            throw new InvalidOperationException("Node host administration token file is missing or invalid.");
        string token = File.ReadAllText(file.FullName).Trim();
        if (token.Length is < 32 or > 256 || token.Any(ch => ch > 127 || char.IsWhiteSpace(ch)))
            throw new InvalidOperationException("Node host administration token must be 32..256 printable ASCII characters.");
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(token));
        return new(hash);
    }

    internal bool Authorize(HttpRequest request)
    {
        string value = request.Headers.Authorization.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.Ordinal) || value.Length > 263)
            return false;
        string token = value[7..];
        if (token.Length is < 32 or > 256 || token.Any(ch => ch > 127 || char.IsWhiteSpace(ch)))
            return false;
        Span<byte> candidate = stackalloc byte[32];
        SHA256.HashData(Encoding.ASCII.GetBytes(token), candidate);
        return CryptographicOperations.FixedTimeEquals(candidate, _tokenHash!);
    }
}

internal static class NodeHostAdminEndpoints
{
    private const int MaximumRequestBytes = 512;

    internal sealed record LifecycleDiagnosticsResponse(DateTimeOffset CapturedAt,
        IReadOnlyList<LobbyLifecycleDiagnosticsSnapshot> Lobbies,
        IReadOnlyList<CoordinatorLifecycleDiagnosticsSnapshot> Matches,
        IReadOnlyList<WorkerPlacementDiagnosticsSnapshot> Placements);

    internal static IResult GetLifecycleDiagnostics(HttpContext context,
        NodeHostAdminAuthorization authorization, LobbyManager lobbies,
        NodeMatchCoordinator coordinator, WorkerScheduler scheduler,
        TimeProvider clock)
    {
        if (!context.Request.IsHttps) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!authorization.Authorize(context.Request)) return Results.Unauthorized();

        // Each owner takes and releases its own gate before the response is
        // composed. The projections are immutable and bounded by the owners'
        // existing lifecycle capacities; no Worker IPC is performed here.
        DateTimeOffset capturedAt = clock.GetUtcNow();
        LifecycleDiagnosticsResponse response = new(capturedAt,
            lobbies.LifecycleDiagnosticsSnapshot(),
            coordinator.LifecycleDiagnosticsSnapshot(),
            scheduler.LifecycleDiagnosticsSnapshot());
        return Results.Ok(response);
    }

    internal static async Task<IResult> ConfigureHistoricalDebugAsync(Guid matchId,
        HttpContext context, NodeHostAdminAuthorization authorization,
        NodeMatchCoordinator coordinator)
    {
        if (!context.Request.IsHttps) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!authorization.Authorize(context.Request)) return Results.Unauthorized();
        if (context.Request.ContentLength is not long length || length is < 2 or > MaximumRequestBytes)
            return Results.BadRequest();

        byte[] bytes = new byte[length];
        try
        {
            await context.Request.Body.ReadExactlyAsync(bytes, context.RequestAborted);
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 3 });
            NodeControlCodec.RejectDuplicates(document.RootElement);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Any(property => property.Name is not ("action" or "mode" or "seat"))
                || !root.TryGetProperty("action", out JsonElement actionElement)
                || actionElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("seat", out JsonElement seatElement)
                || !seatElement.TryGetByte(out byte seat) || seat >= 8)
                return Results.BadRequest();

            string? action = actionElement.GetString();
            string? mode = root.TryGetProperty("mode", out JsonElement modeElement)
                && modeElement.ValueKind == JsonValueKind.String ? modeElement.GetString() : null;
            AdminAction workerAction = (action, mode) switch
            {
                ("enable" or "refresh", "history") => AdminAction.LagCompHistory,
                ("enable" or "refresh", "dynamic") => AdminAction.LagCompDynamic,
                ("clear", null) => AdminAction.LagCompClear,
                _ => (AdminAction)(-1)
            };
            if (!Enum.IsDefined(workerAction)) return Results.BadRequest();
            return coordinator.TrySendHistoricalDebug(new(matchId), workerAction, seat)
                ? Results.Accepted() : Results.NotFound();
        }
        catch (Exception error) when (error is JsonException or EndOfStreamException
            or IOException or InvalidOperationException)
        {
            return Results.BadRequest();
        }
    }
}
