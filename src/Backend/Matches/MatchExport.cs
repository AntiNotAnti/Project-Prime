using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Backend.Tickets;
using MphRead.Identity;

namespace MphRead.Backend.Matches;

public static class MatchExport
{
    public static void MapMatchExports(this WebApplication app)
    {
        app.MapGet("/v1/matches/{id}/export", async (Guid id, BackendDbContext db, GameTicketIssuer signer, CancellationToken ct) =>
        {
            var match = await db.Matches.AsNoTracking().SingleOrDefaultAsync(x => x.MatchId == id, ct);
            if (match == null)
                return BackendProblem.Create("invalid_request", "The match export was not found.",
                    StatusCodes.Status404NotFound);
            if (!signer.IsConfigured)
                return BackendProblem.Create("service_busy", "Result signing is not configured.",
                    StatusCodes.Status503ServiceUnavailable);
            var report = JsonSerializer.Deserialize<MatchReportV1>(match.OriginalReport, MatchIngestion.ReportJson)!;
            RatingReceipt rating = await RatingProjection.ReadReceiptAsync(db, match, ct);
            return Results.Ok(new
            {
                match.MatchId, match.ServerId, report.ServerIncarnation, report.TournamentId, report.RoundId,
                report.ReplayId, TrustClass = (MatchTrustClass)match.TrustClass, match.ProcessingOrder,
                match.PayloadHash, OriginalReport = Convert.ToBase64String(match.OriginalReport),
                Scoreboard = report.Participants, match.RatingStatus, Rating = rating,
                SignedReceipt = signer.SignMatchResult(match.MatchId, match.PayloadHash, match.ProcessingOrder, match.TrustClass)
            });
        }).Bodyless().RequireRateLimiting(BackendRoutePolicy.Api);
    }
}
