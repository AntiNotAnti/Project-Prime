#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;

namespace MphRead.Mods.Launcher.Core;

public sealed record TheatreBookmarkEntry(Guid Id, uint Frame, string Name, DateTime CreatedUtc);
public sealed record TheatreBookmarkContext(long SceneGeneration, string LogicalPath, string PlaybackPath,
    string RecordingRevision, string AnnotationRevision, uint Frame, uint Duration);
public sealed record TheatreBookmarkSnapshot(ImmutableArray<TheatreBookmarkEntry> Entries, int SelectedIndex,
    TheatreBookmarkEntry? Selected, TheatreBookmarkEntry? PendingRemoval, string Status, string Error, bool Current,
    string ReplayName);

/// <summary>Existing replay and annotation services remain the only playback and file owners.</summary>
public interface ITheatreBookmarkBackend
{
    TheatreBookmarkContext Capture();
    bool IsCurrent(TheatreBookmarkContext context);
    ImmutableArray<TheatreBookmarkEntry> Read(TheatreBookmarkContext context);
    void Add(TheatreBookmarkContext context, string name);
    void Remove(TheatreBookmarkContext context, TheatreBookmarkEntry exact);
    void Seek(TheatreBookmarkContext context, TheatreBookmarkEntry exact);
}
#endif
