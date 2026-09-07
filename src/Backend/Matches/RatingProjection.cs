using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Backend.Rating;
using MphRead.Identity;

namespace MphRead.Backend.Matches;

public sealed record RatingPairReceipt(Guid OpponentPlayerId, int OpponentPointsBefore,
    int OpponentTierBefore, string Result, int Delta);
public sealed record RatingTransactionReceipt(Guid PlayerId, int PointsBefore, int TierBefore,
    IReadOnlyList<RatingPairReceipt> PairContributions, int OpponentCount, int RawDelta,
    int NormalizedDelta, int AppliedDelta, int PointsAfter, int TierAfter, string Policy);
public sealed record RatingReceipt(string Status, string Policy, string? IneligibilityReason,
    IReadOnlyList<RatingTransactionReceipt> Transactions);
public sealed record RatingSummary(int Points, int Tier, string Title, int? NextThreshold,
    int? LastOfficialDelta, string Policy);

public static class RatingProjection
{
    public const string AppliedStatus = "applied";
    public const string IneligibleStatus = "ineligible";

    public static async Task<RatingSummary> ReadSummaryAsync(BackendDbContext db, HunterLicense license,
        CancellationToken ct)
    {
        RatingLedgerEntry? latest = await db.RatingTransactions.AsNoTracking()
            .Where(x => x.PlayerId == license.PlayerId).OrderByDescending(x => x.ProcessingOrder)
            .FirstOrDefaultAsync(ct);
        int tier = RetailPointMatrix.TierForPoints(license.RatingPoints);
        int? next = tier < RetailPointMatrix.TierCount ? RetailPointMatrix.TierMinimums[tier] : null;
        return new(license.RatingPoints, tier, Title(tier), next, latest?.AppliedDelta,
            (latest == null ? RatingPolicyVersion.PairwiseNormalizedV1 : (RatingPolicyVersion)latest.PolicyVersion).ToString());
    }

    public static string Title(int tier) => tier switch
    {
        1 => "Bounty Hunter",
        2 => "Super Hunter",
        3 => "Elite Hunter",
        4 => "Master Hunter",
        5 => "Legendary Hunter",
        _ => throw new ArgumentOutOfRangeException(nameof(tier))
    };

    public static RatingCalculation Calculate(MatchReportV1 report,
        IReadOnlyDictionary<Guid, HunterLicense> licenses)
    {
        var participants = report.Participants.Select(participant => new RatingParticipantInput(
            participant.PlayerId?.Value,
            participant.Kind,
            participant.StartedMatch,
            participant.Outcome,
            participant.PlayerId is { } id && licenses.TryGetValue(id.Value, out HunterLicense? license)
                ? license.RatingPoints : 0,
            participant.Metrics.Standing,
            participant.Metrics.TeamStanding,
            participant.Spans[0].TeamIndex)).ToImmutableArray();
        return PairwiseNormalizedV1.Instance.Calculate(new(report.SchemaVersion, report.TrustClass,
            report.Rules.RankingEligibility, report.EndReason, report.Rules.Teams, participants));
    }

    public static void Apply(BackendDbContext db, AcceptedMatch match, RatingCalculation calculation,
        IReadOnlyDictionary<Guid, HunterLicense> licenses)
    {
        match.RatingStatus = calculation.IsEligible ? AppliedStatus : IneligibleStatus;
        match.RatingPolicyVersion = (int)calculation.PolicyVersion;
        match.RatingIneligibilityReason = calculation.IsEligible ? null : (int)calculation.Eligibility.Reason;
        if (!calculation.IsEligible) return;

        foreach (RatingTransaction transaction in calculation.Transactions)
        {
            db.RatingTransactions.Add(new RatingLedgerEntry
            {
                MatchId = match.MatchId,
                PlayerId = transaction.PlayerId,
                ProcessingOrder = match.ProcessingOrder,
                PointsBefore = transaction.PointsBefore,
                TierBefore = transaction.TierBefore,
                OpponentCount = transaction.OpponentCount,
                RawDelta = transaction.RawDelta,
                NormalizedDelta = transaction.NormalizedDelta,
                AppliedDelta = transaction.AppliedDelta,
                PointsAfter = transaction.PointsAfter,
                TierAfter = transaction.TierAfter,
                PolicyVersion = (int)transaction.PolicyVersion
            });
            foreach (RatingPairContribution pair in transaction.PairContributions)
            {
                db.RatingPairContributions.Add(new RatingPairLedgerEntry
                {
                    MatchId = match.MatchId,
                    PlayerId = transaction.PlayerId,
                    OpponentPlayerId = pair.OpponentPlayerId,
                    OpponentPointsBefore = pair.OpponentPointsBefore,
                    OpponentTierBefore = pair.OpponentTierBefore,
                    Result = (int)pair.Result,
                    Delta = pair.Delta
                });
            }
            licenses[transaction.PlayerId].RatingPoints = transaction.PointsAfter;
        }
    }

    public static async Task<RatingReceipt> ReadReceiptAsync(BackendDbContext db, AcceptedMatch match,
        CancellationToken ct)
    {
        string policy = ((RatingPolicyVersion?)match.RatingPolicyVersion)?.ToString()
            ?? RatingPolicyVersion.PairwiseNormalizedV1.ToString();
        if (match.RatingStatus != AppliedStatus)
        {
            string? reason = ((RatingIneligibilityReason?)match.RatingIneligibilityReason)?.ToString();
            return new(match.RatingStatus, policy, reason, Array.Empty<RatingTransactionReceipt>());
        }

        var entries = await db.RatingTransactions.AsNoTracking().Where(x => x.MatchId == match.MatchId)
            .OrderBy(x => x.PlayerId).ToListAsync(ct);
        var pairs = await db.RatingPairContributions.AsNoTracking().Where(x => x.MatchId == match.MatchId)
            .OrderBy(x => x.PlayerId).ThenBy(x => x.OpponentPlayerId).ToListAsync(ct);
        return new(match.RatingStatus, policy, null, entries.Select(entry => new RatingTransactionReceipt(
            entry.PlayerId, entry.PointsBefore, entry.TierBefore,
            pairs.Where(pair => pair.PlayerId == entry.PlayerId).Select(pair => new RatingPairReceipt(
                pair.OpponentPlayerId, pair.OpponentPointsBefore, pair.OpponentTierBefore,
                ((RatingPairResult)pair.Result).ToString(), pair.Delta)).ToArray(),
            entry.OpponentCount, entry.RawDelta, entry.NormalizedDelta, entry.AppliedDelta,
            entry.PointsAfter, entry.TierAfter, ((RatingPolicyVersion)entry.PolicyVersion).ToString())).ToArray());
    }
}
