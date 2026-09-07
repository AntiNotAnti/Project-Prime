using System.Collections.Immutable;
using MphRead.Backend.Rating;
using MphRead.Identity;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class PairwiseNormalizedV1Tests
{
    private static readonly PairwiseNormalizedV1 Policy = PairwiseNormalizedV1.Instance;
    private static readonly int[] TierPoints = [20, 50, 200, 500, 800];
    private static readonly int[,] Gains =
    {
        { 2, 4, 7, 10, 15 }, { 1, 3, 6, 10, 15 }, { 1, 2, 4, 8, 12 },
        { 1, 1, 4, 5, 8 }, { 1, 1, 2, 4, 6 }
    };
    private static readonly int[,] Losses =
    {
        { 1, 1, 1, 0, 0 }, { 2, 3, 1, 0, 0 }, { 4, 3, 4, 3, 2 },
        { 8, 8, 6, 5, 4 }, { 15, 12, 10, 8, 6 }
    };

    public static IEnumerable<object[]> EveryTierPair()
    {
        for (int self = 0; self < 5; self++)
            for (int opponent = 0; opponent < 5; opponent++)
                yield return [self, opponent, Gains[self, opponent], Losses[self, opponent]];
    }

    [Theory]
    [MemberData(nameof(EveryTierPair))]
    public void EveryMatrixGainIsAppliedWithSelfAsTheRow(int selfTier, int opponentTier, int gain, int _)
    {
        RatingTransaction winner = Transaction(Calculate(P(1, TierPoints[selfTier], 0),
            P(2, TierPoints[opponentTier], 1)), 1);

        Assert.Equal(gain, winner.RawDelta);
        Assert.Equal(gain, winner.AppliedDelta);
    }

    [Theory]
    [MemberData(nameof(EveryTierPair))]
    public void EveryMatrixLossIsAppliedWithOpponentAsTheColumn(int selfTier, int opponentTier, int _, int loss)
    {
        RatingTransaction loser = Transaction(Calculate(P(1, TierPoints[selfTier], 1),
            P(2, TierPoints[opponentTier], 0)), 1);

        Assert.Equal(-loss, loser.RawDelta);
        Assert.Equal(-loss, loser.AppliedDelta);
    }

    [Fact]
    public void TieProducesZeroAndPersistsThePairEvidence()
    {
        RatingCalculation result = Calculate(P(1, 100, 0), P(2, 200, 0));

        Assert.True(result.IsEligible);
        Assert.All(result.Transactions, transaction =>
        {
            Assert.Equal(0, transaction.RawDelta);
            Assert.Equal(0, transaction.NormalizedDelta);
            Assert.Equal(0, transaction.AppliedDelta);
            RatingPairContribution pair = Assert.Single(transaction.PairContributions);
            Assert.Equal(RatingPairResult.Tie, pair.Result);
            Assert.Equal(0, pair.Delta);
            Assert.Equal(RatingPolicyVersion.PairwiseNormalizedV1, transaction.PolicyVersion);
        });
    }

    [Fact]
    public void MixedSignedPairsAreSummedBeforeNormalizationAndClamp()
    {
        RatingCalculation result = Calculate(P(1, 20, 1), P(2, 20, 0), P(3, 40, 2));
        RatingTransaction middle = Transaction(result, 1);

        Assert.Equal([-1, 4], middle.PairContributions.Select(pair => pair.Delta));
        Assert.Equal(3, middle.RawDelta);
        Assert.Equal(3, middle.NormalizedDelta);
        Assert.Equal(23, middle.PointsAfter);
    }

    [Fact]
    public void EightPlayerNormalizationTruncatesTowardZeroForBothSigns()
    {
        RatingCalculation result = Calculate(Enumerable.Range(1, 8).Select(id => P(id, 20, id - 1)).ToArray());
        RatingTransaction winner = Transaction(result, 1);
        RatingTransaction loser = Transaction(result, 8);

        Assert.Equal(7, winner.OpponentCount);
        Assert.Equal(14, winner.RawDelta);
        Assert.Equal(6, winner.NormalizedDelta);
        Assert.Equal(-7, loser.RawDelta);
        Assert.Equal(-3, loser.NormalizedDelta);
    }

    [Fact]
    public void FourVersusFourUsesOnlyOpposingPairs()
    {
        RatingParticipantInput[] participants = Enumerable.Range(1, 8)
            .Select(id => P(id, 20, standing: id <= 4 ? 0 : 1,
                teamId: id <= 4 ? 0 : 1, teamStanding: id <= 4 ? 0 : 1))
            .ToArray();
        RatingCalculation result = Calculate(teams: true, participants);

        foreach (RatingTransaction winner in result.Transactions.Take(4))
        {
            Assert.Equal(4, winner.OpponentCount);
            Assert.Equal(8, winner.RawDelta);
            Assert.Equal(6, winner.NormalizedDelta);
        }
        foreach (RatingTransaction loser in result.Transactions.Skip(4))
        {
            Assert.Equal(4, loser.OpponentCount);
            Assert.Equal(-4, loser.RawDelta);
            Assert.Equal(-3, loser.NormalizedDelta);
        }
    }

    [Fact]
    public void TeamTiesAndTeammatesNeverExchangePoints()
    {
        RatingCalculation result = Calculate(teams: true,
            P(1, 20, 0, teamId: 0, teamStanding: 0), P(2, 20, 0, teamId: 0, teamStanding: 0),
            P(3, 20, 0, teamId: 1, teamStanding: 0), P(4, 20, 0, teamId: 1, teamStanding: 0));

        Assert.All(result.Transactions, transaction =>
        {
            Assert.Equal(2, transaction.OpponentCount);
            Assert.Equal(0, transaction.RawDelta);
            Assert.DoesNotContain(transaction.PairContributions,
                pair => pair.OpponentPlayerId == Id(transaction.PlayerId == Id(1) ? 2 : transaction.PlayerId == Id(2) ? 1
                    : transaction.PlayerId == Id(3) ? 4 : 3));
        });
    }

    [Fact]
    public void FinishersBeatForfeitersAndForfeitersTieEachOther()
    {
        RatingCalculation result = Calculate(P(1, 20, 3),
            P(2, 20, 0, outcome: ParticipantOutcome.Forfeited),
            P(3, 20, 1, outcome: ParticipantOutcome.Forfeited));

        RatingTransaction finisher = Transaction(result, 1);
        Assert.Equal([RatingPairResult.Win, RatingPairResult.Win], finisher.PairContributions.Select(pair => pair.Result));
        Assert.Equal(4, finisher.RawDelta);
        foreach (RatingTransaction forfeiter in result.Transactions.Where(transaction => transaction.PlayerId != Id(1)))
        {
            Assert.Contains(forfeiter.PairContributions, pair => pair.OpponentPlayerId == Id(1)
                && pair.Result == RatingPairResult.Loss);
            Assert.Contains(forfeiter.PairContributions, pair => pair.OpponentPlayerId != Id(1)
                && pair.Result == RatingPairResult.Tie);
        }
    }

    [Fact]
    public void StartedDepartedParticipantIsRejectedInsteadOfSilentlyBecomingAForfeit()
        => AssertIneligible(Match(P(1, 20, 0), P(2, 20, 1,
            outcome: ParticipantOutcome.Departed)), RatingIneligibilityReason.InvalidParticipant);

    [Fact]
    public void FinalClampRecordsTheActuallyAppliedDeltaAtBothBounds()
    {
        RatingTransaction upper = Transaction(Calculate(P(1, 849, 0), P(2, 750, 1)), 1);
        Assert.Equal(6, upper.NormalizedDelta);
        Assert.Equal(1, upper.AppliedDelta);
        Assert.Equal(850, upper.PointsAfter);

        RatingTransaction lower = Transaction(Calculate(P(1, 2, 3), P(2, 20, 0), P(3, 20, 1), P(4, 20, 2)), 1);
        Assert.Equal(-3, lower.NormalizedDelta);
        Assert.Equal(-2, lower.AppliedDelta);
        Assert.Equal(0, lower.PointsAfter);
    }

    [Fact]
    public void AllPairCellsUseTransactionFrozenTiersAcrossAThreshold()
    {
        RatingTransaction transaction = Transaction(Calculate(P(1, 39, 0), P(2, 20, 1), P(3, 20, 2)), 1);

        Assert.Equal(1, transaction.TierBefore);
        Assert.Equal([2, 2], transaction.PairContributions.Select(pair => pair.Delta));
        Assert.Equal(43, transaction.PointsAfter);
        Assert.Equal(2, transaction.TierAfter);
    }

    [Fact]
    public void EnumerationOrderDoesNotChangeTransactionsOrPairEvidence()
    {
        RatingParticipantInput[] participants = [P(3, 390, 2), P(1, 39, 0), P(2, 140, 1)];
        RatingCalculation first = Calculate(participants);
        RatingCalculation second = Calculate(participants.Reverse().ToArray());

        Assert.Equal(first.Transactions.Length, second.Transactions.Length);
        foreach ((RatingTransaction left, RatingTransaction right) in first.Transactions.Zip(second.Transactions))
        {
            Assert.Equal(left.PlayerId, right.PlayerId);
            Assert.Equal(left.PointsBefore, right.PointsBefore);
            Assert.Equal(left.TierBefore, right.TierBefore);
            Assert.Equal(left.PairContributions.ToArray(), right.PairContributions.ToArray());
            Assert.Equal(left.OpponentCount, right.OpponentCount);
            Assert.Equal(left.RawDelta, right.RawDelta);
            Assert.Equal(left.NormalizedDelta, right.NormalizedDelta);
            Assert.Equal(left.AppliedDelta, right.AppliedDelta);
            Assert.Equal(left.PointsAfter, right.PointsAfter);
            Assert.Equal(left.TierAfter, right.TierAfter);
            Assert.Equal(left.PolicyVersion, right.PolicyVersion);
        }
    }

    [Theory]
    [InlineData(MatchTrustClass.Community, RatingIneligibilityReason.CommunityServer)]
    [InlineData(MatchTrustClass.Private, RatingIneligibilityReason.PrivateServer)]
    [InlineData(MatchTrustClass.Tournament, RatingIneligibilityReason.TournamentRatingDisabled)]
    [InlineData(MatchTrustClass.Practice, RatingIneligibilityReason.PracticeServer)]
    public void NonOfficialTrustClassesExcludeTheWholeMatch(MatchTrustClass trust, RatingIneligibilityReason reason)
        => AssertIneligible(Match(P(1, 20, 0), P(2, 20, 1)) with { TrustClass = trust }, reason);

    [Fact]
    public void RankedTrustIsEligibleWhenRulesAreOfficial()
    {
        RatingMatchInput input = Match(P(1, 20, 0), P(2, 20, 1)) with { TrustClass = MatchTrustClass.Ranked };
        Assert.True(Policy.Calculate(input).IsEligible);
    }

    [Theory]
    [InlineData(ParticipantKind.Bot, RatingIneligibilityReason.BotParticipant)]
    [InlineData(ParticipantKind.Guest, RatingIneligibilityReason.GuestParticipant)]
    public void BotOrGuestInStartingRosterExcludesTheWholeMatch(ParticipantKind kind, RatingIneligibilityReason reason)
        => AssertIneligible(Match(P(1, 20, 0), P(2, 20, 1, kind: kind)), reason);

    [Fact]
    public void NonStartingBotSpectatorDoesNotEnterOrInvalidateTheFrozenRoster()
    {
        RatingParticipantInput spectator = P(3, 20, 0, kind: ParticipantKind.Bot) with { StartedMatch = false };
        RatingCalculation result = Policy.Calculate(Match(P(1, 20, 0), P(2, 20, 1), spectator));

        Assert.True(result.IsEligible);
        Assert.Equal(2, result.Transactions.Length);
        Assert.DoesNotContain(result.Transactions, transaction => transaction.PlayerId == Id(3));
    }

    [Fact]
    public void LegacyNonOfficialAndAbortedReportsHaveExplicitWholeMatchReasons()
    {
        RatingMatchInput valid = Match(P(1, 20, 0), P(2, 20, 1));
        AssertIneligible(valid with { ReportSchemaVersion = 0 }, RatingIneligibilityReason.LegacyReport);
        AssertIneligible(valid with { RankingEligibility = RankingEligibility.Unranked }, RatingIneligibilityReason.NonOfficialRules);
        AssertIneligible(valid with { EndReason = MatchEndReason.Forced }, RatingIneligibilityReason.IncompleteMatch);
        AssertIneligible(valid with { EndReason = MatchEndReason.InvalidTeams }, RatingIneligibilityReason.IncompleteMatch);
    }

    [Fact]
    public void InvalidBalancesStandingsAndTeamRosterAreRejectedBeforeCalculation()
    {
        AssertIneligible(Match(P(1, -1, 0), P(2, 20, 1)), RatingIneligibilityReason.InvalidPoints);
        AssertIneligible(Match(P(1, 851, 0), P(2, 20, 1)), RatingIneligibilityReason.InvalidPoints);
        AssertIneligible(Match(P(1, 20, -1), P(2, 20, 1)), RatingIneligibilityReason.InvalidStandings);
        AssertIneligible(Match(teams: true, P(1, 20, 0, teamId: 0, teamStanding: 0),
            P(2, 20, 1, teamId: 0, teamStanding: 0)), RatingIneligibilityReason.NoOpposingPair);
        AssertIneligible(Match(teams: true, P(1, 20, 0, teamId: 0, teamStanding: 0),
            P(2, 20, 1, teamId: 0, teamStanding: 1), P(3, 20, 2, teamId: 1, teamStanding: 1)),
            RatingIneligibilityReason.InvalidStandings);
    }

    private static RatingParticipantInput P(int id, int points, int standing, int? teamId = null,
        int teamStanding = 0, ParticipantOutcome outcome = ParticipantOutcome.Finished,
        ParticipantKind kind = ParticipantKind.RegisteredHuman)
        => new(kind == ParticipantKind.RegisteredHuman ? Id(id) : null, kind, true, outcome,
            points, standing, teamStanding, teamId);

    private static RatingCalculation Calculate(params RatingParticipantInput[] participants)
        => Policy.Calculate(Match(participants));

    private static RatingCalculation Calculate(bool teams, params RatingParticipantInput[] participants)
        => Policy.Calculate(Match(teams, participants));

    private static RatingMatchInput Match(params RatingParticipantInput[] participants)
        => Match(false, participants);

    private static RatingMatchInput Match(bool teams, params RatingParticipantInput[] participants)
        => new(RatingPolicy.CurrentReportSchemaVersion, MatchTrustClass.VerifiedCasual,
            RankingEligibility.VerifiedServerOnly, MatchEndReason.TimeLimit, teams, [.. participants]);

    private static RatingTransaction Transaction(RatingCalculation calculation, int id)
        => Assert.Single(calculation.Transactions, transaction => transaction.PlayerId == Id(id));

    private static void AssertIneligible(RatingMatchInput input, RatingIneligibilityReason reason)
    {
        RatingCalculation result = Policy.Calculate(input);
        Assert.False(result.IsEligible);
        Assert.Equal(reason, result.Eligibility.Reason);
        Assert.Empty(result.Transactions);
        Assert.Equal(RatingPolicyVersion.PairwiseNormalizedV1, result.PolicyVersion);
    }

    private static Guid Id(int id) => Guid.Parse($"00000000-0000-0000-0000-{id:x12}");
}
