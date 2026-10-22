using System;
using System.IO;
using System.Linq;
using MphRead.Mods.MapGen;
using MphRead.Mods.MapEditor;

internal static class ModelReimportChecks
{
    public static void Run(Action<bool, string> check, string root)
    {
        string path = Path.Combine(root, "reimport.obj");
        string source = "mtllib reimport.mtl\no Floor\nv 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 0 1\nusemtl red\nf 1/1 2/2 3/3\n";
        File.WriteAllText(path, source);
        File.WriteAllText(Path.Combine(root, "reimport.mtl"), "newmtl red\nKd 1 0 0\n");
        var doc = new MapDocument(new MapProject(new MapDefinition()));
        void Import(Guid? id = null) => doc.Edit("Reimport", d => ModelReimport.Apply(d,
            ModelImportService.Import(path, new()), path, MapHash256.HashFile(path).ToString(), new(), id));
        Import();
        var model = doc.Project.Definition.ModelSources.Single();
        Guid id = model.Id, objectId = model.Objects.Single().Id;
        string before = doc.Project.Definition.Serialize(); var state = doc.CurrentStateId;
        Import(id);
        check(doc.CurrentStateId == state && doc.Project.Definition.Serialize() == before, "unchanged reimport creates no history or effective change");
        doc.Edit("Local overrides", d =>
        {
            var mesh = (MapMesh)d.Geometry.Single();
            mesh.Transform.Position[0] = 7; mesh.FaceMaterials[0] = 0;
            mesh.FaceTexcoords[0]![0][0] = 123;
        });
        before = doc.Project.Definition.Serialize();
        File.WriteAllText(path, source + "o Added\nf 1 3 2\n");
        Import(id);
        var updated = (MapMesh)doc.Project.Definition.Geometry.Single(m => m.Id == objectId);
        check(doc.Project.Definition.Geometry.Count == 2 && updated.Transform.Position[0] == 7, "reimport adds objects and preserves transform and identity");
        check(updated.FaceMaterials[0] == 0 && updated.FaceTexcoords[0]![0][0] == 123, "compatible face paint and explicit UV edits survive reimport");
        doc.History.Undo(); check(doc.Project.Definition.Serialize() == before, "reimport undo restores geometry, materials and provenance together");
        doc.History.Redo(); check(doc.Project.Definition.Geometry.Count == 2, "reimport redo restores added object");
        File.WriteAllText(path, source);
        Import(id); check(doc.Project.Definition.Geometry.Count == 1, "reimport removes only obsolete source objects");
        int red = doc.Project.Definition.Materials.FindIndex(m => m.Name == "red");
        string? texture = doc.Project.Definition.Materials[red].Texture;
        File.WriteAllText(Path.Combine(root, "reimport.mtl"), "newmtl red\nKd 0 1 0\n");
        Import(id); check(doc.Project.Definition.Materials[red].Texture != texture, "MTL-only changes update unedited material without duplicating mapping");
        doc.Edit("Override material", d => d.Materials[red].TexScale = 9);
        File.WriteAllText(Path.Combine(root, "reimport.mtl"), "newmtl red\nKd 0 0 1\n");
        Import(id); check(doc.Project.Definition.Materials[red].TexScale == 9, "Studio material override survives external material changes");
        before = doc.Project.Definition.Serialize(); File.Delete(path);
        bool missing = false; try { Import(id); } catch (FileNotFoundException) { missing = true; }
        check(missing && doc.Project.Definition.Serialize() == before, "missing source leaves existing import untouched");
        var paintMesh = new MapMesh { Vertices = new() { new[] {0f,0,0}, new[] {1f,0,0}, new[] {0f,1,0}, new[] {1f,1,0} },
            Faces = new() {new[] {0,1,2},new[] {1,3,2}}, FaceMaterials = new() {1,1} };
        var paintDefinition = new MapDefinition { Materials = new() { new(), new(), new() }, Geometry = new() {paintMesh} };
        var paintDocument = new MapDocument(new MapProject(paintDefinition));
        object stroke = new();
        paintDocument.PaintFaces(paintMesh.Id,new[] {0},0,stroke);
        paintDocument.PaintFaces(paintMesh.Id,new[] {1},0,stroke);
        check(paintDocument.History.CommandCount == 1, "drag stroke coalesces face deltas into one undo command");
        paintDocument.History.Undo();
        check(((MapMesh)paintDocument.Project.Definition.Geometry.Single()).FaceMaterials.All(m=>m==1), "stroke undo restores every painted face");
        paintDocument.History.Redo();
        check(((MapMesh)paintDocument.Project.Definition.Geometry.Single()).FaceMaterials.All(m=>m==0), "stroke redo reapplies every painted face");
        bool inUse = false; try { MapMaterialEditing.DeleteUnused(paintDefinition,1); } catch(InvalidOperationException) {inUse=true;}
        check(inUse, "per-face reference prevents deleting a material");
        MapMaterialEditing.Replace(paintDefinition,1,2); MapMaterialEditing.DeleteUnused(paintDefinition,1);
        check(paintDefinition.Materials.Count == 2 && paintMesh.FaceMaterials.All(m=>m==1), "replacement and deletion remap face indices");
        MapMeshEditing.FitUv(paintMesh,new[] {0},16);
        check(paintMesh.FaceTexcoords[0]!.Max(p=>p[0]) == 64 && paintMesh.FaceTexcoords[0]!.Max(p=>p[1]) == 64, "UV fit spans target texture");
        MapMeshEditing.MatchTexelDensity(paintMesh,new[] {0},16,16);
        var uv = paintMesh.FaceTexcoords[0]!;
        double area = Math.Abs((uv[1][0]-uv[0][0])*(uv[2][1]-uv[0][1])-(uv[1][1]-uv[0][1])*(uv[2][0]-uv[0][0])) / 2;
        check(Math.Abs(area-128) < .01, "texel density matches world-space face area");
        var clone = MapProjectSerializer.Clone(doc.Project.Definition);
        check(clone.ModelSources.Single().Objects.Single().Id == objectId, "source tracking survives project serialization");
    }
}
