using System;
using System.Linq;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class DesktopRendererDependencyTests
{
    [Fact]
    public void DesktopClientAssemblyDoesNotReferenceLegacyOpenGlOrGameWindow()
    {
        string[] references = typeof(ScenePresentation).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("OpenTK.Graphics", references, StringComparer.Ordinal);
        Assert.DoesNotContain("OpenTK.Windowing.Desktop", references, StringComparer.Ordinal);
    }
}
