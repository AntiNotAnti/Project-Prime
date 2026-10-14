using System;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Content-kind names are stable API identifiers. Maps remain backed by the
/// existing v2 revision catalog, not copied into the generic v3 metadata store.
/// </summary>
public static class CommunityContentKinds
{
    public const string Map = "map";
    public const string Texture = "texture";
    public const string Material = "material";
    public const string Model = "model";
    public const string Prefab = "prefab";

    // Return a fresh public array; callers cannot mutate the process-wide
    // allowlist through the capability/default DTO representation.
    public static string[] Supported => new[] { Map, Texture, Material, Model, Prefab };

    public static bool IsSupported(string? kind)
        => kind is Map or Texture or Material or Model or Prefab;

    public static string Normalize(string? kind)
    {
        string value = kind?.Trim().ToLowerInvariant() ?? "";
        if (!IsSupported(value))
            throw new ArgumentException("Unknown Community content kind.", nameof(kind));
        return value;
    }
}

public static class CommunityContentVisibility
{
    public const string Published = "published";
    public const string Unlisted = "unlisted";
    public const string Draft = "draft";

    public static string Normalize(string? visibility)
    {
        string value = visibility?.Trim().ToLowerInvariant() ?? "";
        if (value is not (Published or Unlisted or Draft))
            throw new ArgumentException("Unknown Community content visibility.", nameof(visibility));
        return value;
    }
}

/// <summary>
/// A content dependency is pinned by the exact immutable SHA-256 identity.
/// PR-10 will implement dependency resolution and cross-content lockfiles.
/// </summary>
public sealed record CommunityContentDependency(
    string Kind, Guid ContentId, string Hash,
    string Role = CommunityDependencyRoles.OptionalVisual);

/// <summary>
/// A particular immutable asset or map archive, with a content digest that
/// never changes when authors edit presentation metadata. For maps, ArchiveHash
/// is the SHA-256 of the exact .ppmap ZIP while ContentHash is its manifest's
/// historical content digest. For other kinds, ContentHash is SHA-256 of the
/// raw asset bytes and ArchiveHash is null.
/// </summary>
public sealed record CommunityContentRevision(
    int RevisionNumber,
    string ContentHash,
    string? ArchiveHash,
    long Bytes,
    string MimeType,
    string Visibility,
    DateTimeOffset CreatedAt,
    string? ParentHash = null,
    string? ReleaseNotes = null,
    CommunityContentDependency[]? Dependencies = null);

/// <summary>
/// Common project-level catalog response. V3 maps are read-through projections
/// over v2; generic asset kinds use a separate immutable metadata catalog.
/// Public responses omit account/creator ownership secrets.
/// </summary>
public sealed record CommunityContentProject(
    string Kind,
    Guid ContentId,
    string Name,
    string? DisplayName,
    string? Author,
    string? OwnerId,
    string? Description,
    string[] Tags,
    string? CurrentHash,
    string LatestHash,
    int RevisionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    CommunityContentRevision? CurrentRevision,
    CommunityContentRevision LatestRevision,
    bool CanManage = false,
    bool Archived = false);

public sealed record CommunityContentPage(
    int SchemaVersion,
    int Total,
    int Page,
    int PageSize,
    CommunityContentProject[] Items);

/// <summary>
/// Trusted service-side immutable metadata registration. There is no public
/// upload endpoint for non-map kinds until the PR-09 validation pipeline.
/// </summary>
public sealed record CommunityContentRegistration(
    string Kind,
    Guid ContentId,
    string OwnerId,
    string Name,
    string? DisplayName,
    string? Author,
    string? Description,
    string[] Tags,
    string MimeType,
    string Visibility,
    string? ExpectedParentHash = null,
    string? ReleaseNotes = null,
    CommunityContentDependency[]? Dependencies = null);

public static class CommunityContentProjection
{
    private const string MapMime = "application/vnd.projectprime.ppmap";

    private static CommunityContentRevision FromMapRevision(CommunityMapRevision revision)
    {
        CommunityMap map = revision.Package;
        return new CommunityContentRevision(
            revision.RevisionNumber,
            map.ContentHash,
            map.Hash,
            map.Bytes,
            MapMime,
            map.Draft ? CommunityContentVisibility.Draft
                : map.Listed ? CommunityContentVisibility.Published
                : CommunityContentVisibility.Unlisted,
            revision.CreatedAt,
            revision.ParentHash,
            revision.ReleaseNotes);
    }

    public static CommunityContentProject FromMap(CommunityMapProject project)
    {
        CommunityContentRevision latest = FromMapRevision(project.LatestRevision);
        CommunityContentRevision? current = project.CurrentRevision == null
            ? null : FromMapRevision(project.CurrentRevision);
        return new CommunityContentProject(
            CommunityContentKinds.Map,
            project.MapId,
            project.Name,
            project.DisplayName,
            project.Author,
            project.CanManageLifecycle ? project.OwnerId : null,
            Description: null,
            Tags: Array.Empty<string>(),
            CurrentHash: project.CurrentHash,
            LatestHash: project.LatestHash,
            RevisionCount: project.RevisionCount,
            CreatedAt: project.CreatedAt,
            UpdatedAt: project.UpdatedAt,
            CurrentRevision: current,
            LatestRevision: latest,
            CanManage: project.CanManageLifecycle,
            Archived: project.ArchivedAt != null);
    }

    public static CommunityContentRevision FromMap(CommunityMapRevision revision)
        => FromMapRevision(revision);
}
