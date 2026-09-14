using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MphRead.Cosmetics;
using MphRead.Cosmetics.Presentation;
using MphRead.Mods;
using MphRead.Mods.Launcher.Settings;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class CosmeticPresentationTests
{
    [Fact]
    public void DefaultsSelectFullDesktopAndReducedMobile()
    {
        Assert.Equal(CosmeticQuality.Full,
            CosmeticPresentationSettings.CreateDefault(false).Quality);
        Assert.Equal(CosmeticQuality.Reduced,
            CosmeticPresentationSettings.CreateDefault(true).Quality);
        Assert.True(CosmeticPresentationSettings.DesktopDefault.ShowOtherPlayerCosmetics);
    }

    [Fact]
    public void PersistedPreferencesUsePlatformFallbacksAndExplicitAccessibilityValues()
    {
        Assert.Equal(CosmeticQuality.Full,
            CosmeticPresentationPreferences.Parse(new MenuSettings(),
                mobileOrLowPower: false).Quality);
        Assert.Equal(CosmeticQuality.Reduced,
            CosmeticPresentationPreferences.Parse(new MenuSettings(),
                mobileOrLowPower: true).Quality);

        var settings = new MenuSettings
        {
            CosmeticQuality = "off",
            ShowOtherPlayerCosmetics = "off",
            ReduceCosmeticFlashes = "on",
            ForceStrongTeamColors = "on",
            DisableCosmeticDistortion = "on",
            DisableCosmeticParticles = "on"
        };
        CosmeticPresentationSettings parsed = CosmeticPresentationPreferences.Parse(
            settings, mobileOrLowPower: false);
        Assert.Equal(CosmeticQuality.Off, parsed.Quality);
        Assert.False(parsed.ShowOtherPlayerCosmetics);
        Assert.True(parsed.ReduceCosmeticFlashes);
        Assert.True(parsed.ForceStrongTeamColors);
        Assert.True(parsed.DisableCosmeticDistortion);
        Assert.True(parsed.DisableCosmeticParticles);

        settings.CosmeticQuality = "unsupported";
        Assert.Equal(CosmeticQuality.Reduced,
            CosmeticPresentationPreferences.Parse(settings,
                mobileOrLowPower: true).Quality);
    }

    [Fact]
    public void PublishedPreferenceSnapshotNeverTearsAcrossFields()
    {
        CosmeticPresentationSettings original = CosmeticPresentationPreferences.Current;
        var full = new MenuSettings
        {
            CosmeticQuality = "full",
            ShowOtherPlayerCosmetics = "on",
            ReduceCosmeticFlashes = "off",
            ForceStrongTeamColors = "off",
            DisableCosmeticDistortion = "off",
            DisableCosmeticParticles = "off"
        };
        var reduced = new MenuSettings
        {
            CosmeticQuality = "reduced",
            ShowOtherPlayerCosmetics = "off",
            ReduceCosmeticFlashes = "on",
            ForceStrongTeamColors = "on",
            DisableCosmeticDistortion = "on",
            DisableCosmeticParticles = "on"
        };
        CosmeticPresentationSettings first = CosmeticPresentationPreferences.Parse(
            full, mobileOrLowPower: false);
        CosmeticPresentationSettings second = CosmeticPresentationPreferences.Parse(
            reduced, mobileOrLowPower: false);
        try
        {
            Parallel.For(0, 20_000, index =>
            {
                if ((index & 3) == 0)
                {
                    CosmeticPresentationPreferences.Apply(
                        (index & 4) == 0 ? full : reduced,
                        mobileOrLowPower: false);
                }
                else
                {
                    CosmeticPresentationSettings observed
                        = CosmeticPresentationPreferences.Current;
                    Assert.True(observed == first || observed == second);
                }
            });
        }
        finally
        {
            var restore = new MenuSettings
            {
                CosmeticQuality = original.Quality.ToString(),
                ShowOtherPlayerCosmetics = original.ShowOtherPlayerCosmetics ? "on" : "off",
                ReduceCosmeticFlashes = original.ReduceCosmeticFlashes ? "on" : "off",
                ForceStrongTeamColors = original.ForceStrongTeamColors ? "on" : "off",
                DisableCosmeticDistortion = original.DisableCosmeticDistortion ? "on" : "off",
                DisableCosmeticParticles = original.DisableCosmeticParticles ? "on" : "off"
            };
            CosmeticPresentationPreferences.Apply(restore,
                mobileOrLowPower: false);
        }
    }

    [Fact]
    public void SettingRegistryExposesEveryCosmeticPreference()
    {
        string[] ids =
        [
            SettingRowIds.CosmeticQuality,
            SettingRowIds.ShowOtherPlayerCosmetics,
            SettingRowIds.ReduceCosmeticFlashes,
            SettingRowIds.ForceStrongTeamColors,
            SettingRowIds.DisableCosmeticDistortion,
            SettingRowIds.DisableCosmeticParticles
        ];
        foreach (string id in ids)
        {
            Assert.Contains(id, SettingRowIds.Fixed);
            Assert.Equal(id, SettingRegistry.Get(id).RowId);
        }
    }

    [Fact]
    public void QualityOffAndOtherPlayerToggleSuppressWithoutAffectingLocal()
    {
        CosmeticPresentationPlan off = CosmeticQualityPolicy.Evaluate(
            CosmeticPresentationSettings.DesktopDefault with { Quality = CosmeticQuality.Off },
            State());
        Assert.True(off.Suppression.HasFlag(CosmeticSuppressionReason.QualityOff));
        Assert.Equal(CosmeticRenderFeatures.None, off.Features);

        CosmeticPresentationSettings hiddenOthers
            = CosmeticPresentationSettings.DesktopDefault with
            { ShowOtherPlayerCosmetics = false };
        Assert.True(CosmeticQualityPolicy.Evaluate(hiddenOthers, State()).Suppressed);
        Assert.False(CosmeticQualityPolicy.Evaluate(hiddenOthers,
            State(local: true)).Suppressed);
    }

    [Theory]
    [InlineData(64, CosmeticDistanceLod.Full)]
    [InlineData(65, CosmeticDistanceLod.Reduced)]
    [InlineData(400, CosmeticDistanceLod.Reduced)]
    [InlineData(401, CosmeticDistanceLod.Silhouette)]
    [InlineData(1600, CosmeticDistanceLod.Silhouette)]
    [InlineData(1601, CosmeticDistanceLod.Hidden)]
    public void DistanceLodUsesSquaredDistance(float distanceSquared,
        CosmeticDistanceLod expected)
        => Assert.Equal(expected,
            CosmeticQualityPolicy.GetDistanceLod(distanceSquared));

    [Fact]
    public void FirstPersonAndAccessibilityReduceObstructionAndFlashing()
    {
        CosmeticPresentationSettings settings
            = CosmeticPresentationSettings.DesktopDefault with
            {
                ReduceCosmeticFlashes = true,
                DisableCosmeticDistortion = true,
                DisableCosmeticParticles = true,
                ForceStrongTeamColors = true
            };
        CosmeticPresentationPlan plan = CosmeticQualityPolicy.Evaluate(settings,
            State(local: true, firstPerson: true));

        Assert.False(plan.Features.HasFlag(CosmeticRenderFeatures.Particles));
        Assert.False(plan.Features.HasFlag(CosmeticRenderFeatures.Distortion));
        Assert.False(plan.Features.HasFlag(CosmeticRenderFeatures.Attachments));
        Assert.Equal(.2f, plan.MaximumFlashAmplitude);
        Assert.Equal(.5f, plan.MinimumFlashPeriodSeconds);
        Assert.True(plan.RootOnly);
        Assert.True(plan.ForceStrongTeamColors);
    }

    [Theory]
    [InlineData(true, false, false, CosmeticSuppressionReason.Spectator)]
    [InlineData(false, true, false, CosmeticSuppressionReason.HiddenModel)]
    [InlineData(false, false, true, CosmeticSuppressionReason.DeathTakeover)]
    public void PlayerStateSuppressionsAreExplicit(bool spectator,
        bool hidden, bool death, CosmeticSuppressionReason expected)
    {
        CosmeticPresentationPlan plan = CosmeticQualityPolicy.Evaluate(
            CosmeticPresentationSettings.DesktopDefault,
            State() with { Spectator = spectator, HiddenModel = hidden,
                DeathTakeover = death });
        Assert.True(plan.Suppression.HasFlag(expected));
    }

    [Fact]
    public void SeedsAreDeterministicAndEveryInputSeparatesStreams()
    {
        var match = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        ulong baseline = CosmeticSeed.Derive(match, 1, 2, 3, 4, 5);
        Assert.Equal(baseline, CosmeticSeed.Derive(match, 1, 2, 3, 4, 5));
        Assert.NotEqual(baseline, CosmeticSeed.Derive(match, 2, 2, 3, 4, 5));
        Assert.NotEqual(baseline, CosmeticSeed.Derive(match, 1, 3, 3, 4, 5));
        Assert.NotEqual(baseline, CosmeticSeed.Derive(match, 1, 2, 4, 4, 5));
        Assert.NotEqual(baseline, CosmeticSeed.Derive(match, 1, 2, 3, 5, 5));
        Assert.NotEqual(baseline, CosmeticSeed.Derive(match, 1, 2, 3, 4, 6));
        Assert.NotEqual(baseline, CosmeticSeed.Derive(match, 1, 2,
            "prime.armor_fx.lightning", 4, 5));
    }

    [Fact]
    public void AllSixteenRecipesAreStableBoundedAndUsePackagedAtlas()
    {
        Assert.Equal(16, ArmorEffectRecipeCatalog.All.Count);
        Assert.Equal(Enumerable.Range(1, 16).Select(value => (ushort)value),
            ArmorEffectRecipeCatalog.All.Select(recipe => recipe.Definition.Id));
        Assert.Equal(16, ArmorEffectRecipeCatalog.All
            .Select(recipe => recipe.Definition.Key).Distinct(StringComparer.Ordinal).Count());

        using FileStream stream = File.OpenRead(OfficialAtlasPath());
        CosmeticAtlasCatalog atlas = CosmeticAtlasCatalog.Load(stream);
        foreach (ArmorEffectRecipe recipe in ArmorEffectRecipeCatalog.All)
        {
            Assert.InRange(recipe.TypicalParticleCount, 0, 48);
            Assert.InRange(recipe.Definition.Ribbons?.Count ?? 0, 0, 3);
            Assert.InRange(recipe.Definition.Ribbons?.Sum(ribbon => ribbon.Segments) ?? 0,
                0, 96);
            Assert.InRange(recipe.Definition.Attachments?.Count ?? 0, 0, 4);
            Assert.Equal(recipe.Definition.Particles?.Count ?? 0,
                recipe.ParticleSprites?.Count ?? 0);
            foreach (string sprite in recipe.ParticleSprites ?? Array.Empty<string>())
            {
                Assert.True(atlas.TryResolve(sprite, out _), sprite);
            }
            foreach (CosmeticAttachmentDefinition attachment
                in recipe.Definition.Attachments ?? Array.Empty<CosmeticAttachmentDefinition>())
            {
                Assert.Equal("octolith_simple", attachment.Mesh);
                Assert.InRange(attachment.Scale, .01f, 4);
            }
        }
        Assert.Equal(2, atlas.InsetPixels);
    }

    [Fact]
    public void ArmorRuntimeFailsSoftAndAppliesQualityPolicy()
    {
        var runtime = new ArmorEffectPresentation();
        runtime.Select(UInt16.MaxValue);
        Assert.False(runtime.Active);
        Assert.False(runtime.TryEvaluate(CosmeticPresentationSettings.DesktopDefault,
            State(), 10, .25f, 7, out _));

        runtime.Select(1);
        Assert.True(runtime.TryEvaluate(
            CosmeticPresentationSettings.ReducedDefault,
            State(), 10, .25f, 7, out ArmorEffectFrame frame));
        Assert.Equal((ushort)1, frame.Recipe.Definition.Id);
        Assert.InRange(frame.BudgetRequest.Particles, 1, 48);
        Assert.InRange(frame.BudgetRequest.RibbonSegments, 1, 96);

        Assert.False(runtime.TryEvaluate(
            CosmeticPresentationSettings.DesktopDefault with
            { Quality = CosmeticQuality.Off }, State(), 10, .25f, 7, out _));
    }

    [Fact]
    public void AdmittedArmorFrameSubmitsStableInterpolatedPrimitives()
    {
        var runtime = new ArmorEffectPresentation();
        runtime.Select(1);
        Assert.True(runtime.TryEvaluate(CosmeticPresentationSettings.DesktopDefault,
            State(), 120, .5f, 0x1234, out ArmorEffectFrame frame));
        CosmeticBudgetAllowance allowance = Assert.Single(
            CosmeticBudgetArbiter.Admit(new[] { frame.BudgetRequest }));
        var anchors = new ArmorEffectAnchorSet(
            Root: new Vector3(1, 2, 3), Head: new Vector3(1, 4, 3),
            Chest: new Vector3(1, 3, 3), LeftShoulder: new Vector3(.5f, 3, 3),
            RightShoulder: new Vector3(1.5f, 3, 3), LeftHand: new Vector3(0, 2.5f, 3),
            RightHand: new Vector3(2, 2.5f, 3), LeftFoot: new Vector3(.75f, 1, 3),
            RightFoot: new Vector3(1.25f, 1, 3), Weapon: new Vector3(2.25f, 2.75f, 3));

        var first = new CosmeticPrimitiveSubmissionBuffer();
        var second = new CosmeticPrimitiveSubmissionBuffer();
        int firstCount = runtime.SubmitPrimitives(frame, allowance, anchors, first);
        int secondCount = runtime.SubmitPrimitives(frame, allowance, anchors, second);

        Assert.Equal(firstCount, secondCount);
        Assert.NotEqual(0, firstCount);
        Assert.Equal(first.Seal(), second.Seal());
        Assert.Contains(first.Seal(), item => item.Kind == CosmeticPrimitiveKind.Ribbon
            && item.Start == anchors.LeftShoulder
            && item.End == anchors.RightShoulder);
        Assert.Contains(first.Seal(), item => item.Kind == CosmeticPrimitiveKind.LocalLight
            && item.Start == anchors.Chest);
        Assert.Contains(first.Seal(), item => item.Kind == CosmeticPrimitiveKind.Particle
            && item.AssetKey == frame.Recipe.ParticleSprites![0]);
        CosmeticPrimitiveSubmission[] particles = first.Seal()
            .Where(item => item.Kind == CosmeticPrimitiveKind.Particle).ToArray();
        Assert.Equal(allowance.Particles, particles.Length);
        Assert.True(particles.Length > allowance.ParticleEmitters);
        Assert.All(particles, item =>
        {
            Assert.InRange(item.Size, .005f, 2);
            Assert.True(float.IsFinite(item.Start.X));
            Assert.True(float.IsFinite(item.Start.Y));
            Assert.True(float.IsFinite(item.Start.Z));
            Assert.InRange((item.Start - anchors.Chest).Length, 0, .75f);
        });

        Assert.True(runtime.TryEvaluate(CosmeticPresentationSettings.DesktopDefault,
            State(), 150, .5f, 0x1234, out ArmorEffectFrame laterFrame));
        CosmeticBudgetAllowance laterAllowance = Assert.Single(
            CosmeticBudgetArbiter.Admit(new[] { laterFrame.BudgetRequest }));
        var later = new CosmeticPrimitiveSubmissionBuffer();
        runtime.SubmitPrimitives(laterFrame, laterAllowance, anchors, later);
        CosmeticPrimitiveSubmission[] laterParticles = later.Seal()
            .Where(item => item.Kind == CosmeticPrimitiveKind.Particle).ToArray();
        Assert.Equal(particles.Select(item => item.StableKey),
            laterParticles.Select(item => item.StableKey));
        Assert.Contains(particles.Zip(laterParticles), pair =>
            pair.First.Start != pair.Second.Start);
    }

    [Fact]
    public void FirstPersonReducedAndOffPoliciesBoundParticleDensity()
    {
        var runtime = new ArmorEffectPresentation();
        runtime.Select(BuiltInCosmeticIds.ArmorInferno);
        Assert.True(runtime.TryEvaluate(CosmeticPresentationSettings.DesktopDefault,
            State(), 10, 0, 1, out ArmorEffectFrame full));
        Assert.True(runtime.TryEvaluate(CosmeticPresentationSettings.ReducedDefault,
            State(), 10, 0, 1, out ArmorEffectFrame reduced));
        Assert.True(runtime.TryEvaluate(CosmeticPresentationSettings.DesktopDefault,
            State(local: true, firstPerson: true), 10, 0, 1,
            out ArmorEffectFrame firstPerson));
        Assert.True(reduced.BudgetRequest.Particles < full.BudgetRequest.Particles);
        Assert.True(firstPerson.BudgetRequest.Particles < full.BudgetRequest.Particles);
        Assert.Equal(0, firstPerson.BudgetRequest.AttachmentMeshes);
        Assert.Equal(0, firstPerson.BudgetRequest.LocalLights);
        Assert.False(runtime.TryEvaluate(CosmeticPresentationSettings.DesktopDefault
            with { Quality = CosmeticQuality.Off }, State(), 10, 0, 1, out _));
    }

    [Fact]
    public void AuthoredAttachmentsUseBoundedStaticModelTransforms()
    {
        var runtime = new ArmorEffectPresentation();
        runtime.Select(BuiltInCosmeticIds.ArmorGlacial);
        Assert.True(runtime.TryEvaluate(CosmeticPresentationSettings.DesktopDefault,
            State(), 1, 0, 77, out ArmorEffectFrame frame));
        CosmeticBudgetAllowance allowance = Assert.Single(
            CosmeticBudgetArbiter.Admit(new[] { frame.BudgetRequest }));
        var submissions = new CosmeticPrimitiveSubmissionBuffer();

        runtime.SubmitPrimitives(frame, allowance, default, submissions);

        CosmeticPrimitiveSubmission[] attachments = submissions.Seal()
            .Where(item => item.Kind == CosmeticPrimitiveKind.Attachment).ToArray();
        Assert.Equal(2, attachments.Length);
        Assert.All(attachments,
            item =>
            {
                Assert.Equal("octolith_simple", item.AssetKey);
                Assert.NotEqual(Matrix4.Identity, item.LocalTransform);
            });
    }

    [Fact]
    public void SemanticAnchorCacheRefreshesOnlyWhenModelReferenceChanges()
    {
        var cache = new CosmeticAnchorNodeCache();
        var first = new object();
        var second = new object();
        int lookups = 0;
        int Lookup(string name)
        {
            lookups++;
            return name.Length;
        }

        Assert.True(cache.EnsureModel(first, Hunter.Samus, Lookup));
        Assert.Equal(9, lookups);
        Assert.False(cache.EnsureModel(first, Hunter.Samus, Lookup));
        Assert.Equal(9, lookups);
        Assert.True(cache.EnsureModel(second, Hunter.Samus, Lookup));
        Assert.Equal(18, lookups);
        Assert.Equal("Head_1".Length, cache.Resolve(CosmeticAnchor.Head));
        Assert.Equal(2, cache.RefreshCount);
    }

    [Theory]
    [InlineData(Hunter.Samus, "R_elbow")]
    [InlineData(Hunter.Kanden, "R_elbow")]
    [InlineData(Hunter.Trace, "R_elbow")]
    [InlineData(Hunter.Sylux, "R_elbow")]
    [InlineData(Hunter.Noxus, "R_elbow")]
    [InlineData(Hunter.Spire, "R_elbow")]
    [InlineData(Hunter.Weavel, "R_elbow")]
    [InlineData(Hunter.Guardian, "Head_1")]
    public void EverySupportedHunterHasAnExplicitAnchorMap(Hunter hunter,
        string weaponNode)
    {
        var cache = new CosmeticAnchorNodeCache();
        var names = new List<string>();
        Assert.True(cache.EnsureModel(new object(), hunter, name =>
        {
            names.Add(name);
            return name == weaponNode ? 17 : -1;
        }));
        Assert.Equal(9, names.Count);
        Assert.Equal(17, cache.Resolve(CosmeticAnchor.Weapon));
        Assert.Equal(-1, cache.Resolve(CosmeticAnchor.LeftFoot));
    }

    [Fact]
    public void AnchorCacheUsesInterpolatedPoseAndInvalidatesForHunterOrModel()
    {
        var cache = new CosmeticAnchorNodeCache();
        var model = new object();
        Assert.True(cache.EnsureModel(model, Hunter.Samus,
            name => name == "Head_1" ? 2 : -1));
        var poses = new Matrix4[3];
        poses[2] = Matrix4.CreateTranslation(4, 5, 6);
        Assert.True(cache.TryResolveInterpolated(CosmeticAnchor.Head, poses,
            out Vector3 head));
        Assert.Equal(new Vector3(4, 5, 6), head);
        Assert.False(cache.TryResolveInterpolated(CosmeticAnchor.LeftHand,
            poses, out _));
        Assert.True(cache.EnsureModel(model, Hunter.Kanden, _ => -1));
        Assert.True(cache.EnsureModel(new object(), Hunter.Kanden, _ => -1));
        Assert.Equal(3, cache.RefreshCount);
    }

    [Fact]
    public void AltFormPoliciesHideRootOrAdaptSubmissions()
    {
        CosmeticPresentationSettings settings
            = CosmeticPresentationSettings.DesktopDefault;
        CosmeticPresentationPlan hidden = CosmeticQualityPolicy.Evaluate(settings,
            State() with { AltForm = true,
                AltFormMode = CosmeticAltFormMode.Hidden });
        Assert.True(hidden.Suppressed);

        CosmeticPresentationPlan root = CosmeticQualityPolicy.Evaluate(settings,
            State() with { AltForm = true,
                AltFormMode = CosmeticAltFormMode.RootOnly });
        Assert.False(root.Suppressed);
        Assert.True(root.RootOnly);

        CosmeticPresentationPlan adapted = CosmeticQualityPolicy.Evaluate(settings,
            State() with { AltForm = true,
                AltFormMode = CosmeticAltFormMode.Adapted });
        Assert.False(adapted.Suppressed);
        Assert.False(adapted.RootOnly);
    }

    [Fact]
    public void ArmorPrimitiveSubmissionRejectsAllowanceFromAnotherPlayer()
    {
        var runtime = new ArmorEffectPresentation();
        runtime.Select(4);
        Assert.True(runtime.TryEvaluate(CosmeticPresentationSettings.DesktopDefault,
            State(), 1, 0, 99, out ArmorEffectFrame frame));
        CosmeticBudgetAllowance allowance = Assert.Single(
            CosmeticBudgetArbiter.Admit(new[] { frame.BudgetRequest }));
        allowance = allowance with { PlayerSlot = 2 };

        Assert.Throws<ArgumentException>(() => runtime.SubmitPrimitives(frame,
            allowance, default, new CosmeticPrimitiveSubmissionBuffer()));
    }

    [Fact]
    public void DistortionUsesOneAdmittedEnhancedBodyStateAndNoPrimitiveSubmission()
    {
        var runtime = new ArmorEffectPresentation();
        runtime.Select(BuiltInCosmeticIds.ArmorInferno);
        Assert.True(runtime.TryEvaluate(CosmeticPresentationSettings.DesktopDefault,
            State(), 12, .25f, 0x1234, out ArmorEffectFrame frame));
        CosmeticBudgetAllowance allowance = Assert.Single(
            CosmeticBudgetArbiter.Admit(new[] { frame.BudgetRequest }));

        Assert.True(runtime.TryCreateDistortionState(frame, allowance,
            TimeSpan.FromSeconds(1), supported: true,
            out EnhancedForceFieldDrawState distortion));
        Assert.Equal(frame.BudgetRequest.StableKey, distortion.StableSourceKey);
        Assert.InRange(distortion.Profile.DistortionStrength, 0.0001f,
            ForceFieldVisualProfile.MaximumDistortionStrength);
        Assert.False(runtime.TryCreateDistortionState(frame, allowance,
            TimeSpan.Zero, supported: false, out _));

        var submissions = new CosmeticPrimitiveSubmissionBuffer();
        runtime.SubmitPrimitives(frame, allowance, default, submissions);
        Assert.DoesNotContain(submissions.Seal(),
            item => item.Kind == CosmeticPrimitiveKind.Distortion);
    }

    [Fact]
    public void AtlasRejectsOutOfBoundsAndDuplicateSprites()
    {
        const string invalid = """
            {"format":1,"image":"atlas.png","width":16,"height":16,
             "sprites":[{"key":"x","x":0,"y":0,"width":17,"height":1}]}
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalid));
        Assert.Throws<InvalidDataException>(() => CosmeticAtlasCatalog.Load(stream));
    }

    private static CosmeticPlayerPresentationState State(bool local = false,
        bool firstPerson = false) => new(PlayerSlot: 1, LocalPlayer: local,
        FirstPerson: firstPerson, Spectator: false, HiddenModel: false,
        DeathTakeover: false, AltForm: false,
        AltFormMode: CosmeticAltFormMode.RootOnly,
        Visibility: CosmeticVisibility.Visible, DistanceSquared: 1);

    private static string OfficialAtlasPath([CallerFilePath] string sourcePath = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", "..",
            "src", "Client.Presentation", CosmeticAtlasCatalog.OfficialManifestRelativePath));
}
