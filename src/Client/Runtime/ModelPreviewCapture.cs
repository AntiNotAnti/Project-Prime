using System;
using System.IO;
using MphRead.Cosmetics;
using MphRead.Cosmetics.Presentation;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods;

/// <summary>Desktop renderer-worker lifecycle for one actual local game model.</summary>
internal sealed class ModelPreviewCapture : IRenderToolClient
{
    private readonly IRenderToolHost _host;
    private readonly ModelPreviewSpec _spec;
    private readonly string _outputPath;
    private int _frames = 2;
    private int _attempts;
    private bool _saved;
    private readonly Vector3 _cameraPosition;
    private readonly Vector3 _cameraTarget;
    private readonly DeathPreviewModelEntity? _deathModel;
    private readonly ArmorEffectPresentation? _armor;
    private readonly CosmeticPrimitiveSubmissionBuffer _armorSubmissions = new();

    private ModelPreviewCapture(ModelPreviewSpec spec, string outputPath, IRenderToolHost host)
    {
        _spec = spec;
        _outputPath = outputPath;
        _host = host;
        Scene = new Scene(features: ClientMatchFeatures.Capture());
        Presentation = host.CreatePresentation(Scene);
        Presentation.IsolatedPresentationSubmission = SubmitPreviewCosmetics;
        ModelInstance source = Read.GetModelInstance(spec.ModelName);
        EntityBase model;
        if (spec.DeathEffectId != 0)
        {
            _deathModel = new DeathPreviewModelEntity(
                Read.GetModelInstance(spec.ModelName), Scene, spec);
            Scene.InsertEntity(_deathModel);
            Presentation.InitEntity(_deathModel);
            model = _deathModel;
        }
        else model = Presentation.AddModel(spec.ModelName);
        if (spec.ArmorEffectId != 0)
        {
            _armor = new ArmorEffectPresentation();
            _armor.Select(spec.ArmorEffectId);
            if (!_armor.Active)
                throw new InvalidDataException(
                    $"Armor preview '{spec.WorkerKey}' was rejected.");
        }
        model.Recolor = spec.BaseRecolor;
        Vector3 scale = source.Model.Scale * spec.Scale;
        model.Scale = new Vector3(spec.Scale);
        model.Rotation = new Vector3(0, MathF.PI, 0);
        Vector3 min = new(Single.MaxValue);
        Vector3 max = new(Single.MinValue);
        foreach (Node node in source.Model.Nodes)
        {
            if (node.Bounds.Length < 6) continue;
            min = Vector3.ComponentMin(min,
                new Vector3(node.Bounds[0], node.Bounds[1], node.Bounds[2]) * scale);
            max = Vector3.ComponentMax(max,
                new Vector3(node.Bounds[3], node.Bounds[4], node.Bounds[5]) * scale);
        }
        Vector3 center = (min + max) / 2;
        float halfExtent = Math.Max((max.X - min.X) / 2, (max.Y - min.Y) / 2);
        halfExtent = Math.Max(halfExtent, (max.Z - min.Z) / 2);
        if (!Single.IsFinite(halfExtent) || halfExtent < 0.01f) halfExtent = 1;
        _cameraTarget = new Vector3(-center.X,
            center.Y - (spec.DeathEffectId == 0 ? 0 : halfExtent * .45f),
            -center.Z);
        float distance = halfExtent * spec.FrameMargin
            / MathF.Tan(MathHelper.DegreesToRadians(27.5f));
        _cameraPosition = _cameraTarget + new Vector3(0, 0, Math.Max(0.1f, distance));
        Console.WriteLine($"[previews] {spec.WorkerKey} bounds {min}..{max}, "
            + $"camera {_cameraPosition} -> {_cameraTarget}");
    }

    public Scene Scene { get; }
    public ScenePresentation Presentation { get; }
    public bool Succeeded => _saved;

    public void OnLoad()
    {
        Presentation.Size = _host.Size;
        Presentation.OnLoad();
        Presentation.OnResize();
    }

    public void OnFrame()
    {
        Presentation.OnUpdateFrame();
        Presentation.SetPreviewCamera(_cameraPosition, _cameraTarget);
        if (_frames-- > 0) return;
        bool armorStageReady = _armor == null || _deathModel?.Sample != null;
        var request = new RenderToolCapture(CaptureTargetKind.SceneTarget, _outputPath);
        RenderToolFrameResult frame = _host.Render(Presentation, request);
        if (!armorStageReady)
        {
            if (!frame.Submitted) _host.Close();
            else _frames = 0;
            return;
        }
        for (int i = 0; i < frame.Captures.Count; i++) OnCapture(frame.Captures[i]);
        if (frame.Submitted) _attempts++;
        if (!frame.Submitted || _saved || _attempts >= 3) _host.Close();
        else _frames = 1;
    }

    private void SubmitPreviewCosmetics()
    {
        AddDeathStageParticles();
        AddArmorStage();
    }

    private void AddDeathStageParticles()
    {
        if (_deathModel?.Sample is not DeathPresentationSample sample) return;
        for (int i = 1; i < sample.Nodes.Length; i += 2)
        {
            Presentation.AddSingleParticle(SingleType.Death,
                sample.Nodes[i].Row3.Xyz, sample.EmissionTint,
                Math.Clamp(1 - sample.Progress, 0, 1), .12f);
        }
    }

    private void AddArmorStage()
    {
        if (_armor == null || _deathModel?.Sample is not DeathPresentationSample death)
            return;
        var state = new CosmeticPlayerPresentationState(PlayerSlot: 0,
            LocalPlayer: true, FirstPerson: false, Spectator: false,
            HiddenModel: false, DeathTakeover: false, AltForm: false,
            CosmeticAltFormMode.RootOnly, CosmeticVisibility.Visible,
            DistanceSquared: 0);
        ulong seed = CosmeticSeed.Derive(Guid.Empty, 0, 1,
            _spec.ArmorEffectId, eventTick: 0);
        if (!_armor.TryEvaluate(CosmeticPresentationSettings.DesktopDefault,
                state, _spec.DeathSampleTick, renderAlpha: 0, seed,
                out ArmorEffectFrame frame))
            return;
        Span<CosmeticBudgetRequest> requests = stackalloc CosmeticBudgetRequest[1];
        Span<CosmeticBudgetAllowance> allowances = stackalloc CosmeticBudgetAllowance[1];
        requests[0] = frame.BudgetRequest;
        CosmeticBudgetArbiter.Admit(requests, allowances);
        Vector3 root = death.Nodes.Length == 0 ? Vector3.Zero
            : death.Nodes[0].Row3.Xyz;
        Vector3 head = death.Nodes.Length < 2 ? root + Vector3.UnitY
            : death.Nodes[^1].Row3.Xyz;
        Vector3 chest = Vector3.Lerp(root, head, .6f);
        var anchors = new ArmorEffectAnchorSet(root, head, chest,
            chest + new Vector3(-.3f, .1f, 0),
            chest + new Vector3(.3f, .1f, 0),
            chest + new Vector3(-.55f, -.2f, 0),
            chest + new Vector3(.55f, -.2f, 0),
            root + new Vector3(-.18f, 0, 0),
            root + new Vector3(.18f, 0, 0),
            chest + new Vector3(.65f, 0, 0));
        _armorSubmissions.Clear();
        _armor.SubmitPrimitives(frame, allowances[0], anchors,
            _armorSubmissions);
        Presentation.FlushCosmeticSubmissions(_armorSubmissions.Seal());
        _deathModel.ApplyArmorEmission(frame.EmissionStrength,
            frame.Recipe.Definition.Material?.EmissionTint);
    }

    /// <summary>
    /// Isolated preview-only body. Its draw-time transform override samples
    /// the same captured-pose runtime as gameplay presentation, but it owns no
    /// PlayerEntity, network identity, simulation state, or gameplay timer.
    /// </summary>
    private sealed class DeathPreviewModelEntity : ModelEntity
    {
        private readonly ModelPreviewSpec _spec;
        private readonly Hunter _hunter;
        private readonly DeathPresentationRuntime _runtime = new();
        private bool _initialized;
        private float _armorEmissionStrength;
        private CosmeticColor? _armorEmissionTint;

        public DeathPreviewModelEntity(ModelInstance model, Scene scene,
            ModelPreviewSpec spec) : base(model, scene, spec.BaseRecolor)
        {
            _spec = spec;
            _hunter = Enum.Parse<Hunter>(spec.Key, ignoreCase: true);
        }

        public DeathPresentationSample? Sample { get; private set; }

        public void ApplyArmorEmission(float strength, CosmeticColor? tint)
        {
            _armorEmissionStrength = strength;
            _armorEmissionTint = tint;
        }

        protected internal override void UpdateTransforms(ModelInstance instance,
            int index, bool transformRoomNodes = false)
        {
            base.UpdateTransforms(instance, index, transformRoomNodes);
            if (!_initialized)
            {
                ushort skinId = 0;
                if (_spec.SkinKey != null
                    && CosmeticCatalog.BuiltIn.TryGetSkin(_spec.SkinKey,
                        out SkinDefinition skin))
                    skinId = skin.Id;
                var pose = new CapturedDeathPose();
                pose.CaptureSubmitted(instance.Model, Transform,
                    submittedNodes: null, submittedStack: null,
                    new CapturedDeathAppearance(_hunter,
                        new CosmeticLoadoutIds(skinId, _spec.ArmorEffectId,
                            _spec.DeathEffectId),
                        _spec.BaseRecolor, 1));
                _initialized = _runtime.Begin(new CombatActor(0, 1, 1),
                    authoritativeTick: 0, _spec.DeathEffectId, pose);
                if (!_initialized)
                    throw new InvalidDataException(
                        $"Death preview '{_spec.WorkerKey}' was rejected.");
            }
            if (!_runtime.TrySample(_spec.DeathSampleTick,
                    out DeathPresentationSample sample))
                throw new InvalidDataException(
                    $"Death preview '{_spec.WorkerKey}' has no bounded sample.");
            for (int i = 0; i < sample.Nodes.Length; i++)
                instance.Model.Nodes[i].Animation = sample.Nodes[i];
            instance.Model.UpdateMatrixStack();
            Alpha = sample.BodyAlpha;
            PaletteOverride = sample.EmissionStrength > 0
                ? new Vector4(sample.EmissionTint, 1) : null;
            if (_armorEmissionStrength > 0
                && _armorEmissionTint is CosmeticColor color)
            {
                float blend = Math.Clamp(_armorEmissionStrength / 4, 0, .5f);
                Vector3 armorTint = new(color.R, color.G, color.B);
                PaletteOverride = new Vector4(Vector3.Lerp(sample.EmissionTint,
                    armorTint, blend), 1);
            }
            Sample = sample;
        }
    }

    public void OnCapture(RenderCaptureResult capture)
    {
        if (_saved || capture.Target != CaptureTargetKind.SceneTarget) return;
        _saved = SaveCandidate(capture);
        if (_saved) _host.Close();
    }

    private bool SaveCandidate(RenderCaptureResult capture)
    {
        double lit = RenderToolCaptureSupport.NonBlackFraction(capture);
        Console.WriteLine($"[previews] {_spec.WorkerKey} candidate {lit * 100:0.00}% lit");
        if (lit < 0.0025) return false;
        Action<byte[], int, int, string>? writer = ScreenCapture.PngWriter;
        if (writer == null) return false;
        string? directory = Path.GetDirectoryName(_outputPath);
        if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        writer(capture.CopyBytes(), capture.Width, capture.Height, _outputPath);
        return true;
    }

    public void OnClosing() => Presentation.DoCleanup();

    public static bool Capture(ModelPreviewSpec spec, int width, int height)
    {
        string path;
        try { path = ModelPreviewCatalog.CurrentPath(spec); }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[previews] identity failed: {error.Message}");
            return false;
        }
        string temporary = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            ThumbnailMode.Enter();
            RenderOptions.Lighting = false;
            RenderOptions.Fog = false;
            using IRenderToolHost host = RenderToolHostFactory.Create(
                new Vector2i(width, height), $"{Branding.Name} model preview",
                visible: false, presentable: false);
            var capture = new ModelPreviewCapture(spec, temporary, host);
            host.Run(capture);
            if (!capture.Succeeded) return false;
            string? directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[previews] {spec.WorkerKey}: {error}");
            return false;
        }
        finally
        {
            ThumbnailMode.Exit();
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }
}
