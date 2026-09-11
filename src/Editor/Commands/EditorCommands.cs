using MphRead.Mods.MapGen;
using ProjectPrime.Editor.Documents;

namespace ProjectPrime.Editor.Commands;

/// <summary>
/// Full-project snapshots are retained for imports and future bulk/CSG edits.
/// Interactive object, transform, and material commands below use bounded deltas.
/// </summary>
public abstract class SnapshotEditorCommand : IEditorCommand
{
    private readonly byte[] _before;
    private readonly byte[] _after;
    public abstract string Name { get; }
    public EditorChangeKind ChangeKind { get; }
    public long ApproximateMemoryBytes => _before.LongLength + _after.LongLength + 128;

    protected SnapshotEditorCommand(MapDocument document, Action<MapProject> change,
        EditorChangeKind changeKind = EditorChangeKind.All)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(change);
        _before = document.Snapshot();
        MapProject copy = System.Text.Json.JsonSerializer.Deserialize(_before,
            MapJsonContext.Default.MapProject)!;
        change(copy);
        _after = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(copy,
            MapJsonContext.Default.MapProject);
        ChangeKind = changeKind;
    }

    public void Execute(MapDocument document) => document.Restore(_after);
    public void Undo(MapDocument document) => document.Restore(_before);
}

public sealed class CreateBrushCommand : IEditorCommand
{
    private readonly ConvexBrush _brush;
    private readonly int _index;
    public string Name => $"Create {_brush.Primitive}";
    public EditorChangeKind ChangeKind => EditorChangeKind.Geometry;
    public long ApproximateMemoryBytes => 256 + _brush.Faces.Count * 128L;

    public CreateBrushCommand(MapDocument document, ConvexBrush brush)
    {
        MapAuthoringScene scene = Editable(document);
        _brush = EditorCommandCopies.Brush(brush);
        _index = scene.Brushes.Count;
    }

    public void Execute(MapDocument document)
    {
        List<ConvexBrush> brushes = Editable(document).Brushes;
        if (brushes.Any(value => value.Id == _brush.Id))
            throw new InvalidOperationException($"Brush '{_brush.Id}' already exists.");
        brushes.Insert(Math.Min(_index, brushes.Count), EditorCommandCopies.Brush(_brush));
    }

    public void Undo(MapDocument document)
    {
        List<ConvexBrush> brushes = Editable(document).Brushes;
        int index = brushes.FindIndex(value => value.Id == _brush.Id);
        if (index < 0) throw new KeyNotFoundException(_brush.Id);
        brushes.RemoveAt(index);
    }

    private static MapAuthoringScene Editable(MapDocument document)
        => document.Project.Authoring
            ?? throw new InvalidOperationException("Project is not natively editable.");
}

public sealed class CreateEntityCommand : IEditorCommand
{
    private readonly MapEntityDefinition _entity;
    private readonly int _index;
    public string Name => "Create Entity";
    public EditorChangeKind ChangeKind => EditorChangeKind.Entity;
    public long ApproximateMemoryBytes => 512;

    public CreateEntityCommand(MapDocument document, MapEntityDefinition entity)
    {
        _entity = EditorCommandCopies.Entity(entity);
        _index = Editable(document).Entities.Count;
    }

    public void Execute(MapDocument document)
    {
        List<MapEntityDefinition> entities = Editable(document).Entities;
        if (entities.Any(value => value.Id == _entity.Id))
            throw new InvalidOperationException($"Entity '{_entity.Id}' already exists.");
        entities.Insert(Math.Min(_index, entities.Count), EditorCommandCopies.Entity(_entity));
    }

    public void Undo(MapDocument document)
    {
        List<MapEntityDefinition> entities = Editable(document).Entities;
        int index = entities.FindIndex(value => value.Id == _entity.Id);
        if (index < 0) throw new KeyNotFoundException(_entity.Id);
        entities.RemoveAt(index);
    }

    private static MapAuthoringScene Editable(MapDocument document)
        => document.Project.Authoring
            ?? throw new InvalidOperationException("Project is not natively editable.");
}

public sealed class DeleteObjectCommand : IEditorCommand
{
    private readonly ConvexBrush? _brush;
    private readonly MapEntityDefinition? _entity;
    private readonly int _index;
    private string Id => _brush?.Id ?? _entity!.Id;
    public string Name => "Delete Object";
    public EditorChangeKind ChangeKind => _brush != null
        ? EditorChangeKind.Geometry : EditorChangeKind.Entity;
    public long ApproximateMemoryBytes => _brush == null ? 512 : 256 + _brush.Faces.Count * 128L;

    public DeleteObjectCommand(MapDocument document, string objectId)
    {
        MapAuthoringScene scene = Editable(document);
        _index = scene.Brushes.FindIndex(value => value.Id == objectId);
        if (_index >= 0)
        {
            ConvexBrush source = scene.Brushes[_index];
            _brush = EditorCommandCopies.Brush(source);
            return;
        }
        _index = scene.Entities.FindIndex(value => value.Id == objectId);
        if (_index < 0) throw new KeyNotFoundException(objectId);
        _entity = EditorCommandCopies.Entity(scene.Entities[_index]);
    }

    public void Execute(MapDocument document)
    {
        MapAuthoringScene scene = Editable(document);
        int index = _brush != null
            ? scene.Brushes.FindIndex(value => value.Id == Id)
            : scene.Entities.FindIndex(value => value.Id == Id);
        if (index < 0) throw new KeyNotFoundException(Id);
        if (_brush != null) scene.Brushes.RemoveAt(index);
        else scene.Entities.RemoveAt(index);
    }

    public void Undo(MapDocument document)
    {
        MapAuthoringScene scene = Editable(document);
        if (_brush != null)
            scene.Brushes.Insert(Math.Min(_index, scene.Brushes.Count),
                EditorCommandCopies.Brush(_brush));
        else
            scene.Entities.Insert(Math.Min(_index, scene.Entities.Count),
                EditorCommandCopies.Entity(_entity!));
    }

    private static MapAuthoringScene Editable(MapDocument document)
        => document.Project.Authoring
            ?? throw new InvalidOperationException("Project is not natively editable.");
}

public sealed class TransformObjectCommand : IEditorCommand
{
    private readonly string _objectId;
    private readonly bool _brush;
    private readonly MapTransform _before;
    private MapTransform _after;
    public string Name => "Transform Object";
    public EditorChangeKind ChangeKind => EditorChangeKind.Transform
        | (_brush ? EditorChangeKind.Geometry : EditorChangeKind.Entity);
    public long ApproximateMemoryBytes => 256;

    public TransformObjectCommand(MapDocument document, string objectId, MapTransform transform)
    {
        MapAuthoringScene scene = Editable(document);
        MapTransform? current = scene.Brushes.FirstOrDefault(value => value.Id == objectId)?.Transform;
        _brush = current != null;
        current ??= scene.Entities.FirstOrDefault(value => value.Id == objectId)?.Transform
            ?? throw new KeyNotFoundException(objectId);
        _objectId = objectId;
        _before = EditorCommandCopies.Transform(current);
        _after = EditorCommandCopies.Transform(transform);
    }

    public void Execute(MapDocument document) => Apply(document, _after);
    public void Undo(MapDocument document) => Apply(document, _before);

    public bool TryCoalesce(IEditorCommand newer)
    {
        if (newer is not TransformObjectCommand transform
            || transform._objectId != _objectId || transform._brush != _brush) return false;
        _after = EditorCommandCopies.Transform(transform._after);
        return true;
    }

    private void Apply(MapDocument document, MapTransform value)
    {
        MapAuthoringScene scene = Editable(document);
        if (_brush)
            (scene.Brushes.FirstOrDefault(item => item.Id == _objectId)
                ?? throw new KeyNotFoundException(_objectId)).Transform = EditorCommandCopies.Transform(value);
        else
            (scene.Entities.FirstOrDefault(item => item.Id == _objectId)
                ?? throw new KeyNotFoundException(_objectId)).Transform = EditorCommandCopies.Transform(value);
    }

    private static MapAuthoringScene Editable(MapDocument document)
        => document.Project.Authoring
            ?? throw new InvalidOperationException("Project is not natively editable.");
}

public sealed class ChangeMaterialCommand : IEditorCommand
{
    private readonly string _brushId;
    private readonly int? _faceIndex;
    private readonly string[] _before;
    private string _materialId;
    public string Name => "Change Material";
    public EditorChangeKind ChangeKind => EditorChangeKind.Material;
    public long ApproximateMemoryBytes => 128 + _before.Sum(value => value.Length * 2L);

    public ChangeMaterialCommand(MapDocument document, string brushId, string materialId,
        int? faceIndex = null)
    {
        ConvexBrush brush = document.Project.Authoring?.Brushes.Single(value => value.Id == brushId)
            ?? throw new InvalidOperationException("Project is not natively editable.");
        if (faceIndex is < 0 || faceIndex >= brush.Faces.Count)
            throw new ArgumentOutOfRangeException(nameof(faceIndex));
        _brushId = brushId;
        _faceIndex = faceIndex;
        _before = faceIndex.HasValue
            ? [brush.Faces[faceIndex.Value].MaterialId]
            : brush.Faces.Select(value => value.MaterialId).ToArray();
        _materialId = materialId;
    }

    public void Execute(MapDocument document) => Apply(document, _materialId);

    public void Undo(MapDocument document)
    {
        ConvexBrush brush = Brush(document);
        if (_faceIndex.HasValue) brush.Faces[_faceIndex.Value].MaterialId = _before[0];
        else for (int index = 0; index < brush.Faces.Count; index++)
            brush.Faces[index].MaterialId = _before[index];
    }

    public bool TryCoalesce(IEditorCommand newer)
    {
        if (newer is not ChangeMaterialCommand material || material._brushId != _brushId
            || material._faceIndex != _faceIndex) return false;
        _materialId = material._materialId;
        return true;
    }

    private void Apply(MapDocument document, string materialId)
    {
        ConvexBrush brush = Brush(document);
        if (_faceIndex.HasValue) brush.Faces[_faceIndex.Value].MaterialId = materialId;
        else foreach (ConvexBrushFace face in brush.Faces) face.MaterialId = materialId;
    }

    private ConvexBrush Brush(MapDocument document)
        => document.Project.Authoring?.Brushes.Single(value => value.Id == _brushId)
            ?? throw new InvalidOperationException("Project is not natively editable.");
}

public sealed class ModifyPropertyCommand : SnapshotEditorCommand
{
    private readonly string _name;
    public override string Name => _name;
    public ModifyPropertyCommand(MapDocument document, string name, Action<MapProject> change,
        EditorChangeKind changeKind = EditorChangeKind.Metadata)
        : base(document, change, changeKind) => _name = name;
}

/// <summary>Small-object/property delta used by inspectors and overlay controls.</summary>
public sealed class DeltaEditorCommand<T> : IEditorCommand
{
    private readonly Func<MapProject, T> _get;
    private readonly Action<MapProject, T> _set;
    private readonly Func<T, T> _copy;
    private readonly T _before;
    private T _after;
    private readonly string? _coalescingId;
    public string Name { get; }
    public EditorChangeKind ChangeKind { get; }
    public long ApproximateMemoryBytes { get; }

    public DeltaEditorCommand(MapDocument document, string name, EditorChangeKind changeKind,
        Func<MapProject, T> get, Action<MapProject, T> set, Func<T, T> copy,
        Func<T, T> change, long approximateMemoryBytes = 256, string? coalescingId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        _get = get ?? throw new ArgumentNullException(nameof(get));
        _set = set ?? throw new ArgumentNullException(nameof(set));
        _copy = copy ?? throw new ArgumentNullException(nameof(copy));
        ArgumentNullException.ThrowIfNull(change);
        Name = name;
        ChangeKind = changeKind;
        ApproximateMemoryBytes = Math.Max(128, approximateMemoryBytes);
        _coalescingId = coalescingId;
        _before = _copy(_get(document.Project));
        _after = change(_copy(_before));
    }

    public void Execute(MapDocument document) => _set(document.Project, _copy(_after));
    public void Undo(MapDocument document) => _set(document.Project, _copy(_before));

    public bool TryCoalesce(IEditorCommand newer)
    {
        if (_coalescingId == null || newer is not DeltaEditorCommand<T> delta
            || delta._coalescingId != _coalescingId) return false;
        _after = _copy(delta._after);
        return true;
    }
}

internal static class EditorCommandCopies
{
    public static ConvexBrush Brush(ConvexBrush value) => new()
    {
        Id = value.Id,
        Primitive = value.Primitive,
        Transform = Transform(value.Transform),
        Faces = value.Faces.Select(Face).ToList(),
        Solid = value.Solid,
        Damaging = value.Damaging,
        Shade = value.Shade
    };

    public static MapTransform Transform(MapTransform value) => new()
    {
        Position = (float[])value.Position.Clone(),
        Rotation = (float[])value.Rotation.Clone(),
        Scale = (float[])value.Scale.Clone()
    };

    public static ConvexBrushFace Face(ConvexBrushFace value) => new()
    {
        Normal = (float[])value.Normal.Clone(),
        Distance = value.Distance,
        MaterialId = value.MaterialId,
        TextureOffset = (float[])value.TextureOffset.Clone(),
        TextureRotation = value.TextureRotation,
        TextureScale = value.TextureScale
    };

    public static MapAuthoringMaterial Material(MapAuthoringMaterial value) => new()
    {
        Id = value.Id,
        Name = value.Name,
        SourceRoom = value.SourceRoom,
        SourceMaterial = value.SourceMaterial,
        CustomImage = value.CustomImage,
        Tiling = value.Tiling,
        Offset = (float[])value.Offset.Clone(),
        Rotation = value.Rotation,
        Terrain = value.Terrain
    };

    public static MapEnvironment Environment(MapEnvironment value) => new()
    {
        KillHeight = value.KillHeight,
        FarClip = value.FarClip,
        FogEnabled = value.FogEnabled,
        FogColor = (int[])value.FogColor.Clone(),
        FogSlope = value.FogSlope,
        FogOffset = value.FogOffset,
        Light1Color = (int[])value.Light1Color.Clone(),
        Light1Vector = (float[])value.Light1Vector.Clone(),
        Light2Color = (int[])value.Light2Color.Clone(),
        Light2Vector = (float[])value.Light2Vector.Clone()
    };

    public static MapEditorSettings Settings(MapEditorSettings value) => new()
    {
        PositionSnap = value.PositionSnap,
        RotationSnap = value.RotationSnap,
        ScaleSnap = value.ScaleSnap,
        CameraPosition = (float[])value.CameraPosition.Clone(),
        CameraTarget = (float[])value.CameraTarget.Clone(),
        ShowCollision = value.ShowCollision,
        ShowEntities = value.ShowEntities,
        ShowWorldBounds = value.ShowWorldBounds
    };

    public static MapEntityDefinition Entity(MapEntityDefinition value) => new()
    {
        Id = value.Id,
        Kind = value.Kind,
        Transform = Transform(value.Transform),
        Size = (float[])value.Size.Clone(),
        Target = value.Target == null ? null : (float[])value.Target.Clone(),
        LaunchVector = value.LaunchVector == null ? null : (float[])value.LaunchVector.Clone(),
        LaunchSpeed = value.LaunchSpeed,
        ControlLockTime = value.ControlLockTime,
        CooldownTime = value.CooldownTime,
        ModelId = value.ModelId,
        ItemType = value.ItemType,
        HasBase = value.HasBase,
        SpawnInterval = value.SpawnInterval,
        Team = value.Team,
        DamagePerTick = value.DamagePerTick,
        TargetObjectId = value.TargetObjectId
    };
}
