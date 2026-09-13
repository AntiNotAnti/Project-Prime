using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead.Cosmetics.Presentation;

/// <summary>
/// Model-reference-scoped semantic node cache. LOD/model replacement creates
/// a different runtime Model object even when its asset ID is shared, so
/// reference identity is the invalidation boundary.
/// </summary>
public sealed class CosmeticAnchorNodeCache
{
    // All MPH biped animation paths use the shared spine/limb contract
    // (PlayerBipedPose depends on it). Guardian is the one authored weapon
    // exception: its muzzle is Head_1 rather than R_elbow.
    private static readonly IReadOnlyDictionary<Hunter, string[]> _nodeNamesByHunter
        = new Dictionary<Hunter, string[]>
        {
            [Hunter.Samus] = Names("R_elbow"),
            [Hunter.Kanden] = Names("R_elbow"),
            [Hunter.Trace] = Names("R_elbow"),
            [Hunter.Sylux] = Names("R_elbow"),
            [Hunter.Noxus] = Names("R_elbow"),
            [Hunter.Spire] = Names("R_elbow"),
            [Hunter.Weavel] = Names("R_elbow"),
            [Hunter.Guardian] = Names("Head_1")
        };

    private readonly int[] _indices = new int[10];
    private object? _modelReference;
    private Hunter _hunter = (Hunter)byte.MaxValue;

    public int RefreshCount { get; private set; }

    public bool EnsureModel(Model model, Hunter hunter)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (ReferenceEquals(_modelReference, model) && _hunter == hunter) return false;
        string[] nodeNames = NamesFor(hunter);
        _modelReference = model;
        _hunter = hunter;
        _indices[0] = -1;
        for (int i = 1; i < _indices.Length; i++)
            _indices[i] = model.GetNodeIndexByName(nodeNames[i]);
        RefreshCount++;
        return true;
    }

    /// <summary>
    /// Adapter overload for tools/tests that expose model identity separately
    /// from node lookup. The live player path uses the allocation-free Model
    /// overload above.
    /// </summary>
    public bool EnsureModel(object modelReference, Hunter hunter,
        Func<string, int> lookup)
    {
        ArgumentNullException.ThrowIfNull(modelReference);
        ArgumentNullException.ThrowIfNull(lookup);
        if (ReferenceEquals(_modelReference, modelReference) && _hunter == hunter)
            return false;
        string[] nodeNames = NamesFor(hunter);
        _modelReference = modelReference;
        _hunter = hunter;
        _indices[0] = -1;
        for (int i = 1; i < _indices.Length; i++)
            _indices[i] = lookup(nodeNames[i]);
        RefreshCount++;
        return true;
    }

    public int Resolve(CosmeticAnchor anchor)
    {
        int index = (int)anchor;
        return (uint)index < (uint)_indices.Length ? _indices[index] : -1;
    }

    public bool TryResolveInterpolated(CosmeticAnchor anchor,
        Matrix4[]? interpolatedNodes, out Vector3 position)
    {
        int index = Resolve(anchor);
        if (interpolatedNodes != null && (uint)index < (uint)interpolatedNodes.Length)
        {
            position = interpolatedNodes[index].Row3.Xyz;
            return true;
        }
        position = default;
        return false;
    }

    public void Clear()
    {
        _modelReference = null;
        _hunter = (Hunter)byte.MaxValue;
        Array.Fill(_indices, -1);
    }

    private static string[] NamesFor(Hunter hunter)
        => _nodeNamesByHunter.TryGetValue(hunter, out string[]? names)
            ? names : throw new ArgumentOutOfRangeException(nameof(hunter));

    private static string[] Names(string weapon)
        => ["", "Head_1", "Spine_1", "L_shoulder", "R_shoulder",
            "L_elbow", "R_elbow", "L_ankle", "R_ankle", weapon];
}
