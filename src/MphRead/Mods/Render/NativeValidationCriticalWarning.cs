#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Threading;

namespace MphRead.Mods.Render;

// A native once-only fence warning must survive the unrelated warning flood.
// The original native event remains verbatim and is not a completion assertion.
internal sealed class NativeValidationCriticalWarning
{
    internal const string Prefix = "GPU fence wait incomplete: wait_completed=false requested=";
    private int _attempted;
    internal static bool Matches(string detail) => detail.StartsWith(Prefix, StringComparison.Ordinal);
    internal bool TryForward(bool validationEnabled, string detail, TextWriter writer)
    {
        if (!validationEnabled || !Matches(detail)
            || Interlocked.CompareExchange(ref _attempted, 1, 0) != 0) return false;
        return ShaderDiagnosticPolicy.Write(writer, "[wgpu-validation-critical] Warn: " + detail);
    }
}
#endif
