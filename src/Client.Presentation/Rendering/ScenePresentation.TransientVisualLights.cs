using System;
using OpenTK.Mathematics;

namespace MphRead
{
    public partial class ScenePresentation
    {
        // Integration is deliberately limited to authoritative muzzle events.
        // Generic SpawnEffect IDs do not establish impact/explosion identity,
        // and the pickup-respawn notice stream is announcement-filtered rather
        // than complete, so neither is inferred into transient lights here.
        private readonly TransientVisualLightPresentationState
            _transientVisualLights = new();

        /// <summary>
        /// Queues one explicit, stable presentation event for transient-light
        /// capture. The call retains no gameplay or entity object.
        /// </summary>
        internal bool TrySpawnTransientVisualLight(ulong stableSourceKey,
            Vector3 position, VisualLightProfile profile)
            => _transientVisualLights.TryQueue(stableSourceKey, position, profile);

        internal ulong GetTransientVisualLightSourceKey(
            TransientVisualLightSourceKind kind, uint eventId)
            => TransientVisualLightSourceKey.ForEvent(kind,
                _visualLightIdentities.Scope, eventId);

        private void SubmitTransientVisualLights(ulong capturedPresentationTick,
            TimeSpan capturedPresentationTime)
        {
            VisualLightCandidate[] candidates = _transientVisualLights.CaptureFrame(
                capturedPresentationTick, capturedPresentationTime);
            for (int i = 0; i < candidates.Length; i++)
                _renderFrame.AddVisualLightCandidate(candidates[i]);
        }

        private void ResetTransientVisualLights()
            => _transientVisualLights.Reset();
    }
}
