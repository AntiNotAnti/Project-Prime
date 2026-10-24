using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Replay;

// Freeze one optional track for the whole exact export, including every seek.
// Preparation happens off-thread; a missing/invalid track is a stable fallback.
internal static class ReplayOwnerExport
{
    private static Task<ReplayOwnerTrack?>? _load;
    private static ReplayOwnerPresentationSource? _source;
    internal static bool Ready => _load?.IsCompleted ?? true;
    internal static ReplayOwnerTrack? Track => _load?.IsCompletedSuccessfully == true ? _load.Result : null;
    internal static void Begin(string source)
    {
        End(); _source = new(source); _load = _source.Load;
    }
    internal static void End()
    {
        _source?.Dispose(); _source = null; _load = null;
    }
}
