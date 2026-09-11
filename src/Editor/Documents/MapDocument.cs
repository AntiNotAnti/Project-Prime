using System.Text.Json;
using MphRead.Mods.MapGen;

namespace ProjectPrime.Editor.Documents;

public sealed class MapDocument
{
    private readonly List<IEditorCommand> _undo = [];
    private readonly List<IEditorCommand> _redo = [];
    private int _savedRevision;

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
            SelectedFaceIndex = null;
        }
    }
    public int? SelectedFaceIndex { get; set; }
    public int Revision { get; private set; }
    public bool IsDirty => Revision != _savedRevision;
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;
    public event Action? Changed;

    private MapDocument(MapProject project, string? path)
    {
        Project = project;
        Path = path;
    }

    public static MapDocument New(string stableId, string displayName)
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
        return new MapDocument(project, null);
    }

    public static MapDocument Open(string path)
        => new(MapProjectIO.Load(path), System.IO.Path.GetFullPath(path));

    public void Execute(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Execute(this);
        _undo.Add(command);
        _redo.Clear();
        Revision++;
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        IEditorCommand command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Undo(this);
        _redo.Add(command);
        Revision--;
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        IEditorCommand command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Execute(this);
        _undo.Add(command);
        Revision++;
        Changed?.Invoke();
    }

    public void Save(string? path = null)
    {
        string destination = path ?? Path
            ?? throw new InvalidOperationException("Choose a map project path before saving.");
        MapProjectIO.Save(Project, destination);
        Path = System.IO.Path.GetFullPath(destination);
        _savedRevision = Revision;
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
        _undo.Clear();
        _redo.Clear();
        Revision = Math.Max(_savedRevision + 1, Revision + 1);
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

public interface IEditorCommand
{
    string Name { get; }
    void Execute(MapDocument document);
    void Undo(MapDocument document);
}
