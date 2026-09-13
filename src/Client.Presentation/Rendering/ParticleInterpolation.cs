using System.Collections.Generic;
using MphRead.Effects;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
namespace MphRead
{
    internal sealed class ParticlePoseHistory
    {
        private sealed class ParticlePoseTrack
        {
            public readonly SimulationPoseHistory History = new();
            public ulong Seen;
        }
        private readonly Dictionary<EffectParticle, ParticlePoseTrack> _particlePoses = new();
        private readonly List<EffectParticle> _removedParticlePoses = new();
        private readonly Dictionary<EffectElementEntry, ParticlePoseTrack> _ownerPoses = new();
        private readonly List<EffectElementEntry> _removedOwnerPoses = new();
        public void Clear()
        {
            _particlePoses.Clear();
            _ownerPoses.Clear();
        }
        public void Remove(EffectParticle particle) => _particlePoses.Remove(particle);
        public void Capture(IEnumerable<EffectElementEntry> elements, ulong tick, long epoch)
        {
            // Independent world-space sprites and owner-relative attachments
            // each retain their own completed simulation transform. The latter
            // is resolved separately so drawing never mutates the effect state.
            foreach (EffectElementEntry element in elements)
            {
                if (element.Flags.TestFlag(EffElemFlags.UseTransform))
                {
                    if (!_ownerPoses.TryGetValue(element, out ParticlePoseTrack? ownerTrack))
                        _ownerPoses.Add(element, ownerTrack = new());
                    ownerTrack.History.Capture(element.Transform, tick, epoch);
                    ownerTrack.Seen = tick;
                }
                if (element.Flags.TestFlag(EffElemFlags.UseTransform) || element.Flags.TestFlag(EffElemFlags.UseMesh)) continue;
                foreach (EffectParticle particle in element.Particles)
                {
                    if (!_particlePoses.TryGetValue(particle, out ParticlePoseTrack? track))
                        _particlePoses.Add(particle, track = new());
                    track.History.Capture(Matrix4.CreateTranslation(particle.Position), tick, epoch);
                    track.Seen = tick;
                }
            }
            _removedParticlePoses.Clear();
            foreach (var pair in _particlePoses) if (pair.Value.Seen != tick) _removedParticlePoses.Add(pair.Key);
            foreach (EffectParticle removed in _removedParticlePoses) _particlePoses.Remove(removed);
            _removedOwnerPoses.Clear();
            foreach (var pair in _ownerPoses) if (pair.Value.Seen != tick) _removedOwnerPoses.Add(pair.Key);
            foreach (EffectElementEntry removed in _removedOwnerPoses) _ownerPoses.Remove(removed);
        }
        public Matrix4 Resolve(EffectParticle particle, float alpha, bool enabled)
        {
            if (enabled && !particle.DrawNode && _particlePoses.TryGetValue(particle, out ParticlePoseTrack? track)
                && track.History.HasSamples) return track.History.Resolve(alpha);
            return Matrix4.CreateTranslation(particle.Position);
        }
        public Matrix4 ResolveOwner(EffectElementEntry owner, float alpha, bool enabled)
        {
            if (enabled && _ownerPoses.TryGetValue(owner, out ParticlePoseTrack? track)
                && track.History.HasSamples)
            {
                return track.History.Resolve(alpha);
            }
            return owner.Transform;
        }
    }
    public partial class ScenePresentation
    {
        private readonly ParticlePoseHistory _particlePoses = new();
        private void CaptureParticlePoses(ulong tick) => _particlePoses.Capture(_activeElements, tick, _poseGeneration);
        internal Matrix4 ResolveParticleTransform(EffectParticle particle) =>
            _particlePoses.Resolve(particle, Timing.RenderAlpha, InterpolationEnabled);
        internal Matrix4 ResolveEffectOwnerTransform(EffectElementEntry owner) =>
            _particlePoses.ResolveOwner(owner, Timing.RenderAlpha, InterpolationEnabled);
    }
}
