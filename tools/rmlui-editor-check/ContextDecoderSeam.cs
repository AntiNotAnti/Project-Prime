using System.Collections.Generic;
using System.Threading;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

// This UI/ownership fixture has synthetic file bytes, not a packet decoder.
// Context proof logic and real file decoding are exercised by nettest/CLI.
internal static class ReplayContextAnalysis
{
    internal static IReadOnlyList<ReplayHighlight> Suggestions(string path, IReadOnlyList<ReplayEvent> events,
        uint duration, ReplayHighlightWeights? weights = null, CancellationToken cancellation = default)
        => ReplayStudio.Highlights(events, duration, weights, cancellation);
}
