using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Core;

public enum CommunityTab { Discover, MyMaps, Favorites }
public enum CommunitySort { Name, RecentlyUpdated, Favorites }
public enum CommunityLifecycle { All, Active, Archived, Deleted }
public enum CommunityPageState { Loading, Ready, Empty, Offline, Unauthorized, Failed, Cancelled }
public enum CommunityForm { None, Revisions, Report, Publish, Confirm, Conflict, Import }
public enum CommunityVisibility { Published, Unlisted, Draft }
public enum CommunityDetailAction { Install, InstallAndHost, Favorite, Revisions, Report, Publish, CopyLink, Archive, RestoreMap, DeleteMap }
public enum CommunityCreatorAction { Promote, Unlist, Draft, DeleteRevision, RestoreRevision }
public enum CommunityConflictAction { ReviewLatest, PublishAnyway, DraftBranch, UploadAsRevision, Discard }

public readonly record struct CommunityActionResult(bool Accepted, string Message)
{
    public static CommunityActionResult Ok => new(true, "");
    public static CommunityActionResult Reject(string message) => new(false, message);
}
public readonly record struct CommunityHostRequest(string RoomKey, MapContentIdentity Identity);
public readonly record struct CommunityTransferProgress(long Completed, long Total, string Stage);
public readonly record struct CommunityMapRow(Guid MapId, string Name, string Detail, bool Selected);
public readonly record struct CommunityRevisionRow(int Number, string Name, string Detail, bool Selected, bool Deleted);

/// <summary>No credentials, mutable server arrays, editor documents or toolkit objects cross this snapshot.</summary>
public sealed record CommunitySnapshot
{
    public Guid Lifetime { get; init; }
    public long Revision { get; init; }
    public CommunityPageState State { get; init; }
    public CommunityTab Tab { get; init; }
    public CommunitySort Sort { get; init; }
    public CommunityLifecycle Lifecycle { get; init; }
    public CommunityForm Form { get; init; }
    public string Address { get; init; } = "";
    public string Search { get; init; } = "";
    public string Status { get; init; } = "";
    public string Error { get; init; } = "";
    public string Title { get; init; } = "Select a Community map";
    public string Detail { get; init; } = "";
    public string SelectedPackageHash { get; init; } = "";
    public string Confirmation { get; init; } = "";
    public string PublishSource { get; init; } = "";
    public CommunityVisibility Visibility { get; init; }
    public int ReportReason { get; init; }
    public string Conflict { get; init; } = "";
    public string ConflictCode { get; init; } = "";
    public bool Busy { get; init; }
    public bool CanManage { get; init; }
    public bool CanManageLifecycle { get; init; }
    public bool HasSelection { get; init; }
    public bool Favorited { get; init; }
    public bool HasRevision { get; init; }
    public bool RevisionDeleted { get; init; }
    public bool MapDeleted { get; init; }
    public bool MapArchived { get; init; }
    public bool CanPublish { get; init; }
    public bool PreviousPage { get; init; }
    public bool NextPage { get; init; }
    public bool PreviousRevisionPage { get; init; }
    public bool NextRevisionPage { get; init; }
    public int First { get; init; }
    public int Total { get; init; }
    public int RevisionFirst { get; init; }
    public int RevisionTotal { get; init; }
    public CommunityTransferProgress Progress { get; init; }
    public ImmutableArray<CommunityMapRow> Maps { get; init; } = ImmutableArray<CommunityMapRow>.Empty;
    public ImmutableArray<CommunityRevisionRow> Revisions { get; init; } = ImmutableArray<CommunityRevisionRow>.Empty;
}

public interface ICommunityInstallation : IDisposable
{
    MapContentIdentity Identity { get; }
    /// <summary>Invoked exclusively by CommunityController.Tick on the engine thread.</summary>
    string Commit(CancellationToken cancellation);
}

public interface ICommunityPublication : IDisposable
{
    MapContentIdentity Identity { get; }
    string DisplayName { get; }
    CommunityMapProject? Existing { get; }
}

/// <summary>Existing map/auth/build authorities behind an injectable, toolkit-neutral seam.</summary>
public interface ICommunityBackend
{
    string DefaultAddress { get; }
    void SetAddress(string address);
    MapContentIdentity? Installed(Guid mapId);
    void ValidateInstallation(CommunityMap? package);
    Task<CommunityMapProject[]> BrowseAsync(CommunityTab tab, CancellationToken cancellation);
    Task<CommunityMapRevision[]> RevisionsAsync(Guid map, bool authenticated, CancellationToken cancellation);
    Task<ICommunityInstallation> PrepareInstallAsync(CommunityMap package, Action<CommunityTransferProgress> progress, CancellationToken cancellation);
    Task<ICommunityInstallation> PrepareImportAsync(string path, Action<CommunityTransferProgress> progress, CancellationToken cancellation);
    Task FavoriteAsync(Guid map, bool favorite, CancellationToken cancellation);
    Task ReportAsync(Guid map, MapReportRequest report, CancellationToken cancellation);
    Task ModifyAsync(Guid map, string hash, int revision, CommunityCreatorAction action, CancellationToken cancellation);
    Task ModifyMapAsync(Guid map, CommunityDetailAction action, CancellationToken cancellation);
    Task<ICommunityPublication> PreparePublicationAsync(string path, Action<CommunityTransferProgress> progress, CancellationToken cancellation);
    Task<CommunityPublishResult> PublishAsync(ICommunityPublication publication, CommunityPublishRequest request,
        CommunityVisibility visibility, Action<CommunityTransferProgress> progress, CancellationToken cancellation);
    Task DiscardPublicationAsync(ICommunityPublication publication, CancellationToken cancellation);
}
