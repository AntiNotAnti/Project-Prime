using System.Text;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

internal static class AnimatedPrefabChecks
{
    internal static void Run(string testRoot, Action<bool, string> check)
    {
        string root = Path.Combine(testRoot, "animated-prefab"); Directory.CreateDirectory(root);
        string tile = Path.Combine(root, "tile.tex");
        using (var writer = new BinaryWriter(File.Create(tile)))
        {
            writer.Write(Encoding.ASCII.GetBytes("FPTX")); writer.Write((ushort)1); writer.Write((ushort)1);
            writer.Write((ushort)0); writer.Write((ushort)8); writer.Write((ushort)8); writer.Write((ushort)1);
            writer.Write((ushort)0); writer.Write((ushort)32767); writer.Write(new byte[64]);
        }
        var source = new MapDefinition { Name = "ANIMATED_PREFAB", FormatVersion = 2,
            MapId = Guid.NewGuid(), BaseDirectory = root };
        source.Materials.Add(new() { Id = Guid.NewGuid(), Name = new string('A', 31), Texture = "tile.tex",
            Animation = new() { UvScroll = [.2f, 0], LoopFrames = 300, FlipbookFrames = ["tile.tex"], FlipbookHoldFrames = 1 } });
        source.Assets.Add(new() { Path = "tile.tex", Kind = "texture" });
        var box = new MapBox { Transform = new() { Position = [0f, -1, 0], Scale = [8f, 1, 8] } };
        var spawn = new MapSpawn { Id = Guid.NewGuid(), Position = [0f, 2, 0] };
        source.Geometry.Add(box); source.Spawns.Add(spawn);
        var selected = new HashSet<Guid> { box.Id, spawn.Id };
        string prefab = Path.Combine(root, "library", "animated.json"); MapPrefabService.Save(source, selected, prefab);
        var document = new MapDocument(new(source));
        Guid ownerMaterialId = document.Project.Definition.Materials[0].Id;
        string ownerName = document.Project.Definition.Materials[0].Name;
        var first = Insert(document, prefab, root, new() { Position = [12f, 0, 0] });
        var second = Insert(document, prefab, root, new() { Position = [24f, 0, 0] });
        Guid MaterialId(Guid instanceId) => document.Project.Definition.PrefabInstances.Single(i => i.Id == instanceId).Materials.Single().MaterialId;
        Guid firstId = MaterialId(first.InstanceId), secondId = MaterialId(second.InstanceId);
        MapMaterial Material(Guid id) => document.Project.Definition.Materials.Single(m => m.Id == id);
        string firstName = Material(firstId).Name, secondName = Material(secondId).Name;
        check(firstId != ownerMaterialId && secondId != firstId && secondId != ownerMaterialId,
            "animated prefab instances retain independent material identities");
        check(new[] { ownerName, firstName, secondName }.Distinct(StringComparer.Ordinal).Count() == 3,
            "source-owner and repeated animated prefab insertions allocate distinct native names");
        check(firstName.Length <= 31 && secondName.Length <= 31
            && firstName.All(c => c <= byte.MaxValue) && secondName.All(c => c <= byte.MaxValue),
            "animated prefab names respect the native single-byte length limit");
        check(Material(ownerMaterialId).Name == ownerName, "prefab insertion preserves the authored owner material name");
        Compile(document.Project.Definition, check, "repeated animated prefab insertions");
        string inserted = document.Project.Definition.Serialize();
        document.History.Undo();
        check(document.Project.Definition.PrefabInstances.Count == 1 && Material(firstId).Name == firstName,
            "animated prefab insertion undo preserves the first material binding");
        document.History.Redo(); check(document.Project.Definition.Serialize() == inserted,
            "animated prefab insertion redo restores byte-identical names and binding identities");

        source.Materials[0].Name = "Changed source label";
        source.Materials[0].Animation!.UvScroll = [.4f, 0];
        MapPrefabService.Save(source, selected, prefab);
        string beforeUpdate = document.Project.Definition.Serialize();
        Update(document, first.InstanceId, root);
        check(MaterialId(first.InstanceId) == firstId && Material(firstId).Name == firstName
            && Material(firstId).Animation!.UvScroll[0] == .4f,
            "source update preserves the allocated native name and identity while adopting animation changes");
        check(Material(secondId).Name == secondName && Material(secondId).Animation!.UvScroll[0] == .2f,
            "updating one prefab leaves another instance and owner material independent");
        string updated = document.Project.Definition.Serialize();
        document.History.Undo(); check(document.Project.Definition.Serialize() == beforeUpdate,
            "animated source update undo restores exact prior authoring state");
        document.History.Redo(); check(document.Project.Definition.Serialize() == updated,
            "animated source update redo restores exact accepted authoring state");
        Compile(document.Project.Definition, check, "updated animated prefab");

        // An author may use a generated-looking name later. The prefab must move
        // its own track, preserving that authored material and every other instance.
        Guid authoredId = Guid.NewGuid();
        document.Edit("Add authored name collision", d => d.Materials.Add(new() { Id = authoredId, Name = firstName, Texture = "tile.tex" }));
        source.Materials[0].TexScale = 32; MapPrefabService.Save(source, selected, prefab);
        Update(document, first.InstanceId, root);
        string collisionSafeName = Material(firstId).Name;
        check(collisionSafeName != firstName && Material(authoredId).Name == firstName
            && Material(secondId).Name == secondName,
            "source update avoids a user-authored generated-looking name without renaming unrelated materials");
        source.Materials[0].TexScale = 48; MapPrefabService.Save(source, selected, prefab);
        Update(document, first.InstanceId, root);
        check(Material(firstId).Name == collisionSafeName && Material(firstId).TexScale == 48,
            "collision-resolved native name remains stable on later revisions");
        document.Edit("Local animated material override", d =>
        { var material = d.Materials.Single(m => m.Id == firstId); material.Name = "LocalAnimatedName"; material.TexScale = 24; });
        source.Materials[0].Animation!.UvScroll = [.6f, 0]; MapPrefabService.Save(source, selected, prefab);
        Update(document, first.InstanceId, root);
        check(Material(firstId).Name == "LocalAnimatedName" && Material(firstId).TexScale == 24
            && Material(firstId).Animation!.UvScroll[0] == .6f,
            "source animation updates retain independent local material name and scale overrides");
        Guid localCollisionId = Guid.NewGuid();
        document.Edit("Add authored override-name collision", d => d.Materials.Add(new()
            { Id = localCollisionId, Name = "LocalAnimatedName", Texture = "tile.tex" }));
        source.Materials[0].Animation!.UvScroll = [.8f, 0]; MapPrefabService.Save(source, selected, prefab);
        Update(document, first.InstanceId, root);
        string resolvedOverrideName = Material(firstId).Name;
        check(resolvedOverrideName != "LocalAnimatedName" && Material(localCollisionId).Name == "LocalAnimatedName"
            && Material(firstId).TexScale == 24 && Material(firstId).Animation!.UvScroll[0] == .8f,
            "colliding local name override moves only the prefab track and preserves its other overrides");
        source.Materials[0].Animation!.UvScroll = [1f, 0]; MapPrefabService.Save(source, selected, prefab);
        Update(document, first.InstanceId, root);
        check(Material(firstId).Name == resolvedOverrideName && Material(firstId).TexScale == 24,
            "resolved local name override remains stable on subsequent source updates");

        var flattened = MapPrefabService.Flatten(document.Project.Definition);
        Compile(flattened, check, "flattened animated prefab with local overrides");
        var scheduler = new MapBuildScheduler(Path.Combine(root, "native-cache"));
        var build = scheduler.BuildAsync(MapBuildSnapshot.Capture(flattened)).GetAwaiter().GetResult();
        check(build.Succeeded, "resolved animated prefab builds a complete native runtime output set");
        byte[] native = File.ReadAllBytes(build.Outputs!.Animation);
        int uvGroup = checked((int)BitConverter.ToUInt32(native, 36));
        int uvCount = checked((int)BitConverter.ToUInt32(native, uvGroup + 16));
        int uvTracks = checked((int)BitConverter.ToUInt32(native, uvGroup + 20));
        string[] expectedTracks = flattened.Materials.Where(m => m.Animation?.FlipbookFrames.Count > 0)
            .Select(m => m.Name).ToArray();
        string[] Names(int offset, int stride, int count) => Enumerable.Range(0, count)
            .Select(i => Encoding.Latin1.GetString(native, offset + stride * i, 32).TrimEnd('\0')).ToArray();
        check(Names(uvTracks, 60, uvCount).SequenceEqual(expectedTracks),
            "native UV animation tracks bind each resolved prefab material name exactly once");
        int textureGroup = checked((int)BitConverter.ToUInt32(native, 40));
        int textureCount = BitConverter.ToUInt16(native, textureGroup + 8);
        int textureTracks = checked((int)BitConverter.ToUInt32(native, textureGroup + 24));
        check(Names(textureTracks, 44, textureCount).SequenceEqual(expectedTracks),
            "native flipbook animation tracks bind each resolved prefab material name exactly once");
        string package = scheduler.PackageAsync(MapBuildSnapshot.Capture(flattened),
            Path.Combine(root, "animated-resolved.ppmap")).GetAwaiter().GetResult();
        File.Delete(prefab);
        var packaged = MapDefinition.Load(package);
        check(packaged.PrefabInstances.Count == 0 && packaged.PrefabSource == null,
            "animated runtime package contains no external prefab source dependency");
        Compile(packaged, check, "packaged animated prefab after source deletion");
        check(packaged.Materials.Select(m => m.Name).SequenceEqual(flattened.Materials.Select(m => m.Name)),
            "canonical runtime package preserves the resolved animation material names");
    }

    private static MapPrefabService.InstanceResult Insert(MapDocument document, string path, string root, MapTransform transform)
        => document.InsertPrefab(path, root, transform);
    private static void Update(MapDocument document, Guid instanceId, string root)
        => document.UpdatePrefab(instanceId, root);
    private static void Compile(MapDefinition definition, Action<bool, string> check, string label)
    {
        var compiled = MapCompiler.Compile(definition);
        check(compiled.Map != null && compiled.Validation.IsValid, label + " compile through the native compiler: "
            + string.Join("; ", compiled.Validation.Diagnostics.Select(d => d.Message)));
    }
}
