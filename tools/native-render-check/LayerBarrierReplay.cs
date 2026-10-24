using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

sealed partial class CheckWindow
{
    private bool _layerBarrier, _layerCrossed;
    private Vector3 _layerNormal;
    private float _layerPlane, _layerStartSide, _layerClosest3D = float.MaxValue;
    private int _layerSupportedFrames, _layerMaximumSupportedFrames;
    private float _layerMinimumOriginalSide = float.MaxValue;
    private Vector3[]? _layerFootprint;
    private BuiltFace? _layerFace;
    private Func<BuiltFace, Vector3, bool>? _layerAccepts;

    private void ConfigureLayerBarrier(string[] args, MapDefinition definition)
    {
        if (!args.Contains("-movement-layer-barrier")) return;
        if (!_movementPackageRoom || !_movementRequireClearance || !_movementNonWalkable
            || _movementJumpTrial || _movementWall || _movementAlt || _movementInputUntil.HasValue)
            throw new ArgumentException("Layer barrier requires unchanged packaged nonwalking biped input and strict clearance.");
        var current = MapCompiler.Compile(definition);
        if (!current.Validation.IsValid || current.Map?.AltFormCertification is not { Passed: true })
            throw new InvalidDataException("Layer barrier requires a fresh passing package compilation.");
        var factory = typeof(MapAltFormCertification).GetMethod("CreateFloorLayerBarrierProbe", BindingFlags.NonPublic | BindingFlags.Static)!;
        Func<Vector3, Vector3, bool> Probe(BuiltFace[] faces) => (Func<Vector3, Vector3, bool>)factory.Invoke(null,
            new object[] { faces, CancellationToken.None })!;
        if (!Probe(current.Map.Solid.ToArray())(_movementStart, _movementGoal))
            throw new InvalidDataException("Current production finite-floor probe does not separate these exact endpoints.");
        var parts = typeof(MapPacker).GetMethod("CollisionParts", BindingFlags.Static | BindingFlags.NonPublic)!;
        var floor = current.Map.Solid.SelectMany(f => (IEnumerable<BuiltFace>)parts.Invoke(null, new object[] { f })!)
            .First(f => Probe(new[] { f })(_movementStart, _movementGoal));
        _layerNormal = floor.Normal.Normalized(); _layerPlane = Vector3.Dot(_layerNormal, floor.Points[0]);
        _layerStartSide = Vector3.Dot(_layerNormal, _movementStart) - _layerPlane;
        _layerFace = floor;
        _layerAccepts = typeof(MapAltFormCertification).GetMethod("Accepts", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<BuiltFace, Vector3, bool>>();
        _layerFootprint = floor.Points.ToArray(); _layerBarrier = true;
    }

    private void ObserveLayerBarrier(PlayerEntity player, bool standing)
    {
        if (!_layerBarrier) return;
        float originalSide = MathF.Sign(_layerStartSide) * (Vector3.Dot(_layerNormal, player.Position) - _layerPlane);
        var projected = new Vector3(player.Position.X,
            (_layerPlane - _layerNormal.X * player.Position.X - _layerNormal.Z * player.Position.Z) / _layerNormal.Y, player.Position.Z);
        bool inside = _layerAccepts!(_layerFace!, projected);
        if (inside) _layerMinimumOriginalSide = Math.Min(_layerMinimumOriginalSide, originalSide);
        _layerCrossed |= inside && originalSide < -1f / 4096;
        _layerClosest3D = Math.Min(_layerClosest3D, (player.Position - _movementGoal).Length);
        // The reverse approach may reach the requested X/Z on the upper deck.
        // Require sustained real support there, without reaching the lower goal.
        bool supported = inside && standing && !player.IsAltForm && !player.IsMorphing && !player.IsUnmorphing
            && (player.Position - _movementGoal).Xz.Length < .4f && originalSide >= 0;
        _layerSupportedFrames = supported ? _layerSupportedFrames + 1 : 0;
        _layerMaximumSupportedFrames = Math.Max(_layerMaximumSupportedFrames, _layerSupportedFrames);
    }

    private bool LayerBarrierAccepted => !_layerCrossed && _layerClosest3D >= .4f
        && _movementArrived == 0 && _movementInputFrames > 0 && _layerMaximumSupportedFrames >= 60
        && _movementClosest + .5f < (_movementStart - _movementGoal).Xz.Length;

    private object? LayerBarrierResult() => !_layerBarrier ? null : new
    {
        scope = "Separate finite-floor layer control; horizontal wall acceptance remains unchanged.",
        finiteFloorProbeVerified = true,
        normal = new[] { _layerNormal.X, _layerNormal.Y, _layerNormal.Z }, plane = _layerPlane,
        footprint = _layerFootprint!.Select(v => new[] { v.X, v.Y, v.Z }).ToArray(), startSide = _layerStartSide,
        minimumOriginalSide = _layerMinimumOriginalSide, crossedLayer = _layerCrossed,
        closestGoal3DDistance = _layerClosest3D, supportedOnOriginalLayerFrames = _layerMaximumSupportedFrames
    };
}
