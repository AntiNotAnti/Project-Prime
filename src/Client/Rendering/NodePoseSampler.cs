using System;
using OpenTK.Mathematics;
namespace MphRead.Mods.Render
{
    /// <summary>Read-only counterpart of Model.AnimateNodes for copied render poses.
    /// The authored LUT interpolation and traversal order match the game evaluator.
    /// No Node.Transform, Node.Animation or skinning cache is written.</summary>
    internal static class NodePoseSampler
    {
        public static void Sample(Model model, AnimationInfo info, Matrix4 parent, Matrix4[] transforms, Matrix4[] poses)
        {
            if (poses.Length != model.Nodes.Count || transforms.Length != poses.Length)
                throw new ArgumentException("Node pose buffer size does not match model.");
            if (poses.Length == 0) return;
            // Ignored subtrees retain their existing authored cache in AnimateNodes.
            for (int i = 0; i < poses.Length; i++) poses[i] = model.Nodes[i].Animation;
            Compute(0);
            Animate(0);
            void Compute(int index)
            {
                for (int i = index; i != -1; i = model.Nodes[i].NextIndex)
                {
                    Node node = model.Nodes[i];
                    Matrix4 local = ComputeNodeTransforms(node.Scale, node.Angle, node.Position / model.Scale);
                    transforms[i] = node.ParentIndex == -1 ? local : local * transforms[node.ParentIndex];
                    if (node.ChildIndex != -1) Compute(node.ChildIndex);
                }
            }
            void Animate(int index)
            {
                for (int i = index; i != -1; i = model.Nodes[i].NextIndex)
                {
                    Node node = model.Nodes[i];
                    Matrix4 value = transforms[i];
                    NodeAnimationGroup? group = info.Node.Group;
                    if (group != null && group.Animations.TryGetValue(node.Name, out NodeAnimation animation))
                    {
                        value = AnimateNode(model, group, animation, model.Scale, info.NodeFrame);
                        if (node.ParentIndex != -1 && !node.AnimIgnoreParent) value *= poses[node.ParentIndex];
                    }
                    poses[i] = value;
                    if (node.ChildIndex != -1 && !node.AnimIgnoreChild) Animate(node.ChildIndex);
                    if (node.AfterTransform.HasValue) poses[i] = node.AfterTransform.Value * poses[i] * parent;
                    else if (node.BeforeTransform.HasValue) poses[i] = poses[i] * parent * node.BeforeTransform.Value;
                    poses[i] *= parent;
                }
            }
        }
        private static Matrix4 ComputeNodeTransforms(Vector3 scale, Vector3 angle, Vector3 position)
        {
            float sinAx = MathF.Sin(angle.X);
            float sinAy = MathF.Sin(angle.Y);
            float sinAz = MathF.Sin(angle.Z);
            float cosAx = MathF.Cos(angle.X);
            float cosAy = MathF.Cos(angle.Y);
            float cosAz = MathF.Cos(angle.Z);

            float v18 = cosAx * cosAz;
            float v19 = cosAx * sinAz;
            float v20 = cosAx * cosAy;

            float v22 = sinAx * sinAy;

            float v17 = v19 * sinAy;

            Matrix4 transform = default;

            transform.M11 = scale.X * cosAy * cosAz;
            transform.M12 = scale.X * cosAy * sinAz;
            transform.M13 = scale.X * -sinAy;

            transform.M21 = scale.Y * ((v22 * cosAz) - v19);
            transform.M22 = scale.Y * ((v22 * sinAz) + v18);
            transform.M23 = scale.Y * sinAx * cosAy;

            transform.M31 = scale.Z * (v18 * sinAy + sinAx * sinAz);
            transform.M32 = scale.Z * (v17 + (v19 * sinAy) - (sinAx * cosAz));
            transform.M33 = scale.Z * v20;

            transform.M41 = position.X;
            transform.M42 = position.Y;
            transform.M43 = position.Z;

            transform.M14 = 0;
            transform.M24 = 0;
            transform.M34 = 0;
            transform.M44 = 1;

            return transform;
        }

        private static Matrix4 AnimateNode(Model model, NodeAnimationGroup group, NodeAnimation animation, Vector3 modelScale, int currentFrame)
        {
            float scaleX = model.InterpolateAnimation(group.Scales, animation.ScaleLutIndexX, currentFrame,
                animation.ScaleBlendX, animation.ScaleLutLengthX, group.FrameCount);
            float scaleY = model.InterpolateAnimation(group.Scales, animation.ScaleLutIndexY, currentFrame,
                animation.ScaleBlendY, animation.ScaleLutLengthY, group.FrameCount);
            float scaleZ = model.InterpolateAnimation(group.Scales, animation.ScaleLutIndexZ, currentFrame,
                animation.ScaleBlendZ, animation.ScaleLutLengthZ, group.FrameCount);
            float rotateX = model.InterpolateAnimation(group.Rotations, animation.RotateLutIndexX, currentFrame,
                animation.RotateBlendX, animation.RotateLutLengthX, group.FrameCount, isRotation: true);
            float rotateY = model.InterpolateAnimation(group.Rotations, animation.RotateLutIndexY, currentFrame,
                animation.RotateBlendY, animation.RotateLutLengthY, group.FrameCount, isRotation: true);
            float rotateZ = model.InterpolateAnimation(group.Rotations, animation.RotateLutIndexZ, currentFrame,
                animation.RotateBlendZ, animation.RotateLutLengthZ, group.FrameCount, isRotation: true);
            float translateX = model.InterpolateAnimation(group.Translations, animation.TranslateLutIndexX, currentFrame,
                animation.TranslateBlendX, animation.TranslateLutLengthX, group.FrameCount);
            float translateY = model.InterpolateAnimation(group.Translations, animation.TranslateLutIndexY, currentFrame,
                animation.TranslateBlendY, animation.TranslateLutLengthY, group.FrameCount);
            float translateZ = model.InterpolateAnimation(group.Translations, animation.TranslateLutIndexZ, currentFrame,
                animation.TranslateBlendZ, animation.TranslateLutLengthZ, group.FrameCount);
            var nodeMatrix = Matrix4.CreateTranslation(translateX / modelScale.X, translateY / modelScale.Y, translateZ / modelScale.Z);
            nodeMatrix = Matrix4.CreateRotationX(rotateX) * Matrix4.CreateRotationY(rotateY) * Matrix4.CreateRotationZ(rotateZ) * nodeMatrix;
            nodeMatrix = Matrix4.CreateScale(scaleX, scaleY, scaleZ) * nodeMatrix;
            return nodeMatrix;
        }

    }
}
