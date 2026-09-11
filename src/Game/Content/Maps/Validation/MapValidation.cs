using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen;

public interface IMapModeValidator
{
    MapMode Mode { get; }
    void Validate(MapProject project, MapModeCapabilities capabilities, MapDiagnosticBag diagnostics);
}

public sealed class MapValidator
{
    private readonly IReadOnlyDictionary<MapMode, IMapModeValidator> _modeValidators;

    public MapValidator(IEnumerable<IMapModeValidator>? modeValidators = null)
    {
        IMapModeValidator[] validators = (modeValidators ??
            [new BattleMapModeValidator(), new SurvivalMapModeValidator(), new CaptureMapModeValidator(),
             new BountyMapModeValidator(), new NodesMapModeValidator()]).ToArray();
        _modeValidators = validators.ToDictionary(validator => validator.Mode);
    }

    public ImmutableArray<MapDiagnostic> ValidateProject(MapProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var diagnostics = new MapDiagnosticBag();
        try { _ = project.Identity; }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            diagnostics.Add(new("MAP-ID-001", MapDiagnosticSeverity.Error, exception.Message,
                SourcePath: project.SourcePath, SuggestedAction: "Choose a canonical lowercase stable ID and semantic version."));
        }
        if (project.Format != MapProject.CurrentFormat)
            diagnostics.Add(new("MAP-SRC-001", MapDiagnosticSeverity.Error,
                $"Unsupported map project format {project.Format}.", SourcePath: project.SourcePath));
        if (string.IsNullOrWhiteSpace(project.Metadata.Name))
            diagnostics.Add(new("MAP-SRC-002", MapDiagnosticSeverity.Error, "Map display name is required."));
        if (project.SupportedModes.Count == 0)
            diagnostics.Add(new("MAP-MODE-001", MapDiagnosticSeverity.Error,
                "A map must explicitly support at least one mode."));
        if (project.SupportedModes.Count != project.SupportedModes.Distinct().Count())
            diagnostics.Add(new("MAP-MODE-001", MapDiagnosticSeverity.Error,
                "Supported modes contain duplicates."));

        diagnostics.AddRange(MapDependencyAnalyzer.Analyze(project).Diagnostics);

        ValidateDefinition(project.Map, diagnostics, project.Authoring != null);
        if (project.Environment != null) ValidateEnvironment(project.Environment, diagnostics);
        if (project.Authoring != null) ValidateAuthoring(project, diagnostics);
        MapModeCapabilities capabilities = Capabilities(project);
        foreach (MapMode mode in project.SupportedModes.Distinct().OrderBy(mode => mode))
        {
            if (_modeValidators.TryGetValue(mode, out IMapModeValidator? validator))
                validator.Validate(project, capabilities, diagnostics);
            else
                diagnostics.Add(new("MAP-MODE-001", MapDiagnosticSeverity.Error,
                    $"No validator is registered for {mode}."));
        }
        return diagnostics.ToImmutable();
    }

    public ImmutableArray<MapDiagnostic> ValidateStatistics(MapBuildStatistics statistics)
    {
        var diagnostics = new MapDiagnosticBag();
        AddBudget(diagnostics, "MAP-COL-004", "Collision grid references",
            statistics.CollisionGridReferences, ushort.MaxValue);
        AddBudget(diagnostics, "MAP-COL-005", "Distinct collision points",
            statistics.CollisionPoints, ushort.MaxValue);
        AddBudget(diagnostics, "MAP-COL-006", "Collision planes",
            statistics.CollisionPlanes, ushort.MaxValue);
        AddBudget(diagnostics, "MAP-ENT-004", "Entities", statistics.Entities, short.MaxValue);
        return diagnostics.ToImmutable();
    }

    private static void ValidateDefinition(MapDefinition definition, MapDiagnosticBag diagnostics,
        bool hasAuthoring)
    {
        if (!hasAuthoring && definition.Import == null && definition.Brushes.Count == 0)
            diagnostics.Add(new("MAP-GEO-002", MapDiagnosticSeverity.Error,
                "Map has neither native geometry nor an imported geometry source."));
        if (definition.ScaleFactor is < 0 or > 20)
            diagnostics.Add(new("MAP-GEO-003", MapDiagnosticSeverity.Error,
                "Model scale factor must be between 0 and 20."));
        if (!float.IsFinite(definition.KillHeight) || !float.IsFinite(definition.FarClip)
            || definition.FarClip <= 0)
            diagnostics.Add(new("MAP-ENV-001", MapDiagnosticSeverity.Error,
                "Kill height and far clip must be finite, and far clip must be positive."));
        if (definition.Import is { } import)
        {
            if (!float.IsFinite(import.UnitsPerUnit) || import.UnitsPerUnit <= 0
                || !float.IsFinite(import.TexScale) || import.TexScale <= 0
                || import.PatchLevel is < 1 or > 16)
                diagnostics.Add(new("MAP-GEO-006", MapDiagnosticSeverity.Error,
                    "Q3 import units/texture scale must be positive and patch level must be 1..16."));
        }

        for (int index = 0; index < definition.Brushes.Count; index++)
        {
            MapBrush brush = definition.Brushes[index];
            string objectId = $"brush:{index}";
            if (!Vector(brush.Min) || !Vector(brush.Max))
            {
                diagnostics.Add(new("MAP-GEO-004", MapDiagnosticSeverity.Error,
                    "Brush bounds must contain three finite coordinates.", objectId));
                continue;
            }
            if (brush.Min.Zip(brush.Max).Any(pair => pair.First == pair.Second))
                diagnostics.Add(new("MAP-GEO-001", MapDiagnosticSeverity.Error,
                    "Brush has zero extent on at least one axis.", objectId,
                    SuggestedAction: "Resize or remove the degenerate brush."));
            if (brush.Material < 0 || brush.Material >= definition.Materials.Count)
                diagnostics.Add(new("MAP-MAT-001", MapDiagnosticSeverity.Error,
                    $"Brush references missing material {brush.Material}.", objectId));
            float scale = MathF.Pow(2, definition.ScaleFactor);
            foreach (float coordinate in brush.Min.Concat(brush.Max))
            {
                float packed = coordinate / scale * 4096;
                if (packed < short.MinValue || packed > short.MaxValue)
                    diagnostics.Add(new("MAP-GEO-005", MapDiagnosticSeverity.Error,
                        $"Coordinate {coordinate} does not fit the model fixed-point range at scale {scale}.", objectId,
                        SuggestedAction: "Raise scaleFactor or reduce world bounds."));
            }
        }
        for (int index = 0; index < definition.Materials.Count; index++)
        {
            MapMaterial material = definition.Materials[index];
            if (string.IsNullOrWhiteSpace(material.Name) || !float.IsFinite(material.TexScale)
                || material.TexScale <= 0)
                diagnostics.Add(new("MAP-MAT-002", MapDiagnosticSeverity.Error,
                    "Material name and a positive finite texture scale are required.", $"material:{index}"));
        }
        for (int index = 0; index < definition.Spawns.Count; index++)
        {
            MapSpawn spawn = definition.Spawns[index];
            string objectId = $"spawn:{index}";
            if (!Vector(spawn.Position) || !float.IsFinite(spawn.Yaw))
            {
                diagnostics.Add(new("MAP-SPAWN-001", MapDiagnosticSeverity.Error,
                    "Spawn position and facing must be finite.", objectId));
                continue;
            }
            if (spawn.Position[1] < definition.KillHeight)
                diagnostics.Add(new("MAP-SPAWN-002", MapDiagnosticSeverity.Error,
                    "Spawn is below the map kill height.", objectId,
                    SuggestedAction: "Move the spawn above kill height."));
            if (definition.Brushes.Any(brush => brush.Solid && ContainsStrictly(brush, spawn.Position)))
                diagnostics.Add(new("MAP-SPAWN-003", MapDiagnosticSeverity.Error,
                    "Spawn intersects solid native geometry.", objectId,
                    SuggestedAction: "Move the spawn into clear space."));
            for (int prior = 0; prior < index; prior++)
            {
                if (Vector(definition.Spawns[prior].Position)
                    && SquaredDistance(spawn.Position, definition.Spawns[prior].Position) < 0.25f)
                    diagnostics.Add(new("MAP-SPAWN-004", MapDiagnosticSeverity.Warning,
                        $"Spawn overlaps spawn:{prior}.", objectId));
            }
        }
        for (int index = 0; index < definition.JumpPads.Count; index++)
        {
            MapJumpPad pad = definition.JumpPads[index];
            string objectId = $"jump-pad:{index}";
            if (!Vector(pad.Position) || !Vector(pad.Size) || pad.Size.Any(value => value <= 0))
                diagnostics.Add(new("MAP-JUMP-001", MapDiagnosticSeverity.Error,
                    "Jump-pad position and positive trigger size are required.", objectId));
            if (pad.Target != null && !Vector(pad.Target) || pad.Vector != null && !Vector(pad.Vector))
                diagnostics.Add(new("MAP-JUMP-002", MapDiagnosticSeverity.Error,
                    "Jump-pad target/vector must contain three finite coordinates.", objectId));
            if (pad.Target == null && (pad.Vector == null || !float.IsFinite(pad.Speed) || pad.Speed <= 0))
                diagnostics.Add(new("MAP-JUMP-003", MapDiagnosticSeverity.Error,
                    "Jump pad needs a target or a vector with positive speed.", objectId));
        }
    }

    private static void ValidateAuthoring(MapProject project, MapDiagnosticBag diagnostics)
    {
        MapAuthoringScene scene = project.Authoring!;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var materialIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (MapAuthoringMaterial material in scene.Materials)
        {
            if (!MapObjectId.IsValid(material.Id) || !materialIds.Add(material.Id))
                diagnostics.Add(new("MAP-MAT-003", MapDiagnosticSeverity.Error,
                    $"Material ID '{material.Id}' is invalid or duplicated.", material.Id));
            if (string.IsNullOrWhiteSpace(material.Name) || !float.IsFinite(material.Tiling)
                || material.Tiling <= 0 || material.Offset.Length != 2
                || material.Offset.Any(value => !float.IsFinite(value))
                || !float.IsFinite(material.Rotation))
                diagnostics.Add(new("MAP-MAT-002", MapDiagnosticSeverity.Error,
                    "Material name, positive tiling, and finite UV transform are required.", material.Id));
            bool custom = !string.IsNullOrWhiteSpace(material.CustomImage);
            bool borrowed = material.SourceMaterial.HasValue;
            if (custom == borrowed || material.SourceMaterial is < 0)
                diagnostics.Add(new("MAP-MAT-004", MapDiagnosticSeverity.Error,
                    "A material must use exactly one custom image or non-negative base-game source material.",
                    material.Id));
            if (!Enum.TryParse(material.Terrain, ignoreCase: true, out Terrain terrain)
                || !Enum.IsDefined(terrain) || terrain is Terrain.Unknown11 or Terrain.All)
                diagnostics.Add(new("MAP-MAT-006", MapDiagnosticSeverity.Error,
                    $"Material terrain '{material.Terrain}' is unsupported.", material.Id));
        }
        foreach (ConvexBrush brush in scene.Brushes)
        {
            if (!MapObjectId.IsValid(brush.Id) || !ids.Add(brush.Id))
                diagnostics.Add(new("MAP-GEO-007", MapDiagnosticSeverity.Error,
                    $"Brush ID '{brush.Id}' is invalid or duplicated.", brush.Id));
            if (!Transform(brush.Transform) || brush.Faces.Count < 4)
                diagnostics.Add(new("MAP-GEO-008", MapDiagnosticSeverity.Error,
                    "Convex brush transform and at least four planes are required.", brush.Id));
            foreach (ConvexBrushFace face in brush.Faces)
            {
                if (!Vector(face.Normal) || !float.IsFinite(face.Distance)
                    || face.TextureOffset.Length != 2 || face.TextureOffset.Any(value => !float.IsFinite(value))
                    || !float.IsFinite(face.TextureScale) || face.TextureScale <= 0)
                    diagnostics.Add(new("MAP-GEO-008", MapDiagnosticSeverity.Error,
                        "Brush plane and texture transform must be finite.", brush.Id));
                if (!materialIds.Contains(face.MaterialId))
                    diagnostics.Add(new("MAP-MAT-005", MapDiagnosticSeverity.Error,
                        $"Brush references missing material '{face.MaterialId}'.", brush.Id));
            }
        }
        var priorSpawns = new List<MapEntityDefinition>();
        foreach (MapEntityDefinition entity in scene.Entities)
        {
            if (!MapEntitySupport.IsCompilerSupported(entity.Kind))
                diagnostics.Add(new("MAP-ENT-005", MapDiagnosticSeverity.Error,
                    $"{entity.Kind} is not supported by compiler schema {MapCompilerSchema.Current}.",
                    entity.Id,
                    SuggestedAction: "Remove the entity or use a compiler version that explicitly supports it."));
            if (!MapObjectId.IsValid(entity.Id) || !ids.Add(entity.Id))
                diagnostics.Add(new("MAP-ENT-002", MapDiagnosticSeverity.Error,
                    $"Entity ID '{entity.Id}' is invalid or duplicated.", entity.Id));
            if (!Transform(entity.Transform) || !Vector(entity.Size)
                || entity.Size.Any(value => value <= 0))
                diagnostics.Add(new("MAP-ENT-003", MapDiagnosticSeverity.Error,
                    "Entity transform and positive volume size are required.", entity.Id));
            float killHeight = project.Environment?.KillHeight ?? project.Map.KillHeight;
            if (entity.Transform.Position is { Length: 3 } position
                && position.All(float.IsFinite) && position[1] < killHeight
                && entity.Kind is MapEntityKind.PlayerSpawn or MapEntityKind.TeamSpawn)
                diagnostics.Add(new("MAP-SPAWN-002", MapDiagnosticSeverity.Error,
                    "Spawn is below the map kill height.", entity.Id));
            if (entity.Transform.Position is { Length: 3 } spawnPosition
                && spawnPosition.All(float.IsFinite)
                && entity.Kind is MapEntityKind.PlayerSpawn or MapEntityKind.TeamSpawn)
            {
                if (scene.Brushes.Any(brush => brush.Solid && Contains(brush, spawnPosition)))
                    diagnostics.Add(new("MAP-SPAWN-003", MapDiagnosticSeverity.Error,
                        "Spawn intersects solid native geometry.", entity.Id,
                        SuggestedAction: "Move the spawn into clear space."));
                MapEntityDefinition? overlap = priorSpawns.FirstOrDefault(other =>
                    SquaredDistance(spawnPosition, other.Transform.Position) < 0.25f);
                if (overlap != null)
                    diagnostics.Add(new("MAP-SPAWN-004", MapDiagnosticSeverity.Warning,
                        $"Spawn overlaps '{overlap.Id}'.", entity.Id));
                priorSpawns.Add(entity);
            }
            if (entity.Kind == MapEntityKind.JumpPad
                && entity.Target == null
                && (entity.LaunchVector == null || !float.IsFinite(entity.LaunchSpeed)
                    || entity.LaunchSpeed <= 0))
                diagnostics.Add(new("MAP-JUMP-003", MapDiagnosticSeverity.Error,
                    "Jump pad needs a target or a vector with positive speed.", entity.Id));
            if (entity.Kind is MapEntityKind.TeamSpawn or MapEntityKind.CaptureBase
                && entity.Team is not (0 or 1))
                diagnostics.Add(new("MAP-MODE-004", MapDiagnosticSeverity.Error,
                    "Team spawn/base must select team 0 or team 1.", entity.Id));
            if (!string.IsNullOrWhiteSpace(entity.TargetObjectId)
                && (!MapObjectId.IsValid(entity.TargetObjectId)
                    || entity.TargetObjectId == entity.Id
                    || !scene.Entities.Any(candidate => candidate.Id == entity.TargetObjectId)))
                diagnostics.Add(new("MAP-LINK-001", MapDiagnosticSeverity.Error,
                    $"Entity target '{entity.TargetObjectId}' does not resolve to another stable object ID.",
                    entity.Id));
        }
    }

    private static void ValidateEnvironment(MapEnvironment environment, MapDiagnosticBag diagnostics)
    {
        bool color(int[] value) => value is { Length: 3 }
            && value.All(component => component is >= 0 and <= 31);
        if (!float.IsFinite(environment.KillHeight) || !float.IsFinite(environment.FarClip)
            || environment.FarClip <= 0 || !color(environment.FogColor)
            || !color(environment.Light1Color) || !color(environment.Light2Color)
            || !Vector(environment.Light1Vector) || !Vector(environment.Light2Vector))
            diagnostics.Add(new("MAP-ENV-001", MapDiagnosticSeverity.Error,
                "Environment heights, clip distance, colors, and light vectors are invalid."));
    }

    private static MapModeCapabilities Capabilities(MapProject project)
    {
        MapDefinition definition = project.Map;
        MapEntityDefinition[] entities = project.Authoring?.Entities.ToArray() ?? [];
        return new()
        {
            HasGeneralSpawns = definition.Spawns.Count >= 2 || definition.Import?.KeepSpawns == true
                || entities.Count(entity => entity.Kind is MapEntityKind.PlayerSpawn or MapEntityKind.TeamSpawn) >= 2,
            HasTeamZeroSpawns = entities.Any(entity => entity.Kind == MapEntityKind.TeamSpawn && entity.Team == 0),
            HasTeamOneSpawns = entities.Any(entity => entity.Kind == MapEntityKind.TeamSpawn && entity.Team == 1),
            HasTeamZeroBase = entities.Any(entity => entity.Kind == MapEntityKind.CaptureBase && entity.Team == 0),
            HasTeamOneBase = entities.Any(entity => entity.Kind == MapEntityKind.CaptureBase && entity.Team == 1),
            HasBountyBase = entities.Any(entity => entity.Kind == MapEntityKind.BountyBase),
            ObjectiveNodeCount = entities.Count(entity => entity.Kind == MapEntityKind.NodeObjective)
        };
    }

    private static bool Transform(MapTransform value)
        => Vector(value.Position) && Vector(value.Rotation) && Vector(value.Scale)
            && value.Scale.All(component => component != 0);

    private static bool Vector(float[]? value)
        => value is { Length: 3 } && value.All(float.IsFinite);

    private static bool ContainsStrictly(MapBrush brush, float[] point)
        => point[0] > Math.Min(brush.Min[0], brush.Max[0]) && point[0] < Math.Max(brush.Min[0], brush.Max[0])
        && point[1] > Math.Min(brush.Min[1], brush.Max[1]) && point[1] < Math.Max(brush.Min[1], brush.Max[1])
        && point[2] > Math.Min(brush.Min[2], brush.Max[2]) && point[2] < Math.Max(brush.Min[2], brush.Max[2]);

    private static bool Contains(ConvexBrush brush, float[] worldPoint)
    {
        MapTransform value = brush.Transform;
        if (!Transform(value) || brush.Faces.Any(face => face.Normal.Length != 3
            || !face.Normal.All(float.IsFinite) || !float.IsFinite(face.Distance))) return false;
        Vector3 rotation = new(value.Rotation[0], value.Rotation[1], value.Rotation[2]);
        rotation *= MathF.PI / 180;
        Matrix4 transform = Matrix4.CreateScale(value.Scale[0], value.Scale[1], value.Scale[2])
            * Matrix4.CreateRotationX(rotation.X) * Matrix4.CreateRotationY(rotation.Y)
            * Matrix4.CreateRotationZ(rotation.Z)
            * Matrix4.CreateTranslation(value.Position[0], value.Position[1], value.Position[2]);
        Vector3 local = Vector3.TransformPosition(new Vector3(worldPoint[0], worldPoint[1], worldPoint[2]),
            transform.Inverted());
        foreach (ConvexBrushFace face in brush.Faces)
        {
            Vector3 normal = new(face.Normal[0], face.Normal[1], face.Normal[2]);
            float length = normal.Length;
            if (length <= 0 || Vector3.Dot(normal / length, local) > face.Distance / length + 0.0005f)
                return false;
        }
        return true;
    }

    private static float SquaredDistance(float[] left, float[] right)
        => (left[0] - right[0]) * (left[0] - right[0])
        + (left[1] - right[1]) * (left[1] - right[1])
        + (left[2] - right[2]) * (left[2] - right[2]);

    private static void AddBudget(MapDiagnosticBag diagnostics, string code, string name, int value, int maximum)
    {
        if (value > maximum)
            diagnostics.Add(new(code, MapDiagnosticSeverity.Error,
                $"{name} requires {value:N0}; the format maximum is {maximum:N0}."));
        else if (value >= maximum * 0.8)
            diagnostics.Add(new(code, MapDiagnosticSeverity.Warning,
                $"{name} uses {value:N0} / {maximum:N0} ({value * 100 / maximum}%).",
                SuggestedAction: "Reduce geometry before reaching the hard binary-format limit."));
    }
}

public sealed class BattleMapModeValidator : IMapModeValidator
{
    public MapMode Mode => MapMode.Battle;
    public void Validate(MapProject project, MapModeCapabilities capabilities, MapDiagnosticBag diagnostics)
    {
        if (!capabilities.HasGeneralSpawns)
            diagnostics.Add(new("MAP-MODE-002", MapDiagnosticSeverity.Error,
                "Battle requires at least two valid player spawns."));
    }
}

public sealed class SurvivalMapModeValidator : IMapModeValidator
{
    public MapMode Mode => MapMode.Survival;
    public void Validate(MapProject project, MapModeCapabilities capabilities, MapDiagnosticBag diagnostics)
    {
        if (!capabilities.HasGeneralSpawns)
            diagnostics.Add(new("MAP-MODE-002", MapDiagnosticSeverity.Error,
                "Survival requires at least two valid player spawns."));
    }
}

public sealed class CaptureMapModeValidator : IMapModeValidator
{
    public MapMode Mode => MapMode.Capture;
    public void Validate(MapProject project, MapModeCapabilities capabilities, MapDiagnosticBag diagnostics)
    {
        if (!capabilities.HasTeamZeroSpawns || !capabilities.HasTeamOneSpawns
            || !capabilities.HasTeamZeroBase || !capabilities.HasTeamOneBase)
            diagnostics.Add(new("MAP-MODE-002", MapDiagnosticSeverity.Error,
                "Capture requires team 0/1 spawns and team 0/1 capture bases."));
    }
}

public sealed class BountyMapModeValidator : IMapModeValidator
{
    public MapMode Mode => MapMode.Bounty;
    public void Validate(MapProject project, MapModeCapabilities capabilities, MapDiagnosticBag diagnostics)
    {
        if (!capabilities.HasGeneralSpawns || !capabilities.HasBountyBase)
            diagnostics.Add(new("MAP-MODE-002", MapDiagnosticSeverity.Error,
                "Bounty requires at least two player spawns and one bounty base."));
    }
}

public sealed class NodesMapModeValidator : IMapModeValidator
{
    public MapMode Mode => MapMode.Nodes;
    public void Validate(MapProject project, MapModeCapabilities capabilities, MapDiagnosticBag diagnostics)
    {
        if (!capabilities.HasGeneralSpawns || capabilities.ObjectiveNodeCount == 0)
            diagnostics.Add(new("MAP-MODE-002", MapDiagnosticSeverity.Error,
                "Nodes requires at least two player spawns and one node objective."));
    }
}
