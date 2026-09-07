using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Identity;

namespace MphRead.Backend.Matches;

/// <summary>Explicit operator operation, never called at startup or exposed to player HTTP.
/// Replays the immutable ledger in processing order under an exclusive projection barrier.</summary>
public sealed class CareerRebuild(BackendDbContext db)
{
    internal const long ProjectionLock = 0x5048434152454552;

    public async Task RebuildAsync(CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({ProjectionLock})", ct);
        await db.Aggregates.ExecuteDeleteAsync(ct);
        await db.Participations.ExecuteDeleteAsync(ct);
        long cursor = 0;
        while (true)
        {
            var matches = await db.Matches.AsNoTracking().Where(m => m.ProcessingOrder > cursor)
                .OrderBy(m => m.ProcessingOrder).Take(100).ToListAsync(ct);
            if (matches.Count == 0) break;
            foreach (var match in matches)
            {
                if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(match.OriginalReport)) != match.PayloadHash)
                    throw new InvalidDataException("Accepted report hash mismatch.");
                var report = JsonSerializer.Deserialize<MatchReportV1>(match.OriginalReport, MatchIngestion.ReportJson)
                    ?? throw new InvalidDataException("Accepted report is missing.");
                if (!report.IsValid || report.MatchId != match.MatchId || report.ServerId != match.ServerId)
                    throw new InvalidDataException("Accepted report identity mismatch.");
                await CareerProjection.ApplyAsync(db, match, report with { TrustClass = (MatchTrustClass)match.TrustClass }, ct);
                await db.SaveChangesAsync(ct);
                cursor = match.ProcessingOrder;
                db.ChangeTracker.Clear();
            }
        }
        await transaction.CommitAsync(ct);
    }
}
