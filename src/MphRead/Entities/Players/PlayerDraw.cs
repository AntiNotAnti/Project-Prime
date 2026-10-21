using System;
using System.Buffers;
using System.Diagnostics;
using MphRead.Formats;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        /// <summary>
        /// A living remote network player must never disappear only because
        /// portal culling disagrees with its replicated/presentation position.
        ///
        /// The model can be presented from an older smoothed snapshot while
        /// NodeRef belongs to the current simulation position. Near a room-part
        /// boundary those two valid worlds can name different portal parts.
        /// Culling the model with the newer NodeRef then produces an invisible,
        /// still-shootable opponent. Dead players, spectators, replay replicas,
        /// local players and inactive/unspawned slots keep the ordinary rules.
        /// </summary>
        internal static bool ModForceNetworkVisibility(bool replica, bool networkActive,
            int slot, int localSlot, bool active, bool spawned, int health)
        {
            return !replica && networkActive && slot >= 0 && slot != localSlot
                && active && spawned && health > 0;
        }

        private static readonly bool HunterAnimationBlendEnabled =
            AnimationRollout.Enabled("PROJECT_PRIME_PLAYER_ANIMATION_BLEND");
        // Draw-only rollback for the local third-person body. First-person
        // input and the 60 Hz authoritative gameplay pose remain immediate.
        private static readonly bool ThirdPersonBodyBlendEnabled =
            AnimationRollout.Enabled("PROJECT_PRIME_THIRD_PERSON_BODY_BLEND");

        // 15 frames at 60 FPS gives a readable 2 Hz white flash without turning the
        // three-second protection window into a high-frequency strobe.
        private const int SpawnProtectionFlashHalfPeriodFrames = 15;

        /// <summary>
        /// The body, alternate form and shadow share one actor pose. The
        /// existing EntityBase policy keeps local first-person current and
        /// leaves live remote players to network playout below.
        /// </summary>
        internal void ModGetDrawActorPose(out Vector3 position, out Vector3 facing)
        {
            position = Position;
            facing = _facingVector;
            // Third-person exposes the local biped. Its world draw must
            // consume the same completed-history fractional clock as the
            // camera, instead of 60 Hz simulation jumps. Do not modify
            // the local first-person or authoritative movement paths.
            if (ThirdPersonBodyBlendEnabled && IsMainPlayer
                && _scene.ModThirdPersonCameraActive && !_scene.Services.IsReplica
                && FrameTiming.Active && FrameTiming.HighRefreshPresentation
                && CameraType == CameraType.First
                && !IsAltForm && !IsMorphing && !IsUnmorphing
                && ModTrySampleCapturedDrawTransform(out Matrix4 thirdPersonBody,
                    out _))
            {
                position = thirdPersonBody.Row3.Xyz;
                Vector3 interpolatedFacing = thirdPersonBody.Row2.Xyz;
                if (float.IsFinite(interpolatedFacing.X)
                    && float.IsFinite(interpolatedFacing.Y)
                    && float.IsFinite(interpolatedFacing.Z)
                    && interpolatedFacing.LengthSquared > 0.000001f)
                    facing = interpolatedFacing.Normalized();
                return;
            }
            if (!InterpolateDrawTransform) return;
            if (!_scene.Services.IsReplica && (!FrameTiming.Active || !FrameTiming.HighRefreshPresentation)) return;
            Matrix4 pose = ReplayDrawTransform;
            position = pose.Row3.Xyz;
            Vector3 sampledFacing = pose.Row2.Xyz;
            if (_scene.Services.IsReplica)
                facing = sampledFacing.Normalized(); // preserve the recorded-pose branch
            else if (float.IsFinite(sampledFacing.X) && float.IsFinite(sampledFacing.Y)
                && float.IsFinite(sampledFacing.Z) && sampledFacing.LengthSquared >= 0.000001f)
                facing = sampledFacing.Normalized();
        }

        internal static Matrix4 ModBuildBipedDrawTransform(Vector3 position, Vector3 facing,
            Vector3 fallbackLateral, float scale, float bottom, out float pitch)
        {
            float limit = Fixed.ToFloat(2896);
            float cos = MathF.Sqrt(1 - facing.Y * facing.Y);
            float sin = facing.Y;
            if (MathF.Abs(facing.Y) > limit)
            {
                cos = limit;
                sin = facing.Y <= 0 ? -limit : limit;
            }
            pitch = MathF.Atan2(sin, cos);
            Vector3 lateral = new(facing.X, 0, facing.Z);
            if (lateral.LengthSquared < 0.000001f) lateral = fallbackLateral;
            lateral = lateral.Normalized();
            Vector3 bodyRight = new(lateral.Z, 0, -lateral.X);
            Matrix4 transform = Matrix4.Identity;
            transform.Row0.Xyz = -bodyRight * scale;
            transform.Row1.Xyz = Vector3.Cross(lateral, bodyRight) * scale;
            transform.Row2.Xyz = -lateral * scale;
            transform.Row3.Xyz = position;
            transform.Row3.Y += bottom + bottom * (1 - scale);
            return transform;
        }

        public void Draw()
        {
            // Player entities bypass GetDrawInfo() in Renderer; emit replay-only
            // transient impact particles from the guaranteed player draw path.
            if (_scene.Services.IsReplica && IsMainPlayer)
                _scene.ReplayPoses?.DrawResolvedImpactPresentation(_scene);

            Vector3 presentedPosition = default;
            bool presentedAlt = false;
            bool networkPresented = !_scene.Services.IsReplica && Mods.Network.NetSession.Active
                && SlotIndex != Mods.Network.NetHooks.LocalSlot
                && Mods.Network.NetSmoothing.SamplePresentation(
                    SlotIndex, out presentedPosition, out presentedAlt);
            ModGetDrawActorPose(out Vector3 drawPosition, out Vector3 drawFacing);
            if (networkPresented)
                drawPosition = Mods.Network.NetPlayerBridge.InFormFor(this, presentedPosition, presentedAlt);
            bool drawAltForm = IsAltForm;
            bool drawAlive = _health > 0;
            // This is the newly enabled consumer. Preserve preexisting live
            // network/replay/spectator branches while scoping its sampled body
            // state away from canonical model/effect/checkpoint owners.
            bool ordinaryOfflinePresentation = !_scene.Services.IsReplica && !Mods.Network.NetSession.Active
                && !IsMainPlayer && !Mods.SpectatorMode.IsSpectating && drawAlive
                && FrameTiming.Active && FrameTiming.HighRefreshPresentation;
            bool ordinaryOfflineActor = ordinaryOfflinePresentation
                && (drawPosition != Position || drawFacing != _facingVector);
            // Only a locally visible 3P biped receives this sampled pose;
            // the model, shadow and emitter must be canonical again after
            // each draw. Smooth crouch's render-only pose shares this fence.
            bool localThirdPersonActor = ThirdPersonBodyBlendEnabled
                && IsMainPlayer && _scene.ModThirdPersonCameraActive
                && !_scene.Services.IsReplica
                && FrameTiming.Active && FrameTiming.HighRefreshPresentation
                && (drawPosition != Position || drawFacing != _facingVector
                    || ModCrouchVisualAmount != (ModCrouched ? 1f : 0f) || ModCrouchGaitActive);
            bool isolateLocalOrOfflineActor = ordinaryOfflineActor || localThirdPersonActor;
            bool forceNetworkVisibility = ModForceNetworkVisibility(
                _scene.Services.IsReplica, Mods.Network.NetSession.Active,
                SlotIndex, Mods.Network.NetHooks.LocalSlot,
                LoadFlags.TestFlag(LoadFlags.Active),
                LoadFlags.TestFlag(LoadFlags.Spawned), _health);

            if (_scene.ReplayPoses?.SamplePresented(SlotIndex, _scene.ReplayRenderAlpha, out _, out Vector3 replicaFacing) == true)
            {
                drawFacing = replicaFacing;
            }
            else if (networkPresented && Mods.Render.FrameTiming.Active
                && CameraInfo.ModGetFirstPersonDrawPose(
                    Mods.Render.FrameTiming.PresentationAlpha,
                    out Vector3 drawCameraPosition, out Vector3 drawCameraTarget,
                    out _, out _))
            {
                Vector3 presentedFacing = drawCameraTarget - drawCameraPosition;
                if (presentedFacing.LengthSquared > 0.000001f)
                {
                    drawFacing = presentedFacing.Normalized();
                }
            }

            if (Flags2.TestFlag(PlayerFlags2.Spectating))
            {
                // Before the shadow, not after it. A spectator is out of the
                // match on every machine, and the shadow is drawn from the
                // player's volume rather than from the model -- so hiding only
                // the model left a shadow sliding around the floor under
                // nobody, which is exactly how it was reported.
                return;
            }
            DrawShadow(drawPosition);
            if (IsMainPlayer && ScanVisor)
            {
                DrawScanModels();
            }
            bool classicHeadDeathVisible = !drawAlive && ModClassicHeadDeathVisible;
            bool customDeathVisible = !drawAlive && (classicHeadDeathVisible
                || Mods.Cosmetics.Death.DeathPresentationRuntime.Visible(_cosmeticDeath, CosmeticDeathDefinition, _scene.ElapsedTime));
            if (customDeathVisible)
            {
                if (classicHeadDeathVisible && _scene.ModHeadDeathAnchor(this, out var deathPosition, out var deathFacing))
                { drawPosition = deathPosition; drawFacing = deathFacing; drawAltForm = false; }
                else
                { drawPosition = _cosmeticDeath.Position; drawFacing = _cosmeticDeath.Facing; drawAltForm = _cosmeticDeath.WasAltForm; }
            }
            if (Flags2.TestFlag(PlayerFlags2.HideModel) && !forceNetworkVisibility && !customDeathVisible)
            {
                return;
            }
            if (Hunter == Hunter.Spire && Flags2.TestFlag(PlayerFlags2.AltAttack))
            {
                AnimateSpireAltAttack();
            }
            Vector4? brightSkin = BrightSkins.GetColor(this);
            Vector4? outlineColor = BrightSkins.GetOutlineColor(this);
            if (ShouldFlashSpawnProtection())
            {
                // Reuse the player surface-color path so biped and alt forms get the
                // same cue without adding another material/shader state. Between white
                // frames the normal suit/team render is untouched.
                brightSkin = Vector4.One;
            }
            int lod = 0;
            Flags2 &= ~PlayerFlags2.Lod1;
            if (!IsMainPlayer && !Features.MaxPlayerDetail
                && (Position - _scene.Players.Main.CameraInfo.Position).LengthSquared >= 3 * 3)
            {
                lod = 1;
                Flags2 |= PlayerFlags2.Lod1;
            }
            _bipedModel1.SetModel(_bipedModelLods[lod].Model);
            _bipedModel2.SetModel(_bipedModelLods[lod].Model);
            Flags2 &= ~PlayerFlags2.DrawnThirdPerson;
            bool drawBiped = false;
            // Portal visibility is safe for ordinary entities because their
            // position and NodeRef advance together. A remote network player is
            // different: the body may be drawn from NetSmoothing's presentation
            // sample while NodeRef tracks the newer simulation placement.
            //
            // A valid-but-wrong NodeRef is worse than an unresolved one: the
            // existing ModNodeUnresolved backstop cannot help because resolution
            // technically succeeded. Never let that mismatch hide a living remote
            // opponent. World geometry/depth still occludes the model normally.
            if (IsMainPlayer || forceNetworkVisibility || ModNodeUnresolved || IsVisible(NodeRef))
            {
                drawBiped = customDeathVisible || !IsMainPlayer || CameraType != CameraType.First
                    || (IsMainPlayer && _scene.ModThirdPersonCameraActive)
                    || _scene.CameraSequences.Current != null
                    || _camSwitchTimer < Values.CamSwitchTime * 2; // todo: FPS stuff
                if (drawAltForm && !drawAlive && _cosmeticDeath.Active
                    && CosmeticDeathDefinition.WireId != 0 && Mods.RenderOptions.ShowCustomCosmetics
                    && Mods.RenderOptions.CosmeticQuality != Mods.Cosmetics.CosmeticEffectQuality.Off)
                {
                    var deathTransform = _modelTransform;
                    deathTransform.Row3.Xyz = drawPosition;
                    UpdateTransforms(_altModel, deathTransform, Recolor);
                    DrawCosmeticDeath(_altModel);
                    Flags2 |= PlayerFlags2.DrawnThirdPerson;
                }
                else if (drawAltForm)
                {
                    Matrix4 canonicalAlt = _modelTransform;
                    canonicalAlt.Row3.Xyz = Position;
                    _modelTransform = ModGetAlternateFormDrawPose(drawPosition, out bool specializedAltSample);
                    try
                    {
                    if (_timeSinceDamage < Values.DamageFlashTime * 2) // todo: FPS stuff
                    {
                        PaletteOverride = Metadata.RedPalette;
                    }
                    if (ScoutDroneEnabled)
                    {
                        DrawScoutDrone(brightSkin, outlineColor, drawPosition, drawFacing);
                    }
                    else if (Hunter == Hunter.Kanden)
                    {
                        specializedAltSample |= DrawKandenAlt(brightSkin, outlineColor, drawPosition);
                    }
                    else if (Hunter == Hunter.Spire && Flags2.TestFlag(PlayerFlags2.AltAttack))
                    {
                        specializedAltSample |= DrawSpireAltAttack(brightSkin, outlineColor);
                    }
                    else
                    {
                        UpdateTransforms(_altModel, _modelTransform, Recolor);
                        bool altSample = TryHunterNodeSample(_altModel, out var altPresentation);
                        try
                        {
                            if (altSample)
                                ApplyHunterNodePose(_altModel, _modelTransform, altPresentation);
                            if (!TryDrawModernCharacter(_altModel, CharacterModelPart.AlternateForm,
                                _curAlpha, overrideColor: brightSkin, outlineColor: outlineColor))
                                GetDrawItems(_altModel, _altModel.Model.Nodes[0], _curAlpha,
                                    overrideColor: brightSkin, outlineColor: outlineColor);
                        }
                        finally
                        {
                            if (altSample)
                                ApplyHunterNodePose(_altModel, _modelTransform, null);
                        }
                    }
                    PaletteOverride = null;
                    if (_frozenGfxTimer > 0)
                    {
                        float radius = _volume.SphereRadius + 0.2f;
                        Matrix4 transform = Matrix4.CreateScale(radius) * _modelTransform;
                        transform.Row3.Y += Fixed.ToFloat(Values.AltColYPos);
                        UpdateTransforms(_altIceModel, transform, recolor: 0);
                        GetDrawItems(_altIceModel, _altIceModel.Model.Nodes[0], alpha: 1, recolor: 0);
                    }
                    if (Hunter == Hunter.Samus
                        && !Flags2.TestFlag(PlayerFlags2.Cloaking))
                    {
                        DrawMorphBallTrail();
                    }
                    DrawCosmeticArmor(_altModel.Model, true);
                    Flags2 |= PlayerFlags2.DrawnThirdPerson;
                    }
                    finally
                    {
                        // Primitive canonical state must be restored even if
                        // a later node cleanup fails on invalid model data.
                        _modelTransform = canonicalAlt;
                        _modelTransform.Row3.Xyz = Vector3.Zero;
                        if (ordinaryOfflineActor || specializedAltSample)
                        {
                            PaletteOverride = null;
                            if (Hunter == Hunter.Spire && Flags2.TestFlag(PlayerFlags2.AltAttack))
                            {
                                AnimateSpireAltAttack();
                                UpdateSpireAltAttackNodes(canonicalAlt);
                            }
                            else if (Hunter == Hunter.Kanden)
                            {
                                for (int i = 0; i < _kandenSegMtx.Length; i++)
                                    _altModel.Model.Nodes[i].Animation = _kandenSegMtx[i];
                                _altModel.Model.UpdateMatrixStack();
                            }
                            else
                                ApplyHunterNodePose(_altModel, canonicalAlt, null);
                            if (_frozenGfxTimer > 0)
                            {
                                float radius = _volume.SphereRadius + 0.2f;
                                Matrix4 canonicalIce = Matrix4.CreateScale(radius) * canonicalAlt;
                                canonicalIce.Row3.Y += Fixed.ToFloat(Values.AltColYPos);
                                ApplyHunterNodePose(_altIceModel, canonicalIce, null);
                            }
                        }
                    }
                }
                else if (drawBiped)
                {
                    // we want to animate first up to the spine with _bipedModel1's animation info, then the rest with _bipedModel2's info
                    Node spineNode = _spineNodes[lod]!;
                    // Preserve the original split-lower/upper animation
                    // semantics and spine look offset at any display rate.
                    Model model = _bipedModel1.Model;
                    float scale = Metadata.HunterScales[Hunter];
                    float bottom = Fixed.ToFloat(Values.MinPickupHeight);
                    if (ModTucked) drawPosition.Y += ModTuckRise;
                    // Build the render-only body basis from the same facing
                    // selected above for this picture. Remote/replay hunters have
                    // already been angularly interpolated there; using raw
                    // _field70/_field74/_gunVec2 here put their torso rotation
                    // back on the 60 Hz simulation clock.
                    Vector3 fallbackLateral = new(_field70, 0, _field74);
                    Matrix4 transform = ModBuildBipedDrawTransform(drawPosition, drawFacing,
                        fallbackLateral, scale, bottom, out float angle);
                    float crouchVisual = ModCrouchVisualAmount;
                    bool crouchInterpolated = crouchVisual != (ModCrouched ? 1 : 0) || ModCrouchGaitActive;
                    float canonicalAngle = angle;
                    Matrix4 canonicalBody = isolateLocalOrOfflineActor || crouchInterpolated
                        ? ModBuildBipedDrawTransform(Position.AddY(ModTucked ? ModTuckRise : 0), _facingVector,
                            fallbackLateral, scale, bottom, out canonicalAngle) : transform;
                    if (!drawAlive && Mods.Cosmetics.Death.DeathPresentationRuntime.Visible(_cosmeticDeath, CosmeticDeathDefinition, _scene.ElapsedTime))
                        transform = Mods.Cosmetics.Death.DeathPresentationRuntime.Pose(CosmeticDeathDefinition,
                            _cosmeticDeath.Progress(_scene.ElapsedTime, CosmeticDeathDefinition)) * transform;
                    AnimationSamples.Sample? lowerSample = null, upperSample = null;
                    if (HunterAnimationBlendEnabled && FrameTiming.Active
                        && FrameTiming.HighRefreshPresentation && !_scene.Services.IsReplica
                        && _frozenTimer == 0)
                    {
                        double presentation = FrameTiming.TotalSteps + FrameTiming.PresentationAlpha;
                        if (_bipedModel1.PresentationSamples.TryGet(_bipedModel1.AnimInfo,
                            _bipedModel1.AnimInfo.Node.Slot, presentation, out var lower))
                            lowerSample = lower;
                        if (_bipedModel2.PresentationSamples.TryGet(_bipedModel2.AnimInfo,
                            _bipedModel2.AnimInfo.Node.Slot, presentation, out var upper))
                            upperSample = upper;
                    }
                    bool interpolatedPose = lowerSample.HasValue || upperSample.HasValue;
                    // Node interpolation can remain fractional when float actor
                    // interpolation rounds exactly to the current world pose.
                    // These canonical owners must not inherit either sample.
                    bool isolateBipedAnchors = isolateLocalOrOfflineActor || crouchInterpolated
                        || (ordinaryOfflinePresentation && interpolatedPose);
                    try
                    {
                    UpdateHunterBipedVisual(model, spineNode, transform, angle,
                        lowerSample, upperSample, crouchVisual);
                    if (drawAlive)
                    {
                        if (_timeSinceDamage < Values.DamageFlashTime * 2) // todo: FPS stuff
                        {
                            PaletteOverride = Metadata.RedPalette;
                        }
                        float alpha = _curAlpha;
                        if (IsMainPlayer && _scene.ModThirdPersonCameraActive)
                            alpha *= _scene.ModThirdPersonLocalBodyOpacity;
                        if (IsMainPlayer && _scene.CameraSequences.Current == null
                            && _bipedModel1.AnimInfo.Index[0] == (int)PlayerAnimation.Unmorph)
                        {
                            alpha -= alpha * _bipedModel1.AnimInfo.Frame[0] / _bipedModel1.AnimInfo.FrameCount[0];
                            alpha = Math.Clamp(alpha, 0, 1);
                        }
                        UpdateMaterials(_bipedModel2, Recolor);
                        // The replacement pack may provide the same LOD tier
                        // the native player draw already selected. Missing HD
                        // LOD1 falls through to the native LOD1 automatically.
                        using (Mods.Render.HeadHitPoseOverlay.Begin(_bipedModel2.Model, _scene.ModHeadHitPose(this)))
                        {
                        if (!ModTryDrawSourceBody(drawPosition, drawFacing, alpha, brightSkin, outlineColor, lod)
                            && !TryDrawModernCharacter(_bipedModel2,
                            CharacterModelPart.Biped, alpha, lod: lod,
                            overrideColor: brightSkin, outlineColor: outlineColor))
                            GetDrawItems(_bipedModel2, _bipedModel2.Model.Nodes[0], alpha,
                                overrideColor: brightSkin, outlineColor: outlineColor);
                        }
                        PaletteOverride = null;
                        ModDrawCustomRifle(firstPerson: false,lod,alpha);
                        if (_chargeEffect != null || _muzzleEffect != null)
                        {
                            // Capture the visible cannon tip before returning
                            // to the canonical skeleton for combat and effect
                            // simulation. This is picture-only, never a muzzle
                            // source for projectile hit registration.
                            Vector3 displayMuzzle = default;
                            if (localThirdPersonActor)
                                displayMuzzle = Matrix.Vec3MultMtx4(
                                    Metadata.MuzzleOffests[(int)Hunter],
                                    _shootNodes[lod]!.Animation);
                            if (isolateBipedAnchors)
                                UpdateHunterBipedPresentation(model, spineNode, canonicalBody, canonicalAngle, null, null);
                            try
                            {
                            Vector3 muzzlePos = Metadata.MuzzleOffests[(int)Hunter];
                            muzzlePos = Matrix.Vec3MultMtx4(muzzlePos, _shootNodes[lod]!.Animation);
                            // Preserve canonical emitter updates on every existing
                            // positive-pending path before PrepareEffectDraw. Only
                            // a live offline main player's repeated picture uses
                            // a world-projected, one-picture attachment instead.
                            bool presentationOnlyMainEffect = ReferenceEquals(this, _scene.Players.Main)
                                && !_scene.Services.IsReplica
                                && (!Mods.Network.NetSession.Active || _scene.ModThirdPersonCameraActive)
                                && !Mods.Network.DemoPlayback.IsActive && !Mods.KillCam.Active
                                && !Mods.SpectatorMode.IsSpectating && FrameTiming.Active
                                && !_scene.HasPendingEffectSimulation;
                            Vector3 effectDrawMuzzle = localThirdPersonActor
                                && presentationOnlyMainEffect ? displayMuzzle : muzzlePos;
                            if (_chargeEffect != null)
                            {
                                _chargeEffect.SetDrawEnabled(true);
                                if (presentationOnlyMainEffect)
                                    _scene.SetPresentationEffectTransform(_chargeEffect,
                                        GetTransformMatrix(_gunVec2, _gunVec1, effectDrawMuzzle), viewModel: false);
                                else
                                    _chargeEffect.Transform(_gunVec2, _gunVec1, muzzlePos);
                            }
                            if (_muzzleEffect != null)
                            {
                                _muzzleEffect.SetDrawEnabled(true);
                                if (presentationOnlyMainEffect)
                                    _scene.SetPresentationEffectTransform(_muzzleEffect,
                                        GetTransformMatrix(_gunVec2, _gunVec1, effectDrawMuzzle), viewModel: false);
                                else
                                    _muzzleEffect.Transform(_gunVec2, _gunVec1, muzzlePos);
                            }
                            }
                            finally
                            {
                                if (isolateBipedAnchors)
                                    UpdateHunterBipedVisual(model, spineNode, transform, angle, lowerSample, upperSample, crouchVisual);
                            }
                        }
                        if (_frozenGfxTimer > 0)
                        {
                            for (int i = 0; i < _bipedIceModel.Model.Nodes.Count; i++)
                            {
                                _bipedIceModel.Model.Nodes[i].Animation = _bipedModel1.Model.Nodes[i].Animation;
                                _bipedIceTransforms[i] = _bipedModel1.Model.Nodes[i].Animation;
                            }
                            _bipedIceModel.Model.UpdateMatrixStack();
                            UpdateMaterials(_bipedIceModel, recolor: 0);
                            GetDrawItems(_bipedIceModel, _bipedIceModel.Model.Nodes[0], alpha: 1, recolor: 0);
                        }
                    }
                    DrawCosmeticArmor(_bipedModel1.Model, false);
                    _modelTransform = isolateBipedAnchors ? canonicalBody : transform;
                    if (_health == 0)
                    {
                        if (!DrawCosmeticDeath()) DrawDeathParticles();
                    }
                    Flags2 |= PlayerFlags2.DrawnThirdPerson;
                    }
                    finally
                    {
                        // The world still reads canonical model nodes for muzzle
                        // and effect ownership. Render sampling must leave only
                        // the integer pose even when a material/LOD draw fails.
                        if (isolateBipedAnchors)
                        {
                            _modelTransform = canonicalBody;
                            PaletteOverride = null;
                        }
                        if (isolateBipedAnchors || interpolatedPose)
                            UpdateHunterBipedPresentation(model, spineNode,
                                isolateBipedAnchors ? canonicalBody : transform,
                                isolateBipedAnchors ? canonicalAngle : angle,
                                null, null);
                        if (isolateBipedAnchors && _frozenGfxTimer > 0)
                        {
                            // The simulation uses this archive to place ice
                            // break effects; leave the completed body pose.
                            for (int i = 0; i < _bipedIceModel.Model.Nodes.Count; i++)
                            {
                                _bipedIceModel.Model.Nodes[i].Animation = _bipedModel1.Model.Nodes[i].Animation;
                                _bipedIceTransforms[i] = _bipedModel1.Model.Nodes[i].Animation;
                            }
                            _bipedIceModel.Model.UpdateMatrixStack();
                        }
                    }
                }
                else if (AttachedEnemy == null && !_field6D0 && Hunter != Hunter.Guardian)
                {
                    // The arm cannon is camera-attached viewmodel geometry, not
                    // part of the room. Mark everything emitted here so the
                    // renderer can use the camera-authored FOV instead of the
                    // player's widened world FOV. Keeping the same render-item
                    // path preserves materials, depth, transparency and cel ink.
                    _scene.BeginViewModelItems();
                    try
                    {
                        // TransformCamera prepared a camera-local first-person pose
                        // before entity drawing. Consume that exact pose here so a
                        // 90/120/240/540 Hz late-latched camera can never leave the
                        // arm cannon sitting at the previous 60 Hz orientation.
                        Matrix4 transform = ModGetFirstPersonGunTransform(out Matrix4 renderTransform)
                            ? renderTransform
                            : GetTransformMatrix(_aimVec, _upVector, _gunDrawPos);
                        using var ownerGunDraw = ModBeginOwnerGunDraw(transform);
                        UpdateTransforms(_gunModel, transform, Recolor);
                        bool gunSample = ModTryOwnerGunSample(out var gunPresentation)
                            || TryHunterNodeSample(_gunModel, out gunPresentation);
                        try
                        {
                            if (gunSample)
                                ApplyHunterNodePose(_gunModel, transform, gunPresentation);
                            if (!ModTryDrawSourceCannon(transform)
                                && !TryDrawModernCharacter(_gunModel, CharacterModelPart.ViewModel, _curAlpha))
                                GetDrawItems(_gunModel, _gunModel.Model.Nodes[0], _curAlpha);
                        }
                        finally
                        {
                            // The original muzzle and charge effects are
                            // simulation owned. Never leave a sampled cannon
                            // pose behind after submitting draw geometry.
                            if (gunSample)
                                ApplyHunterNodePose(_gunModel, transform, null);
                        }
                        ModDrawCustomRifle(firstPerson: true,lod: 0,_curAlpha);
                        if (Flags1.TestFlag(PlayerFlags1.DrawGunSmoke))
                        {
                            var drawPos = new Vector3(0, 0, Fixed.ToFloat(Values.MuzzleOffset));
                            drawPos = Matrix.Vec3MultMtx4(drawPos, transform);
                            transform.Row3.Xyz = drawPos;
                            UpdateTransforms(_gunSmokeModel, transform, recolor: 0);
                            GetDrawItems(_gunSmokeModel, _gunSmokeModel.Model.Nodes[0], _smokeAlpha, recolor: 0);
                        }
                        if (ModGetFirstPersonEffectTransform(out Matrix4 effectTransform))
                        {
                            // These EffectEntry transforms remain simulation-owned.
                            // Only their particle draw reads this one-picture override,
                            // so charge/muzzle visuals follow the same late-latched
                            // barrel without changing spawning or effect lifetime.
                            if (_chargeEffect != null)
                            {
                                _scene.SetPresentationEffectTransform(_chargeEffect, effectTransform);
                            }
                            if (_muzzleEffect != null)
                            {
                                _scene.SetPresentationEffectTransform(_muzzleEffect, effectTransform);
                            }
                        }
                    }
                    finally
                    {
                        _scene.EndViewModelItems();
                    }
                }
            }
            if (!IsMainPlayer && !drawBiped)
            {
                if (_chargeEffect != null)
                {
                    _chargeEffect.SetDrawEnabled(false);
                }
                if (_muzzleEffect != null)
                {
                    _muzzleEffect.SetDrawEnabled(false);
                }
            }
            if (_scene.GameState.SinglePlayer && IsMainPlayer && _deathCountdown > 0 && _deathCountdown <= 119 / 30f
                && System.Numerics.BitOperations.PopCount(_scene.GameState.StorySave.CurrentOctoliths) > 0)
            {
                Matrix4 transform = Matrix4.Identity;
                transform.Row3.Xyz = _lostOctolithDrawPos;
                UpdateTransforms(_octolithSimpleModel, transform, recolor: 0);
                GetDrawItems(_octolithSimpleModel, _octolithSimpleModel.Model.Nodes[0], alpha: 1, recolor: 0);
            }
            DrawVolumes();
        }

        /// <summary>
        /// Compose a two-channel Hunter pose from original animation curves.
        /// All seven Hunters use the same lower/upper split. This method is
        /// called again with null samples in finally to restore the current
        /// canonical pose, not the pose from a previous presentation frame.
        /// </summary>
        /// <summary>
        /// Native viewmodels and ordinary alt forms share the same immutable
        /// per-tick sampling cadence as the two-part body. Specialized Kanden
        /// segments and Spire attack collision nodes are not sampled here.
        /// </summary>
        private bool TryHunterNodeSample(ModelInstance instance,
            out AnimationSamples.Sample sampled)
        {
            sampled = default;
            return HunterAnimationBlendEnabled && FrameTiming.Active
                && FrameTiming.HighRefreshPresentation && !_scene.Services.IsReplica
                && _frozenTimer == 0
                && instance.PresentationSamples.TryGet(instance.AnimInfo,
                    instance.AnimInfo.Node.Slot,
                    FrameTiming.TotalSteps + FrameTiming.PresentationAlpha, out sampled);
        }

        private void ApplyHunterNodePose(ModelInstance instance, Matrix4 transform,
            AnimationSamples.Sample? sample)
        {
            Model model = instance.Model;
            model.AnimateNodes(0, UseNodeTransform, transform, model.Scale, instance.AnimInfo, sample);
            model.UpdateMatrixStack();
        }

        private void UpdateHunterBipedPresentation(Model model, Node spineNode,
            Matrix4 world, float pitch,
            AnimationSamples.Sample? lower, AnimationSamples.Sample? upper)
            => UpdateHunterBipedPose(model, spineNode, world, pitch, lower, upper,
                ModCrouched ? 1 : 0, visualGait: false);

        private void UpdateHunterBipedVisual(Model model, Node spineNode, Matrix4 world, float pitch,
            AnimationSamples.Sample? lower, AnimationSamples.Sample? upper, float crouchAmount)
            => UpdateHunterBipedPose(model, spineNode, world, pitch, lower, upper, crouchAmount,
                visualGait: ModCrouchGaitActive);

        private void UpdateHunterBipedPose(Model model, Node spineNode, Matrix4 world, float pitch,
            AnimationSamples.Sample? lower, AnimationSamples.Sample? upper, float crouchAmount, bool visualGait)
        {
            AnimationInfo lowerInfo = _bipedModel1.AnimInfo;
            AnimationInfo upperInfo = _bipedModel2.AnimInfo;
            // Procedural legs must not relax or retime the native weapon arm.
            // Walking, shot, charge and ability upper curves keep their aim pose.
            spineNode.AnimIgnoreChild = true;
            spineNode.AfterTransform = Matrix4.CreateRotationZ(pitch);
            try
            {
                if (visualGait)
                {
                    AnimationInfo neutral = ModCrouchLowerPose(model, out var neutralSample);
                    model.AnimateNodesBlended(0, lowerInfo, lower, neutral, neutralSample,
                        Math.Max(ModCrouchWalkAmount, ModSlideVisualAmount));
                }
                else model.AnimateNodes(0, false, Matrix4.Identity, Vector3.One, lowerInfo, lower);
            }
            finally
            {
                spineNode.AnimIgnoreChild = false;
                spineNode.AfterTransform = null;
            }
            model.AnimateNodes(spineNode.ChildIndex, false, Matrix4.Identity, Vector3.One,
                upperInfo, upper);
            foreach (Node node in model.Nodes)
                node.Animation *= world;
            ModApplyCrouchVisualPose(model, world, crouchAmount, visualGait);
            model.UpdateMatrixStack();
        }

        private bool DrawKandenAlt(Vector4? brightSkin, Vector4? outlineColor, Vector3 drawPosition)
        {
            bool sampled = false;
            for (int i = 0; i < _kandenSegMtx.Length; i++)
            {
                Matrix4 segment = ModGetKandenSegmentDrawPose(i, drawPosition, out bool segmentSampled);
                sampled |= segmentSampled || segment != _kandenSegMtx[i];
                _altModel.Model.Nodes[i].Animation = segment;
            }
            _altModel.Model.UpdateMatrixStack();
            UpdateMaterials(_altModel, Recolor);
            if (!TryDrawModernCharacter(_altModel, CharacterModelPart.AlternateForm,
                _curAlpha, overrideColor: brightSkin, outlineColor: outlineColor))
                GetDrawItems(_altModel, _altModel.Model.Nodes[0], _curAlpha,
                    overrideColor: brightSkin, outlineColor: outlineColor);
            return sampled;
        }

        private bool DrawSpireAltAttack(Vector4? brightSkin, Vector4? outlineColor)
        {
            bool sampled = TryHunterNodeSample(_altModel, out var presentation);
            Matrix4 rotation = SpireAttackDrawRotation();
            bool rotationSampled = rotation != GetTransformMatrix(_spireAltFacing, _spireAltUp);
            _altModel.Model.AnimateNodes(0, false, rotation,
                Vector3.One, _altModel.AnimInfo, sampled ? presentation : null);
            UpdateSpireAltAttackNodes(_modelTransform);
            UpdateMaterials(_altModel, Recolor);
            if (!TryDrawModernCharacter(_altModel, CharacterModelPart.AlternateForm,
                _curAlpha, overrideColor: brightSkin, outlineColor: outlineColor))
                GetDrawItems(_altModel, _altModel.Model.Nodes[0], _curAlpha,
                    overrideColor: brightSkin, outlineColor: outlineColor);
            return sampled || rotationSampled;
        }

        private void UpdateSpireAltAttackNodes(Matrix4 transform)
        {
            _altModel.Model.Nodes[0].Animation = transform;
            for (int i = 1; i < _altModel.Model.Nodes.Count; i++)
            {
                Node node = _altModel.Model.Nodes[i];
                Matrix4 animation = node.Animation;
                animation.Row3.Xyz += transform.Row3.Xyz;
                node.Animation = animation;
            }
            _altModel.Model.UpdateMatrixStack();
        }

        private bool ShouldFlashSpawnProtection()
        {
            if (!ModMatchSpawnProtectionActive
                || Flags2.TestFlag(PlayerFlags2.Cloaking)
                || BrightSkinStatusOverride || BrightSkinFrozenOverlay)
            {
                return false;
            }

            // Presentation has its own local clock. Protection truth comes from
            // the authority, but a cosmetic pulse does not need its remaining
            // frame count and therefore cannot drift the gameplay timer.
            return (ModSpawnProtectionVisualTicks / SpawnProtectionFlashHalfPeriodFrames & 1) == 0;
        }

        private bool TryDrawModernCharacter(ModelInstance inst, CharacterModelPart part, float alpha,
            int lod = 0, int recolor = -1, Vector4? overrideColor = null, Vector4? outlineColor = null, bool cosmeticDeath = false)
        {
            if (alpha <= 0) return true;
            Model model = inst.Model;
            if (CharacterModelRuntime.TryGetWeighted(_scene, Hunter, part, model,
                out CharacterWeightedRenderModel weighted, lod))
            {
                weighted.UpdatePalette(model);
                int weightedPolygonId = _scene.GetNextPolygonId();
                Node texgenNode = model.Nodes[0];
                foreach (CharacterWeightedRenderSegment segment in weighted.Segments)
                {
                    Material material = model.Materials[segment.NativeMaterialIndex];
                    Vector3 emission = GetEmission(inst, material, segment.NativeMaterialIndex);
                    Vector4? color = PaletteOverride == null
                        ? BrightSkins.ForMaterial(overrideColor, material.TextureId != -1,
                            material.CurrentAlpha * alpha, _scene.ShowTextures) : null;
                    int? authoredAlbedo = segment.GetAlbedo(_scene, Recolor);
                    int? bindingOverride = GetBindingOverride(
                        inst, material, segment.NativeMaterialIndex) ?? authoredAlbedo;
                    bool authoredTexture = segment.AlbedoBinding.HasValue
                        && bindingOverride == authoredAlbedo;
                    Matrix4 texcoordMatrix = !authoredTexture && bindingOverride.HasValue
                        ? GetTexcoordMatrix(inst, material, segment.NativeMaterialIndex,
                            texgenNode, recolor)
                        : Matrix4.Identity;

                    var previousCosmetic = _scene.CosmeticSubmission;
                    var previousMaterial = _scene.CosmeticMaterialSubmission;
                    if (Mods.RenderOptions.ShowCustomCosmetics && !BrightSkinStatusOverride
                        && !BrightSkinFrozenOverlay && !ModMatchSpawnProtectionActive
                        && _timeSinceDamage >= Values.DamageFlashTime * 2
                        && !BrightSkins.ShouldApply(this)
                        && !Flags2.TestFlag(PlayerFlags2.Cloaking)
                        && _curAlpha >= 1 && !_scene.GameState.Teams)
                        _scene.CosmeticMaterialSubmission = _scene.GetCosmeticMaterial(
                            CosmeticAppearance.Skin, model, segment.NativeMaterialIndex,
                            part switch
                            {
                                CharacterModelPart.ViewModel => Mods.Cosmetics.SkinContext.ViewModel,
                                CharacterModelPart.AlternateForm => Mods.Cosmetics.SkinContext.AltForm,
                                _ => Mods.Cosmetics.SkinContext.Biped
                            });
                    if (!cosmeticDeath) _scene.CosmeticSubmission = CosmeticMaterial(
                        part == CharacterModelPart.ViewModel);
                    if (authoredTexture && _scene.CosmeticMaterialSubmission == default
                        && Mods.RenderOptions.AdvancedMaterials && segment.MaterialMaps.Any)
                        _scene.CosmeticMaterialSubmission = new(0, segment.MaterialMaps.Normal,
                            segment.MaterialMaps.Specular, segment.MaterialMaps.Emissive);
                    _scene.AddRenderItem(material, weightedPolygonId, alpha, emission,
                        GetLightInfo(), texcoordMatrix, Matrix4.Identity, segment.ListId,
                        weighted.Joints.Count, weighted.MatrixPalette, color,
                        PaletteOverride, SelectionType.None, BillboardMode.None,
                        _drawScale, bindingOverride,
                        color.HasValue && Mods.RenderOptions.BrightSkins
                            && Mods.RenderOptions.BrightSkinStyle != Mods.PlayerSkinStyle.Solid,
                        PaletteOverride == null ? outlineColor : null,
                        weightedSkinning: true, authoredTexture: authoredTexture,
                        authoredWrapS: segment.WrapS, authoredWrapT: segment.WrapT,
                            authoredDoubleSided: segment.DoubleSided,
                            authoredTransparent: authoredTexture && segment.Transparent);
                    _scene.CosmeticSubmission = previousCosmetic;
                    _scene.CosmeticMaterialSubmission = previousMaterial;
                }
                return true;
            }

            if (!CharacterModelRuntime.TryGetRigid(_scene, Hunter, part, model,
                out CharacterRigidRenderModel replacement, lod))
                return false;

            int polygonId = _scene.GetNextPolygonId();
            foreach (CharacterRigidRenderSegment segment in replacement.Segments)
            {
                Node node = model.Nodes[segment.NativeNodeIndex];
                if (!node.Enabled || !model.NodeParentsEnabled(node)) continue;
                Material material = model.Materials[segment.NativeMaterialIndex];
                Vector3 emission = GetEmission(inst, material, segment.NativeMaterialIndex);
                Vector4? color = PaletteOverride == null
                    ? BrightSkins.ForMaterial(overrideColor, material.TextureId != -1,
                        material.CurrentAlpha * alpha, _scene.ShowTextures) : null;
                int? authoredAlbedo = segment.GetAlbedo(_scene, Recolor);
                int? bindingOverride = GetBindingOverride(inst, material, segment.NativeMaterialIndex) ?? authoredAlbedo;
                bool authoredTexture = segment.AlbedoBinding.HasValue && bindingOverride == authoredAlbedo;
                Matrix4 texcoordMatrix = !authoredTexture && bindingOverride.HasValue
                    ? GetTexcoordMatrix(inst, material, segment.NativeMaterialIndex, node, recolor)
                    : Matrix4.Identity;

                var previousCosmetic = _scene.CosmeticSubmission;
                var previousMaterial = _scene.CosmeticMaterialSubmission;
                if (Mods.RenderOptions.ShowCustomCosmetics && !BrightSkinStatusOverride && !BrightSkinFrozenOverlay
                    && !ModMatchSpawnProtectionActive && _timeSinceDamage >= Values.DamageFlashTime * 2
                    && !BrightSkins.ShouldApply(this) && !Flags2.TestFlag(PlayerFlags2.Cloaking)
                    && _curAlpha >= 1 && !_scene.GameState.Teams)
                    _scene.CosmeticMaterialSubmission = _scene.GetCosmeticMaterial(
                        CosmeticAppearance.Skin, model, segment.NativeMaterialIndex,
                        part switch
                        {
                            CharacterModelPart.ViewModel => Mods.Cosmetics.SkinContext.ViewModel,
                            CharacterModelPart.AlternateForm => Mods.Cosmetics.SkinContext.AltForm,
                            _ => Mods.Cosmetics.SkinContext.Biped
                        });
                if (!cosmeticDeath) _scene.CosmeticSubmission = CosmeticMaterial(part == CharacterModelPart.ViewModel);
                if (authoredTexture && _scene.CosmeticMaterialSubmission == default
                    && Mods.RenderOptions.AdvancedMaterials && segment.MaterialMaps.Any)
                    _scene.CosmeticMaterialSubmission = new(0, segment.MaterialMaps.Normal,
                        segment.MaterialMaps.Specular, segment.MaterialMaps.Emissive);

                // GLB UVs are already normalized authoring coordinates. Reuse
                // the native material's binding/shading identity but not the
                // cartridge display-list texture matrix.
                _scene.AddRenderItem(material, polygonId, alpha, emission, GetLightInfo(), texcoordMatrix,
                    node.Animation, segment.ListId, 0, Array.Empty<float>(), color,
                    PaletteOverride, SelectionType.None, node.BillboardMode, _drawScale, bindingOverride,
                    color.HasValue && Mods.RenderOptions.BrightSkins
                        && Mods.RenderOptions.BrightSkinStyle != Mods.PlayerSkinStyle.Solid,
                    PaletteOverride == null ? outlineColor : null,
                    authoredTexture: authoredTexture, authoredWrapS: segment.WrapS, authoredWrapT: segment.WrapT,
                            authoredDoubleSided: segment.DoubleSided,
                            authoredTransparent: authoredTexture && segment.Transparent);

                _scene.CosmeticSubmission = previousCosmetic;
                _scene.CosmeticMaterialSubmission = previousMaterial;
            }
            return true;
        }

        private void GetDrawItems(ModelInstance inst, Node node, float alpha, int polygonId = -1, int recolor = -1,
            Vector4? overrideColor = null, Vector4? outlineColor = null, bool cosmeticDeath = false)
        {
            if (alpha <= 0)
            {
                return;
            }
            if (polygonId == -1)
            {
                polygonId = _scene.GetNextPolygonId();
            }
            Model model = inst.Model;
            if (node.Enabled)
            {
                int start = node.MeshId / 2;
                for (int i = 0; i < node.MeshCount; i++)
                {
                    Mesh mesh = model.Meshes[start + i];
                    if (!mesh.Visible)
                    {
                        continue;
                    }
                    Material material = model.Materials[mesh.MaterialId];
                    Vector3 emission = GetEmission(inst, material, mesh.MaterialId);
                    Matrix4 texcoordMatrix = GetTexcoordMatrix(inst, material, mesh.MaterialId, node, recolor);
                    Vector4? color = PaletteOverride == null
                        ? BrightSkins.ForMaterial(overrideColor, material.TextureId != -1,
                            material.CurrentAlpha * alpha, _scene.ShowTextures) : null;
                    SelectionType selectionType = SelectionType.None;
                    int? bindingOverride = GetBindingOverride(inst, material, mesh.MaterialId);
                    var previousCosmetic = _scene.CosmeticSubmission;
                    var previousMaterial = _scene.CosmeticMaterialSubmission;
                    if (Mods.RenderOptions.ShowCustomCosmetics && !BrightSkinStatusOverride && !BrightSkinFrozenOverlay
                        && !ModMatchSpawnProtectionActive && _timeSinceDamage >= Values.DamageFlashTime * 2
                        && !BrightSkins.ShouldApply(this) && !Flags2.TestFlag(PlayerFlags2.Cloaking)
                        && _curAlpha >= 1 && !_scene.GameState.Teams)
                        _scene.CosmeticMaterialSubmission = _scene.GetCosmeticMaterial(CosmeticAppearance.Skin, model, mesh.MaterialId,
                            inst == _gunModel ? Mods.Cosmetics.SkinContext.ViewModel : IsAltForm ? Mods.Cosmetics.SkinContext.AltForm : Mods.Cosmetics.SkinContext.Biped);
                    bool cosmeticBody = inst == _gunModel || inst == _altModel || inst == _bipedModel1 || inst == _bipedModel2;
                    if (!cosmeticDeath) _scene.CosmeticSubmission = cosmeticBody ? CosmeticMaterial(inst == _gunModel) : default;
                    if (!cosmeticBody) _scene.CosmeticMaterialSubmission = default;
                    _scene.AddRenderItem(material, polygonId, alpha, emission, GetLightInfo(), texcoordMatrix,
                        node.Animation, mesh.ListId, model.NodeMatrixIds.Count, model.MatrixStackValues, color,
                        PaletteOverride, selectionType, node.BillboardMode, _drawScale, bindingOverride,
                        color.HasValue && Mods.RenderOptions.BrightSkins
                            && Mods.RenderOptions.BrightSkinStyle != Mods.PlayerSkinStyle.Solid,
                        PaletteOverride == null ? outlineColor : null);
                    _scene.CosmeticSubmission = previousCosmetic;
                    _scene.CosmeticMaterialSubmission = previousMaterial;
                }
                if (node.ChildIndex != -1)
                {
                    GetDrawItems(inst, model.Nodes[node.ChildIndex], alpha, polygonId, recolor, overrideColor, outlineColor, cosmeticDeath);
                }
            }
            if (node.NextIndex != -1)
            {
                GetDrawItems(inst, model.Nodes[node.NextIndex], alpha, polygonId, recolor, overrideColor, outlineColor, cosmeticDeath);
            }
        }

        protected override int? GetBindingOverride(ModelInstance inst, Material material, int index)
        {
            if (_doubleDmgTimer > 0 && (Hunter != Hunter.Spire || !(inst == _gunModel && index == 0)) && material.Lighting > 0)
            {
                return _doubleDmgBindingId;
            }
            return base.GetBindingOverride(inst, material, index);
        }

        protected override Vector3 GetEmission(ModelInstance inst, Material material, int index)
        {
            if (_doubleDmgTimer > 0 && (Hunter != Hunter.Spire || !(inst == _gunModel && index == 0)) && material.Lighting > 0)
            {
                return Metadata.EmissionGray;
            }
            if (Team == Team.Orange)
            {
                return Metadata.EmissionOrange;
            }
            if (Team == Team.Green)
            {
                return Metadata.EmissionGreen;
            }
            return base.GetEmission(inst, material, index);
        }

        protected override Matrix4 GetTexcoordMatrix(ModelInstance inst, Material material, int materialId,
            Node node, int recolor)
        {
            if (_doubleDmgTimer > 0 && (Hunter != Hunter.Spire || !(inst == _gunModel && materialId == 0))
                && material.Lighting > 0 && node.BillboardMode == BillboardMode.None)
            {
                Texture texture = _doubleDmgModel.Model.Recolors[0].Textures[0];
                // product should start with the upper 3x3 of the node animation result,
                // texgenMatrix should be multiplied with the view matrix if lighting is enabled,
                // and the result should be transposed.
                // these steps are done in the shader so the view matrix can be updated when frame advance is on.
                // strictly speaking, the use_light check in the shader is not the same as what the game does,
                // since the game checks if *any* material in the model uses lighting, but the result is the same.
                Matrix4 texgenMatrix = Matrix4.Identity;
                // in-game, there's only one uniform scale factor for models
                if (inst.Model.Scale.X != 1 || inst.Model.Scale.Y != 1 || inst.Model.Scale.Z != 1)
                {
                    texgenMatrix = Matrix4.CreateScale(inst.Model.Scale) * texgenMatrix;
                }
                Matrix4 product = texgenMatrix;
                product.M12 *= -1;
                product.M13 *= -1;
                product.M22 *= -1;
                product.M23 *= -1;
                product.M32 *= -1;
                product.M33 *= -1;
                ulong frame = _scene.LiveFrames / 2;
                float rotZ = ((int)(16 * ((781874935307L * (53248 * frame) >> 32) + 2048)) >> 20) * (360 / 4096f);
                float rotY = ((int)(16 * ((781874935307L * (26624 * frame) + 0x80000000000) >> 32)) >> 20) * (360 / 4096f);
                var rot = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(rotZ));
                rot *= Matrix4.CreateRotationY(MathHelper.DegreesToRadians(rotY));
                product = rot * product;
                product *= 1.0f / (texture.Width / 2);
                product = new Matrix4(
                    product.Row0 * 16.0f,
                    product.Row1 * 16.0f,
                    product.Row2 * 16.0f,
                    product.Row3
                );
                return product;
            }
            return base.GetTexcoordMatrix(inst, material, materialId, node, recolor);
        }

        private void DrawShadow(Vector3 drawPosition)
        {
            if (IsMainPlayer && CameraType == CameraType.First && !_scene.ModThirdPersonCameraActive)
            {
                return;
            }
            Material material = _trailModel.Model.Materials[1];
            Vector3 presentationOffset = drawPosition - Position;
            Vector3 point1 = _volume.SpherePosition + presentationOffset;
            Vector3 point2 = point1.AddY(-10);
            CollisionResult colRes = default;
            if (CollisionDetection.CheckBetweenPoints(point1, point2, TestFlags.None, _scene, ref colRes)
                && colRes.Plane.Y >= Fixed.ToFloat(4))
            {
                float height = point1.Y - colRes.Position.Y;
                if (height < 10)
                {
                    float pct = 1 - height / 10;
                    float alpha = _curAlpha * pct;
                    if (_health == 0)
                    {
                        float respawnTime = _deathCountdown > 0 ? UInt16.MaxValue : RespawnTime;
                        float decrease = 2 * (respawnTime - _respawnTimer) / 2f; // todo: FPS stuff
                        alpha -= decrease;
                    }
                    if (alpha > 0)
                    {
                        Vector3 row1 = Vector3.Cross(colRes.Plane.Xyz, Vector3.UnitZ).Normalized();
                        Vector3 row2 = colRes.Plane.Xyz;
                        var row3 = Vector3.Cross(row1, colRes.Plane.Xyz);
                        row1 *= pct;
                        row2 *= pct;
                        row3 *= pct;
                        float factor = Fixed.ToFloat(100);
                        var row4 = new Vector3(
                            colRes.Position.X + colRes.Plane.X * factor,
                            colRes.Position.Y + colRes.Plane.Y * factor,
                            colRes.Position.Z + colRes.Plane.Z * factor
                        );
                        var transform = new Matrix4(
                            row1.X, row1.Y, row1.Z, 0,
                            row2.X, row2.Y, row2.Z, 0,
                            row3.X, row3.Y, row3.Z, 0,
                            row4.X, row4.Y, row4.Z, 1
                        );
                        Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(8);
                        uvsAndVerts[0] = new Vector3(0, 0, 0);
                        uvsAndVerts[1] = new Vector3(-0.75f, 0.03125f, -0.75f);
                        uvsAndVerts[2] = new Vector3(0, 1, 0);
                        uvsAndVerts[3] = new Vector3(-0.75f, 0.03125f, 0.75f);
                        uvsAndVerts[4] = new Vector3(1, 1, 0);
                        uvsAndVerts[5] = new Vector3(0.75f, 0.03125f, 0.75f);
                        uvsAndVerts[6] = new Vector3(1, 0, 0);
                        uvsAndVerts[7] = new Vector3(0.75f, 0.03125f, -0.75f);
                        int polygonId = _scene.GetNextPolygonId();
                        var color = new Vector3(0, 0, 0);
                        _scene.AddRenderItem(RenderItemType.Particle, alpha, polygonId, color, material.XRepeat, material.YRepeat,
                            material.ScaleS, material.ScaleT, transform, uvsAndVerts, _trailBindingId2);
                    }
                }
            }
        }

        private void DrawMorphBallTrail()
        {
            Debug.Assert(_trailModel != null);
            Material material = _trailModel.Model.Materials[0];
            Debug.Assert(_trailModel.Model.Recolors[0].Textures[material.TextureId].Width == 32);
            float[] matrixStack = ArrayPool<float>.Shared.Rent(16 * _mbTrailSegments);
            for (int i = 0; i < _mbTrailSegments; i++)
            {
                Matrix4 matrix = _mbTrailMatrices[SlotIndex, i];
                matrixStack[i * 16] = matrix.Row0.X;
                matrixStack[i * 16 + 1] = matrix.Row0.Y;
                matrixStack[i * 16 + 2] = matrix.Row0.Z;
                matrixStack[i * 16 + 3] = matrix.Row0.W;
                matrixStack[i * 16 + 4] = matrix.Row1.X;
                matrixStack[i * 16 + 5] = matrix.Row1.Y;
                matrixStack[i * 16 + 6] = matrix.Row1.Z;
                matrixStack[i * 16 + 7] = matrix.Row1.W;
                matrixStack[i * 16 + 8] = matrix.Row2.X;
                matrixStack[i * 16 + 9] = matrix.Row2.Y;
                matrixStack[i * 16 + 10] = matrix.Row2.Z;
                matrixStack[i * 16 + 11] = matrix.Row2.W;
                matrixStack[i * 16 + 12] = matrix.Row3.X;
                matrixStack[i * 16 + 13] = matrix.Row3.Y;
                matrixStack[i * 16 + 14] = matrix.Row3.Z;
                matrixStack[i * 16 + 15] = matrix.Row3.W;
            }
            int count = 0;
            int index = _mbTrailIndices[SlotIndex];
            Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(8 * _mbTrailSegments);
            for (int i = 0; i < _mbTrailSegments; i++)
            {
                // going backwards with wrap-around
                int mtxId1 = index - 1 - i + (index - 1 - i < 0 ? _mbTrailSegments : 0);
                int mtxId2 = mtxId1 - 1 + (mtxId1 - 1 < 0 ? _mbTrailSegments : 0);
                float alpha1 = _mbTrailAlphas[SlotIndex, mtxId1];
                float alpha2 = _mbTrailAlphas[SlotIndex, mtxId2];
                if (alpha1 > 0 && alpha2 > 0)
                {
                    float uvS1 = (31 - (int)(alpha1 * 31)) / 32f;
                    float uvS2 = (31 - (int)(alpha2 * 31)) / 32f;
                    uvsAndVerts[i * 8] = new Vector3(uvS1, 0, mtxId1);
                    uvsAndVerts[i * 8 + 1] = new Vector3(0, 0.375f, 0);
                    uvsAndVerts[i * 8 + 2] = new Vector3(uvS1, 1, mtxId1);
                    uvsAndVerts[i * 8 + 3] = new Vector3(0, -0.375f, 0);
                    uvsAndVerts[i * 8 + 4] = new Vector3(uvS2, 1, mtxId2);
                    uvsAndVerts[i * 8 + 5] = new Vector3(0, -0.375f, 0);
                    uvsAndVerts[i * 8 + 6] = new Vector3(uvS2, 0, mtxId2);
                    uvsAndVerts[i * 8 + 7] = new Vector3(0, 0.375f, 0);
                    count++;
                }
            }
            if (count > 0)
            {
                var color = new Vector3(1, 27 / 31f, 11 / 31f);
                _scene.AddRenderItem(RenderItemType.TrailStack, _scene.GetNextPolygonId(), color, material.XRepeat, material.YRepeat,
                    material.ScaleS, material.ScaleT, _mbTrailSegments, matrixStack, uvsAndVerts, count, _trailBindingId1);
            }
            ArrayPool<float>.Shared.Return(matrixStack);
        }

        // todo: FPS stuff
        private void DrawDeathParticles()
        {
            // get current percentage through the first 1/3 of the respawn cooldown
            float timePct = 1 - ((_respawnTimer - (2 / 3f * RespawnTime)) / (1 / 3f * RespawnTime));
            if (timePct < 0 || timePct > 1)
            {
                return;
            }
            float scale = timePct / 2 + 0.1f;
            // todo: the angle stuff could be removed
            float angle = MathF.Sin(MathHelper.DegreesToRadians(270 - 90 * timePct));
            float sin270 = MathF.Sin(MathHelper.DegreesToRadians(270));
            float sin180 = MathF.Sin(MathHelper.DegreesToRadians(180));
            float offset = (angle - sin270) / (sin180 - sin270);
            for (int i = 1; i < _bipedModel1.Model.Nodes.Count; i++)
            {
                Node node = _bipedModel1.Model.Nodes[i];
                var nodePos = new Vector3(node.Animation.Row3);
                nodePos.Y += offset;
                if (node.ChildIndex != -1)
                {
                    Debug.Assert(node.ChildIndex > 0);
                    var childPos = new Vector3(_bipedModel1.Model.Nodes[node.ChildIndex].Animation.Row3);
                    childPos.Y += offset;
                    for (int j = 1; j < 5; j++)
                    {
                        var segPos = new Vector3(
                            nodePos.X + j * (childPos.X - nodePos.X) / 5,
                            nodePos.Y + j * (childPos.Y - nodePos.Y) / 5,
                            nodePos.Z + j * (childPos.Z - nodePos.Z) / 5
                        );
                        segPos += (segPos - Position).Normalized() * offset;
                        _scene.AddSingleParticle(SingleType.Death, segPos, Vector3.One, 1 - timePct, scale);
                    }
                }
                if (node.NextIndex != -1)
                {
                    Debug.Assert(node.NextIndex > 0);
                    var nextPos = new Vector3(_bipedModel1.Model.Nodes[node.NextIndex].Animation.Row3);
                    nextPos.Y += offset;
                    for (int j = 1; j < 5; j++)
                    {
                        var segPos = new Vector3(
                            nodePos.X + j * (nextPos.X - nodePos.X) / 5,
                            nodePos.Y + j * (nextPos.Y - nodePos.Y) / 5,
                            nodePos.Z + j * (nextPos.Z - nodePos.Z) / 5
                        );
                        segPos += (segPos - Position).Normalized() * offset;
                        _scene.AddSingleParticle(SingleType.Death, segPos, Vector3.One, 1 - timePct, scale);
                    }
                }
                nodePos += (nodePos - Position).Normalized() * offset;
                _scene.AddSingleParticle(SingleType.Death, nodePos, Vector3.One, 1 - timePct, scale);
            }
        }

        private void DrawVolumes()
        {
            if (_scene.ShowVolumes == VolumeDisplay.KillPlane)
            {
                if (!IsAltForm && !IsMorphing && !IsUnmorphing)
                {
                    AddVectorItem(_gunDrawPos, _aimVec * 3, Vector3.UnitZ);
                    AddDotItem(_muzzlePos, Vector3.UnitX);
                    AddDotItem(_muzzlePos + (_aimPosition - _muzzlePos).Normalized() * 3, Vector3.UnitX);
                }
                AddVectorItem(_position, _facingVector * 3, Vector3.UnitY);
            }
        }

        public override void GetDrawInfo()
        {
        }
    }
}
