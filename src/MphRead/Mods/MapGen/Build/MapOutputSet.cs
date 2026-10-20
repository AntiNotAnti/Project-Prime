using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace MphRead.Mods.MapGen
{
    public sealed record MapOutputSet(string Model, string Animation, string Collision, string Entities, string Nodes)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SourceGeometry { get; init; }
        public IEnumerable<string> Files => SourceGeometry is { } sourceGeometry
            ? new[] { Model, Animation, Collision, Entities, Nodes, sourceGeometry }
            : new[] { Model, Animation, Collision, Entities, Nodes };
        public bool Complete => Files.All(File.Exists);
        public string Manifest => Path.Combine(Path.GetDirectoryName(Model)!, "map.build.json");

        public static MapOutputSet Create(MapDefinition definition, string archive, string entities, string nodes)
        {
            MapValidator.RequireRuntimeName(definition.Name);
            string prefix = definition.Name.ToLowerInvariant();
            return new(Path.Combine(archive, prefix + "_Model.bin"),
                Path.Combine(archive, prefix + "_Anim.bin"),
                Path.Combine(archive, prefix + "_Collision.bin"),
                Path.Combine(entities, prefix + "_Ent.bin"),
                Path.Combine(nodes, prefix + "_Node.bin"))
            {
                SourceGeometry = definition.HasSourceGeometry
                    ? Path.Combine(archive, prefix + "_SourceGeometry.bin") : null
            };
        }
    }
}
