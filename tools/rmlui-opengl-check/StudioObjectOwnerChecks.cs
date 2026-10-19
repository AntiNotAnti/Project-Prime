using System.Numerics;
using System.Text.Json;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

/// <summary>Pure production object-command checks; synthetic definitions, no files, UI or GL.</summary>
internal static class StudioObjectOwnerChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Vector3 placement = new(11, 3, -7);
        foreach (MapStudioObjectKind kind in Enum.GetValues<MapStudioObjectKind>())
        {
            var document = Fixture();
            var definition = document.Project.Definition;
            var originalIds = MapObjects.All(definition).Select(o => o.Id).ToArray();
            var untouched = MapObjects.All(definition).Select(o => o.Value).ToArray();
            string before = definition.Serialize();
            var initialState = document.CurrentStateId;
            int commands = 0, selectionNotifications = 0;
            document.History.Changed += _ => commands++;
            document.Invalidated += change => { if (change.Domains == MapChangeDomain.Selection) selectionNotifications++; };
            Guid created = MapStudioObjectCommands.Create(document, kind, placement);
            var value = MapObjects.Find(definition, created)!;
            check(created != Guid.Empty && !originalIds.Contains(created)
                && MapObjects.All(definition).Select(o => o.Id).Distinct().Count() == originalIds.Length + 1,
                $"{kind} creates exactly one fresh unique object ID");
            check(Defaults(kind, value.Value, placement), $"{kind} preserves retained Studio type, label, placement and defaults");
            check(document.Selection.SetEquals(new[] { created }) && document.ActiveObjectId == created && selectionNotifications == 1,
                $"{kind} selects and activates the new object with one selection notification");
            check(commands == 1 && document.History.CommandCount == 1 && document.CurrentStateId != initialState,
                $"{kind} creates one history command");
            check(untouched.All(o => MapObjects.All(definition).Any(item => ReferenceEquals(item.Value, o)))
                && originalIds.SequenceEqual(MapObjects.All(definition).Where(o => o.Id != created).Select(o => o.Id)),
                $"{kind} preserves unrelated object references and typed collection order");
            string after = definition.Serialize();
            var createdState = document.CurrentStateId;
            document.History.Undo();
            check(definition.Serialize() == before && document.CurrentStateId == initialState && document.Selection.Count == 0,
                $"{kind} undo exactly restores all original collections and state");
            document.History.Redo();
            check(definition.Serialize() == after && document.CurrentStateId == createdState
                && MapObjects.Find(definition, created)?.Id == created,
                $"{kind} redo exactly restores collections and the same object ID");
        }

        var duplicate = Fixture();
        var source = duplicate.Project.Definition;
        var originals = MapObjects.All(source).ToArray();
        var selected = new[] { source.Geometry[1].Id, source.Brushes[0].Id, source.Spawns[1].Id,
            source.Items[0].Id, source.JumpPads[1].Id, source.NavigationLinks[0].Id };
        foreach (Guid id in selected.Reverse()) duplicate.Selection.Add(id);
        duplicate.ActiveObjectId = selected[0];
        string original = source.Serialize();
        Guid[] added = MapStudioObjectCommands.DuplicateSelection(duplicate);
        check(added.Length == selected.Length && added.Distinct().Count() == added.Length
            && !added.Intersect(originals.Select(o => o.Id)).Any() && added.All(id => id != Guid.Empty),
            "duplicate creates fresh unique IDs for exactly the selected mixed object types");
        var sourceOrder = originals.Where(o => selected.Contains(o.Id)).ToArray();
        check(added.Select(id => MapObjects.Find(source, id)!.Value.GetType()).SequenceEqual(sourceOrder.Select(o => o.Value.GetType()))
            && added.Select(id => MapObjects.Find(source, id)!.Position).Zip(sourceOrder).All(pair =>
                pair.First.SequenceEqual(new[] { pair.Second.Position[0] + 1, pair.Second.Position[1], pair.Second.Position[2] + 1 })),
            "duplicate preserves typed source order and the existing plus-one X/Z placement");
        var sourceLink = sourceOrder.Single(o => o.Value is MapNavigationLink).Value as MapNavigationLink;
        var addedLink = (MapNavigationLink)MapObjects.Find(source, added[^1])!.Value;
        check(addedLink.To.SequenceEqual(new[] { sourceLink!.To[0] + 1, sourceLink.To[1], sourceLink.To[2] + 1 }),
            "duplicate offsets both navigation endpoints");
        check(originals.All(o => ReferenceEquals(MapObjects.Find(source, o.Id)!.Value, o.Value))
            && duplicate.Selection.SetEquals(added) && duplicate.ActiveObjectId == added[0] && duplicate.History.CommandCount == 1,
            "duplicate leaves every original object intact and selects copies in one history command");
        string duplicated = source.Serialize();
        duplicate.History.Undo();
        check(source.Serialize() == original, "duplicate undo exactly restores all typed collection order");
        duplicate.History.Redo();
        check(source.Serialize() == duplicated, "duplicate redo exactly restores copy IDs and collection order");

        var deletion = Fixture();
        var deleting = deletion.Project.Definition;
        deleting.Geometry[1].Locked = true;
        var locked = deleting.Geometry[1];
        deletion.Selection.Add(locked.Id); deletion.ActiveObjectId = locked.Id;
        string lockedBefore = deleting.Serialize();
        check(!MapStudioObjectCommands.DeleteSelection(deletion) && deleting.Serialize() == lockedBefore
            && deletion.History.CommandCount == 0 && deletion.Selection.SetEquals(new[] { locked.Id }) && deletion.ActiveObjectId == locked.Id,
            "locked-only delete is vetoed without history, selection or partial mutation");
        var lockedDuplicate = MapStudioObjectCommands.DuplicateSelection(deletion);
        check(lockedDuplicate.Length == 1 && ((MapGeometry)MapObjects.Find(deleting, lockedDuplicate[0])!.Value).Locked
            && MapObjects.Find(deleting, lockedDuplicate[0])!.Position.SequenceEqual(locked.Transform.Position),
            "locked duplicate preserves the existing locked-object movement veto");
        deletion.History.Undo();
        deletion.Selection.Clear();
        var deletedIds = new[] { deleting.Geometry[0].Id, deleting.Brushes[1].Id, deleting.Spawns[0].Id,
            deleting.Items[1].Id, deleting.JumpPads[0].Id, deleting.NavigationLinks[1].Id };
        foreach (Guid id in deletedIds.Append(locked.Id)) deletion.Selection.Add(id);
        deletion.ActiveObjectId = deletedIds[0];
        string deleteBefore = deleting.Serialize();
        int deleteCommands = 0;
        deletion.History.Changed += _ => deleteCommands++;
        check(MapStudioObjectCommands.DeleteSelection(deletion) && deleteCommands == 1
            && deletedIds.All(id => MapObjects.Find(deleting, id) == null) && ReferenceEquals(MapObjects.Find(deleting, locked.Id)!.Value, locked)
            && deletion.Selection.SetEquals(new[] { locked.Id }) && deletion.ActiveObjectId == locked.Id,
            "mixed delete removes selected unlocked types and retains locked selection in one command");
        string deleteAfter = deleting.Serialize();
        deletion.History.Undo();
        check(deleting.Serialize() == deleteBefore, "delete undo restores exact typed collection positions");
        deletion.History.Redo();
        check(deleting.Serialize() == deleteAfter, "delete redo exactly restores the deletion result");

        var empty = Fixture();
        string emptyBefore = empty.Project.Definition.Serialize();
        check(MapStudioObjectCommands.DuplicateSelection(empty).Length == 0 && !MapStudioObjectCommands.DeleteSelection(empty),
            "empty selection duplicate and delete report no operation");
        Guid missing = Guid.NewGuid(); empty.Selection.Add(missing); empty.ActiveObjectId = missing;
        check(MapStudioObjectCommands.DuplicateSelection(empty).Length == 0 && !MapStudioObjectCommands.DeleteSelection(empty)
            && empty.Project.Definition.Serialize() == emptyBefore && empty.History.CommandCount == 0
            && empty.Selection.SetEquals(new[] { missing }) && empty.ActiveObjectId == missing,
            "nonexistent selected IDs leave object data, selection and history unchanged");
        foreach (MapStudioObjectKind kind in Enum.GetValues<MapStudioObjectKind>())
        foreach (Vector3 invalid in new[] { new Vector3(float.NaN, 0, 0), new Vector3(0, float.PositiveInfinity, 0), new Vector3(0, 0, float.NegativeInfinity) })
        {
            bool rejected = false;
            try { MapStudioObjectCommands.Create(empty, kind, invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected && empty.Project.Definition.Serialize() == emptyBefore && empty.History.CommandCount == 0
                && empty.Selection.SetEquals(new[] { missing }) && empty.ActiveObjectId == missing,
                $"{kind} rejects nonfinite placement before any partial mutation");
        }
        bool invalidKind = false;
        try { MapStudioObjectCommands.Create(empty, (MapStudioObjectKind)999, Vector3.Zero); }
        catch (ArgumentOutOfRangeException) { invalidKind = true; }
        check(invalidKind && empty.Project.Definition.Serialize() == emptyBefore && empty.History.CommandCount == 0
            && empty.Selection.SetEquals(new[] { missing }) && empty.ActiveObjectId == missing,
            "unknown object kind is rejected without partial mutation");
    }

    private static bool Defaults(MapStudioObjectKind kind, object actual, Vector3 place)
    {
        float[] position = { place.X, place.Y, place.Z };
        object expected = kind switch
        {
            MapStudioObjectKind.Box => new MapBox { Label = "Box", Transform = new() { Position = new[] { place.X, place.Y + 1, place.Z }, Scale = new[] { 4f, 2, 4 } } },
            MapStudioObjectKind.Wedge => new MapWedge { Label = "Ramp", Transform = new() { Position = new[] { place.X, place.Y + 1, place.Z }, Scale = new[] { 4f, 2, 6 } } },
            MapStudioObjectKind.Prism => new MapPrism { Label = "Prism", Transform = new() { Position = new[] { place.X, place.Y + 1, place.Z }, Scale = new[] { 3f, 2, 3 } } },
            MapStudioObjectKind.Convex => new MapConvexBrush { Label = "Convex brush", Transform = new() { Position = position },
                Vertices = new() { new[] { -1f, 0, -1 }, new[] { 1f, 0, -1 }, new[] { 0f, 2, 0 }, new[] { 0f, 0, 1 } },
                Faces = new() { new[] { 0, 1, 2 }, new[] { 0, 1, 3 }, new[] { 0, 2, 3 }, new[] { 1, 2, 3 } } },
            MapStudioObjectKind.Mesh => new MapMesh { Label = "Editable mesh", Transform = new() { Position = position },
                Vertices = new() { new[] { -2f, 0, -2 }, new[] { 2f, 0, -2 }, new[] { 2f, 0, 2 }, new[] { -2f, 0, 2 } },
                Faces = new() { new[] { 0, 1, 2, 3 } }, FaceMaterials = new() { 0 }, Solid = false },
            MapStudioObjectKind.Spawn => new MapSpawn { Position = new[] { place.X, place.Y + .1f, place.Z } },
            MapStudioObjectKind.Pickup => new MapItem { Type = "HealthMedium", Position = new[] { place.X, place.Y + .1f, place.Z } },
            MapStudioObjectKind.JumpPad => new MapJumpPad { Position = new[] { place.X, place.Y + .1f, place.Z }, Target = new[] { place.X + 8, place.Y + 2, place.Z } },
            MapStudioObjectKind.NavigationLink => new MapNavigationLink { From = new[] { place.X, place.Y + .1f, place.Z }, To = new[] { place.X + 6, place.Y + .1f, place.Z } },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (actual.GetType() != expected.GetType()) return false;
        // Only identity varies; compare the full concrete value, including inherited defaults.
        var id = actual.GetType().GetProperty("Id")!;
        id.SetValue(expected, id.GetValue(actual));
        return JsonSerializer.Serialize(actual, actual.GetType()) == JsonSerializer.Serialize(expected, expected.GetType());
    }

    private static MapDocument Fixture()
    {
        // Deliberately reverse IDs so serialization equality also detects collection sorting.
        Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");
        var definition = new MapDefinition { FormatVersion = 2, Name = "STUDIO_OBJECT_FIXTURE", MapId = Id(100),
            Geometry = new() { new MapBox { Id = Id(9), Label = "unrelated box" }, new MapWedge { Id = Id(8), Transform = new() { Position = new[] { 3f, 4, 5 } } }, new MapPrism { Id = Id(7) } },
            Brushes = new() { new() { Id = Id(19), Min = new[] { 1f, 2, 3 }, Max = new[] { 5f, 6, 7 } }, new() { Id = Id(18) } },
            Spawns = new() { new() { Id = Id(29) }, new() { Id = Id(28), Position = new[] { 2f, 3, 4 } } },
            Items = new() { new() { Id = Id(39), Position = new[] { 4f, 5, 6 } }, new() { Id = Id(38) } },
            JumpPads = new() { new() { Id = Id(49) }, new() { Id = Id(48), Position = new[] { 6f, 7, 8 }, Target = new[] { 9f, 10, 11 } } },
            NavigationLinks = new() { new() { Id = Id(59), From = new[] { 8f, 9, 10 }, To = new[] { 11f, 12, 13 } }, new() { Id = Id(58) } } };
        return new(new MapProject(definition));
    }
}
