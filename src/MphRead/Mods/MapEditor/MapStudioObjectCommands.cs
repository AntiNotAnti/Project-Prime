using System;
using System.Linq;
using System.Numerics;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public enum MapStudioObjectKind { Box, Wedge, Prism, Convex, Mesh, Spawn, Pickup, JumpPad, NavigationLink }

/// <summary>Object commands shared by the retained Studio owner and its presentations.</summary>
public static class MapStudioObjectCommands
{
    public static Guid Create(MapDocument document, MapStudioObjectKind kind, Vector3 placement)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!float.IsFinite(placement.X) || !float.IsFinite(placement.Y) || !float.IsFinite(placement.Z))
            throw new ArgumentOutOfRangeException(nameof(placement), "Object placement must be finite.");
        Vector3 place = placement;
        Guid created = Guid.Empty;
        string label = kind switch { MapStudioObjectKind.JumpPad => "Jump pad", MapStudioObjectKind.NavigationLink => "Navigation link", _ => kind.ToString() };
        document.EditObjects("Create " + label, Array.Empty<Guid>(), d =>
        {
            switch (kind)
            {
                case MapStudioObjectKind.Box:
                    d.Geometry.Add(new MapBox { Label = "Box", Transform = new() { Position = new[] { place.X, place.Y + 1, place.Z }, Scale = new[] { 4f, 2, 4 } } }); break;
                case MapStudioObjectKind.Wedge:
                    d.Geometry.Add(new MapWedge { Label = "Ramp", Transform = new() { Position = new[] { place.X, place.Y + 1, place.Z }, Scale = new[] { 4f, 2, 6 } } }); break;
                case MapStudioObjectKind.Prism:
                    d.Geometry.Add(new MapPrism { Label = "Prism", Transform = new() { Position = new[] { place.X, place.Y + 1, place.Z }, Scale = new[] { 3f, 2, 3 } } }); break;
                case MapStudioObjectKind.Convex:
                    d.Geometry.Add(new MapConvexBrush { Label = "Convex brush", Transform = new() { Position = new[] { place.X, place.Y, place.Z } },
                        Vertices = new() { new[] { -1f, 0, -1 }, new[] { 1f, 0, -1 }, new[] { 0f, 2, 0 }, new[] { 0f, 0, 1 } },
                        Faces = new() { new[] { 0, 1, 2 }, new[] { 0, 1, 3 }, new[] { 0, 2, 3 }, new[] { 1, 2, 3 } } }); break;
                case MapStudioObjectKind.Mesh:
                    d.Geometry.Add(new MapMesh { Label = "Editable mesh", Transform = new() { Position = new[] { place.X, place.Y, place.Z } },
                        Vertices = new() { new[] { -2f, 0, -2 }, new[] { 2f, 0, -2 }, new[] { 2f, 0, 2 }, new[] { -2f, 0, 2 } },
                        Faces = new() { new[] { 0, 1, 2, 3 } }, FaceMaterials = new() { 0 }, Solid = false }); break;
                case MapStudioObjectKind.Spawn:
                    d.Spawns.Add(new MapSpawn { Id = Guid.NewGuid(), Position = new[] { place.X, place.Y + .1f, place.Z } }); break;
                case MapStudioObjectKind.Pickup:
                    d.Items.Add(new MapItem { Id = Guid.NewGuid(), Type = "HealthMedium", Position = new[] { place.X, place.Y + .1f, place.Z } }); break;
                case MapStudioObjectKind.JumpPad:
                    d.JumpPads.Add(new MapJumpPad { Id = Guid.NewGuid(), Position = new[] { place.X, place.Y + .1f, place.Z }, Target = new[] { place.X + 8, place.Y + 2, place.Z } }); break;
                case MapStudioObjectKind.NavigationLink:
                    d.NavigationLinks.Add(new MapNavigationLink { From = new[] { place.X, place.Y + .1f, place.Z }, To = new[] { place.X + 6, place.Y + .1f, place.Z } }); break;
            }
            var item = MapObjects.All(d).Single();
            var existing = MapObjects.All(document.Project.Definition).Select(o => o.Id).ToHashSet();
            created = item.Id;
            while (created == Guid.Empty || existing.Contains(created)) created = Guid.NewGuid();
            item.SetId(created);
        });
        Select(document, new[] { created });
        return created;
    }

    public static Guid[] DuplicateSelection(MapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var selected = document.Selection.ToHashSet();
        var existing = MapObjects.All(document.Project.Definition).Select(o => o.Id).ToHashSet();
        if (!existing.Overlaps(selected)) return Array.Empty<Guid>();
        document.EditObjects("Duplicate", selected, d =>
        {
            var originals = MapObjects.All(d).Select(o => o.Value).ToHashSet();
            MapObjects.Duplicate(d, selected);
            var used = existing.ToHashSet();
            foreach (var copy in MapObjects.All(d).Where(o => !originals.Contains(o.Value)))
            {
                Guid id = copy.Id;
                while (id == Guid.Empty || !used.Add(id)) id = Guid.NewGuid();
                copy.SetId(id);
            }
        });
        var added = MapObjects.All(document.Project.Definition).Where(o => !existing.Contains(o.Id)).Select(o => o.Id).ToArray();
        if (added.Length > 0) Select(document, added);
        return added;
    }

    public static bool DeleteSelection(MapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var selected = document.Selection.ToHashSet();
        var before = document.CurrentStateId;
        document.EditObjects("Delete", selected, d => MapObjects.Delete(d, selected));
        if (before == document.CurrentStateId) return false;
        document.SelectionChanged();
        return true;
    }

    private static void Select(MapDocument document, Guid[] ids)
    {
        document.Selection.Clear();
        foreach (Guid id in ids) document.Selection.Add(id);
        document.ActiveObjectId = ids.FirstOrDefault();
        document.SelectionChanged();
    }
}
