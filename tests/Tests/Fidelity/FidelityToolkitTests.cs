using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace MphRead.Tests.Fidelity;

public sealed class FidelityToolkitTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("prime-fidelity-toolkit-").FullName;

    [Fact, Trait("Category", "FidelitySmoke")]
    public void NarrowExtractorsAreDeterministicAndPreservePerFieldProvenance()
    {
        FidelityReferenceIdentity identity = CreateReference();

        FidelityExtractReport first = FidelityReferenceExtractor.Extract(_root, "binary-layout", identity);
        FidelityExtractReport second = FidelityReferenceExtractor.Extract(_root, "binary-layout", identity);
        FidelityExtractReport archives = FidelityReferenceExtractor.Extract(_root, "multiplayer-archives", identity);
        FidelityExtractReport audio = FidelityReferenceExtractor.Extract(_root, "audio-sequences", identity);

        Assert.Equal(FidelityJson.Serialize(first), FidelityJson.Serialize(second));
        Assert.Equal(["_bin/arm9.bin", "_bin/overlay9_1"], first.Records.Select(value => value.Key));
        Assert.All(first.Records, value =>
        {
            Assert.Equal(value.Key, value.Provenance.SourcePath);
            Assert.Equal(0, value.Provenance.Offset);
            Assert.Equal("sha256-manifest-v1", value.Provenance.Method);
            Assert.Equal(64, value.Provenance.Sha256.Length);
        });
        Assert.Equal("archives/mp1.arc", Assert.Single(archives.Records).Key);
        Assert.Equal("SEQ_MP1", Assert.Single(audio.Records).Value);
        Assert.Throws<NotSupportedException>(() => FidelityReferenceExtractor.Extract(_root, "everything", identity));
    }

    [Fact, Trait("Category", "FidelitySmoke")]
    public void ExactJsonComparisonIgnoresObjectOrderAndReportsFirstCanonicalDifference()
    {
        FidelityJsonDifference equal = FidelityJsonComparer.Compare("FID-SIM-001",
            "{\"b\":2,\"a\":1}"u8, "{\"a\":1,\"b\":2}"u8);
        FidelityJsonDifference different = FidelityJsonComparer.Compare("FID-SIM-001",
            "{\"b\":2,\"a\":[1,2]}"u8, "{\"a\":[1,3],\"b\":9}"u8);

        Assert.True(equal.Equal);
        Assert.False(different.Equal);
        Assert.Equal("$/a/1", different.Path);
        Assert.Equal("2", different.Expected);
        Assert.Equal("3", different.Actual);
        Assert.Throws<InvalidDataException>(() => FidelityJsonComparer.Compare("invalid", "{}"u8, "{}"u8));
        Assert.Throws<InvalidDataException>(() => FidelityJsonComparer.Compare("FID-SIM-001",
            "{\"a\":1,\"a\":2}"u8, "{}"u8));
        Assert.Throws<InvalidDataException>(() => FidelityJsonComparer.Compare("FID-SIM-001", "{"u8, "{}"u8));
    }

    [Fact, Trait("Category", "FidelitySmoke")]
    public void TrialWorksheetAndProgramReportRetainReferenceIdentityAndStableCounts()
    {
        const string digest = "f64b99e2f976b30e5a667156bd7878f8a43c741bb1a5e02be1c6f40adc513226";
        FidelityCaseDocument first = Case("FID-SIM-001", FidelityDomain.Simulation,
            FidelitySeverity.P1, FidelityStatus.Reproducing, digest);
        FidelityCaseDocument second = Case("FID-MOVE-001", FidelityDomain.Movement,
            FidelitySeverity.P1, FidelityStatus.Candidate, digest);
        File.WriteAllText(Path.Combine(_root, first.Id + ".json"), FidelityCaseParser.Serialize(first));
        File.WriteAllText(Path.Combine(_root, second.Id + ".json"), FidelityCaseParser.Serialize(second));

        FidelityTrialWorksheet worksheet = FidelityTrialWorksheetBuilder.Build(first);
        FidelityProgramReport report = FidelityProgramReportBuilder.Build(_root, "AMHE1", digest);

        Assert.Equal(first.Id, worksheet.CaseId);
        Assert.Equal("not-run", worksheet.Result);
        Assert.Equal(digest, worksheet.ReferenceContentDigest);
        Assert.Equal(["FID-MOVE-001", "FID-SIM-001"], report.Cases.Select(value => value.Id));
        Assert.Equal(1, report.StatusCounts[nameof(FidelityStatus.Candidate)]);
        Assert.Equal(1, report.StatusCounts[nameof(FidelityStatus.Reproducing)]);
        Assert.Equal(2, report.SeverityCounts[nameof(FidelitySeverity.P1)]);
    }

    private FidelityReferenceIdentity CreateReference()
    {
        Directory.CreateDirectory(Path.Combine(_root, "_bin"));
        Directory.CreateDirectory(Path.Combine(_root, "archives"));
        Directory.CreateDirectory(Path.Combine(_root, "_seq"));
        byte[] anchor = [1, 2, 3, 4];
        File.WriteAllBytes(Path.Combine(_root, "_bin", "arm9.bin"), anchor);
        File.WriteAllBytes(Path.Combine(_root, "_bin", "overlay9_1"), [5]);
        File.WriteAllBytes(Path.Combine(_root, "archives", "mp1.arc"), [6]);
        File.WriteAllBytes(Path.Combine(_root, "_seq", "0001 - SEQ_MP1.minincsf"), [7]);
        return new("AMHE1", "_bin/arm9.bin",
            Convert.ToHexStringLower(SHA256.HashData(anchor)));
    }

    private static FidelityCaseDocument Case(string id, FidelityDomain domain,
        FidelitySeverity severity, FidelityStatus status, string digest)
        => new(FidelityCaseParser.SchemaVersion, id, id, domain, severity, status,
            "AMHE1", digest, FidelityEvidenceLevel.E2,
            new Dictionary<string, string> { ["source"] = "test" }, ["Prepare fixture."],
            new FidelityScenario("MP1 SANCTORUS", "Battle", "Classic", 1, 1, 2,
                [new FidelityScheduledAction("step", 0, "player-0", "idle", new Dictionary<string, double>())]),
            [new FidelityExpectedObservation("result", 1, "match", "phase", null, "Playing")],
            [], false, ["Classic"], ["Dedicated"], ["test"], ["test"], "candidate");

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
    }
}
