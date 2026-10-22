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

// Explicit Arsenal capability. Native body owners retain their Native-only
// contract; the view model receives a separate, part-limited capability below.
internal sealed class CharacterSourceArsenalBodyOwner : IDisposable, ICharacterSourcePresentationOwner
{
    private readonly Scene _scene;
    private readonly PlayerEntity _player;
    private readonly CharacterSourcePreviewSelection _selection;
    private readonly TrooperPirateBlasterRuntime _blaster;
    private readonly WeaponRegistry _registry;
    private readonly long _generation;
    private readonly int _bots;
    private readonly Model _nativeBody, _nativeCannon;
    private readonly CharacterSourcePreviewInstance _bodyInstance, _cannonInstance;
    private readonly CharacterSourceArsenalViewModelOwner _viewOwner;
    private readonly CharacterSourceTrooperPose _pose;
    private readonly CharacterWeightedRenderModel _model, _cannonModel;
    private WeaponOwnerContext _weaponLife;
    private ulong? _step;
    private long _life = -1, _ticks, _cannonTicks;
    private bool _disposed;
    internal bool Ready { get; private set; }
    internal bool CannonReady { get; private set; }
    internal string Generation => _selection.Generation;
    internal Matrix4 FeetFit { get; }
    internal CharacterWeightedRenderModel Model => _model;
    internal CharacterWeightedRenderModel CannonModel => _cannonModel;
    internal double MaterialSeconds => _ticks / 60d;
    internal double CannonMaterialSeconds => _cannonTicks / 60d;
    internal ICharacterSourcePresentationOwner ViewOwner => _viewOwner;

    internal CharacterSourceArsenalBodyOwner(Scene scene, PlayerEntity player,
        CharacterSourcePreviewSelection selection, int bots)
    {
        if (!selection.RuntimeArsenalBody || !selection.RuntimeCannon || selection.HasBall
            || selection.SelectedAppearance.CharacterId != "trooper"
            || CharacterGeneratedTrooperProfile.FromSkin(selection.SelectedAppearance.SkinSource) == null
            || !player.Initialized || player.Hunter != Hunter.Weavel || player.IsBot
            || player.ModCharacterGameplay != CharacterGameplayRules.TrooperArsenalV1 || bots is < 0 or > 7)
            throw new InvalidOperationException("An explicit initialized local Trooper Arsenal body owner is required.");
        (_scene, _player, _selection, _bots, _generation) = (scene, player, selection, bots, scene.LifetimeGeneration);
        _blaster = scene.PirateBlaster ?? throw new InvalidOperationException("The native Arsenal weapon owner is unavailable.");
        _registry = scene.CustomWeapons ?? throw new InvalidOperationException("The native Arsenal registry is unavailable.");
        _weaponLife = _blaster.Owner;
        _nativeBody = player.BipedModel2.Model;
        _nativeCannon = player.ModSourceNativeGun;
        // Multiplayer load initializes an unspawned actor. Do not fabricate a
        // native Spawn/life just to bind cosmetic resources at the load barrier.
        if (!StructuralCurrent)
            throw new InvalidOperationException("The initialized native Arsenal actor and configured opponents are required.");
        _bodyInstance = new(Anchor(_nativeBody), bodyOwner: this);
        _viewOwner = new(this);
        _cannonInstance = new(Anchor(_nativeCannon, cannon: true), CharacterModelPart.ViewModel, bodyOwner: _viewOwner);
        try
        {
            if (!CharacterModelRuntime.TryGetSourcePreview(scene, Hunter.Weavel, CharacterModelPart.Biped,
                _nativeBody, selection.Pack, selection.SelectedAppearance, _bodyInstance, true, out _model)
                || !Complete(_model, 1970))
                throw new InvalidDataException("The complete selected Arsenal source body could not bind.");
            var clips = new Dictionary<string, CharacterSourceClip>(StringComparer.Ordinal);
            foreach (string name in CharacterSourceTrooperPose.RequiredClips)
            {
                var choices = selection.Clips.Where(c => c.Name == name).ToArray();
                if (choices.Length != 1 || !CharacterModelRuntime.TryLoadSourceClip(scene, _bodyInstance,
                    _model, choices[0].Ordinal, out var clip))
                    throw new InvalidDataException("Missing strict Arsenal body clip: " + name);
                clips.Add(name, clip);
            }
            _pose = new(_model.SourceRig!, clips);
            FeetFit = _model.FitSourceBodyToFeet(_pose.Sample(PlayerAnimation.Idle, 0, PlayerAnimation.Idle, 0, 0),
                Fixed.ToFloat(player.Values.MaxPickupHeight - player.Values.MinPickupHeight));
            _pose.Reset();
            if (!CharacterModelRuntime.TryGetSourcePreview(scene, Hunter.Weavel, CharacterModelPart.ViewModel,
                _nativeCannon, selection.Pack, selection.SelectedAppearance, _cannonInstance, true, out _cannonModel)
                || !Complete(_cannonModel, 206) || _cannonModel.SourceCannon?.IsGenerated != true
                || _cannonModel.SourceCannon.TriangleCount != 206)
                throw new InvalidDataException("The complete selected Arsenal source cannon could not bind.");
        }
        catch
        {
            CharacterModelRuntime.ReleaseSourcePreview(scene, _cannonInstance);
            CharacterModelRuntime.ReleaseSourcePreview(scene, _bodyInstance);
            throw;
        }
    }

    private static string Anchor(Model native, bool cannon = false) => native.Materials
        .GroupBy(m => m.Name, StringComparer.Ordinal).Where(g => g.Count() == 1).Select(g => g.Single())
        .First(m => (!cannon || m.PolygonMode == PolygonMode.Modulate && m.RenderMode == RenderMode.Normal)
            && CharacterModelRuntime.CanUseAuthoredTexcoords(m.TexgenMode, true)).Name;
    private static bool Complete(CharacterWeightedRenderModel model, int triangles) => model.Joints.Count == 35
        && model.IndexCount == triangles * 3 && model.Segments.Count == 3
        && model.Segments.All(s => s.SourceGx != null && s.SourceJointPalette is { Count: > 0 and <= 32 }
            && s.SourcePresentationPalette != null);

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
                || !ReferenceEquals(_player.BipedModel2.Model, _nativeBody)
                || !ReferenceEquals(_player.ModSourceNativeGun, _nativeCannon)
                || _player.ModCharacterGameplay != CharacterGameplayRules.TrooperArsenalV1
                || _scene.Players.PlayerCount != _bots + 1 || _scene.Players.PlayersCreated != _bots + 1)
                return false;
            for (int slot = 1; slot <= _bots; slot++)
                if (!_scene.Players.Items[slot].IsBot
                    || !_scene.Players.Items[slot].LoadFlags.TestFlag(LoadFlags.Active)
                    || _scene.Players.Items[slot].ModCharacterGameplay != CharacterGameplayRules.Native) return false;
            for (int slot = _bots + 1; slot < _scene.Players.Items.Count; slot++)
                if (_scene.Players.Items[slot].LoadFlags.TestFlag(LoadFlags.Active)) return false;
            return true;
        }
    }
    public bool Owns(Scene scene, Model native, CharacterModelPack pack, PrivateCharacterAppearance appearance,
        CharacterSourcePreviewInstance instance) => StructuralCurrent && ReferenceEquals(scene, _scene)
        && ReferenceEquals(native, _nativeBody) && ReferenceEquals(pack, _selection.Pack)
        && appearance == _selection.SelectedAppearance && ReferenceEquals(instance, _bodyInstance);
    internal bool OwnsViewModel(Scene scene, Model native, CharacterModelPack pack, PrivateCharacterAppearance appearance,
        CharacterSourcePreviewInstance instance) => StructuralCurrent && ReferenceEquals(scene, _scene)
        && ReferenceEquals(native, _nativeCannon) && ReferenceEquals(pack, _selection.Pack)
        && appearance == _selection.SelectedAppearance && ReferenceEquals(instance, _cannonInstance);

    // Residency cannot be reconstructed from Step/Draw. Native Spawn alone
    // advances the weapon's match/epoch/generation/life context.
    private bool ResidentsCurrent => CharacterModelRuntime.IsSourceBodyCurrent(_scene, _bodyInstance, _model)
        && CharacterModelRuntime.IsSourceBodyCurrent(_scene, _cannonInstance, _cannonModel);
    internal bool Current => StructuralCurrent && _blaster.Available(_player)
        && _weaponLife.IsValid && _weaponLife == _blaster.Owner && ResidentsCurrent;
    internal bool CannonCurrent => Current && _blaster.Selected(_player);
    public bool IsCurrent(CharacterModelPart part) => part == CharacterModelPart.Biped && Ready && Current;

    internal void Step(ulong step, long life, PlayerAnimation lower, int lowerFrame,
        PlayerAnimation upper, int upperFrame, float aimY, bool supported, bool cannonSupported)
    {
        if (_disposed || _step == step) return;
        _step = step;
        if (!StructuralCurrent || !_blaster.Available(_player) || !ResidentsCurrent)
        { Ready = CannonReady = false; _pose.Reset(); return; }
        if (_life != life || _weaponLife != _blaster.Owner)
        {
            _life = life; _weaponLife = _blaster.Owner; _ticks = _cannonTicks = 0; _pose.Reset();
        }
        Ready = supported && CharacterSourceTrooperPose.Supports(lower, upper);
        CannonReady = Ready && cannonSupported && _blaster.Selected(_player);
        if (!Ready) { _pose.Reset(); return; }
        _model.UpdateSourcePreviewPalette(_pose.Sample(lower, lowerFrame, upper, upperFrame, aimY));
        _ticks++;
        if (CannonReady) _cannonTicks++;
    }
    internal bool Prepare(Matrix4 transform)
    {
        if (!Ready || !Current) return false;
        _model.PrepareSourcePreviewPresentation(transform); return true;
    }
    internal bool PrepareCannon(Matrix4 gun, float muzzleOffset, out Matrix4 presentation)
    {
        presentation = Matrix4.Identity;
        if (!CannonReady || !CannonCurrent || !float.IsFinite(muzzleOffset)) return false;
        // Same qualified fixed source tip and native late-latched effect point.
        presentation = Matrix4.CreateScale(FeetFit.ExtractScale().X * .7f)
            * Matrix4.CreateTranslation(0, 0, muzzleOffset) * gun;
        _cannonModel.PrepareSourcePreviewPresentation(presentation); return true;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Ready = CannonReady = false;
        CharacterModelRuntime.ReleaseSourcePreview(_scene, _cannonInstance);
        CharacterModelRuntime.ReleaseSourcePreview(_scene, _bodyInstance);
    }
}

internal sealed class CharacterSourceArsenalViewModelOwner(CharacterSourceArsenalBodyOwner actor) : ICharacterSourcePresentationOwner
{
    public bool Owns(Scene scene, Model native, CharacterModelPack pack, PrivateCharacterAppearance appearance,
        CharacterSourcePreviewInstance instance) => actor.OwnsViewModel(scene, native, pack, appearance, instance);
    public bool IsCurrent(CharacterModelPart part) => part == CharacterModelPart.ViewModel
        && actor.CannonReady && actor.CannonCurrent;
}
