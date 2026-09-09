using System;
using System.Text.Json.Serialization;

namespace MphRead.Runtime.Content;

/// <summary>
/// The only presentation pack kinds understood by the first local-content
/// catalog.  Adding a new value is an API/schema change, not an invitation to
/// load an arbitrary plug-in.
/// </summary>
public enum OptionalPresentationKind : byte
{
    Announcer = 1,
    Music = 2,
    HudTheme = 3,
    CosmeticEffects = 4
}

/// <summary>Stable identity used when selecting an installed content pack.</summary>
public readonly record struct ContentPackIdentity(string StableId, string Version, string ContentHash)
{
    [JsonIgnore]
    public bool IsValid => ContentManifestValidator.IsIdentity(this);

    public void Validate() => ContentManifestValidator.ValidateIdentity(this);

    public override string ToString() => $"{StableId}@{Version}#{ContentHash}";
}

/// <summary>One immutable file declaration in a local content pack.</summary>
public sealed record ContentFileEntry(string Path, long Bytes, string Sha256);

/// <summary>
/// Gameplay identities are deliberately separate from the pack's stable
/// identity.  They describe the required map/collision/entity/gameplay-data
/// inputs without becoming a second server admission key.
/// </summary>
public sealed record GameplayContentIdentity(
    string Map,
    string Collision,
    string Entities,
    string GameplayData);

/// <summary>
/// Required content declaration.  The server's existing ContentIdentity and
/// Worker content hash remain authoritative for admission; this manifest is a
/// bounded local description and installed-pack integrity check.
/// </summary>
public sealed record GameplayContentManifest(
    int Format,
    string StableId,
    string Version,
    string ContentHash,
    GameplayContentIdentity Gameplay,
    ContentFileEntry[] Files)
{
    public const int CurrentFormat = 1;
    [JsonIgnore]
    public ContentPackIdentity PackIdentity => new(StableId, Version, ContentHash);
}

/// <summary>
/// A semantic event-to-asset mapping for the QZ3 announcer presentation.
/// It contains no executable callback, URI, or client-provided code.
/// </summary>
public sealed record OptionalPresentationEvent(string Key, string Path);

/// <summary>
/// Optional, local, data-only presentation content.  Its identity is never
/// folded into gameplay compatibility or MatchSpec.
/// </summary>
public sealed record OptionalPresentationManifest(
    int Format,
    string StableId,
    string Version,
    string ContentHash,
    OptionalPresentationKind Kind,
    ContentFileEntry[] Files,
    OptionalPresentationEvent[] Events)
{
    public const int CurrentFormat = 1;
    [JsonIgnore]
    public ContentPackIdentity PackIdentity => new(StableId, Version, ContentHash);
}

/// <summary>Manifest file names recognized by local discovery.</summary>
public static class ContentManifestFileNames
{
    public const string Gameplay = "gameplay-manifest.json";
    public const string OptionalPresentation = "optional-presentation-manifest.json";
}

/// <summary>Hard limits applied before allocating or hashing installed files.</summary>
public static class ContentManifestLimits
{
    public const int MaximumManifestBytes = 2 * 1024 * 1024;
    public const int MaximumJsonDepth = 16;
    public const int MaximumManifestItems = 512;
    public const int MaximumFiles = 256;
    public const int MaximumPathLength = 240;
    public const int MaximumIdentityLength = 128;
    public const int MaximumStableIdLength = 96;
    public const int MaximumVersionLength = 64;
    public const long MaximumIndividualFileBytes = 64L * 1024 * 1024;
    public const long MaximumTotalFileBytes = 256L * 1024 * 1024;
    public const int MaximumCatalogEntries = 256;
    public const int MaximumInstalledPacks = 64;
}

/// <summary>Why optional selection used the built-in/default presentation.</summary>
public enum OptionalPresentationFallbackReason : byte
{
    None = 0,
    NotRequested = 1,
    Missing = 2,
    Invalid = 3,
    Mismatch = 4
}

/// <summary>
/// A validated optional pack.  A null RootDirectory denotes the built-in
/// presentation and is intentionally not a downloadable or executable pack.
/// </summary>
public sealed record InstalledOptionalPresentationPack(
    OptionalPresentationManifest Manifest,
    string? RootDirectory,
    bool IsBuiltIn = false)
{
    [JsonIgnore]
    public ContentPackIdentity Identity => Manifest.PackIdentity;
}

/// <summary>A validated required gameplay pack discovered on local disk.</summary>
public sealed record InstalledGameplayPack(GameplayContentManifest Manifest, string RootDirectory)
{
    [JsonIgnore]
    public ContentPackIdentity Identity => Manifest.PackIdentity;
}

/// <summary>Result of optional pack selection.  Pack is null only for built-in fallback.</summary>
public sealed record OptionalPresentationSelection(
    OptionalPresentationKind Kind,
    InstalledOptionalPresentationPack? Pack,
    bool UsedFallback,
    OptionalPresentationFallbackReason FallbackReason)
{
    public static OptionalPresentationSelection BuiltIn(OptionalPresentationKind kind,
        OptionalPresentationFallbackReason reason)
        => new(kind, null, true, reason);
}

/// <summary>A local discovery failure. Invalid optional packs are isolated and skipped.</summary>
public sealed record ContentDiscoveryIssue(string Path, string Reason, bool IsGameplay);

/// <summary>
/// A catalog-level exception is intentionally distinct from JSON parse errors
/// so callers can fail required admission while falling back optional content.
/// </summary>
public sealed class ContentManifestValidationException : InvalidOperationException
{
    public ContentManifestValidationException(string message) : base(message) { }
    public ContentManifestValidationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>Raised when required installed gameplay content cannot be matched exactly.</summary>
public sealed class RequiredContentMismatchException : InvalidOperationException
{
    public RequiredContentMismatchException(string message) : base(message) { }
}
