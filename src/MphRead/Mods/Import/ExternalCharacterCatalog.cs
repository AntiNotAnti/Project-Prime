using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.Import;

/// <summary>
/// Stable additional character IDs, separate from the existing seven Hunter
/// enum values. Merely registering an ID does not enable it in any match.
/// </summary>
public sealed record ExternalCharacterDefinition(string Id, string Name,
    Hunter BaseHunter, bool ChangesGameplay, IReadOnlyList<string> SkinSources);

public static class ExternalCharacterCatalog
{
    public static bool EnabledByDefault => false;

    public static IReadOnlyList<ExternalCharacterDefinition> All { get; } =
        Array.AsReadOnly(new[]
        {
            new ExternalCharacterDefinition("darksamus", "DARK SAMUS",
                Hunter.Samus, false,
                Array.AsReadOnly(new[] { "MP2DarkSamus", "MP3DarkSamus" })),
            new ExternalCharacterDefinition("trooper", "TROOPER",
                Hunter.Weavel, true,
                Array.AsReadOnly(new[]
                {
                    "MP1PowerTrooper", "MP1IceTrooper", "MP1WaveTrooper", "MP1PlasmaTrooper"
                })),
        });

    public static ExternalCharacterDefinition? Find(string? id) =>
        All.FirstOrDefault(x => String.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Canonical local-only skin IDs; do not use file paths from peer packets.</summary>
    public static bool AllowsSkin(string? character, string? skin) =>
        Find(character) is { } def && !string.IsNullOrWhiteSpace(skin) &&
        def.SkinSources.Contains(skin, StringComparer.OrdinalIgnoreCase);
}
