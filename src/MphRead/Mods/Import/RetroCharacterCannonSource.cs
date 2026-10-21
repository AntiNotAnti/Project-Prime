// Cut/frame behavior adapted from Second Hunt Campaign/BodyArmCannon.cs
// at 41452f2d053fcfa679e33c00a73d120a53f59e61; MIT, see repository LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.Render.Characters;
using OpenTK.Mathematics;

namespace MphRead.Mods.Import;

// Pure importer authoring from captured original CMDL/CSKR/CINF and the existing
// bounded raw animation bank. No Scene, renderer, lease or gameplay gun owner.
internal static class RetroCharacterCannonSource
{
    internal static byte[] Author(Cmdl model, RetroNativeSkeleton skeleton,
        RetroCharacterAnimationArchive.Snapshot archive, Dictionary<string, string> inputs,
        string sourceCharacter = "darksamus-echoes")
    {
        void Require(bool value, string why) { if (!value) throw new InvalidDataException(why); }
        bool trooper = sourceCharacter is "trooper-power" or "trooper-ice" or "trooper-wave" or "trooper-plasma";
        Require(trooper || sourceCharacter == "darksamus-echoes", "Closed generated cannon source profile required.");
        uint elbowId = trooper ? 23u : 24u, tipId = trooper ? 34u : 32u, externalParent = trooper ? 2u : 97u;
        int ordinal = trooper ? 1 : 32;
        string tipName = trooper ? "R_gun_LCTR" : "GUN_Particle_LCTR";
        var bones = skeleton.Bones;
        Require(bones.Count == (trooper ? 35 : 39) && bones.TryGetValue(elbowId, out var elbow) && elbow.Name == "R_elbow"
            && bones.TryGetValue(tipId, out var locator) && locator.Name == tipName, "Exact generated cannon source locators required.");
        bool Under(uint id)
        {
            var seen = new HashSet<uint>();
            while (bones.TryGetValue(id, out var bone))
            { Require(seen.Add(id), "Source cut ancestry cycle."); if (id == elbowId) return true; id = bone.Parent; }
            Require(id == externalParent, "Source cut requires complete original ancestry."); return false;
        }
        Require(Under(tipId) && model.Materials.Length == (trooper ? 3 : 7) && model.Skin?.Count == model.PositionCount, "Complete original source cut required.");
        var selected = Enumerable.Range(0, model.Materials.Length).Select(_ => new List<int>()).ToArray();
        for (int face = 0; face < model.TriangleCount; face++)
        {
            bool on = true;
            for (int c = 0; c < 3; c++)
            {
                var weights = model.Skin![model.Triangles[face * 3 + c].P]; bool vertex = false;
                foreach (var influence in weights)
                { Require(float.IsFinite(influence.Weight) && influence.Weight >= 0, "Invalid source cut weight."); if (influence.Weight > 0) vertex |= Under(influence.Bone); }
                on &= vertex;
            }
            if (on) selected[model.TriangleMaterial[face]].Add(face);
        }
        Require(trooper ? selected.Sum(a => a.Count) == 206 && selected[0].Count == 10 && selected[1].Count == 32 && selected[2].Count == 164
            : selected.Sum(a => a.Count) == 392 && selected[2].Count == 66 && selected[5].Count == 326,
            "Original elbow cut must retain the complete pinned whole-triangle inventory.");
        var root = archive.Roots[ordinal]; var clip = archive.OpenStrictPrimitive(ordinal);
        Require(root.CharacterDeclared && clip.Source.Id == (trooper ? 0x5528972bu : 0xd12fdfa3u) && clip.RigidScale
            && (!trooper || root.Name == "A_gunloop_pirate" && clip.Source.Sha256 == "5b6540fbfdcbf37edd24133b4475468e0f0199a744e3159df2f0b942c498ccc7"),
            "Exact rigid zero-phase cannon source primitive required.");
        var animation = clip.CaptureAnimation(); animation.Sample(0, out var rotations, out var translations);
        Require(animation.Rot.Keys.Concat(animation.Trans.Keys).Concat(animation.Scale.Keys).All(bones.ContainsKey), "Foreign cannon animation channel.");
        var globals = new Dictionary<uint, Matrix4>();
        foreach (uint id in bones.Keys.Order())
        {
            var chain = new Stack<uint>(); var seen = new HashSet<uint>(); uint current = id;
            while (bones.ContainsKey(current) && !globals.ContainsKey(current))
            { Require(seen.Add(current), "Source pose ancestry cycle."); chain.Push(current); current = bones[current].Parent; }
            Require(globals.ContainsKey(current) || current == externalParent, "Complete original source root pose required.");
            while (chain.TryPop(out uint node))
            {
                var bone = bones[node]; double[] bind = { bone.X, bone.Y, bone.Z };
                if (bones.TryGetValue(bone.Parent, out var parent)) { bind[0] -= parent.X; bind[1] -= parent.Y; bind[2] -= parent.Z; }
                Matrix4 local = CharacterSourceClip.ComposeSourceLocal(bind, rotations.GetValueOrDefault(node), translations.GetValueOrDefault(node));
                globals.Add(node, globals.TryGetValue(bone.Parent, out var global) ? local * global : local);
            }
        }
        Matrix4 m = CharacterSourceCannonRecipe.SourceFrame(globals[elbowId].Row3.Xyz, globals[tipId].Row3.Xyz);
        float[] matrix = { m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44 };
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = 2, mode = "generatedSourceCannonAuthoring", presentationPart = "viewModel", sourceCharacter,
            writerVersion = RetroCharacterSourceGlbWriter.Version, materialPolicy = RetroCharacterSourceGlbWriter.MaterialPolicy, sourceBasis = "(-x,z,y)",
            sourceInputs = inputs, sourceElbowId = elbowId, sourceElbowName = "R_elbow", sourceParticleLocatorId = tipId, sourceParticleLocatorName = tipName,
            clipOrdinal = ordinal, clipResourceId = clip.Source.Id, clipSeconds = 0, toCannon = matrix, triangles = selected,
            sourceUnitsUnscaled = true, calibrationOnly = true, gameplayMuzzleChanged = false, publicSelectionEnabled = false,
            networkReplayEnabled = false, firstPersonVerified = false
        });
    }
}
