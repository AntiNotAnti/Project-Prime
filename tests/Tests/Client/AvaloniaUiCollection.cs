using Xunit;

namespace MphRead.Tests.Client;

/// <summary>
/// Avalonia owns one application and UI dispatcher per test process. Keep all
/// tests that construct real controls on the same non-parallel collection so
/// focused and full-suite runs exercise the same deterministic lifecycle.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AvaloniaUiCollection
{
    public const string Name = "Avalonia UI";
}
