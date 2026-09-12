using MphRead;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;
using ProjectPrime.Editor.Commands;
using ProjectPrime.Editor.App;
using ProjectPrime.Editor.Documents;
using ProjectPrime.Editor.Viewport;

namespace ProjectPrime.MapPlatform.Tests;

public sealed class EditorViewportTests
{
    [Fact]
    public void HiDpiLayoutUsesOneLogicalAndPixelViewportContract()
    {
        EditorViewportLayout layout = EditorViewportLayout.Create(
            new Vector2i(1440, 900), new Vector2i(2880, 1800),
            new EditorRect(230, 34, 940, 698));

        Assert.Equal(new EditorRect(230, 34, 940, 698), layout.LogicalRect);
        Assert.Equal(new PixelRect(460, 68, 1880, 1396), layout.PixelRect);
        Assert.Equal(1880f / 1396, layout.AspectRatio, 5);

        var viewport = new EditorViewport();
        MapDocument document = MapDocument.New("community.viewport", "Viewport");
        RenderFrame frame = viewport.BuildFrame(document, new Vector2i(2880, 1800), layout);
        Assert.Equal(new Vector2i(1880, 1396), frame.SceneTargetSize);
        Assert.Equal(new RenderDestinationViewport(460, 68, 1880, 1396),
            frame.Composite.DestinationViewport);
    }

    [Fact]
    public void EditorOverlayUsesTheSharedFixedPresentationOrder()
    {
        var frame = new RenderFrame(capacity: 1, maximumCapacity: 1);
        EditorPresentationStages.BeginHudOverlay(frame);
        frame.AddOverlayCommand(new RenderOverlayCommand(RenderOverlayKind.FlatBox,
        [
            new(Vector3.Zero, Vector2.Zero, Vector4.One),
            new(Vector3.UnitX, Vector2.Zero, Vector4.One),
            new(Vector3.UnitY, Vector2.Zero, Vector4.One)
        ]));
        EditorPresentationStages.Complete(frame);

        SdlGpuPresentationOrder.Validate(frame.OverlayCommands);
        Assert.Equal(Enum.GetValues<RenderPresentationStage>(),
            frame.OverlayCommands.Where(command => command.Kind == RenderOverlayKind.StageMarker)
                .Select(command => command.Stage));
    }

    [Fact]
    public void SelectionAndOverlayChangesDoNotRebuildGeometry()
    {
        var viewport = new EditorViewport();
        MapDocument document = MapDocument.New("community.incremental", "Incremental");
        viewport.Synchronize(document);
        EditorViewportCacheStatistics initial = viewport.CacheStatistics;

        document.SelectedObjectId = "brush.floor";
        viewport.Synchronize(document);
        EditorViewportCacheStatistics selected = viewport.CacheStatistics;
        Assert.Equal(initial.GeometryRebuilds, selected.GeometryRebuilds);
        Assert.Equal(initial.SelectionRebuilds + 1, selected.SelectionRebuilds);
        Assert.Equal(initial.EntityRebuilds, selected.EntityRebuilds);

        document.Execute(new ModifyPropertyCommand(document, "Toggle collision", project =>
            project.Authoring!.Editor.ShowCollision = false, EditorChangeKind.Overlay));
        viewport.Synchronize(document);
        EditorViewportCacheStatistics overlay = viewport.CacheStatistics;
        Assert.Equal(selected.GeometryRebuilds, overlay.GeometryRebuilds);
        Assert.Equal(selected.CollisionRebuilds + 1, overlay.CollisionRebuilds);

        MapEntityDefinition entity = document.Project.Authoring!.Entities[0];
        MapTransform transform = new()
        {
            Position = [2, 3, 4],
            Rotation = (float[])entity.Transform.Rotation.Clone(),
            Scale = (float[])entity.Transform.Scale.Clone()
        };
        document.Execute(new TransformObjectCommand(document, entity.Id, transform));
        viewport.Synchronize(document);
        EditorViewportCacheStatistics moved = viewport.CacheStatistics;
        Assert.Equal(overlay.GeometryRebuilds, moved.GeometryRebuilds);
        Assert.Equal(overlay.EntityRebuilds + 1, moved.EntityRebuilds);
    }

    [Fact]
    public void EditorGeometryUsesVisibleExplicitColorsWithoutRuntimeLightBindings()
    {
        var viewport = new EditorViewport();
        MapDocument document = MapDocument.New("community.visible", "Visible");
        EditorViewportLayout layout = EditorViewportLayout.Create(
            new Vector2i(1440, 900), new Vector2i(1440, 900),
            new EditorRect(230, 34, 940, 698));

        RenderFrame frame = viewport.BuildFrame(document, new Vector2i(1440, 900), layout);

        DrawSubmission geometry = Assert.Single(frame.Submissions, item =>
            item.Primitive == RenderPrimitive.Mesh && !item.Wireframe);
        Assert.False(geometry.Lighting);
        CpuMesh mesh = Assert.Single(frame.MeshResources.Values,
            value => value.TriangleIndexCount > 0);
        Assert.All(mesh.Vertices, vertex => Assert.True(vertex.HasExplicitColor));
        Assert.Contains(mesh.Vertices, vertex => vertex.Color.X > 0
            || vertex.Color.Y > 0 || vertex.Color.Z > 0);
    }

    [Fact]
    public void DefaultCameraPlacesNewObjectsOnTheVisibleGroundPlane()
    {
        var camera = new EditorCamera();

        Vector3 point = camera.GroundPlacement();

        Assert.Equal(0, point.Y, 5);
        Assert.InRange(point.X, -8, 8);
        Assert.InRange(point.Z, -8, 8);
    }

    [Theory]
    [InlineData(1, 1, 1, 0)]
    [InlineData(10, 1, 1, 0)]
    [InlineData(1, 10, 0.5f, 0)]
    [InlineData(1, 10, 0.5f, 37)]
    public void BrushPickingUsesWorldDistanceUnderScaleAndRotation(
        float xScale, float yScale, float zScale, float yaw)
    {
        var camera = new EditorCamera();
        var scene = new MapAuthoringScene();
        ConvexBrush brush = ConvexBrushFactory.Box("brush.target", "material.default",
            new Vector3(2, 2, 2));
        Vector3 center = camera.Position + camera.Forward * 8;
        brush.Transform.Position = [center.X, center.Y, center.Z];
        brush.Transform.Rotation = [0, yaw, 0];
        brush.Transform.Scale = [xScale, yScale, zScale];
        scene.Brushes.Add(brush);
        EditorViewportLayout layout = EditorViewportLayout.Create(
            new Vector2i(1440, 900), new Vector2i(2160, 1350),
            new EditorRect(230, 34, 940, 698));
        Vector2 mouse = new(layout.LogicalRect.X + layout.LogicalRect.Width / 2,
            layout.LogicalRect.Y + layout.LogicalRect.Height / 2);

        string? selected = EditorPicking.Pick(scene, camera, mouse, layout, 500);

        Assert.Equal("brush.target", selected);
    }

    [Fact]
    public void NearestWorldSpaceBrushWins()
    {
        var camera = new EditorCamera();
        var scene = new MapAuthoringScene();
        scene.Brushes.Add(BrushAt("brush.far", camera, 14, [2, 1, 0.5f], 23));
        scene.Brushes.Add(BrushAt("brush.near", camera, 6, [1, 1, 1], 0));
        EditorViewportLayout layout = EditorViewportLayout.Create(
            new Vector2i(1440, 900), new Vector2i(1440, 900),
            new EditorRect(230, 34, 940, 698));
        Vector2 mouse = new(layout.LogicalRect.X + layout.LogicalRect.Width / 2,
            layout.LogicalRect.Y + layout.LogicalRect.Height / 2);

        Assert.Equal("brush.near", EditorPicking.Pick(scene, camera, mouse, layout, 500));
    }

    private static ConvexBrush BrushAt(string id, EditorCamera camera, float distance,
        float[] scale, float yaw)
    {
        ConvexBrush brush = ConvexBrushFactory.Box(id, "material.default", new Vector3(2, 2, 2));
        Vector3 center = camera.Position + camera.Forward * distance;
        brush.Transform.Position = [center.X, center.Y, center.Z];
        brush.Transform.Rotation = [0, yaw, 0];
        brush.Transform.Scale = scale;
        return brush;
    }
}
