using System;
using System.Collections.Generic;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Detached compiler input captured at the scheduler boundary.
///
/// The editor keeps a mutable <see cref="MapProject"/> graph because that is
/// the most useful authoring API. A background build must not retain any of
/// that graph, however: an edit made while a build is importing geometry must
/// describe the build that was requested, not whichever state the editor has
/// reached in the meantime. This type therefore captures the complete project
/// input graph explicitly, including the non-serialized paths used to resolve
/// package dependencies, and never exposes the detached graph to callers.
/// </summary>
public sealed class MapBuildSnapshot
{
    private readonly MapProject _project;

    private MapBuildSnapshot(MapProject project) => _project = project;

    /// <summary>Captures all mutable project data read by the compiler.</summary>
    public static MapBuildSnapshot Capture(MapProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new(CloneProject(project));
    }

    /// <summary>
    /// Gives one compiler operation its private mutable DTO graph. The graph is
    /// never shared with the editor or with another build operation.
    /// </summary>
    internal MapProject Materialize() => CloneProject(_project);

    private static MapProject CloneProject(MapProject source)
    {
        var copy = new MapProject
        {
            Format = source.Format,
            StableId = source.StableId,
            Version = source.Version,
            Metadata = CloneMetadata(source.Metadata),
            SupportedModes = [.. source.SupportedModes],
            Environment = source.Environment == null ? null : CloneEnvironment(source.Environment),
            Map = CloneDefinition(source.Map),
            PreviewImage = source.PreviewImage,
            Authoring = source.Authoring == null ? null : CloneAuthoring(source.Authoring),
            SourcePath = source.SourcePath,
            DeclaredContentIdentity = source.DeclaredContentIdentity
        };
        return copy;
    }

    private static MapProjectMetadata CloneMetadata(MapProjectMetadata source)
        => new()
        {
            Name = source.Name,
            Author = source.Author,
            Description = source.Description,
            Redistribution = source.Redistribution
        };

    private static MapEnvironment CloneEnvironment(MapEnvironment source)
        => new()
        {
            KillHeight = source.KillHeight,
            FarClip = source.FarClip,
            FogEnabled = source.FogEnabled,
            FogColor = Clone(source.FogColor),
            FogSlope = source.FogSlope,
            FogOffset = source.FogOffset,
            Light1Color = Clone(source.Light1Color),
            Light1Vector = Clone(source.Light1Vector),
            Light2Color = Clone(source.Light2Color),
            Light2Vector = Clone(source.Light2Vector)
        };

    private static MapDefinition CloneDefinition(MapDefinition source)
    {
        var copy = new MapDefinition
        {
            Name = source.Name,
            InGameName = source.InGameName,
            TextureSource = source.TextureSource,
            ScaleFactor = source.ScaleFactor,
            KillHeight = source.KillHeight,
            FarClip = source.FarClip,
            FogEnabled = source.FogEnabled,
            FogColor = Clone(source.FogColor),
            FogSlope = source.FogSlope,
            FogOffset = source.FogOffset,
            Light1Color = Clone(source.Light1Color),
            Light1Vector = Clone(source.Light1Vector),
            Light2Color = Clone(source.Light2Color),
            Light2Vector = Clone(source.Light2Vector),
            BattleTimeLimit = source.BattleTimeLimit,
            PointLimit = source.PointLimit,
            Import = source.Import == null ? null : CloneImport(source.Import),
            Preview = source.Preview == null ? null : ClonePreview(source.Preview),
            BaseDirectory = source.BaseDirectory,
            BundlePath = source.BundlePath,
            SourcePath = source.SourcePath
        };
        copy.Materials = [.. source.Materials.ConvertAll(CloneMaterial)];
        copy.Brushes = [.. source.Brushes.ConvertAll(CloneBrush)];
        copy.Spawns = [.. source.Spawns.ConvertAll(CloneSpawn)];
        copy.JumpPads = [.. source.JumpPads.ConvertAll(CloneJumpPad)];
        copy.Items = [.. source.Items.ConvertAll(CloneItem)];
        return copy;
    }

    private static MapPreview ClonePreview(MapPreview source)
        => new() { Position = Clone(source.Position), Target = Clone(source.Target) };

    private static MapImport CloneImport(MapImport source)
        => new()
        {
            Source = source.Source,
            BundlePath = source.BundlePath,
            BaseDirectory = source.BaseDirectory,
            MapName = source.MapName,
            UnitsPerUnit = source.UnitsPerUnit,
            Textures = source.Textures,
            ShaderMaterials = new Dictionary<string, int>(source.ShaderMaterials,
                StringComparer.Ordinal),
            DefaultMaterial = source.DefaultMaterial,
            TexScale = source.TexScale,
            KeepSky = source.KeepSky,
            KeepClip = source.KeepClip,
            PatchLevel = source.PatchLevel,
            KeepSpawns = source.KeepSpawns
        };

    private static MapMaterial CloneMaterial(MapMaterial source)
        => new() { Name = source.Name, SourceMaterial = source.SourceMaterial, TexScale = source.TexScale };

    private static MapBrush CloneBrush(MapBrush source)
        => new()
        {
            Min = Clone(source.Min),
            Max = Clone(source.Max),
            Material = source.Material,
            Shade = source.Shade,
            Solid = source.Solid,
            Damaging = source.Damaging,
            Terrain = source.Terrain
        };

    private static MapSpawn CloneSpawn(MapSpawn source)
        => new() { Position = Clone(source.Position), Yaw = source.Yaw };

    private static MapJumpPad CloneJumpPad(MapJumpPad source)
        => new()
        {
            Position = Clone(source.Position),
            Target = source.Target == null ? null : Clone(source.Target),
            Vector = source.Vector == null ? null : Clone(source.Vector),
            Speed = source.Speed,
            Size = Clone(source.Size),
            ModelId = source.ModelId,
            CooldownTime = source.CooldownTime,
            ControlLockTime = source.ControlLockTime
        };

    private static MapItem CloneItem(MapItem source)
        => new()
        {
            Position = Clone(source.Position),
            Type = source.Type,
            HasBase = source.HasBase,
            SpawnInterval = source.SpawnInterval
        };

    private static MapAuthoringScene CloneAuthoring(MapAuthoringScene source)
    {
        var copy = new MapAuthoringScene
        {
            Editor = CloneEditorSettings(source.Editor)
        };
        copy.Materials = [.. source.Materials.ConvertAll(CloneAuthoringMaterial)];
        copy.Brushes = [.. source.Brushes.ConvertAll(CloneConvexBrush)];
        copy.Entities = [.. source.Entities.ConvertAll(CloneEntity)];
        return copy;
    }

    private static MapAuthoringMaterial CloneAuthoringMaterial(MapAuthoringMaterial source)
        => new()
        {
            Id = source.Id,
            Name = source.Name,
            SourceRoom = source.SourceRoom,
            SourceMaterial = source.SourceMaterial,
            CustomImage = source.CustomImage,
            Tiling = source.Tiling,
            Offset = Clone(source.Offset),
            Rotation = source.Rotation,
            Terrain = source.Terrain
        };

    private static ConvexBrush CloneConvexBrush(ConvexBrush source)
    {
        var copy = new ConvexBrush
        {
            Id = source.Id,
            Primitive = source.Primitive,
            Transform = CloneTransform(source.Transform),
            Solid = source.Solid,
            Damaging = source.Damaging,
            Shade = source.Shade
        };
        copy.Faces = [.. source.Faces.ConvertAll(CloneConvexFace)];
        return copy;
    }

    private static ConvexBrushFace CloneConvexFace(ConvexBrushFace source)
        => new()
        {
            Normal = Clone(source.Normal),
            Distance = source.Distance,
            MaterialId = source.MaterialId,
            TextureOffset = Clone(source.TextureOffset),
            TextureRotation = source.TextureRotation,
            TextureScale = source.TextureScale
        };

    private static MapTransform CloneTransform(MapTransform source)
        => new()
        {
            Position = Clone(source.Position),
            Rotation = Clone(source.Rotation),
            Scale = Clone(source.Scale)
        };

    private static MapEntityDefinition CloneEntity(MapEntityDefinition source)
        => new()
        {
            Id = source.Id,
            Kind = source.Kind,
            Transform = CloneTransform(source.Transform),
            Size = Clone(source.Size),
            Target = source.Target == null ? null : Clone(source.Target),
            LaunchVector = source.LaunchVector == null ? null : Clone(source.LaunchVector),
            LaunchSpeed = source.LaunchSpeed,
            ControlLockTime = source.ControlLockTime,
            CooldownTime = source.CooldownTime,
            ModelId = source.ModelId,
            ItemType = source.ItemType,
            HasBase = source.HasBase,
            SpawnInterval = source.SpawnInterval,
            Team = source.Team,
            DamagePerTick = source.DamagePerTick,
            TargetObjectId = source.TargetObjectId
        };

    private static MapEditorSettings CloneEditorSettings(MapEditorSettings source)
        => new()
        {
            PositionSnap = source.PositionSnap,
            RotationSnap = source.RotationSnap,
            ScaleSnap = source.ScaleSnap,
            CameraPosition = Clone(source.CameraPosition),
            CameraTarget = Clone(source.CameraTarget),
            ShowCollision = source.ShowCollision,
            ShowEntities = source.ShowEntities,
            ShowWorldBounds = source.ShowWorldBounds
        };

    private static int[] Clone(int[] source) => (int[])source.Clone();
    private static float[] Clone(float[] source) => (float[])source.Clone();
}
