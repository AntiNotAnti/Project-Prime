using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render.Characters;

// The flying Arsenal player, never the separately spawned grounded DroneBody.
// Body/cannon and drone retain independently captured source generations.
internal sealed class CharacterSourceArsenalDroneOwner : IDisposable, ICharacterSourcePresentationOwner
{
    private readonly Scene _scene;
    private readonly PlayerEntity _player;
    private readonly Model _native;
    private readonly CharacterSourceArsenalDroneSelection _selection;
    private readonly CharacterSourcePreviewInstance _instance;
    private readonly CharacterWeightedRenderModel _model;
    private readonly CharacterSourceDronePose _pose;
    private readonly CharacterSourceDroneMotion _motion = new();
    private readonly TrooperPirateBlasterRuntime _blaster;
    private readonly WeaponRegistry _registry;
    private readonly long _generation;
    private readonly int _bots;
    private readonly Matrix4 _fit;
    private WeaponOwnerContext _weaponLife;
    private Vector3 _lastCenter;
    private ulong? _step;
    private long _life = -1;
    private bool _disposed;
    internal bool Ready { get; private set; }
    internal long CompletedSteps { get; private set; }
    internal double MaterialSeconds => CompletedSteps / 60d;
    internal string Generation => _selection.Source.Generation;
    internal CharacterWeightedRenderModel Model => _model;
    internal CharacterSourceDronePose Pose => _pose;
    internal CharacterSourceDroneMotion Motion => _motion;
    internal Matrix4 Fit => _fit;

    internal CharacterSourceArsenalDroneOwner(Scene scene, PlayerEntity player,
        CharacterSourceArsenalDroneSelection selection, int bots)
    {
        if (!player.Initialized || player.Hunter != Hunter.Weavel || player.IsBot
            || player.ModCharacterGameplay != CharacterGameplayRules.TrooperArsenalV1
            || player.ModSourceNativeDrone == null || bots is < 0 or > 7)
            throw new InvalidOperationException("An initialized local Trooper Arsenal drone actor is required.");
        (_scene, _player, _selection, _native, _bots, _generation) =
            (scene, player, selection, player.ModSourceNativeDrone, bots, scene.LifetimeGeneration);
        _blaster = scene.PirateBlaster ?? throw new InvalidOperationException("The native Arsenal weapon owner is unavailable.");
        _registry = scene.CustomWeapons ?? throw new InvalidOperationException("The native Arsenal registry is unavailable.");
        _weaponLife = _blaster.Owner;
        _lastCenter = player.Position.AddY(Fixed.ToFloat(player.Values.AltColYPos));
        // Native Multiplayer load precedes genuine Shoot-to-Spawn. Structural
        // resource binding grants neither health nor a semantic weapon life.
        if (!StructuralCurrent)
            throw new InvalidOperationException("The initialized Arsenal drone actor and configured native bots are required.");
        string anchor = _native.Materials.GroupBy(m => m.Name, StringComparer.Ordinal)
            .Where(g => g.Count() == 1).Select(g => g.Single())
            .First(m => CharacterModelRuntime.CanUseAuthoredTexcoords(m.TexgenMode, true)).Name;
        _instance = new(anchor, CharacterModelPart.AlternateForm, bodyOwner: this);
        var source = selection.Source;
        try
        {
            if (!CharacterModelRuntime.TryGetSourcePreview(scene, Hunter.Weavel, CharacterModelPart.AlternateForm,
                _native, source.Pack, source.Appearance, _instance, true, out _model)
                || _model.Joints.Count != 33 || _model.IndexCount != 1390 * 3 || _model.Segments.Count != 2
                || _model.Segments.Any(s => s.SourceGx == null || s.SourceJointPalette is not { Count: > 0 and <= 32 }
                    || s.SourcePresentationPalette == null))
                throw new InvalidDataException("The complete selected Arsenal source drone could not bind.");
            var clips = new Dictionary<int, CharacterSourceClip>();
            foreach (int ordinal in CharacterSourceDronePose.RequiredOrdinals)
            {
                if (!CharacterModelRuntime.TryLoadSourceClip(scene, _instance, _model, ordinal, out var raw))
                    throw new InvalidDataException("Missing strict Arsenal drone primitive: " + ordinal);
                clips.Add(ordinal, raw.WithPinnedDroneRootMotion());
            }
            _pose = new(clips);
            var pose = clips[CharacterGeneratedDroneProfile.ClipOrdinal].Sample(0);
            var rig = _model.SourceRig!;
            int pelvis = Enumerable.Range(0, rig.Nodes.Count).Single(i => rig.Nodes[i].Name == "source_bone_4_Skeleton_Root");
            Matrix4 global = pose[pelvis];
            for (int parent = rig.Nodes[pelvis].Parent; parent >= 0; parent = rig.Nodes[parent].Parent) global *= pose[parent];
            _fit = Matrix4.CreateTranslation(-global.Row3.Xyz) * Matrix4.CreateScale(.3f);
            _model.UpdateSourcePreviewPalette(pose);
        }
        catch { CharacterModelRuntime.ReleaseSourcePreview(scene, _instance); throw; }
    }

    private bool StructuralCurrent
    {
        get
        {
            if (_disposed || _scene.Exiting || _scene.SideScene || _scene.IsReplayLab
                || _scene.LifetimeGeneration != _generation || _scene.Services.IsReplica
                || _scene.GameState.Mode != GameMode.Battle || _scene.GameState.Teams || _scene.GameState.SinglePlayer
                || NetSession.Active || DemoPlayback.IsActive || ReplayCapture.LiveRecordingActive
                || !ReferenceEquals(GameState.Current, _scene.GameState)
                || !ReferenceEquals(PlayerEntity.LegacyRegistry, _scene.Players)
                || !ReferenceEquals(_scene.CustomWeapons, _registry) || _registry.IsDisposed
                || !ReferenceEquals(_scene.PirateBlaster, _blaster)
                || !_player.Initialized || _player.IsBot || _player.Hunter != Hunter.Weavel || _player.SlotIndex != 0
                || _scene.Players.Items.Count == 0 || !ReferenceEquals(_scene.Players.Main, _player)
                || !ReferenceEquals(_scene.Players.Items[0], _player)
                || !ReferenceEquals(_player.ModSourceNativeDrone, _native)
                || _player.ModCharacterGameplay != CharacterGameplayRules.TrooperArsenalV1
                || _scene.Players.PlayerCount != _bots + 1 || _scene.Players.PlayersCreated != _bots + 1) return false;
            for (int slot = 1; slot <= _bots; slot++)
                if (!_scene.Players.Items[slot].IsBot || !_scene.Players.Items[slot].LoadFlags.TestFlag(LoadFlags.Active)
                    || _scene.Players.Items[slot].ModCharacterGameplay != CharacterGameplayRules.Native) return false;
            for (int slot = _bots + 1; slot < _scene.Players.Items.Count; slot++)
                if (_scene.Players.Items[slot].LoadFlags.TestFlag(LoadFlags.Active)) return false;
            return true;
        }
    }
    public bool Owns(Scene scene, Model native, CharacterModelPack pack, PrivateCharacterAppearance appearance,
        CharacterSourcePreviewInstance instance) => StructuralCurrent && ReferenceEquals(scene, _scene)
        && ReferenceEquals(native, _native) && ReferenceEquals(pack, _selection.Source.Pack)
        && appearance == _selection.Source.Appearance && ReferenceEquals(instance, _instance);
    private bool ResidentCurrent => CharacterModelRuntime.IsSourceBodyCurrent(_scene, _instance, _model);
    // Blaster selection is irrelevant in alternate form. Its native Available
    // life is the authority fence; Selected governs the separate cannon only.
    internal bool Current => StructuralCurrent && _blaster.Available(_player)
        && _weaponLife.IsValid && _weaponLife == _blaster.Owner && ResidentCurrent;
    public bool IsCurrent(CharacterModelPart part) => part == CharacterModelPart.AlternateForm && Ready && Current;

    internal void Step(ulong step, long life, bool supported)
    {
        if (_disposed || _step == step) return;
        _step = step;
        Vector3 center = _player.Position.AddY(Fixed.ToFloat(_player.Values.AltColYPos));
        Vector3 velocity = (center - _lastCenter) * 60f; _lastCenter = center;
        if (!StructuralCurrent || !_blaster.Available(_player) || !ResidentCurrent)
        { Ready = false; _pose.Suspend(); _motion.Reset(); return; }
        if (_life != life || _weaponLife != _blaster.Owner)
        {
            _life = life; _weaponLife = _blaster.Owner; CompletedSteps = 0;
            _pose.Reset(); _motion.Reset(); velocity = Vector3.Zero;
        }
        Ready = supported && CharacterSourceDronePose.Supports(_player.ModSourceDroneState, _player.ModSourceDroneFrame);
        if (!Ready) { _pose.Suspend(); _motion.Reset(); return; }
        CompletedSteps++;
        int state = _player.ModSourceDroneState, frame = _player.ModSourceDroneFrame;
        if (state == 0)
        {
            Vector3 facing = _player.FacingVector;
            if (facing.X * facing.X + facing.Z * facing.Z <= 1e-8f) facing = new(_player.Field70, 0, _player.Field74);
            state = CharacterSourceDronePose.StateForFreeOrdinal(_motion.Sample(velocity, facing));
        }
        _model.UpdateSourcePreviewPalette(_pose.Sample(state, frame));
    }
    internal bool Prepare(Matrix4 nativeFacing, out Matrix4 presentation)
    {
        presentation = _fit * nativeFacing;
        if (!Ready || !Current) return false;
        _model.PrepareSourcePreviewPresentation(presentation); return true;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Ready = false;
        CharacterModelRuntime.ReleaseSourcePreview(_scene, _instance);
    }
}
