using System;
using MphRead.Mods.Render;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private Mods.Replay.ReplayOwnerVisual _ownerVisual;
    private bool _ownerGunDrawActive;
    private (Matrix4 Transform, Matrix4 Animation)[] _ownerSavedNodes = Array.Empty<(Matrix4, Matrix4)>();
    private OwnerMaterial[] _ownerSavedMaterials = Array.Empty<OwnerMaterial>();
    private float[] _ownerSavedPalette = Array.Empty<float>();
    private readonly record struct OwnerMaterial(float Alpha, Vector3 Diffuse, Vector3 Ambient, Vector3 Specular,
        int Texture, int Palette, int Binding, RenderMode Mode)
    {
        internal static OwnerMaterial Capture(Material m) => new(m.CurrentAlpha, m.CurrentDiffuse, m.CurrentAmbient,
            m.CurrentSpecular, m.CurrentTextureId, m.CurrentPaletteId, m.TextureBindingId, m.RenderMode);
        internal void Restore(Material m)
        { m.CurrentAlpha = Alpha; m.CurrentDiffuse = Diffuse; m.CurrentAmbient = Ambient; m.CurrentSpecular = Specular;
            m.CurrentTextureId = Texture; m.CurrentPaletteId = Palette; m.TextureBindingId = Binding; m.RenderMode = Mode; }
    }

    internal Mods.Replay.ReplayOwnerVisual ModCaptureOwnerVisual()
    {
        AnimationSamples.Sample? sample = TryHunterNodeSample(_gunModel, out var sampled) ? sampled : null;
        return new(true, (byte)Hunter, (byte)CurrentWeapon, (byte)Recolor, _curAlpha,
            Mods.Replay.ReplayOwnerAnimation.Capture(_gunModel, sample), CosmeticMaterial(true));
    }
    internal bool ModSetOwnerVisual(Mods.Replay.ReplayOwnerVisual visual)
    {
        bool compatible = visual.Present && visual.Hunter == (byte)Hunter && visual.Weapon == (byte)CurrentWeapon
            && visual.Animation.CanApply(_gunModel);
        _ownerVisual = compatible ? visual : default;
        return compatible;
    }
    private OwnerGunDrawScope ModBeginOwnerGunDraw(Matrix4 transform) => new(this, transform);
    private bool ModTryOwnerGunSample(out AnimationSamples.Sample sample)
    {
        var value = _ownerVisual.Animation;
        sample = new(value.From, value.To, value.Fraction);
        return _ownerGunDrawActive;
    }
    private readonly struct OwnerGunDrawScope : IDisposable
    {
        private readonly PlayerEntity? _player;
        private readonly Mods.Replay.ReplayOwnerAnimation _animation;
        private readonly int _recolor;
        private readonly float _alpha;
        private readonly ModelInstance? _model;
        internal OwnerGunDrawScope(PlayerEntity player, Matrix4 transform)
        {
            _player = null; _animation = default; _recolor = 0; _alpha = 0; _model = null;
            var visual = player._ownerVisual;
            if (player._ownerGunDrawActive || !player._scene.Services.IsReplica || !visual.Present || visual.Hunter != (byte)player.Hunter
                || visual.Weapon != (byte)player.CurrentWeapon || !visual.Animation.CanApply(player._gunModel)) return;
            _player = player; _animation = Mods.Replay.ReplayOwnerAnimation.Capture(player._gunModel);
            _model = player._gunModel; var model = _model.Model;
            if (player._ownerSavedNodes.Length != model.Nodes.Count) player._ownerSavedNodes = new (Matrix4, Matrix4)[model.Nodes.Count];
            if (player._ownerSavedMaterials.Length != model.Materials.Count) player._ownerSavedMaterials = new OwnerMaterial[model.Materials.Count];
            if (player._ownerSavedPalette.Length != model.MatrixStackValues.Count) player._ownerSavedPalette = new float[model.MatrixStackValues.Count];
            for (int i = 0; i < model.Nodes.Count; i++) player._ownerSavedNodes[i] = (model.Nodes[i].Transform, model.Nodes[i].Animation);
            for (int i = 0; i < model.Materials.Count; i++) player._ownerSavedMaterials[i] = OwnerMaterial.Capture(model.Materials[i]);
            for (int i = 0; i < model.MatrixStackValues.Count; i++) player._ownerSavedPalette[i] = model.MatrixStackValues[i];
            _recolor = player.Recolor; _alpha = player._curAlpha;
            visual.Animation.Apply(player._gunModel); player.Recolor = visual.Recolor; player._curAlpha = visual.Alpha;
            player._ownerGunDrawActive = true;
        }
        public void Dispose()
        {
            if (_player == null) return;
            _animation.Apply(_model!); _player.Recolor = _recolor; _player._curAlpha = _alpha;
            _player._ownerGunDrawActive = false;
            // Restore shared animation matrices/materials even if a draw failed.
            var model = _model!.Model;
            for (int i = 0; i < model.Nodes.Count; i++)
            { model.Nodes[i].Transform = _player._ownerSavedNodes[i].Transform; model.Nodes[i].Animation = _player._ownerSavedNodes[i].Animation; }
            for (int i = 0; i < model.Materials.Count; i++) _player._ownerSavedMaterials[i].Restore(model.Materials[i]);
            var palette = (float[])model.MatrixStackValues;
            Array.Copy(_player._ownerSavedPalette, palette, palette.Length); MatrixPaletteRevision.Changed(palette);
        }
    }
}
