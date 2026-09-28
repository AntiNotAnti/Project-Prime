using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public sealed partial class MapDocument
{
    public void PaintFaces(Guid meshId, IEnumerable<int> faces, int material, object? stroke = null)
    {
        if (material < 0 || material >= Project.Definition.Materials.Count) throw new ArgumentOutOfRangeException(nameof(material));
        var mesh = Project.Definition.Geometry.OfType<MapMesh>().Single(m => m.Id == meshId);
        var selected = faces.Distinct().ToArray();
        if (selected.Any(f => f < 0 || f >= mesh.Faces.Count)) throw new ArgumentOutOfRangeException(nameof(faces));
        var before = selected.ToDictionary(f => f, f => f < mesh.FaceMaterials.Count ? mesh.FaceMaterials[f] : mesh.Material);
        foreach (int face in before.Where(p => p.Value == material).Select(p => p.Key).ToArray()) before.Remove(face);
        if (before.Count == 0) return;
        History.Execute(new PaintCommand(this, meshId, mesh.FaceMaterials.Count, before,
            before.Keys.ToDictionary(f => f, _ => material)), stroke);
    }
    private sealed class PaintCommand(MapDocument document, Guid id, int originalCount,
        Dictionary<int, int> before, Dictionary<int, int> after) : IMapEditCommand
    {
        private readonly MapDocument _document = document;
        private readonly Guid _id = id;
        private readonly Dictionary<int, int> _before = before, _after = after;
        public string Label => "Paint face material";
        public long ApproximateBytes => 128 + (_before.Count + _after.Count) * 32L;
        public MapDocumentChange Change => new(MapChangeDomain.Geometry, new[] { _id });
        private MapMesh Mesh => _document.Project.Definition.Geometry.OfType<MapMesh>().Single(m => m.Id == _id);
        public void Execute() => Apply(_after);
        public void Undo()
        {
            Apply(_before);
            var mesh = Mesh;
            if (mesh.FaceMaterials.Count > originalCount) mesh.FaceMaterials.RemoveRange(originalCount, mesh.FaceMaterials.Count - originalCount);
        }
        private void Apply(Dictionary<int, int> values)
        {
            var mesh = Mesh;
            foreach (var pair in values)
            {
                while (mesh.FaceMaterials.Count <= pair.Key) mesh.FaceMaterials.Add(mesh.Material);
                mesh.FaceMaterials[pair.Key] = pair.Value;
            }
        }
        public bool TryMerge(IMapEditCommand next)
        {
            if (next is not PaintCommand paint || paint._document != _document || paint._id != _id) return false;
            foreach (var pair in paint._before) _before.TryAdd(pair.Key, pair.Value);
            foreach (var pair in paint._after) _after[pair.Key] = pair.Value;
            return true;
        }
    }
}
