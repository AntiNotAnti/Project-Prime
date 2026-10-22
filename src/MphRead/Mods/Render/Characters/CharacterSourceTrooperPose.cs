using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render.Characters;

internal interface ICharacterSourceBodyPose
{
    bool Supports(PlayerAnimation lower, PlayerAnimation upper);
    void Reset();
    Matrix4[] Sample(PlayerAnimation lower, int lowerFrame, PlayerAnimation upper, int upperFrame, float aimY);
}

// Grounded Pirate mappings, RootPin, model-space torso layering and AimTurn
// adapted from SecondHunt 41452f2d053fcfa679e33c00a73d120a53f59e61,
// MIT, Copyright (c) 2020 NoneGiven (notice retained in repository LICENSE).
// This adapter owns presentation only; native gameplay supplies every state.
internal sealed class CharacterSourceTrooperPose : ICharacterSourceBodyPose
{
    internal static readonly string[] RequiredClips =
    ["B_ready_pirate", "B_runloop_pirate", "B_backward_pirate", "B_strafleft_pirate",
        "B_strafright_pirate", "A_standshot_pirate", "A_gunloop_pirate"];
    private readonly CharacterSourceRig _rig;
    private readonly IReadOnlyDictionary<string, CharacterSourceClip> _clips;
    private readonly int _spine;
    private readonly bool[] _torso;
    private Matrix4[]? _last, _from;
    private PlayerAnimation _lower, _upper;
    private int _lowerFrame = -1, _upperFrame = -1, _lowerTicks, _upperTicks, _fade;

    internal CharacterSourceTrooperPose(CharacterSourceRig rig, IReadOnlyDictionary<string, CharacterSourceClip> clips)
    {
        _rig = rig;
        _spine = Enumerable.Range(0, rig.Nodes.Count).Single(i => rig.Nodes[i].Name == "source_bone_30_Spine_1");
        if (rig.Nodes.Count != 36 || rig.JointNodes.Count != 35)
            throw new InvalidDataException("Complete Trooper source hierarchy required.");
        _torso = Enumerable.Range(0, rig.Nodes.Count).Select(i => Descendant(i, _spine)).ToArray();
        _clips = RequiredClips.ToDictionary(n => n, n => clips[n].WithPinnedTrooperRootMotion(), StringComparer.Ordinal);
    }
    private bool Descendant(int index, int parent)
    {
        for (int n = index; n >= 0; n = _rig.Nodes[n].Parent) if (n == parent) return true;
        return false;
    }
    private Matrix4[] Globals(IReadOnlyList<Matrix4> local)
    {
        var world = new Matrix4[local.Count]; var done = new bool[local.Count];
        Matrix4 Resolve(int n)
        {
            if (!done[n]) { world[n] = _rig.Nodes[n].Parent < 0 ? local[n] : local[n] * Resolve(_rig.Nodes[n].Parent); done[n] = true; }
            return world[n];
        }
        for (int n = 0; n < local.Count; n++) Resolve(n);
        return world;
    }
    internal static string? LowerClip(PlayerAnimation state) => state switch
    {
        PlayerAnimation.Idle or PlayerAnimation.Turn or PlayerAnimation.Shoot
            or PlayerAnimation.Charge or PlayerAnimation.ChargeShoot => RequiredClips[0],
        PlayerAnimation.WalkForward => RequiredClips[1], PlayerAnimation.WalkBackward => RequiredClips[2],
        PlayerAnimation.WalkLeft => RequiredClips[3], PlayerAnimation.WalkRight => RequiredClips[4], _ => null
    };
    internal static string? UpperClip(PlayerAnimation state) => state switch
    {
        PlayerAnimation.Shoot or PlayerAnimation.ChargeShoot => RequiredClips[5],
        PlayerAnimation.Charge => RequiredClips[6], _ => null
    };
    internal static bool Supports(PlayerAnimation lower, PlayerAnimation upper)
        => LowerClip(lower) != null && (UpperClip(upper) != null || LowerClip(upper) != null);
    bool ICharacterSourceBodyPose.Supports(PlayerAnimation lower, PlayerAnimation upper) => Supports(lower, upper);
    public void Reset() { _last = _from = null; _lowerFrame = _upperFrame = -1; _lowerTicks = _upperTicks = _fade = 0; }
    public Matrix4[] Sample(PlayerAnimation lower, int lowerFrame, PlayerAnimation upper, int upperFrame, float aimY)
    {
        if (!Supports(lower, upper) || !float.IsFinite(aimY) || lowerFrame < 0 || upperFrame < 0)
            throw new InvalidDataException("Unsupported Trooper body pose.");
        bool lowerChanged = _lowerFrame < 0 || lower != _lower || lowerFrame < _lowerFrame;
        bool upperChanged = _upperFrame < 0 || upper != _upper || upperFrame < _upperFrame;
        if (lowerChanged) _lowerTicks = 0;
        if (upperChanged) _upperTicks = 0;
        if (lowerChanged || upperChanged) { _from = _last?.ToArray(); _fade = 0; }
        (_lower, _upper, _lowerFrame, _upperFrame) = (lower, upper, lowerFrame, upperFrame);
        var lowerClip = _clips[LowerClip(lower)!];
        var pose = lowerClip.Sample((_lowerTicks++ / 60d) % lowerClip.Duration).ToArray();
        if (UpperClip(upper) is { } name)
        {
            var clip = _clips[name];
            var torso = clip.Sample((_upperTicks++ / 60d) % clip.Duration);
            var lowerWorld = Globals(pose); var upperWorld = Globals(torso);
            // Retain the lower body's spine pivot, replace its model orientation.
            // Descendants then follow the upper clip's local transforms.
            Matrix4 spine = upperWorld[_spine].ClearTranslation();
            spine.Row3 = lowerWorld[_spine].Row3;
            int parent = _rig.Nodes[_spine].Parent;
            pose[_spine] = parent < 0 ? spine : spine * lowerWorld[parent].Inverted();
            for (int i = 0; i < pose.Length; i++) if (_torso[i] && i != _spine) pose[i] = torso[i];
        }
        if (_from != null && _fade < 6)
        {
            float weight = ++_fade / 6f;
            for (int i = 0; i < pose.Length; i++)
            {
                var mixed = Matrix4.CreateFromQuaternion(Quaternion.Slerp(_from[i].ExtractRotation(), pose[i].ExtractRotation(), weight));
                mixed.Row3.Xyz = Vector3.Lerp(_from[i].Row3.Xyz, pose[i].Row3.Xyz, weight);
                pose[i] = mixed;
            }
        }
        // Keep transition history before aim, so each tick applies pitch once.
        _last = pose.ToArray();
        var world = Globals(pose);
        float pitch = MathF.Asin(Math.Clamp(aimY, -Fixed.ToFloat(2896), Fixed.ToFloat(2896)));
        Matrix4 aimed = world[_spine].ClearTranslation() * Matrix4.CreateRotationX(-pitch);
        aimed.Row3 = world[_spine].Row3;
        int spineParent = _rig.Nodes[_spine].Parent;
        pose[_spine] = spineParent < 0 ? aimed : aimed * world[spineParent].Inverted();
        foreach (var matrix in pose) CharacterSourceRig.RequireRigid(matrix);
        return pose;
    }
}
