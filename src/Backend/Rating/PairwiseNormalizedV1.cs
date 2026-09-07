using System.Collections.Immutable;
using MphRead.Identity;

namespace MphRead.Backend.Rating;

/// <summary>
/// Applies the USA Rev 1 matrix to each opposing pair, caps scale at three opponents,
/// truncates once toward zero, and clamps the final balance once.
/// </summary>
public sealed class PairwiseNormalizedV1 : RatingPolicy
{
    public static PairwiseNormalizedV1 Instance { get; } = new();
    public override RatingPolicyVersion Version => RatingPolicyVersion.PairwiseNormalizedV1;

    public override RatingCalculation Calculate(RatingMatchInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        RatingIneligibilityReason? reason = ValidateMatch(input);
        if (reason.HasValue) return Ineligible(reason.Value);

        RatingParticipantInput[] roster = input.Participants
            .Where(participant => participant.StartedMatch)
            .OrderBy(participant => participant.PlayerId!.Value)
            .ToArray();
        var transactions = ImmutableArray.CreateBuilder<RatingTransaction>(roster.Length);

        foreach (RatingParticipantInput participant in roster)
        {
            int tierBefore = RetailPointMatrix.TierForPoints(participant.PointsBefore);
            var contributions = ImmutableArray.CreateBuilder<RatingPairContribution>();
            foreach (RatingParticipantInput opponent in roster)
            {
                if (opponent.PlayerId == participant.PlayerId
                    || input.Teams && opponent.TeamId == participant.TeamId) continue;

                int opponentTier = RetailPointMatrix.TierForPoints(opponent.PointsBefore);
                RatingPairResult result = Compare(participant, opponent, input.Teams);
                RetailPointCell cell = RetailPointMatrix.Get(tierBefore, opponentTier);
                int delta = result switch
                {
                    RatingPairResult.Win => cell.Gain,
                    RatingPairResult.Loss => -cell.Loss,
                    _ => 0
                };
                contributions.Add(new(opponent.PlayerId!.Value, opponent.PointsBefore, opponentTier, result, delta));
            }

            int opponentCount = contributions.Count;
            int rawDelta = contributions.Sum(contribution => contribution.Delta);
            // C# signed integer division truncates toward zero. No pair or intermediate balance is clamped.
            int normalizedDelta = rawDelta * Math.Min(3, opponentCount) / opponentCount;
            int pointsAfter = Math.Clamp(participant.PointsBefore + normalizedDelta,
                RetailPointMatrix.MinimumPoints, RetailPointMatrix.MaximumPoints);
            transactions.Add(new(participant.PlayerId!.Value, participant.PointsBefore, tierBefore,
                contributions.ToImmutable(), opponentCount, rawDelta, normalizedDelta,
                pointsAfter - participant.PointsBefore, pointsAfter,
                RetailPointMatrix.TierForPoints(pointsAfter), Version));
        }

        return new(Version, RatingEligibility.Eligible, transactions.MoveToImmutable());
    }

    private static RatingIneligibilityReason? ValidateMatch(RatingMatchInput input)
    {
        if (input.ReportSchemaVersion < CurrentReportSchemaVersion) return RatingIneligibilityReason.LegacyReport;
        if (input.ReportSchemaVersion != CurrentReportSchemaVersion) return RatingIneligibilityReason.UnsupportedReportSchema;
        RatingIneligibilityReason? trustReason = input.TrustClass switch
        {
            MatchTrustClass.VerifiedCasual or MatchTrustClass.Ranked => null,
            MatchTrustClass.Community => RatingIneligibilityReason.CommunityServer,
            MatchTrustClass.Private => RatingIneligibilityReason.PrivateServer,
            MatchTrustClass.Tournament => RatingIneligibilityReason.TournamentRatingDisabled,
            MatchTrustClass.Practice => RatingIneligibilityReason.PracticeServer,
            _ => RatingIneligibilityReason.UnsupportedTrustClass
        };
        if (trustReason.HasValue) return trustReason;
        if (input.RankingEligibility != RankingEligibility.VerifiedServerOnly)
            return RatingIneligibilityReason.NonOfficialRules;
        if (!Enum.IsDefined(input.EndReason)
            || input.EndReason is MatchEndReason.Forced or MatchEndReason.InvalidTeams)
            return RatingIneligibilityReason.IncompleteMatch;
        if (input.Participants.IsDefault) return RatingIneligibilityReason.InvalidRoster;
        if (input.Participants.Any(participant => participant is null))
            return RatingIneligibilityReason.InvalidParticipant;

        RatingParticipantInput[] roster = input.Participants.Where(participant => participant.StartedMatch).ToArray();
        if (roster.Length is < 2 or > 8) return RatingIneligibilityReason.InvalidRoster;
        if (roster.Any(participant => participant.Kind == ParticipantKind.Bot))
            return RatingIneligibilityReason.BotParticipant;
        if (roster.Any(participant => participant.Kind == ParticipantKind.Guest))
            return RatingIneligibilityReason.GuestParticipant;
        if (roster.Any(participant => participant.Kind != ParticipantKind.RegisteredHuman
            || participant.PlayerId is not { } playerId || playerId == Guid.Empty
            || participant.Outcome is not (ParticipantOutcome.Finished or ParticipantOutcome.Forfeited)))
            return RatingIneligibilityReason.InvalidParticipant;
        if (roster.Select(participant => participant.PlayerId).Distinct().Count() != roster.Length)
            return RatingIneligibilityReason.InvalidRoster;
        if (roster.Any(participant => participant.PointsBefore is < RetailPointMatrix.MinimumPoints
            or > RetailPointMatrix.MaximumPoints)) return RatingIneligibilityReason.InvalidPoints;

        if (input.Teams)
        {
            if (roster.Any(participant => participant.TeamId is null or < 0 or > 7
                || participant.TeamStanding is < 0 or > 7))
                return RatingIneligibilityReason.InvalidStandings;
            if (roster.GroupBy(participant => participant.TeamId)
                .Any(team => team.Select(participant => participant.TeamStanding).Distinct().Count() != 1))
                return RatingIneligibilityReason.InvalidStandings;
        }
        else if (roster.Any(participant => participant.Standing is < 0 or > 7))
        {
            return RatingIneligibilityReason.InvalidStandings;
        }

        bool hasOpposingPair = roster.Any(participant => roster.Any(opponent => opponent != participant
            && (!input.Teams || opponent.TeamId != participant.TeamId)));
        return hasOpposingPair ? null : RatingIneligibilityReason.NoOpposingPair;
    }

    private static RatingPairResult Compare(RatingParticipantInput participant,
        RatingParticipantInput opponent, bool teams)
    {
        bool participantForfeited = participant.Outcome != ParticipantOutcome.Finished;
        bool opponentForfeited = opponent.Outcome != ParticipantOutcome.Finished;
        if (participantForfeited && opponentForfeited) return RatingPairResult.Tie;
        if (participantForfeited) return RatingPairResult.Loss;
        if (opponentForfeited) return RatingPairResult.Win;

        int participantStanding = teams ? participant.TeamStanding : participant.Standing;
        int opponentStanding = teams ? opponent.TeamStanding : opponent.Standing;
        return participantStanding < opponentStanding ? RatingPairResult.Win
            : participantStanding > opponentStanding ? RatingPairResult.Loss
            : RatingPairResult.Tie;
    }
}
