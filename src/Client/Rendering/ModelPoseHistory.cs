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
            bool reset = discontinuity || _group != info.Node.Group || _index != info.NodeIndex
                || Math.Abs(info.NodeFrame - _frame) > 2;
            NodePoseSampler.Sample(_model, info, parent, _transforms, _sample);
            for (int i = 0; i < _nodes.Length; i++) _nodes[i].Capture(_sample[i], tick, epoch, reset);
            _group = info.Node.Group;
            _index = info.NodeIndex;
            _frame = info.NodeFrame;
        }
        public void Resolve(float alpha, out Matrix4[] nodes, out float[] stack)
        {
            for (int i = 0; i < _nodes.Length; i++)
                _resolved[i] = _nodes[i].HasSamples ? _nodes[i].Resolve(alpha) : _sample[i];
            for (int i = 0; i < _model.NodeMatrixIds.Count; i++)
            {
                int nodeId = _model.NodeMatrixIds[i];
                Matrix4 value = _resolved[nodeId];
                if (_model.Nodes[nodeId].BillboardMode is BillboardMode.Sphere or BillboardMode.Cylinder)
                    value = value.ClearRotation();
                for (int row = 0; row < 4; row++)
                    for (int column = 0; column < 4; column++) _stack[i * 16 + row * 4 + column] = value[row, column];
            }
            nodes = _resolved;
            stack = _stack;
        }
    }
}
