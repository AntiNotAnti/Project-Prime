using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Backend.Rating;
using MphRead.Identity;

namespace MphRead.Backend.Matches;

public sealed record MatchReceipt(Guid MatchId, string PayloadHash, long ProcessingOrder,
    string RatingStatus, RatingReceipt Rating);
public sealed record IngestionResult(int StatusCode, MatchReceipt? Receipt = null, string? Error = null);

public sealed class MatchIngestion(BackendDbContext db, TimeProvider clock, ILogger<MatchIngestion> logger)
{
    public static readonly JsonSerializerOptions ReportJson = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    public async Task<IngestionResult> AcceptAsync(Guid serverId, MatchTrustClass trust, Guid matchId,
        string claimedHash, byte[] payload, CancellationToken ct)
    {
        using BackendMetrics.MatchIngestionMeasurement measurement = BackendMetrics.Begin(payload.Length);
        try
        {
            IngestionResult result = await AcceptCoreAsync(serverId, trust, matchId, claimedHash, payload, ct);
            measurement.Complete(result.StatusCode);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            BackendDiagnostics.Rejected(logger, "match_report", "exception");
            throw;
        }
    }

    private async Task<IngestionResult> AcceptCoreAsync(Guid serverId, MatchTrustClass trust, Guid matchId,
        string claimedHash, byte[] payload, CancellationToken ct)
    {
        if (payload.Length is 0 or > ReportValidation.MaximumBytes || claimedHash.Length != 64)
            return new(400, Error: "Invalid report size or hash.");
        string hash = Convert.ToHexString(SHA256.HashData(payload));
        if (!string.Equals(hash, claimedHash, StringComparison.Ordinal)) return new(400, Error: "Report hash mismatch.");
        MatchReportV1? report;
        try { report = JsonSerializer.Deserialize<MatchReportV1>(payload, ReportJson); }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        { return new(400, Error: "Invalid report document."); }
        if (report == null || report.MatchId != matchId || report.ServerId != serverId || !report.IsValid)
            return new(400, Error: "Invalid authoritative report facts.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({CareerRebuild.ProjectionLock})", ct);
            // Same UUID submissions serialize before checking the ledger, including conflicting
            // bodies. Hash collisions only serialize unrelated reports; they cannot alter identity.
            int advisoryKey = BinaryPrimitives.ReadInt32LittleEndian(matchId.ToByteArray());
            const int reportNamespace = 0x50484D52;
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({reportNamespace}, {advisoryKey})", ct);
        }
        var existing = await db.Matches.AsNoTracking().SingleOrDefaultAsync(m => m.MatchId == matchId, ct);
        if (existing != null)
            return existing.ServerId == serverId && existing.PayloadHash == hash
                ? new(200, await ReceiptAsync(existing, ct)) : new(409, Error: "MatchId already contains a different report.");

        if (!ReportValidation.Validate(report, serverId, trust, clock.GetUtcNow()))
            return new(400, Error: "Invalid authoritative report facts.");

        // The authenticated registry is authoritative; retain the raw claim only in the immutable body.
        var effective = report with { TrustClass = trust };
        Guid[] players = report.Participants.Where(p => p.PlayerId.HasValue).Select(p => p.PlayerId!.Value.Value)
            .OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
        var licenses = new Dictionary<Guid, HunterLicense>();
        foreach (Guid player in players)
        {
            // Every aggregate update (and future approved current-balance rating calculation)
            // follows these stable license-row locks, in the same total order.
            var license = db.Database.IsNpgsql()
                ? await db.Licenses.FromSqlInterpolated($"SELECT * FROM \"prime\".\"hunter_licenses\" WHERE \"PlayerId\" = {player} FOR UPDATE").SingleOrDefaultAsync(ct)
                : await db.Licenses.SingleOrDefaultAsync(p => p.PlayerId == player, ct);
            if (license == null) return new(400, Error: "Report contains an unknown registered player.");
            licenses.Add(player, license);
        }
        RatingCalculation rating = RatingProjection.Calculate(effective, licenses);
        var accepted = new AcceptedMatch
        {
            MatchId = matchId, ServerId = serverId, ServerIncarnation = report.ServerIncarnation,
            PayloadHash = hash, OriginalReport = payload, AcceptedAt = clock.GetUtcNow(), EndedAt = report.EndedAtUtc,
            RoomKey = report.Rules.RoomKey, Mode = (int)report.Rules.Mode, TrustClass = (int)trust,
            CareerEligible = CareerProjection.IsEligible(effective),
            RatingStatus = rating.IsEligible ? RatingProjection.AppliedStatus : RatingProjection.IneligibleStatus,
            RatingPolicyVersion = (int)rating.PolicyVersion,
            RatingIneligibilityReason = rating.IsEligible ? null : (int)rating.Eligibility.Reason
        };
        // SQLite is used only by focused HTTP tests. Production PostgreSQL owns its sequence.
        if (!db.Database.IsNpgsql()) accepted.ProcessingOrder = (await db.Matches.MaxAsync(m => (long?)m.ProcessingOrder, ct) ?? 0) + 1;
        db.Matches.Add(accepted);
        await db.SaveChangesAsync(ct);
        RatingProjection.Apply(db, accepted, rating, licenses);
        await CareerProjection.ApplyAsync(db, accepted, effective, ct);
        await db.SaveChangesAsync(ct);
        MatchReceipt receipt = await ReceiptAsync(accepted, ct);
        await transaction.CommitAsync(ct);
        return new(201, receipt);
    }

    private async Task<MatchReceipt> ReceiptAsync(AcceptedMatch match, CancellationToken ct) => new(
        match.MatchId, match.PayloadHash, match.ProcessingOrder, match.RatingStatus,
        await RatingProjection.ReadReceiptAsync(db, match, ct));
}
