using System;
using MphRead.Runtime.Content;

namespace MphRead.Mods.Audio;

/// <summary>
/// Local cosmetic mapping from a semantic announcer event to an asset key.
/// It is bounded, text-only metadata and never participates in match hashes.
/// </summary>
public sealed class AnnouncerPack
{
    public const int MaximumEntries = 32;
    public const int MaximumAssetKeyLength = 96;
    public const int MaximumNameLength = ContentManifestLimits.MaximumStableIdLength;

    private readonly AnnouncerEvent[] _events = new AnnouncerEvent[MaximumEntries];
    private readonly string[] _assets = new string[MaximumEntries];
    private int _count;

    public string Name { get; }
    public int Count => _count;

    public AnnouncerPack(string name = "builtin")
    {
        if (String.IsNullOrWhiteSpace(name) || name.Length > MaximumNameLength)
            throw new ArgumentException("Invalid announcer pack name.", nameof(name));
        Name = name;
    }

    public bool TryMap(AnnouncerEvent value, string assetKey)
    {
        if (value == 0 || !IsSafeAssetKey(assetKey)) return false;
        for (int i = 0; i < _count; i++)
            if (_events[i] == value) { _assets[i] = assetKey; return true; }
        if (_count == MaximumEntries) return false;
        _events[_count] = value; _assets[_count++] = assetKey;
        return true;
    }

    public string Resolve(AnnouncerEvent value)
    {
        for (int i = 0; i < _count; i++)
            if (_events[i] == value) return _assets[i];
        return "builtin:" + value.ToString().ToLowerInvariant();
    }

    public static AnnouncerPack BuiltIn()
    {
        var pack = new AnnouncerPack();
        foreach (AnnouncerEvent value in Enum.GetValues<AnnouncerEvent>()) pack.TryMap(value, "builtin:" + value.ToString().ToLowerInvariant());
        return pack;
    }

    /// <summary>
    /// Builds a local mapping from an already validated QZ6 optional
    /// announcer manifest. Only relative manifest paths are returned; the
    /// selected content owner resolves them under its installed root. Invalid
    /// or non-announcer packs fail closed to built-ins.
    /// </summary>
    public static AnnouncerPack FromOptionalPack(InstalledOptionalPresentationPack? installed)
    {
        if (installed is null || installed.Manifest.Kind != OptionalPresentationKind.Announcer)
            return BuiltIn();
        try
        {
            OptionalPresentationManifest manifest = ContentManifestValidator.ValidateOptional(installed.Manifest);
            var pack = new AnnouncerPack(manifest.StableId);
            foreach (OptionalPresentationEvent mapping in manifest.Events)
            {
                if (!Enum.TryParse(mapping.Key, ignoreCase: true, out AnnouncerEvent value))
                    continue;
                pack.TryMap(value, mapping.Path);
            }
            return pack;
        }
        catch (ContentManifestValidationException)
        {
            return BuiltIn();
        }
        catch (ArgumentException)
        {
            return BuiltIn();
        }
    }

    private static bool IsSafeAssetKey(string value)
    {
        if (String.IsNullOrWhiteSpace(value) || value.Length > MaximumAssetKeyLength || value.Contains("..", StringComparison.Ordinal)) return false;
        foreach (char c in value)
            if (!(Char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '/' or ':')) return false;
        return true;
    }
}
