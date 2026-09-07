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
            if (match == null) return Results.NotFound();
            if (!signer.IsConfigured) return Results.Problem("Result signing is not configured.", statusCode: 503);
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
        }).RequireRateLimiting("api");
    }
}
