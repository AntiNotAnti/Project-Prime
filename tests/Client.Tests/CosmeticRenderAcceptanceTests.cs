using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Cosmetics;
using MphRead.Cosmetics.Presentation;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace MphRead.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CosmeticPerformanceCollection
{
    public const string Name = "Cosmetic performance";
}

[Collection(CosmeticPerformanceCollection.Name)]
public sealed class CosmeticSubmissionAcceptanceTests
{
    private static readonly int[] SingleRootNodeMatrixIds = [0];
    private readonly ITestOutputHelper _output;

    public CosmeticSubmissionAcceptanceTests(ITestOutputHelper output)
        => _output = output;

    private static readonly AcceptanceScenario[] AcceptanceScenarios =
    [
        new("samus-base", 1),
        new("samus-skin", 1, SkinId: BuiltInCosmeticIds.SkinSamusSolar),
        new("samus-lightning", 1, ArmorEffectId: BuiltInCosmeticIds.ArmorLightning),
        new("samus-inferno", 1, ArmorEffectId: BuiltInCosmeticIds.ArmorInferno),
        new("samus-phase", 1, ArmorEffectId: BuiltInCosmeticIds.ArmorPhase),
        new("samus-quantum-death", 1, DeathEffectId: BuiltInCosmeticIds.DeathQuantum),
        new("samus-spectral-death", 1, DeathEffectId: BuiltInCosmeticIds.DeathSpectral),
        new("samus-inferno-death", 1, DeathEffectId: BuiltInCosmeticIds.DeathInfernoBurnout),
        new("eight-player-cosmetics", 8,
            ArmorEffectId: BuiltInCosmeticIds.ArmorThunderstorm),
        new("first-person-cosmetics", 1,
            ArmorEffectId: BuiltInCosmeticIds.ArmorLightning, FirstPerson: true),
        new("team-colors-with-skins", 2,
            SkinId: BuiltInCosmeticIds.SkinSamusObsidian, StrongTeamColors: true)
    ];

    public static TheoryData<string> RequiredSceneNames => new()
    {
        "samus-base",
        "samus-skin",
        "samus-lightning",
        "samus-inferno",
        "samus-phase",
        "samus-quantum-death",
        "samus-spectral-death",
        "samus-inferno-death",
        "eight-player-cosmetics",
        "first-person-cosmetics",
        "team-colors-with-skins"
    };

    public static TheoryData<string, int, CosmeticQuality, ushort, bool> Benchmarks => new()
    {
        { "0 cosmetics", 0, CosmeticQuality.Full, 0, false },
        { "1 local cosmetic player", 1, CosmeticQuality.Full,
            BuiltInCosmeticIds.ArmorLightning, false },
        { "8 full cosmetic players", 8, CosmeticQuality.Full,
            BuiltInCosmeticIds.ArmorLightning, false },
        { "8 reduced cosmetic players", 8, CosmeticQuality.Reduced,
            BuiltInCosmeticIds.ArmorLightning, false },
        { "particle-heavy effects", 8, CosmeticQuality.Full,
            BuiltInCosmeticIds.ArmorThunderstorm, false },
        { "distortion-heavy effects", 8, CosmeticQuality.Full,
            BuiltInCosmeticIds.ArmorPhase, false },
        { "death burst scenario", 8, CosmeticQuality.Full, 0, true }
    };

    [Theory]
    [MemberData(nameof(RequiredSceneNames))]
    public void RequiredNamedSceneHasDeterministicBoundedSubmissionCapture(
        string sceneName)
    {
        AcceptanceScenario scenario = Assert.Single(AcceptanceScenarios,
            value => value.Name == sceneName);

        SubmissionCapture first = Capture(scenario);
        SubmissionCapture second = Capture(scenario);

        Assert.Equal(first, second);
        Assert.Equal(scenario.PlayerCount, first.BodyCount);
        Assert.True(first.ExpectedBodyPresent);
        Assert.True(first.AllValuesFinite);
        Assert.InRange(first.MaximumEmissionStrength, 0, 16);
        Assert.InRange(first.ParticleEmitters, 0,
            CosmeticBudgetLimits.Default.ParticleEmitters);
        Assert.InRange(first.Particles, 0,
            CosmeticBudgetLimits.Default.Particles);
        Assert.InRange(first.RibbonSystems, 0,
            CosmeticBudgetLimits.Default.RibbonSystems);
        Assert.InRange(first.RibbonSegments, 0,
            CosmeticBudgetLimits.Default.RibbonSegments);
        Assert.InRange(first.DistortionSources, 0,
            CosmeticBudgetLimits.Default.DistortionSources);
        Assert.InRange(first.LocalLights, 0,
            CosmeticBudgetLimits.Default.LocalLights);

        if (scenario.FirstPerson)
        {
            Assert.Equal(0, first.DistortionSources);
            Assert.InRange(first.Particles, 0, 4);
        }
        if (scenario.StrongTeamColors) Assert.True(first.StrongTeamColors);
        if (scenario.DeathEffectId != 0)
        {
            Assert.True(first.DeathBodySampled);
            Assert.InRange(first.MinimumBodyAlpha, float.Epsilon, 1);
        }
    }

    [Theory]
    [MemberData(nameof(Benchmarks))]
    public void CosmeticSteadyStateBenchmarkIsBoundedFiniteAndAllocationFree(
        string name, int playerCount, CosmeticQuality quality, ushort armorEffectId,
        bool deathBurst)
    {
        var benchmark = new CosmeticFrameBenchmark(playerCount, quality,
            armorEffectId, deathBurst);
        for (int i = 0; i < 128; i++) benchmark.RunFrame();

        const int frames = 2_000;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < frames; i++) benchmark.RunFrame();
        long elapsed = Stopwatch.GetTimestamp() - started;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        double cpuMicrosecondsPerFrame = elapsed * 1_000_000d
            / Stopwatch.Frequency / frames;
        int gen0Collections = GC.CollectionCount(0) - gen0Before;
        int gen1Collections = GC.CollectionCount(1) - gen1Before;
        int gen2Collections = GC.CollectionCount(2) - gen2Before;
        Assert.True(double.IsFinite(cpuMicrosecondsPerFrame), name);
        Assert.InRange(cpuMicrosecondsPerFrame, 0, double.MaxValue);
        Assert.Equal(0, allocated);
        Assert.True(benchmark.AllValuesFinite, name);
        Assert.InRange(benchmark.LastPrimitiveCount, 0,
            CosmeticPrimitiveSubmissionBuffer.MaximumCapacity);
        Assert.InRange(benchmark.LastParticles, 0,
            CosmeticBudgetLimits.Default.Particles);
        Assert.InRange(benchmark.LastRibbonSegments, 0,
            CosmeticBudgetLimits.Default.RibbonSegments);
        Assert.InRange(benchmark.LastDistortionSources, 0,
            CosmeticBudgetLimits.Default.DistortionSources);
        Assert.InRange(gen0Collections, 0, int.MaxValue);
        Assert.InRange(gen1Collections, 0, int.MaxValue);
        Assert.InRange(gen2Collections, 0, int.MaxValue);
        _output.WriteLine(
            $"{name}: cpu={cpuMicrosecondsPerFrame:F3} us/frame; "
            + $"managed={allocated / (double)frames:F3} B/frame; "
            + $"gc=({gen0Collections},{gen1Collections},{gen2Collections}); "
            + $"primitives={benchmark.LastPrimitiveCount}; "
            + $"particles={benchmark.LastParticles}; "
            + $"ribbon_segments={benchmark.LastRibbonSegments}; "
            + $"distortion={benchmark.LastDistortionSources}; "
            + "gpu_time=unavailable; gpu_memory=unavailable; "
            + "draw_calls=unavailable; texture_uploads=unavailable");
    }

    private static SubmissionCapture Capture(AcceptanceScenario scenario)
    {
        var benchmark = new CosmeticFrameBenchmark(scenario.PlayerCount,
            CosmeticQuality.Full, scenario.ArmorEffectId, deathBurst: false,
            scenario.FirstPerson, scenario.StrongTeamColors);
        benchmark.RunFrame();

        float bodyAlpha = 1;
        float deathEmission = 0;
        bool deathBodySampled = false;
        if (scenario.DeathEffectId != 0)
        {
            CapturedDeathPose pose = CapturedPose(scenario.DeathEffectId,
                scenario.SkinId);
            var runtime = new DeathPresentationRuntime();
            DeathPresentationSample sample = default;
            deathBodySampled = runtime.Begin(new CombatActor(0, 1, 1), 100,
                    scenario.DeathEffectId, pose)
                && runtime.TrySample(136, out sample);
            if (deathBodySampled)
            {
                bodyAlpha = sample.BodyAlpha;
                deathEmission = sample.EmissionStrength;
                AssertFinite(sample.EmissionTint);
                Assert.All(sample.Nodes, AssertFinite);
                Assert.All(sample.MatrixStack, value => Assert.True(float.IsFinite(value)));
            }
        }

        if (scenario.SkinId != 0)
        {
            Assert.True(CosmeticCatalog.BuiltIn.TryGetSkin(scenario.SkinId,
                out SkinDefinition skin));
            Assert.Equal(Hunter.Samus, skin.Hunter);
        }

        return new SubmissionCapture(scenario.PlayerCount,
            ExpectedBodyPresent: scenario.PlayerCount > 0,
            AllValuesFinite: benchmark.AllValuesFinite && float.IsFinite(bodyAlpha)
                && float.IsFinite(deathEmission),
            benchmark.MaximumEmissionStrength,
            benchmark.LastParticleEmitters, benchmark.LastParticles,
            benchmark.LastRibbonSystems, benchmark.LastRibbonSegments,
            benchmark.LastDistortionSources, benchmark.LastLocalLights,
            DeathBodySampled: deathBodySampled,
            MinimumBodyAlpha: bodyAlpha,
            StrongTeamColors: scenario.StrongTeamColors);
    }

    private static CapturedDeathPose CapturedPose(ushort deathEffectId,
        ushort skinId)
    {
        Model model = (Model)RuntimeHelpers.GetUninitializedObject(typeof(Model));
        Node node = (Node)RuntimeHelpers.GetUninitializedObject(typeof(Node));
        Set(node, "Name", "Root");
        Set(node, "ParentIndex", -1);
        Set(node, "ChildIndex", -1);
        Set(node, "NextIndex", -1);
        node.Scale = Vector3.One;
        Set(model, "Nodes", new List<Node> { node });
        Set(model, "Scale", Vector3.One);
        Set(model, "NodeMatrixIds", SingleRootNodeMatrixIds);
        typeof(Model).GetField("_matrixStackValues",
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, new float[16]);

        var pose = new CapturedDeathPose();
        pose.Capture(model, Matrix4.Identity, new[] { Matrix4.Identity },
            new float[16], new CapturedDeathAppearance(Hunter.Samus,
                new CosmeticLoadoutIds(skinId, 0, deathEffectId), 0, 1));
        return pose;
    }

    private static void Set(object value, string name, object field)
        => value.GetType().GetField($"<{name}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, field);

    private static void AssertFinite(Matrix4 matrix)
    {
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                Assert.True(float.IsFinite(matrix[row, column]));
    }

    private static void AssertFinite(Vector3 value)
    {
        Assert.True(float.IsFinite(value.X));
        Assert.True(float.IsFinite(value.Y));
        Assert.True(float.IsFinite(value.Z));
    }

    private readonly record struct AcceptanceScenario(string Name, int PlayerCount,
        ushort SkinId = 0, ushort ArmorEffectId = 0, ushort DeathEffectId = 0,
        bool FirstPerson = false, bool StrongTeamColors = false);

    private readonly record struct SubmissionCapture(int BodyCount,
        bool ExpectedBodyPresent, bool AllValuesFinite,
        float MaximumEmissionStrength, int ParticleEmitters, int Particles,
        int RibbonSystems, int RibbonSegments, int DistortionSources,
        int LocalLights, bool DeathBodySampled, float MinimumBodyAlpha,
        bool StrongTeamColors);

    private sealed class CosmeticFrameBenchmark
    {
        private static readonly ArmorEffectAnchorSet Anchors = new(
            Root: Vector3.Zero, Head: new Vector3(0, 1.8f, 0),
            Chest: new Vector3(0, 1.2f, 0),
            LeftShoulder: new Vector3(-.4f, 1.4f, 0),
            RightShoulder: new Vector3(.4f, 1.4f, 0),
            LeftHand: new Vector3(-.7f, .9f, 0),
            RightHand: new Vector3(.7f, .9f, 0),
            LeftFoot: new Vector3(-.2f, 0, 0),
            RightFoot: new Vector3(.2f, 0, 0),
            Weapon: new Vector3(.8f, 1.1f, 0));

        private readonly int _playerCount;
        private readonly CosmeticPresentationSettings _settings;
        private readonly ArmorEffectPresentation[] _armor;
        private readonly ArmorEffectFrame[] _frames;
        private readonly CosmeticBudgetRequest[] _requests;
        private readonly CosmeticBudgetAllowance[] _allowances;
        private readonly DeathPresentationRuntime[] _deaths;
        private readonly CapturedDeathPose? _deathPose;
        private readonly CosmeticPrimitiveSubmissionBuffer _submissions = new();
        private readonly bool _firstPerson;
        private uint _tick = 100;

        public int LastPrimitiveCount { get; private set; }
        public int LastParticleEmitters { get; private set; }
        public int LastParticles { get; private set; }
        public int LastRibbonSystems { get; private set; }
        public int LastRibbonSegments { get; private set; }
        public int LastDistortionSources { get; private set; }
        public int LastLocalLights { get; private set; }
        public float MaximumEmissionStrength { get; private set; }
        public bool AllValuesFinite { get; private set; } = true;

        public CosmeticFrameBenchmark(int playerCount, CosmeticQuality quality,
            ushort armorEffectId, bool deathBurst, bool firstPerson = false,
            bool strongTeamColors = false)
        {
            _playerCount = playerCount;
            _settings = CosmeticPresentationSettings.DesktopDefault with
            {
                Quality = quality,
                ForceStrongTeamColors = strongTeamColors
            };
            _firstPerson = firstPerson;
            _armor = new ArmorEffectPresentation[playerCount];
            _frames = new ArmorEffectFrame[playerCount];
            _requests = new CosmeticBudgetRequest[playerCount];
            _allowances = new CosmeticBudgetAllowance[playerCount];
            _deaths = new DeathPresentationRuntime[deathBurst ? playerCount : 0];
            for (int i = 0; i < playerCount; i++)
            {
                _armor[i] = new ArmorEffectPresentation();
                _armor[i].Select(armorEffectId);
            }
            if (deathBurst)
            {
                _deathPose = CapturedPose(BuiltInCosmeticIds.DeathQuantum, 0);
                for (int i = 0; i < _deaths.Length; i++)
                {
                    _deaths[i] = new DeathPresentationRuntime();
                    _deaths[i].Begin(new CombatActor((byte)i, 1, 1), _tick,
                        (ushort)(1 + i % 3), _deathPose);
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RunFrame()
        {
            _submissions.Clear();
            LastPrimitiveCount = LastParticleEmitters = LastParticles = 0;
            LastRibbonSystems = LastRibbonSegments = LastDistortionSources = 0;
            LastLocalLights = 0;
            MaximumEmissionStrength = 0;
            AllValuesFinite = true;

            int requestCount = 0;
            for (int i = 0; i < _playerCount; i++)
            {
                bool local = i == 0;
                var state = new CosmeticPlayerPresentationState((byte)i, local,
                    _firstPerson && local, Spectator: false, HiddenModel: false,
                    DeathTakeover: false, AltForm: false,
                    CosmeticAltFormMode.RootOnly, CosmeticVisibility.Visible,
                    DistanceSquared: i * i);
                if (_armor[i].TryEvaluate(_settings, state, _tick, .5f,
                    0x1000UL + (uint)i, out ArmorEffectFrame frame))
                {
                    _frames[requestCount] = frame;
                    _requests[requestCount++] = frame.BudgetRequest;
                    MaximumEmissionStrength = Math.Max(MaximumEmissionStrength,
                        frame.EmissionStrength);
                    AllValuesFinite &= float.IsFinite(frame.EmissionStrength);
                }
            }

            int admitted = CosmeticBudgetArbiter.Admit(
                _requests.AsSpan(0, requestCount),
                _allowances.AsSpan(0, requestCount));
            for (int i = 0; i < admitted; i++)
            {
                CosmeticBudgetAllowance allowance = _allowances[i];
                LastParticleEmitters += allowance.ParticleEmitters;
                LastParticles += allowance.Particles;
                LastRibbonSystems += allowance.RibbonSystems;
                LastRibbonSegments += allowance.RibbonSegments;
                LastDistortionSources += allowance.DistortionSources;
                LastLocalLights += allowance.LocalLights;
                for (int frameIndex = 0; frameIndex < requestCount; frameIndex++)
                {
                    if (_frames[frameIndex].BudgetRequest.StableKey == allowance.StableKey)
                    {
                        _armor[_frames[frameIndex].BudgetRequest.PlayerSlot]
                            .SubmitPrimitives(_frames[frameIndex], allowance,
                                Anchors, _submissions);
                        break;
                    }
                }
            }
            LastPrimitiveCount = _submissions.Count;
            IReadOnlyList<CosmeticPrimitiveSubmission> sealedSubmissions
                = _submissions.Seal();
            for (int i = 0; i < sealedSubmissions.Count; i++)
            {
                CosmeticPrimitiveSubmission submission = sealedSubmissions[i];
                AllValuesFinite &= IsFinite(submission.Start)
                    && IsFinite(submission.End) && IsFinite(submission.Color)
                    && float.IsFinite(submission.Intensity);
            }

            for (int i = 0; i < _deaths.Length; i++)
            {
                if (!_deaths[i].TrySample(_tick, out DeathPresentationSample sample))
                {
                    _deaths[i].Begin(new CombatActor((byte)i, 1, 1), _tick,
                        (ushort)(1 + i % 3), _deathPose!);
                    _deaths[i].TrySample(_tick, out sample);
                }
                MaximumEmissionStrength = Math.Max(MaximumEmissionStrength,
                    sample.EmissionStrength);
                AllValuesFinite &= float.IsFinite(sample.Progress)
                    && float.IsFinite(sample.BodyAlpha)
                    && float.IsFinite(sample.EmissionStrength)
                    && IsFinite(sample.EmissionTint);
            }
            _tick++;
        }

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z);
    }
}
