using System;
using System.Collections.Generic;

namespace MphRead.Mods.MapEditor;

public enum MapAssetDragKind { Material, Texture, Audio, Model, Prefab }
/// <summary>A process-local browser reference, bound to its authoritative map owner.</summary>
public sealed record MapAssetDragData(Guid MapId,MapAssetDragKind Kind,string Key)
{
    public MapAssetDragContext? Context { get; init; }
}
public sealed record MapAssetSourceStamp(string Path,long Length,long LastWriteUtcTicks);
public sealed record MapAssetDragContext(Guid OwnerId,DocumentStateId State,string? FilePath,
    string? BaseDirectory,string? SourcePath,string? BundlePath,string? SourceRevision,
    IReadOnlyList<MapAssetSourceStamp> SourceFiles);
