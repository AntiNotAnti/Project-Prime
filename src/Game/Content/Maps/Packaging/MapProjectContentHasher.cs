using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Computes the logical identity of editable map source plus every external
/// dependency that can affect compilation. Physical paths and timestamps are
/// deliberately excluded.
/// </summary>
public static class MapProjectContentHasher
{
    public static string Compute(MapProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        byte[] canonical = CanonicalProject(project);
        var hashes = new List<byte>(canonical);
        foreach ((string name, string hash) in Dependencies(project)
            .OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            hashes.AddRange(Encoding.UTF8.GetBytes(name));
            hashes.Add(0);
            hashes.AddRange(Convert.FromHexString(hash));
        }
        return MapJson.Sha256(CollectionsMarshal.AsSpan(hashes));
    }

    public static string ComputeCanonicalProject(MapProject project)
        => MapJson.Sha256(CanonicalProject(project));

    private static byte[] CanonicalProject(MapProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return MapJson.Canonicalize(
            JsonSerializer.SerializeToUtf8Bytes(project, MapJsonContext.Default.MapProject));
    }

    public static IReadOnlyList<(string Name, string Hash)> Dependencies(MapProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        MapDependencyAnalysis analysis = MapDependencyAnalyzer.Analyze(project);
        ImmutableArray<MapDiagnostic> errors = analysis.Diagnostics
            .Where(value => value.Severity == MapDiagnosticSeverity.Error).ToImmutableArray();
        if (!errors.IsEmpty)
            throw new MapDependencyException(errors[0].Message, errors);
        return analysis.Dependencies
            .Where(value => value.AffectsBuild && value.Hash != null)
            .DistinctBy(value => (value.LogicalName, value.Hash),
                EqualityComparer<(string LogicalName, string? Hash)>.Default)
            .Select(value => (value.LogicalName, value.Hash!)).ToArray();
    }
}
