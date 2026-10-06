using System;
using System.Collections.Generic;
using System.Numerics;
using MphRead.Mods.MapEditor;

internal static class EditorMeshRetentionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var first = new MapViewportMesh(Guid.NewGuid(), Array.Empty<MapViewportFace>());
        var second = new MapViewportMesh(Guid.NewGuid(), Array.Empty<MapViewportFace>());
        var resident = new[] { first, second };
        var resources = new MapViewportMeshResources<int>();
        int uploads = 0;
        var released = new HashSet<int>();
        int Upload(MapViewportMesh _) => ++uploads;
        void Release(int id) => check(released.Add(id), "editor mesh resource released once");
        MapRenderFrame Frame(IReadOnlyList<MapViewportMesh> visible, IReadOnlyList<MapViewportMesh> all)
            => new(new(800, 600), new(Vector3.UnitZ, Vector3.Zero, true), visible,
                new HashSet<Guid>(), new Dictionary<Guid, Matrix4x4>(), false, false) { ResidentMeshes = all };

        resources.Synchronize(Frame(new[] { first }, resident), Upload, Release);
        int firstResource = resources[first.ObjectId];
        check(uploads == 1 && resources.Count == 1, "editor uploads only visible meshes lazily");
        resources.Synchronize(Frame(Array.Empty<MapViewportMesh>(), resident), Upload, Release);
        check(resources.Count == 1 && released.Count == 0, "culled document mesh retains its resource");
        resources.Synchronize(Frame(new[] { first }, resident), Upload, Release);
        check(uploads == 1 && resources[first.ObjectId] == firstResource,
            "camera return preserves upload count and resource identity");
        resources.Synchronize(Frame(new[] { second }, resident), Upload, Release);
        check(uploads == 2 && resources.Count == 2 && released.Count == 0,
            "camera movement retains previously visible chunks");

        var replacement = first with { Faces = new[] { new MapViewportFace(first.ObjectId,
            new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, 1, 0, true) } };
        var changed = new[] { replacement, second };
        resources.Synchronize(Frame(new[] { second }, changed), Upload, Release);
        check(resources.Count == 1 && released.Contains(firstResource) && uploads == 2,
            "hidden geometry replacement releases stale resource without uploading");
        resources.Synchronize(Frame(new[] { replacement }, changed), Upload, Release);
        check(resources[first.ObjectId] != firstResource && uploads == 3,
            "changed geometry uploads when it becomes visible");
        int replacementResource = resources[first.ObjectId];
        resources.Synchronize(Frame(Array.Empty<MapViewportMesh>(), new[] { second }), Upload, Release);
        check(resources.Count == 1 && released.Contains(replacementResource),
            "document deletion releases a culled mesh");

        resources.Clear(Release);
        resources.Clear(Release);
        check(resources.Count == 0 && released.Count == uploads, "editor teardown releases every retained resource once");

        // Synthetic/editor callers that never provide a separate resident set
        // retain the existing whole-frame contract, including record copies.
        var defaultFrame = new MapRenderFrame(new(800, 600), new(Vector3.UnitZ, Vector3.Zero, true),
            new[] { first }, new HashSet<Guid>(), new Dictionary<Guid, Matrix4x4>(), false, false);
        var copiedFrame = defaultFrame with { Meshes = new[] { second } };
        check(ReferenceEquals(copiedFrame.ResidentMeshes, copiedFrame.Meshes),
            "default resident set follows meshes when a render frame is copied");
    }
}
