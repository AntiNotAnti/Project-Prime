using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Settings;

/// <summary>Controls which runtime can render a descriptor.</summary>
[Flags]
public enum SettingPlatform
{
    None = 0,
    Windows = 1 << 0,
    Linux = 1 << 1,
    MacOs = 1 << 2,
    /// <summary>Compatibility spelling for callers using the platform name.</summary>
    MacOS = MacOs,
    Android = 1 << 3,
    Desktop = Windows | Linux | MacOs,
    All = Desktop | Android
}

/// <summary>How a descriptor is represented by a settings view.</summary>
public enum SettingControlKind
{
    Toggle,
    Choice,
    Slider,
    Text,
    KeyBinding,
    /// <summary>Compatibility spelling retained for metadata consumers.</summary>
    Keybind = KeyBinding,
    GamepadBinding,
    Action,
    Information
}

/// <summary>A stable choice identity and its localized/display label.</summary>
public sealed record SettingChoice(string Id, string Label);

/// <summary>
/// Descriptive metadata for one player-facing setting.
///
/// This type intentionally does not build controls. Persistence identity and
/// dependency metadata stay independent from an individual SettingsView and
/// its MenuSettings instance, so a view can be rebuilt safely during a resume
/// or match transition.
/// </summary>
public sealed record SettingDescriptor
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required SettingCategory Category { get; init; }
    public string? Group { get; init; }
    public required SettingScope Scope { get; init; }
    public required SettingControlKind Kind { get; init; }
    public bool Advanced { get; init; }
    public bool RequiresRestart { get; init; }
    public SettingPlatform Platform { get; init; } = SettingPlatform.All;
    public SettingAvailability Availability { get; init; } = SettingAvailability.Available;
    public string? AvailabilityReason { get; init; }

    /// <summary>
    /// Optional dependency by descriptor ID. A disabled dependent row remains
    /// discoverable and explains which setting enables it.
    /// </summary>
    public string? DependsOn { get; init; }

    /// <summary>
    /// The ID assigned to the actual row in SettingsView. Informational and
    /// action descriptors can omit persistence but still have a row ID.
    /// </summary>
    public string? RowId { get; init; }

    /// <summary>
    /// Persistence is metadata, not a delegate over a MenuSettings object.
    /// Examples are settings.json, controls.txt, and launcher.txt.
    /// </summary>
    public string? PersistenceFile { get; init; }
    public string? PersistenceKey { get; init; }
    public IReadOnlyList<string> PersistenceAliases { get; init; }
        = Array.Empty<string>();

    /// <summary>
    /// A choice list may be supplied by installed content/device discovery.
    /// DynamicChoices allows validation to distinguish that case from a
    /// malformed static descriptor with no choices.
    /// </summary>
    public IReadOnlyList<SettingChoice> Choices { get; init; }
        = Array.Empty<SettingChoice>();
    public bool DynamicChoices { get; init; }

    public object? DefaultValue { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public double? Step { get; init; }

    // These hooks are available to a caller that binds metadata to a current
    // lifecycle explicitly. The registry itself leaves them null; static
    // descriptors must not capture a MenuSettings instance.
    public Func<bool>? VisibleWhen { get; init; }
    public Func<bool>? EnabledWhen { get; init; }
    public Func<string?>? DisabledReason { get; init; }
    public Func<object?>? GetValue { get; init; }
    public Action<object?>? SetValue { get; init; }

    internal string StorageIdentity => PersistenceFile == null || PersistenceKey == null
        ? ""
        : PersistenceFile + "\n" + PersistenceKey;
}
