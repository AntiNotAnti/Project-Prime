using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

// Values also define the bounded native TheatreAction argument. Append; never reorder.
public enum TheatreAction
{
    Search, NextFilter, NextSort, PreviousPage, NextPage, Refresh, Watch, Favorite,
    Validate, Recover, CancelJob, Export, Rename, Organize, Delete, ConfirmDelete,
    CancelDelete, Reveal, Studio, Import, ImportPath, FavoriteFiltered, ValidateFiltered,
    ClearSearch, CancelLaunch
}

public enum TheatreFilter
{
    All, FullReplays, Clips, Favorites, RecentSevenDays, SameMap, SamePlayers,
    Annotated, Highlights, Bookmarks, Organized, LongSessions, ShortClips, NeedsRecovery
}
public enum TheatreSort { Newest, Oldest, Name, Longest }
public enum TheatreOperation { Favorite, Validate, Recover, Export, Rename, Organize, Delete, FavoriteFiltered, ValidateFiltered }
public enum TheatreLibraryState { Loading, Ready, Empty, Failed, Closed }

/// <summary>Immutable metadata copied from the authoritative replay and annotation services.</summary>
public sealed record TheatreEntry
{
    public string Path { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Metadata { get; init; } = "";
    public string Hero { get; init; } = "";
    public DateTime Recorded { get; init; }
    public uint DurationFrames { get; init; }
    public bool IsClip { get; init; }
    public bool VirtualClip { get; init; }
    public bool Favorite { get; init; }
    public bool Recoverable { get; init; }
    public bool Interrupted { get; init; }
    public bool Annotated { get; init; }
    public bool Organized { get; init; }
    public int BookmarkCount { get; init; }
    public int HighlightCount { get; init; }
    public string Room { get; init; } = "";
    public string Players { get; init; } = "";
    public string SearchText { get; init; } = "";
    public string Tags { get; init; } = "";
    public string Collections { get; init; } = "";
    public ImmutableArray<string> PreviewPaths { get; init; } = ImmutableArray<string>.Empty;
}

public sealed record TheatreSnapshot
{
    public Guid Lifetime { get; init; }
    public long Revision { get; init; }
    public TheatreLibraryState State { get; init; }
    public ImmutableArray<TheatreEntry> Entries { get; init; } = ImmutableArray<TheatreEntry>.Empty;
    public ImmutableArray<TheatreEntry> VisibleEntries { get; init; } = ImmutableArray<TheatreEntry>.Empty;
    public TheatreEntry? Selected { get; init; }
    public TheatreFilter Filter { get; init; }
    public TheatreSort Sort { get; init; }
    public string Search { get; init; } = "";
    public int Page { get; init; }
    public int PageCount { get; init; }
    public int TotalCount { get; init; }
    public int FilteredCount { get; init; }
    public int Thumbnail { get; init; }
    public bool Busy { get; init; }
    public bool Recovering { get; init; }
    public bool LaunchPending { get; init; }
    public bool ConfirmDelete { get; init; }
    public string DeletePath { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Insights { get; init; } = "";
    public string Status { get; init; } = "";
    public string Error { get; init; } = "";
    public string LaunchProblem { get; init; } = "";
    public bool DesktopActions { get; init; }
    public int FavoriteTargets { get; init; }
    public int ValidateTargets { get; init; }
    public bool CanManage => State != TheatreLibraryState.Closed && !Busy && !LaunchPending && !ConfirmDelete;
    public bool CanWatch => CanManage && Selected is { Interrupted: false } && LaunchProblem.Length == 0;
}

public readonly record struct TheatreOperationResult(string Status, string? Selection = null, bool Reload = true);
public readonly record struct TheatreLaunchResult(LaunchPlan? Plan, string Error);

/// <summary>Disk work is cancellable and returns copied data; workers never call a UI runtime.</summary>
public interface ITheatreBackend
{
    bool DesktopActions { get; }
    bool CanLaunch(out string reason);
    Task<ImmutableArray<TheatreEntry>> Scan(bool applyStoragePolicy, CancellationToken cancellation);
    Task<TheatreLaunchResult> PreparePlayback(string path, CancellationToken cancellation);
    Task<TheatreOperationResult> Execute(TheatreOperation operation, ImmutableArray<TheatreEntry> targets,
        string name, string tags, string collections, CancellationToken cancellation);
    Task<string?> PickImport(CancellationToken cancellation);
    bool OpenStudio(string path, out string? error);
    void Reveal(string path);
}

// Values also define the bounded native ReplayAction argument. Append; never reorder.
public enum TheatrePlaybackAction
{
    TogglePause, JumpBack, JumpForward, Restart, Step, NextRate, NextCamera,
    PreviousPlayer, NextPlayer, Seek, Studio, Back, Fullscreen
}
public sealed record TheatrePlaybackSnapshot(Guid Lifetime, long Revision, bool Active, uint Frame,
    uint Duration, string State, float Rate, bool PlayAction, string Camera, string Error);
