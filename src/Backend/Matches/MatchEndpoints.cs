using MphRead.Backend.Tickets;

namespace MphRead.Backend.Matches;

public static class MatchEndpoints
{
    public static void MapMatches(this WebApplication app)
    {
        app.MapPost("/v1/server/matches", async (HttpContext http, GameServerRegistry registry,
            AuthenticatedNodeRateLimiter nodeLimiter, MatchIngestion ingestion, CancellationToken ct) =>
        {
            string authorization = http.Request.Headers.Authorization.ToString();
            if (!Guid.TryParseExact(http.Request.Headers["X-Server-Id"], "D", out Guid server)
                || !authorization.StartsWith("Bearer ", StringComparison.Ordinal)
                || !registry.TryAuthenticate(server, authorization[7..], out var trust))
                return BackendProblem.Create("invalid_credential", "Server credentials are invalid.",
                    StatusCodes.Status401Unauthorized);
            if (!nodeLimiter.TryAcquire(server, "match-report"))
                return BackendProblem.Create("rate_limited", "Node update rate limit exceeded.",
                    StatusCodes.Status429TooManyRequests);
            if (!Guid.TryParseExact(http.Request.Headers["Idempotency-Key"], "D", out Guid match)
                || match == Guid.Empty)
                return BackendProblem.Create("invalid_request", "The match report identity is invalid.",
                    StatusCodes.Status400BadRequest);
            byte[]? body = await BackendRequestLimits.ReadBoundedPayloadAsync(
                http.Request.Body, ReportValidation.MaximumBytes, ct);
            if (body == null)
                return BackendProblem.Create("invalid_match_report", "The match report exceeds its size bound.",
                    StatusCodes.Status413PayloadTooLarge);
            var result = await ingestion.AcceptAsync(server, trust, match,
                http.Request.Headers["X-Content-SHA256"].ToString(), body, ct);
            return result.Receipt != null ? Results.Json(result.Receipt, statusCode: result.StatusCode)
                : BackendProblem.Create(result.StatusCode == StatusCodes.Status409Conflict
                    ? "report_conflict" : "invalid_match_report",
                    result.Error ?? "The match report is invalid.", result.StatusCode);
        }).RequireRateLimiting(BackendRoutePolicy.Api);
    }
}
