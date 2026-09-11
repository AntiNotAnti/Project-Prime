using MphRead.Formats;
using MphRead.Hud;
using MphRead.Sound;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        public void LoadPresentationModels()
        {
            _player._gunSmokeModel = Read.GetModelInstance("gunSmoke");
            _player._bipedIceModel = Read.GetModelInstance(_player.Hunter == Hunter.Noxus || _player.Hunter == Hunter.Trace ? "nox_ice" : "samus_ice");
            _player._altIceModel = Read.GetModelInstance("alt_ice");
            _player._doubleDmgModel = Read.GetModelInstance("doubleDamage_img");
            _player._octolithSimpleModel = Read.GetModelInstance("octolith_simple");
            _player._models.Add(_player._gunSmokeModel);
            _player._models.Add(_player._bipedIceModel);
            _player._models.Add(_player._altIceModel);
            _player._models.Add(_player._doubleDmgModel);
            _player._models.Add(_player._octolithSimpleModel);
            _player._trailModel = Read.GetModelInstance("trail");
            Material material = _player._trailModel.Model.Materials[0];
            _trailBindingId1 = Presentation.BindGetTexture(_player._trailModel.Model, material.TextureId, material.PaletteId, 0);
            material = _player._trailModel.Model.Materials[1];
            _trailBindingId2 = Presentation.BindGetTexture(_player._trailModel.Model, material.TextureId, material.PaletteId, 0);
            _doubleDmgBindingId = Presentation.BindGetTexture(_player._doubleDmgModel.Model, 0, 0, 0);
        }

        public void InitializePresentation()
        {
            _hudObjects = HudElements.HunterObjects[(int)_player.Hunter];
            if (_player.IsMainPlayer)
            {
                SetUpHud();
            }

            _walkSfxTimer = 0;
            _walkSfxIndex = 0;
            _burnSfxAmount = 0;
        }

        public void SpawnPresentation()
        {
            ResetAuthoritativeDeathPresentation();
            _missileSfxHandle = -1;
            if (_player.IsMainPlayer)
            {
                // the game only does this in multiplayer, but it can't hurt either way
                ResetReticle();
                _weaponIconInst.SetIndex(0, _player._scene);
            }

            _cloakTextTimer = 30 / 30f;
            _doubleDamageTextTimer = 60 / 30f;
            _player._drawIceLayer = false;
            _hudShiftX = 0;
            _hudShiftY = 0;
            _objShiftX = 0;
            _objShiftY = 0;
            // todo: update more UI fields
            if (_player.IsMainPlayer)
            {
                EndWhiteout();
                if (_player.IsAltForm || _player.IsMorphing)
                {
                    _healthbarYOffset = _hudObjects.HealthOffsetYAlt;
                    _boostBombsYOffset = 160;
                }
                else
                {
                    _healthbarYOffset = _hudObjects.HealthOffsetY;
                    _boostBombsYOffset = 208;
                }
            }
        }

        public void GunAnimationPresentation(GunAnimation anim)
        {
            if (_player.IsMainPlayer)
            {
                if (anim == GunAnimation.MissileClose)
                {
                    _player._soundSource.StopSfxByHandle(_missileSfxHandle);
                    _missileSfxHandle = -1;
                    if (!_player.IsAltForm)
                    {
                        PlayMissileSfx(HunterSfx.MissileClose);
                    }
                }
                else if (anim == GunAnimation.MissileOpen && _player.EquipInfo.ChargeLevel == 0)
                {
                    _player._soundSource.StopSfxByHandle(_missileSfxHandle);
                    if (!_player.IsAltForm && _player._health > 0)
                    {
                        _missileSfxHandle = PlayMissileSfx(HunterSfx.MissileSwitch);
                    }
                }
            }
        }

        public void ShowDamageIndicator(int index)
        {
            _damageIndicatorTimers[index] = (ushort)SimTicks.From30HzFrames(63);
        }

        public void StartBoostPresentation()
        {
            _boostInst.SetAnimation(start: 0, target: 10, frames: 11, afterAnim: 0);
            if (_player.IsMainPlayer)
                Mods.Input.GamepadHaptics.Play(Mods.Input.HapticEvent.MorphBoost,
                    unchecked((uint)_player.ModScene.FrameCount));
        }

        public void PlayScrollPresentation()
        {
            _scrollSfxTimer = 2 / 30f;
        }

        public void OpenMissilePresentation()
        {
            _player._soundSource.StopSfxByHandle(_missileSfxHandle);
            _missileSfxHandle = PlayMissileSfx(HunterSfx.MissileOpen);
        }

        public void ShowNodeStolen() => QueueHudMessage(128, 133, Align.Center, 256, 8, new ColorRgba(31), 1, 90 / 30f, 17, Text.Strings.GetHudMessage(211));
        public void ShowObjectiveMessage(int messageId, float duration) => QueueHudMessage(128, 133, duration, 1, messageId);
        public void ShowMissingOctolith() => QueueHudMessage(128, 50, 1 / 1000f, 0, 232);
        public void ResetPresentationLighting()
        {
            _player._light1Vector = Presentation.Light1Vector;
            _player._light1Color = Presentation.Light1Color;
            _player._light2Vector = Presentation.Light2Vector;
            _player._light2Color = Presentation.Light2Color;
        }

        public void ShowDeathFade() => Presentation.SetFade(FadeType.FadeInWhite, 90 / 30f, overwrite: true);
        public void ResetWalkingSound()
        {
            _walkSfxTimer = 0;
            _walkSfxIndex = 0;
        }
    }
}
