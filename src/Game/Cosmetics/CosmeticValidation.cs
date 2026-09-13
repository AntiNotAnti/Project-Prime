namespace MphRead.Cosmetics;

public enum CosmeticValidationIssueKind
{
    MissingCatalog,
    CatalogTooLarge,
    ManifestTooLarge,
    MalformedManifest,
    UnsupportedFormat,
    TooManyEntries,
    DuplicateId,
    DuplicateKey,
    InvalidId,
    InvalidKey,
    InvalidDefinition,
    InvalidAssetPath,
    MissingAsset,
    WrongHunter
}

public sealed record CosmeticValidationIssue(
    CosmeticValidationIssueKind Kind,
    string Message,
    string? Key = null,
    string? Path = null);

public enum CosmeticLoadoutIssue : byte
{
    None,
    InvalidSkin,
    SkinHunterMismatch,
    InvalidArmorEffect,
    InvalidDeathEffect,
    DeathEffectHunterMismatch
}

public static class CosmeticPackageLimits
{
    public const int MaximumCatalogBytes = 1024 * 1024;
    public const int MaximumManifestBytes = 256 * 1024;
    public const int MaximumEntriesPerKind = 4096;
    public const int MaximumMaterialOverrides = 256;
    public const int MaximumParticleEmitters = 16;
    public const int MaximumRibbons = 8;
    public const int MaximumAttachments = 8;
    public const float MaximumDeathDurationSeconds = 5;
    public const int MaximumRelativePathLength = 512;
}
