using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead.Mods.Fidelity;

internal enum FidelityTier { F0, F1, F2, F3, F4, F5 }
internal sealed record FidelityScenario(string Id, int Version, int Ticks, uint Seed,
    string Content, int CaptureInterval, FidelityTier Tier, string Description);
internal sealed record FidelityCheckpoint(int Tick, SortedDictionary<string, long> Values);
internal sealed record FidelityResult(int Format, FidelityScenario Scenario, string EngineRevision,
    string CanonicalHash, List<FidelityCheckpoint> Checkpoints);
internal sealed record FidelityDifference(string Scenario, int? Tick, string Field, string? Expected, string? Actual);

/// <summary>Normalized derived data only. Verification never writes a baseline.</summary>
internal static class FidelityOracle
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        MaxDepth = 24, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    internal static long Normalize(double value, int precision = 4096)
    {
        if (!double.IsFinite(value) || precision <= 0) throw new InvalidDataException("Non-finite oracle value or invalid precision.");
        return checked((long)Math.Round(value * precision, MidpointRounding.AwayFromZero));
    }
    internal static FidelityCheckpoint Point(int tick, params (string Key, long Value)[] fields)
    {
        var values = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var field in fields) values.Add(field.Key, field.Value);
        return new(tick, values);
    }
    internal static FidelityResult Run(string id, string revision, int presentationHz = 60, bool allowContent = false)
    {
        var scenario = FidelityScenarios.All.Concat(FidelityContentScenarios.All).SingleOrDefault(s => s.Id == id)
            ?? throw new ArgumentException("Unknown fidelity scenario: " + id);
        if (scenario.Tier == FidelityTier.F2 && !allowContent) throw new InvalidOperationException("F2 scenarios require explicit -allow-content and local game files.");
        var checkpoints = scenario.Tier == FidelityTier.F2 ? FidelityContentScenarios.Capture(scenario, presentationHz) : FidelityScenarios.Capture(scenario, presentationHz);
        ValidatePoints(scenario, checkpoints);
        return new(1, scenario, revision, Hash(scenario, checkpoints), checkpoints);
    }
    internal static string Hash(FidelityScenario scenario, List<FidelityCheckpoint> checkpoints)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            // Explicit contract: declaration metadata and sorted integer fields, never float memory,
            // dictionary insertion order, machine paths, display names, wall time or engine revision.
            writer.WriteStartArray(); writer.WriteStringValue(scenario.Id);
            writer.WriteNumberValue(scenario.Version); writer.WriteNumberValue(scenario.Ticks);
            writer.WriteNumberValue(scenario.Seed); writer.WriteNumberValue(scenario.CaptureInterval);
            writer.WriteStringValue(scenario.Content); writer.WriteStringValue(scenario.Tier.ToString());
            foreach (var point in checkpoints)
            {
                writer.WriteStartArray(); writer.WriteNumberValue(point.Tick);
                foreach (var field in point.Values.OrderBy(v => v.Key, StringComparer.Ordinal))
                { writer.WriteStringValue(field.Key); writer.WriteNumberValue(field.Value); }
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
        return Convert.ToHexString(SHA256.HashData(memory.ToArray())).ToLowerInvariant();
    }
    internal static FidelityResult Read(string path)
    {
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("Oracle baseline too large.");
        var result = JsonSerializer.Deserialize<FidelityResult>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Missing oracle baseline.");
        if (result.Format != 1 || result.Scenario == null || string.IsNullOrWhiteSpace(result.EngineRevision))
            throw new InvalidDataException("Unsupported oracle baseline.");
        ValidatePoints(result.Scenario, result.Checkpoints);
        if (result.CanonicalHash != Hash(result.Scenario, result.Checkpoints)) throw new InvalidDataException("Oracle baseline hash mismatch.");
        return result;
    }
    private static void ValidatePoints(FidelityScenario scenario, List<FidelityCheckpoint> checkpoints)
    {
        if (scenario.Version < 1 || scenario.Ticks < 1 || scenario.Ticks > 100000 || scenario.CaptureInterval < 1
            || !Enum.IsDefined(scenario.Tier) || string.IsNullOrWhiteSpace(scenario.Id) || checkpoints == null
            || checkpoints.Count == 0 || checkpoints.Count > 100001) throw new InvalidDataException("Invalid oracle contract.");
        int previous = -1;
        foreach (var point in checkpoints)
        {
            if (point == null || point.Tick <= previous || point.Tick > scenario.Ticks || point.Values == null
                || point.Values.Count == 0 || point.Values.Count > 256 || point.Values.Keys.Any(k => k.Length is 0 or > 128))
                throw new InvalidDataException("Invalid normalized checkpoint.");
            previous = point.Tick;
        }
    }
    internal static FidelityDifference? Compare(FidelityResult expected, FidelityResult actual)
    {
        if (expected.Format != actual.Format || expected.Scenario != actual.Scenario)
            return new(actual.Scenario.Id, null, "scenario contract", JsonSerializer.Serialize(expected.Scenario, Json), JsonSerializer.Serialize(actual.Scenario, Json));
        for (int i = 0; i < Math.Max(expected.Checkpoints.Count, actual.Checkpoints.Count); i++)
        {
            var e = i < expected.Checkpoints.Count ? expected.Checkpoints[i] : null;
            var a = i < actual.Checkpoints.Count ? actual.Checkpoints[i] : null;
            if (e?.Tick != a?.Tick) return new(actual.Scenario.Id, a?.Tick ?? e?.Tick, "checkpoint.tick", e?.Tick.ToString(CultureInfo.InvariantCulture), a?.Tick.ToString(CultureInfo.InvariantCulture));
            foreach (string field in e!.Values.Keys.Union(a!.Values.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                string? ev = e.Values.TryGetValue(field, out long v) ? v.ToString(CultureInfo.InvariantCulture) : null;
                string? av = a.Values.TryGetValue(field, out v) ? v.ToString(CultureInfo.InvariantCulture) : null;
                if (ev != av) return new(actual.Scenario.Id, a.Tick, field, ev, av);
            }
        }
        return null;
    }
}
