using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render;

// CPU-only, scene-owned poses. Geometry IDs are borrowed from the live scene;
// these copies never acquire a GPU lease or enter animation/simulation updates.
internal sealed class DetachedShadowPoseCache
{
    internal const int MaxRigs = 128, MaxNodes = 8192, MaxMeshes = 4096, MaxMaterials = 4096;
    internal sealed class Pose
    {
        internal readonly Model Source;
        internal readonly ModelInstance Instance;
        internal readonly AnimationInfo Neutral = new();
        internal readonly bool BoundsKnown;
        internal readonly OpenTK.Mathematics.Vector3 Min, Max;
        internal long Collection;
        internal Pose(Model source)
        {
            Source = source; Instance = new(source.CreateSceneCopy());
            ParkourPoseOverlay.Prewarm(Instance.Model);
            HeadHitPoseOverlay.Prewarm(Instance.Model);
            BoundsKnown = DetachedShadowBounds.Read(source, out Min, out Max);
        }
    }
    private readonly record struct Key(object Owner, ModelInstance Instance, Model Source);
    private readonly Dictionary<Key, Pose> _poses = new();
    private int _nodes, _meshes, _materials;
    internal int Count => _poses.Count;
    internal int Nodes => _nodes;
    internal int Meshes => _meshes;
    internal int Materials => _materials;
    internal void Clear() { _poses.Clear(); _nodes = _meshes = _materials = 0; }

    internal bool TryPrepare(object owner, ModelInstance source, long collection, out Pose pose)
    {
        pose = null!;
        Model model = source.Model;
        if (collection <= 0 || model.Nodes.Count == 0 || model.NodeMatrixIds.Count > 32
            || model.Nodes.Count > MaxNodes || model.Meshes.Count > MaxMeshes || model.Materials.Count > MaxMaterials) return false;
        var key = new Key(owner, source, model);
        if (_poses.TryGetValue(key, out Pose? existing))
        {
            // The ordinary entity draw visit owns the preparation. Duplicate
            // callbacks within one collection cannot resubmit the same rig.
            if (existing.Collection == collection) return false;
            pose = existing;
        }
        else
        {
            while (_poses.Count >= MaxRigs || _nodes + model.Nodes.Count > MaxNodes
                || _meshes + model.Meshes.Count > MaxMeshes || _materials + model.Materials.Count > MaxMaterials)
            {
                Key oldest = default;
                long age = collection;
                foreach (var pair in _poses)
                    if (pair.Value.Collection < age) { oldest = pair.Key; age = pair.Value.Collection; }
                if (oldest.Owner == null) return false; // All remaining owners are used this picture.
                _nodes -= oldest.Source!.Nodes.Count; _meshes -= oldest.Source!.Meshes.Count;
                _materials -= oldest.Source!.Materials.Count;
                _poses.Remove(oldest);
            }
            pose = new(model); _poses.Add(key, pose);
            _nodes += model.Nodes.Count; _meshes += model.Meshes.Count; _materials += model.Materials.Count;
        }
        pose.Collection = collection;
        Refresh(model, pose.Instance.Model);
        CopyAnimation(source.AnimInfo, pose.Instance.AnimInfo);
        return true;
    }

    private static void CopyAnimation(AnimationInfo source, AnimationInfo target)
    {
        Array.Copy(source.Index, target.Index, 2); Array.Copy(source.PrevIndex, target.PrevIndex, 2);
        Array.Copy(source.Frame, target.Frame, 2); Array.Copy(source.FrameCount, target.FrameCount, 2);
        Array.Copy(source.Flags, target.Flags, 2); Array.Copy(source.Step, target.Step, 2);
        target.Node.Slot = source.Node.Slot; target.Node.Group = source.Node.Group;
        target.Material.Slot = source.Material.Slot; target.Material.Group = source.Material.Group;
        target.Texture.Slot = source.Texture.Slot; target.Texture.Group = source.Texture.Group;
        target.Texcoord.Slot = source.Texcoord.Slot; target.Texcoord.Group = source.Texcoord.Group;
    }

    private static void Refresh(Model source, Model target)
    {
        for (int i = 0; i < source.Nodes.Count; i++)
        {
            Node a = source.Nodes[i], b = target.Nodes[i];
            b.Enabled = a.Enabled; b.Scale = a.Scale; b.Angle = a.Angle; b.Position = a.Position;
            b.AnimIgnoreParent = a.AnimIgnoreParent; b.AnimIgnoreChild = a.AnimIgnoreChild;
            b.BeforeTransform = a.BeforeTransform; b.AfterTransform = a.AfterTransform;
            b.Transform = a.Transform; b.Animation = a.Animation;
        }
        for (int i = 0; i < source.Materials.Count; i++)
        {
            Material a = source.Materials[i], b = target.Materials[i];
            b.Lighting = a.Lighting; b.Culling = a.Culling; b.Alpha = a.Alpha; b.Wireframe = a.Wireframe;
            b.Diffuse = a.Diffuse; b.Ambient = a.Ambient; b.PolygonMode = a.PolygonMode;
            b.RenderMode = a.RenderMode; b.AnimationFlags = a.AnimationFlags; b.TexgenMode = a.TexgenMode;
            b.TexcoordAnimationId = a.TexcoordAnimationId; b.MatrixId = a.MatrixId;
            b.CurrentAlpha = a.CurrentAlpha; b.CurrentDiffuse = a.CurrentDiffuse;
            b.CurrentAmbient = a.CurrentAmbient; b.CurrentSpecular = a.CurrentSpecular;
            b.CurrentTextureId = a.CurrentTextureId; b.CurrentPaletteId = a.CurrentPaletteId;
            b.TextureBindingId = a.TextureBindingId;
        }
        for (int i = 0; i < source.Meshes.Count; i++)
        {
            target.Meshes[i].ListId = source.Meshes[i].ListId;
            target.Meshes[i].Visible = source.Meshes[i].Visible;
        }
    }
}
