using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;

namespace MphRead.Mods.MapGen;

[JsonConverter(typeof(JsonStringEnumConverter<MapDiagnosticSeverity>))]
public enum MapDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record MapDiagnostic(
    string Code,
    MapDiagnosticSeverity Severity,
    string Message,
    string? ObjectId = null,
    string? SourcePath = null,
    string? SuggestedAction = null);

public sealed class MapDiagnosticBag
{
    private readonly List<MapDiagnostic> _diagnostics = [];

    public void Add(MapDiagnostic diagnostic) => _diagnostics.Add(diagnostic);
    public void AddRange(IEnumerable<MapDiagnostic> diagnostics) => _diagnostics.AddRange(diagnostics);
    public ImmutableArray<MapDiagnostic> ToImmutable() => [.. _diagnostics];
    public bool HasErrors => _diagnostics.Any(d => d.Severity == MapDiagnosticSeverity.Error);
}

public class MapValidationException : ProgramException
{
    public IReadOnlyList<MapDiagnostic> Diagnostics { get; }
    public MapValidationException(string message, IReadOnlyList<MapDiagnostic>? diagnostics = null,
        System.Exception? innerException = null) : base(message, innerException)
        => Diagnostics = diagnostics ?? [];
}

public class MapPackageException : ProgramException
{
    public string Code { get; }
    public MapPackageException(string code, string message, System.Exception? innerException = null)
        : base(message, innerException) => Code = code;
}

public class MapCompilationException : ProgramException
{
    public IReadOnlyList<MapDiagnostic> Diagnostics { get; }
    public MapCompilationException(string message, IReadOnlyList<MapDiagnostic>? diagnostics = null,
        System.Exception? innerException = null) : base(message, innerException)
        => Diagnostics = diagnostics ?? [];
}

public class MapDependencyException : MapCompilationException
{
    public MapDependencyException(string message, IReadOnlyList<MapDiagnostic>? diagnostics = null,
        System.Exception? innerException = null) : base(message, diagnostics, innerException) { }
}
