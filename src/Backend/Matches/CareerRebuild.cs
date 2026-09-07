using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Backend.Rating;
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
        Dictionary<Guid, HunterLicense> balances = await db.Licenses.AsNoTracking()
            .ToDictionaryAsync(x => x.PlayerId, x => new HunterLicense
            {
                PlayerId = x.PlayerId, CreatedAt = x.CreatedAt, RatingPoints = 0
            }, ct);
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
                MatchReportV1 effective = report with { TrustClass = (MatchTrustClass)match.TrustClass };
                RatingCalculation calculated = RatingProjection.Calculate(effective, balances);
                List<RatingLedgerEntry> stored = await db.RatingTransactions.AsNoTracking()
                    .Where(x => x.MatchId == match.MatchId).OrderBy(x => x.PlayerId).ToListAsync(ct);
                List<RatingPairLedgerEntry> pairs = await db.RatingPairContributions.AsNoTracking()
                    .Where(x => x.MatchId == match.MatchId).OrderBy(x => x.PlayerId)
                    .ThenBy(x => x.OpponentPlayerId).ToListAsync(ct);
                ValidateRating(match, calculated, stored, pairs);
                foreach (RatingLedgerEntry entry in stored)
                    balances[entry.PlayerId].RatingPoints = entry.PointsAfter;
                await CareerProjection.ApplyAsync(db, match, effective, ct);
                await db.SaveChangesAsync(ct);
                cursor = match.ProcessingOrder;
                db.ChangeTracker.Clear();
            }
        }
        db.ChangeTracker.Clear();
        List<HunterLicense> licenses = await db.Licenses.ToListAsync(ct);
        foreach (HunterLicense license in licenses)
            license.RatingPoints = balances[license.PlayerId].RatingPoints;
        CareerProjectionState? state = await db.ProjectionStates.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (state == null) db.ProjectionStates.Add(new() { Id = 1, RebuildRequired = false });
        else state.RebuildRequired = false;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static void ValidateRating(AcceptedMatch match, RatingCalculation calculated,
        IReadOnlyList<RatingLedgerEntry> stored, IReadOnlyList<RatingPairLedgerEntry> pairs)
    {
        string expectedStatus = calculated.IsEligible ? RatingProjection.AppliedStatus : RatingProjection.IneligibleStatus;
        int? expectedReason = calculated.IsEligible ? null : (int)calculated.Eligibility.Reason;
        if (match.RatingStatus != expectedStatus || match.RatingPolicyVersion != (int)calculated.PolicyVersion
            || match.RatingIneligibilityReason != expectedReason)
            throw new InvalidDataException("Accepted match rating decision does not match its authoritative report.");
        if (!calculated.IsEligible)
        {
            if (stored.Count != 0 || pairs.Count != 0)
                throw new InvalidDataException("Rating-ineligible match contains rating transactions.");
            return;
        }
        if (stored.Count != calculated.Transactions.Length)
            throw new InvalidDataException("Rating transaction count mismatch.");

        foreach (RatingTransaction expected in calculated.Transactions)
        {
            RatingLedgerEntry entry = stored.SingleOrDefault(x => x.PlayerId == expected.PlayerId)
                ?? throw new InvalidDataException("Rating transaction participant mismatch.");
            if (entry.ProcessingOrder != match.ProcessingOrder || entry.PointsBefore != expected.PointsBefore
                || entry.TierBefore != expected.TierBefore || entry.OpponentCount != expected.OpponentCount
                || entry.RawDelta != expected.RawDelta || entry.NormalizedDelta != expected.NormalizedDelta
                || entry.AppliedDelta != expected.AppliedDelta || entry.PointsAfter != expected.PointsAfter
                || entry.TierAfter != expected.TierAfter || entry.PolicyVersion != (int)expected.PolicyVersion)
                throw new InvalidDataException("Rating transaction chain or calculation mismatch.");
            RatingPairLedgerEntry[] storedPairs = pairs.Where(x => x.PlayerId == expected.PlayerId)
                .OrderBy(x => x.OpponentPlayerId).ToArray();
            RatingPairContribution[] expectedPairs = expected.PairContributions.OrderBy(x => x.OpponentPlayerId).ToArray();
            if (storedPairs.Length != expectedPairs.Length) throw new InvalidDataException("Rating pair evidence count mismatch.");
            for (int i = 0; i < storedPairs.Length; i++)
            {
                RatingPairLedgerEntry actual = storedPairs[i]; RatingPairContribution pair = expectedPairs[i];
                if (actual.OpponentPlayerId != pair.OpponentPlayerId
                    || actual.OpponentPointsBefore != pair.OpponentPointsBefore
                    || actual.OpponentTierBefore != pair.OpponentTierBefore
                    || actual.Result != (int)pair.Result || actual.Delta != pair.Delta)
                    throw new InvalidDataException("Rating pair evidence mismatch.");
            }
        }
    }
}
