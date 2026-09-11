using MphRead.Combat;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private readonly DeathPresentationGate _deathPresentation = new();
        private bool _networkDeathParticles;
        private uint _networkDeathParticleTick;
        private CombatActor _authoritativeActor = CombatActor.None;
        internal CombatActor AuthoritativeActor => _authoritativeActor;

        internal void ObserveAuthoritativeDeath(in CombatActor actor, bool dead, bool altForm,
            uint tick, bool presentationAlreadyHandled, bool suppressSound)
        {
            _authoritativeActor = actor;
            DeathPresentationCue cue = _deathPresentation.ObserveSnapshot(actor, dead, altForm,
                tick, presentationAlreadyHandled);
            PresentDeathCue(cue, suppressSound);
        }

        internal void PresentAuthoritativeKill(in KillEvent value, uint receiptTick, bool suppressSound)
        {
            DeathPresentationCue cue = _deathPresentation.ObserveKill(value.Victim, receiptTick);
            PresentDeathCue(cue, suppressSound);
        }

        internal void ResetAuthoritativeDeathPresentation()
        {
            _deathPresentation.Reset();
            _networkDeathParticles = false;
            _networkDeathParticleTick = 0;
            _authoritativeActor = CombatActor.None;
        }

        internal bool TryGetNetworkDeathParticleTime(uint tick, out float timePct)
        {
            timePct = 0;
            if (!_networkDeathParticles)
                return false;
            uint age = CombatFeedback.Age(tick, _networkDeathParticleTick);
            if (age >= DeathPresentationGate.DefaultParticleTicks)
            {
                _networkDeathParticles = false;
                return false;
            }
            timePct = age / (float)DeathPresentationGate.DefaultParticleTicks;
            return true;
        }

        private void PresentDeathCue(in DeathPresentationCue cue, bool suppressSound)
        {
            if (!cue.IsValid)
                return;
            _networkDeathParticles = !cue.AltForm;
            _networkDeathParticleTick = cue.Tick;
            if (_player._scene.IsHeadless)
                return;
            if (cue.AltForm)
                _player._scene.SpawnEffect(216, Vector3.UnitX, Vector3.UnitY, _player.Position);
            if (!suppressSound)
                PlayHunterSfx(HunterSfx.Death);
        }
    }
}
