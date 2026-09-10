using MphRead.Formats;
using MphRead.Sound;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public interface IPlayerPresentation
    {
        void LoadPresentationModels();
        void StartFlagCarrySfx();
        void StopFlagCarrySfx();
        void StopAllSfx();
        void PlayPickupSound(SfxId id);
        void PresentMajorPickup(ItemType itemType);
        void SetDoorChimeTimer(float value);
        void SetDoorUnlockTimer(float value);
        void SetForceFieldSoundTimer(float value);
        void ResetPresentationLighting();
        void ShowDeathFade();
        void ResetWalkingSound();
        void StopContinuousBeamSfx(BeamType beam);
        void RestartLongSfx(bool force = false);
        void RestartTimedSfx(bool force = false);
        void StopLongSfx();
        void StopTimedSfx();
        void ShowNodeStolen();
        void ShowObjectiveMessage(int messageId, float duration);
        void ShowMissingOctolith();
        void GunAnimationPresentation(GunAnimation anim);
        void HudEndDisrupted();
        void HudOnDisrupted();
        void HudOnFiredShot();
        void HudOnMorphStart();
        void HudOnWeaponSwitch(BeamType beam);
        void HudOnZoom(bool zoom);
        void InitializePresentation();
        void OpenMissilePresentation();
        void PlayBeamChargeSfx(BeamType beam);
        void PlayBeamEmptySfx(BeamType beam);
        void PlayBeamShotSfx(BeamType beam, bool charged, bool continuous, bool homing, float amountA);
        void PlayHunterSfx(HunterSfx sfx);
        void PlayLandingSfx();
        void PlayRandomDamageSfx();
        void PlayScrollPresentation();
        void QueueHudMessage(float x, float y, float duration, byte category, int messageId);
        void QueueHudMessage(float x, float y, int maxWidth, float duration, byte category, int messageId);
        void QueueHudMessage(float x, float y, int maxWidth, float duration, byte category, string text);
        void QueueHudMessage(float x, float y, float duration, byte category, string text);
        void ShowDamageIndicator(int index);
        void ShowNoAmmoMessage();
        void SpawnPresentation();
        void StartBoostPresentation();
        void StopAltFormSfx();
        void StopBeamChargeSfx(BeamType beam);
        void StopTerrainSfx(Terrain prevTerrain);
        void UpdateAltMovementSfx();
        void UpdateBurningSfx(bool burning);
        void UpdateCloakSfx(int index, bool play);
        void UpdateDoubleDamageSfx(int index, bool play);
        void UpdateDoubleDamageSpeed(int speed);
        void UpdateHealthSfx(int health);
        void UpdateHudShiftX(float amount);
        void UpdateHudShiftY(float amount);
        void UpdateOpponent(int slot);
        void UpdateSlidingSfx(float newAmount);
        void UpdateWalkingSfx();
    }

    public partial class PlayerEntity
    {
        internal IPlayerPresentation? Presentation { get; set; }

        public void StartFlagCarrySfx() => Presentation?.StartFlagCarrySfx();
        public void StopFlagCarrySfx() => Presentation?.StopFlagCarrySfx();
        public void StopAllSfx() => Presentation?.StopAllSfx();
        public void PlayPickupSound(SfxId id) => Presentation?.PlayPickupSound(id);
        private void PresentMajorPickup(ItemType itemType)
            => Presentation?.PresentMajorPickup(itemType);
        public void SetDoorChimeTimer(float value) => Presentation?.SetDoorChimeTimer(value);
        public void SetDoorUnlockTimer(float value) => Presentation?.SetDoorUnlockTimer(value);
        public void SetForceFieldSoundTimer(float value) => Presentation?.SetForceFieldSoundTimer(value);
        public void ResetPresentationLighting() => Presentation?.ResetPresentationLighting();
        public void ShowDeathFade() => Presentation?.ShowDeathFade();
        public void ResetWalkingSound() => Presentation?.ResetWalkingSound();
        public void StopContinuousBeamSfx(BeamType beam) => Presentation?.StopContinuousBeamSfx(beam);
        public void RestartLongSfx(bool force = false) => Presentation?.RestartLongSfx(force);
        public void RestartTimedSfx(bool force = false) => Presentation?.RestartTimedSfx(force);
        public void StopLongSfx() => Presentation?.StopLongSfx();
        public void StopTimedSfx() => Presentation?.StopTimedSfx();
        public void ShowNodeStolen() => Presentation?.ShowNodeStolen();
        public void ShowObjectiveMessage(int messageId, float duration) => Presentation?.ShowObjectiveMessage(messageId, duration);
        public void ShowMissingOctolith() => Presentation?.ShowMissingOctolith();
        private void GunAnimationPresentation(GunAnimation anim) => Presentation?.GunAnimationPresentation(anim);
        public void HudEndDisrupted() => Presentation?.HudEndDisrupted();
        private void HudOnDisrupted() => Presentation?.HudOnDisrupted();
        private void HudOnFiredShot() => Presentation?.HudOnFiredShot();
        private void HudOnMorphStart() => Presentation?.HudOnMorphStart();
        private void HudOnWeaponSwitch(BeamType beam) => Presentation?.HudOnWeaponSwitch(beam);
        private void HudOnZoom(bool zoom) => Presentation?.HudOnZoom(zoom);
        private void InitializePresentation() => Presentation?.InitializePresentation();
        private void OpenMissilePresentation() => Presentation?.OpenMissilePresentation();
        private void PlayBeamChargeSfx(BeamType beam) => Presentation?.PlayBeamChargeSfx(beam);
        private void PlayBeamEmptySfx(BeamType beam) => Presentation?.PlayBeamEmptySfx(beam);
        private void PlayBeamShotSfx(BeamType beam, bool charged, bool continuous, bool homing, float amountA) => Presentation?.PlayBeamShotSfx(beam, charged, continuous, homing, amountA);
        private void PlayHunterSfx(HunterSfx sfx) => Presentation?.PlayHunterSfx(sfx);
        private void PlayLandingSfx() => Presentation?.PlayLandingSfx();
        private void PlayRandomDamageSfx() => Presentation?.PlayRandomDamageSfx();
        private void PlayScrollPresentation() => Presentation?.PlayScrollPresentation();
        public void QueueHudMessage(float x, float y, float duration, byte category, int messageId) => Presentation?.QueueHudMessage(x, y, duration, category, messageId);
        public void QueueHudMessage(float x, float y, int maxWidth, float duration, byte category, int messageId) => Presentation?.QueueHudMessage(x, y, maxWidth, duration, category, messageId);
        public void QueueHudMessage(float x, float y, int maxWidth, float duration, byte category, string text) => Presentation?.QueueHudMessage(x, y, maxWidth, duration, category, text);
        public void QueueHudMessage(float x, float y, float duration, byte category, string text) => Presentation?.QueueHudMessage(x, y, duration, category, text);
        private void ShowDamageIndicator(int index) => Presentation?.ShowDamageIndicator(index);
        private void ShowNoAmmoMessage() => Presentation?.ShowNoAmmoMessage();
        private void SpawnPresentation() => Presentation?.SpawnPresentation();
        private void StartBoostPresentation() => Presentation?.StartBoostPresentation();
        private void StopAltFormSfx() => Presentation?.StopAltFormSfx();
        private void StopBeamChargeSfx(BeamType beam) => Presentation?.StopBeamChargeSfx(beam);
        private void StopTerrainSfx(Terrain prevTerrain) => Presentation?.StopTerrainSfx(prevTerrain);
        private void UpdateAltMovementSfx() => Presentation?.UpdateAltMovementSfx();
        private void UpdateBurningSfx(bool burning) => Presentation?.UpdateBurningSfx(burning);
        private void UpdateCloakSfx(int index, bool play) => Presentation?.UpdateCloakSfx(index, play);
        private void UpdateDoubleDamageSfx(int index, bool play) => Presentation?.UpdateDoubleDamageSfx(index, play);
        private void UpdateDoubleDamageSpeed(int speed) => Presentation?.UpdateDoubleDamageSpeed(speed);
        private void UpdateHealthSfx(int health) => Presentation?.UpdateHealthSfx(health);
        private void UpdateHudShiftX(float amount) => Presentation?.UpdateHudShiftX(amount);
        private void UpdateHudShiftY(float amount) => Presentation?.UpdateHudShiftY(amount);
        private void UpdateOpponent(int slot) => Presentation?.UpdateOpponent(slot);
        private void UpdateSlidingSfx(float newAmount) => Presentation?.UpdateSlidingSfx(newAmount);
        private void UpdateWalkingSfx() => Presentation?.UpdateWalkingSfx();
    }
}
