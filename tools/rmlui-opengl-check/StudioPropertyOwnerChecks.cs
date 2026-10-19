using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

/// <summary>Real document/history checks over synthetic objects; no display, files, or alternate writer.</summary>
internal static class StudioPropertyOwnerChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var definition = new MapDefinition { Name = "PROPERTY_CHECK", FormatVersion = 2 };
        definition.Materials.Add(new MapMaterial { Name = "First" });
        definition.Materials.Add(new MapMaterial { Name = "Second" });
        var mesh = new MapMesh { Label = "Mesh", Transform = new() { Position = new[] { 1f, 2, 3 } } };
        var prism = new MapPrism { Label = "Prism" };
        var spawn = new MapSpawn { Id = Guid.NewGuid(), Label = "Spawn" };
        var item = new MapItem { Id = Guid.NewGuid(), Label = "Pickup" };
        var pad = new MapJumpPad { Id = Guid.NewGuid(), Label = "Pad", Target = new[] { 0f, 4, 0 } };
        var link = new MapNavigationLink { From = new[] { 2f, 3, 4 }, To = new[] { 5f, 6, 7 } };
        var brush = new MapBrush { Id = Guid.NewGuid(), Min = new[] { -1f, -1, -1 }, Max = new[] { 1f, 1, 1 } };
        definition.Geometry.Add(mesh); definition.Geometry.Add(prism);
        definition.Spawns.Add(spawn); definition.Items.Add(item); definition.JumpPads.Add(pad);
        definition.NavigationLinks.Add(link); definition.Brushes.Add(brush);
        var document = new MapDocument(new MapProject(definition));
        var owner = document.Project;
        var history = document.History;
        var materials = document.Project.Definition.Materials;
        var material = materials[1];
        int changes = 0;
        document.History.Changed += _ => changes++;

        Dictionary<string, string> Patch(params (string Key, string Value)[] values)
            => values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
        void ExactEdit(Guid id, Dictionary<string, string> patch, string name)
        {
            string before = document.Project.Definition.Serialize();
            int priorChanges = changes;
            var priorState = document.CurrentStateId;
            MapObjectPropertySchema.Apply(document, id, patch);
            string after = document.Project.Definition.Serialize();
            var nextState = document.CurrentStateId;
            check(after != before && changes == priorChanges + 1 && nextState != priorState,
                name + " is one ordinary object-history command");
            history.Undo();
            check(document.Project.Definition.Serialize() == before && document.CurrentStateId == priorState,
                name + " undoes to the exact serialized state");
            history.Redo();
            check(document.Project.Definition.Serialize() == after && document.CurrentStateId == nextState,
                name + " redoes to the exact serialized state");
            check(ReferenceEquals(owner, document.Project) && ReferenceEquals(history, document.History)
                && ReferenceEquals(materials, document.Project.Definition.Materials) && ReferenceEquals(material, materials[1]),
                name + " preserves the retained document owner, history, and global materials");
        }
        void Reject(Guid id, Dictionary<string, string> patch, string name)
        {
            string before = document.Project.Definition.Serialize();
            var state = document.CurrentStateId;
            int priorChanges = changes;
            bool rejected = false;
            try { MapObjectPropertySchema.Apply(document, id, patch); }
            catch (FormatException) { rejected = true; }
            check(rejected && before == document.Project.Definition.Serialize()
                && state == document.CurrentStateId && changes == priorChanges,
                name + " rejects the complete patch atomically");
        }

        var fields = MapObjectPropertySchema.Read(document.Project.Definition, mesh.Id);
        check(fields.Count > 24 && fields.Any(f => f.Id == "rotation.z")
            && fields.Any(f => f.Id == "ignoreScan") && fields.Any(f => f.Id == "material" && f.Max == 1),
            "mesh schema exposes paged transform/surface/channel fields with live material bounds");
        check(fields is not MapObjectPropertyField[] && fields.First(f => f.Id == "terrain").Choices is not string[],
            "property field and choice collections are immutable detached views");

        ExactEdit(mesh.Id, Patch(("label", "Edited mesh"), ("position.x", "12.5"), ("size.y", "2"),
            ("rotation.z", "45"), ("material", "1"), ("shade", "0.5"), ("uvScale.x", "2"),
            ("uvOffset.y", "-0.25"), ("uvRotation", "90"), ("ignoreScan", "true"),
            ("slipperiness", "3")), "mesh property batch");
        check(fields.First(f => f.Id == "label").Value == "Mesh"
            && fields.First(f => f.Id == "position.x").Value == "1",
            "captured schema values remain unchanged after document edits");
        Reject(mesh.Id, Patch(("label", "Must not apply"), ("material", "2")), "global material upper bound");
        Reject(mesh.Id, Patch(("label", "Must not apply"), ("position.x", "NaN")), "nonfinite input");
        Reject(mesh.Id, Patch(("shade", "Infinity")), "infinite input");
        Reject(mesh.Id, Patch(("shade", "1,25")), "non-invariant decimal input");
        Reject(mesh.Id, Patch(("size.x", "0")), "zero scale");
        Reject(mesh.Id, Patch(("terrain", "255")), "numeric enum input");
        Reject(mesh.Id, Patch(("slipperiness", "4")), "mesh channel bounds");
        Reject(mesh.Id, Patch(("label", "Must not apply"), ("Transform.Position", "99")), "unknown property path");
        Reject(mesh.Id, Patch(("label", new string('x', 257))), "bounded text input");
        Reject(mesh.Id, Patch(("hidden", "1")), "invalid boolean input");

        int noOpChanges = changes;
        var noOpState = document.CurrentStateId;
        MapObjectPropertySchema.Apply(document, mesh.Id, MapObjectPropertySchema.Read(document.Project.Definition, mesh.Id)
            .ToDictionary(f => f.Id, f => f.Value, StringComparer.Ordinal));
        MapObjectPropertySchema.Apply(document, mesh.Id, Patch());
        check(changes == noOpChanges && document.CurrentStateId == noOpState,
            "unchanged complete page and empty patches create no history command or quaternion drift");

        ExactEdit(mesh.Id, Patch(("solid", "false"), ("collisionOnly", "true")), "collision proxy flag batch");
        check(((MapMesh)MapObjects.Find(document.Project.Definition, mesh.Id)!.Value).Solid,
            "enabling collision-only preserves the existing solid-channel intent");
        ExactEdit(mesh.Id, Patch(("locked", "true")), "explicit lock flag");
        float lockedX = MapObjects.Find(document.Project.Definition, mesh.Id)!.Position[0];
        ExactEdit(mesh.Id, Patch(("position.x", "100"), ("locked", "false")), "explicit unlock flag batch");
        check(MapObjects.Find(document.Project.Definition, mesh.Id)!.Position[0] == lockedX,
            "same-patch unlock retains existing position-before-lock inspector semantics");
        ExactEdit(prism.Id, Patch(("sides", "12")), "prism sides");
        Reject(prism.Id, Patch(("sides", "33")), "prism side bounds");
        ExactEdit(spawn.Id, Patch(("yaw", "135"), ("team", "3"), ("label", "Blue spawn")), "spawn properties");
        Reject(spawn.Id, Patch(("team", "4")), "spawn team bounds");
        ExactEdit(item.Id, Patch(("type", "HealthBig"), ("spawnInterval", "65535"), ("hasBase", "false")), "pickup properties");
        Reject(item.Id, Patch(("spawnInterval", "65536")), "pickup frame bounds");
        ExactEdit(pad.Id, Patch(("launchMode", "Vector and speed"), ("direction.x", "1"), ("speed", "18"),
            ("cooldownTime", "0"), ("controlLockTime", "60"), ("size.z", "2")), "jump-pad vector mode");
        var editedPad = (MapJumpPad)MapObjects.Find(document.Project.Definition, pad.Id)!.Value;
        check(editedPad.Target == null && editedPad.Vector!.SequenceEqual(new[] { 1f, 1, 0 }),
            "jump-pad launch mode applies exactly one authored launch channel");
        Reject(pad.Id, Patch(("label", "Must not apply"), ("direction.x", "0"), ("direction.y", "0"), ("direction.z", "0")),
            "zero jump-pad launch vector");
        Reject(pad.Id, Patch(("speed", "0")), "nonpositive vector launch speed");
        ExactEdit(pad.Id, Patch(("launchMode", "Target"), ("target.y", "9")), "jump-pad target mode");
        ExactEdit(link.Id, Patch(("position.y", "8"), ("destination.x", "11"), ("kind", "Jump"),
            ("fromNodeKind", "Vantage"), ("toNodeKind", "Hazard"), ("bidirectional", "true")), "navigation properties");
        Reject(link.Id, Patch(("kind", "77")), "navigation numeric enum");
        ExactEdit(brush.Id, Patch(("minimum.x", "-3"), ("maximum.y", "4"), ("material", "1")), "legacy brush properties");
        Reject(brush.Id, Patch(("minimum.z", "1")), "zero legacy brush dimension");
    }
}
