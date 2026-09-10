using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MphRead;
using Xunit;

namespace MphRead.Tests.Fidelity;

public sealed class FidelityCaseTests : IDisposable
{
    private const string Digest = "f64b99e2f976b30e5a667156bd7878f8a43c741bb1a5e02be1c6f40adc513226";
    private readonly string _root = Directory.CreateTempSubdirectory("prime-fidelity-case-").FullName;

    [Fact, Trait("Category", "FidelitySmoke")]
    public void VersionedCaseRoundTripsWithStrictIdentity()
    {
        FidelityCaseDocument actual = Parse(Valid());
        Assert.Equal("FID-SIM-001", actual.Id); Assert.Single(actual.Scenario.Actions);
        Assert.Single(actual.ExpectedObservations); Assert.Single(actual.Tolerances);
    }

    [Fact]
    public void MalformedUnknownOversizedAndWrongReferenceFailClosed()
    {
        Assert.Throws<InvalidDataException>(() => FidelityCaseParser.Parse("{"u8, "AMHE1", Digest));
        string unknown = FidelityCaseParser.Serialize(Valid()).Replace("\"decision\": \"candidate\"", "\"decision\": \"candidate\", \"unknown\": true", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => FidelityCaseParser.Parse(Encoding.UTF8.GetBytes(unknown), "AMHE1", Digest));
        Assert.Throws<InvalidDataException>(() => FidelityCaseParser.Parse(new byte[FidelityCaseParser.MaximumFixtureBytes + 1], "AMHE1", Digest));
        byte[] valid = Encoding.UTF8.GetBytes(FidelityCaseParser.Serialize(Valid()));
        Assert.Throws<InvalidDataException>(() => FidelityCaseParser.Parse(valid, "AMHE1", new string('0', 64)));
    }

    [Fact]
    public void DuplicateIdsNonfiniteAndScenarioBoundsAreRejected()
    {
        FidelityCaseDocument value = Valid(); FidelityScheduledAction action = value.Scenario.Actions[0];
        Assert.Throws<InvalidDataException>(() => Parse(value with { Scenario = value.Scenario with { Actions = [action, action] } }));
        string nonfinite = FidelityCaseParser.Serialize(value).Replace("\"numericValue\": 1", "\"numericValue\": 1e999", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => FidelityCaseParser.Parse(Encoding.UTF8.GetBytes(nonfinite), "AMHE1", Digest));
        Assert.Throws<InvalidDataException>(() => Parse(value with { Scenario = value.Scenario with { PlayerCount = 9 } }));
        Assert.Throws<InvalidDataException>(() => Parse(value with { Scenario = value.Scenario with { PlayerCount = 0 } }));
        Assert.Throws<InvalidDataException>(() => Parse(value with { Scenario = value.Scenario with { MaxTicks = FidelityCaseParser.MaximumTicks + 1 } }));
        Assert.Throws<InvalidDataException>(() => Parse(value with { Scenario = value.Scenario with
        {
            Actions = [action with { Tick = value.Scenario.MaxTicks }]
        } }));
        Assert.Throws<InvalidDataException>(() => Parse(value with { Tolerances = [new("position.x", -0.1, "meters", "invalid")] }));
    }

    [Theory, Trait("Category", "FidelitySmoke")]
    [InlineData("\"status\": \"Candidate\"", "\"status\": \"Rejected\", \"status\": \"Candidate\"")]
    [InlineData("\"seed\": 12345", "\"seed\": 1, \"seed\": 12345")]
    [InlineData("\"source\": \"static\"", "\"source\": \"other\", \"source\": \"static\"")]
    [InlineData("\"durationTicks\": 120", "\"durationTicks\": 1, \"durationTicks\": 120")]
    [InlineData("\"status\": \"Candidate\"", "\"st\\u0061tus\": \"Rejected\", \"status\": \"Candidate\"")]
    public void DuplicateJsonPropertiesFailClosed(string property, string duplicate)
    {
        string valid = FidelityCaseParser.Serialize(Valid());
        Assert.Contains(property, valid);
        string json = valid.Replace(property, duplicate, StringComparison.Ordinal);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            FidelityCaseParser.Parse(Encoding.UTF8.GetBytes(json), "AMHE1", Digest));

        Assert.IsType<System.Text.Json.JsonException>(exception.InnerException);
    }

    [Fact]
    public void LoaderRejectsTraversalAndSymbolicLinks()
    {
        File.WriteAllText(Path.Combine(_root, "case.json"), FidelityCaseParser.Serialize(Valid()));
        Assert.Equal("FID-SIM-001", FidelityCaseParser.Load(_root, "case.json", "AMHE1", Digest).Id);
        Assert.Throws<InvalidDataException>(() => FidelityCaseParser.Load(_root, "../case.json", "AMHE1", Digest));
        string outside = Path.Combine(Path.GetTempPath(), $"prime-fidelity-case-outside-{Guid.NewGuid():N}.json");
        File.WriteAllText(outside, FidelityCaseParser.Serialize(Valid()));
        try { File.CreateSymbolicLink(Path.Combine(_root, "link.json"), outside); Assert.Throws<InvalidDataException>(() => FidelityCaseParser.Load(_root, "link.json", "AMHE1", Digest)); }
        finally { File.Delete(outside); }
    }

    private static FidelityCaseDocument Parse(FidelityCaseDocument value) => FidelityCaseParser.Parse(Encoding.UTF8.GetBytes(FidelityCaseParser.Serialize(value)), "AMHE1", Digest);
    private static FidelityCaseDocument Valid() => new(FidelityCaseParser.SchemaVersion, "FID-SIM-001", "Bounded idle simulation",
        FidelityDomain.Simulation, FidelitySeverity.P1, FidelityStatus.Candidate, "AMHE1", Digest, FidelityEvidenceLevel.E2,
        new Dictionary<string, string> { ["source"] = "static" }, ["Load the retail room."],
        new FidelityScenario("MP1 SANCTORUS", "Battle", "Classic", 1, 12345, 120,
            [new FidelityScheduledAction("idle-001", 0, "player-0", "idle", new Dictionary<string, double> { ["durationTicks"] = 120 })]),
        [new FidelityExpectedObservation("frame-001", 119, "match", "frame", 1, null)],
        [new FidelityTolerance("position.x", 0.001, "world units", "fixed-point conversion")], false,
        ["Classic"], ["Practice", "Dedicated"], ["FidelityScenarioTests.Idle"], ["docs/fidelity/cases/FID-SIM-001.md"], "candidate");
    public void Dispose() { try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { } }
}
