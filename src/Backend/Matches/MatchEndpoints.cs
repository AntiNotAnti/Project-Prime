using MphRead.Backend.Tickets;

namespace MphRead.Backend.Matches;

public static class MatchEndpoints
{
    public static void MapMatches(this WebApplication app)
    {
        app.MapPost("/v1/server/matches", async (HttpContext http, GameServerRegistry registry,
            MatchIngestion ingestion, CancellationToken ct) =>
        {
            string authorization = http.Request.Headers.Authorization.ToString();
            if (!Guid.TryParseExact(http.Request.Headers["X-Server-Id"], "D", out Guid server)
                || !authorization.StartsWith("Bearer ", StringComparison.Ordinal)
                || !registry.TryAuthenticate(server, authorization[7..], out var trust)) return Results.Unauthorized();
            if (!Guid.TryParseExact(http.Request.Headers["Idempotency-Key"], "D", out Guid match)) return Results.BadRequest();
            using var body = new MemoryStream();
            byte[] buffer = new byte[8192];
            while (true)
            {
                int count = await http.Request.Body.ReadAsync(buffer, ct);
                if (count == 0) break;
                if (body.Length + count > ReportValidation.MaximumBytes) return Results.StatusCode(413);
                body.Write(buffer, 0, count);
            }
            var result = await ingestion.AcceptAsync(server, trust, match,
                http.Request.Headers["X-Content-SHA256"].ToString(), body.ToArray(), ct);
            return result.Receipt != null ? Results.Json(result.Receipt, statusCode: result.StatusCode)
                : Results.Json(new { result.Error }, statusCode: result.StatusCode);
        }).RequireRateLimiting("api");
    }
}
