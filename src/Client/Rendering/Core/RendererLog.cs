using System;

namespace MphRead;

/// <summary>
/// Logging boundary owned by the renderer. Composition roots may attach their
/// normal diagnostic sink; the reusable renderer never depends on launcher or
/// updater infrastructure.
/// </summary>
public static class RendererLog
{
    public static Action<string, string>? Sink { get; set; }

    public static void Line(string category, string message)
        => Sink?.Invoke(category, message);
}
