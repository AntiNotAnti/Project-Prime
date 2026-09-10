using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Render-only history for the two authored player biped tracks.
    ///
    /// Unlike an entity pose history, this class never captures the entity's
    /// world transform.  Its samples are root-relative skeletal matrices;
    /// <see cref="Resolve"/> applies the one world root for the picture and
    /// writes the matching skinning stack into reusable buffers.
    /// </summary>
    internal sealed class PlayerBipedPoseHistory
    {
        private Model _model;
        private Matrix4[] _sample;
        private Matrix4[] _resolved;
        private SimulationPoseHistory[] _nodes;
        private float[] _stack;
        private int _spineIndex;
        private NodeAnimationGroup? _legsGroup;
        private NodeAnimationGroup? _torsoGroup;
        private int _legsIndex = -1;
        private int _torsoIndex = -1;
        private int _legsFrame;
        private int _torsoFrame;
        private bool _captured;

        public PlayerBipedPoseHistory(Model model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _sample = Array.Empty<Matrix4>();
            _resolved = Array.Empty<Matrix4>();
            _nodes = Array.Empty<SimulationPoseHistory>();
            _stack = Array.Empty<float>();
            _spineIndex = -1;
            EnsureBuffers(model);
        }

        public Model Model => _model;
        public bool HasSamples => _captured;

        /// <summary>
        /// Reuses this per-player history when a LOD or hunter replacement
        /// supplies a different runtime model.  Buffers are resized only when
        /// the model shape changes; a same-shaped replacement just resets the
        /// history and starts with one fresh sample.
        /// </summary>
        public void SetModel(Model model)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (ReferenceEquals(_model, model)) return;
            _model = model;
            EnsureBuffers(model);
            Reset();
        }

        public void Reset()
        {
            _legsGroup = null;
            _torsoGroup = null;
            _legsIndex = _torsoIndex = -1;
            _legsFrame = _torsoFrame = 0;
            _captured = false;
            for (int i = 0; i < _nodes.Length; i++) _nodes[i].Reset();
        }

        /// <summary>Capture one completed simulation pose at the existing game cadence.</summary>
        public void Capture(AnimationInfo legs, AnimationInfo torso, float pitch,
            ulong tick, long epoch, bool discontinuity = false)
        {
            bool animationDiscontinuity = !_captured || discontinuity
                || _legsGroup != legs.Node.Group || _torsoGroup != torso.Node.Group
                || _legsIndex != legs.NodeIndex || _torsoIndex != torso.NodeIndex
                || Math.Abs(legs.NodeFrame - _legsFrame) > 2
                || Math.Abs(torso.NodeFrame - _torsoFrame) > 2;

            NodePoseSampler.SampleBiped(_model, legs, torso, _spineIndex, pitch, _sample);
            for (int i = 0; i < _nodes.Length; i++)
                _nodes[i].Capture(_sample[i], tick, epoch, animationDiscontinuity);

            _legsGroup = legs.Node.Group;
            _torsoGroup = torso.Node.Group;
            _legsIndex = legs.NodeIndex;
            _torsoIndex = torso.NodeIndex;
            _legsFrame = legs.NodeFrame;
            _torsoFrame = torso.NodeFrame;
            _captured = true;
        }

        /// <summary>
        /// Resolve the local history and apply the current world root once.
        /// Both returned arrays are owned by this history and remain stable
        /// until the next resolve; callers must consume them immediately.
        /// </summary>
        public void Resolve(float alpha, in Matrix4 worldRoot,
            out Matrix4[] nodes, out float[] stack)
        {
            if (!_captured)
            {
                nodes = Array.Empty<Matrix4>();
                stack = Array.Empty<float>();
                return;
            }

            for (int i = 0; i < _nodes.Length; i++)
            {
                Matrix4 local = _nodes[i].HasSamples ? _nodes[i].Resolve(alpha) : _sample[i];
                // Samples never contain world movement.  Keep the row-vector
                // multiplication order used by Model.AnimateNodes and the
                // existing draw path when applying the current root.
                _resolved[i] = local * worldRoot;
            }

            for (int i = 0; i < _model.NodeMatrixIds.Count; i++)
            {
                int nodeId = _model.NodeMatrixIds[i];
                Matrix4 value = _resolved[nodeId];
                if (_model.Nodes[nodeId].BillboardMode is BillboardMode.Sphere or BillboardMode.Cylinder)
                    value = value.ClearRotation();
                for (int row = 0; row < 4; row++)
                    for (int column = 0; column < 4; column++)
                        _stack[i * 16 + row * 4 + column] = value[row, column];
            }

            nodes = _resolved;
            stack = _stack;
        }

        private void EnsureBuffers(Model model)
        {
            int nodeCount = model.Nodes.Count;
            if (_sample.Length != nodeCount)
            {
                _sample = new Matrix4[nodeCount];
                _resolved = new Matrix4[nodeCount];
                _nodes = new SimulationPoseHistory[nodeCount];
                for (int i = 0; i < nodeCount; i++) _nodes[i] = new SimulationPoseHistory();
            }
            else if (_nodes.Length == 0 && nodeCount != 0)
            {
                _nodes = new SimulationPoseHistory[nodeCount];
                for (int i = 0; i < nodeCount; i++) _nodes[i] = new SimulationPoseHistory();
            }

            int stackLength = model.NodeMatrixIds.Count * 16;
            if (_stack.Length != stackLength) _stack = new float[stackLength];
            _spineIndex = model.GetNodeIndexByName("Spine_1");
        }
    }
}
