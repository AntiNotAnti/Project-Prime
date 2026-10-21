using System;
using System.Collections.Generic;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    private static readonly bool DetachedShadowCastersEnabled =
        OpenGlOptimizationPolicy.Requested("DYNAMIC_SHADOW_CASTERS") && !OpenGlOptimizationPolicy.ForceLegacy;
    internal const int MaxDetachedShadowPackets = 2048;
    private readonly DetachedShadowPoseCache _detachedShadowPoses = new();
    private readonly List<RenderItem> _detachedShadowItems = new();
    private long _detachedShadowCollection, _detachedShadowDraws, _detachedShadowGeneration;
    private bool _collectingDetachedShadows, _detachedShadowBoundsKnown, _detachedShadowOverflow;
    private Vector3 _detachedShadowMin, _detachedShadowMax;
    internal bool CanPrepareDetachedShadows => DetachedShadowCastersEnabled
        && Mods.RenderOptions.Shadows != Mods.ShadowQuality.Off && !this.GameState.MenuPause
        && _detachedShadowCollection > 0;
    internal long DetachedShadowDraws => _detachedShadowDraws;
    internal bool DetachedShadowCapacityAvailable => !_collectingDetachedShadows
        || _detachedShadowItems.Count < MaxDetachedShadowPackets;

    private void BeginDetachedShadowCollection()
    {
        _detachedShadowItems.Clear(); _collectingDetachedShadows = false;
        if (_detachedShadowGeneration != FrameTiming.PresentationGeneration)
        {
            _detachedShadowGeneration = FrameTiming.PresentationGeneration;
            _detachedShadowPoses.Clear();
        }
        if (++_detachedShadowCollection <= 0) { _detachedShadowCollection = 1; _detachedShadowPoses.Clear(); }
        if (!CanPrepareDetachedShadows) _detachedShadowPoses.Clear();
    }

    internal bool TryPrepareDetachedShadow(object owner, ModelInstance source,
        out DetachedShadowPoseCache.Pose pose)
    {
        pose = null!;
        if (!CanPrepareDetachedShadows || _detachedShadowItems.Count >= MaxDetachedShadowPackets) return false;
        bool prepared = _detachedShadowPoses.TryPrepare(owner,source,_detachedShadowCollection,out pose);
        if (!prepared && !_detachedShadowOverflow)
        {
            _detachedShadowOverflow = true;
            Console.WriteLine("[render] detached shadow pose unavailable/duplicate or bounded cache full; visible caster path remains active.");
        }
        return prepared;
    }

    internal readonly struct DetachedShadowSubmission : IDisposable
    {
        private readonly Scene _scene;
        internal DetachedShadowSubmission(Scene scene) => _scene = scene;
        public void Dispose() => _scene._collectingDetachedShadows = false;
    }
    internal DetachedShadowSubmission BeginDetachedShadowSubmission(DetachedShadowPoseCache.Pose pose)
    {
        if (_collectingDetachedShadows) throw new InvalidOperationException("Nested detached shadow submission.");
        _collectingDetachedShadows = true; _detachedShadowBoundsKnown = pose.BoundsKnown;
        _detachedShadowMin = pose.Min; _detachedShadowMax = pose.Max;
        return new(this);
    }
    internal void UnknownDetachedShadowBounds() => _detachedShadowBoundsKnown = false;

    private void SubmitDetachedShadow(RenderItem item, bool pooled)
    {
        item.ViewModel = false;
        if (_detachedShadowItems.Count < MaxDetachedShadowPackets
            && item.Type == RenderItemType.Mesh && item.SourceGx == null
            && item.Alpha >= .999f && item.RenderMode != RenderMode.Decal
            && item.BillboardMode == BillboardMode.None && !item.Wireframe)
        {
            item.RetainedGpuVisibilityEligible = _detachedShadowBoundsKnown;
            item.RetainedBoundsMin = _detachedShadowMin; item.RetainedBoundsMax = _detachedShadowMax;
            _detachedShadowItems.Add(item);
            if (pooled) _usedRenderItems.Enqueue(item);
        }
        else
        {
            if (_detachedShadowItems.Count >= MaxDetachedShadowPackets && !_detachedShadowOverflow)
            {
                _detachedShadowOverflow = true;
                Console.WriteLine("[render] detached shadow packet budget reached; visible caster path remains active.");
            }
            if (pooled) _freeRenderItems.Enqueue(item);
        }
    }

    internal void UpdateDetachedShadowMaterials(Model source, Model pose, int recolor)
    {
        if (Mods.Headless.Active) return;
        for (int i = 0; i < pose.Materials.Count; i++)
        {
            Material material = pose.Materials[i];
            if (material.CurrentTextureId == -1) continue;
            var (binding, opaque) = _texPalMap[source.Id].GetForMaterial(i,
                material.CurrentTextureId,material.CurrentPaletteId,recolor);
            material.TextureBindingId = binding; UpdateMaterial(material,opaque);
        }
    }

    private void ReleaseDetachedShadowCasters()
    {
        _detachedShadowItems.Clear(); _detachedShadowPoses.Clear();
        _detachedShadowCollection = 0; _collectingDetachedShadows = _detachedShadowOverflow = false;
    }
}
