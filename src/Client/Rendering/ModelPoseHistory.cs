using System;
using OpenTK.Mathematics;
namespace MphRead.Mods.Render
{
    internal sealed class ModelPoseHistory
    {
        private readonly Model _model;
        private readonly Matrix4[] _transforms;
        private readonly Matrix4[] _sample;
        private readonly SimulationPoseHistory[] _nodes;
        private readonly Matrix4[] _resolved;
        private readonly float[] _stack;
        private NodeAnimationGroup? _group;
        private int _index = -1;
        private int _frame;
        private int _mode;
        public ModelPoseHistory(Model model)
        {
            _model = model;
            _transforms = new Matrix4[model.Nodes.Count];
            _sample = new Matrix4[model.Nodes.Count];
            _resolved = new Matrix4[model.Nodes.Count];
            _nodes = new SimulationPoseHistory[model.Nodes.Count];
            for (int i = 0; i < _nodes.Length; i++) _nodes[i] = new();
            _stack = new float[model.NodeMatrixIds.Count * 16];
        }
        public Model Model => _model;
        public bool HasSamples
        {
            get
            {
                for (int i = 0; i < _nodes.Length; i++)
                    if (_nodes[i].HasSamples) return true;
                return false;
            }
        }
        public void Capture(AnimationInfo info, Matrix4 parent, ulong tick, long epoch,
            bool discontinuity = false)
        {
            NodePoseSampler.Sample(_model, info, parent, _transforms, _sample);
            CaptureResolved(info, _sample, tick, epoch, discontinuity);
        }
        public void CaptureResolved(AnimationInfo info, ReadOnlySpan<Matrix4> poses,
            ulong tick, long epoch, bool discontinuity = false, int mode = 0)
        {
            if (poses.Length != _sample.Length)
                throw new ArgumentException("Node pose buffer size does not match model.", nameof(poses));
            bool reset = discontinuity || _mode != mode || _group != info.Node.Group
                || _index != info.NodeIndex || Math.Abs(info.NodeFrame - _frame) > 2;
            poses.CopyTo(_sample);
            for (int i = 0; i < _nodes.Length; i++) _nodes[i].Capture(_sample[i], tick, epoch, reset);
            _mode = mode;
            _group = info.Node.Group;
            _index = info.NodeIndex;
            _frame = info.NodeFrame;
        }
        public void Resolve(float alpha, out Matrix4[] nodes, out float[] stack)
        {
            for (int i = 0; i < _nodes.Length; i++)
                _resolved[i] = _nodes[i].HasSamples ? _nodes[i].Resolve(alpha) : _sample[i];
            WriteStack();
            nodes = _resolved;
            stack = _stack;
        }
        public void Resolve(float alpha, ReadOnlySpan<Matrix4> currentPose,
            out Matrix4[] nodes, out float[] stack)
        {
            if (currentPose.Length != _sample.Length)
                throw new ArgumentException("Node pose buffer size does not match model.", nameof(currentPose));
            Matrix4 presentationDelta = Matrix4.Identity;
            bool adjusted = false;
            if (_sample.Length > 0)
            {
                float determinant = _sample[0].Determinant;
                if (float.IsFinite(determinant)
                    && MathF.Abs(determinant) >= 0.000001f)
                {
                    presentationDelta = _sample[0].Inverted()
                        * currentPose[0];
                    adjusted = true;
                }
            }
            for (int i = 0; i < _nodes.Length; i++)
            {
                Matrix4 value = _nodes[i].HasSamples
                    ? _nodes[i].Resolve(alpha) : _sample[i];
                if (adjusted)
                {
                    value *= presentationDelta;
                }
                else
                {
                    value = currentPose[i];
                }
                _resolved[i] = value;
            }
            WriteStack();
            nodes = _resolved;
            stack = _stack;
        }

        private void WriteStack()
        {
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
        }
    }
}
