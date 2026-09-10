using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Settings;

/// <summary>
/// A persisted value intentionally absent from the normal player settings UI.
/// The reason is part of the inventory so future changes cannot silently turn
/// an internal or legacy field into an ownerless setting.
/// </summary>
public sealed record SettingExclusion(
    string Id,
    string Reason,
    string? PersistenceFile = null,
    string? PersistenceKey = null);

/// <summary>One legacy parser key that may map to one or more canonical IDs.</summary>
public sealed record SettingAlias(
    string Key,
    IReadOnlyList<string> CanonicalIds,
    string Reason,
    string PersistenceFile = "controls.txt");
