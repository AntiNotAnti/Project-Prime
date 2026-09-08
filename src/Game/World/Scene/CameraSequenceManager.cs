using System;
using MphRead.Entities;
using MphRead.Formats;

namespace MphRead
{
    // Sequences contain playback state and resolved scene entity references, so even
    // the loaded sequence cache belongs to one scene, not the shared content cache.
    public sealed class CameraSequenceManager
    {
        private readonly Scene _scene;
        private readonly CameraSequence?[] _sequences = new CameraSequence[199];
        public CameraSequence? Current { get; set; }
        public CameraSequence? Intro { get; set; }

        internal CameraSequenceManager(Scene scene) => _scene = scene;

        public CameraSequence GetOrLoad(int id)
        {
            if ((uint)id >= _sequences.Length) throw new ArgumentOutOfRangeException(nameof(id));
            return _sequences[id] ??= CameraSequence.Load(id, _scene);
        }

        public void Clear()
        {
            Current = null;
            Intro = null;
            Array.Clear(_sequences);
        }
    }

    public sealed class SpecialEntityRegistry
    {
        public CamSeqEntity? CameraSequence { get; set; }
        public PointModuleEntity? PointModule { get; internal set; }

        public void CancelCameraSequence() => CameraSequence?.Cancel();

        public void Clear()
        {
            CameraSequence = null;
            PointModule = null;
        }
    }
}
