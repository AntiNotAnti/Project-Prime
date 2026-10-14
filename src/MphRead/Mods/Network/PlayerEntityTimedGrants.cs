using MphRead.Mods.Network;
using MphRead.Sound;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    internal ushort ModTimedVisualRemaining(TimedPowerUp type) => type switch
    { TimedPowerUp.DoubleDamage => _doubleDmgTimer, TimedPowerUp.Cloak => Flags2.TestFlag(PlayerFlags2.Cloaking) ? _cloakTimer : (ushort)0,
        TimedPowerUp.DeathAlt => _deathaltTimer, _ => 0 };
    internal bool ModUsesTimedGrantPresentation => _scene.Services is ReplaySceneServices replay
        ? replay.State.TimedGrants.HasCoverage
        : NetSession.IsClient && !NetSession.IsAuthority;

    internal bool ModConfirmedTimedEffect(TimedPowerUp type)
    {
        if (!ModUsesTimedGrantPresentation)
            return type switch { TimedPowerUp.DoubleDamage => _doubleDmgTimer > 0,
                TimedPowerUp.Cloak => Flags2.TestFlag(PlayerFlags2.Cloaking) && _cloakTimer > 0,
                TimedPowerUp.DeathAlt => _deathaltTimer > 0, _ => false };
        if (Health == 0) return false;
        if (_scene.Services is ReplaySceneServices replay)
        {
            if (replay.State.Match is { } match && replay.State.TryGetPlayer(SlotIndex, out var state))
                return replay.State.TimedGrants.View(new(match.AuthorityEpoch, match.MatchId, SlotIndex,
                    state.SlotGeneration, state.LifeId, 0), type, replay.Session.RecordingFrame).ConfirmedRemaining > 0;
            return false;
        }
        return NetGrantNetwork.Presentation.View(ShotKey.For(SlotIndex, 0), type, NetSession.NetFrame).ConfirmedRemaining > 0;
    }

    internal void ModProjectTimedGrants(TimedGrantPresentation presentation, ShotKey life, uint frame, bool quiet)
    {
        bool alive = Health > 0 && ModIsInPlay;
        Apply(TimedPowerUp.DoubleDamage);
        Apply(TimedPowerUp.Cloak);
        Apply(TimedPowerUp.DeathAlt);
        NetCampaignEvidence.TimedGrantView(this, presentation, life, frame);
        void Apply(TimedPowerUp type)
        {
            var view = alive ? presentation.View(life, type, frame) : default;
            // Current network clients always suppress unknown native timed grants.
            // Historical replicas keep their original native timers until coverage exists.
            if (alive && !view.Covered && _scene.Services.IsReplica) return;
            ushort remaining = view.Remaining;
            switch (type)
            {
            case TimedPowerUp.DoubleDamage:
                ushort previousDouble = _doubleDmgTimer;
                bool starting = previousDouble == 0 && remaining > 0;
                _doubleDmgTimer = remaining;
                if (remaining == 0)
                {
                    if (IsMainPlayer) UpdateDoubleDamageSfx(0, play: false);
                    if (_doubleDmgEffect != null) { _scene.UnlinkEffectEntry(_doubleDmgEffect); _doubleDmgEffect = null; }
                }
                else if (IsMainPlayer)
                {
                    int stage = remaining <= 240 ? 3 : remaining <= 420 ? 2 : 1;
                    int oldStage = previousDouble <= 240 ? 3 : previousDouble <= 420 ? 2 : 1;
                    if (starting || stage != oldStage) UpdateDoubleDamageSpeed(stage);
                    if (_dblDamageSfxId != _dblDamageIds[stage - 1]) UpdateDoubleDamageSfx(stage - 1, play: true);
                    if (starting && !quiet && !view.Resume)
                    { _timeSincePickup = 0; _soundSource.PlayFreeSfx(SfxId.DOUBLE_DAMAGE_POWER_UP); }
                }
                break;
            case TimedPowerUp.Cloak:
                bool cloaking = Flags2.TestFlag(PlayerFlags2.Cloaking);
                if (remaining == 0) { EndTimedCloak(playLoss: !quiet && alive); break; }
                _cloakTimer = remaining; Flags2 |= PlayerFlags2.Cloaking; _targetAlpha = 3 / 31f;
                int cloakStage = remaining <= 240 ? 2 : remaining <= 420 ? 1 : 0;
                if (IsMainPlayer && _cloakSfxId != _cloakSfxIds[cloakStage]) UpdateCloakSfx(cloakStage, play: true);
                if (!cloaking && IsMainPlayer && !quiet && !view.Resume)
                { _timeSincePickup = 0; _soundSource.PlayFreeSfx(SfxId.CLOAK_POWER_UP); }
                break;
            case TimedPowerUp.DeathAlt:
                bool deathStarting = _deathaltTimer == 0 && remaining > 0;
                _deathaltTimer = remaining;
                // Form prediction runs once at owner contact. Remote and replay
                // forms remain under their existing N9 lifecycle stream.
                if (deathStarting && !quiet && !view.Resume && !_scene.Services.IsReplica && SlotIndex == NetSession.LocalSlot)
                {
                    _timeSincePickup = 0;
                    if (!IsAltForm && !IsMorphing) TrySwitchForms(force: true);
                    if (IsMainPlayer) _soundSource.PlayFreeSfx(SfxId.DOUBLE_DAMAGE_POWER_UP);
                }
                if (remaining == 0 && _deathaltEffect != null)
                { _scene.UnlinkEffectEntry(_deathaltEffect); _deathaltEffect = null; }
                break;
            }
        }
    }
    private void EndTimedCloak(bool playLoss = false)
    {
        if (!Flags2.TestFlag(PlayerFlags2.Cloaking)) return; // preserve passive Trace camouflage
        Flags2 &= ~PlayerFlags2.Cloaking; _cloakTimer = 0; _targetAlpha = 1;
        if (playLoss) _soundSource.PlayFreeSfx(SfxId.CLOAK_OFF);
        if (IsMainPlayer) UpdateCloakSfx(0, play: false);
    }
}
