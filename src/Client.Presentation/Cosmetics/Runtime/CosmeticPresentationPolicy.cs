using System;
using MphRead.Cosmetics;
using MphRead.Mods;

namespace MphRead.Cosmetics.Presentation;

public readonly record struct CosmeticPresentationSettings(
    CosmeticQuality Quality,
    bool ShowOtherPlayerCosmetics,
    bool ReduceCosmeticFlashes,
    bool ForceStrongTeamColors,
    bool DisableCosmeticDistortion,
    bool DisableCosmeticParticles)
{
    public static CosmeticPresentationSettings DesktopDefault { get; } = new(
        CosmeticQuality.Full, ShowOtherPlayerCosmetics: true,
        ReduceCosmeticFlashes: false, ForceStrongTeamColors: false,
        DisableCosmeticDistortion: false, DisableCosmeticParticles: false);

    public static CosmeticPresentationSettings ReducedDefault { get; } = new(
        CosmeticQuality.Reduced, ShowOtherPlayerCosmetics: true,
        ReduceCosmeticFlashes: false, ForceStrongTeamColors: false,
        DisableCosmeticDistortion: false, DisableCosmeticParticles: false);

    public static CosmeticPresentationSettings CreateDefault(bool mobileOrLowPower)
        => mobileOrLowPower ? ReducedDefault : DesktopDefault;
}

/// <summary>
/// Process-local immutable snapshot populated by GameSettings. Rendering reads
/// one value per frame and never reaches back into the mutable settings model.
/// </summary>
public static class CosmeticPresentationPreferences
{
    private sealed class Snapshot
    {
        public Snapshot(CosmeticPresentationSettings value) => Value = value;

        public CosmeticPresentationSettings Value { get; }
    }

    private static Snapshot _current = new(
        CosmeticPresentationSettings.CreateDefault(OperatingSystem.IsAndroid()));

    public static CosmeticPresentationSettings Current
        => System.Threading.Volatile.Read(ref _current).Value;

    public static CosmeticPresentationSettings Parse(MenuSettings settings,
        bool mobileOrLowPower)
    {
        ArgumentNullException.ThrowIfNull(settings);
        CosmeticQuality fallback = mobileOrLowPower
            ? CosmeticQuality.Reduced : CosmeticQuality.Full;
        CosmeticQuality quality = Enum.TryParse(settings.CosmeticQuality, true,
                out CosmeticQuality parsed) && Enum.IsDefined(parsed)
            ? parsed : fallback;
        return new CosmeticPresentationSettings(quality,
            RenderOptions.ParseOnOff(settings.ShowOtherPlayerCosmetics, true),
            RenderOptions.ParseOnOff(settings.ReduceCosmeticFlashes, false),
            RenderOptions.ParseOnOff(settings.ForceStrongTeamColors, false),
            RenderOptions.ParseOnOff(settings.DisableCosmeticDistortion, false),
            RenderOptions.ParseOnOff(settings.DisableCosmeticParticles, false));
    }

    public static void Apply(MenuSettings settings, bool mobileOrLowPower)
        => System.Threading.Volatile.Write(ref _current,
            new Snapshot(Parse(settings, mobileOrLowPower)));
}

public enum CosmeticVisibility : byte
{
    Hidden,
    PotentiallyVisible,
    Visible
}

public enum CosmeticDistanceLod : byte
{
    Full,
    Reduced,
    Silhouette,
    Hidden
}

[Flags]
public enum CosmeticSuppressionReason : ushort
{
    None = 0,
    QualityOff = 1 << 0,
    OtherPlayersDisabled = 1 << 1,
    Spectator = 1 << 2,
    HiddenModel = 1 << 3,
    DeathTakeover = 1 << 4,
    UnsupportedAltForm = 1 << 5,
    VisibilityCulled = 1 << 6,
    DistanceCulled = 1 << 7
}

public readonly record struct CosmeticRuntimeContext(
    uint ServerTick,
    float RenderAlpha,
    ulong StableSeed,
    bool FirstPerson,
    bool LocalPlayer,
    CosmeticQuality Quality)
{
    public CosmeticRuntimeContext Validate()
    {
        if (!float.IsFinite(RenderAlpha) || RenderAlpha < 0 || RenderAlpha > 1)
            throw new ArgumentOutOfRangeException(nameof(RenderAlpha));
        if (!Enum.IsDefined(Quality))
            throw new ArgumentOutOfRangeException(nameof(Quality));
        return this;
    }
}

public readonly record struct CosmeticPlayerPresentationState(
    byte PlayerSlot,
    bool LocalPlayer,
    bool FirstPerson,
    bool Spectator,
    bool HiddenModel,
    bool DeathTakeover,
    bool AltForm,
    CosmeticAltFormMode AltFormMode,
    CosmeticVisibility Visibility,
    float DistanceSquared)
{
    public CosmeticPlayerPresentationState Validate()
    {
        if (!Enum.IsDefined(AltFormMode))
            throw new ArgumentOutOfRangeException(nameof(AltFormMode));
        if (!Enum.IsDefined(Visibility))
            throw new ArgumentOutOfRangeException(nameof(Visibility));
        if (!float.IsFinite(DistanceSquared) || DistanceSquared < 0)
            throw new ArgumentOutOfRangeException(nameof(DistanceSquared));
        return this;
    }
}

public readonly record struct CosmeticPresentationPlan(
    CosmeticSuppressionReason Suppression,
    CosmeticDistanceLod Lod,
    CosmeticRenderFeatures Features,
    float EmissionScale,
    float ParticleScale,
    float RibbonUpdateScale,
    float DistortionScale,
    float MaximumFlashAmplitude,
    float MinimumFlashPeriodSeconds,
    bool RootOnly,
    bool ForceStrongTeamColors)
{
    public bool Suppressed => Suppression != CosmeticSuppressionReason.None;
}

/// <summary>
/// Pure armor presentation policy. It consumes copied presentation facts only
/// and never reaches into gameplay, scene, or global settings state.
/// </summary>
public static class CosmeticQualityPolicy
{
    public const float FullDistanceSquared = 8 * 8;
    public const float ReducedDistanceSquared = 20 * 20;
    public const float SilhouetteDistanceSquared = 40 * 40;

    public static CosmeticPresentationPlan Evaluate(
        CosmeticPresentationSettings settings,
        CosmeticPlayerPresentationState state)
    {
        state.Validate();
        if (!Enum.IsDefined(settings.Quality))
            throw new ArgumentOutOfRangeException(nameof(settings));

        CosmeticSuppressionReason suppression = CosmeticSuppressionReason.None;
        if (settings.Quality == CosmeticQuality.Off)
            suppression |= CosmeticSuppressionReason.QualityOff;
        if (!state.LocalPlayer && !settings.ShowOtherPlayerCosmetics)
            suppression |= CosmeticSuppressionReason.OtherPlayersDisabled;
        if (state.Spectator) suppression |= CosmeticSuppressionReason.Spectator;
        if (state.HiddenModel) suppression |= CosmeticSuppressionReason.HiddenModel;
        if (state.DeathTakeover) suppression |= CosmeticSuppressionReason.DeathTakeover;
        if (state.Visibility == CosmeticVisibility.Hidden)
            suppression |= CosmeticSuppressionReason.VisibilityCulled;
        if (state.AltForm && state.AltFormMode == CosmeticAltFormMode.Hidden)
            suppression |= CosmeticSuppressionReason.UnsupportedAltForm;

        CosmeticDistanceLod lod = GetDistanceLod(state.DistanceSquared);
        if (lod == CosmeticDistanceLod.Hidden)
            suppression |= CosmeticSuppressionReason.DistanceCulled;
        if (suppression != CosmeticSuppressionReason.None)
        {
            return new CosmeticPresentationPlan(suppression, lod,
                CosmeticRenderFeatures.None, 0, 0, 0, 0, 0,
                Single.PositiveInfinity, RootOnly: false,
                settings.ForceStrongTeamColors);
        }

        CosmeticRenderFeatures features = CosmeticRenderFeatures.MaterialOverlay;
        float emission = 1;
        float particles = 1;
        float ribbons = 1;
        float distortion = 1;

        if (settings.Quality == CosmeticQuality.Reduced)
        {
            particles *= .5f;
            ribbons *= .5f;
            distortion *= .5f;
        }

        switch (lod)
        {
            case CosmeticDistanceLod.Full:
                features |= CosmeticRenderFeatures.Particles
                    | CosmeticRenderFeatures.Ribbons
                    | CosmeticRenderFeatures.Attachments
                    | CosmeticRenderFeatures.Distortion
                    | CosmeticRenderFeatures.LocalLight;
                break;
            case CosmeticDistanceLod.Reduced:
                features |= CosmeticRenderFeatures.Particles
                    | CosmeticRenderFeatures.Ribbons
                    | CosmeticRenderFeatures.Attachments
                    | CosmeticRenderFeatures.Distortion;
                particles *= .5f;
                ribbons *= .5f;
                distortion *= .5f;
                break;
            case CosmeticDistanceLod.Silhouette:
                emission *= .65f;
                particles = 0;
                ribbons = 0;
                distortion = 0;
                break;
        }

        bool rootOnly = state.AltForm && state.AltFormMode == CosmeticAltFormMode.RootOnly;
        if (state.FirstPerson && state.LocalPlayer)
        {
            features &= ~(CosmeticRenderFeatures.Attachments
                | CosmeticRenderFeatures.LocalLight);
            emission *= .5f;
            particles *= .2f;
            ribbons *= .25f;
            distortion *= .2f;
            rootOnly = true;
        }

        if (settings.DisableCosmeticParticles)
        {
            features &= ~CosmeticRenderFeatures.Particles;
            particles = 0;
        }
        if (settings.DisableCosmeticDistortion)
        {
            features &= ~CosmeticRenderFeatures.Distortion;
            distortion = 0;
        }
        if (particles == 0) features &= ~CosmeticRenderFeatures.Particles;
        if (ribbons == 0) features &= ~CosmeticRenderFeatures.Ribbons;
        if (distortion == 0) features &= ~CosmeticRenderFeatures.Distortion;

        float maximumFlashAmplitude = settings.ReduceCosmeticFlashes ? .2f : 1;
        float minimumFlashPeriod = settings.ReduceCosmeticFlashes ? .5f : 0;
        return new CosmeticPresentationPlan(suppression, lod, features,
            emission, particles, ribbons, distortion, maximumFlashAmplitude,
            minimumFlashPeriod, rootOnly, settings.ForceStrongTeamColors);
    }

    public static CosmeticDistanceLod GetDistanceLod(float distanceSquared)
    {
        if (!float.IsFinite(distanceSquared) || distanceSquared < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceSquared));
        if (distanceSquared <= FullDistanceSquared) return CosmeticDistanceLod.Full;
        if (distanceSquared <= ReducedDistanceSquared) return CosmeticDistanceLod.Reduced;
        if (distanceSquared <= SilhouetteDistanceSquared) return CosmeticDistanceLod.Silhouette;
        return CosmeticDistanceLod.Hidden;
    }
}
