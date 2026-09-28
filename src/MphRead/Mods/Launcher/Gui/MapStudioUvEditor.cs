using System;
using System.Linq;
using System.Numerics;
using Avalonia.Controls;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private float[][]? _copiedFaceUv;
    private void FaceUvControls(StackPanel panel)
    {
        if (_document == null || _viewport is not { ElementMode: "Face" } viewport || viewport.SelectedFaceIndices.Count == 0) return;
        Guid id = viewport.SelectedFaceObjectId;
        int[] faces = viewport.SelectedFaceIndices.ToArray();
        if (MapObjects.Find(_document.Project.Definition, id)?.Value is not MapMesh mesh) return;
        float texScale = _document.Project.Definition.Materials[mesh.Material].TexScale;
        panel.Children.Add(Text($"UV · {faces.Length} selected faces · " +
            (faces.All(i => i < mesh.FaceTexcoords.Count && mesh.FaceTexcoords[i] != null) ? "Explicit" : "Projected / mixed")));
        var scaleX = new TextBox { Text = "1" }; var scaleY = new TextBox { Text = "1" };
        var offsetX = new TextBox { Text = "0" }; var offsetY = new TextBox { Text = "0" };
        var rotation = new TextBox { Text = "0" };
        foreach (var field in new[] { ("Scale U", scaleX), ("Scale V", scaleY), ("Offset U (texels)", offsetX), ("Offset V (texels)", offsetY), ("Rotation (degrees)", rotation) })
        { panel.Children.Add(Text(field.Item1)); panel.Children.Add(field.Item2); }
        void Change(string label, Vector2 scale, Vector2 offset, float angle)
        {
            try
            {
                _document.EditObjects(label, new[] { id }, d =>
                {
                    var target = (MapMesh)MapObjects.Find(d, id)!.Value;
                    MapMeshEditing.TransformUv(target, faces, scale, offset, angle, texScale);
                });
            }
            catch (Exception ex) { Failure(ex); }
        }
        AddButton(panel, "Apply UV", () =>
        {
            try { Change("Transform UV", new(Number(scaleX.Text!), Number(scaleY.Text!)), new(Number(offsetX.Text!), Number(offsetY.Text!)), Number(rotation.Text!)); }
            catch (Exception ex) { Failure(ex); }
        });
        void EditUv(string label, Action<MapMesh> action)
        {
            try { _document.EditObjects(label, new[] { id }, d => action((MapMesh)MapObjects.Find(d,id)!.Value)); }
            catch (Exception ex) { Failure(ex); }
        }
        AddButton(panel, "Fit UV to 64 × 64", () => EditUv("Fit UV", m => MapMeshEditing.FitUv(m,faces,texScale)));
        AddButton(panel, "Copy UV from first selected face", () =>
        {
            var uv = GeometryCompiler.Compile(mesh,texScale)[faces[0]].Texcoords;
            _copiedFaceUv = uv.Select(p => new[] {p.X,p.Y}).ToArray(); _status.Text="UV copied.";
        });
        AddButton(panel, "Paste UV", () => EditUv("Paste UV", m =>
        {
            if (_copiedFaceUv == null || faces.Any(f => m.Faces[f].Length != _copiedFaceUv.Length))
                throw new InvalidOperationException("Copy a face with the same number of corners first.");
            while(m.FaceTexcoords.Count < m.Faces.Count) m.FaceTexcoords.Add(null);
            foreach(int face in faces) m.FaceTexcoords[face] = _copiedFaceUv.Select(p=>p.ToArray()).ToArray();
        }));
        var density = new TextBox { Text = texScale.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        panel.Children.Add(Text("Texels per world unit")); panel.Children.Add(density);
        AddButton(panel, "Match texel density", () => EditUv("Match texel density", m => MapMeshEditing.MatchTexelDensity(m,faces,Number(density.Text!),texScale)));
        void SelectFaces(System.Collections.Generic.IEnumerable<int> selection)
        { viewport.SelectedFaceIndices.Clear(); viewport.SelectedFaceIndices.UnionWith(selection); viewport.InvalidateVisual(); ShowInspectorPage(_inspectorPage,false); }
        AddButton(panel, "Select connected faces", () => SelectFaces(faces.SelectMany(f => MapMeshEditing.ConnectedFaces(mesh,f)).Distinct()));
        AddButton(panel, "Select faces with same material", () =>
        {
            int selectedMaterial = faces[0] < mesh.FaceMaterials.Count ? mesh.FaceMaterials[faces[0]] : mesh.Material;
            SelectFaces(Enumerable.Range(0,mesh.Faces.Count).Where(f => (f < mesh.FaceMaterials.Count ? mesh.FaceMaterials[f] : mesh.Material) == selectedMaterial));
        });
        AddButton(panel, "Select all faces", () => SelectFaces(Enumerable.Range(0,mesh.Faces.Count)));
        AddButton(panel, "Rotate +90°", () => Change("Rotate UV", Vector2.One, Vector2.Zero, 90));
        AddButton(panel, "Rotate −90°", () => Change("Rotate UV", Vector2.One, Vector2.Zero, -90));
        AddButton(panel, "Flip U", () => Change("Flip U", new(-1, 1), Vector2.Zero, 0));
        AddButton(panel, "Flip V", () => Change("Flip V", new(1, -1), Vector2.Zero, 0));
        AddButton(panel, "Convert to explicit UV", () => Change("Explicit UV", Vector2.One, Vector2.Zero, 0));
        AddButton(panel, "Reset to projected UV", () => _document.EditObjects("Projected UV", new[] { id }, d =>
        {
            var target = (MapMesh)MapObjects.Find(d, id)!.Value;
            foreach (int face in faces) if (face < target.FaceTexcoords.Count) target.FaceTexcoords[face] = null;
        }));
    }
}
