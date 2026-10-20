using System;
using MphRead.Mods;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Cosmetics.Death;
using MphRead.Mods.Network;
using MphRead.Mods.Render.Characters;
using OpenTK.Mathematics;
namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        private readonly DeathPresentationState _cosmeticDeath = new();
        internal DeathPresentationState CosmeticDeathState => _cosmeticDeath;
        private uint CosmeticSeed => _scene.Services is ReplaySceneServices replay
            ? CosmeticRuntime.Seed(replay.State.Match?.MatchId ?? 0, SlotIndex, replay.State.Occupant(SlotIndex).Generation)
            : CosmeticRuntime.Seed(NetSession.Active ? NetSession.CurrentMatchId : (ushort)0,
                SlotIndex, NetSession.Active ? NetPlayerLifecycle.Generation(SlotIndex) : (ushort)1);
        private Mods.Cosmetics.Death.DeathPresentationDefinition CosmeticDeathDefinition
        {
            get
            {
                if (_cosmeticDeath.Active)
                    for (int i = 0; i < CosmeticCatalog.DeathPresentations.Count; i++)
                        if (CosmeticCatalog.DeathPresentations[i].Key == _cosmeticDeath.Key)
                            return CosmeticCatalog.DeathPresentations[i];
                return CosmeticAppearance.Death;
            }
        }
        private CosmeticAppearance CosmeticAppearance => CosmeticRuntime.Get(_scene, SlotIndex, Hunter, IsMainPlayer);
        internal void ModCosmeticObserveAuthority(int health, ushort life, ushort generation)
        {
            if (_scene == null || _cosmeticDeath == null) return;
            _cosmeticDeath.Observe(health, life, generation, _scene.ElapsedTime, CosmeticAppearance,
                Position, FacingVector, IsAltForm, CosmeticSeed);
        }
        internal bool CosmeticDeathLight(out Vector3 color, out float strength)
        {
            var definition = CosmeticDeathDefinition;
            color = definition.LightEffect;
            strength = 0.6f * (1 - _cosmeticDeath.Progress(_scene.ElapsedTime, definition));
            return DeathPresentationRuntime.Visible(_cosmeticDeath, definition, _scene.ElapsedTime);
        }
        internal CosmeticSurface CosmeticMaterial(bool firstPerson)
        {
            if (firstPerson && _ownerGunDrawActive) return _ownerVisual.Surface;
            bool suppressed = Mods.Render.BrightSkins.ShouldApply(this) || BrightSkinStatusOverride || BrightSkinFrozenOverlay || ModMatchSpawnProtectionActive
                || (Flags2.TestFlag(PlayerFlags2.Cloaking) || _curAlpha < 1) || _timeSinceDamage < Values.DamageFlashTime * 2;
            float distance = IsMainPlayer ? 0 : (Position - _scene.Players.Main.CameraInfo.Position).Length;
            return CosmeticRuntime.Surface(CosmeticAppearance, _scene.ElapsedTime, suppressed, firstPerson,
                IsAltForm, distance, _scene.GameState.Teams);
        }
        private void DrawCosmeticArmor(Model model, bool alt)
        {
            if (Health <= 0 || !RenderOptions.ShowCustomCosmetics || CosmeticMaterial(false).Effect == 0) return;
            float distance = (Position - _scene.Players.Main.CameraInfo.Position).Length;
            Mods.Cosmetics.Armor.ArmorEffectParticles.Draw(_scene, model, Position, CosmeticAppearance.Armor,
                _scene.ElapsedTime, CosmeticSeed, CosmeticRuntime.Lod(distance), alt, Hunter);
        }
        private void DrawHeadDeathBody(ModelInstance body, Mods.Render.HeadDeathPose pose, bool cosmeticDeath)
        {
            UpdateMaterials(body, Recolor);
            var previous = _scene.CosmeticSubmission;
            var previousMaterial = _scene.CosmeticMaterialSubmission;
            try
            {
                using var scoped = Mods.Render.HeadDeathPoseOverlay.Begin(body.Model, pose);
                if (!TryDrawModernCharacter(body, CharacterModelPart.Biped, pose.Active ? pose.Alpha : 1,
                    lod: Flags2.TestFlag(PlayerFlags2.Lod1) ? 1 : 0, cosmeticDeath: cosmeticDeath))
                    GetDrawItems(body, body.Model.Nodes[0], pose.Active ? pose.Alpha : 1, cosmeticDeath: cosmeticDeath);
            }
            finally { _scene.CosmeticSubmission = previous; _scene.CosmeticMaterialSubmission = previousMaterial; }
        }
        private bool DrawCosmeticDeath(ModelInstance? body = null)
        {
            var appearance = CosmeticAppearance;
            var definition = CosmeticDeathDefinition;
            var headPose = _scene.ModHeadDeathPose(this, definition);
            if (!DeathPresentationRuntime.Visible(_cosmeticDeath, definition, _scene.ElapsedTime))
            {
                if (definition.WireId == 0 && ModClassicHeadDeathVisible)
                    DrawHeadDeathBody(body ?? _bipedModel2, headPose, cosmeticDeath: true);
                // Classic particles retain their original call/clock even while
                // a short accepted headshot body is visible. No extra audio.
                return _cosmeticDeath.Active && definition.WireId != 0 && RenderOptions.ShowCustomCosmetics
                    && RenderOptions.CosmeticQuality != CosmeticEffectQuality.Off;
            }
            float progress = _cosmeticDeath.Progress(_scene.ElapsedTime, definition);
            if (progress < definition.HideBodyAt)
            {
                body ??= _bipedModel2;
                UpdateMaterials(body, Recolor);
                var previous = _scene.CosmeticSubmission;
                _scene.CosmeticSubmission = DeathPresentationRuntime.Surface(appearance, definition,
                    _scene.ElapsedTime, progress, _scene.GameState.Teams);
                try
                {
                    // Keep one biped model route throughout the catalog effect,
                    // including after the short head reaction settles. Otherwise
                    // a replacement corpse would pop back to native at tick42.
                    if (!ReferenceEquals(body, _altModel)) DrawHeadDeathBody(body, headPose, cosmeticDeath: true);
                    else GetDrawItems(body, body.Model.Nodes[0], 1, cosmeticDeath: true);
                }
                finally { _scene.CosmeticSubmission = previous; }
            }
            Mods.Cosmetics.Armor.ArmorEffectParticles.DrawDeath(_scene, _cosmeticDeath.Position, definition, progress, _cosmeticDeath.Seed);
            return true;
        }
    }
}
