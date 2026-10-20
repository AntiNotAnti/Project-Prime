using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Render.Characters;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// The hunter you are about to come back as, turning on the spot, in the
    /// suit you picked -- the real model, not a picture of one.
    ///
    /// The results screen asked this question with the game's 32x32 portrait
    /// sprite first, and the portrait cannot answer half of it: there is one
    /// per hunter and it does not know what a suit is, so the four colours
    /// beside it were the only thing saying what you had chosen. A model does
    /// know: <c>Recolor</c> is the same palette index the player wears, so
    /// what is drawn here is exactly what everybody else is about to see.
    ///
    /// <para>
    /// **It is an entity that is never in the scene.** It is built like one --
    /// <see cref="EntityBase"/> is where the node walk, the material update
    /// and the render-item building live, and reimplementing those for one
    /// preview would be a second renderer -- but it is never inserted, so it
    /// takes no slot, runs no Process, holds no collision, is never asked for
    /// a NodeRef and cannot outlive a room change. That last one is not
    /// hypothetical: an entity alive across a rotation is exactly the shape of
    /// bug that produced the crash this screen was being fixed for.
    /// </para>
    /// <para>
    /// Its items are drawn in a pass of their own, into a scissored corner of
    /// the frame with its own projection and a depth buffer cleared to itself
    /// (see <c>Scene.ModDrawPreview</c>). That is what makes it a preview
    /// window rather than a model standing in the room: nothing in the level
    /// can occlude it, and it cannot be lit by whatever the level happens to
    /// be lit by.
    /// </para>
    /// </summary>
    public class HunterPreviewEntity : EntityBase
    {
        /// <summary>
        /// Fixed lighting, not the room's.
        ///
        /// The room's lights are what make a level look like itself, and half
        /// the multiplayer maps would leave this pitch dark or bright orange.
        /// A key from the front left and a cool fill from the right is the
        /// lighting every character select in every game has, for the reason
        /// that it shows the shape.
        /// </summary>
        private static readonly LightInfo _light = new LightInfo(
            new Vector3(-0.35f, -0.30f, -0.89f), new Vector3(1f, 0.97f, 0.92f),
            new Vector3(0.60f, 0.20f, 0.77f), new Vector3(0.32f, 0.36f, 0.48f));

        // The same entity also serves results screens over a live Scene.
        // Keep their original light unless the launcher grants this frame's
        // cinematic permission; never change shared room lighting or albedo.
        private LightInfo CurrentLight
        {
            get
            {
                if (!Scene.LauncherPreview || !Scene.LauncherPreviewCinematicLighting)
                    return _light;

                Launcher.LauncherHunterTheme theme = Launcher.LauncherHunterTheme.For(_hunter);
                return new LightInfo(
                    new Vector3(-0.42f, -0.38f, -0.82f), new Vector3(0.98f, 0.99f, 1f),
                    new Vector3(0.60f, 0.20f, 0.77f),
                    new Vector3(0.28f + theme.Rim.R * 0.12f,
                        0.34f + theme.Rim.G * 0.12f,
                        0.44f + theme.Rim.B * 0.12f));
            }
        }

        /// <summary>Half a turn: these models are authored facing away.</summary>
        private static readonly Matrix4 _facing =
            Matrix4.CreateRotationY(MathHelper.DegreesToRadians(180));

        private Mods.Cosmetics.CosmeticAppearance _cosmetics = Mods.Cosmetics.CosmeticAppearance.Default;
        private readonly Mods.Cosmetics.Death.DeathPresentationState _death = new();
        private float _cosmeticTime;
        private int _deathRequest;
        private long _previewSteps;
        private long _lastPreviewStepStamp;
        public void SetCosmetics(Mods.Cosmetics.CosmeticLoadout loadout, int deathRequest)
        {
            if (_cosmetics.Loadout != loadout || _cosmeticHunter != _hunter) { _cosmetics = new(_hunter, loadout); _cosmeticHunter = _hunter; _death.Reset(); }
            if (_deathRequest != deathRequest)
            {
                _deathRequest = deathRequest;
                if (deathRequest == 0) _death.Reset(); else _death.Preview(_cosmetics, _cosmeticTime);
            }
        }
        private Hunter _cosmeticHunter = Hunter.Random;
        private Hunter _hunter = Hunter.Random;

        /// <summary>The model form currently shown by the preview.</summary>
        public Mods.Cosmetics.SkinContext Mode { get; private set; }

        // Metadata absence is deterministic for this process, but a model read
        // can fail transiently while paths/resources are settling. Keep those
        // cases separate so one bad attempt cannot pin the block fallback
        // until restart.
        private Mods.Cosmetics.SkinContext _unavailableMode;
        private Hunter _unavailable = Hunter.Random;
        private Mods.Cosmetics.SkinContext _retryMode;
        private Hunter _retryHunter = Hunter.Random;
        private int _loadFailures;
        private long _retryAfter;

        private int LoadRetryDelay()
        {
            int shift = Math.Min(_loadFailures, 3);
            _loadFailures++;
            return Math.Min(250 << shift, 2000);
        }

        private void ClearLoadRetry()
        {
            _retryHunter = Hunter.Random;
            _loadFailures = 0;
            _retryAfter = 0;
        }
        private int _recolor = -1;
        private ModelInstance? _model;
        private CharacterSourcePreviewBinding? _sourceRigPreview;

        // Acceptance-only attachment. The actual pack/runtime must verify it;
        // no public selection or global source-pose switch is added.
        internal bool TrySetSourceRigPreview(CharacterModelPack pack,
            PrivateCharacterAppearance appearance, IReadOnlyList<Matrix4>? immutablePose = null)
        {
            if (_model == null || Mode != Mods.Cosmetics.SkinContext.Biped
                || !CharacterSourcePreviewBinding.TryCreate(_scene, _hunter, _model.Model,
                    pack, appearance, immutablePose, out var binding)) return false;
            ClearSourceRigPreview(); _sourceRigPreview = binding; return true;
        }

        // Private raw-source diagnostic route; existing model/scene owns drawing.
        internal bool TrySetSourceMaterialRigPreview(CharacterModelPack pack,
            PrivateCharacterAppearance appearance,string nativeMaterialAnchor,object? stageOwner=null)
        {
            if(_model==null||Mode!=Mods.Cosmetics.SkinContext.Biped
                ||!CharacterSourcePreviewBinding.TryCreateWithSourceMaterials(_scene,_hunter,_model.Model,
                    pack,appearance,nativeMaterialAnchor,out var binding,stageOwner))return false;
            ClearSourceRigPreview();_sourceRigPreview=binding;return true;
        }
        internal bool TrySetSourceCannonPreview(CharacterModelPack pack,
            PrivateCharacterAppearance appearance,string nativeMaterialAnchor,object? stageOwner=null)
        {
            if(_model==null||Mode!=Mods.Cosmetics.SkinContext.ViewModel
                ||!CharacterSourcePreviewBinding.TryCreateSourceCannon(_scene,_hunter,_model.Model,
                    pack,appearance,nativeMaterialAnchor,out var binding,stageOwner))return false;
            ClearSourceRigPreview();_sourceRigPreview=binding;return true;
        }
        internal bool TrySelectSourceRigClip(int ordinal) => _sourceRigPreview?.TrySelectSourceClip(ordinal)==true;
        internal bool TrySetSourceRigTime(double seconds) => _sourceRigPreview?.TrySetSourceTime(seconds)==true;
        internal CharacterSourceClip? SelectedSourceRigClip => _sourceRigPreview?.SelectedSourceClip;

        private object? _sourceStageOwner;
        private CharacterModelPack? _sourceStagePack;
        private PrivateCharacterAppearance? _sourceStageAppearance, _sourceStageAttemptAppearance;
        private CharacterModelPart _sourceStagePart, _sourceStageAttemptPart;
        private object? _sourceStageAttemptOwner;
        private CharacterModelPack? _sourceStageAttemptPack;
        private double _sourceMaterialSeconds;
        private Matrix4 _sourceStageFit = Matrix4.Identity;
        private Matrix4 _sourceStageRestFit = Matrix4.Identity;
        internal void ReleaseSourceStage(object owner)
        { if (ReferenceEquals(owner, _sourceStageOwner) || ReferenceEquals(owner, _sourceStageAttemptOwner)) ClearSourceRigPreview(); }
        internal bool SyncSourceStage(CharacterSourceStageRequest? request)
        {
            if (request == null)
            { if (_sourceStageOwner != null || _sourceStageAttemptOwner != null) ClearSourceRigPreview(); return true; }
            if (!double.IsFinite(request.MaterialSeconds) || request.MaterialSeconds < 0
                || !double.IsFinite(request.Seconds) || request.Seconds < 0 || _model == null
                || !PrivateCharacterAppearance.TryCreate(Hunter.Samus, request.Appearance.CharacterId, request.Appearance.SkinSource, out _, out _, generatedSource: true)
                || _hunter != Hunter.Samus || request.Part is not (CharacterModelPart.Biped or CharacterModelPart.ViewModel)
                || Mode != (request.Part == CharacterModelPart.Biped ? Mods.Cosmetics.SkinContext.Biped : Mods.Cosmetics.SkinContext.ViewModel)
                || request.Part == CharacterModelPart.ViewModel && (request.ClipOrdinal != 32 || request.Seconds != 0)) return false;
            if (!ReferenceEquals(_sourceStageOwner, request.Owner) || !ReferenceEquals(_sourceStagePack, request.Pack) || _sourceStagePart != request.Part || _sourceStageAppearance != request.Appearance)
            {
                // A refused publication must not rehash/recompile the same
                // generation every frame. Reopening creates a fresh owner.
                if (ReferenceEquals(_sourceStageAttemptOwner, request.Owner) && ReferenceEquals(_sourceStageAttemptPack, request.Pack) && _sourceStageAttemptPart == request.Part && _sourceStageAttemptAppearance == request.Appearance) return false;
                ClearSourceRigPreview();
                _sourceStageAttemptOwner = request.Owner; _sourceStageAttemptPack = request.Pack; _sourceStageAttemptPart = request.Part; _sourceStageAttemptAppearance = request.Appearance;
                var uniqueMaterials = _model.Model.Materials.GroupBy(m => m.Name).Where(g => g.Count() == 1).Select(g => g.Single());
                // The native gun starts with invisible flash/glow materials.
                // Use a solid existing sampler/draw anchor for the diagnostic
                // cannon; never inherit an inactive muzzle effect's alpha.
                string? anchor = request.Part == CharacterModelPart.ViewModel
                    ? uniqueMaterials.Where(m => m.Alpha == 31 && m.CurrentAlpha >= .99999f
                        && m.RenderMode == RenderMode.Normal && m.PolygonMode == PolygonMode.Modulate)
                        .OrderByDescending(m => m.Lighting != 0).FirstOrDefault()?.Name
                    : uniqueMaterials.FirstOrDefault()?.Name;
                if (anchor == null) return false;
                bool attached = request.Part == CharacterModelPart.Biped
                    ? TrySetSourceMaterialRigPreview(request.Pack, request.Appearance, anchor, request.Owner)
                    : TrySetSourceCannonPreview(request.Pack, request.Appearance, anchor, request.Owner);
                if (!attached) return false;
                _sourceStageAttemptOwner = request.Owner; _sourceStageAttemptPack = request.Pack; _sourceStageAttemptPart = request.Part; _sourceStageAttemptAppearance = request.Appearance;
                if (!TryGetSourceRigPreview(out var source)) return false;
                _sourceStageOwner = request.Owner; _sourceStagePack = request.Pack; _sourceStagePart = request.Part; _sourceStageAppearance = request.Appearance;
                _sourceStageRestFit = source.DiagnosticPreviewFit;
            }
            _sourceMaterialSeconds = request.MaterialSeconds;
            if (request.Part == CharacterModelPart.ViewModel)
            { _sourceStageFit = _sourceStageRestFit; return true; }
            if (SelectedSourceRigClip?.Ordinal != request.ClipOrdinal && !TrySelectSourceRigClip(request.ClipOrdinal)) return false;
            if (!TrySetSourceRigTime(request.Seconds)) return false;
            // The diagnostic camera follows the source root. Raw source keys,
            // sampled skin palettes and gameplay transforms remain untouched.
            _sourceStageFit = Matrix4.CreateTranslation(-_sourceRigPreview!.DiagnosticRootDisplacement) * _sourceStageRestFit;
            return true;
        }

        internal void ClearSourceRigPreview()
        { _sourceRigPreview?.Release(); _sourceRigPreview = null; _sourceStageOwner = null; _sourceStagePack = null; _sourceStageAttemptOwner = null; _sourceStageAttemptPack = null; _sourceStageFit = Matrix4.Identity; _sourceStageRestFit = Matrix4.Identity; _sourceStageAppearance = _sourceStageAttemptAppearance = null; _sourceMaterialSeconds = 0; }

        internal bool TryGetSourceRigPreview(out CharacterWeightedRenderModel model)
        {
            model = null!;
            return _model != null && _sourceRigPreview != null
                && _sourceRigPreview.TryGet(_scene, _hunter, Mode == Mods.Cosmetics.SkinContext.ViewModel ? CharacterModelPart.ViewModel : CharacterModelPart.Biped,
                    _model.Model, out model);
        }
        private Vector3 _displayCenter;
        private float _displayScale = 1;

        public HunterPreviewEntity(Scene scene) : base(EntityType.Model, scene)
        {
        }

        public bool Ready => _model != null;

        /// <summary>
        /// The hunter and suit the model actually loaded is, which is not
        /// always the one last asked for: a hunter whose model is missing,
        /// or one asked for again before the disk answered, leaves the one
        /// before it standing there. Whoever draws the panel around this has
        /// to know, or it leaves a hole for the wrong character.
        /// </summary>
        public Hunter Shown => _model == null ? Hunter.Random : _hunter;

        public int ShownSuit => _recolor;
        public string ThumbnailKey => _death.Active && _death.Progress(_cosmeticTime, _cosmetics.Death) is > 0.35f and < 0.65f
            ? _cosmetics.Death.Key : _cosmetics.Armor.WireId != 0 ? _cosmetics.Armor.Key : _cosmetics.Skin.Key;

        /// <summary>
        /// Point it at a hunter and a suit. Cheap to call every frame: only a
        /// change loads anything, and <c>Read</c> caches models globally, so a
        /// hunter somebody is already playing costs nothing at all.
        /// </summary>
        public void SetUp(Hunter hunter, int recolor, Mods.Cosmetics.SkinContext mode = Mods.Cosmetics.SkinContext.Biped)
        {
            if (hunter != _hunter || Mode != mode) ClearSourceRigPreview();
            if (_model != null && hunter == _hunter && recolor == _recolor && Mode == mode)
            {
                return;
            }
            if (_unavailable == hunter && _unavailableMode == mode)
            {
                return;
            }
            if (_retryHunter == hunter && _retryMode == mode
                && Environment.TickCount64 < _retryAfter)
            {
                return;
            }
            if (hunter != _hunter || _model == null || Mode != mode)
            {
                try
                {
                    if (!Metadata.HunterModels.TryGetValue(hunter, out IReadOnlyList<string>? models)
                        || models.Count == 0)
                    {
                        _unavailable = hunter;
                        _unavailableMode = mode;
                        _model = null;
                        return;
                    }
                    // The first LOD, which is the one the game draws for a
                    // player you are standing next to.
                    int modelIndex = mode == Mods.Cosmetics.SkinContext.ViewModel ? 3
                        : mode == Mods.Cosmetics.SkinContext.AltForm ? 2 : 0;
                    if (modelIndex >= models.Count)
                    {
                        _unavailable = hunter;
                        _unavailableMode = mode;
                        _model = null;
                        Mods.DebugLog.Line("ui",
                            $"hunter preview has no {mode} model slot for {hunter}");
                        return;
                    }
                    ModelInstance inst = Read.GetModelInstance(models[modelIndex]);
                    _models.Clear();
                    _models.Add(inst);
                    _model = inst;
                    _previewSteps = 0;
                    _lastPreviewStepStamp = 0;
                    _hunter = hunter;
                    Mode = mode;
                    _death.Reset(); _deathRequest = 0;
                    // Standing, not the bind pose.
                    //
                    // A model with no animation set draws the skeleton as it
                    // was authored, which for these is arms straight out --
                    // the T-pose, and not a picture of anybody. Idle is the
                    // animation the game plays for a hunter standing still,
                    // so it is both the right pose and the one everybody
                    // recognises the character in.
                    inst.SetAnimation(mode == Mods.Cosmetics.SkinContext.Biped ? (int)PlayerAnimation.Idle : 0,
                        mode == Mods.Cosmetics.SkinContext.Biped ? AnimFlags.None : AnimFlags.Paused);
                    FitDisplayModel(inst);
                    ClearLoadRetry();
                    if (_unavailable == hunter && _unavailableMode == mode)
                    {
                        _unavailable = Hunter.Random;
                    }
                }
                catch (Exception ex)
                {
                    // A preview is not worth a match. Fall back immediately,
                    // but do not classify every I/O/decoder hiccup as a model
                    // that can never load. Retry with bounded backoff instead.
                    _model = null;
                    if (_retryHunter != hunter || _retryMode != mode)
                    {
                        _loadFailures = 0;
                    }
                    _retryHunter = hunter;
                    _retryMode = mode;
                    int delay = LoadRetryDelay();
                    _retryAfter = Environment.TickCount64 + delay;
                    Mods.DebugLog.Line("ui",
                        $"hunter preview model load failed for {hunter}/{mode}; "
                        + $"retrying in {delay} ms: {ex.Message}");
                    return;
                }
            }
            _recolor = _model == null ? 0 : Math.Clamp(recolor, 0, _model.Model.Recolors.Count - 1);
            Recolor = _recolor;
        }

        private void FitDisplayModel(ModelInstance inst)
        {
            _displayCenter = Vector3.Zero; _displayScale = 1;
            if (Mode == Mods.Cosmetics.SkinContext.Biped) return;
            var model = inst.Model;
            model.ComputeNodeMatrices(0);
            model.AnimateNodes(0, UseNodeTransform, Matrix4.Identity, model.Scale, inst.AnimInfo);
            Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
            foreach (var node in model.Nodes)
            {
                if (node.MeshCount == 0 || !node.Enabled) continue;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = new Vector3((corner & 1) == 0 ? node.MinBounds.X : node.MaxBounds.X,
                        (corner & 2) == 0 ? node.MinBounds.Y : node.MaxBounds.Y,
                        (corner & 4) == 0 ? node.MinBounds.Z : node.MaxBounds.Z);
                    p = Vector3.TransformPosition(p, node.Animation);
                    min = Vector3.ComponentMin(min, p); max = Vector3.ComponentMax(max, p);
                }
            }
            float diameter = (max - min).Length;
            if (float.IsFinite(diameter) && diameter > 0.001f)
            {
                _displayCenter = (min + max) / 2;
                _displayScale = Math.Clamp(2.2f / diameter, 0.1f, 20);
                // Spire's authored bounds include the extended attack shell,
                // while the paused idle shape is a compact ball.
                if (Mode == Mods.Cosmetics.SkinContext.AltForm && _hunter == Hunter.Spire)
                    _displayScale *= 2.4f;
            }
        }

        /// <summary>
        /// Advance the idle animation, once a simulation step.
        ///
        /// Called from the step and never from a draw, for the reason every
        /// other timer in this program is: a picture with no step behind it
        /// must not advance anything, or the hunter breathes at the frame
        /// rate. The instance is this preview's own -- <c>Read</c> caches the
        /// <c>Model</c>, not the <c>ModelInstance</c> -- so nothing a player
        /// is doing is touched by this.
        /// </summary>
        public void Step()
        {
            if (_model != null)
            {
                _model.UpdateAnimFrames();
                // The launcher drives this clock independently of a live
                // FrameTiming simulation. Record after its owned 60-Hz update;
                // a separate wall-clock phase is only for displaying the
                // intermediate pose. It never advances the actual animation.
                long step = ++_previewSteps;
                _lastPreviewStepStamp = System.Diagnostics.Stopwatch.GetTimestamp();
                var info = _model.AnimInfo;
                _model.PresentationSamples.Record(info, 0, step);
                _model.PresentationSamples.Record(info, 1, step);
            }
            _cosmeticTime += 1f / 60;
            if (Mods.Cosmetics.CosmeticPreview.LoopDeath && _deathRequest != 0
                && Mode == Mods.Cosmetics.SkinContext.Biped
                && (!_death.Active || _cosmeticTime - _death.StartTime >= _cosmetics.Death.Duration + 0.75f))
                _death.Preview(_cosmetics, _cosmeticTime);
        }

        public void Reset()
        {
            ClearSourceRigPreview();
            _death.Reset();
            _deathRequest = 0;
            _previewSteps = 0;
            _lastPreviewStepStamp = 0;
            _model?.PresentationSamples.Reset();
        }

        protected override Mods.Cosmetics.Skins.RenderMaterialOverride GetCosmeticMaterialOverride(ModelInstance inst, Material material, int index)
            => Mods.RenderOptions.ShowCustomCosmetics ? _scene.GetCosmeticMaterial(_cosmetics.Skin, inst.Model, index, Mode) : default;

        protected override LightInfo GetLightInfo()
        {
            return CurrentLight;
        }

        /// <summary>
        /// Build this frame's render items. The caller has already told the
        /// scene to collect them into the preview list rather than the world's.
        ///
        /// The pose comes from the idle animation set in <see cref="SetUp"/>
        /// and advanced in <see cref="Step"/>; nothing here touches it, so a
        /// frame drawn twice draws the same pose twice.
        /// </summary>
        // Retain owner transform metadata. Weighted4 uses the composed source
        // palette below for GPU positions; native palettes already contain pose.
        internal static Matrix4 WeightedPreviewTransform(CharacterWeightedRenderModel model,Matrix4 presentationPose)
            => model.IsSourcePreview ? presentationPose : Matrix4.Identity;

        private bool TryDrawModernCharacter(Matrix4 presentationPose)
        {
            if (_model == null) return false;
            CharacterModelPart? part = Mode switch
            {
                Mods.Cosmetics.SkinContext.Biped => CharacterModelPart.Biped,
                Mods.Cosmetics.SkinContext.ViewModel => CharacterModelPart.ViewModel,
                Mods.Cosmetics.SkinContext.AltForm => CharacterModelPart.AlternateForm,
                _ => null
            };
            if (part == null) return false;
            Model model = _model.Model;
            bool sourceRequested = _sourceRigPreview != null;
            CharacterWeightedRenderModel weighted;
            bool weightedReady = sourceRequested
                ? _sourceRigPreview!.TryGet(_scene, _hunter, part.Value, model, out weighted)
                : CharacterModelRuntime.TryGetWeighted(_scene, _hunter, part.Value, model, out weighted);
            if (weightedReady)
            {
                if (sourceRequested) weighted.PrepareSourcePreviewPresentation(presentationPose);
                else weighted.UpdatePalette(model);
                int weightedPolygonId = _scene.GetNextPolygonId();
                foreach (CharacterWeightedRenderSegment segment in weighted.Segments)
                {
                    Material material = model.Materials[segment.NativeMaterialIndex];
                    Vector3 emission = GetEmission(_model, material, segment.NativeMaterialIndex);
                    Vector4? color = GetRenderColor(_model, 0, material);
                    int? authoredAlbedo = segment.GetAlbedo(_scene, _recolor);
                    int? bindingOverride = GetBindingOverride(
                        _model, material, segment.NativeMaterialIndex) ?? authoredAlbedo;
                    bool authoredTexture = segment.AlbedoBinding.HasValue
                        && bindingOverride == authoredAlbedo;
                    var previousGx = _scene.SourceGxSubmission;
                    _scene.SourceGxSubmission = segment.SourceGx == null ? null : new(segment.SourceGx, _sourceMaterialSeconds);
                    var previousMaterial = _scene.CosmeticMaterialSubmission;
                    _scene.CosmeticMaterialSubmission = GetCosmeticMaterialOverride(
                        _model, material, segment.NativeMaterialIndex);
                    if (authoredTexture && _scene.CosmeticMaterialSubmission == default
                        && RenderOptions.AdvancedMaterials && segment.MaterialMaps.Any)
                        _scene.CosmeticMaterialSubmission = new(0, segment.MaterialMaps.Normal,
                            segment.MaterialMaps.Specular, segment.MaterialMaps.Emissive);
                    try
                    {
                        _scene.AddRenderItem(material, weightedPolygonId, Alpha,
                            emission, CurrentLight, Matrix4.Identity, WeightedPreviewTransform(weighted,presentationPose),
                            segment.ListId, segment.SourceJointPalette?.Count ?? weighted.Joints.Count,
                            segment.SourcePresentationPalette ?? weighted.MatrixPalette,
                            color, PaletteOverride, SelectionType.None,
                            BillboardMode.None, _drawScale, bindingOverride,
                            UseTexturedPlayerSkin(_model), GetPlayerOutlineColor(_model),
                            weightedSkinning: true, authoredTexture: authoredTexture,
                            authoredWrapS: segment.WrapS, authoredWrapT: segment.WrapT,
                            authoredDoubleSided: segment.DoubleSided,
                            authoredTransparent: authoredTexture && segment.Transparent);
                    }
                    finally
                    {
                        _scene.SourceGxSubmission = previousGx;
                        _scene.CosmeticMaterialSubmission = previousMaterial;
                    }
                }
                return true;
            }

            if (sourceRequested) return false;
            if (!CharacterModelRuntime.TryGetRigid(_scene, _hunter, part.Value,
                model, out CharacterRigidRenderModel replacement))
                return false;

            int polygonId = _scene.GetNextPolygonId();
            foreach (CharacterRigidRenderSegment segment in replacement.Segments)
            {
                Node node = model.Nodes[segment.NativeNodeIndex];
                if (!node.Enabled || !model.NodeParentsEnabled(node)) continue;
                Material material = model.Materials[segment.NativeMaterialIndex];
                Vector3 emission = GetEmission(_model, material, segment.NativeMaterialIndex);
                Vector4? color = GetRenderColor(_model, 0, material);
                int? authoredAlbedo = segment.GetAlbedo(_scene, _recolor);
                int? bindingOverride = GetBindingOverride(_model, material,
                    segment.NativeMaterialIndex) ?? authoredAlbedo;
                bool authoredTexture = segment.AlbedoBinding.HasValue && bindingOverride == authoredAlbedo;

                var previousMaterial = _scene.CosmeticMaterialSubmission;
                _scene.CosmeticMaterialSubmission = GetCosmeticMaterialOverride(
                    _model, material, segment.NativeMaterialIndex);
                if (authoredTexture && _scene.CosmeticMaterialSubmission == default
                    && RenderOptions.AdvancedMaterials && segment.MaterialMaps.Any)
                    _scene.CosmeticMaterialSubmission = new(0, segment.MaterialMaps.Normal,
                        segment.MaterialMaps.Specular, segment.MaterialMaps.Emissive);
                try
                {
                    _scene.AddRenderItem(material, polygonId, Alpha, emission, CurrentLight,
                        Matrix4.Identity, node.Animation, segment.ListId,
                        0, Array.Empty<float>(), color, PaletteOverride,
                        SelectionType.None, node.BillboardMode, _drawScale,
                        bindingOverride, UseTexturedPlayerSkin(_model),
                        GetPlayerOutlineColor(_model), authoredTexture: authoredTexture,
                        authoredWrapS: segment.WrapS, authoredWrapT: segment.WrapT,
                            authoredDoubleSided: segment.DoubleSided,
                            authoredTransparent: authoredTexture && segment.Transparent);
                }
                finally
                {
                    _scene.CosmeticMaterialSubmission = previousMaterial;
                }
            }
            return true;
        }

        // Explicit opt-in until hardware cadence and shared model restoration are verified.
        // Enabled by default for supported preview paths. Environment
        // overrides provide a fast reversible compatibility switch.
        private static readonly bool PreviewBlendEnabled = Mods.Render.AnimationRollout.Enabled(
            "PROJECT_PRIME_PREVIEW_ANIMATION_BLEND");
        private static readonly bool MaterialBlendEnabled = Mods.Render.AnimationRollout.Enabled(
            "PROJECT_PRIME_MATERIAL_ANIMATION_BLEND");
        private static readonly bool TexcoordBlendEnabled = Mods.Render.AnimationRollout.Enabled(
            "PROJECT_PRIME_TEXCOORD_ANIMATION_BLEND");

        public override void GetDrawInfo()
        {
            if (_model == null)
            {
                return;
            }
            // Turned to face the camera, and still.
            //
            // A hunter model is authored facing -Z, which is the direction a
            // player walks in, and the preview camera stands at +Z looking
            // back at the origin -- so the identity transform shows the back
            // of their head. Half a turn puts the face, the arm cannon and the
            // chest, which is the half of a hunter anybody recognises, towards
            // whoever is choosing.
            //
            // Still, because it turned on the spot first and that is the wrong
            // thing for a picker: the answer to "what does this suit look
            // like" should be the same every time you glance at it, not
            // whichever side happens to be towards you.
            Matrix4 pose = Matrix4.CreateScale(Mods.Cosmetics.CosmeticPreview.Zoom)
                * Matrix4.CreateRotationY(Mods.Cosmetics.CosmeticPreview.Yaw) * _facing;
            if (_sourceStageOwner != null && Mode == Mods.Cosmetics.SkinContext.ViewModel)
                pose = Matrix4.CreateScale(Mods.Cosmetics.CosmeticPreview.Zoom)
                    * Matrix4.CreateRotationY(Mods.Cosmetics.CosmeticPreview.Yaw - .7f);
            else if (Mode != Mods.Cosmetics.SkinContext.Biped)
                pose = Matrix4.CreateTranslation(-_displayCenter)
                    * Matrix4.CreateScale(_displayScale * Mods.Cosmetics.CosmeticPreview.Zoom)
                    * Matrix4.CreateRotationY(Mods.Cosmetics.CosmeticPreview.Yaw
                        + (Mode == Mods.Cosmetics.SkinContext.ViewModel ? -0.7f : MathF.PI + 0.45f))
                    * Matrix4.CreateTranslation(0, 1.05f, 0);
            if (_sourceStageOwner != null) pose = _sourceStageFit * pose;
            Matrix4 particlePose = pose;
            var surface = Mods.Cosmetics.CosmeticRuntime.Surface(_cosmetics, _cosmeticTime, false,
                firstPerson: Mode == Mods.Cosmetics.SkinContext.ViewModel, alt: Mode == Mods.Cosmetics.SkinContext.AltForm);
            bool presentingDeath = Mode == Mods.Cosmetics.SkinContext.Biped && _death.Active && Mods.RenderOptions.ShowCustomCosmetics
                && Mods.RenderOptions.CosmeticQuality != Mods.Cosmetics.CosmeticEffectQuality.Off
                && _cosmeticTime - _death.StartTime < _cosmetics.Death.Duration;
            bool dying = presentingDeath;
            float progress = dying ? _death.Progress(_cosmeticTime, _cosmetics.Death) : 0;
            if (dying)
            {
                pose = Mods.Cosmetics.Death.DeathPresentationRuntime.Pose(_cosmetics.Death, progress) * pose;
                surface = Mods.Cosmetics.Death.DeathPresentationRuntime.Surface(_cosmetics, _cosmetics.Death, _cosmeticTime, progress);
            }
            UpdateTransforms(_model, pose, _recolor < 0 ? 0 : _recolor);
            // The render-item writer snapshots the model palette. Restore the
            // integer pose afterwards so no simulation or other model owner
            // observes an interpolated transform from the preview pass.
            bool blended = false, materialBlended = false;
            var previous = _scene.CosmeticSubmission;
            var previousUvOwner = _presentationTexcoordOwner;
            var previousUvSample = _presentationTexcoordSample;
            try
            {
                bool presentationEnabled = PreviewBlendEnabled || MaterialBlendEnabled || TexcoordBlendEnabled;
                double phase = !presentationEnabled || _lastPreviewStepStamp == 0 ? 0
                    : Math.Clamp(System.Diagnostics.Stopwatch.GetElapsedTime(_lastPreviewStepStamp)
                        .TotalSeconds * 60.0, 0, 1);
                double drawStep = _previewSteps + phase;
                if (PreviewBlendEnabled && _previewSteps > 0
                    && _model.PresentationSamples.TryGet(_model.AnimInfo, _model.AnimInfo.Node.Slot,
                        drawStep, out var sample))
                {
                    blended = true;
                    _model.Model.AnimateNodes(0, UseNodeTransform, pose,
                        _model.Model.Scale, _model.AnimInfo, sample);
                    _model.Model.UpdateMatrixStack();
                }
                if (MaterialBlendEnabled && _previewSteps > 0
                    && _model.PresentationSamples.TryGet(_model.AnimInfo, _model.AnimInfo.Material.Slot,
                        drawStep, out var materialSample))
                {
                    materialBlended = true;
                    _model.Model.AnimateMaterials(_model.AnimInfo, materialSample);
                    _scene.UpdateMaterials(_model.Model, _recolor < 0 ? 0 : _recolor);
                }
                if (TexcoordBlendEnabled && _previewSteps > 0
                    && _model.PresentationSamples.TryGet(_model.AnimInfo, _model.AnimInfo.Texcoord.Slot,
                        drawStep, out var uvSample))
                {
                    _presentationTexcoordOwner = _model;
                    _presentationTexcoordSample = uvSample;
                }
                _scene.CosmeticSubmission = surface;
                if (!presentingDeath || (dying && _cosmetics.Death.WireId != 0
                    && progress < _cosmetics.Death.HideBodyAt))
                {
                    if (!TryDrawModernCharacter(pose))
                        GetDrawItems(_model, 0, CurrentLight);
                }
            }
            finally
            {
                try
                {
                    if (blended)
                    {
                        _model.Model.AnimateNodes(0, UseNodeTransform, pose,
                            _model.Model.Scale, _model.AnimInfo);
                        _model.Model.UpdateMatrixStack();
                    }
                }
                finally
                {
                    try
                    {
                        if (materialBlended)
                        {
                            // Animated alpha is read by some game logic, so
                            // restore the canonical channel even when drawing
                            // fails after a partial render-item submission.
                            _model.Model.AnimateMaterials(_model.AnimInfo);
                            _scene.UpdateMaterials(_model.Model, _recolor < 0 ? 0 : _recolor);
                        }
                    }
                    finally
                    {
                        _presentationTexcoordOwner = previousUvOwner;
                        _presentationTexcoordSample = previousUvSample;
                        _scene.CosmeticSubmission = previous;
                    }
                }
            }
            if (dying) Mods.Cosmetics.Armor.ArmorEffectParticles.DrawDeath(_scene, Vector3.Zero, _cosmetics.Death, progress, 17, particlePose);
            else if (!presentingDeath && Mode != Mods.Cosmetics.SkinContext.ViewModel && Mods.RenderOptions.ShowCustomCosmetics)
                Mods.Cosmetics.Armor.ArmorEffectParticles.Draw(_scene, _model.Model, Vector3.Zero, _cosmetics.Armor,
                    _cosmeticTime, 17, Mods.Cosmetics.CosmeticLod.Near, Mode == Mods.Cosmetics.SkinContext.AltForm, _hunter, pose);
        }
    }
}
