using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private readonly DisplayPoseHistory[] _kandenDrawSegments = new DisplayPoseHistory[5];
    private DisplayPoseLifetime _specializedDrawLifetime;
    private DisplayPoseHistory _altDrawRotation, _spireAttackDrawRotation;
    private bool _spireDrawAttackFlag;

    internal void ModCaptureSpecializedDrawState(in DisplayPoseLifetime lifetime, bool reset)
    {
        _specializedDrawLifetime = lifetime;
        Matrix4 rotation = _modelTransform;
        rotation.Row3.Xyz = Vector3.Zero;
        Matrix4 attack = Hunter == Hunter.Spire && IsAltForm
            ? GetTransformMatrix(_spireAltFacing, _spireAltUp) : Matrix4.Identity;
        bool attacking = Flags2.TestFlag(PlayerFlags2.AltAttack);
        if (reset)
        {
            _altDrawRotation.Reset(rotation, lifetime, _scene.FrameCount);
            _spireAttackDrawRotation.Reset(attack, lifetime, _scene.FrameCount);
        }
        else
        {
            _altDrawRotation.Capture(rotation, lifetime, _scene.FrameCount);
            if (attacking != _spireDrawAttackFlag)
                _spireAttackDrawRotation.Reset(attack, lifetime, _scene.FrameCount);
            else
                _spireAttackDrawRotation.Capture(attack, lifetime, _scene.FrameCount);
        }
        _spireDrawAttackFlag = attacking;
        for (int i = 0; i < _kandenDrawSegments.Length; i++)
        {
            Matrix4 local = _kandenSegMtx[i];
            local.Row3.Xyz -= Position;
            if (reset) _kandenDrawSegments[i].Reset(local, lifetime, _scene.FrameCount);
            else _kandenDrawSegments[i].Capture(local, lifetime, _scene.FrameCount);
        }
    }

    private bool CanSampleSpecializedPose(out double alpha)
    {
        alpha = 1;
        return HunterAnimationBlendEnabled && !_scene.Services.IsReplica && !NetSession.Active && FrameTiming.Active
            && FrameTiming.HighRefreshPresentation && _frozenTimer == 0 && IsAltForm
            && ModTrySampleCapturedDrawTransform(out _, out alpha);
    }

    internal Matrix4 ModGetAlternateFormDrawPose(Vector3 drawPosition, out bool sampled)
    {
        Matrix4 rotation = _modelTransform;
        rotation.Row3.Xyz = Vector3.Zero;
        sampled = CanSampleSpecializedPose(out double alpha)
            && _altDrawRotation.TryGetTransform(rotation, _specializedDrawLifetime,
                _scene.FrameCount, alpha, out rotation);
        rotation.Row3.Xyz = drawPosition;
        return rotation;
    }

    private Matrix4 SpireAttackDrawRotation()
    {
        Matrix4 rotation = GetTransformMatrix(_spireAltFacing, _spireAltUp);
        if (Flags2.TestFlag(PlayerFlags2.AltAttack) == _spireDrawAttackFlag
            && CanSampleSpecializedPose(out double alpha))
            _spireAttackDrawRotation.TryGetTransform(rotation, _specializedDrawLifetime,
                _scene.FrameCount, alpha, out rotation);
        return rotation;
    }

    internal Matrix4 ModGetKandenSegmentDrawPose(int index, Vector3 drawPosition, out bool sampled)
    {
        Matrix4 canonical = _kandenSegMtx[index];
        Matrix4 local = canonical;
        local.Row3.Xyz -= Position;
        sampled = false;
        // Existing live network/replay owners retain their current specialized
        // policy. Offline segment shape uses the completed actor lifetime and
        // relative pose, so the local actor's position/aim stays responsive.
        if (CanSampleSpecializedPose(out double alpha))
            sampled = _kandenDrawSegments[index].TryGetTransform(local, _specializedDrawLifetime,
                _scene.FrameCount, alpha, out local);
        if (sampled)
        {
            local.Row3.Xyz += drawPosition;
            return local;
        }
        Vector3 delta = drawPosition - Position;
        if (delta != Vector3.Zero) canonical.Row3.Xyz += delta;
        return canonical;
    }
}
