using System;
using MphRead.Cosmetics;
using OpenTK.Mathematics;

namespace MphRead.Cosmetics.Presentation;

public readonly record struct ArmorEffectFrame(
    ArmorEffectRecipe Recipe,
    CosmeticRuntimeContext Context,
    CosmeticPresentationPlan Plan,
    float EmissionStrength,
    CosmeticBudgetRequest BudgetRequest);

/// <summary>
/// Already-interpolated attachment points copied from the submitted player
/// pose. Recipes consume these values without reading or mutating PlayerEntity.
/// </summary>
public readonly record struct ArmorEffectAnchorSet(
    Vector3 Root,
    Vector3 Head,
    Vector3 Chest,
    Vector3 LeftShoulder,
    Vector3 RightShoulder,
    Vector3 LeftHand,
    Vector3 RightHand,
    Vector3 LeftFoot,
    Vector3 RightFoot,
    Vector3 Weapon)
{
    public Vector3 Resolve(CosmeticAnchor anchor) => anchor switch
    {
        CosmeticAnchor.Root => Root,
        CosmeticAnchor.Head => Head,
        CosmeticAnchor.Chest => Chest,
        CosmeticAnchor.LeftShoulder => LeftShoulder,
        CosmeticAnchor.RightShoulder => RightShoulder,
        CosmeticAnchor.LeftHand => LeftHand,
        CosmeticAnchor.RightHand => RightHand,
        CosmeticAnchor.LeftFoot => LeftFoot,
        CosmeticAnchor.RightFoot => RightFoot,
        CosmeticAnchor.Weapon => Weapon,
        _ => Root
    };
}

/// <summary>
/// Reusable, allocation-free-per-player armor recipe evaluator. Renderer
/// admission remains a separate frame-level step so eight players share one
/// deterministic budget instead of racing to consume it in draw order.
/// </summary>
public sealed class ArmorEffectPresentation
{
    private ArmorEffectRecipe? _recipe;

    public ushort EffectId => _recipe?.Definition.Id ?? 0;
    public bool Active => _recipe != null;

    public void Select(ushort effectId)
        => _recipe = ArmorEffectRecipeCatalog.Resolve(effectId);

    public void Clear() => _recipe = null;

    public int SubmitPrimitives(in ArmorEffectFrame frame,
        in CosmeticBudgetAllowance allowance, in ArmorEffectAnchorSet anchors,
        CosmeticPrimitiveSubmissionBuffer submissions)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        if (allowance.StableKey != frame.BudgetRequest.StableKey
            || allowance.PlayerSlot != frame.BudgetRequest.PlayerSlot)
        {
            throw new ArgumentException("Armor allowance does not match its evaluated frame.",
                nameof(allowance));
        }

        CosmeticColor authored = frame.Recipe.Definition.Material?.EmissionTint
            ?? new CosmeticColor(1, 1, 1);
        var color = new Vector3(authored.R, authored.G, authored.B);
        ArmorEffectAnchorSet resolvedAnchors = frame.Plan.RootOnly
            ? new ArmorEffectAnchorSet(anchors.Root, anchors.Root, anchors.Root,
                anchors.Root, anchors.Root, anchors.Root, anchors.Root,
                anchors.Root, anchors.Root, anchors.Root)
            : anchors;
        int submitted = 0;
        uint stream = 1;

        int emitterCount = Math.Min(allowance.ParticleEmitters,
            frame.Recipe.Definition.Particles?.Count ?? 0);
        for (int i = 0; i < emitterCount; i++)
        {
            CosmeticParticleDefinition particle = frame.Recipe.Definition.Particles![i];
            Vector3 position = resolvedAnchors.Resolve(particle.Anchor);
            float phase = Unit(frame.Context.StableSeed, stream++);
            position += new Vector3((phase - .5f) * .12f,
                .04f + phase * .08f, (.5f - phase) * .12f);
            float intensity = Math.Clamp(frame.EmissionStrength, .05f, 16);
            string? sprite = frame.Recipe.ParticleSprites != null
                && i < frame.Recipe.ParticleSprites.Count
                ? frame.Recipe.ParticleSprites[i] : null;
            if (submissions.TryAdd(new CosmeticPrimitiveSubmission(
                    Key(frame.Context.StableSeed, stream++), allowance.PlayerSlot,
                    CosmeticPrimitiveKind.Particle, position, position, color,
                    intensity, assetKey: sprite)))
            {
                submitted++;
            }
        }

        int ribbonCount = Math.Min(allowance.RibbonSystems,
            frame.Recipe.Definition.Ribbons?.Count ?? 0);
        int remainingSegments = allowance.RibbonSegments;
        for (int i = 0; i < ribbonCount && remainingSegments > 0; i++)
        {
            CosmeticRibbonDefinition ribbon = frame.Recipe.Definition.Ribbons![i];
            int systemsLeft = ribbonCount - i;
            int segments = Math.Min(ribbon.Segments,
                Math.Max(1, remainingSegments / systemsLeft));
            remainingSegments -= segments;
            if (submissions.TryAdd(new CosmeticPrimitiveSubmission(
                    Key(frame.Context.StableSeed, stream++), allowance.PlayerSlot,
                    CosmeticPrimitiveKind.Ribbon, resolvedAnchors.Resolve(ribbon.From),
                    resolvedAnchors.Resolve(ribbon.To), color,
                    Math.Clamp(frame.EmissionStrength, .05f, 16), segments,
                    assetKey: frame.Recipe.ParticleSprites is { Count: > 0 }
                        ? frame.Recipe.ParticleSprites[0] : null)))
            {
                submitted++;
            }
        }

        int attachmentCount = Math.Min(allowance.AttachmentMeshes,
            frame.Recipe.Definition.Attachments?.Count ?? 0);
        for (int i = 0; i < attachmentCount; i++)
        {
            CosmeticAttachmentDefinition attachment
                = frame.Recipe.Definition.Attachments![i];
            Vector3 position = resolvedAnchors.Resolve(attachment.Anchor);
            Matrix4 localTransform = Matrix4.CreateScale(attachment.Scale)
                * Matrix4.CreateRotationX(MathHelper.DegreesToRadians(
                    attachment.PitchDegrees))
                * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(
                    attachment.YawDegrees))
                * Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(
                    attachment.RollDegrees))
                * Matrix4.CreateTranslation(attachment.OffsetX,
                    attachment.OffsetY, attachment.OffsetZ);
            if (submissions.TryAdd(new CosmeticPrimitiveSubmission(
                    Key(frame.Context.StableSeed, stream++), allowance.PlayerSlot,
                    CosmeticPrimitiveKind.Attachment, position, position, color,
                    Math.Clamp(frame.EmissionStrength, .05f, 16),
                    assetKey: attachment.Mesh,
                    localTransform: localTransform)))
            {
                submitted++;
            }
        }

        if (allowance.LocalLights > 0 && frame.Recipe.LocalLight
            && submissions.TryAdd(new CosmeticPrimitiveSubmission(
                Key(frame.Context.StableSeed, stream), allowance.PlayerSlot,
                CosmeticPrimitiveKind.LocalLight, resolvedAnchors.Chest,
                resolvedAnchors.Chest,
                color, Math.Clamp(frame.EmissionStrength, .05f, 16))))
        {
            submitted++;
        }
        return submitted;
    }

    public bool TryCreateDistortionState(in ArmorEffectFrame frame,
        in CosmeticBudgetAllowance allowance, TimeSpan presentationTime,
        bool supported, out EnhancedForceFieldDrawState state)
    {
        state = default;
        if (!supported || allowance.StableKey != frame.BudgetRequest.StableKey
            || allowance.PlayerSlot != frame.BudgetRequest.PlayerSlot
            || allowance.DistortionSources == 0
            || frame.Recipe.Definition.Distortion is not CosmeticDistortionDefinition distortion)
        {
            return false;
        }
        CosmeticColor authored = frame.Recipe.Definition.Material?.EmissionTint
            ?? new CosmeticColor(1, 1, 1);
        Vector3 color = Vector3.Clamp(new Vector3(authored.R, authored.G, authored.B),
            Vector3.Zero, Vector3.One);
        ForceFieldVisualProfile basis = EnhancedForceFieldProfiles.Default;
        var profile = new ForceFieldVisualProfile(basis.NoiseScale,
            basis.NoiseStrength, basis.NoiseSpeed, basis.UvFlow,
            basis.FresnelPower, basis.FresnelStrength, color,
            Math.Clamp(frame.EmissionStrength, 0,
                ForceFieldVisualProfile.MaximumEmissionStrength),
            Math.Clamp(distortion.Strength * frame.Plan.DistortionScale, 0,
                ForceFieldVisualProfile.MaximumDistortionStrength),
            basis.IntersectionStrength);
        state = new EnhancedForceFieldDrawState(frame.BudgetRequest.StableKey,
            profile, presentationTime);
        return true;
    }

    public bool TryEvaluate(CosmeticPresentationSettings settings,
        CosmeticPlayerPresentationState playerState, uint serverTick,
        float renderAlpha, ulong stableSeed, out ArmorEffectFrame frame)
    {
        ArmorEffectRecipe? recipe = _recipe;
        if (recipe == null)
        {
            frame = default;
            return false;
        }

        playerState = playerState with
        {
            AltFormMode = recipe.Definition.AltFormMode
        };
        CosmeticPresentationPlan plan = CosmeticQualityPolicy.Evaluate(
            settings, playerState);
        if (plan.Suppressed)
        {
            frame = default;
            return false;
        }

        var context = new CosmeticRuntimeContext(serverTick, renderAlpha,
            stableSeed, playerState.FirstPerson, playerState.LocalPlayer,
            settings.Quality).Validate();
        float authoredEmission = recipe.Definition.Material?.EmissionStrength ?? 0;
        float pulse = EvaluatePulse(serverTick, renderAlpha, stableSeed,
            plan.MaximumFlashAmplitude, plan.MinimumFlashPeriodSeconds);
        float emission = authoredEmission * plan.EmissionScale * pulse;

        int emitters = (plan.Features & CosmeticRenderFeatures.Particles) != 0
            ? recipe.Definition.Particles?.Count ?? 0 : 0;
        int particles = emitters == 0 ? 0
            : Math.Min(48, (int)MathF.Ceiling(recipe.TypicalParticleCount
                * plan.ParticleScale));
        int ribbonSystems = (plan.Features & CosmeticRenderFeatures.Ribbons) != 0
            ? recipe.Definition.Ribbons?.Count ?? 0 : 0;
        int ribbonSegments = ribbonSystems == 0 ? 0
            : Math.Min(96, (int)MathF.Ceiling(recipe.TotalRibbonSegments
                * plan.RibbonUpdateScale));
        int attachments = (plan.Features & CosmeticRenderFeatures.Attachments) != 0
            ? Math.Min(4, recipe.Definition.Attachments?.Count ?? 0) : 0;
        int distortion = (plan.Features & CosmeticRenderFeatures.Distortion) != 0
            && recipe.Definition.Distortion != null ? 1 : 0;
        int lights = (plan.Features & CosmeticRenderFeatures.LocalLight) != 0
            && recipe.LocalLight ? 1 : 0;
        var request = new CosmeticBudgetRequest(stableSeed,
            playerState.PlayerSlot, playerState.LocalPlayer, focusTarget: false,
            playerState.DistanceSquared, emitters, particles, ribbonSystems,
            ribbonSegments, attachments, distortion, lights);
        frame = new ArmorEffectFrame(recipe, context, plan, emission, request);
        return true;
    }

    private static float EvaluatePulse(uint serverTick, float renderAlpha,
        ulong stableSeed, float maximumAmplitude, float minimumPeriodSeconds)
    {
        float phaseOffset = (stableSeed & 0xFFFF) / 65536f;
        float seconds = (serverTick + renderAlpha) / 60f;
        float period = MathF.Max(minimumPeriodSeconds, .75f);
        float wave = .5f + .5f * MathF.Sin(2 * MathF.PI
            * (seconds / period + phaseOffset));
        return 1 - maximumAmplitude * .35f + maximumAmplitude * .35f * wave;
    }

    private static ulong Key(ulong seed, uint stream)
    {
        unchecked
        {
            ulong value = seed ^ (0x9E3779B97F4A7C15UL * stream);
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }

    private static float Unit(ulong seed, uint stream)
        => (Key(seed, stream) & 0xFFFF) / 65535f;
}
