using System;
using MphRead.Cosmetics;
using MphRead.Cosmetics.Presentation;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerPresentation
{
    public SkinPresentation Skin { get; } = new();
    public ArmorEffectPresentation Armor { get; } = new();
    public CosmeticLoadoutIds CosmeticLoadoutIds { get; private set; }
    private ArmorEffectFrame? _armorFrame;
    private bool _armorDistortionSubmitted;
    private readonly CosmeticAnchorNodeCache _armorAnchorNodes = new();

    public void SetCosmeticLoadout(CosmeticLoadoutIds loadout)
    {
        CosmeticLoadoutIds = CosmeticCatalog.BuiltIn.Sanitize(loadout, _player.Hunter);
        Skin.Select(CosmeticLoadoutIds.SkinId, _player.Hunter);
        Armor.Select(CosmeticLoadoutIds.ArmorEffectId);
    }

    internal bool PrepareArmorEffect(CosmeticPresentationSettings settings,
        out ArmorEffectFrame frame)
    {
        _armorFrame = null;
        bool local = _player.IsMainPlayer;
        bool firstPerson = local && _player.CameraType == CameraType.First
            && _player._scene.CameraSequences.Current == null;
        bool visible = local || IsVisible(_player.NodeRef) || _player.ModNodeUnresolved;
        Vector3 camera = _player._scene.LocalPlayer?.CameraInfo.Position
            ?? _player.Position;
        float distanceSquared = (_player.Position - camera).LengthSquared;
        Mods.Network.CombatActor identity = _player.CombatIdentity;
        uint life = identity.IsValid ? identity.Life : _player.PresentationPoseEpoch;
        Guid matchId = LegacyMatchIdentity(_player._scene.Match.MatchId);
        ulong seed = CosmeticSeed.Derive(matchId, (byte)_player.SlotIndex, life,
            Armor.EffectId, eventTick: 0);
        float renderAlpha = Presentation.Timing.RenderAlpha;
        if (!float.IsFinite(renderAlpha)) renderAlpha = 0;
        var state = new CosmeticPlayerPresentationState(
            (byte)_player.SlotIndex, local, firstPerson,
            _player.Flags2.TestFlag(PlayerFlags2.Spectating),
            _player.Flags2.TestFlag(PlayerFlags2.HideModel)
                || _player.Flags2.TestFlag(PlayerFlags2.Cloaking),
            _deathRuntime.Active || _player.Health <= 0,
            _player.IsAltForm, CosmeticAltFormMode.RootOnly,
            visible ? CosmeticVisibility.Visible : CosmeticVisibility.Hidden,
            Math.Max(0, distanceSquared));
        if (!Armor.TryEvaluate(settings, state,
                _player._scene.Services.WorldServerTick,
                Math.Clamp(renderAlpha, 0, 1), seed,
                out frame))
        {
            return false;
        }
        _armorFrame = frame;
        return true;
    }

    internal SkinAppearanceResolution ResolveSkinAppearance(int canonicalRecolor,
        TextureAssetKey? source, GameplayMaterialFeedback feedback)
        => Skin.ResolveAppearance(canonicalRecolor, source, feedback,
            teamMode: _player._scene.Match.Rules.Teams && _player.Team != Team.None,
            forceStrongTeamColors: Presentation.ArmorPresentationSettings
                .ForceStrongTeamColors);

    internal CosmeticMaterialOverride? ResolveCosmeticMaterial(
        in SkinAppearanceResolution appearance,
        GameplayMaterialFeedback feedback)
    {
        // Without an authored mask the only safe team accent is the canonical
        // team palette itself. Masked accents may retain non-albedo surface
        // channels already filtered by SkinPresentation.ResolveAppearance.
        CosmeticMaterialOverride? skin = appearance.TeamAccentStrength > 0
            && appearance.TeamAccentMask == null ? null : appearance.Material;
        if (_armorFrame is not ArmorEffectFrame frame
            || feedback != GameplayMaterialFeedback.None
            || (frame.Plan.Features & CosmeticRenderFeatures.MaterialOverlay) == 0
            || frame.Plan.ForceStrongTeamColors && _player.Team != Team.None
            || frame.Recipe.Definition.Material is not CosmeticMaterialDefinition material)
        {
            return skin;
        }
        CosmeticColor? tint = material.EmissionTint;
        var armor = new CosmeticMaterialOverride(
            SpecularStrength: material.SpecularStrength,
            Smoothness: material.Smoothness,
            ReflectionStrength: material.ReflectionStrength,
            EmissionTint: tint is { } color
                ? new Vector3(color.R, color.G, color.B) : null,
            EmissionStrength: frame.EmissionStrength);
        if (skin is not CosmeticMaterialOverride baseLayer) return armor;
        return armor with
        {
            Albedo = baseLayer.Albedo,
            Normal = baseLayer.Normal,
            Emissive = baseLayer.Emissive,
            SpecularStrength = armor.SpecularStrength ?? baseLayer.SpecularStrength,
            Smoothness = armor.Smoothness ?? baseLayer.Smoothness,
            ReflectionStrength = armor.ReflectionStrength ?? baseLayer.ReflectionStrength,
            EmissionTint = armor.EmissionTint ?? baseLayer.EmissionTint,
            EmissionStrength = armor.EmissionStrength ?? baseLayer.EmissionStrength
        };
    }

    internal void BeginArmorSubmission() => _armorDistortionSubmitted = false;

    internal EnhancedForceFieldDrawState? TakeArmorDistortion(ModelInstance instance)
    {
        if (_armorDistortionSubmitted
            || instance != _player._bipedModel2 && instance != _player._altModel
            || _armorFrame is not ArmorEffectFrame frame
            || !Presentation.TryGetArmorAllowance(frame.BudgetRequest.StableKey,
                out CosmeticBudgetAllowance allowance))
        {
            return null;
        }
        bool supported = RenderBackendSelection.Current == RenderBackendKind.Sdl
            && Mods.RenderOptions.GraphicsPreset == Mods.GraphicsPreset.Enhanced;
        if (!Armor.TryCreateDistortionState(frame, allowance,
                Presentation.CapturedPresentationTime, supported,
                out EnhancedForceFieldDrawState state))
        {
            return null;
        }
        _armorDistortionSubmitted = true;
        return state;
    }

    internal void SubmitArmorPrimitives(Model model, Matrix4 root,
        Matrix4[]? interpolatedNodes)
    {
        if (_armorFrame is not ArmorEffectFrame frame
            || !Presentation.TryGetArmorAllowance(frame.BudgetRequest.StableKey,
                out CosmeticBudgetAllowance allowance))
        {
            return;
        }
        var anchors = ResolveArmorAnchors(model, root, interpolatedNodes);
        Armor.SubmitPrimitives(frame, allowance, anchors,
            Presentation.ArmorPrimitiveSubmissions);
    }

    private ArmorEffectAnchorSet ResolveArmorAnchors(Model model, Matrix4 root,
        Matrix4[]? interpolatedNodes)
    {
        _armorAnchorNodes.EnsureModel(model, _player.Hunter);
        Vector3 rootPosition = root.Row3.Xyz;
        Vector3 Node(CosmeticAnchor anchor, Vector3 fallback)
        {
            int index = _armorAnchorNodes.Resolve(anchor);
            if (index < 0) return fallback;
            return _armorAnchorNodes.TryResolveInterpolated(anchor,
                    interpolatedNodes, out Vector3 interpolated)
                ? interpolated : model.Nodes[index].Animation.Row3.Xyz;
        }
        Vector3 chest = Node(CosmeticAnchor.Chest, rootPosition + Vector3.UnitY);
        Vector3 head = Node(CosmeticAnchor.Head, chest + Vector3.UnitY * .45f);
        Vector3 leftShoulder = Node(CosmeticAnchor.LeftShoulder,
            chest + new Vector3(-.3f, .15f, 0));
        Vector3 rightShoulder = Node(CosmeticAnchor.RightShoulder,
            chest + new Vector3(.3f, .15f, 0));
        Vector3 leftHand = Node(CosmeticAnchor.LeftHand,
            leftShoulder + new Vector3(-.25f, -.25f, 0));
        Vector3 rightHand = Node(CosmeticAnchor.RightHand,
            rightShoulder + new Vector3(.25f, -.25f, 0));
        Vector3 leftFoot = Node(CosmeticAnchor.LeftFoot,
            rootPosition + new Vector3(-.18f, .08f, 0));
        Vector3 rightFoot = Node(CosmeticAnchor.RightFoot,
            rootPosition + new Vector3(.18f, .08f, 0));
        return new ArmorEffectAnchorSet(rootPosition, head, chest,
            leftShoulder, rightShoulder, leftHand, rightHand, leftFoot,
            rightFoot, rightHand);
    }

    private static Guid LegacyMatchIdentity(uint matchId)
    {
        Span<byte> bytes = stackalloc byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, matchId);
        return new Guid(bytes);
    }

    public bool DeathPresentationActive => _deathRuntime.Active;
}
