using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using MphRead;
using Xunit;

namespace MphRead.Tests.Fidelity;

public sealed class OracleTests
{
    [Fact, Trait("Category", "FidelitySchema")]
    public void ScenarioParserRejectsUnknownDuplicateAndNonFiniteInput()
    {
        string valid = ScenarioJson();
        OracleScenario scenario = OracleJson.ParseScenario(Encoding.UTF8.GetBytes(valid));
        Assert.Equal("FID-RETAIL-SAMUS-001", scenario.Id);

        Assert.Throws<InvalidDataException>(() => OracleJson.ParseScenario(
            Encoding.UTF8.GetBytes(valid.Replace("\"seed\": 123", "\"seed\": 123, \"extra\": true", StringComparison.Ordinal))));
        Assert.Throws<InvalidDataException>(() => OracleJson.ParseScenario(
            Encoding.UTF8.GetBytes(valid.Replace("\"seed\": 123", "\"seed\": 123, \"seed\": 124", StringComparison.Ordinal))));
        Assert.Throws<InvalidDataException>(() => OracleJson.ParseScenario(
            Encoding.UTF8.GetBytes(valid.Replace("\"x\": 1", "\"x\": 1e999", StringComparison.Ordinal))));
    }

    [Fact, Trait("Category", "FidelitySchema")]
    public void ArtifactParserRejectsDuplicateCheckpointAndWrongReference()
    {
        string artifact = ArtifactJson();
        OracleArtifact parsed = OracleJson.ParseArtifact(Encoding.UTF8.GetBytes(artifact));
        Assert.Single(parsed.Checkpoints);
        // Duplicate checkpoint identity is produced by repeating the object,
        // which must fail before a comparison can inspect any state.
        const string secondCheckpoint = "{\n"
            + "      \"vblank\": 0,\n"
            + "      \"elapsedMilliseconds\": 0,\n"
            + "      \"semanticEventIndex\": 0,\n"
            + "      \"state\": { \"health\": 100 }\n"
            + "    }";
        int eventsStart = artifact.IndexOf("\"events\": []", StringComparison.Ordinal);
        int checkpointsEnd = artifact.LastIndexOf(']', eventsStart);
        Assert.True(eventsStart > 0 && checkpointsEnd > 0);
        string duplicate = artifact.Insert(checkpointsEnd, ",\n    " + secondCheckpoint);
        Assert.Throws<InvalidDataException>(() => OracleJson.ParseArtifact(
            Encoding.UTF8.GetBytes(duplicate)));
        Assert.Throws<InvalidDataException>(() => OracleJson.ParseArtifact(
            Encoding.UTF8.GetBytes(artifact.Replace("\"AMHE1\"", "\"AMHE0\"", StringComparison.Ordinal))));
    }

    [Fact, Trait("Category", "FidelitySchema")]
    public void OracleSerializationUsesCanonicalVblankAndRoundTripsStrictly()
    {
        OracleScenario scenario = OracleJson.ParseScenario(
            Encoding.UTF8.GetBytes(ScenarioJson()));
        string serializedScenario = OracleJson.SerializeScenario(scenario);
        Assert.Contains("\"vblank\"", serializedScenario, StringComparison.Ordinal);
        Assert.DoesNotContain("\"vBlank\"", serializedScenario, StringComparison.Ordinal);
        OracleScenario reparsedScenario = OracleJson.ParseScenario(
            Encoding.UTF8.GetBytes(serializedScenario));
        Assert.Equal(scenario.Checkpoints[0], reparsedScenario.Checkpoints[0]);

        OracleArtifact artifact = Artifact(Source(), 0);
        string serializedArtifact = OracleJson.SerializeArtifact(artifact);
        Assert.Contains("\"vblank\"", serializedArtifact, StringComparison.Ordinal);
        Assert.DoesNotContain("\"vBlank\"", serializedArtifact, StringComparison.Ordinal);
        OracleArtifact reparsedArtifact = OracleJson.ParseArtifact(
            Encoding.UTF8.GetBytes(serializedArtifact));
        Assert.Equal(artifact.Checkpoints[0].VBlank, reparsedArtifact.Checkpoints[0].VBlank);
        Assert.Equal(artifact.Checkpoints[0].ElapsedMilliseconds,
            reparsedArtifact.Checkpoints[0].ElapsedMilliseconds);
        Assert.Equal(artifact.Events[0].VBlank, reparsedArtifact.Events[0].VBlank);
    }

    [Fact, Trait("Category", "FidelitySchema")]
    public void ComparerUsesTolerancesAndReportsFirstDivergence()
    {
        OracleSourceIdentity source = Source();
        OracleArtifact expected = Artifact(source, 0.0);
        OracleArtifact near = Artifact(source, 0.00005);
        OracleComparison tolerant = OracleComparer.Compare(expected, near);
        Assert.True(tolerant.Compatible);
        Assert.True(tolerant.Equal);

        OracleArtifact far = Artifact(source, 0.5);
        OracleComparison mismatch = OracleComparer.Compare(expected, far);
        Assert.True(mismatch.Compatible);
        Assert.False(mismatch.Equal);
        Assert.Equal("position.x", mismatch.FirstDivergence?.Path);
    }

    [Fact, Trait("Category", "FidelitySchema")]
    public void NormalizerKeepsExactTolerantAndInvariantRulesExplicit()
    {
        string normalizer = """
        {
          "schemaVersion": 1,
          "revision": "AMHE1",
          "fields": [
            { "path": "position", "mode": "Tolerant", "absoluteTolerance": 0.1, "invariant": null },
            { "path": "health", "mode": "Exact", "absoluteTolerance": null, "invariant": null },
            { "path": "phase", "mode": "Invariant", "absoluteTolerance": null, "invariant": "nonEmpty" }
          ]
        }
        """;
        OracleNormalizer parsed = OracleJson.ParseNormalizer(Encoding.UTF8.GetBytes(normalizer));
        Assert.Equal(OracleComparisonMode.Tolerant, parsed.Fields[0].Mode);
        Assert.Equal(OracleComparisonMode.Exact, parsed.Fields[1].Mode);
        Assert.Equal("nonEmpty", parsed.Fields[2].Invariant);

        Assert.Throws<InvalidDataException>(() => OracleJson.ParseNormalizer(
            Encoding.UTF8.GetBytes(normalizer.Replace("\"nonEmpty\"", "\"unknown\"", StringComparison.Ordinal))));
    }

    [Fact, Trait("Category", "FidelitySchema")]
    public void VerifyCommandRequiresContentReferenceForVerificationClaim()
    {
        string path = Path.Combine(Path.GetTempPath(), "prime-oracle-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, ArtifactJson(), Encoding.UTF8);
            Assert.Equal(1, FidelityCommand.Run(["oracle", "verify", path]));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact, Trait("Category", "FidelitySchema")]
    public void RomVerifierRejectsHeaderCompatibleImageWithWrongDigest()
    {
        string path = Path.Combine(Path.GetTempPath(), "prime-oracle-rom-"
            + Guid.NewGuid().ToString("N") + ".nds");
        try
        {
            using (FileStream stream = new(path, FileMode.CreateNew, FileAccess.ReadWrite,
                       FileShare.Read))
            {
                stream.SetLength(OracleJson.Amhe1RomIdentity.Size);
                stream.Position = MphRead.CartridgeHeader.GameCodeOffset;
                stream.Write(Encoding.ASCII.GetBytes("AMHE"));
                stream.Position = MphRead.CartridgeHeader.RevisionOffset;
                stream.WriteByte(1);
            }

            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => OracleHost.VerifyRom(path));
            Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact, Trait("Category", "FidelitySchema")]
    public void ConfiguredPrivateAmhe1RomMatchesSharedCatalog()
    {
        string? path = Environment.GetEnvironmentVariable("PRIME_AMHE1_ROM");
        if (String.IsNullOrWhiteSpace(path)) return;

        OracleRomIdentity identity = OracleHost.VerifyRom(path);
        CartridgeIdentity expected = OracleJson.Amhe1RomIdentity;
        Assert.Equal("sha256:" + expected.Sha256, identity.Identity);
        Assert.Equal(expected.GameCode, identity.GameCode);
        Assert.Equal(expected.Revision, identity.RomVersion);
        Assert.Equal(expected.Size, identity.Length);
    }

    [Fact, Trait("Category", "FidelitySchema")]
    public void RecordedCaptureRequiresBoundedDualScreenRgbaPng()
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-oracle-capture-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        OracleArtifact artifact = Artifact(Source(), 0);
        string path = Path.Combine(root, "capture-0.png");
        try
        {
            Assert.Throws<InvalidDataException>(() =>
                OracleHost.ValidateRecordedCaptures(root, artifact));

            byte[] png = new byte[33];
            byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
            signature.CopyTo(png, 0);
            png[11] = 13;
            Encoding.ASCII.GetBytes("IHDR").CopyTo(png, 12);
            png[18] = 1; // width: 256
            png[22] = 1; // height: 384
            png[23] = 128;
            png[24] = 8;
            png[25] = 6;
            File.WriteAllBytes(path, png);
            OracleHost.ValidateRecordedCaptures(root, artifact);

            png[22] = 0;
            png[23] = 192; // wrong height: 192
            File.WriteAllBytes(path, png);
            Assert.Throws<InvalidDataException>(() =>
                OracleHost.ValidateRecordedCaptures(root, artifact));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static OracleArtifact Artifact(OracleSourceIdentity source, double positionX)
    {
        var state = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["position"] = JsonDocument.Parse($"{{\"x\":{positionX.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}").RootElement.Clone(),
            ["phase"] = JsonDocument.Parse("\"match\"").RootElement.Clone()
        };
        var manifest = new OracleArtifactManifest(1, "FID-RETAIL-SAMUS-001", source,
            "test-adapter", "unverified:no-full-rom", "test", "sha256:" + new string('a', 64),
            new string('b', 64), 123, DateTimeOffset.UnixEpoch, "normalized");
        return new OracleArtifact(1, manifest,
            [new OracleCheckpoint(0, 0, 0, state)],
            [new OracleEvent(0, "spawn", 0, 0)]);
    }

    private static OracleSourceIdentity Source()
        => new(OracleJson.DefaultProjectRevision, OracleJson.Amhe1Revision,
            OracleJson.Amhe1AnchorPath, OracleJson.Amhe1AnchorSha256,
            OracleJson.Amhe1AggregateSha256);

    private static string ScenarioJson()
        => $$"""
        {
          "schemaVersion": 1,
          "id": "FID-RETAIL-SAMUS-001",
          "source": {
            "projectRevision": "{{OracleJson.DefaultProjectRevision}}",
            "referenceRevision": "AMHE1",
            "referenceAnchorPath": "_bin/arm9.bin",
            "referenceAnchorSha256": "{{OracleJson.Amhe1AnchorSha256}}",
            "referenceAggregateSha256": "{{OracleJson.Amhe1AggregateSha256}}"
          },
          "hunter": "Samus",
          "map": "MP1 SANCTORUS",
          "seed": 123,
          "actions": [
            { "at": 0, "action": "move", "values": { "x": 1 } }
          ],
          "checkpoints": [
            { "vblank": 0, "elapsedMilliseconds": 0, "semanticEventIndex": 0 }
          ]
        }
        """;

    private static string ArtifactJson()
        => $$"""
        {
          "schemaVersion": 1,
          "manifest": {
            "schemaVersion": 1,
            "scenarioId": "FID-RETAIL-SAMUS-001",
            "source": {
              "projectRevision": "{{OracleJson.DefaultProjectRevision}}",
              "referenceRevision": "AMHE1",
              "referenceAnchorPath": "_bin/arm9.bin",
              "referenceAnchorSha256": "{{OracleJson.Amhe1AnchorSha256}}",
              "referenceAggregateSha256": "{{OracleJson.Amhe1AggregateSha256}}"
            },
            "oracleImplementationRevision": "test-adapter",
            "romIdentity": "unverified:no-full-rom",
            "hostOs": "test",
            "oracleExecutableIdentity": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "scenarioHash": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "seed": 123,
            "runTimestamp": "1970-01-01T00:00:00+00:00",
            "result": "normalized"
          },
          "checkpoints": [
            {
              "vblank": 0,
              "elapsedMilliseconds": 0,
              "semanticEventIndex": 0,
              "state": { "health": 100 }
            }
          ],
          "events": []
        }
        """;
}
