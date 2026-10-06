using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public sealed partial class MapDocument
{
    private int _prefabPreparationCount;
    /// <summary>Prepare from a dispatcher-captured immutable snapshot. Apply and dispose on the document owner.</summary>
    public Task<PreparedPrefabEdit> PreparePrefabInsertAsync(string path, string destinationRoot, MapTransform? transform = null,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        var frozenTransform = MapSnapshotCopy.Copy(transform ?? new MapTransform());
        return PreparePrefabAsync("Insert prefab", destinationRoot, (definition, stage, prefix) =>
            MapPrefabService.InsertLinkedPrepared(definition, path, stage, frozenTransform, prefix), selectResult: true, cancellation);
    }

    public Task<PreparedPrefabEdit> PreparePrefabUpdateAsync(Guid instanceId, string destinationRoot, CancellationToken cancellation = default)
        => PreparePrefabAsync("Update prefab", destinationRoot, (definition, stage, prefix) =>
            MapPrefabService.UpdatePrepared(definition, instanceId, stage, prefix), selectResult: false, cancellation);

    private Task<PreparedPrefabEdit> PreparePrefabAsync(string label, string destinationRoot,
        Func<MapDefinition, string, string, MapPrefabService.InstanceResult> operation, bool selectResult, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        string root = Path.GetFullPath(destinationRoot), id = Guid.NewGuid().ToString("N"), prefix = "prefab-assets/" + id;
        if (Interlocked.Increment(ref _prefabPreparationCount) > 2)
        { Interlocked.Decrement(ref _prefabPreparationCount); throw new InvalidOperationException("Two prefab preparations are already pending. Adopt or dispose one first."); }
        MapBuildSnapshot snapshot;
        try { snapshot = CaptureBuildSnapshot(); }
        catch { Interlocked.Decrement(ref _prefabPreparationCount); throw; }
        var state = CurrentStateId; int thread = Environment.CurrentManagedThreadId;
        var context = (FilePath, Project.Definition.BaseDirectory, Project.Definition.SourcePath, Project.Definition.BundlePath);
        string stage = Path.Combine(root, ".prefab-preparation-" + id);
        return Task.Run(() =>
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                var before = snapshot.CreateDefinition(); var after = MapSnapshotCopy.Copy(before);
                var result = operation(after, stage, prefix);
                cancellation.ThrowIfCancellationRequested();
                var command = CreatePrefabCommand(label, before, after);
                cancellation.ThrowIfCancellationRequested();
                return new PreparedPrefabEdit(this, state, thread, root, stage, prefix, result, command, selectResult, context);
            }
            catch { Interlocked.Decrement(ref _prefabPreparationCount); if (Directory.Exists(stage)) Directory.Delete(stage, true); throw; }
        });
    }

    public MapPrefabService.InstanceResult ApplyPreparedPrefabEdit(PreparedPrefabEdit prepared, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!ReferenceEquals(prepared.Document, this)) throw new InvalidOperationException("Prepared prefab belongs to another document.");
        if (Environment.CurrentManagedThreadId != prepared.OwnerThread) throw new InvalidOperationException("Adopt the prepared prefab on the document owner thread.");
        if (prepared.Completed) throw new InvalidOperationException("Prepared prefab was already adopted or disposed.");
        if (CurrentStateId != prepared.State) throw new InvalidOperationException("Map changed while preparing the prefab. Prepare again from the current state.");
        if (prepared.Context != (FilePath, Project.Definition.BaseDirectory, Project.Definition.SourcePath, Project.Definition.BundlePath))
            throw new InvalidOperationException("Map file or asset context changed while preparing the prefab. Prepare again in the current project folder.");
        string? published = null;
        try
        {
            if (prepared.Result.GeneratedAssets.Count != 0)
            {
                string source = Path.Combine(prepared.Stage, prepared.AssetPrefix), target = Path.Combine(prepared.Root, prepared.AssetPrefix);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Prepared prefab asset target already exists.");
                Directory.Move(source, target); published = target; // Atomic directory adoption; no texture decoding/copying on dispatcher.
            }
            if (prepared.Command != null) History.Execute(prepared.Command);
            foreach (string asset in prepared.Result.GeneratedAssets) RegisterGeneratedAsset(asset, prepared.Root);
            if (prepared.SelectResult) { Selection.Clear(); Selection.UnionWith(prepared.Result.ObjectIds); ActiveObjectId = prepared.Result.ObjectIds.FirstOrDefault(); SelectionChanged(); }
            prepared.Completed = true;
            Interlocked.Decrement(ref _prefabPreparationCount);
            return prepared.Result;
        }
        catch { if (published != null) Directory.Delete(published, true); throw; }
        finally { if (prepared.Completed && Directory.Exists(prepared.Stage)) Directory.Delete(prepared.Stage, true); }
    }

    public sealed class PreparedPrefabEdit : IDisposable
    {
        internal MapDocument Document { get; }
        internal DocumentStateId State { get; }
        internal int OwnerThread { get; }
        internal string Root { get; }
        internal string Stage { get; }
        internal string AssetPrefix { get; }
        internal IMapEditCommand? Command { get; }
        internal bool SelectResult { get; }
        internal bool Completed { get; set; }
        internal (string? File, string? Base, string? Source, string? Bundle) Context { get; }
        public MapPrefabService.InstanceResult Result { get; }
        internal PreparedPrefabEdit(MapDocument document, DocumentStateId state, int ownerThread, string root, string stage,
            string assetPrefix, MapPrefabService.InstanceResult result, IMapEditCommand? command, bool selectResult,
            (string? File, string? Base, string? Source, string? Bundle) context)
        { Document = document; State = state; OwnerThread = ownerThread; Root = root; Stage = stage; AssetPrefix = assetPrefix; Result = result; Command = command; SelectResult = selectResult; Context = context; }
        public void Dispose()
        {
            if (Completed) return;
            Completed = true;
            Interlocked.Decrement(ref Document._prefabPreparationCount);
            if (Directory.Exists(Stage)) Directory.Delete(Stage, true);
        }
    }
}
