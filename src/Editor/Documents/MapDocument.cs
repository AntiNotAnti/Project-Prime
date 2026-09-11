using System.Text.Json;
using MphRead.Mods.MapGen;

namespace ProjectPrime.Editor.Documents;

public sealed class MapDocument
{
    private readonly List<EditorHistoryEntry> _undo = [];
    private readonly List<EditorHistoryEntry> _redo = [];
    private readonly EditorHistoryLimits _historyLimits;
    private long _nextStateId;
    private long _historyMemoryBytes;

    public MapProject Project { get; private set; }
    public string? Path { get; private set; }
    private string? _selectedObjectId;
    public string? SelectedObjectId
    {
        get => _selectedObjectId;
        set
        {
            if (_selectedObjectId == value) return;
            _selectedObjectId = value;
            _selectedFaceIndex = null;
            PublishChange(EditorChangeKind.Selection, contentChange: false);
        }
    }
    private int? _selectedFaceIndex;
    public int? SelectedFaceIndex
    {
        get => _selectedFaceIndex;
        set
        {
            if (_selectedFaceIndex == value) return;
            _selectedFaceIndex = value;
            PublishChange(EditorChangeKind.Selection, contentChange: false);
        }
    }
    public int Revision { get; private set; }
    public DocumentStateId CurrentStateId { get; private set; }
    public DocumentStateId SavedStateId { get; private set; }
    public EditorChangeState ChangeState { get; private set; }
    public bool IsDirty => CurrentStateId != SavedStateId;
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;
    public int UndoCount => _undo.Count;
    public long HistoryMemoryBytes => _historyMemoryBytes;
    public event Action? Changed;

    private MapDocument(MapProject project, string? path, EditorHistoryLimits? historyLimits = null)
    {
        Project = project;
        Path = path;
        _historyLimits = historyLimits ?? EditorHistoryLimits.Default;
    }

    public static MapDocument New(string stableId, string displayName,
        EditorHistoryLimits? historyLimits = null)
    {
        var project = new MapProject
        {
            StableId = stableId,
            Metadata = new MapProjectMetadata { Name = displayName, Author = Environment.UserName },
            Environment = new MapEnvironment(),
            Authoring = new MapAuthoringScene()
        };
        project.Map.Name = displayName.ToUpperInvariant();
        project.Map.InGameName = displayName;
        project.Map.TextureSource = "MP3 PROVING GROUND";
        project.Authoring.Materials.Add(new MapAuthoringMaterial
        {
            Id = "material.default", Name = "Default", SourceRoom = project.Map.TextureSource,
            SourceMaterial = 1
        });
        ConvexBrush floor = ConvexBrushFactory.Box("brush.floor", "material.default", new(16, 1, 16));
        floor.Transform.Position = [0, -0.5f, 0];
        project.Authoring.Brushes.Add(floor);
        project.Authoring.Entities.AddRange(
        [
            Spawn("spawn.1", -4, 0.1f, -4, 45), Spawn("spawn.2", 4, 0.1f, 4, -135),
            Spawn("spawn.3", 4, 0.1f, -4, -45), Spawn("spawn.4", -4, 0.1f, 4, 135)
        ]);
        return new MapDocument(project, null, historyLimits);
    }

    public static MapDocument Open(string path, EditorHistoryLimits? historyLimits = null)
        => new(MapProjectIO.Load(path), System.IO.Path.GetFullPath(path), historyLimits);

    public void Execute(IEditorCommand command) => ExecuteCore(command, null);

    /// <summary>
    /// Applies a continuous edit while retaining one undo entry for the given
    /// interaction. Callers must use a fresh key for each mouse-down/spinner
    /// transaction; ordinary discrete edits use <see cref="Execute"/>.
    /// </summary>
    public void ExecuteCoalesced(IEditorCommand command, string transactionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionKey);
        ExecuteCore(command, transactionKey);
    }

    private void ExecuteCore(IEditorCommand command, string? transactionKey)
    {
        ArgumentNullException.ThrowIfNull(command);
        DocumentStateId before = CurrentStateId;
        command.Execute(this);
        DocumentStateId after = NextState();
        bool coalesced = transactionKey != null && _undo.Count != 0
            && _undo[^1].TransactionKey == transactionKey
            && _undo[^1].AfterState == before
            && before != SavedStateId
            && _undo[^1].Command.TryCoalesce(command);
        if (coalesced)
        {
            EditorHistoryEntry previous = _undo[^1];
            _historyMemoryBytes -= previous.ApproximateMemoryBytes;
            EditorHistoryEntry replacement = previous with
            {
                AfterState = after,
                ApproximateMemoryBytes = previous.Command.ApproximateMemoryBytes
            };
            _undo[^1] = replacement;
            _historyMemoryBytes += replacement.ApproximateMemoryBytes;
        }
        else
        {
            var entry = new EditorHistoryEntry(command, before, after, transactionKey,
                command.ApproximateMemoryBytes);
            _undo.Add(entry);
            _historyMemoryBytes += entry.ApproximateMemoryBytes;
        }
        foreach (EditorHistoryEntry entry in _redo) _historyMemoryBytes -= entry.ApproximateMemoryBytes;
        _redo.Clear();
        CurrentStateId = after;
        EnforceHistoryLimits();
        PublishChange(command.ChangeKind, contentChange: true);
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        EditorHistoryEntry entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        entry.Command.Undo(this);
        _redo.Add(entry);
        CurrentStateId = entry.BeforeState;
        PublishChange(entry.Command.ChangeKind, contentChange: true);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        EditorHistoryEntry entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        entry.Command.Execute(this);
        _undo.Add(entry);
        CurrentStateId = entry.AfterState;
        PublishChange(entry.Command.ChangeKind, contentChange: true);
    }

    public void Save(string? path = null)
    {
        string destination = path ?? Path
            ?? throw new InvalidOperationException("Choose a map project path before saving.");
        MapProjectIO.Save(Project, destination);
        Path = System.IO.Path.GetFullPath(destination);
        SavedStateId = CurrentStateId;
        Changed?.Invoke();
    }

    public void SaveSnapshot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        MapProject snapshot = JsonSerializer.Deserialize(Snapshot(), MapJsonContext.Default.MapProject)
            ?? throw new InvalidOperationException("Editor snapshot is invalid.");
        MapProjectIO.Save(snapshot, path);
    }

    public void Recover(string autosavePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(autosavePath);
        byte[] snapshot = File.ReadAllBytes(System.IO.Path.GetFullPath(autosavePath));
        Restore(snapshot);
        foreach (EditorHistoryEntry entry in _undo) _historyMemoryBytes -= entry.ApproximateMemoryBytes;
        foreach (EditorHistoryEntry entry in _redo) _historyMemoryBytes -= entry.ApproximateMemoryBytes;
        _undo.Clear();
        _redo.Clear();
        CurrentStateId = NextState();
        PublishChange(EditorChangeKind.All, contentChange: true);
    }

    private DocumentStateId NextState() => new(checked(++_nextStateId));

    private void EnforceHistoryLimits()
    {
        while (_undo.Count != 0 && (_undo.Count + _redo.Count > _historyLimits.MaximumCommandCount
            || _historyMemoryBytes > _historyLimits.MaximumApproximateBytes))
        {
            EditorHistoryEntry dropped = _undo[0];
            _undo.RemoveAt(0);
            _historyMemoryBytes -= dropped.ApproximateMemoryBytes;
        }
    }

    private void PublishChange(EditorChangeKind kind, bool contentChange)
    {
        if (kind == EditorChangeKind.None) return;
        ChangeState = ChangeState.Advance(kind);
        if (contentChange) Revision++;
        Changed?.Invoke();
    }

    internal byte[] Snapshot()
        => JsonSerializer.SerializeToUtf8Bytes(Project, MapJsonContext.Default.MapProject);

    internal void Restore(byte[] snapshot)
    {
        string? path = Path;
        Project = JsonSerializer.Deserialize(snapshot, MapJsonContext.Default.MapProject)
            ?? throw new InvalidOperationException("Editor command snapshot is invalid.");
        Project.SourcePath = path;
        Project.Map.SourcePath = path;
        Project.Map.BaseDirectory = path == null ? null : System.IO.Path.GetDirectoryName(path);
    }

    private static MapEntityDefinition Spawn(string id, float x, float y, float z, float yaw)
        => new()
        {
            Id = id,
            Kind = MapEntityKind.PlayerSpawn,
            Transform = new MapTransform { Position = [x, y, z], Rotation = [0, yaw, 0] }
        };
}

public readonly record struct DocumentStateId(long Value);

public readonly record struct EditorHistoryEntry(
    IEditorCommand Command,
    DocumentStateId BeforeState,
    DocumentStateId AfterState,
    string? TransactionKey,
    long ApproximateMemoryBytes);

public sealed record EditorHistoryLimits
{
    public static EditorHistoryLimits Default { get; } = new(500, 256L * 1024 * 1024);
    public int MaximumCommandCount { get; }
    public long MaximumApproximateBytes { get; }

    public EditorHistoryLimits(int maximumCommandCount, long maximumApproximateBytes)
    {
        if (maximumCommandCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCommandCount));
        if (maximumApproximateBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumApproximateBytes));
        MaximumCommandCount = maximumCommandCount;
        MaximumApproximateBytes = maximumApproximateBytes;
    }
}

[Flags]
public enum EditorChangeKind
{
    None = 0,
    Selection = 1 << 0,
    Transform = 1 << 1,
    Geometry = 1 << 2,
    Material = 1 << 3,
    Entity = 1 << 4,
    Environment = 1 << 5,
    Overlay = 1 << 6,
    Metadata = 1 << 7,
    All = Selection | Transform | Geometry | Material | Entity | Environment | Overlay | Metadata
}

public readonly record struct EditorChangeState(
    long Sequence,
    long Selection,
    long Geometry,
    long Entity,
    long Overlay,
    long Environment)
{
    internal EditorChangeState Advance(EditorChangeKind kind)
    {
        long next = checked(Sequence + 1);
        return new(next,
            kind.HasFlag(EditorChangeKind.Selection) ? next : Selection,
            (kind & (EditorChangeKind.Geometry | EditorChangeKind.Material)) != 0
                ? next : Geometry,
            kind.HasFlag(EditorChangeKind.Entity) ? next : Entity,
            kind.HasFlag(EditorChangeKind.Overlay) ? next : Overlay,
            kind.HasFlag(EditorChangeKind.Environment) ? next : Environment);
    }
}

public interface IEditorCommand
{
    string Name { get; }
    EditorChangeKind ChangeKind => EditorChangeKind.Metadata;
    long ApproximateMemoryBytes => 128;
    void Execute(MapDocument document);
    void Undo(MapDocument document);
    bool TryCoalesce(IEditorCommand newer) => false;
}
