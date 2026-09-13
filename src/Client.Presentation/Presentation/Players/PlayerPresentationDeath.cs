using System;
using MphRead.Combat;
using MphRead.Mods.Network;
using MphRead.Cosmetics;
using MphRead.Cosmetics.Presentation;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private readonly DeathPresentationGate _deathPresentation = new();
        private readonly CapturedDeathPose _capturedDeathPose = new();
        private readonly DeathPresentationRuntime _deathRuntime = new();
        private DeathEffectFrame? _deathFrame;
        private bool _deathDistortionSubmitted;
        private bool _networkDeathParticles;
        private uint _networkDeathParticleTick;
        private CombatActor _authoritativeActor = CombatActor.None;
        internal CombatActor AuthoritativeActor => _authoritativeActor;

        internal void ObserveAuthoritativeDeath(in CombatActor actor, bool dead, bool altForm,
            uint tick, bool presentationAlreadyHandled, bool suppressSound)
        {
            if (_authoritativeActor.IsValid && _authoritativeActor != actor)
            {
                _deathRuntime.Cancel();
                _capturedDeathPose.Invalidate();
                _networkDeathParticles = false;
            }
            _authoritativeActor = actor;
            DeathPresentationCue cue = _deathPresentation.ObserveSnapshot(actor, dead, altForm,
                tick, presentationAlreadyHandled);
            if (!dead && _deathRuntime.Active)
            {
                _deathRuntime.Cancel();
                _networkDeathParticles = false;
            }
            PresentDeathCue(cue, suppressSound);
        }

        internal void PresentAuthoritativeKill(in KillEvent value, uint receiptTick, bool suppressSound)
        {
            _ = receiptTick;
            DeathPresentationCue cue = _deathPresentation.ObserveKill(value.Victim, value.Tick);
            _deathRuntime.ReconcileAuthoritativeTick(value.Victim, value.Tick);
            PresentDeathCue(cue, suppressSound);
        }

        internal void ResetAuthoritativeDeathPresentation()
        {
            _deathPresentation.Reset();
            _networkDeathParticles = false;
            _networkDeathParticleTick = 0;
            _authoritativeActor = CombatActor.None;
            _deathRuntime.Cancel();
            _deathFrame = null;
            _capturedDeathPose.Invalidate();
        }

        internal void CaptureAliveDeathPose(Model model, in Matrix4 root,
            Matrix4[]? submittedNodes, float[]? submittedStack, float alpha)
            => _capturedDeathPose.CaptureSubmitted(model, root, submittedNodes,
                submittedStack, new CapturedDeathAppearance(_player.Hunter,
                    CosmeticLoadoutIds, _player.Recolor, alpha));

        internal bool TryGetDeathPresentation(uint tick,
            out DeathPresentationSample sample)
        {
            _ = tick;
            if (_deathFrame is DeathEffectFrame frame && _deathRuntime.Active)
            {
                sample = frame.Sample;
                return true;
            }
            sample = default;
            return false;
        }

        internal bool PrepareDeathEffect(CosmeticPresentationSettings settings,
            out DeathEffectFrame frame)
        {
            // Death takeover owns the player's cosmetic presentation for this
            // frame; never let the last alive armor material or distortion leak
            // into the captured death body.
            _armorFrame = null;
            _deathFrame = null;
            bool local = _player.IsMainPlayer;
            bool visible = local || IsVisible(_player.NodeRef) || _player.ModNodeUnresolved;
            Vector3 camera = _player._scene.LocalPlayer?.CameraInfo.Position
                ?? _player.Position;
            float distanceSquared = (_player.Position - camera).LengthSquared;
            if (!_deathRuntime.TryEvaluate(settings, local, visible,
                    Math.Max(0, distanceSquared),
                    _player._scene.Services.WorldServerTick,
                    Math.Clamp(Presentation.Timing.RenderAlpha, 0, 1), out frame))
                return false;
            _deathFrame = frame;
            _deathDistortionSubmitted = false;
            return true;
        }

        internal CosmeticMaterialOverride? ResolveDeathMaterial(
            CosmeticMaterialOverride? baseLayer)
        {
            if (_deathFrame?.MaterialOverride is not CosmeticMaterialOverride death)
                return baseLayer;
            if (baseLayer is not CosmeticMaterialOverride skin) return death;
            return death with
            {
                Albedo = skin.Albedo,
                Normal = skin.Normal,
                Emissive = skin.Emissive,
                SpecularStrength = death.SpecularStrength ?? skin.SpecularStrength,
                Smoothness = death.Smoothness ?? skin.Smoothness,
                ReflectionStrength = death.ReflectionStrength ?? skin.ReflectionStrength
            };
        }

        internal EnhancedForceFieldDrawState? TakeDeathDistortion(ModelInstance instance)
        {
            if (_deathDistortionSubmitted || instance != _player._bipedModel2
                || _deathFrame is not DeathEffectFrame frame
                || !Presentation.TryGetArmorAllowance(frame.BudgetRequest.StableKey,
                    out CosmeticBudgetAllowance allowance))
                return null;
            bool supported = RenderBackendSelection.Current == RenderBackendKind.Sdl
                && Mods.RenderOptions.GraphicsPreset == Mods.GraphicsPreset.Enhanced;
            if (!_deathRuntime.TryCreateDistortionState(frame, allowance,
                    Presentation.CapturedPresentationTime, supported, out var state))
                return null;
            _deathDistortionSubmitted = true;
            return state;
        }

        internal void SubmitDeathPrimitives()
        {
            if (_deathFrame is DeathEffectFrame frame
                && Presentation.TryGetArmorAllowance(frame.BudgetRequest.StableKey,
                    out CosmeticBudgetAllowance allowance))
                _deathRuntime.SubmitPrimitives(frame, allowance,
                    Presentation.ArmorPrimitiveSubmissions);
        }

        internal Model? CapturedDeathModel => _capturedDeathPose.Model;

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
            bool takeover = !cue.AltForm && _deathRuntime.Begin(cue.Actor,
                cue.Tick, CosmeticLoadoutIds.DeathEffectId, _capturedDeathPose,
                matchId: _player._scene.Match.MatchId);
            if (!cue.AltForm && CosmeticLoadoutIds.DeathEffectId != 0 && !takeover)
                Mods.DebugLog.Line("cosmetics/death",
                    $"Fell back from death effect {CosmeticLoadoutIds.DeathEffectId} for slot {cue.Actor.Slot}.");
            _networkDeathParticles = !takeover && !cue.AltForm
                && !cue.EnginePresentationHandled;
            _networkDeathParticleTick = cue.Tick;
            if (_player._scene.IsHeadless)
                return;
            if (cue.AltForm)
                _player._scene.SpawnEffect(216, Vector3.UnitX, Vector3.UnitY, _player.Position);
            if (!suppressSound && !cue.EnginePresentationHandled)
                PlayHunterSfx(HunterSfx.Death);
        }
    }
}
