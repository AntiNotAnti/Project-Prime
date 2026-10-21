using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Launcher;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render.Characters;

// A single initialized actor owns its mutable pose/palettes. Import/proof/clip IO
// happens only at the explicit match load barrier, never from Step or Draw.
internal sealed class CharacterSourceBodyOwner : IDisposable, ICharacterSourcePresentationOwner
{
    private readonly Scene _scene;
    private readonly PlayerEntity _player;
    private readonly Model _native;
    private readonly CharacterSourcePreviewSelection _selection;
    private readonly CharacterSourcePreviewInstance _instance;
    private readonly CharacterWeightedRenderModel _model;
    private readonly ICharacterSourceBodyPose _pose;
    private readonly Model? _nativeCannon;
    private readonly CharacterSourcePreviewInstance? _cannonInstance;
    private readonly CharacterWeightedRenderModel? _cannonModel;
    private readonly Model? _nativeBall;
    private readonly CharacterSourcePreviewInstance? _ballInstance;
    private readonly CharacterWeightedRenderModel? _ballModel;
    internal Matrix4 BallFit { get; }
    internal CharacterWeightedRenderModel? BallModel => _ballModel;
    internal bool BallReady { get; private set; }
    private long _ballTicks;
    internal long BallCompletedSteps => _ballTicks;
    internal double BallMaterialSeconds => _ballTicks / 60d;
    internal Matrix4 FeetFit { get; }
    private readonly int _slot;
    private readonly long _sceneGeneration;
    private bool _disposed;
    private ulong? _step;
    private long _life = -1;
    private long _ticks;
    internal bool Ready { get; private set; }
    internal double MaterialSeconds => _ticks / 60d;
    internal string Generation => _selection.Generation;
    internal CharacterWeightedRenderModel Model => _model;
    internal CharacterWeightedRenderModel? CannonModel => _cannonModel;

    internal CharacterSourceBodyOwner(Scene scene, PlayerEntity player, CharacterSourcePreviewSelection selection)
    {
        if (!player.Initialized || player.Hunter != selection.NativeHunter || player.IsBot || scene.Exiting
            || scene.SideScene || scene.IsReplayLab || scene.Services.IsReplica || scene.GameState.SinglePlayer
            || player.ModCharacterGameplay.Variant != Multiplayer.CharacterGameplayVariant.Native)
            throw new InvalidOperationException("An initialized matching local offline actor is required for an imported body.");
        (_scene, _player, _native, _selection) = (scene, player, player.BipedModel2.Model, selection);
        _slot = player.SlotIndex;
        _sceneGeneration = scene.LifetimeGeneration;
        string anchor = _native.Materials.GroupBy(m => m.Name, StringComparer.Ordinal)
            .Where(g => g.Count() == 1).Select(g => g.Single())
            .First(m => CharacterModelRuntime.CanUseAuthoredTexcoords(m.TexgenMode, true)).Name;
        _instance = new(anchor, bodyOwner: this);
        try
        {
            if (!CharacterModelRuntime.TryGetSourcePreview(scene, player.Hunter, CharacterModelPart.Biped,
                _native, selection.Pack, selection.SelectedAppearance, _instance, true, out _model))
                throw new InvalidDataException("The selected imported body could not bind to the current Scene.");
            var clips = new Dictionary<string, CharacterSourceClip>(StringComparer.Ordinal);
            foreach (string name in selection.NativeHunter == Hunter.Weavel
                ? CharacterSourceTrooperPose.RequiredClips : CharacterSourceBodyPose.RequiredClips)
            {
                var choices = selection.Clips.Where(c => c.Name == name).ToArray();
                if (choices.Length != 1 || !CharacterModelRuntime.TryLoadSourceClip(scene, _instance, _model,
                    choices[0].Ordinal, out var clip)) throw new InvalidDataException("Missing strict body clip: " + name);
                clips.Add(name, clip);
            }
            if (_model.Segments.Count == 0 || _model.Segments.Any(s => s.SourceGx == null
                || s.SourceJointPalette is not { Count: > 0 and <= 32 } || s.SourcePresentationPalette == null))
                throw new InvalidDataException("Complete source body programs and palettes are required.");
            _pose = selection.NativeHunter == Hunter.Weavel
                ? new CharacterSourceTrooperPose(_model.SourceRig!, clips) : new CharacterSourceBodyPose(_model.SourceRig!, clips);
            FeetFit = _model.FitSourceBodyToFeet(_pose.Sample(PlayerAnimation.Idle, 0, PlayerAnimation.Idle, 0, 0),
                Fixed.ToFloat(player.Values.MaxPickupHeight - player.Values.MinPickupHeight));
            _pose.Reset();
            if (selection.RuntimeCannon)
            {
                _nativeCannon = player.ModSourceNativeGun;
                // Native flash/glow anchors may have zero alpha. The source
                // program owns appearance; use one stable opaque UV anchor.
                string cannonAnchor = _nativeCannon.Materials.GroupBy(m => m.Name, StringComparer.Ordinal)
                    .Where(g => g.Count() == 1).Select(g => g.Single())
                    .First(m => m.PolygonMode == PolygonMode.Modulate && m.RenderMode == RenderMode.Normal
                        && CharacterModelRuntime.CanUseAuthoredTexcoords(m.TexgenMode, true)).Name;
                _cannonInstance = new(cannonAnchor, CharacterModelPart.ViewModel, bodyOwner: this);
                if (!CharacterModelRuntime.TryGetSourcePreview(scene, player.Hunter, CharacterModelPart.ViewModel,
                    _nativeCannon, selection.Pack, selection.SelectedAppearance, _cannonInstance, true, out _cannonModel)
                    || _cannonModel.SourceCannon?.IsGenerated != true || _cannonModel.IndexCount != _cannonModel.SourceCannon.TriangleCount * 3
                    || _cannonModel.Segments.Count == 0 || _cannonModel.Segments.Any(s => s.SourceGx == null
                        || s.SourceJointPalette is not { Count: > 0 and <= 32 } || s.SourcePresentationPalette == null))
                    throw new InvalidDataException("The selected source cannon could not bind completely.");
            }
            if (selection.RuntimeBall)
            {
                _nativeBall=player.ModSourceNativeBall;
                string ballAnchor=_nativeBall.Materials.GroupBy(m=>m.Name,StringComparer.Ordinal).Where(g=>g.Count()==1)
                    .Select(g=>g.Single()).First(m=>CharacterModelRuntime.CanUseAuthoredTexcoords(m.TexgenMode,true)).Name;
                _ballInstance=new(ballAnchor,CharacterModelPart.AlternateForm,bodyOwner:this);
                if (!CharacterModelRuntime.TryGetSourcePreview(scene,player.Hunter,CharacterModelPart.AlternateForm,
                    _nativeBall,selection.Pack,selection.SelectedAppearance,_ballInstance,true,out _ballModel)
                    || _ballModel.Joints.Count!=2 || _ballModel.IndexCount!=(_ballModel.IsFinalSourceBall?8170:2220)*3 || _ballModel.Segments.Count==0
                    || _ballModel.Segments.Any(s=>s.SourceGx==null||s.SourceJointPalette is not {Count:>0 and <=32})
                    || !CharacterModelRuntime.TryLoadSourceClip(scene,_ballInstance,_ballModel,CharacterGeneratedNormalBallProfile.ClipOrdinal,out var ready))
                    throw new InvalidDataException("The selected source ball could not bind completely.");
                var pose=ready.Sample(0);_ballModel.UpdateSourcePreviewPalette(pose);
                BallFit=_ballModel.FitSourceBall(pose,Fixed.ToFloat(player.Values.AltColRadius));
            }
        }
        catch
        {
            if (_ballInstance != null) CharacterModelRuntime.ReleaseSourcePreview(scene,_ballInstance);
            if (_cannonInstance != null) CharacterModelRuntime.ReleaseSourcePreview(scene, _cannonInstance);
            CharacterModelRuntime.ReleaseSourcePreview(scene, _instance); throw;
        }
    }

    internal bool Owns(Scene scene, Model native, CharacterModelPack pack, PrivateCharacterAppearance appearance,
        CharacterSourcePreviewInstance instance)
        => !_disposed && !_scene.Exiting && !_scene.SideScene && !_scene.IsReplayLab
            && _scene.LifetimeGeneration == _sceneGeneration && ReferenceEquals(scene, _scene)
            && ReferenceEquals(pack, _selection.Pack) && appearance == _selection.SelectedAppearance
            && ReferenceEquals(_player.BipedModel2.Model, _native)
            && (ReferenceEquals(instance, _instance) && ReferenceEquals(native, _native)
                || _cannonInstance != null && ReferenceEquals(instance, _cannonInstance)
                    && ReferenceEquals(native, _nativeCannon) && ReferenceEquals(_player.ModSourceNativeGun, _nativeCannon)
                || _ballInstance != null && ReferenceEquals(instance,_ballInstance)
                    && ReferenceEquals(native,_nativeBall) && ReferenceEquals(_player.ModSourceNativeBall,_nativeBall))
            && !_player.IsBot && _player.SlotIndex == _slot && (uint)_slot < _scene.Players.Items.Count
            && ReferenceEquals(_scene.Players.Items[_slot], _player)
            && _player.Hunter == _selection.NativeHunter && !_scene.Services.IsReplica && !_scene.GameState.SinglePlayer
            && _player.ModCharacterGameplay.Variant == Multiplayer.CharacterGameplayVariant.Native
            && !Network.NetSession.Active && !Network.DemoPlayback.IsActive && !Network.ReplayCapture.LiveRecordingActive;

    internal bool Current => Owns(_scene, _native, _selection.Pack, _selection.SelectedAppearance, _instance)
        && CharacterModelRuntime.IsSourceBodyCurrent(_scene, _instance, _model);
    bool ICharacterSourcePresentationOwner.Owns(Scene scene, Model native, CharacterModelPack pack,
        PrivateCharacterAppearance appearance, CharacterSourcePreviewInstance instance) => Owns(scene,native,pack,appearance,instance);
    bool ICharacterSourcePresentationOwner.IsCurrent(CharacterModelPart part) => part switch
    { CharacterModelPart.Biped => Current, CharacterModelPart.ViewModel => CannonCurrent,
        CharacterModelPart.AlternateForm => BallCurrent, _ => false };
    internal bool CannonCurrent => Current && _cannonInstance != null && _cannonModel != null
        && Owns(_scene, _nativeCannon!, _selection.Pack, _selection.SelectedAppearance, _cannonInstance)
        && CharacterModelRuntime.IsSourceBodyCurrent(_scene, _cannonInstance, _cannonModel);
    internal bool BallCurrent => Current && _ballInstance != null && _ballModel != null
        && Owns(_scene,_nativeBall!,_selection.Pack,_selection.SelectedAppearance,_ballInstance)
        && CharacterModelRuntime.IsSourceBodyCurrent(_scene,_ballInstance,_ballModel);

    internal void Step(ulong step, long life, PlayerAnimation lower, int lowerFrame,
        PlayerAnimation upper, int upperFrame, float aimY, bool supported, bool ballSupported=false)
    {
        if (_disposed || _step == step) return;
        _step = step;
        if (!Current) { Ready = false; BallReady=false; return; }
        if (_life != life) { _life = life; _ticks = 0; _ballTicks=0; _pose.Reset(); }
        BallReady=ballSupported&&BallCurrent;
        if(BallReady)_ballTicks++;
        Ready = supported && _pose.Supports(lower, upper);
        if (!Ready) { _pose.Reset(); return; }
        var pose = _pose.Sample(lower, lowerFrame, upper, upperFrame, aimY);
        _model.UpdateSourcePreviewPalette(pose);
        _ticks++;
    }

    internal bool Prepare(Matrix4 transform)
    {
        if (!Ready || !Current) return false;
        _model.PrepareSourcePreviewPresentation(transform);
        return true;
    }

    internal bool PrepareCannon(Matrix4 gun, float muzzleOffset, out Matrix4 presentation)
    {
        presentation = Matrix4.Identity;
        if (!Ready || !CannonCurrent || !float.IsFinite(muzzleOffset)) return false;
        // BodyArmCannon sizing from SecondHunt 41452f2d (MIT, NoneGiven 2020).
        // The fixed recipe places its locator at zero and barrel along +Z.
        // Keep that tip on the native late-latched effect point; no recoil
        // translation, diagnostic fit or gameplay muzzle mutation is applied.
        presentation = Matrix4.CreateScale(FeetFit.ExtractScale().X * .7f)
            * Matrix4.CreateTranslation(0, 0, muzzleOffset) * gun;
        _cannonModel!.PrepareSourcePreviewPresentation(presentation); // ToCannon exactly once, inside this owner.
        return true;
    }
    internal bool PrepareBall(Matrix4 roll,out Matrix4 presentation)
    {
        presentation=Matrix4.Identity;
        if(!BallReady||!BallCurrent)return false;
        presentation=BallFit*roll;
        _ballModel!.PrepareSourceBallPresentation(presentation,BallCompletedSteps);return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Ready = false; BallReady=false;
        if (_ballInstance != null) CharacterModelRuntime.ReleaseSourcePreview(_scene,_ballInstance);
        if (_cannonInstance != null) CharacterModelRuntime.ReleaseSourcePreview(_scene, _cannonInstance);
        CharacterModelRuntime.ReleaseSourcePreview(_scene, _instance);
    }
}

// Clip mapping/held arm behavior adapted from Second Hunt DarkSamusBody.cs,
// 41452f2d053fcfa679e33c00a73d120a53f59e61 (MIT, Copyright (c) 2020
// NoneGiven; permission/warranty notice retained in the repository LICENSE).
// State/pose adapter over the existing full-key decoder. Source root translation
// is removed from presentation only. Native state, aim and muzzle are never set.
internal sealed class CharacterSourceBodyPose : ICharacterSourceBodyPose
{
    internal static readonly string[] RequiredClips =
    ["B_ready_dark_samus", "B_glideForward_dark_samus", "B_glideBackwards_dark_samus",
        "B_glideLeft_dark_samus", "B_glideRight_dark_samus", "A_MissileBarrage_dark_samus",
        "A_BoostBall_ChargeReady_dark_samus", "A_BoostBall_ChargeShot_dark_samus"];
    private readonly CharacterSourceRig _rig;
    private readonly IReadOnlyDictionary<string, CharacterSourceClip> _clips;
    private readonly int _root, _spine, _shoulder, _elbow, _tip;
    private readonly bool[] _torso;
    private readonly Matrix4 _heldShoulder, _heldElbow;
    private Matrix4[]? _last, _from;
    private PlayerAnimation _lower, _upper;
    private int _lowerFrame = -1, _upperFrame = -1, _lowerTicks, _upperTicks, _fade;

    internal CharacterSourceBodyPose(CharacterSourceRig rig, IReadOnlyDictionary<string, CharacterSourceClip> clips)
    {
        (_rig, _clips) = (rig, clips);
        int Bone(string suffix) => Enumerable.Range(0, rig.Nodes.Count).Single(i => rig.Nodes[i].Name.EndsWith("_" + suffix, StringComparison.Ordinal));
        _root = Bone("0_root"); _spine = Bone("Spine_1"); _shoulder = Bone("R_shoulder");
        _elbow = Bone("R_elbow"); _tip = Bone("GUN_Particle_LCTR");
        _torso = Enumerable.Range(0, rig.Nodes.Count).Select(i => Descendant(i, _spine)).ToArray();
        var held = Globals(clips[RequiredClips[5]].Sample(0));
        Vector3 direction = held[_tip].Row3.Xyz - held[_elbow].Row3.Xyz;
        if (direction.LengthSquared < .000001f) throw new InvalidDataException("Source cannon aim locator is degenerate.");
        direction.Normalize();
        float dot = Math.Clamp(Vector3.Dot(direction, Vector3.UnitZ), -1, 1);
        Vector3 axis = Vector3.Cross(direction, Vector3.UnitZ);
        Matrix4 turn = axis.LengthSquared < .000001f
            ? dot < 0 ? Matrix4.CreateRotationY(MathF.PI) : Matrix4.Identity
            : Matrix4.CreateFromAxisAngle(axis.Normalized(), MathF.Acos(dot));
        _heldShoulder = held[_shoulder].ClearTranslation() * turn;
        _heldElbow = held[_elbow] * held[_shoulder].Inverted();
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
        PlayerAnimation.Idle or PlayerAnimation.Turn or PlayerAnimation.LandNeutral
            or PlayerAnimation.LandLeft or PlayerAnimation.LandRight => RequiredClips[0],
        PlayerAnimation.WalkForward => RequiredClips[1], PlayerAnimation.WalkBackward => RequiredClips[2],
        PlayerAnimation.WalkLeft => RequiredClips[3], PlayerAnimation.WalkRight => RequiredClips[4], _ => null
    };
    internal static string? UpperClip(PlayerAnimation state) => state switch
    {
        PlayerAnimation.Shoot => RequiredClips[5], PlayerAnimation.Charge => RequiredClips[6],
        PlayerAnimation.ChargeShoot => RequiredClips[7], _ => null
    };
    internal static bool Supports(PlayerAnimation lower, PlayerAnimation upper)
        => LowerClip(lower) != null && (UpperClip(upper) != null || LowerClip(upper) != null);
    bool ICharacterSourceBodyPose.Supports(PlayerAnimation lower, PlayerAnimation upper) => Supports(lower, upper);
    void ICharacterSourceBodyPose.Reset() => Reset();
    Matrix4[] ICharacterSourceBodyPose.Sample(PlayerAnimation lower, int lowerFrame, PlayerAnimation upper, int upperFrame, float aimY)
        => Sample(lower, lowerFrame, upper, upperFrame, aimY);
    internal void Reset() { _last = _from = null; _lowerFrame = _upperFrame = -1; _lowerTicks = _upperTicks = _fade = 0; }
    internal Matrix4[] Sample(PlayerAnimation lower, int lowerFrame, PlayerAnimation upper, int upperFrame, float aimY)
    {
        if (!Supports(lower, upper) || !float.IsFinite(aimY)) throw new InvalidDataException("Unsupported body pose.");
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
            var clip = _clips[name]; double time = _upperTicks++ / 60d;
            var torso = clip.Sample(upper == PlayerAnimation.Charge ? time % clip.Duration : Math.Min(time, clip.Duration));
            for (int i = 0; i < pose.Length; i++) if (_torso[i]) pose[i] = torso[i];
        }
        if (_from != null && _fade < 6)
        {
            float weight = ++_fade / 6f;
            for (int i = 0; i < pose.Length; i++) pose[i] = Mix(_from[i], pose[i], weight);
        }
        pose[_root].Row3.Xyz = _rig.Nodes[_root].Local.Row3.Xyz;
        var world = Globals(pose);
        float limit = Fixed.ToFloat(2896);
        float pitch = MathF.Asin(Math.Clamp(aimY, -limit, limit));
        Matrix4 shoulder = _heldShoulder * Matrix4.CreateRotationX(-pitch);
        shoulder.Row3 = world[_shoulder].Row3;
        int parent = _rig.Nodes[_shoulder].Parent;
        pose[_shoulder] = parent < 0 ? shoulder : shoulder * world[parent].Inverted();
        pose[_elbow] = _heldElbow;
        foreach (var matrix in pose) CharacterSourceRig.RequireRigid(matrix);
        _last = pose.ToArray();
        return pose;
    }
    internal static Matrix4 Mix(Matrix4 a, Matrix4 b, float amount)
    {
        var result = Matrix4.CreateFromQuaternion(Quaternion.Slerp(a.ExtractRotation(), b.ExtractRotation(), amount));
        result.Row3.Xyz = Vector3.Lerp(a.Row3.Xyz, b.Row3.Xyz, amount);
        return result;
    }
}
