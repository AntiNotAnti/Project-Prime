using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>Selection only; discovery and server admission remain authoritative.</summary>
public static class RmlUiRandomMatch
{
    public static RmlUiServerCandidate? Choose(IEnumerable<RmlUiServerCandidate> rows, Func<int, int> next)
    {
        var eligible = rows.Where(row => row.Compatible && row.Capacity > 0
            && row.Players < row.Capacity && !String.IsNullOrWhiteSpace(row.Endpoint))
            .DistinctBy(row => row.Endpoint, StringComparer.OrdinalIgnoreCase).ToArray();
        return eligible.Length == 0 ? null : eligible[next(eligible.Length)];
    }
}
