using MphRead;
using MphRead.Mods;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;
using ProjectPrime.Editor.Documents;

namespace ProjectPrime.Editor.Viewport;

public sealed class EditorViewport
{
    private readonly EditorCamera _camera = new();
    private CpuMesh? _mesh;
    private object _meshIdentity = new();
    private CpuMesh? _overlayMesh;
    private object _overlayMeshIdentity = new();
    private int _revision = -1;
    private string? _selectedId;
    private Vector3 _min = new(-1);
    private Vector3 _max = new(1);

    public EditorCamera Camera => _camera;
    public Vector4 ClearColor { get; set; } = new(0.035f, 0.045f, 0.065f, 1);

    public void Update(MapDocument document, RenderSurfaceInput input, Vector2i size,
        EditorRect viewport, float elapsedSeconds)
    {
        bool hovered = viewport.Contains(input.MousePosition);
        _camera.Update(input, elapsedSeconds, hovered);
        if (_revision != document.Revision || _selectedId != document.SelectedObjectId)
        {
            Rebuild(document);
            _revision = document.Revision;
            _selectedId = document.SelectedObjectId;
        }
        if (hovered && input.LeftPressed && document.Project.Authoring != null)
            document.SelectedObjectId = Raycast(document.Project.Authoring, input.MousePosition,
                size, viewport);
        if (input.Pressed(RenderSurfaceKey.F)) _camera.Frame(_min, _max);
    }

    public RenderFrame BuildFrame(MapDocument document, Vector2i size)
    {
        var frame = new RenderFrame(capacity: 8, maximumCapacity: 8192);
        float aspect = size.Y == 0 ? 1 : size.X / (float)size.Y;
        Matrix4 view = _camera.View;
        Matrix4 inverse = view.Inverted();
        Matrix4 projection = _camera.Projection(aspect,
            Math.Max(500, document.Project.Environment?.FarClip ?? document.Project.Map.FarClip));
        var quality = new RenderQualitySnapshot(GraphicsPreset.Original,
            TextureFilteringPreset.Smooth, AnisotropyLevel.Off, MsaaLevel.Off,
            Bloom: false, DynamicVisualLights: false);
        frame.CaptureState(view, inverse, inverse, projection, _camera.EyePosition,
            size, size, ClearColor, new Vector3(0.3f, -1, 0.2f).Normalized(),
            Vector3.One, new Vector3(-0.3f, 1, -0.2f).Normalized(),
            new Vector3(0.3f), false, Vector4.Zero, 0, 0,
            new RenderFrameOptions(true, true, false, true, true, true, false,
                false, 4, 0, 0, false, false, quality));
        if (_mesh != null && _mesh.VertexCount != 0)
        {
            frame.CaptureMesh(_meshIdentity, _mesh);
            DrawSubmission draw = frame.Acquire();
            draw.GeometryIdentity = _meshIdentity;
            draw.Primitive = RenderPrimitive.Mesh;
            draw.PolygonMode = PolygonMode.Modulate;
            draw.CullingMode = CullingMode.Back;
            draw.Diffuse = Vector3.One;
            draw.Ambient = new Vector3(0.35f);
            draw.Lighting = true;
            draw.Alpha = 1;
            frame.Add(draw);
        }
        if (_overlayMesh != null && _overlayMesh.LineIndexCount != 0)
        {
            frame.CaptureMesh(_overlayMeshIdentity, _overlayMesh);
            DrawSubmission overlay = frame.Acquire();
            overlay.GeometryIdentity = _overlayMeshIdentity;
            overlay.Primitive = RenderPrimitive.Mesh;
            overlay.PolygonMode = PolygonMode.Modulate;
            overlay.CullingMode = CullingMode.Neither;
            overlay.Diffuse = Vector3.One;
            overlay.Ambient = Vector3.One;
            overlay.Lighting = false;
            overlay.Wireframe = true;
            overlay.Alpha = 1;
            frame.Add(overlay);
        }
        return frame;
    }

    private void Rebuild(MapDocument document)
    {
        try
        {
            MapBuildScene scene = (document.Project.Authoring == null
                ? (IMapImporter)new LegacyMapImporter() : new NativeMapProjectImporter())
                .Import(document.Project, false);
            var vertices = new List<RenderVertex>();
            var triangles = new List<int>();
            var lines = new List<int>();
            _min = new Vector3(float.MaxValue);
            _max = new Vector3(float.MinValue);
            foreach (BuiltFace face in scene.Faces)
            {
                int start = vertices.Count;
                Vector4 color = FaceColor(face, document.SelectedObjectId);
                for (int index = 0; index < face.Points.Length; index++)
                {
                    Vector3 point = face.Points[index];
                    _min = Vector3.ComponentMin(_min, point);
                    _max = Vector3.ComponentMax(_max, point);
                    vertices.Add(new RenderVertex(point, color, face.Normal,
                        face.Texcoords[index], flags: RenderVertexFlags.ExplicitColor));
                    lines.Add(start + index);
                    lines.Add(start + (index + 1) % face.Points.Length);
                }
                for (int index = 1; index < face.Points.Length - 1; index++)
                {
                    triangles.Add(start);
                    triangles.Add(start + index);
                    triangles.Add(start + index + 1);
                }
            }
            if (vertices.Count == 0) { _min = new(-1); _max = new(1); }
            _mesh = new CpuMesh(vertices.ToArray(), triangles.ToArray(), lines.ToArray());
            _meshIdentity = new object();
            _overlayMesh = BuildOverlay(document, scene);
            _overlayMeshIdentity = new object();
        }
        catch
        {
            _mesh = null;
            _meshIdentity = new object();
            _overlayMesh = null;
            _overlayMeshIdentity = new object();
        }
    }

    private CpuMesh BuildOverlay(MapDocument document, MapBuildScene scene)
    {
        var vertices = new List<RenderVertex>();
        var lines = new List<int>();
        MapAuthoringScene? authoring = document.Project.Authoring;
        Vector4 grid = new(0.18f, 0.23f, 0.28f, 1);
        int extent = Math.Clamp((int)MathF.Ceiling(MathF.Max(
            MathF.Max(MathF.Abs(_min.X), MathF.Abs(_max.X)),
            MathF.Max(MathF.Abs(_min.Z), MathF.Abs(_max.Z)))) + 2, 8, 64);
        for (int value = -extent; value <= extent; value++)
        {
            AppendLine(new(-extent, 0, value), new(extent, 0, value), value == 0 ? new(0.5f, 0.2f, 0.2f, 1) : grid);
            AppendLine(new(value, 0, -extent), new(value, 0, extent), value == 0 ? new(0.2f, 0.35f, 0.65f, 1) : grid);
        }
        if (authoring?.Editor.ShowCollision == true)
        {
            Vector4 collision = new(0.1f, 0.9f, 0.9f, 1);
            foreach (BuiltFace face in scene.Solid)
                for (int index = 0; index < face.Points.Length; index++)
                    AppendLine(face.Points[index], face.Points[(index + 1) % face.Points.Length], collision);
        }
        if (authoring?.Editor.ShowEntities == true)
        {
            foreach (MapEntityDefinition entity in authoring.Entities.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                Vector3 center = ToVector(entity.Transform.Position);
                Vector3 size = ToVector(entity.Size) * 0.5f;
                Vector4 color = entity.Id == document.SelectedObjectId
                    ? new(1, 0.85f, 0.15f, 1) : EntityColor(entity.Kind);
                AppendBox(center - size, center + size, color);
                float yaw = MathHelper.DegreesToRadians(entity.Transform.Rotation[1]);
                AppendLine(center, center + new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw))
                    * MathF.Max(1, size.Length), color);
                if (entity.Kind == MapEntityKind.JumpPad) AppendJumpTrajectory(entity, color);
            }
        }
        if (authoring?.Editor.ShowWorldBounds == true) AppendBox(_min, _max, new(0.9f, 0.35f, 0.85f, 1));
        if (authoring != null && document.SelectedObjectId is { } selected)
        {
            MapTransform? transform = authoring.Brushes.FirstOrDefault(value => value.Id == selected)?.Transform
                ?? authoring.Entities.FirstOrDefault(value => value.Id == selected)?.Transform;
            if (transform != null)
            {
                Vector3 origin = ToVector(transform.Position);
                AppendLine(origin, origin + Vector3.UnitX * 2, new(1, 0.15f, 0.15f, 1));
                AppendLine(origin, origin + Vector3.UnitY * 2, new(0.15f, 1, 0.25f, 1));
                AppendLine(origin, origin + Vector3.UnitZ * 2, new(0.15f, 0.4f, 1, 1));
            }
        }
        return new CpuMesh(vertices.ToArray(), [], lines.ToArray());

        void AppendLine(Vector3 first, Vector3 second, Vector4 color)
        {
            int start = vertices.Count;
            vertices.Add(new(first, color, Vector3.UnitY, Vector2.Zero,
                flags: RenderVertexFlags.ExplicitColor));
            vertices.Add(new(second, color, Vector3.UnitY, Vector2.Zero,
                flags: RenderVertexFlags.ExplicitColor));
            lines.Add(start); lines.Add(start + 1);
        }
        void AppendBox(Vector3 min, Vector3 max, Vector4 color)
        {
            Vector3[] points =
            [
                new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
                new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
                new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
                new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z)
            ];
            int[] edges = [0,1, 1,2, 2,3, 3,0, 4,5, 5,6, 6,7, 7,4, 0,4, 1,5, 2,6, 3,7];
            for (int index = 0; index < edges.Length; index += 2)
                AppendLine(points[edges[index]], points[edges[index + 1]], color);
        }
        void AppendJumpTrajectory(MapEntityDefinition entity, Vector4 color)
        {
            try
            {
                var pad = new MapJumpPad
                {
                    Position = (float[])entity.Transform.Position.Clone(),
                    Target = entity.Target == null ? null : (float[])entity.Target.Clone(),
                    Vector = entity.LaunchVector == null ? null : (float[])entity.LaunchVector.Clone(),
                    Speed = entity.LaunchSpeed
                };
                (Vector3 direction, float speed) = MapBuilder.SolveJumpPad(pad);
                Vector3 start = ToVector(entity.Transform.Position), velocity = direction * speed;
                Vector3 previous = start;
                const float gravity = 77 / 4096f;
                for (int frame = 2; frame <= 120; frame += 2)
                {
                    Vector3 point = start + velocity * frame - Vector3.UnitY * (gravity * frame * frame / 2);
                    AppendLine(previous, point, color);
                    previous = point;
                    if (point.Y < (document.Project.Environment?.KillHeight
                        ?? document.Project.Map.KillHeight)) break;
                }
            }
            catch (ProgramException) { }
        }
    }

    private static Vector4 EntityColor(MapEntityKind kind) => kind switch
    {
        MapEntityKind.PlayerSpawn or MapEntityKind.TeamSpawn => new(0.2f, 1, 0.35f, 1),
        MapEntityKind.ItemSpawn => new(0.95f, 0.65f, 0.15f, 1),
        MapEntityKind.JumpPad => new(0.2f, 0.65f, 1, 1),
        MapEntityKind.DamageVolume or MapEntityKind.KillVolume => new(1, 0.2f, 0.2f, 1),
        MapEntityKind.CaptureBase or MapEntityKind.BountyBase or MapEntityKind.NodeObjective
            => new(0.8f, 0.25f, 1, 1),
        _ => new(0.8f, 0.8f, 0.8f, 1)
    };

    private static Vector3 ToVector(float[] value) => new(value[0], value[1], value[2]);

    private static Vector4 FaceColor(BuiltFace face, string? selected)
    {
        _ = selected;
        float hue = (face.Material * 0.173f) % 1;
        Vector3 baseColor = Hsv(hue, 0.42f, 0.78f) * Math.Clamp(face.Shade, 0.3f, 1);
        return new Vector4(baseColor, 1);
    }

    private string? Raycast(MapAuthoringScene scene, Vector2 mouse, Vector2i window,
        EditorRect viewport)
    {
        float x = ((mouse.X - viewport.X) / viewport.Width) * 2 - 1;
        float y = 1 - ((mouse.Y - viewport.Y) / viewport.Height) * 2;
        float aspect = viewport.Width / viewport.Height;
        Matrix4 projection = _camera.Projection(aspect, 500);
        Matrix4 inverse = (_camera.View * projection).Inverted();
        Vector4 near4 = new Vector4(x, y, -1, 1) * inverse;
        Vector4 far4 = new Vector4(x, y, 1, 1) * inverse;
        Vector3 near = near4.Xyz / near4.W;
        Vector3 far = far4.Xyz / far4.W;
        Vector3 direction = (far - near).Normalized();
        string? result = null;
        float closest = float.MaxValue;
        foreach (ConvexBrush brush in scene.Brushes.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            Matrix4 transform = BrushTransform(brush.Transform);
            Matrix4 inverseTransform = transform.Inverted();
            Vector3 localOrigin = Vector3.TransformPosition(near, inverseTransform);
            Vector3 localDirection = Vector3.TransformNormal(direction, inverseTransform).Normalized();
            float enter = 0, exit = float.MaxValue;
            bool hit = true;
            foreach (ConvexBrushFace face in brush.Faces)
            {
                Vector3 normal = new(face.Normal[0], face.Normal[1], face.Normal[2]);
                float denominator = Vector3.Dot(normal, localDirection);
                float distance = face.Distance - Vector3.Dot(normal, localOrigin);
                if (MathF.Abs(denominator) < 0.00001f)
                {
                    if (distance < 0) { hit = false; break; }
                    continue;
                }
                float t = distance / denominator;
                if (denominator < 0) enter = Math.Max(enter, t);
                else exit = Math.Min(exit, t);
                if (enter > exit) { hit = false; break; }
            }
            if (hit && enter < closest) { closest = enter; result = brush.Id; }
        }
        foreach (MapEntityDefinition entity in scene.Entities.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            Vector3 center = ToVector(entity.Transform.Position);
            float along = Vector3.Dot(center - near, direction);
            if (along < 0 || along >= closest) continue;
            float radius = Math.Max(0.6f, ToVector(entity.Size).Length / 2);
            Vector3 nearest = near + direction * along;
            if ((center - nearest).LengthSquared <= radius * radius)
            {
                closest = along;
                result = entity.Id;
            }
        }
        return result;
    }

    private static Matrix4 BrushTransform(MapTransform value)
    {
        Vector3 rotation = new Vector3(value.Rotation[0], value.Rotation[1], value.Rotation[2])
            * (MathF.PI / 180);
        return Matrix4.CreateScale(value.Scale[0], value.Scale[1], value.Scale[2])
            * Matrix4.CreateRotationX(rotation.X) * Matrix4.CreateRotationY(rotation.Y)
            * Matrix4.CreateRotationZ(rotation.Z)
            * Matrix4.CreateTranslation(value.Position[0], value.Position[1], value.Position[2]);
    }

    private static Vector3 Hsv(float h, float s, float v)
    {
        float i = MathF.Floor(h * 6), f = h * 6 - i, p = v * (1 - s),
            q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        return ((int)i % 6) switch
        {
            0 => new(v, t, p), 1 => new(q, v, p), 2 => new(p, v, t),
            3 => new(p, q, v), 4 => new(t, p, v), _ => new(v, p, q)
        };
    }
}

public readonly record struct EditorRect(float X, float Y, float Width, float Height)
{
    public bool Contains(Vector2 point) => point.X >= X && point.X < X + Width
        && point.Y >= Y && point.Y < Y + Height;
}
