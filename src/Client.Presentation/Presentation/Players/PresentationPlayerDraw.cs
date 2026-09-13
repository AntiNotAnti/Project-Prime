using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Combat;
using MphRead.Formats;
using OpenTK.Mathematics;
using MphRead.Hud;
using MphRead.Cosmetics.Presentation;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        public void Draw()
        {
            BeginArmorSubmission();
            if (_player.Flags2.TestFlag(PlayerFlags2.Spectating))
            {
                // Before the shadow, not after it. A spectator is out of the
                // match on every machine, and the shadow is drawn from the
                // player's volume rather than from the model -- so hiding only
                // the model left a shadow sliding around the floor under
                // nobody, which is exactly how it was reported.
                return;
            }

            DrawShadow();
            if (_player.Flags2.TestFlag(PlayerFlags2.HideModel))
            {
                return;
            }

            int lod = 0;
            _player.Flags2 &= ~PlayerFlags2.Lod1;
            if (!_player.IsMainPlayer && !Features.MaxPlayerDetail && (_player.Position - _player._scene.LocalPlayer!.CameraInfo.Position).LengthSquared >= 3 * 3)
            {
                lod = 1;
                _player.Flags2 |= PlayerFlags2.Lod1;
            }

            _player._bipedModel1.SetModel(_player._bipedModelLods[lod].Model);
            _player._bipedModel2.SetModel(_player._bipedModelLods[lod].Model);
            // LOD selection can create a private clone after initial resource loading.
            ScenePresentation.NormalizeModelMaterials(_player._bipedModel2.Model);
            _player.Flags2 &= ~PlayerFlags2.DrawnThirdPerson;
            bool drawBiped = false;
            // todo: entity visibility check needs to do more than use active room parts
            // example issue - Kanden visible for one frame before Data Shrine 03 cam seq starts
            // should be culled because the cam seq frustum info has already been loaded, and is facing away from him,
            // even though the player's view is still what's on the screen (at least that seems to be what's happening)
            if (_player.IsMainPlayer || IsVisible(_player.NodeRef) || _player.ModNodeUnresolved)
            {
                drawBiped = !_player.IsMainPlayer || _player.CameraType != CameraType.First || _player._scene.CameraSequences.Current != null || _player._camSwitchTimer < _player.Values.CamSwitchTime * 2; // todo: FPS stuff
                if (_player.IsAltForm)
                {
                    _player._modelTransform.Row3.Xyz = _player.Position;
                    if (_player._timeSinceDamage < _player.Values.DamageFlashTime * 2) // todo: FPS stuff
                    {
                        _player.PaletteOverride = Metadata.RedPalette;
                    }

                    bool interpolatedAlt = Presentation.ResolvePlayerAltSubmission(
                        _player, _player._altModel, out Matrix4[] altNodes,
                        out float[] altStack);
                    if (interpolatedAlt)
                    {
                        UpdateMaterials(_player._altModel, _player.Recolor);
                        GetDrawItems(_player._altModel,
                            _player._altModel.Model.Nodes[0], _player._curAlpha,
                            nodePoses: altNodes, nodeStack: altStack);
                    }
                    else if (_player.Hunter == Hunter.Kanden)
                    {
                        DrawKandenAlt();
                    }
                    else if (_player.Hunter == Hunter.Spire && _player.Flags2.TestFlag(PlayerFlags2.AltAttack))
                    {
                        // DrawSpireAltAttack applies world translation to these
                        // nodes. Reevaluate the authored pose without changing
                        // simulation collision centers.
                        Matrix4 transform = PlayerEntity.GetTransformMatrix(
                            _player._spireAltFacing, _player._spireAltUp);
                        _player._altModel.Model.AnimateNodes(index: 0,
                            useNodeTransform: false, transform, Vector3.One,
                            _player._altModel.AnimInfo);
                        DrawSpireAltAttack();
                    }
                    else
                    {
                        UpdateTransforms(_player._altModel, _player._modelTransform, _player.Recolor);
                        GetDrawItems(_player._altModel, _player._altModel.Model.Nodes[0], _player._curAlpha);
                    }

                    SubmitArmorPrimitives(_player._altModel.Model,
                        _player._modelTransform,
                        interpolatedAlt ? altNodes : null);

                    _player.PaletteOverride = null;
                    if (_player._frozenGfxTimer > 0)
                    {
                        float radius = _player._volume.SphereRadius + 0.2f;
                        Matrix4 transform = Matrix4.CreateScale(radius) * _player._modelTransform;
                        transform.Row3.Y += Fixed.ToFloat(_player.Values.AltColYPos);
                        UpdateTransforms(_player._altIceModel, transform, recolor: 0);
                        GetDrawItems(_player._altIceModel, _player._altIceModel.Model.Nodes[0],
                            alpha: 1, recolor: 0, castsDirectionalShadow: false);
                    }

                    if (_player.Hunter == Hunter.Samus && !_player.Flags2.TestFlag(PlayerFlags2.Cloaking))
                    {
                        DrawMorphBallTrail();
                    }

                    _player._modelTransform.Row3.Xyz = Vector3.Zero;
                    _player.Flags2 |= PlayerFlags2.DrawnThirdPerson;
                }
                else if (drawBiped)
                {
                    // we want to animate first up to the spine with _bipedModel1's animation info, then the rest with _bipedModel2's info
                    Vector3 facing = _player._facingVector;
                    float angle = PlayerEntity.GetBipedPitch(facing);
                    Model model = _player._bipedModel2.Model;
                    float scale = Metadata.HunterScales[_player.Hunter];
                    float bottom = Fixed.ToFloat(_player.Values.MinPickupHeight);
                    var lateral = new Vector3(_player._field70, 0, _player._field74);
                    Matrix4 transform = Matrix4.Identity;
                    transform.Row0.Xyz = -_player._gunVec2;
                    transform.Row1.Xyz = Vector3.Cross(lateral, _player._gunVec2);
                    transform.Row2.Xyz = -lateral;
                    transform.Row3.Xyz = _player.Position;
                    transform.Row3.Y += bottom + bottom * (1 - scale);
                    transform.Row0.Xyz *= scale;
                    transform.Row1.Xyz *= scale;
                    transform.Row2.Xyz *= scale;
                    bool interpolated = Presentation.ResolvePlayerBipedSubmission(_player, _player._bipedModel2,
                        transform, out Matrix4[] renderedNodes, out float[] renderedStack);
                    if (!interpolated)
                    {
                        // Fallback is the original authored draw path.  A
                        // history may not exist yet (first sample or a LOD
                        // switch), and rendering must remain independent of it.
                        PlayerEntity.AnimateBipedPose(model, _player._bipedModel1.AnimInfo,
                            _player._bipedModel2.AnimInfo, angle);
                        for (int i = 0; i < model.Nodes.Count; i++)
                        {
                            Node node = model.Nodes[i];
                            node.Animation *= transform; // todo?: could do this in the shader
                        }
                        model.UpdateMatrixStack();
                    }
                    if (_player._health > 0)
                    {
                        if (_player._timeSinceDamage < _player.Values.DamageFlashTime * 2) // todo: FPS stuff
                        {
                            _player.PaletteOverride = Metadata.RedPalette;
                        }

                        float alpha = _player._curAlpha;
                        if (_player.IsMainPlayer && _player._scene.CameraSequences.Current == null && _player._bipedModel1.AnimInfo.Index[0] == (int)PlayerAnimation.Unmorph)
                        {
                            alpha -= alpha * _player._bipedModel1.AnimInfo.Frame[0] / _player._bipedModel1.AnimInfo.FrameCount[0];
                            alpha = Math.Clamp(alpha, 0, 1);
                        }

                        UpdateMaterials(_player._bipedModel2, _player.Recolor);
                        GetDrawItems(_player._bipedModel2, _player._bipedModel2.Model.Nodes[0], alpha,
                            nodePoses: interpolated ? renderedNodes : null,
                            nodeStack: interpolated ? renderedStack : null);
                        SubmitArmorPrimitives(model, transform,
                            interpolated ? renderedNodes : null);
                        CaptureAliveDeathPose(model, transform,
                            interpolated ? renderedNodes : null,
                            interpolated ? renderedStack : null, alpha);
                        _player.PaletteOverride = null;
                        if (_player._chargeEffect != null || _player._muzzleEffect != null)
                        {
                            Vector3 muzzlePos = Metadata.MuzzleOffests[(int)_player.Hunter];
                            int muzzleNode = model.GetNodeIndexByName(_player.Hunter == Hunter.Guardian ? "Head_1" : "R_elbow");
                            Matrix4 muzzleTransform = interpolated ? renderedNodes[muzzleNode]
                                : model.Nodes[muzzleNode].Animation;
                            muzzlePos = Matrix.Vec3MultMtx4(muzzlePos, muzzleTransform);
                            if (_player._chargeEffect != null)
                            {
                                _player._chargeEffect.SetDrawEnabled(true);
                                _player._chargeEffect.Transform(_player._gunVec2, _player._gunVec1, muzzlePos);
                            }

                            if (_player._muzzleEffect != null)
                            {
                                _player._muzzleEffect.SetDrawEnabled(true);
                                _player._muzzleEffect.Transform(_player._gunVec2, _player._gunVec1, muzzlePos);
                            }
                        }

                        if (_player._frozenGfxTimer > 0)
                        {
                            for (int i = 0; i < _player._bipedIceModel.Model.Nodes.Count; i++)
                            {
                                Matrix4 pose = interpolated && i < renderedNodes.Length
                                    ? renderedNodes[i] : _player._bipedModel2.Model.Nodes[i].Animation;
                                _player._bipedIceModel.Model.Nodes[i].Animation = pose;
                                _player._bipedIceTransforms[i] = pose;
                            }

                            _player._bipedIceModel.Model.UpdateMatrixStack();
                            UpdateMaterials(_player._bipedIceModel, recolor: 0);
                            GetDrawItems(_player._bipedIceModel,
                                _player._bipedIceModel.Model.Nodes[0], alpha: 1,
                                recolor: 0, castsDirectionalShadow: false);
                        }
                    }

                    _player._modelTransform = transform;
                    if (_player._health == 0)
                    {
                        if (TryGetDeathPresentation(_player._scene.Services.WorldServerTick,
                            out DeathPresentationSample death) && CapturedDeathModel is Model deathModel)
                        {
                            _player._bipedModel2.SetModel(deathModel);
                            _deathEmissionTint = death.EmissionTint;
                            _deathEmissionStrength = death.EmissionStrength;
                            UpdateMaterials(_player._bipedModel2, _capturedDeathPose.Appearance.Recolor);
                            GetDrawItems(_player._bipedModel2, deathModel.Nodes[0],
                                death.BodyAlpha * _capturedDeathPose.Appearance.Alpha,
                                recolor: _capturedDeathPose.Appearance.Recolor,
                                nodePoses: death.Nodes, nodeStack: death.MatrixStack);
                            SubmitDeathPrimitives();
                            _deathEmissionStrength = 0;
                        }
                        else
                        {
                            DrawDeathParticles(interpolated ? renderedNodes : null);
                        }
                    }

                    _player.Flags2 |= PlayerFlags2.DrawnThirdPerson;
                }
                else
                {
                    // A normal local first-person frame does not submit the
                    // biped, but a later authoritative death still needs the
                    // last full-body presentation pose. Keep that retained
                    // copy current without exposing the model to rendering.
                    if (_player._health > 0)
                        CaptureHiddenAliveDeathPose();

                    if (!_player._field6D0 && _player.Hunter != Hunter.Guardian)
                    {
                        Matrix4 transform = PlayerEntity.GetTransformMatrix(_player._aimVec, _player._upVector, _player._gunDrawPos);
                        UpdateTransforms(_player._gunModel, transform, _player.Recolor);
                        // Interpolate authored animation and bob in camera-local
                        // space, then attach it to this frame's resolved camera.
                        // The fallback retains the current simulation pose and
                        // generic camera delta at discontinuity boundaries.
                        if (Presentation.ResolvePlayerGunSubmission(_player,
                            _player._gunModel, out Matrix4[] gunNodes,
                            out float[] gunStack))
                        {
                            GetDrawItems(_player._gunModel,
                                _player._gunModel.Model.Nodes[0],
                                _player._curAlpha, nodePoses: gunNodes,
                                nodeStack: gunStack);
                        }
                        else
                        {
                            GetDrawItems(_player._gunModel,
                                _player._gunModel.Model.Nodes[0],
                                _player._curAlpha);
                        }
                        if (_player.Flags1.TestFlag(PlayerFlags1.DrawGunSmoke))
                        {
                            // todo?: the game uses an alternate projection matrix to draw this
                            var drawPos = new Vector3(0, 0, Fixed.ToFloat(_player.Values.MuzzleOffset));
                            drawPos = Matrix.Vec3MultMtx4(drawPos, transform);
                            transform.Row3.Xyz = drawPos;
                            UpdateTransforms(_player._gunSmokeModel, transform, recolor: 0);
                            GetDrawItems(_player._gunSmokeModel, _player._gunSmokeModel.Model.Nodes[0], _player._smokeAlpha, recolor: 0);
                        }
                    }
                }
            }

            if (!_player.IsMainPlayer && !drawBiped)
            {
                if (_player._chargeEffect != null)
                {
                    _player._chargeEffect.SetDrawEnabled(false);
                }

                if (_player._muzzleEffect != null)
                {
                    _player._muzzleEffect.SetDrawEnabled(false);
                }
            }

            DrawVolumes();
        }

        public void DrawKandenAlt()
        {
            for (int i = 0; i < _player._kandenSegMtx.Length; i++)
            {
                _player._altModel.Model.Nodes[i].Animation = _player._kandenSegMtx[i];
            }

            _player._altModel.Model.UpdateMatrixStack();
            UpdateMaterials(_player._altModel, _player.Recolor);
            GetDrawItems(_player._altModel, _player._altModel.Model.Nodes[0], _player._curAlpha);
        }

        public void DrawSpireAltAttack()
        {
            _player._altModel.Model.Nodes[0].Animation = _player._modelTransform;
            for (int i = 1; i < _player._altModel.Model.Nodes.Count; i++)
            {
                Node node = _player._altModel.Model.Nodes[i];
                Matrix4 animation = node.Animation;
                animation.Row3.Xyz += _player._modelTransform.Row3.Xyz;
                node.Animation = animation;
            }

            _player._altModel.Model.UpdateMatrixStack();
            UpdateMaterials(_player._altModel, _player.Recolor);
            GetDrawItems(_player._altModel, _player._altModel.Model.Nodes[0], _player._curAlpha);
        }

        public void GetDrawItems(ModelInstance inst, Node node, float alpha, int polygonId = -1,
            int recolor = -1, Matrix4[]? nodePoses = null, float[]? nodeStack = null,
            bool castsDirectionalShadow = true)
        {
            if (alpha <= 0)
            {
                return;
            }

            if (polygonId == -1)
            {
                polygonId = Presentation.GetNextPolygonId();
            }

            Model model = inst.Model;
            int nodeIndex = 0;
            while (nodeIndex < model.Nodes.Count && !ReferenceEquals(model.Nodes[nodeIndex], node))
            {
                nodeIndex++;
            }
            if (nodeIndex == model.Nodes.Count)
            {
                return;
            }
            GetDrawItems(inst, nodeIndex, alpha, polygonId, recolor, nodePoses,
                nodeStack, castsDirectionalShadow);
        }

        private void GetDrawItems(ModelInstance inst, int nodeIndex, float alpha,
            int polygonId, int recolor, Matrix4[]? nodePoses, float[]? nodeStack,
            bool castsDirectionalShadow)
        {
            Model model = inst.Model;
            Node node = model.Nodes[nodeIndex];
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
                    Vector4? color = null;
                    SelectionType selectionType = SelectionType.None;
                    int? bindingOverride = GetBindingOverride(inst, material, mesh.MaterialId);
                    int canonicalRecolor = recolor == -1 ? _player.Recolor : recolor;
                    GameplayMaterialFeedback materialFeedback = GameplayMaterialFeedback.None;
                    if (_player.PaletteOverride != null) materialFeedback |= GameplayMaterialFeedback.DamageFlash;
                    if (_player._frozenGfxTimer > 0) materialFeedback |= GameplayMaterialFeedback.Frozen;
                    if (_player.Flags2.TestFlag(PlayerFlags2.Cloaking)) materialFeedback |= GameplayMaterialFeedback.Cloaked;
                    if (_player._doubleDmgTimer > 0) materialFeedback |= GameplayMaterialFeedback.DoubleDamage;
                    if (selectionType != SelectionType.None) materialFeedback |= GameplayMaterialFeedback.Selection;
                    bool teamMode = _player._scene.Match.Rules.Teams
                        && _player.Team != Team.None;
                    int initialRecolor = teamMode ? canonicalRecolor
                        : Skin.ResolveRecolor(canonicalRecolor);
                    TextureAssetKey? textureAssetKey = ScenePresentation.GetModelTextureAssetKey(
                        model, material, initialRecolor);
                    SkinAppearanceResolution skin = ResolveSkinAppearance(
                        canonicalRecolor, textureAssetKey, materialFeedback);
                    int resolvedRecolor = skin.Recolor;
                    TextureIdentity? textureIdentity = GetTextureIdentity(inst, material,
                        mesh.MaterialId, resolvedRecolor);
                    Matrix4 nodeAnimation = nodePoses == null ? node.Animation : nodePoses[nodeIndex];
                    IReadOnlyList<float> stack = nodeStack == null ? model.MatrixStackValues : nodeStack;
                    Presentation.AddRenderItem(material, polygonId, alpha, emission, GetLightInfo(), texcoordMatrix, nodeAnimation, Presentation.GetMeshListId(mesh), mesh.GeometryIdentity, model.NodeMatrixIds.Count, stack, color, _player.PaletteOverride, selectionType, node.BillboardMode, _player._drawScale, bindingOverride, textureIdentity,
                        textureAssetKey: textureAssetKey,
                        enhancedForceField: TakeDeathDistortion(inst) ?? TakeArmorDistortion(inst),
                        castsDirectionalShadow: castsDirectionalShadow,
                        cosmeticMaterialOverride: ResolveDeathMaterial(
                            ResolveCosmeticMaterial(skin, materialFeedback)),
                        gameplayMaterialFeedback: materialFeedback);
                }

                if (node.ChildIndex != -1)
                {
                    GetDrawItems(inst, node.ChildIndex, alpha, polygonId, recolor,
                        nodePoses, nodeStack, castsDirectionalShadow);
                }
            }

            if (node.NextIndex != -1)
            {
                GetDrawItems(inst, node.NextIndex, alpha, polygonId, recolor,
                    nodePoses, nodeStack, castsDirectionalShadow);
            }
        }

        protected override int? GetBindingOverride(ModelInstance inst, Material material, int index)
        {
            if (UsesDoubleDamageTexture(inst, material, index))
            {
                return _doubleDmgBindingId;
            }

            return base.GetBindingOverride(inst, material, index);
        }

        protected override TextureIdentity? GetTextureIdentity(ModelInstance inst, Material material, int index, int recolor)
        {
            if (UsesDoubleDamageTexture(inst, material, index))
            {
                Model model = _player._doubleDmgModel.Model;
                return Presentation.GetTextureIdentity(model, model.Materials[0], 0);
            }

            return base.GetTextureIdentity(inst, material, index, recolor);
        }

        private bool UsesDoubleDamageTexture(ModelInstance inst, Material material, int index)
            => _player._doubleDmgTimer > 0
                && (_player.Hunter != Hunter.Spire || !(inst == _player._gunModel && index == 0))
                && material.Lighting > 0;

        protected override Vector3 GetEmission(ModelInstance inst, Material material, int index)
        {
            if (_player._doubleDmgTimer > 0 && (_player.Hunter != Hunter.Spire || !(inst == _player._gunModel && index == 0)) && material.Lighting > 0)
            {
                return Metadata.EmissionGray;
            }

            if (_player.Team == Team.Orange)
            {
                return Metadata.EmissionOrange;
            }

            if (_player.Team == Team.Green)
            {
                return Metadata.EmissionGreen;
            }

            Vector3 emission = base.GetEmission(inst, material, index);
            return _deathEmissionStrength > 0
                ? emission + _deathEmissionTint * _deathEmissionStrength : emission;
        }

        private Vector3 _deathEmissionTint;
        private float _deathEmissionStrength;

        private void CaptureHiddenAliveDeathPose()
        {
            Vector3 facing = _player._facingVector;
            float angle = PlayerEntity.GetBipedPitch(facing);
            Model model = _player._bipedModel2.Model;
            float scale = Metadata.HunterScales[_player.Hunter];
            float bottom = Fixed.ToFloat(_player.Values.MinPickupHeight);
            var lateral = new Vector3(_player._field70, 0, _player._field74);
            Matrix4 transform = Matrix4.Identity;
            transform.Row0.Xyz = -_player._gunVec2;
            transform.Row1.Xyz = Vector3.Cross(lateral, _player._gunVec2);
            transform.Row2.Xyz = -lateral;
            transform.Row3.Xyz = _player.Position;
            transform.Row3.Y += bottom + bottom * (1 - scale);
            transform.Row0.Xyz *= scale;
            transform.Row1.Xyz *= scale;
            transform.Row2.Xyz *= scale;

            bool interpolated = Presentation.ResolvePlayerBipedSubmission(
                _player, _player._bipedModel2, transform,
                out Matrix4[] renderedNodes, out float[] renderedStack);
            if (!interpolated)
            {
                PlayerEntity.AnimateBipedPose(model,
                    _player._bipedModel1.AnimInfo,
                    _player._bipedModel2.AnimInfo, angle);
                for (int i = 0; i < model.Nodes.Count; i++)
                    model.Nodes[i].Animation *= transform;
                model.UpdateMatrixStack();
            }
            CaptureAliveDeathPose(model, transform,
                interpolated ? renderedNodes : null,
                interpolated ? renderedStack : null, _player._curAlpha);
        }

        protected override Matrix4 GetTexcoordMatrix(ModelInstance inst, Material material, int materialId, Node node, int recolor)
        {
            if (_player._doubleDmgTimer > 0 && (_player.Hunter != Hunter.Spire || !(inst == _player._gunModel && materialId == 0)) && material.Lighting > 0 && node.BillboardMode == BillboardMode.None)
            {
                Texture texture = _player._doubleDmgModel.Model.Recolors[0].Textures[0];
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
                ulong frame = _player._scene.LiveFrames / 2;
                float rotZ = ((int)(16 * ((781874935307L * (53248 * frame) >> 32) + 2048)) >> 20) * (360 / 4096f);
                float rotY = ((int)(16 * ((781874935307L * (26624 * frame) + 0x80000000000) >> 32)) >> 20) * (360 / 4096f);
                var rot = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(rotZ));
                rot *= Matrix4.CreateRotationY(MathHelper.DegreesToRadians(rotY));
                product = rot * product;
                product *= 1.0f / (texture.Width / 2);
                product = new Matrix4(product.Row0 * 16.0f, product.Row1 * 16.0f, product.Row2 * 16.0f, product.Row3);
                return product;
            }

            return base.GetTexcoordMatrix(inst, material, materialId, node, recolor);
        }

        public void DrawShadow()
        {
            if (_player.IsMainPlayer && _player.CameraType == CameraType.First)
            {
                return;
            }

            Material material = _player._trailModel.Model.Materials[1];
            Vector3 point1 = _player._volume.SpherePosition;
            Vector3 point2 = _player._volume.SpherePosition.AddY(-10);
            CollisionResult colRes = default;
            if (CollisionDetection.CheckBetweenPoints(point1, point2, TestFlags.None, _player._scene, ref colRes) && colRes.Plane.Y >= Fixed.ToFloat(4))
            {
                float height = point1.Y - colRes.Position.Y;
                if (height < 10)
                {
                    float pct = 1 - height / 10;
                    float alpha = _player._curAlpha * pct;
                    if (_player._health == 0)
                    {
                        float respawnTime = PlayerEntity.RespawnTime;
                        float decrease = 2 * (respawnTime - _player._respawnTimer) / 2f; // todo: FPS stuff
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
                        var row4 = new Vector3(colRes.Position.X + colRes.Plane.X * factor, colRes.Position.Y + colRes.Plane.Y * factor, colRes.Position.Z + colRes.Plane.Z * factor);
                        var transform = new Matrix4(row1.X, row1.Y, row1.Z, 0, row2.X, row2.Y, row2.Z, 0, row3.X, row3.Y, row3.Z, 0, row4.X, row4.Y, row4.Z, 1);
                        Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(8);
                        uvsAndVerts[0] = new Vector3(0, 0, 0);
                        uvsAndVerts[1] = new Vector3(-0.75f, 0.03125f, -0.75f);
                        uvsAndVerts[2] = new Vector3(0, 1, 0);
                        uvsAndVerts[3] = new Vector3(-0.75f, 0.03125f, 0.75f);
                        uvsAndVerts[4] = new Vector3(1, 1, 0);
                        uvsAndVerts[5] = new Vector3(0.75f, 0.03125f, 0.75f);
                        uvsAndVerts[6] = new Vector3(1, 0, 0);
                        uvsAndVerts[7] = new Vector3(0.75f, 0.03125f, -0.75f);
                        int polygonId = Presentation.GetNextPolygonId();
                        var color = new Vector3(0, 0, 0);
                        Presentation.AddRenderItem(RenderPrimitive.Particle, alpha, polygonId, color, material.XRepeat, material.YRepeat, material.ScaleS, material.ScaleT, transform, uvsAndVerts, Presentation.GetTextureIdentity(_player._trailModel.Model, material, 0), _trailBindingId2);
                    }
                }
            }
        }

        public void DrawMorphBallTrail()
        {
            Debug.Assert(_player._trailModel != null);
            Material material = _player._trailModel.Model.Materials[0];
            Debug.Assert(_player._trailModel.Model.Recolors[0].Textures[material.TextureId].Width == 32);
            float[] matrixStack = ArrayPool<float>.Shared.Rent(16 * PlayerEntity._mbTrailSegments);
            for (int i = 0; i < PlayerEntity._mbTrailSegments; i++)
            {
                Matrix4 matrix = _player._mbTrailMatrices[i];
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
            int index = _player._mbTrailIndex;
            Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(8 * PlayerEntity._mbTrailSegments);
            for (int i = 0; i < PlayerEntity._mbTrailSegments; i++)
            {
                // going backwards with wrap-around
                int mtxId1 = index - 1 - i + (index - 1 - i < 0 ? PlayerEntity._mbTrailSegments : 0);
                int mtxId2 = mtxId1 - 1 + (mtxId1 - 1 < 0 ? PlayerEntity._mbTrailSegments : 0);
                float alpha1 = _player._mbTrailAlphas[mtxId1];
                float alpha2 = _player._mbTrailAlphas[mtxId2];
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
                Presentation.AddRenderItem(RenderPrimitive.TrailStack, Presentation.GetNextPolygonId(), color, material.XRepeat, material.YRepeat, material.ScaleS, material.ScaleT, PlayerEntity._mbTrailSegments, matrixStack, uvsAndVerts, count, Presentation.GetTextureIdentity(_player._trailModel.Model, material, 0), _trailBindingId1);
            }

            ArrayPool<float>.Shared.Return(matrixStack);
        }

        public void DrawDeathParticles(Matrix4[]? nodePoses = null)
        {
            // get current percentage through the first 1/3 of the respawn cooldown
            float timePct;
            if (!TryGetNetworkDeathParticleTime(_player._scene.Services.WorldServerTick, out timePct))
            {
                timePct = 1 - ((_player._respawnTimer - (2 / 3f * PlayerEntity.RespawnTime)) / (1 / 3f * PlayerEntity.RespawnTime));
                if (timePct < 0 || timePct > 1)
                    return;
            }

            float scale = timePct / 2 + 0.1f;
            // todo: the angle stuff could be removed
            float angle = MathF.Sin(MathHelper.DegreesToRadians(270 - 90 * timePct));
            float sin270 = MathF.Sin(MathHelper.DegreesToRadians(270));
            float sin180 = MathF.Sin(MathHelper.DegreesToRadians(180));
            float offset = (angle - sin270) / (sin180 - sin270);
            for (int i = 1; i < _player._bipedModel2.Model.Nodes.Count; i++)
            {
                Node node = _player._bipedModel2.Model.Nodes[i];
                Matrix4 nodeAnimation = nodePoses == null ? node.Animation : nodePoses[i];
                var nodePos = new Vector3(nodeAnimation.Row3);
                nodePos.Y += offset;
                if (node.ChildIndex != -1)
                {
                    Debug.Assert(node.ChildIndex > 0);
                    Matrix4 childAnimation = nodePoses == null ? _player._bipedModel2.Model.Nodes[node.ChildIndex].Animation
                        : nodePoses[node.ChildIndex];
                    var childPos = new Vector3(childAnimation.Row3);
                    childPos.Y += offset;
                    for (int j = 1; j < 5; j++)
                    {
                        var segPos = new Vector3(nodePos.X + j * (childPos.X - nodePos.X) / 5, nodePos.Y + j * (childPos.Y - nodePos.Y) / 5, nodePos.Z + j * (childPos.Z - nodePos.Z) / 5);
                        segPos += (segPos - _player.Position).Normalized() * offset;
                        Presentation.AddSingleParticle(SingleType.Death, segPos, Vector3.One, 1 - timePct, scale);
                    }
                }

                if (node.NextIndex != -1)
                {
                    Debug.Assert(node.NextIndex > 0);
                    Matrix4 nextAnimation = nodePoses == null ? _player._bipedModel2.Model.Nodes[node.NextIndex].Animation
                        : nodePoses[node.NextIndex];
                    var nextPos = new Vector3(nextAnimation.Row3);
                    nextPos.Y += offset;
                    for (int j = 1; j < 5; j++)
                    {
                        var segPos = new Vector3(nodePos.X + j * (nextPos.X - nodePos.X) / 5, nodePos.Y + j * (nextPos.Y - nodePos.Y) / 5, nodePos.Z + j * (nextPos.Z - nodePos.Z) / 5);
                        segPos += (segPos - _player.Position).Normalized() * offset;
                        Presentation.AddSingleParticle(SingleType.Death, segPos, Vector3.One, 1 - timePct, scale);
                    }
                }

                nodePos += (nodePos - _player.Position).Normalized() * offset;
                Presentation.AddSingleParticle(SingleType.Death, nodePos, Vector3.One, 1 - timePct, scale);
            }
        }

        public void DrawVolumes()
        {
            if (Presentation.ShowVolumes == VolumeDisplay.KillPlane)
            {
                if (!_player.IsAltForm && !_player.IsMorphing && !_player.IsUnmorphing)
                {
                    AddVectorItem(_player._gunDrawPos, _player._aimVec * 3, Vector3.UnitZ);
                    AddDotItem(_player._muzzlePos, Vector3.UnitX);
                    AddDotItem(_player._muzzlePos + (_player._aimPosition - _player._muzzlePos).Normalized() * 3, Vector3.UnitX);
                }

                AddVectorItem(_player._position, _player._facingVector * 3, Vector3.UnitY);
            }
        }

        public override void GetDrawInfo()
        {
        }
    }
}
