// Cut/frame behavior adapted from Second Hunt Campaign/BodyArmCannon.cs
// at 41452f2d053fcfa679e33c00a73d120a53f59e61; MIT, see repository LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Import;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render.Characters;

internal sealed partial class CharacterSourceCannonRecipe
{
    internal const string GeneratedReceiptFile = "darksamus-echoes.cannon-source.json";
    // Filled only from the independently checked owned-source authoring output.
    internal const string QualifiedGeneratedReceiptSha256 = "5f1f479438b5efe1d482ff4565f7782e3175a2aed49b4a33a2953b82e4ac7521";
    internal static bool IsGeneratedEntry(CharacterModelManifestEntry e, string modelSha) =>
        e.Part == CharacterModelPart.ViewModel && e.Lod == 0 && e.PoseSource == CharacterPoseSource.SourceRigPreview
        && e.SourceCannonReceipt == GeneratedReceiptFile && e.SourceCannonSha256 == QualifiedGeneratedReceiptSha256
        && modelSha == CharacterModelPack.GeneratedNormalSourceGlbSha256;

    private static CharacterSourceCannonRecipe LoadGenerated(string root, CharacterModelManifestEntry entry, string modelSha)
    {
        Require(IsGeneratedEntry(entry, modelSha), "Only the pinned generated normal Echoes cannon recipe is qualified.");
        byte[] bytes = CharacterModelPack.ReadCannonInput(root, GeneratedReceiptFile, 1024 * 1024);
        Require(Sha(bytes) == QualifiedGeneratedReceiptSha256, "Generated cannon recipe changed.");
        using var document = JsonDocument.Parse(bytes); var r = document.RootElement;
        Require(r.GetProperty("format").GetInt32() == 2 && r.GetProperty("mode").GetString() == "generatedSourceCannonAuthoring"
            && r.GetProperty("presentationPart").GetString() == "viewModel" && r.GetProperty("sourceCharacter").GetString() == "darksamus-echoes"
            && r.GetProperty("clipOrdinal").GetInt32() == 32 && r.GetProperty("clipResourceId").GetUInt32() == 0xd12fdfa3
            && r.GetProperty("clipSeconds").GetDouble() == 0 && r.GetProperty("sourceElbowId").GetInt32() == 24
            && r.GetProperty("sourceParticleLocatorId").GetInt32() == 32
            && r.GetProperty("sourceUnitsUnscaled").GetBoolean() && r.GetProperty("calibrationOnly").GetBoolean()
            && !r.GetProperty("gameplayMuzzleChanged").GetBoolean() && !r.GetProperty("publicSelectionEnabled").GetBoolean()
            && !r.GetProperty("networkReplayEnabled").GetBoolean() && !r.GetProperty("firstPersonVerified").GetBoolean(), "Generated cannon is a fixed diagnostic source cut.");
        var inputs = r.GetProperty("sourceInputs").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
        string[] names = { "darksamus-echoes.source.glb", "darksamus-echoes.source.json", "darksamus-echoes.ancs-graph.json",
            "darksamus-echoes.ancs.bin", "darksamus-echoes.cinf.bin", "darksamus-echoes.animations.bin" };
        Require(inputs.Keys.Order().SequenceEqual(names.Order()) && inputs[names[0]] == modelSha
            && entry.Model == names[0] && entry.Provenance == names[1] && inputs[names[1]] == entry.ProvenanceSha256,
            "Generated cannon uses the complete body and its same source inputs.");
        float[] m = r.GetProperty("toCannon").EnumerateArray().Select(v => v.GetSingle()).ToArray(); Require(m.Length == 16, "Complete cannon basis.");
        var basis = new Matrix4(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);
        CharacterSourceRig.RequireRigid(basis);
        int[][] triangles = r.GetProperty("triangles").EnumerateArray().Select(a => a.EnumerateArray().Select(n => n.GetInt32()).ToArray()).ToArray();
        Require(triangles.Length == 7 && triangles.Sum(a => a.Length) == 392 && triangles[2].Length == 66 && triangles[5].Length == 326,
            "Complete fixed generated cannon cut.");
        var recipe = new CharacterSourceCannonRecipe(QualifiedGeneratedReceiptSha256, basis, inputs, triangles, generated: true);
        recipe.RevalidateGenerated(root, entry, modelSha); return recipe;
    }
    private void RevalidateGenerated(string root, CharacterModelManifestEntry entry, string modelSha)
    {
        Require(IsGeneratedEntry(entry, modelSha) && Sha(CharacterModelPack.ReadCannonInput(root, GeneratedReceiptFile, 1024 * 1024)) == ReceiptSha256,
            "Exact generated cannon owner/receipt required.");
        foreach (var pair in _inputs)
            Require((pair.Key.EndsWith(".glb", StringComparison.Ordinal) ? modelSha
                : Sha(CharacterModelPack.ReadCannonInput(root, pair.Key, pair.Key.EndsWith(".animations.bin", StringComparison.Ordinal)
                    ? RetroCharacterAnimationArchive.MaximumBytes : 1024 * 1024))) == pair.Value, "Generated cannon source input changed: " + pair.Key);
    }
    private CharacterWeightedModelData DeriveGenerated(CharacterModelPack.SourcePreviewLease lease, CharacterModelAsset asset, CharacterWeightedModelData source)
    {
        lease.Require(asset);
        Require(ReferenceEquals(lease.SourceCannon, this) && asset.Part == CharacterModelPart.ViewModel && source.SourceCannon == null
            && source.SourceRig is { Nodes.Count: 40 } && source.Joints.Count == 39 && lease.SourceAnimations != null,
            "Complete generated cannon lease and rig required.");
        var clip = CharacterSourceClip.Load(lease, asset, source.SourceRig!, 32); var pose = clip.Sample(0);
        var basis = GeneratedBasis(source, pose);
        Require(CharacterSourceClip.MatrixDistance(basis, ToCannon) < .00001f, "Generated cannon frame must reproduce from the full original pose.");
        var cut = SelectGeneratedCut(source);
        Require(cut.Selected.Length == _triangles.Count && cut.Selected.Select((ids, m) => ids.SequenceEqual(_triangles[m])).All(v => v),
            "Whole source triangle IDs differ from the pinned cannon recipe.");
        lease.Require(asset);
        return source with { Primitives = cut.Primitives, SourceCannon = this, SourceAuthoringPose = Array.AsReadOnly(pose.ToArray()) };
    }

    // Pure geometry kernel. It grants no pack/lease capability and accepts own
    // synthetic rigs for testing node order, full joints and physical batches.
    internal static bool FinalTrimUv(float u, float v) => MathF.Abs(u - .489f) < .002f && MathF.Abs(v - .764f) < .002f;
    internal static (IReadOnlyList<CharacterWeightedPrimitive> Primitives, int[][] Selected) SelectGeneratedCut(CharacterWeightedModelData source, int elbowId = 24, bool finalTrim = false)
    {
        var rig = source.SourceRig ?? throw new InvalidDataException("Complete source cut rig required.");
        int elbow = NamedNode(rig, "source_bone_" + elbowId + "_R_elbow");
        var result = new List<CharacterWeightedPrimitive>(); var groups = new SortedDictionary<int, List<int>>(); var allIds = new HashSet<int>();
        foreach (var p in source.Primitives)
        {
            Require(p.SourceMaterialOrdinal is >= 0 and < RetroCharacterSourceGlbWriter.MaximumMaterials
                && p.SourceTriangleIds != null && p.Indices.Length % 3 == 0 && p.SourceTriangleIds.Length == p.Indices.Length / 3
                && p.Indices.All(i => i < p.Vertices.Length) && p.JointPalette is { Length: > 0 and <= 32 }
                && p.JointPalette.Distinct().Count() == p.JointPalette.Length && p.JointPalette.All(j => (uint)j < rig.JointNodes.Count),
                "Identified source triangles and complete valid palette required.");
            if (!groups.TryGetValue(p.SourceMaterialOrdinal, out var selected)) groups.Add(p.SourceMaterialOrdinal, selected = new());
            var indices = new List<uint>(); var ids = new List<int>();
            for (int t = 0; t < p.SourceTriangleIds!.Length; t++)
            {
                int id = p.SourceTriangleIds[t]; Require(id >= 0 && allIds.Add(id), "Every original source triangle ID must be unique.");
                bool on = true;
                for (int c = 0; c < 3; c++) on &= On(p.Vertices[p.Indices[t * 3 + c]], p, rig, elbow);
                if (finalTrim && p.SourceMaterialOrdinal == 5
                    && Enumerable.Range(0,3).Any(c => {
                        var uv = p.Vertices[p.Indices[t * 3 + c]].Texcoord;
                        return FinalTrimUv(uv.X,uv.Y);
                    })) continue;
                if (!on) continue;
                selected.Add(id); ids.Add(id); indices.AddRange(p.Indices.AsSpan(t * 3, 3).ToArray());
            }
            if (ids.Count != 0) result.Add(p with { Indices = indices.ToArray(), SourceTriangleIds = ids.ToArray() });
        }
        Require(groups.Count != 0, "Source material groups required.");
        int[][] grouped = Enumerable.Range(0, groups.Keys.Max() + 1).Select(m => groups.TryGetValue(m, out var ids) ? ids.Order().ToArray() : Array.Empty<int>()).ToArray();
        return (result.AsReadOnly(), grouped);
    }
    private static int NamedNode(CharacterSourceRig rig, string name)
    {
        int[] found = Enumerable.Range(0, rig.Nodes.Count).Where(n => rig.Nodes[n].Name == name).ToArray();
        Require(found.Length == 1 && rig.JointNodes.Contains(found[0]), "Exact named source locator joint required."); return found[0];
    }
    internal static Matrix4 GeneratedBasis(CharacterWeightedModelData source, IReadOnlyList<Matrix4> pose,
        int elbowId = 24, int tipId = 32, string tipName = "GUN_Particle_LCTR")
    {
        var rig = source.SourceRig ?? throw new InvalidDataException("Complete source frame rig required.");
        int elbow = NamedNode(rig, "source_bone_" + elbowId + "_R_elbow"), tip = NamedNode(rig, "source_bone_" + tipId + "_" + tipName);
        Require(Under(rig, tip, elbow), "Source muzzle locator must descend from elbow.");
        Matrix4[] skin = rig.Evaluate(pose, source.Joints);
        Vector3 World(int node) { int joint = rig.JointNodes.IndexOf(node); return (source.Joints[joint].InverseBind.Inverted() * skin[joint]).Row3.Xyz; }
        return SourceFrame(World(elbow), World(tip));
    }
    internal static Matrix4 SourceFrame(Vector3 elbow, Vector3 tip)
    {
        Vector3 forward = tip - elbow; Require(float.IsFinite(forward.LengthSquared) && forward.LengthSquared > 1e-8f, "Nonzero bounded cannon axis required."); forward.Normalize();
        var up = Vector3.UnitY - Vector3.Dot(Vector3.UnitY, forward) * forward;
        Require(up.LengthSquared > 1e-8f, "Cannon frame requires unambiguous source up."); up.Normalize(); var left = Vector3.Cross(up, forward);
        var basis = Matrix4.CreateTranslation(-tip) * new Matrix4(left.X,up.X,forward.X,0,left.Y,up.Y,forward.Y,0,left.Z,up.Z,forward.Z,0,0,0,0,1);
        CharacterSourceRig.RequireRigid(basis); return basis;
    }
    private static bool On(CharacterWeightedVertex vertex, CharacterWeightedPrimitive primitive, CharacterSourceRig rig, int elbow)
    {
        Require(vertex.PackedJoints >= 0 && (vertex.PackedJoints >> 20) == 0, "Bounded packed source joints required."); bool result = false; float sum = 0; var used = new HashSet<int>();
        for (int i = 0; i < 4; i++)
        {
            float weight = vertex.Weights[i]; Require(float.IsFinite(weight) && weight >= 0, "Finite positive source weights required."); if (weight == 0) continue;
            int slot = vertex.PackedJoints >> (i * 5) & 31; Require(slot < primitive.JointPalette!.Length, "Local source joint outside palette.");
            int global = primitive.JointPalette[slot]; Require(used.Add(global), "Duplicate positive source joint."); sum += weight;
            result |= Under(rig, rig.JointNodes[global], elbow);
        }
        Require(MathF.Abs(sum - 1) <= .00001f, "Unchanged unit source weights required."); return result;
    }
}
