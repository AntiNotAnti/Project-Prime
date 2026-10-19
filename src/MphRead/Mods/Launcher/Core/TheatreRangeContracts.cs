#if !MPHREAD_SERVER
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

public sealed record TheatreRangeContext(TheatreBookmarkContext Replay, uint? MarkIn, uint? MarkOut);
public readonly record struct TheatreRangeSelection(uint? MarkIn, uint? MarkOut, uint Duration);
public sealed record TheatreRangeSnapshot(uint? MarkIn, uint? MarkOut, uint Duration, string ReplayName,
    bool Current, bool ValidRange, string Status, string Error)
{
    public TheatreRangeSelection? LiveSelection { get; init; }
    public bool SavingClip { get; init; }
    public bool MaterializedClipsAvailable { get; init; }
}
public sealed record TheatreMaterializedClipResult(bool Saved, string OutputPath, string Error);

/// <summary>Existing transport marks and replay annotation/virtual-clip services own all changes.</summary>
public interface ITheatreRangeBackend
{
    TheatreRangeContext Capture();
    bool IsCurrent(TheatreRangeContext context);
    TheatreRangeSelection ReadSelection(TheatreRangeContext context);
    void MarkIn(TheatreRangeContext context);
    void MarkOut(TheatreRangeContext context);
    string SaveHighlight(TheatreRangeContext context, string name);
    string CreateVirtualClip(TheatreRangeContext context, string name);
    bool MaterializedClipsAvailable => false;
    Task<TheatreMaterializedClipResult> SaveMaterializedClip(TheatreRangeContext context, string name,
        CancellationToken cancellation) => Task.FromResult(new TheatreMaterializedClipResult(false, "", "Materialized clip saving is unavailable."));
}
#endif
