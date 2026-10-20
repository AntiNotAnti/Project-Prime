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
        RetroCharacterAnimationArchive.Snapshot archive, Dictionary<string, string> inputs)
    {
        void Require(bool value, string why) { if (!value) throw new InvalidDataException(why); }
        var bones = skeleton.Bones;
        Require(bones.Count == 39 && bones.TryGetValue(24, out var elbow) && elbow.Name == "R_elbow"
            && bones.TryGetValue(32, out var locator) && locator.Name == "GUN_Particle_LCTR", "Exact normal cannon source locators required.");
        bool Under(uint id)
        {
            var seen = new HashSet<uint>();
            while (bones.TryGetValue(id, out var bone))
            { Require(seen.Add(id), "Source cut ancestry cycle."); if (id == 24) return true; id = bone.Parent; }
            Require(id == 97, "Source cut requires complete original ancestry."); return false;
        }
        Require(Under(32) && model.Materials.Length == 7 && model.Skin?.Count == model.PositionCount, "Complete original normal source cut required.");
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
        Require(selected.Sum(a => a.Count) == 392 && selected[2].Count == 66 && selected[5].Count == 326,
            "Original normal elbow cut must retain all 392 complete source triangles.");
        var root = archive.Roots[32]; var clip = archive.OpenStrictPrimitive(32);
        Require(root.CharacterDeclared && clip.Source.Id == 0xd12fdfa3 && clip.RigidScale, "Exact rigid zero-phase Missile Barrage source primitive required.");
        var animation = clip.CaptureAnimation(); animation.Sample(0, out var rotations, out var translations);
        Require(animation.Rot.Keys.Concat(animation.Trans.Keys).Concat(animation.Scale.Keys).All(bones.ContainsKey), "Foreign cannon animation channel.");
        var globals = new Dictionary<uint, Matrix4>();
        foreach (uint id in bones.Keys.Order())
        {
            var chain = new Stack<uint>(); var seen = new HashSet<uint>(); uint current = id;
            while (bones.ContainsKey(current) && !globals.ContainsKey(current))
            { Require(seen.Add(current), "Source pose ancestry cycle."); chain.Push(current); current = bones[current].Parent; }
            Require(globals.ContainsKey(current) || current == 97, "Complete source root97 pose required.");
            while (chain.TryPop(out uint node))
            {
                var bone = bones[node]; double[] bind = { bone.X, bone.Y, bone.Z };
                if (bones.TryGetValue(bone.Parent, out var parent)) { bind[0] -= parent.X; bind[1] -= parent.Y; bind[2] -= parent.Z; }
                Matrix4 local = CharacterSourceClip.ComposeSourceLocal(bind, rotations.GetValueOrDefault(node), translations.GetValueOrDefault(node));
                globals.Add(node, globals.TryGetValue(bone.Parent, out var global) ? local * global : local);
            }
        }
        Matrix4 m = CharacterSourceCannonRecipe.SourceFrame(globals[24].Row3.Xyz, globals[32].Row3.Xyz);
        float[] matrix = { m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44 };
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = 2, mode = "generatedSourceCannonAuthoring", presentationPart = "viewModel", sourceCharacter = "darksamus-echoes",
            writerVersion = RetroCharacterSourceGlbWriter.Version, materialPolicy = RetroCharacterSourceGlbWriter.MaterialPolicy, sourceBasis = "(-x,z,y)",
            sourceInputs = inputs, sourceElbowId = 24, sourceElbowName = "R_elbow", sourceParticleLocatorId = 32, sourceParticleLocatorName = "GUN_Particle_LCTR",
            clipOrdinal = 32, clipResourceId = 0xd12fdfa3u, clipSeconds = 0, toCannon = matrix, triangles = selected,
            sourceUnitsUnscaled = true, calibrationOnly = true, gameplayMuzzleChanged = false, publicSelectionEnabled = false,
            networkReplayEnabled = false, firstPersonVerified = false
        });
    }
}
