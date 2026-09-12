using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen;

public sealed class NativeMapProjectImporter : IMapImporter
{
    private const float Epsilon = 0.0005f;

    public MapBuildScene Import(MapProject project, bool verbose)
    {
        ArgumentNullException.ThrowIfNull(project);
        MapAuthoringScene authoring = project.Authoring
            ?? throw new ArgumentException("Native importer requires an authoring scene.", nameof(project));
        MapDefinition definition = Clone(project.Map);
        project.Environment?.ApplyTo(definition);
        definition.Brushes.Clear();
        definition.Spawns.Clear();
        definition.Items.Clear();
        definition.JumpPads.Clear();

        IReadOnlyList<MapAuthoringMaterial> materials = authoring.Materials
            .OrderBy(material => material.Id, StringComparer.Ordinal).ToArray();
        if (materials.Count == 0)
            throw Failure("MAP-MAT-001", "Native maps require at least one material.");
        string[] sourceRooms = materials.Where(material => material.SourceMaterial.HasValue)
            .Select(material => material.SourceRoom ?? definition.TextureSource)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (sourceRooms.Length > 1)
            throw Failure("MAP-MAT-007",
                "The current Prime material packer requires base-game material references to use one source room.");
        if (sourceRooms.Length == 1) definition.TextureSource = sourceRooms[0];
        definition.Materials = materials.Select(material => new MapMaterial
        {
            Name = material.Name,
            SourceMaterial = material.SourceMaterial ?? 0,
            TexScale = material.Tiling
        }).ToList();
        Dictionary<string, int> materialIndices = materials.Select((material, index) => (material.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);

        var scene = new MapBuildScene(definition);
        var customSources = materials.Select((material, index) => (material, index))
            .Where(value => !string.IsNullOrWhiteSpace(value.material.CustomImage))
            .Select(value => (value.index, value.material.Name,
                (ReadOnlyMemory<byte>)ReadCustomImage(project, value.material.CustomImage!)))
            .ToArray();
        if (customSources.Length != 0)
        {
            MapTexturePack pack = MapTextureBake.BakeImages(customSources);
            scene.CustomTextures = pack.Entries.ToDictionary(entry => entry.SourceIndex);
        }
        foreach (ConvexBrush brush in authoring.Brushes.OrderBy(value => value.Id, StringComparer.Ordinal))
            AddBrush(scene, brush, materials, materialIndices);
        AddEntities(scene, definition, authoring.Entities);
        return scene;
    }

    private static byte[] ReadCustomImage(MapProject project, string path)
    {
        if (project.SourcePath != null && MapBundle.Is(project.SourcePath))
            return new MapBundleReader().Read(project.SourcePath).ReadDeclaredFile(path);
        string baseDirectory = project.Map.BaseDirectory
            ?? (project.SourcePath == null ? Directory.GetCurrentDirectory()
                : Path.GetDirectoryName(project.SourcePath)!);
        string fullPath = Path.GetFullPath(path, baseDirectory);
        if (!File.Exists(fullPath)) throw Failure("MAP-MAT-005",
            $"Custom material image '{path}' is missing.");
        return File.ReadAllBytes(fullPath);
    }

    private static MapDefinition Clone(MapDefinition value)
        => JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(value,
            MapJsonContext.Default.MapDefinition), MapJsonContext.Default.MapDefinition)
            ?? throw new InvalidOperationException("Map definition clone failed.");

    private static void AddBrush(MapBuildScene scene, ConvexBrush brush,
        IReadOnlyList<MapAuthoringMaterial> materials, IReadOnlyDictionary<string, int> materialIndices)
    {
        ValidateTransform(brush.Transform, brush.Id);
        if (!MapObjectId.IsValid(brush.Id)) throw Failure("MAP-GEO-007", $"Invalid brush ID '{brush.Id}'.", brush.Id);
        if (brush.Faces.Count < 4) throw Failure("MAP-GEO-008", "A convex brush requires at least four planes.", brush.Id);
        var planes = brush.Faces.Select((face, index) => MakePlane(face, brush.Id, index)).ToArray();
        List<Vector3> vertices = Intersections(planes);
        if (vertices.Count < 4) throw Failure("MAP-GEO-009", "Brush planes do not enclose a valid convex volume.", brush.Id);
        Matrix4 transform = Transform(brush.Transform);

        for (int faceIndex = 0; faceIndex < planes.Length; faceIndex++)
        {
            (Vector3 normal, float distance) = planes[faceIndex];
            ConvexBrushFace sourceFace = brush.Faces[faceIndex];
            if (!materialIndices.TryGetValue(sourceFace.MaterialId, out int material))
                throw Failure("MAP-MAT-005", $"Brush references missing material '{sourceFace.MaterialId}'.", brush.Id);
            Vector3[] local = vertices.Where(point => MathF.Abs(Vector3.Dot(normal, point) - distance) <= Epsilon * 4)
                .ToArray();
            if (local.Length < 3) continue;
            local = SortFace(local, normal);
            Vector3[] points = local.Select(point => Vector3.TransformPosition(point, transform)).ToArray();
            Vector3 worldNormal = Vector3.Cross(points[1] - points[0], points[2] - points[0]).Normalized();
            Vector3 expected = TransformNormal(normal, brush.Transform);
            if (Vector3.Dot(worldNormal, expected) < 0)
            {
                Array.Reverse(points);
                worldNormal = -worldNormal;
            }
            float tiling = materials[material].Tiling * sourceFace.TextureScale;
            float[] offset =
            [
                materials[material].Offset[0] + sourceFace.TextureOffset[0],
                materials[material].Offset[1] + sourceFace.TextureOffset[1]
            ];
            Vector2[] texcoords = points.Select(point => Project(point, worldNormal,
                brush.Transform.Position, tiling, offset,
                materials[material].Rotation + sourceFace.TextureRotation)).ToArray();
            Terrain terrain = Enum.TryParse(materials[material].Terrain, true, out Terrain parsed)
                ? parsed : Terrain.Metal;
            float shade = Math.Clamp(brush.Shade * (0.6f + MathF.Max(0, worldNormal.Y) * 0.4f), 0, 1);
            var face = new BuiltFace(points, texcoords, worldNormal, material, shade)
            {
                Damaging = brush.Damaging,
                Terrain = terrain
            };
            scene.Faces.Add(face);
            if (brush.Solid) scene.Solid.Add(face);
        }
    }

    private static (Vector3 Normal, float Distance) MakePlane(ConvexBrushFace face,
        string objectId, int index)
    {
        if (face.Normal.Length != 3 || face.TextureOffset.Length != 2)
            throw Failure("MAP-GEO-008", $"Brush plane {index} has invalid vector dimensions.", objectId);
        Vector3 normal = ToVector(face.Normal);
        if (!Finite(normal) || normal.LengthSquared < Epsilon || !float.IsFinite(face.Distance))
            throw Failure("MAP-GEO-008", $"Brush plane {index} is not finite.", objectId);
        return (normal.Normalized(), face.Distance / normal.Length);
    }

    private static List<Vector3> Intersections((Vector3 Normal, float Distance)[] planes)
    {
        var points = new List<Vector3>();
        for (int a = 0; a < planes.Length - 2; a++)
        for (int b = a + 1; b < planes.Length - 1; b++)
        for (int c = b + 1; c < planes.Length; c++)
        {
            Vector3 cross = Vector3.Cross(planes[b].Normal, planes[c].Normal);
            float determinant = Vector3.Dot(planes[a].Normal, cross);
            if (MathF.Abs(determinant) < Epsilon) continue;
            Vector3 point = (planes[a].Distance * cross
                + planes[b].Distance * Vector3.Cross(planes[c].Normal, planes[a].Normal)
                + planes[c].Distance * Vector3.Cross(planes[a].Normal, planes[b].Normal)) / determinant;
            if (planes.Any(plane => Vector3.Dot(plane.Normal, point) > plane.Distance + Epsilon)) continue;
            if (!points.Any(existing => (existing - point).LengthSquared < Epsilon * Epsilon)) points.Add(point);
        }
        return points;
    }

    private static Vector3[] SortFace(Vector3[] points, Vector3 normal)
    {
        Vector3 center = points.Aggregate(Vector3.Zero, (sum, point) => sum + point) / points.Length;
        Vector3 axis = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 u = Vector3.Cross(axis, normal).Normalized();
        Vector3 v = Vector3.Cross(normal, u).Normalized();
        return points.OrderBy(point => MathF.Atan2(Vector3.Dot(point - center, v),
            Vector3.Dot(point - center, u))).ToArray();
    }

    private static Matrix4 Transform(MapTransform value)
    {
        Vector3 rotation = ToVector(value.Rotation) * (MathF.PI / 180);
        return Matrix4.CreateScale(ToVector(value.Scale))
            * Matrix4.CreateRotationX(rotation.X) * Matrix4.CreateRotationY(rotation.Y)
            * Matrix4.CreateRotationZ(rotation.Z) * Matrix4.CreateTranslation(ToVector(value.Position));
    }

    private static Vector3 TransformNormal(Vector3 normal, MapTransform value)
    {
        Vector3 scale = ToVector(value.Scale);
        Vector3 adjusted = new(normal.X / scale.X, normal.Y / scale.Y, normal.Z / scale.Z);
        Vector3 rotation = ToVector(value.Rotation) * (MathF.PI / 180);
        return Vector3.TransformNormal(adjusted, Matrix4.CreateRotationX(rotation.X)
            * Matrix4.CreateRotationY(rotation.Y) * Matrix4.CreateRotationZ(rotation.Z)).Normalized();
    }

    private static Vector2 Project(Vector3 point, Vector3 normal, float[] originValues,
        float scale, float[] offset, float rotationDegrees)
    {
        Vector3 origin = ToVector(originValues);
        Vector2 uv;
        float ax = MathF.Abs(normal.X), ay = MathF.Abs(normal.Y), az = MathF.Abs(normal.Z);
        if (ay > ax && ay >= az) uv = new((point.X - origin.X) * scale, (point.Z - origin.Z) * scale);
        else if (ax >= az) uv = new((point.Z - origin.Z) * scale, (origin.Y - point.Y) * scale);
        else uv = new((point.X - origin.X) * scale, (origin.Y - point.Y) * scale);
        float radians = rotationDegrees * MathF.PI / 180;
        float cosine = MathF.Cos(radians), sine = MathF.Sin(radians);
        return new Vector2(uv.X * cosine - uv.Y * sine + offset[0],
            uv.X * sine + uv.Y * cosine + offset[1]);
    }

    private static void AddEntities(MapBuildScene scene, MapDefinition definition,
        IEnumerable<MapEntityDefinition> entities)
    {
        foreach (MapEntityDefinition entity in entities.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            if (!MapObjectId.IsValid(entity.Id))
                throw Failure("MAP-ENT-002", $"Invalid entity ID '{entity.Id}'.", entity.Id);
            ValidateTransform(entity.Transform, entity.Id);
            float[] position = (float[])entity.Transform.Position.Clone();
            switch (entity.Kind)
            {
                case MapEntityKind.PlayerSpawn:
                    definition.Spawns.Add(new MapSpawn { Position = position, Yaw = entity.Transform.Rotation[1] });
                    break;
                case MapEntityKind.TeamSpawn:
                    scene.Entities.Add(new MphRead.Editor.PlayerSpawnEntityEditor
                    {
                        Id = checked((short)scene.Entities.Count), LayerMask = ushort.MaxValue,
                        Position = ToVector(position), Up = Vector3.UnitY,
                        Facing = Facing(entity.Transform.Rotation[1]), NodeName = "rmMain",
                        Active = true, Availability = 0, TeamIndex = checked((sbyte)entity.Team)
                    });
                    break;
                case MapEntityKind.ItemSpawn:
                    definition.Items.Add(new MapItem
                    {
                        Position = position,
                        Type = entity.ItemType,
                        HasBase = entity.HasBase,
                        SpawnInterval = checked((ushort)Math.Clamp((int)entity.SpawnInterval, 0, ushort.MaxValue))
                    });
                    break;
                case MapEntityKind.JumpPad:
                    definition.JumpPads.Add(new MapJumpPad
                    {
                        Position = position,
                        Target = entity.Target == null ? null : (float[])entity.Target.Clone(),
                        Vector = entity.LaunchVector == null ? null : (float[])entity.LaunchVector.Clone(),
                        Speed = entity.LaunchSpeed,
                        Size = (float[])entity.Size.Clone(),
                        ModelId = checked((uint)Math.Max(0, entity.ModelId)),
                        ControlLockTime = checked((ushort)Math.Clamp((int)entity.ControlLockTime, 0, ushort.MaxValue)),
                        CooldownTime = checked((ushort)Math.Clamp((int)entity.CooldownTime, 0, ushort.MaxValue))
                    });
                    break;
                case MapEntityKind.DamageVolume:
                case MapEntityKind.KillVolume:
                    scene.Entities.Add(new MphRead.Editor.AreaVolumeEntityEditor
                    {
                        Id = checked((short)scene.Entities.Count),
                        LayerMask = ushort.MaxValue,
                        Position = ToVector(position),
                        Up = Vector3.UnitY,
                        Facing = Vector3.UnitZ,
                        NodeName = "geo1",
                        Volume = MakeCenteredBox(entity.Size),
                        InsideMessage = entity.Kind == MapEntityKind.KillVolume
                            ? MphRead.Message.Death : MphRead.Message.Damage,
                        InsideMsgParam1 = entity.Kind == MapEntityKind.KillVolume
                            ? Math.Max(1, entity.DamagePerTick) : Math.Max(1, entity.DamagePerTick),
                        Cooldown = 1
                    });
                    break;
                case MapEntityKind.CaptureBase:
                case MapEntityKind.BountyBase:
                    byte team = entity.Kind == MapEntityKind.BountyBase
                        ? (byte)2 : checked((byte)entity.Team);
                    scene.Entities.Add(new MphRead.Editor.OctolithFlagEntityEditor
                    {
                        Id = checked((short)scene.Entities.Count), LayerMask = ushort.MaxValue,
                        Position = ToVector(position), Up = Vector3.UnitY,
                        Facing = Facing(entity.Transform.Rotation[1]), NodeName = "rmMain", TeamId = team
                    });
                    scene.Entities.Add(new MphRead.Editor.FlagBaseEntityEditor
                    {
                        Id = checked((short)scene.Entities.Count), LayerMask = ushort.MaxValue,
                        Position = ToVector(position), Up = Vector3.UnitY,
                        Facing = Facing(entity.Transform.Rotation[1]), NodeName = "rmMain",
                        TeamId = team, Volume = MakeCenteredBox(entity.Size)
                    });
                    break;
                case MapEntityKind.NodeObjective:
                    scene.Entities.Add(new MphRead.Editor.NodeDefenseEntityEditor
                    {
                        Id = checked((short)scene.Entities.Count), LayerMask = ushort.MaxValue,
                        Position = ToVector(position), Up = Vector3.UnitY,
                        Facing = Facing(entity.Transform.Rotation[1]), NodeName = "rmMain",
                        Volume = MakeCenteredCylinder(entity.Size)
                    });
                    break;
                default:
                    throw Failure("MAP-MODE-004",
                        $"{entity.Kind} requires a later mode/entity compiler.", entity.Id);
            }
        }
        MapBuilder.AddEntities(scene, definition);
    }

    private static MphRead.CollisionVolume MakeCenteredBox(float[] size)
        => new()
        {
            Type = MphRead.VolumeType.Box,
            BoxVector1 = Vector3.UnitX,
            BoxVector2 = Vector3.UnitY,
            BoxVector3 = Vector3.UnitZ,
            BoxPosition = new Vector3(-size[0] / 2, -size[1] / 2, -size[2] / 2),
            BoxDot1 = size[0], BoxDot2 = size[1], BoxDot3 = size[2]
        };

    private static MphRead.CollisionVolume MakeCenteredCylinder(float[] size)
        => new()
        {
            Type = MphRead.VolumeType.Cylinder,
            CylinderVector = Vector3.UnitY,
            CylinderPosition = new Vector3(0, -size[1] / 2, 0),
            CylinderRadius = MathF.Max(size[0], size[2]) / 2,
            CylinderDot = size[1]
        };

    private static Vector3 Facing(float yawDegrees)
    {
        float yaw = MathHelper.DegreesToRadians(yawDegrees);
        return new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw)).Normalized();
    }

    private static void ValidateTransform(MapTransform value, string objectId)
    {
        if (value.Position.Length != 3 || value.Rotation.Length != 3 || value.Scale.Length != 3
            || !Finite(ToVector(value.Position)) || !Finite(ToVector(value.Rotation))
            || !Finite(ToVector(value.Scale)) || value.Scale.Any(component => MathF.Abs(component) < Epsilon))
            throw Failure("MAP-GEO-010", "Object transform is invalid or has a zero scale axis.", objectId);
    }

    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static Vector3 ToVector(float[] value) => new(value[0], value[1], value[2]);
    private static MapCompilationException Failure(string code, string message, string? objectId = null)
        => new(message, [new(code, MapDiagnosticSeverity.Error, message, ObjectId: objectId)]);
}

public static class ConvexBrushFactory
{
    public static ConvexBrush Box(string id, string materialId, Vector3 size)
    {
        Vector3 half = size / 2;
        return new ConvexBrush
        {
            Id = id,
            Primitive = MapPrimitiveKind.Box,
            Faces =
            [
                Face(Vector3.UnitX, half.X, materialId), Face(-Vector3.UnitX, half.X, materialId),
                Face(Vector3.UnitY, half.Y, materialId), Face(-Vector3.UnitY, half.Y, materialId),
                Face(Vector3.UnitZ, half.Z, materialId), Face(-Vector3.UnitZ, half.Z, materialId)
            ]
        };
    }

    public static ConvexBrush Wedge(string id, string materialId, Vector3 size)
    {
        Vector3 half = size / 2;
        Vector3 slope = new(0, size.Z, size.Y);
        slope.Normalize();
        return new ConvexBrush
        {
            Id = id,
            Primitive = MapPrimitiveKind.Wedge,
            Faces =
            [
                Face(Vector3.UnitX, half.X, materialId), Face(-Vector3.UnitX, half.X, materialId),
                Face(-Vector3.UnitY, half.Y, materialId), Face(Vector3.UnitZ, half.Z, materialId),
                Face(new Vector3(0, size.Z, -size.Y).Normalized(), 0, materialId)
            ]
        };
    }

    public static ConvexBrush Cylinder(string id, string materialId, float radius, float height, int sides = 12)
    {
        sides = Math.Clamp(sides, 3, 32);
        var brush = new ConvexBrush { Id = id, Primitive = MapPrimitiveKind.Cylinder };
        brush.Faces.Add(Face(Vector3.UnitY, height / 2, materialId));
        brush.Faces.Add(Face(-Vector3.UnitY, height / 2, materialId));
        for (int i = 0; i < sides; i++)
        {
            float angle = i * MathF.Tau / sides;
            brush.Faces.Add(Face(new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)), radius, materialId));
        }
        return brush;
    }

    public static IReadOnlyList<ConvexBrush> Stairs(string idPrefix, string materialId,
        Vector3 size, int steps = 8)
    {
        steps = Math.Clamp(steps, 1, 32);
        var result = new List<ConvexBrush>(steps);
        float depth = size.Z / steps;
        float rise = size.Y / steps;
        for (int i = 0; i < steps; i++)
        {
            ConvexBrush step = Box($"{idPrefix}.{i + 1}", materialId,
                new Vector3(size.X, rise * (i + 1), depth));
            step.Primitive = MapPrimitiveKind.Stairs;
            step.Transform.Position = [0, -size.Y / 2 + rise * (i + 1) / 2,
                -size.Z / 2 + depth * (i + 0.5f)];
            result.Add(step);
        }
        return result;
    }

    private static ConvexBrushFace Face(Vector3 normal, float distance, string materialId)
        => new() { Normal = [normal.X, normal.Y, normal.Z], Distance = distance, MaterialId = materialId };
}
