using System;
using MphRead.Mods;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private CharacterSourceBodyOwner? _sourceBody;
    private long _sourceBodyLife;
    internal CharacterSourceBodyOwner? ModSourceBody => _sourceBody;

    internal void ModBindSourceBody(CharacterSourcePreviewSelection selection)
    {
        ModRetireSourceBody();
        _sourceBody = new(_scene, this, selection);
    }
    internal void ModRetireSourceBody() { _sourceBody?.Dispose(); _sourceBody = null; }
    internal void ModResetSourceBodyLife() { unchecked { _sourceBodyLife++; } }

    private bool SourceBodyStateSupported => Health > 0 && !IsAltForm && !IsMorphing && !IsUnmorphing
        && !ModCrouched && !ModTucked && ModCrouchVisualAmount == 0
        && _frozenTimer == 0 && _frozenGfxTimer == 0 && _burnTimer == 0
        && _doubleDmgTimer == 0 && _disruptedTimer == 0 && _shockCoilTimer == 0
        && !Flags2.TestFlag(PlayerFlags2.Cloaking) && _curAlpha >= 1
        && !BrightSkinStatusOverride && !BrightSkinFrozenOverlay && !ModMatchSpawnProtectionActive
        && _timeSinceDamage >= Values.DamageFlashTime * 2 && !_scene.GameState.Teams
        && !BrightSkins.ShouldApply(this) && PaletteOverride == null
        && _scene.CameraSequences.Current == null && !_scene.Services.IsReplica
        && !Mods.Network.NetSession.Active && !Mods.Network.DemoPlayback.IsActive
        && !Mods.KillCam.Active;

    internal void ModStepSourceBody()
    {
        if (_sourceBody == null) return;
        _sourceBody.Step(_scene.FrameCount, _sourceBodyLife, Biped1Anim, _bipedModel1.AnimInfo.Frame[0],
            Biped2Anim, _bipedModel2.AnimInfo.Frame[0], _facingVector.Y, SourceBodyStateSupported);
    }

    private bool ModTryDrawSourceBody(Vector3 drawPosition, Vector3 drawFacing, float alpha,
        Vector4? overrideColor, Vector4? outlineColor, int lod)
    {
        if (_sourceBody is not { } owner || !SourceBodyStateSupported || alpha != 1 || lod != 0
            || overrideColor != null || outlineColor != null || !RenderOptions.ShowCustomCosmetics) return false;
        // Feet and height follow the native actor. This positive-scale source
        // basis is distinct from the cartridge model's reversed body basis.
        Vector3 forward = new(drawFacing.X, 0, drawFacing.Z);
        if (forward.LengthSquared < .000001f) return false;
        forward.Normalize();
        var rotation = Matrix4.Identity;
        rotation.Row0.Xyz = Vector3.Cross(Vector3.UnitY, forward);
        rotation.Row2.Xyz = forward;
        rotation.Row3.Xyz = drawPosition.AddY(Fixed.ToFloat(Values.MinPickupHeight) - .03f);
        var model = owner.Model;
        Matrix4 transform = owner.FeetFit * rotation;
        if (!owner.Prepare(transform)) return false;
        int polygonId = _scene.GetNextPolygonId();
        var previous = _scene.SourceGxSubmission;
        try
        {
            foreach (var segment in model.Segments)
            {
                _scene.SourceGxSubmission = new(segment.SourceGx!, owner.MaterialSeconds, owner);
                _scene.AddRenderItem(_bipedModel2.Model.Materials[segment.NativeMaterialIndex], polygonId, 1,
                    Vector3.Zero, GetLightInfo(), Matrix4.Identity, transform, segment.ListId,
                    segment.SourceJointPalette!.Count, segment.SourcePresentationPalette!,
                    null, null, SelectionType.None, BillboardMode.None, 1, segment.AlbedoBinding,
                    weightedSkinning: true, authoredTexture: true, authoredWrapS: segment.WrapS,
                    authoredWrapT: segment.WrapT, authoredDoubleSided: true,
                    authoredTransparent: segment.SourceGx!.Program.DrawPass != CharacterSourceGxProgram.Pass.Opaque);
            }
        }
        finally { _scene.SourceGxSubmission = previous; }
        return true;
    }
}
