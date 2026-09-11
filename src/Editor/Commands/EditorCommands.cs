using MphRead.Mods.MapGen;
using ProjectPrime.Editor.Documents;

namespace ProjectPrime.Editor.Commands;

public abstract class SnapshotEditorCommand : IEditorCommand
{
    private readonly byte[] _before;
    private readonly byte[] _after;
    public abstract string Name { get; }

    protected SnapshotEditorCommand(MapDocument document, Action<MapProject> change)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(change);
        _before = document.Snapshot();
        MapProject copy = System.Text.Json.JsonSerializer.Deserialize(_before,
            MapJsonContext.Default.MapProject)!;
        change(copy);
        _after = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(copy,
            MapJsonContext.Default.MapProject);
    }

    public void Execute(MapDocument document) => document.Restore(_after);
    public void Undo(MapDocument document) => document.Restore(_before);
}

public sealed class CreateBrushCommand : SnapshotEditorCommand
{
    private readonly string _name;
    public override string Name => _name;
    public CreateBrushCommand(MapDocument document, ConvexBrush brush)
        : base(document, project => (project.Authoring
            ?? throw new InvalidOperationException("Project is not natively editable."))
            .Brushes.Add(brush))
        => _name = $"Create {brush.Primitive}";
}

public sealed class CreateEntityCommand : SnapshotEditorCommand
{
    public override string Name => "Create Entity";
    public CreateEntityCommand(MapDocument document, MapEntityDefinition entity)
        : base(document, project => (project.Authoring
            ?? throw new InvalidOperationException("Project is not natively editable."))
            .Entities.Add(entity)) { }
}

public sealed class DeleteObjectCommand : SnapshotEditorCommand
{
    public override string Name => "Delete Object";
    public DeleteObjectCommand(MapDocument document, string objectId)
        : base(document, project =>
        {
            MapAuthoringScene scene = project.Authoring
                ?? throw new InvalidOperationException("Project is not natively editable.");
            scene.Brushes.RemoveAll(value => value.Id == objectId);
            scene.Entities.RemoveAll(value => value.Id == objectId);
        }) { }
}

public sealed class TransformObjectCommand : SnapshotEditorCommand
{
    public override string Name => "Transform Object";
    public TransformObjectCommand(MapDocument document, string objectId, MapTransform transform)
        : base(document, project =>
        {
            MapAuthoringScene scene = project.Authoring
                ?? throw new InvalidOperationException("Project is not natively editable.");
            ConvexBrush? brush = scene.Brushes.FirstOrDefault(value => value.Id == objectId);
            if (brush != null) brush.Transform = transform;
            else
            {
                MapEntityDefinition entity = scene.Entities.FirstOrDefault(value => value.Id == objectId)
                    ?? throw new KeyNotFoundException(objectId);
                entity.Transform = transform;
            }
        }) { }
}

public sealed class ChangeMaterialCommand : SnapshotEditorCommand
{
    public override string Name => "Change Material";
    public ChangeMaterialCommand(MapDocument document, string brushId, string materialId,
        int? faceIndex = null)
        : base(document, project =>
        {
            ConvexBrush brush = project.Authoring!.Brushes.Single(value => value.Id == brushId);
            if (faceIndex.HasValue) brush.Faces[faceIndex.Value].MaterialId = materialId;
            else foreach (ConvexBrushFace face in brush.Faces) face.MaterialId = materialId;
        }) { }
}

public sealed class ModifyPropertyCommand : SnapshotEditorCommand
{
    private readonly string _name;
    public override string Name => _name;
    public ModifyPropertyCommand(MapDocument document, string name, Action<MapProject> change)
        : base(document, change) => _name = name;
}
