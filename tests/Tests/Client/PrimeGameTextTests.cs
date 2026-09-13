using System;
using System.Linq;
using MphRead;
using MphRead.Formats;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Presentation;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class PrimeGameTextTests
{
    [Theory]
    [InlineData(CareerOutcome.FinishedWin, "Victory")]
    [InlineData(CareerOutcome.FinishedLoss, "Defeat")]
    [InlineData(CareerOutcome.Tie, "Draw")]
    [InlineData(CareerOutcome.Forfeit, "Forfeit")]
    [InlineData(CareerOutcome.DepartedGraceExpired, "Disconnected")]
    [InlineData(CareerOutcome.NoContest, "No Contest")]
    public void EveryCareerOutcomeHasReviewedPlayerCopy(CareerOutcome outcome, string expected)
        => Assert.Equal(expected, PrimeGameText.OutcomeLabel(outcome));

    [Theory]
    [InlineData(MatchMode.Battle, "Battle")]
    [InlineData(MatchMode.TeamBattle, "Team Battle")]
    [InlineData(MatchMode.Survival, "Survival")]
    [InlineData(MatchMode.TeamSurvival, "Team Survival")]
    [InlineData(MatchMode.Capture, "Capture")]
    [InlineData(MatchMode.Bounty, "Bounty")]
    [InlineData(MatchMode.TeamBounty, "Team Bounty")]
    [InlineData(MatchMode.Nodes, "Nodes")]
    [InlineData(MatchMode.TeamNodes, "Team Nodes")]
    [InlineData(MatchMode.Defender, "Defender")]
    [InlineData(MatchMode.TeamDefender, "Team Defender")]
    [InlineData(MatchMode.PrimeHunter, "Prime Hunter")]
    public void EveryMatchModeHasReviewedPlayerCopy(MatchMode mode, string expected)
        => Assert.Equal(expected, PrimeGameText.ModeLabel(mode));

    [Theory]
    [InlineData(Hunter.Samus, "Samus")]
    [InlineData(Hunter.Kanden, "Kanden")]
    [InlineData(Hunter.Trace, "Trace")]
    [InlineData(Hunter.Sylux, "Sylux")]
    [InlineData(Hunter.Noxus, "Noxus")]
    [InlineData(Hunter.Spire, "Spire")]
    [InlineData(Hunter.Weavel, "Weavel")]
    [InlineData(Hunter.Guardian, "Guardian")]
    [InlineData(Hunter.Random, "Random")]
    public void EveryHunterHasReviewedPlayerCopy(Hunter hunter, string expected)
        => Assert.Equal(expected, PrimeGameText.HunterLabel(hunter));

    [Theory]
    [InlineData(BeamType.None, "No weapon")]
    [InlineData(BeamType.PowerBeam, "Power Beam")]
    [InlineData(BeamType.VoltDriver, "Volt Driver")]
    [InlineData(BeamType.Missile, "Missile")]
    [InlineData(BeamType.Battlehammer, "Battlehammer")]
    [InlineData(BeamType.Imperialist, "Imperialist")]
    [InlineData(BeamType.Judicator, "Judicator")]
    [InlineData(BeamType.Magmaul, "Magmaul")]
    [InlineData(BeamType.ShockCoil, "Shock Coil")]
    [InlineData(BeamType.OmegaCannon, "Omega Cannon")]
    [InlineData(BeamType.Platform, "Platform")]
    [InlineData(BeamType.Enemy, "Enemy")]
    public void EveryBeamTypeHasReviewedPlayerCopy(BeamType beam, string expected)
        => Assert.Equal(expected, PrimeGameText.BeamLabel(beam));

    [Theory]
    [InlineData(LobbyPhase.Open, "Open")]
    [InlineData(LobbyPhase.StartingMatch, "Starting")]
    [InlineData(LobbyPhase.InMatch, "In match")]
    [InlineData(LobbyPhase.PostMatch, "Between rounds")]
    [InlineData(LobbyPhase.Closing, "Closing")]
    public void EveryLobbyPhaseHasReviewedPlayerCopy(LobbyPhase phase, string expected)
        => Assert.Equal(expected, PrimeGameText.LobbyPhaseLabel(phase));

    [Theory]
    [InlineData(LobbySeatPolicy.ImmediateSeat, "Immediate seat")]
    [InlineData(LobbySeatPolicy.NextMatchSeat, "Next match seat")]
    [InlineData(LobbySeatPolicy.ObserverUntilNextMatch, "Observer until next match")]
    public void EverySeatPolicyHasReviewedPlayerCopy(LobbySeatPolicy policy, string expected)
        => Assert.Equal(expected, PrimeGameText.SeatPolicyLabel(policy));

    [Fact]
    public void UnknownValuesUseSafeUnavailableCopiesInsteadOfEnumNames()
    {
        Assert.Equal("Outcome unavailable",
            PrimeGameText.OutcomeLabel((CareerOutcome)99));
        Assert.Equal("Mode unavailable",
            PrimeGameText.ModeLabel((MatchMode)99));
        Assert.Equal("Hunter unavailable",
            PrimeGameText.HunterLabel((Hunter)99));
        Assert.Equal("Weapon unavailable",
            PrimeGameText.BeamLabel((BeamType)99));
        Assert.Equal("Match state unavailable",
            PrimeGameText.LobbyPhaseLabel((LobbyPhase)99));
        Assert.Equal("Seat policy unavailable",
            PrimeGameText.SeatPolicyLabel((LobbySeatPolicy)99));
    }

    [Theory]
    [InlineData("MP1 SANCTORUS", "Data Shrine")]
    [InlineData("MP3 PROVING GROUND", "Combat Hall")]
    [InlineData("AD2 ALINOS PERCH", "Alinos Perch")]
    [InlineData("UNIT 4 ARCTERRA BASE", "Arcterra Gateway")]
    [InlineData("COMBAT HALL", "Combat Hall")]
    [InlineData("custom-map", "custom-map")]
    public void KnownMapKeysUseCanonicalNamesAndUnknownKeysRemainNonFabricated(
        string mapKey, string expected)
        => Assert.Equal(expected, PrimeGameText.MapName(mapKey));

    [Fact]
    public void MissingMapKeyUsesSafeUnavailableCopy()
        => Assert.Equal("Map unavailable", PrimeGameText.MapName("  "));

    [Fact]
    public void EveryRetailMultiplayerMapUsesItsAuthoredDisplayName()
    {
        foreach (RoomMetadata room in Metadata.RoomList.Where(room =>
            room.Id is >= 93 and <= 118 && room.Multiplayer
            && !room.FirstHunt && !room.Hybrid))
        {
            Assert.False(String.IsNullOrWhiteSpace(room.InGameName));
            Assert.Equal(room.InGameName, PrimeGameText.MapName(room.Name));
        }
    }

    [Fact]
    public void MatchHistoryUsesReviewedOutcomeMissionAndModeCopies()
    {
        var match = new MatchHistoryEntry(
            Guid.NewGuid(), 12, new DateTimeOffset(2026, 9, 9, 18, 42, 0,
                TimeSpan.Zero), "AD2 ALINOS PERCH", MatchMode.TeamBattle,
            MatchTrustClass.Ranked, Eligible: true, Won: true, Tied: false,
            Outcome: CareerOutcome.FinishedWin, PlayedTicks: 600, Kills: 18,
            Deaths: 7, Assists: 5, Damage: 2441, RatingStatus: "+22 RP");

        PrimeMatchHistoryPresentation row = PrimeMatchHistoryPresentation.From(match);

        Assert.Equal("VICTORY", row.Outcome);
        Assert.Equal("Alinos Perch · Team Battle", row.Mission);
        Assert.DoesNotContain(nameof(CareerOutcome.FinishedWin), row.Outcome,
            StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(MatchMode.TeamBattle), row.Mission,
            StringComparison.Ordinal);

        Assert.Equal("Official match", PrimeMatchHistoryPresentation.From(
            match with { RatingStatus = "applied" }).RatingLine);
        Assert.Equal("Not rating eligible", PrimeMatchHistoryPresentation.From(
            match with { RatingStatus = "ineligible", Eligible = false }).RatingLine);

        PrimeMatchHistoryPresentation unknown =
            PrimeMatchHistoryPresentation.From(match with
            {
                Mode = (MatchMode)99,
                Outcome = (CareerOutcome)99
            });
        Assert.Equal("OUTCOME UNAVAILABLE", unknown.Outcome);
        Assert.Equal("Alinos Perch · Mode unavailable", unknown.Mission);
    }

    [Fact]
    public void SupportedCareerFavoriteDimensionsUseCanonicalLabels()
    {
        Assert.Equal("Alinos Perch", PrimeGameText.CareerChoiceLabel(
            new CareerChoice("AD2 ALINOS PERCH", 4, 4), "map"));
        Assert.Equal("Samus", PrimeGameText.CareerChoiceLabel(
            new CareerChoice("0", 4, 4), "hunter"));
        Assert.Equal("Team Battle", PrimeGameText.CareerChoiceLabel(
            new CareerChoice("TeamBattle", 4, 4), "mode"));
        Assert.Equal("Power Beam", PrimeGameText.CareerChoiceLabel(
            new CareerChoice("0", 4, 4), "weapon"));
        Assert.Equal("Hunter unavailable", PrimeGameText.CareerChoiceLabel(
            new CareerChoice("255", 4, 4), "hunter"));
        Assert.Equal("Mode unavailable", PrimeGameText.CareerChoiceLabel(
            new CareerChoice("255", 4, 4), "mode"));
        Assert.Equal("Weapon unavailable", PrimeGameText.CareerChoiceLabel(
            new CareerChoice("99", 4, 4), "weapon"));
        Assert.Equal("Favorite unavailable", PrimeGameText.CareerChoiceLabel(
            new CareerChoice("internal-key", 4, 4), "future-dimension"));
    }
}
