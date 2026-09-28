using System;
using MphRead.Mods;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Cosmetics.Death;
using MphRead.Mods.Network;
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
        private bool DrawCosmeticDeath(ModelInstance? body = null)
        {
            var appearance = CosmeticAppearance;
            var definition = CosmeticDeathDefinition;
            if (!DeathPresentationRuntime.Visible(_cosmeticDeath, definition, _scene.ElapsedTime))
                return _cosmeticDeath.Active && definition.WireId != 0 && RenderOptions.ShowCustomCosmetics
                    && RenderOptions.CosmeticQuality != CosmeticEffectQuality.Off;
            float progress = _cosmeticDeath.Progress(_scene.ElapsedTime, definition);
            if (progress < definition.HideBodyAt)
            {
                body ??= _bipedModel2;
                UpdateMaterials(body, Recolor);
                var previous = _scene.CosmeticSubmission;
                _scene.CosmeticSubmission = DeathPresentationRuntime.Surface(appearance, definition,
                    _scene.ElapsedTime, progress, _scene.GameState.Teams);
                try { GetDrawItems(body, body.Model.Nodes[0], 1, cosmeticDeath: true); }
                finally { _scene.CosmeticSubmission = previous; }
            }
            Mods.Cosmetics.Armor.ArmorEffectParticles.DrawDeath(_scene, _cosmeticDeath.Position, definition, progress, _cosmeticDeath.Seed);
            return true;
        }
    }
}
