using System;
using MphRead;

namespace MphRead.Mods.Render;

/// <summary>
/// Read-only animation frame prediction and per-instance render sampling.
/// This does not advance a model, interpolate a world pose, or alter 60-Hz
/// simulation state. PR-04..06 will consume the samples in draw-only passes.
/// </summary>
internal static class AnimationSampling
{
    internal readonly record struct NextFrame(int Frame, AnimFlags Flags);

    /// <summary>
    /// Pure equivalent of ModelInstance.UpdateAnimFrames(slot)'s integer rules.
    /// No changes to the canonical implementation are made in this PR.
    /// </summary>
    internal static float BlendChannel(float first, float second, float fraction, bool isRotation = false)
    {
        if (!float.IsFinite(first) || !float.IsFinite(second) || !float.IsFinite(fraction))
            return first;
        float difference = second - first;
        if (isRotation)
        {
            if (difference > MathF.PI) difference -= 2f * MathF.PI;
            else if (difference < -MathF.PI) difference += 2f * MathF.PI;
        }
        return first + difference * Math.Clamp(fraction, 0f, 1f);
    }

    internal static NextFrame Predict(int frame, AnimFlags flags, int step, int frameCount)
    {
        if (flags.TestFlag(AnimFlags.Paused) || flags.TestFlag(AnimFlags.Ended))
            return new(frame, flags);
        if (flags.TestFlag(AnimFlags.PingPong))
        {
            if (flags.TestFlag(AnimFlags.Reverse))
            {
                if (frame <= step)
                    return new(step - frame, flags ^ AnimFlags.Reverse);
                return new(frame - step, flags);
            }
            frame += step;
            if (frame >= frameCount - 1)
                return new(2 * frameCount - frame - 2, flags ^ AnimFlags.Reverse);
            return new(frame, flags);
        }
        if (flags.TestFlag(AnimFlags.Reverse))
        {
            if (frame > step) return new(frame - step, flags);
            if (flags.TestFlag(AnimFlags.NoLoop))
                return new(0, flags | AnimFlags.Ended);
            return new(frame == step ? 0 : frameCount - (step - frame), flags);
        }
        frame += step;
        if (frame >= frameCount - 1)
        {
            if (flags.TestFlag(AnimFlags.NoLoop))
                return new(frameCount - 1, flags | AnimFlags.Ended);
            if (frame >= frameCount)
                return new(frame - frameCount, flags);
        }
        return new(frame, flags);
    }
}

internal sealed class AnimationSamples
{
    internal readonly record struct Sample(int FromFrame, int ToFrame, float Fraction);

    private struct SlotTick
    {
        internal bool Valid;
        internal long StepAt;
        internal long Interval;
        internal int Frame, Next, Step, FrameCount, Index;
        internal AnimFlags Flags, NextFlags;
        internal object? NodeGroup, MaterialGroup, TexcoordGroup, TextureGroup;
        internal int NodeSlot, MaterialSlot, TexcoordSlot, TextureSlot;
    }

    private readonly SlotTick[] _ticks = new SlotTick[2];

    internal void Reset() => Array.Clear(_ticks);

    /// <summary>Called after a real animation state transition, not from a draw.</summary>
    internal void Record(AnimationInfo info, int slot, long simulationStep)
    {
        if ((uint)slot >= 2 || simulationStep < 0) return;
        ref SlotTick tick = ref _ticks[slot];
        long interval = tick.Valid && simulationStep > tick.StepAt
            ? simulationStep - tick.StepAt : 0;
        // Focus recovery, model replacement or catch-up must never create a
        // huge extrapolation window. Await another normal tick instead.
        if (interval > 8) interval = 0;
        AnimationSampling.NextFrame next = AnimationSampling.Predict(
            info.Frame[slot], info.Flags[slot], info.Step[slot], info.FrameCount[slot]);
        tick = new SlotTick
        {
            Valid = true,
            StepAt = simulationStep,
            Interval = interval,
            Frame = info.Frame[slot], Next = next.Frame,
            Flags = info.Flags[slot], NextFlags = next.Flags,
            Step = info.Step[slot], FrameCount = info.FrameCount[slot], Index = info.Index[slot],
            NodeGroup = info.Node.Group, MaterialGroup = info.Material.Group,
            TexcoordGroup = info.Texcoord.Group, TextureGroup = info.Texture.Group,
            NodeSlot = info.Node.Slot, MaterialSlot = info.Material.Slot,
            TexcoordSlot = info.Texcoord.Slot, TextureSlot = info.Texture.Slot
        };
    }

    /// <summary>
    /// Returns intermediate frame-only data, never shared matrices/materials.
    /// Caller supplies a 60-Hz step position including FrameTiming's fractional
    /// phase; a separate preview clock can supply its own unit-step position.
    /// </summary>
    internal bool TryGet(AnimationInfo info, int slot, double drawSimulationStep,
        out Sample sample)
    {
        sample = default;
        if ((uint)slot >= 2) return false;
        ref SlotTick tick = ref _ticks[slot];
        if (!tick.Valid || tick.Interval <= 0 || tick.Next == tick.Frame
            || !double.IsFinite(drawSimulationStep)
            || tick.Flags.TestFlag(AnimFlags.Paused) || tick.Flags.TestFlag(AnimFlags.Ended)
            || info.Frame[slot] != tick.Frame || info.Flags[slot] != tick.Flags
            || info.Step[slot] != tick.Step || info.Index[slot] != tick.Index
            || info.FrameCount[slot] != tick.FrameCount
            || info.Node.Slot != tick.NodeSlot || info.Material.Slot != tick.MaterialSlot
            || info.Texcoord.Slot != tick.TexcoordSlot || info.Texture.Slot != tick.TextureSlot
            || !ReferenceEquals(info.Node.Group, tick.NodeGroup)
            || !ReferenceEquals(info.Material.Group, tick.MaterialGroup)
            || !ReferenceEquals(info.Texcoord.Group, tick.TexcoordGroup)
            || !ReferenceEquals(info.Texture.Group, tick.TextureGroup))
            return false;
        double distance = drawSimulationStep - tick.StepAt;
        if (distance < 0 || distance >= tick.Interval) return false;
        sample = new(tick.Frame, tick.Next, (float)(distance / tick.Interval));
        return true;
    }
}
