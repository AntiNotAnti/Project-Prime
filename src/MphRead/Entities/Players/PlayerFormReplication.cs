using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private uint _modFormStartedFrame;
    internal uint ModFormStartedFrame => _modFormStartedFrame;

    internal PlayerFormState ModCaptureForm() => new()
    {
        Frame = NetSession.NetFrame,
        BodyHealth = (ushort)Math.Clamp(Health, 0, ushort.MaxValue),
        StartedFrame = _modFormStartedFrame,
        Phase = IsMorphing ? FormPhase.Morphing : IsUnmorphing ? FormPhase.Unmorphing
            : IsAltForm ? FormPhase.Alt : FormPhase.Biped,
        TurretActive = Hunter == Hunter.Weavel && Flags2.TestFlag(PlayerFlags2.Halfturret),
        TurretHealth = Hunter == Hunter.Weavel ? (ushort)Math.Clamp(_halfturret.Health, 0, ushort.MaxValue) : (ushort)0,
        TurretPosition = Hunter == Hunter.Weavel ? _halfturret.Position : Vector3.Zero
    };

    // Lifecycle/order policy belongs to the scene's replication bridge. Native
    // form construction still owns models, collision volumes, camera and effects,
    // but may not split/merge an already authoritative health sample a second time.
    internal void ModApplyAuthorityForm(in PlayerFormState state)
    {
        if (!state.IsValid || state.Phase == FormPhase.Unknown) return;
        int health = Health;
        bool entering = state.Phase is FormPhase.Morphing or FormPhase.Alt;
        bool newTransition = state.StartedFrame != _modFormStartedFrame;
        if (entering)
        {
            if (newTransition && (IsAltForm || IsMorphing))
            {
                ExitAltForm();
                ModForceForm(false);
            }
            if (!IsAltForm && !IsMorphing)
            {
                // A replica can receive two transitions before the entity loop
                // retires the previous turret. There must only be one instance.
                if (Hunter == Hunter.Weavel) _scene.RemoveEntity(_halfturret);
                EnterAltForm();
            }
            if (state.Phase == FormPhase.Alt) ModForceForm(true);
            else
            {
                if (IsAltForm) UpdateForm(false);
                Flags1 &= ~PlayerFlags1.Unmorphing;
                Flags1 |= PlayerFlags1.Morphing;
                SetBipedAnimation(PlayerAnimation.Morph, AnimFlags.NoLoop);
                ModSetFormAnimationAge(state.Frame - state.StartedFrame);
            }
        }
        else
        {
            if (IsAltForm || IsMorphing || Flags2.TestFlag(PlayerFlags2.Halfturret)
                || state.Phase == FormPhase.Unmorphing && !IsUnmorphing)
                ExitAltForm();
            if (state.Phase == FormPhase.Biped) ModForceForm(false);
            else ModSetFormAnimationAge(state.Frame - state.StartedFrame);
        }
        _modFormStartedFrame = state.StartedFrame;
        Health = health;
        ModApplyAuthorityTurret(state);
    }

    private void ModSetFormAnimationAge(uint age)
    {
        // Native model animations advance at 30Hz inside the 60Hz simulation.
        SetAge(_bipedModel1.AnimInfo, age);
        SetAge(_bipedModel2.AnimInfo, age);
        static void SetAge(AnimationInfo animation, uint age)
            => animation.Frame[0] = (int)Math.Min(age / 2, (uint)Math.Max(0, animation.FrameCount[0] - 1));
    }

    internal void ModApplyAuthorityTurret(in PlayerFormState state)
    {
        if (Hunter != Hunter.Weavel) return;
        bool active = state.TurretActive && state.TurretHealth > 0
            && state.Phase is FormPhase.Morphing or FormPhase.Alt;
        if (!active)
        {
            // Die is idempotent and preserves the native terminal effect.
            _halfturret.Die();
            return;
        }
        if (!IsAltForm && !IsMorphing) return;
        bool present = false;
        foreach (var entity in _scene.Entities)
            if (ReferenceEquals(entity, _halfturret)) { present = true; break; }
        if (!present)
        {
            int health = Health;
            _scene.AddEntity(_halfturret);
            Health = health;
        }
        int healthAfter = NetHitPrediction.TurretHealthFor(SlotIndex, state.TurretHealth, state.DamageEventId);
        if (healthAfter <= 0) { _halfturret.Die(); return; }
        ModRestoreHalfturretFlag();
        _halfturret.Health = healthAfter;
        _halfturret.ModApplyAuthorityAnchor(state.TurretPosition);
    }
}
