using System;
using MphRead.Combat;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Cosmetics.Presentation;

public enum DeathParticleKind : byte
{
    Default,
    Quantum,
    Spectral,
    Inferno
}

public readonly record struct DeathPresentationSample(
    float Progress,
    float BodyAlpha,
    Vector3 EmissionTint,
    float EmissionStrength,
    DeathParticleKind Particles,
    Matrix4[] Nodes,
    float[] MatrixStack);

public readonly record struct DeathEffectFrame(
    DeathEffectDefinition Definition,
    CosmeticRuntimeContext Context,
    DeathPresentationSample Sample,
    CosmeticMaterialOverride? MaterialOverride,
    CosmeticBudgetRequest BudgetRequest,
    bool DistortionEnabled);

/// <summary>
/// One player-life's visual death owner. It consumes copied presentation data
/// and authoritative identity/timing facts, and never reaches into PlayerEntity.
/// </summary>
public sealed class DeathPresentationRuntime
{
    public const uint SimulationTicksPerSecond = 60;
    public const float MaximumDuration = 3;

    private readonly DeathPosePlayer _posePlayer = new();
    private CombatActor _actor = CombatActor.None;
    private DeathEffectDefinition? _effect;
    private DeathAnimationClip? _animation;
    private CapturedDeathPose? _pose;
    private uint _matchId;
    private uint _startTick;
    private uint _durationTicks;
    private ulong _stableSeed;

    public bool Active => _actor.IsValid && _effect != null && _pose?.IsValid == true;
    public CombatActor Actor => _actor;
    public ushort EffectId => _effect?.Id ?? 0;
    public ulong StableSeed => _stableSeed;

    public bool Begin(in CombatActor actor, uint authoritativeTick,
        ushort requestedEffectId, CapturedDeathPose pose,
        DeathAnimationClip? animation = null, uint matchId = 0)
    {
        if (!actor.IsValid || !pose.IsValid || pose.Model == null
            || !TryResolveEffect(requestedEffectId, out DeathEffectDefinition effect))
            return false;
        DeathAnimationClip? acceptedAnimation = null;
        if (effect.BodyMode == DeathBodyMode.CustomAnimation && animation == null)
            DeathAnimationCatalog.TryResolve(effect, pose.Appearance.Hunter,
                pose.Model.Nodes, out animation);
        if (effect.BodyMode == DeathBodyMode.CustomAnimation && animation != null)
        {
            try
            {
                byte[] signature = DeathAnimationSkeleton.ComputeSignature(
                    pose.Appearance.Hunter, pose.Model.Nodes);
                bool validTracks = true;
                for (int i = 0; i < animation.Tracks.Count; i++)
                    validTracks &= animation.Tracks[i].NodeIndex < pose.Model.Nodes.Count;
                if (validTracks && animation.Matches(pose.Appearance.Hunter, signature))
                    acceptedAnimation = animation;
                else return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
        else if (effect.BodyMode == DeathBodyMode.CustomAnimation)
        {
            return false;
        }

        _actor = actor;
        _matchId = matchId;
        _startTick = authoritativeTick;
        _effect = effect;
        _pose = pose;
        _animation = acceptedAnimation;
        _stableSeed = CosmeticSeed.Derive(MatchIdentity(matchId), actor.Slot,
            actor.Life, effect.Id, authoritativeTick);
        if (_stableSeed == 0) _stableSeed = 1;
        float duration = Math.Min(effect.Duration, MaximumDuration);
        if (acceptedAnimation != null) duration = Math.Min(duration, acceptedAnimation.Duration);
        _durationTicks = Math.Max(1u, (uint)MathF.Ceiling(duration * SimulationTicksPerSecond));
        return true;
    }

    /// <summary>Corrects a snapshot-started cue when its reliable kill arrives later.</summary>
    public void ReconcileAuthoritativeTick(in CombatActor actor, uint authoritativeTick)
    {
        if (Active && _actor == actor)
        {
            _startTick = authoritativeTick;
            _stableSeed = CosmeticSeed.Derive(MatchIdentity(_matchId), actor.Slot,
                actor.Life, EffectId, authoritativeTick);
            if (_stableSeed == 0) _stableSeed = 1;
        }
    }

    public bool TrySample(uint serverTick, out DeathPresentationSample sample)
    {
        sample = default;
        if (!Active || _effect == null || _pose == null) return false;
        uint age = CombatFeedback.Age(serverTick, _startTick);
        if (age >= _durationTicks)
        {
            Cancel();
            return false;
        }
        float elapsed = age / (float)SimulationTicksPerSecond;
        float progress = age / (float)_durationTicks;
        _posePlayer.Evaluate(_pose, _animation, elapsed, out Matrix4[] nodes,
            out float[] stack);
        (float alpha, Vector3 tint, float strength, DeathParticleKind particles) =
            Style(_effect, progress);
        sample = new DeathPresentationSample(progress, alpha, tint, strength,
            particles, nodes, stack);
        return true;
    }

    public bool TryEvaluate(CosmeticPresentationSettings settings,
        bool localPlayer, bool visible, float distanceSquared, uint serverTick,
        float renderAlpha, out DeathEffectFrame frame)
    {
        frame = default;
        if (settings.Quality == CosmeticQuality.Off
            || !localPlayer && !settings.ShowOtherPlayerCosmetics
            || !visible || !float.IsFinite(distanceSquared)
            || distanceSquared > CosmeticQualityPolicy.SilhouetteDistanceSquared
            || !TrySample(serverTick, out DeathPresentationSample sample)
            || _effect == null)
            return false;

        float qualityScale = settings.Quality == CosmeticQuality.Reduced ? .5f : 1;
        float flashScale = settings.ReduceCosmeticFlashes ? .2f : 1;
        sample = sample with
        {
            EmissionStrength = sample.EmissionStrength * flashScale
        };
        int authoredParticles = 0;
        if (!settings.DisableCosmeticParticles && _effect.Particles != null)
        {
            for (int i = 0; i < _effect.Particles.Count; i++)
                authoredParticles += _effect.Particles[i].Count;
        }
        int particles = (int)MathF.Ceiling(Math.Min(16, authoredParticles)
            * qualityScale);
        bool distortion = !settings.DisableCosmeticDistortion
            && _effect.Distortion is { Strength: > 0, Duration: > 0 } authoredDistortion
            && sample.Progress * _effect.Duration < authoredDistortion.Duration;
        var request = new CosmeticBudgetRequest(_stableSeed, _actor.Slot,
            localPlayer, focusTarget: false, Math.Max(0, distanceSquared),
            particles > 0 ? 1 : 0, particles, 0, 0, 0,
            distortion ? 1 : 0, 0);
        CosmeticColor? authoredTint = _effect.Material?.EmissionTint;
        var material = new CosmeticMaterialOverride(
            SpecularStrength: _effect.Material?.SpecularStrength,
            Smoothness: _effect.Material?.Smoothness,
            ReflectionStrength: _effect.Material?.ReflectionStrength,
            EmissionTint: authoredTint is { } color
                ? new Vector3(color.R, color.G, color.B) : sample.EmissionTint,
            EmissionStrength: sample.EmissionStrength);
        var context = new CosmeticRuntimeContext(serverTick,
            Math.Clamp(float.IsFinite(renderAlpha) ? renderAlpha : 0, 0, 1),
            _stableSeed, FirstPerson: false, LocalPlayer: localPlayer,
            Quality: settings.Quality);
        frame = new DeathEffectFrame(_effect, context, sample, material,
            request, distortion);
        return true;
    }

    public int SubmitPrimitives(in DeathEffectFrame frame,
        in CosmeticBudgetAllowance allowance,
        CosmeticPrimitiveSubmissionBuffer submissions)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        if (allowance.StableKey != frame.BudgetRequest.StableKey
            || allowance.PlayerSlot != frame.BudgetRequest.PlayerSlot)
            throw new ArgumentException("Death allowance does not match its frame.", nameof(allowance));
        int count = Math.Min(allowance.Particles, 16);
        if (allowance.ParticleEmitters == 0) count = 0;
        int submitted = 0;
        for (int i = 0; i < count; i++)
        {
            int node = frame.Sample.Nodes.Length == 0 ? 0
                : 1 + i % Math.Max(1, frame.Sample.Nodes.Length - 1);
            Vector3 position = frame.Sample.Nodes.Length == 0
                ? Vector3.Zero : frame.Sample.Nodes[Math.Min(node,
                    frame.Sample.Nodes.Length - 1)].Row3.Xyz;
            float x = Unit(frame.Context.StableSeed, (uint)(i * 2 + 1)) - .5f;
            float z = Unit(frame.Context.StableSeed, (uint)(i * 2 + 2)) - .5f;
            position += new Vector3(x * .14f,
                frame.Sample.Progress * (frame.Sample.Particles == DeathParticleKind.Spectral
                    ? 1.2f : .35f), z * .14f);
            if (submissions.TryAdd(new CosmeticPrimitiveSubmission(
                    Key(frame.Context.StableSeed, (uint)i), allowance.PlayerSlot,
                    CosmeticPrimitiveKind.Particle, position, position,
                    frame.Sample.EmissionTint,
                    Math.Clamp(frame.Sample.EmissionStrength, .05f, 16),
                    assetKey: ParticleAssetKey(frame.Definition))))
                submitted++;
        }
        return submitted;
    }

    private static string? ParticleAssetKey(DeathEffectDefinition definition)
        => definition.Particles is { Count: > 0 } ? definition.Particles[0].Kind : null;

    public bool TryCreateDistortionState(in DeathEffectFrame frame,
        in CosmeticBudgetAllowance allowance, TimeSpan presentationTime,
        bool supported, out EnhancedForceFieldDrawState state)
    {
        state = default;
        if (!supported || !frame.DistortionEnabled
            || allowance.StableKey != frame.BudgetRequest.StableKey
            || allowance.DistortionSources == 0
            || frame.Definition.Distortion is not CosmeticDistortionDefinition distortion)
            return false;
        ForceFieldVisualProfile basis = EnhancedForceFieldProfiles.Default;
        var profile = new ForceFieldVisualProfile(basis.NoiseScale,
            basis.NoiseStrength, basis.NoiseSpeed, basis.UvFlow,
            basis.FresnelPower, basis.FresnelStrength,
            Vector3.Clamp(frame.Sample.EmissionTint, Vector3.Zero, Vector3.One),
            Math.Clamp(frame.Sample.EmissionStrength, 0,
                ForceFieldVisualProfile.MaximumEmissionStrength),
            Math.Clamp(distortion.Strength, 0,
                ForceFieldVisualProfile.MaximumDistortionStrength),
            basis.IntersectionStrength);
        state = new EnhancedForceFieldDrawState(frame.Context.StableSeed,
            profile, presentationTime);
        return true;
    }

    public void Cancel()
    {
        _actor = CombatActor.None;
        _effect = null;
        _animation = null;
        _pose = null;
        _matchId = 0;
        _startTick = _durationTicks = 0;
        _stableSeed = 0;
    }

    private static bool TryResolveEffect(ushort id,
        out DeathEffectDefinition effect)
    {
        if (id != 0 && CosmeticCatalog.BuiltIn.TryGetDeathEffect(id, out effect!))
            return true;
        effect = null!;
        return false;
    }

    private static (float Alpha, Vector3 Tint, float Strength, DeathParticleKind Particles)
        Style(DeathEffectDefinition effect, float progress)
    {
        DeathBodyMode mode = effect.BodyMode;
        float fade = Math.Clamp(1 - progress, 0, 1);
        (float alpha, Vector3 tint, float envelope, DeathParticleKind particles) = mode switch
        {
            DeathBodyMode.Dissolve => (fade * fade, new Vector3(.72f, .2f, 1),
                .3f + progress * .7f, DeathParticleKind.Quantum),
            DeathBodyMode.Fade => (fade, new Vector3(.7f, .95f, 1),
                .35f + fade * .65f, DeathParticleKind.Spectral),
            DeathBodyMode.Burn => (progress < .65f ? 1 : fade / .35f,
                new Vector3(1, .28f, .03f), .2f + .8f * progress,
                DeathParticleKind.Inferno),
            _ => (fade, Vector3.One, 0, DeathParticleKind.Default)
        };
        if (effect.Material?.EmissionTint is CosmeticColor color)
            tint = new Vector3(color.R, color.G, color.B);
        float strength = (effect.Material?.EmissionStrength ?? 0) * envelope;
        return (alpha, tint, strength, particles);
    }

    private static Guid MatchIdentity(uint matchId)
    {
        Span<byte> bytes = stackalloc byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, matchId);
        return new Guid(bytes);
    }

    private static float Unit(ulong seed, uint stream)
    {
        ulong value = Key(seed, stream);
        return (value >> 40) * (1f / (1u << 24));
    }

    private static ulong Key(ulong seed, uint stream)
    {
        ulong value = seed ^ (0x9E3779B97F4A7C15UL * (stream + 1));
        value ^= value >> 30; value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27; value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
