using System;
using System.Collections;
using System.Collections.Generic;
using MphRead;
using MphRead.Effects;
using MphRead.Entities;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

// Observe values already consumed by the normal native draw. No particle
// functions, steps or drawing are called by this witness.
sealed partial class CheckWindow
{
    private long _poseParticleSamples, _poseParticleMotionSamples, _poseParticleAppearanceSamples;
    private long _poseBeamDraws, _poseBeamSamples;
    private readonly List<object> _poseParticleSampleExamples = new();

    private void ObserveDisplayParticleSamples()
    {
        foreach (EntityBase entity in Scene.Entities)
        {
            if (entity is not BeamProjectileEntity beam || !beam.Initialized || !beam.ShouldDraw || beam.Age < 0) continue;
            PoseRequire(PoseGet(beam, "_beamDrawActive") is false, "beam draw scope escaped native submission");
            _poseBeamDraws++;
            bool sampled = PoseGet(beam, "_beamDrawSampled") is true;
            if (sampled) _poseBeamSamples++;
            if (_cap == 60) PoseRequire(!sampled, "60 Hz beam drawing left the current completed pose");
        }
        foreach (EffectElementEntry element in (IEnumerable)PoseGet(Scene, "_activeElements")!)
        {
            if (!element.Flags.TestFlag(EffElemFlags.DrawEnabled)) continue;
            foreach (EffectParticle particle in element.Particles)
            {
                PoseRequire(PoseGet(particle, "_drawPoseActive") is false, "particle draw scope escaped native submission");
                object sampled = PoseGet(particle, "_drawPose")
                    ?? throw new MissingMemberException("Particle display history is unavailable in this client.");
                Vector3 position = (Vector3)PoseGet(sampled, "Position")!;
                float scale = (float)PoseGet(sampled, "Scale")!;
                float alpha = (float)PoseGet(sampled, "Alpha")!;
                bool motion = position != particle.Position;
                bool appearance = scale != particle.Scale || alpha != particle.Alpha;
                _poseParticleSamples++;
                if (motion) _poseParticleMotionSamples++;
                if (appearance) _poseParticleAppearanceSamples++;
                if (_cap == 60)
                    PoseRequire(!motion && !appearance, "60 Hz particle drawing left the current completed pose");
                if (motion && _poseParticleSampleExamples.Count < 4)
                    _poseParticleSampleExamples.Add(new { tick = Scene.FrameCount, FrameTiming.PresentationAlpha,
                        element.EffectId, particle.DrawId, sampledPosition = PoseVector(position),
                        canonicalPosition = PoseVector(particle.Position), sampledScale = scale, canonicalScale = particle.Scale });
            }
        }
    }
}
