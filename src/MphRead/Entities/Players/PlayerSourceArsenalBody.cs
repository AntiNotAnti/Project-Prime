using System;
using MphRead.Mods;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private CharacterSourceArsenalBodyOwner? _sourceArsenalBody;
    internal CharacterSourceArsenalBodyOwner? ModSourceArsenalBody => _sourceArsenalBody;
    internal void ModBindSourceArsenalBody(CharacterSourcePreviewSelection selection, int bots)
        => ModBindSourceArsenalPresentation(selection, null, bots);
    internal void ModBindSourceArsenalPresentation(CharacterSourcePreviewSelection? body,
        CharacterSourceArsenalDroneSelection? drone, int bots)
    {
        ModRetireSourceBody();
        try
        {
            if (body != null) _sourceArsenalBody = new(_scene, this, body, bots);
            if (drone != null) _sourceArsenalDrone = new(_scene, this, drone, bots);
        }
        catch { ModRetireSourceBody(); throw; }
    }
    private void ModRetireSourceArsenalBody()
    { _sourceArsenalBody?.Dispose(); _sourceArsenalBody = null; }
    private void ModStepSourceArsenalBody()
    {
        _sourceArsenalBody?.Step(_scene.FrameCount, _sourceBodyLife, Biped1Anim, _bipedModel1.AnimInfo.Frame[0],
            Biped2Anim, _bipedModel2.AnimInfo.Frame[0], _facingVector.Y, SourceBodyStateSupported,
            SourceBodyStateSupported && SourceCannonAnimationSupported(GunAnimation));
    }
    private bool ModTryDrawSourceArsenalBody(Vector3 drawPosition, Vector3 drawFacing, float alpha,
        Vector4? overrideColor, Vector4? outlineColor, int lod)
    {
        if (_sourceArsenalBody is not { } owner || !SourceBodyStateSupported || alpha != 1 || lod != 0
            || overrideColor != null || outlineColor != null || !RenderOptions.ShowCustomCosmetics) return false;
        Vector3 forward = new(drawFacing.X, 0, drawFacing.Z);
        if (forward.LengthSquared < .000001f) return false;
        forward.Normalize();
        var rotation = Matrix4.Identity;
        rotation.Row0.Xyz = Vector3.Cross(Vector3.UnitY, forward);
        rotation.Row2.Xyz = forward;
        rotation.Row3.Xyz = drawPosition.AddY(Fixed.ToFloat(Values.MinPickupHeight) - .03f);
        Matrix4 transform = owner.FeetFit * rotation;
        if (!owner.Prepare(transform)) return false;
        SubmitArsenalSource(owner.Model, owner.MaterialSeconds, owner, transform, _bipedModel2.Model, viewModel: false);
        return true;
    }
    private bool ModTryDrawSourceArsenalCannon(Matrix4 gun)
    {
        if (_sourceArsenalBody is not { } owner || !SourceBodyStateSupported || !RenderOptions.ShowCustomCosmetics
            || _curAlpha != 1 || !SourceCannonAnimationSupported(GunAnimation)
            || !ModGetFirstPersonGunTransform(out _) || !ModGetFirstPersonEffectTransform(out _)
            || !owner.PrepareCannon(gun, Fixed.ToFloat(Values.MuzzleOffset), out var presentation)) return false;
        // Selected() on the captured native Blaster owns this choice. The native
        // Power Beam enum can remain selected while custom fire suppresses it.
        SubmitArsenalSource(owner.CannonModel, owner.CannonMaterialSeconds, owner.ViewOwner,
            owner.CannonModel.SourceCannon!.ToCannon * presentation, _gunModel.Model, viewModel: true);
        return true;
    }
    private void SubmitArsenalSource(CharacterWeightedRenderModel model, double seconds,
        ICharacterSourcePresentationOwner owner, Matrix4 transform, Model native, bool viewModel)
    {
        int polygonId = _scene.GetNextPolygonId();
        var previous = _scene.SourceGxSubmission;
        try
        {
            foreach (var segment in model.Segments)
            {
                _scene.SourceGxSubmission = new(segment.SourceGx!, seconds, owner, ViewModel: viewModel);
                _scene.AddRenderItem(native.Materials[segment.NativeMaterialIndex], polygonId, 1,
                    Vector3.Zero, GetLightInfo(), Matrix4.Identity, transform, segment.ListId,
                    segment.SourceJointPalette!.Count, segment.SourcePresentationPalette!, null, null,
                    SelectionType.None, BillboardMode.None, 1, segment.AlbedoBinding,
                    weightedSkinning: true, authoredTexture: true, authoredWrapS: segment.WrapS,
                    authoredWrapT: segment.WrapT, authoredDoubleSided: true,
                    authoredTransparent: segment.SourceGx!.Program.DrawPass != CharacterSourceGxProgram.Pass.Opaque);
            }
        }
        finally { _scene.SourceGxSubmission = previous; }
    }
}
