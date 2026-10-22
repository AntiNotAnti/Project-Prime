using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>A lobby card's visual position is independent of the Hunter wire ID.</summary>
public readonly record struct RmlUiLobbyHunterCard(int Slot, Hunter? Hunter, bool Random = false)
{
    public string ElementId => Hunter is { } hunter ? "hunter_choice" + (int)hunter
        : Random ? "hunter_random" : "hunter_placeholder" + Slot;
    public string Label => Hunter?.ToString().ToUpperInvariant() ?? (Random ? "RANDOM" : "UNAVAILABLE");
}

public static class RmlUiLobbyHunterCards
{
    public static IReadOnlyList<RmlUiLobbyHunterCard> Slots { get; } = Array.AsReadOnly(new[]
    {
        new RmlUiLobbyHunterCard(0, Hunter.Samus),
        new RmlUiLobbyHunterCard(1, null),
        new RmlUiLobbyHunterCard(2, Hunter.Spire),
        new RmlUiLobbyHunterCard(3, Hunter.Kanden),
        new RmlUiLobbyHunterCard(4, Hunter.Trace),
        new RmlUiLobbyHunterCard(5, Hunter.Noxus),
        new RmlUiLobbyHunterCard(6, Hunter.Sylux),
        new RmlUiLobbyHunterCard(7, Hunter.Weavel),
        new RmlUiLobbyHunterCard(8, null),
        new RmlUiLobbyHunterCard(9, null),
        new RmlUiLobbyHunterCard(10, null, Random: true)
    });

    /// <summary>Resolve locally to a playable allowed wire ID; placeholders never enter the pool.</summary>
    public static bool TryRandom(IReadOnlyCollection<Hunter>? allowed, uint entropy, out Hunter hunter)
    {
        Hunter[] pool = Slots.Where(card => card.Hunter.HasValue && allowed?.Contains(card.Hunter.Value) == true)
            .Select(card => card.Hunter!.Value).ToArray();
        hunter = pool.Length == 0 ? Hunter.Samus : pool[entropy % (uint)pool.Length];
        return pool.Length != 0;
    }
}
