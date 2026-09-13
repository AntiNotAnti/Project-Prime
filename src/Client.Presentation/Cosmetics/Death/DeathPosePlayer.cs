using System;
using System.Collections.Generic;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Cosmetics.Presentation;

/// <summary>Evaluates visual-only death animation into reusable submission buffers.</summary>
public sealed class DeathPosePlayer
{
    private Matrix4[] _nodes = Array.Empty<Matrix4>();
    private float[] _stack = Array.Empty<float>();

    public void Evaluate(CapturedDeathPose capture, DeathAnimationClip? clip,
        float elapsedSeconds, out Matrix4[] nodes, out float[] matrixStack)
    {
        if (!capture.IsValid || capture.Model == null)
            throw new InvalidOperationException("A captured presentation pose is required.");
        EnsureBuffers(capture.Model);
        capture.Nodes.AsSpan().CopyTo(_nodes);

        if (clip != null)
        {
            float blend = clip.BlendDuration <= 0 ? 1
                : Math.Clamp(elapsedSeconds / clip.BlendDuration, 0, 1);
            Matrix4 rootDelta = Matrix4.Identity;
            for (int i = 0; i < clip.Tracks.Count; i++)
            {
                DeathAnimationTrack track = clip.Tracks[i];
                Matrix4 delta = Interpolate(track, elapsedSeconds);
                delta = Interpolate(Matrix4.Identity, delta, blend);
                if (track.NodeIndex == 0)
                    rootDelta = delta;
                else
                    _nodes[track.NodeIndex] = _nodes[track.NodeIndex] * delta;
            }
            if (rootDelta != Matrix4.Identity)
            {
                Matrix4 inverseRoot = capture.Root.Inverted();
                for (int i = 0; i < _nodes.Length; i++)
                    _nodes[i] = _nodes[i] * inverseRoot * rootDelta * capture.Root;
            }
        }

        WriteStack(capture.Model);
        nodes = _nodes;
        matrixStack = _stack;
    }

    private static Matrix4 Interpolate(DeathAnimationTrack track, float seconds)
    {
        IReadOnlyList<CompressedDeathKeyframe> frames = track.Frames;
        float milliseconds = Math.Max(0, seconds * 1000);
        int upper = 0;
        while (upper < frames.Count && frames[upper].TimeMilliseconds < milliseconds) upper++;
        if (upper == 0) return frames[0].ToMatrix();
        if (upper == frames.Count) return frames[^1].ToMatrix();
        CompressedDeathKeyframe before = frames[upper - 1];
        CompressedDeathKeyframe after = frames[upper];
        float amount = (milliseconds - before.TimeMilliseconds)
            / (after.TimeMilliseconds - before.TimeMilliseconds);
        return Compose(
            Vector3.Lerp(before.Translation, after.Translation, amount),
            Quaternion.Slerp(before.Rotation.Normalized(), after.Rotation.Normalized(), amount),
            Vector3.Lerp(before.Scale, after.Scale, amount));
    }

    private static Matrix4 Interpolate(in Matrix4 from, in Matrix4 to, float amount)
        => Compose(Vector3.Lerp(from.ExtractTranslation(), to.ExtractTranslation(), amount),
            Quaternion.Slerp(from.ExtractRotation(), to.ExtractRotation(), amount),
            Vector3.Lerp(from.ExtractScale(), to.ExtractScale(), amount));

    private static Matrix4 Compose(Vector3 translation, Quaternion rotation, Vector3 scale)
        => Matrix4.CreateScale(scale) * Matrix4.CreateFromQuaternion(rotation)
            * Matrix4.CreateTranslation(translation);

    private void EnsureBuffers(Model model)
    {
        if (_nodes.Length != model.Nodes.Count) _nodes = new Matrix4[model.Nodes.Count];
        int stackLength = model.NodeMatrixIds.Count * 16;
        if (_stack.Length != stackLength) _stack = new float[stackLength];
    }

    private void WriteStack(Model model)
    {
        for (int i = 0; i < model.NodeMatrixIds.Count; i++)
        {
            int nodeId = model.NodeMatrixIds[i];
            Matrix4 value = _nodes[nodeId];
            if (model.Nodes[nodeId].BillboardMode is BillboardMode.Sphere or BillboardMode.Cylinder)
                value = value.ClearRotation();
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    _stack[i * 16 + row * 4 + column] = value[row, column];
        }
    }
}
