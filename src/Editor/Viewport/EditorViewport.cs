using MphRead;
using MphRead.Mods;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;
using ProjectPrime.Editor.Documents;

namespace ProjectPrime.Editor.Viewport;

public sealed class EditorViewport
{
    private readonly EditorCamera _camera = new();
    private MapBuildScene? _scene;
    private CpuMesh? _geometryMesh;
    private object _geometryIdentity = new();
    private CpuMesh? _gridMesh;
    private object _gridIdentity = new();
    private CpuMesh? _collisionMesh;
    private object _collisionIdentity = new();
    private CpuMesh? _entityMesh;
    private object _entityIdentity = new();
    private CpuMesh? _selectionMesh;
    private object _selectionIdentity = new();
    private CpuMesh? _boundsMesh;
    private object _boundsIdentity = new();
    private EditorChangeState _changeState;
    private bool _initialized;
    private Vector3 _min = new(-1);
    private Vector3 _max = new(1);

    public EditorCamera Camera => _camera;
    public Vector4 ClearColor { get; set; } = new(0.035f, 0.045f, 0.065f, 1);
    public EditorViewportCacheStatistics CacheStatistics { get; private set; }

    public void Update(MapDocument document, RenderSurfaceInput input,
        EditorViewportLayout layout, float elapsedSeconds)
    {
        bool hovered = layout.LogicalRect.Contains(input.MousePosition);
        _camera.Update(input, elapsedSeconds, hovered);
        Synchronize(document);
        if (hovered && input.LeftPressed && document.Project.Authoring != null)
        {
            document.SelectedObjectId = EditorPicking.Pick(document.Project.Authoring,
                _camera, input.MousePosition, layout,
                Math.Max(500, document.Project.Environment?.FarClip
                    ?? document.Project.Map.FarClip));
            Synchronize(document);
        }
        if (input.Pressed(RenderSurfaceKey.F)) _camera.Frame(_min, _max);
    }

    public void Synchronize(MapDocument document)
    {
        EditorChangeState current = document.ChangeState;
        bool geometry = !_initialized || current.Geometry != _changeState.Geometry;
        bool entities = !_initialized || current.Entity != _changeState.Entity;
        bool overlays = !_initialized || current.Overlay != _changeState.Overlay;
        bool environment = !_initialized || current.Environment != _changeState.Environment;
        bool selection = !_initialized || current.Selection != _changeState.Selection;

        if (geometry)
        {
            RebuildGeometry(document);
            RebuildGrid();
            RebuildCollision(document);
            RebuildBounds(document);
        }
        else if (overlays)
        {
            RebuildCollision(document);
            RebuildBounds(document);
        }
        if (geometry || entities || overlays || environment) RebuildEntities(document);
        if (geometry || selection) RebuildSelection(document);

        _changeState = current;
        _initialized = true;
    }

    public RenderFrame BuildFrame(MapDocument document, Vector2i framebufferSize,
        EditorViewportLayout layout)
    {
        Synchronize(document);
        var frame = new RenderFrame(capacity: 12, maximumCapacity: 8192);
        Matrix4 view = _camera.View;
        Matrix4 inverse = view.Inverted();
        Matrix4 projection = _camera.Projection(layout.AspectRatio,
            Math.Max(500, document.Project.Environment?.FarClip ?? document.Project.Map.FarClip));
        var quality = new RenderQualitySnapshot(GraphicsPreset.Original,
            TextureFilteringPreset.Smooth, AnisotropyLevel.Off, MsaaLevel.Off,
            Bloom: false, DynamicVisualLights: false);
        Vector2i sceneSize = new(layout.PixelRect.Width, layout.PixelRect.Height);
        frame.CaptureState(view, inverse, inverse, projection, _camera.EyePosition,
            framebufferSize, sceneSize, ClearColor,
            new Vector3(0.3f, -1, 0.2f).Normalized(),
            Vector3.One, new Vector3(-0.3f, 1, -0.2f).Normalized(),
            new Vector3(0.3f), false, Vector4.Zero, 0, 0,
            new RenderFrameOptions(true, true, false, true, true, true, false,
                false, 4, 0, 0, false, false, quality));
        frame.CaptureComposite(new RenderCompositeState(framebufferSize, sceneSize,
            RenderCompositeFilter.Linear, ClearDestination: true,
            new RenderDestinationViewport(layout.PixelRect.X, layout.PixelRect.Y,
                layout.PixelRect.Width, layout.PixelRect.Height)));
        AddGeometry(frame, _geometryIdentity, _geometryMesh);
        AddLines(frame, _gridIdentity, _gridMesh);
        AddLines(frame, _collisionIdentity, _collisionMesh);
        AddLines(frame, _entityIdentity, _entityMesh);
        AddLines(frame, _boundsIdentity, _boundsMesh);
        AddLines(frame, _selectionIdentity, _selectionMesh);
        return frame;
    }

    private void RebuildGeometry(MapDocument document)
    {
        try
        {
            _scene = (document.Project.Authoring == null
                ? (IMapImporter)new LegacyMapImporter() : new NativeMapProjectImporter())
                .Import(document.Project, false);
            var vertices = new List<RenderVertex>();
            var triangles = new List<int>();
            var lines = new List<int>();
            _min = new Vector3(float.MaxValue);
            _max = new Vector3(float.MinValue);
            foreach (BuiltFace face in _scene.Faces)
            {
                int start = vertices.Count;
                Vector4 color = FaceColor(face);
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
            _geometryMesh = new CpuMesh(vertices.ToArray(), triangles.ToArray(), lines.ToArray());
        }
        catch
        {
            _scene = null;
            _geometryMesh = null;
            _min = new(-1);
            _max = new(1);
        }
        _geometryIdentity = new object();
        CacheStatistics = CacheStatistics with
            { GeometryRebuilds = CacheStatistics.GeometryRebuilds + 1 };
    }

    private void RebuildGrid()
    {
        var mesh = new LineMeshBuilder();
        Vector4 grid = new(0.18f, 0.23f, 0.28f, 1);
        const float gridHeight = 0.02f;
        int extent = Math.Clamp((int)MathF.Ceiling(MathF.Max(
            MathF.Max(MathF.Abs(_min.X), MathF.Abs(_max.X)),
            MathF.Max(MathF.Abs(_min.Z), MathF.Abs(_max.Z)))) + 2, 8, 64);
        for (int value = -extent; value <= extent; value++)
        {
            mesh.Line(new(-extent, gridHeight, value), new(extent, gridHeight, value),
                value == 0 ? new(0.5f, 0.2f, 0.2f, 1) : grid);
            mesh.Line(new(value, gridHeight, -extent), new(value, gridHeight, extent),
                value == 0 ? new(0.2f, 0.35f, 0.65f, 1) : grid);
        }
        _gridMesh = mesh.Build();
        _gridIdentity = new object();
        CacheStatistics = CacheStatistics with
            { GridRebuilds = CacheStatistics.GridRebuilds + 1 };
    }

    private void RebuildCollision(MapDocument document)
    {
        var mesh = new LineMeshBuilder();
        if (document.Project.Authoring?.Editor.ShowCollision == true && _scene != null)
        {
            Vector4 color = new(0.1f, 0.9f, 0.9f, 1);
            foreach (BuiltFace face in _scene.Solid)
            for (int index = 0; index < face.Points.Length; index++)
                mesh.Line(face.Points[index], face.Points[(index + 1) % face.Points.Length], color);
        }
        _collisionMesh = mesh.Build();
        _collisionIdentity = new object();
        CacheStatistics = CacheStatistics with
            { CollisionRebuilds = CacheStatistics.CollisionRebuilds + 1 };
    }

    private void RebuildEntities(MapDocument document)
    {
        var mesh = new LineMeshBuilder();
        MapAuthoringScene? authoring = document.Project.Authoring;
        if (authoring?.Editor.ShowEntities == true)
        {
            foreach (MapEntityDefinition entity in authoring.Entities
                .OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                Vector3 center = ToVector(entity.Transform.Position);
                Vector3 size = ToVector(entity.Size) * 0.5f;
                Vector4 color = EntityColor(entity.Kind);
                mesh.Box(center - size, center + size, color);
                float yaw = MathHelper.DegreesToRadians(entity.Transform.Rotation[1]);
                mesh.Line(center, center + new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw))
                    * MathF.Max(1, size.Length), color);
                if (entity.Kind == MapEntityKind.JumpPad)
                    AppendJumpTrajectory(mesh, document, entity, color);
            }
        }
        _entityMesh = mesh.Build();
        _entityIdentity = new object();
        CacheStatistics = CacheStatistics with
            { EntityRebuilds = CacheStatistics.EntityRebuilds + 1 };
    }

    private void RebuildBounds(MapDocument document)
    {
        var mesh = new LineMeshBuilder();
        if (document.Project.Authoring?.Editor.ShowWorldBounds == true)
            mesh.Box(_min, _max, new(0.9f, 0.35f, 0.85f, 1));
        _boundsMesh = mesh.Build();
        _boundsIdentity = new object();
        CacheStatistics = CacheStatistics with
            { BoundsRebuilds = CacheStatistics.BoundsRebuilds + 1 };
    }

    private void RebuildSelection(MapDocument document)
    {
        var mesh = new LineMeshBuilder();
        MapAuthoringScene? authoring = document.Project.Authoring;
        if (authoring != null && document.SelectedObjectId is { } selected)
        {
            MapTransform? transform = authoring.Brushes
                    .FirstOrDefault(value => value.Id == selected)?.Transform
                ?? authoring.Entities.FirstOrDefault(value => value.Id == selected)?.Transform;
            if (transform != null)
            {
                Vector3 origin = ToVector(transform.Position);
                mesh.Line(origin, origin + Vector3.UnitX * 2, new(1, 0.15f, 0.15f, 1));
                mesh.Line(origin, origin + Vector3.UnitY * 2, new(0.15f, 1, 0.25f, 1));
                mesh.Line(origin, origin + Vector3.UnitZ * 2, new(0.15f, 0.4f, 1, 1));
            }
        }
        _selectionMesh = mesh.Build();
        _selectionIdentity = new object();
        CacheStatistics = CacheStatistics with
            { SelectionRebuilds = CacheStatistics.SelectionRebuilds + 1 };
    }

    private static void AppendJumpTrajectory(LineMeshBuilder mesh, MapDocument document,
        MapEntityDefinition entity, Vector4 color)
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
                Vector3 point = start + velocity * frame
                    - Vector3.UnitY * (gravity * frame * frame / 2);
                mesh.Line(previous, point, color);
                previous = point;
                if (point.Y < (document.Project.Environment?.KillHeight
                    ?? document.Project.Map.KillHeight)) break;
            }
        }
        catch (ProgramException) { }
    }

    private static void AddGeometry(RenderFrame frame, object identity, CpuMesh? mesh)
    {
        if (mesh == null || mesh.VertexCount == 0) return;
        frame.CaptureMesh(identity, mesh);
        DrawSubmission draw = frame.Acquire();
        draw.GeometryIdentity = identity;
        draw.Primitive = RenderPrimitive.Mesh;
        draw.PolygonMode = PolygonMode.Modulate;
        draw.CullingMode = CullingMode.Back;
        draw.Diffuse = Vector3.One;
        draw.Ambient = new Vector3(0.35f);
        // Authoring geometry supplies explicit diagnostic colours rather than
        // room material/light bindings. Enabling legacy lighting here feeds
        // the shader an empty per-draw LightInfo and renders the mesh black.
        draw.Lighting = false;
        draw.Alpha = 1;
        frame.Add(draw);
    }

    private static void AddLines(RenderFrame frame, object identity, CpuMesh? mesh)
    {
        if (mesh == null || mesh.LineIndexCount == 0) return;
        frame.CaptureMesh(identity, mesh);
        DrawSubmission draw = frame.Acquire();
        draw.GeometryIdentity = identity;
        draw.Primitive = RenderPrimitive.Mesh;
        draw.PolygonMode = PolygonMode.Modulate;
        draw.CullingMode = CullingMode.Neither;
        draw.Diffuse = Vector3.One;
        draw.Ambient = Vector3.One;
        draw.Lighting = false;
        draw.Wireframe = true;
        draw.Alpha = 1;
        frame.Add(draw);
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

    private static Vector4 FaceColor(BuiltFace face)
    {
        float hue = (face.Material * 0.173f) % 1;
        Vector3 baseColor = Hsv(hue, 0.42f, 0.78f) * Math.Clamp(face.Shade, 0.3f, 1);
        return new Vector4(baseColor, 1);
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

public static class EditorPicking
{
    public static string? Pick(MapAuthoringScene scene, EditorCamera camera,
        Vector2 logicalMouse, EditorViewportLayout layout, float farClip)
    {
        (Vector3 origin, Vector3 direction) = CreateWorldRay(camera, logicalMouse, layout, farClip);
        string? result = null;
        float closest = float.MaxValue;
        foreach (ConvexBrush brush in scene.Brushes.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            Matrix4 transform = BrushTransform(brush.Transform);
            Matrix4 inverseTransform = transform.Inverted();
            Vector3 localOrigin = Vector3.TransformPosition(origin, inverseTransform);
            Vector4 localDirection4 = new Vector4(direction, 0) * inverseTransform;
            Vector3 localDirection = localDirection4.Xyz.Normalized();
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
            if (!hit) continue;
            Vector3 localHit = localOrigin + localDirection * enter;
            Vector3 worldHit = Vector3.TransformPosition(localHit, transform);
            float worldDistance = Vector3.Dot(worldHit - origin, direction);
            if (worldDistance >= 0 && worldDistance < closest)
            {
                closest = worldDistance;
                result = brush.Id;
            }
        }
        foreach (MapEntityDefinition entity in scene.Entities
            .OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            Vector3 center = ToVector(entity.Transform.Position);
            float along = Vector3.Dot(center - origin, direction);
            if (along < 0 || along >= closest) continue;
            float radius = Math.Max(0.6f, ToVector(entity.Size).Length / 2);
            Vector3 nearest = origin + direction * along;
            if ((center - nearest).LengthSquared <= radius * radius)
            {
                closest = along;
                result = entity.Id;
            }
        }
        return result;
    }

    public static (Vector3 Origin, Vector3 Direction) CreateWorldRay(EditorCamera camera,
        Vector2 logicalMouse, EditorViewportLayout layout, float farClip)
    {
        EditorRect viewport = layout.LogicalRect;
        float x = ((logicalMouse.X - viewport.X) / viewport.Width) * 2 - 1;
        float y = 1 - ((logicalMouse.Y - viewport.Y) / viewport.Height) * 2;
        Matrix4 projection = camera.Projection(layout.AspectRatio, farClip);
        Matrix4 inverse = (camera.View * projection).Inverted();
        Vector4 near4 = new Vector4(x, y, -1, 1) * inverse;
        Vector4 far4 = new Vector4(x, y, 1, 1) * inverse;
        Vector3 near = near4.Xyz / near4.W;
        Vector3 far = far4.Xyz / far4.W;
        return (near, (far - near).Normalized());
    }

    private static Matrix4 BrushTransform(MapTransform value)
    {
        Vector3 rotation = new(value.Rotation[0], value.Rotation[1], value.Rotation[2]);
        rotation *= MathF.PI / 180;
        return Matrix4.CreateScale(value.Scale[0], value.Scale[1], value.Scale[2])
            * Matrix4.CreateRotationX(rotation.X) * Matrix4.CreateRotationY(rotation.Y)
            * Matrix4.CreateRotationZ(rotation.Z)
            * Matrix4.CreateTranslation(value.Position[0], value.Position[1], value.Position[2]);
    }

    private static Vector3 ToVector(float[] value) => new(value[0], value[1], value[2]);
}

public readonly record struct EditorRect(float X, float Y, float Width, float Height)
{
    public bool Contains(Vector2 point) => point.X >= X && point.X < X + Width
        && point.Y >= Y && point.Y < Y + Height;
}

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public readonly record struct EditorViewportLayout(
    EditorRect LogicalRect,
    PixelRect PixelRect,
    float AspectRatio)
{
    public static EditorViewportLayout Create(Vector2i logicalSize, Vector2i framebufferSize,
        EditorRect logicalRect)
    {
        if (logicalSize.X <= 0 || logicalSize.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(logicalSize));
        if (framebufferSize.X <= 0 || framebufferSize.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(framebufferSize));
        float scaleX = framebufferSize.X / (float)logicalSize.X;
        float scaleY = framebufferSize.Y / (float)logicalSize.Y;
        int left = Math.Clamp((int)MathF.Floor(logicalRect.X * scaleX), 0, framebufferSize.X - 1);
        int top = Math.Clamp((int)MathF.Floor(logicalRect.Y * scaleY), 0, framebufferSize.Y - 1);
        int right = Math.Clamp((int)MathF.Ceiling((logicalRect.X + logicalRect.Width) * scaleX),
            left + 1, framebufferSize.X);
        int bottom = Math.Clamp((int)MathF.Ceiling((logicalRect.Y + logicalRect.Height) * scaleY),
            top + 1, framebufferSize.Y);
        var pixels = new PixelRect(left, top, right - left, bottom - top);
        return new(logicalRect, pixels, pixels.Width / (float)pixels.Height);
    }
}

public readonly record struct EditorViewportCacheStatistics(
    int GeometryRebuilds,
    int GridRebuilds,
    int CollisionRebuilds,
    int EntityRebuilds,
    int BoundsRebuilds,
    int SelectionRebuilds);

internal sealed class LineMeshBuilder
{
    private readonly List<RenderVertex> _vertices = [];
    private readonly List<int> _lines = [];

    public void Line(Vector3 first, Vector3 second, Vector4 color)
    {
        int start = _vertices.Count;
        _vertices.Add(new(first, color, Vector3.UnitY, Vector2.Zero,
            flags: RenderVertexFlags.ExplicitColor));
        _vertices.Add(new(second, color, Vector3.UnitY, Vector2.Zero,
            flags: RenderVertexFlags.ExplicitColor));
        _lines.Add(start);
        _lines.Add(start + 1);
    }

    public void Box(Vector3 min, Vector3 max, Vector4 color)
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
            Line(points[edges[index]], points[edges[index + 1]], color);
    }

    public CpuMesh Build() => new(_vertices.ToArray(), [], _lines.ToArray());
}
