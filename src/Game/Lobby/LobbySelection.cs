using System;

namespace MphRead;

/// <summary>The map and mode being edited before MatchRules are frozen.</summary>
public readonly record struct LobbySelection
{
    public const int MaximumMapKeyLength = 40;

    public string MapKey { get; }
    public MatchMode Mode { get; }

    public LobbySelection(string mapKey, MatchMode mode)
    {
        if (String.IsNullOrWhiteSpace(mapKey) || mapKey.Length > MaximumMapKeyLength)
            throw new ArgumentException("A bounded map key is required.", nameof(mapKey));
        foreach (char character in mapKey)
            if (character is < ' ' or > '~')
                throw new ArgumentException("Map keys must use printable ASCII.", nameof(mapKey));
        _ = mode.ToLegacyMode();
        MapKey = mapKey;
        Mode = mode;
    }
}
