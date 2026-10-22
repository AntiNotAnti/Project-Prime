using System;
using MphRead;
using MphRead.Mods.Render;

namespace MphRead.NetTest;

internal static class AnimationSamplingTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string text)
        {
            checks++;
            if (!ok) throw new InvalidOperationException("ANIMATION SAMPLING " + text);
        }
        // Independent canonical state machine is the oracle: the predictor
        // must agree across every integer transition and both animation slots.
        foreach (AnimFlags flags in new[]
        {
            AnimFlags.None, AnimFlags.Reverse, AnimFlags.PingPong,
            AnimFlags.PingPong | AnimFlags.Reverse, AnimFlags.NoLoop,
            AnimFlags.NoLoop | AnimFlags.Reverse, AnimFlags.Paused,
            AnimFlags.Ended, AnimFlags.PingPong | AnimFlags.NoLoop
        })
        for (int count = 1; count <= 16; count++)
        for (int step = 1; step <= 4; step++)
        for (int frame = 0; frame < count; frame++)
        {
            var model = new ModelInstance(null!);
            var info = model.AnimInfo;
            info.Node.Slot = 0; info.Texture.Slot = 0;
            info.Material.Slot = 1; info.Texcoord.Slot = 1;
            for (int slot = 0; slot < 2; slot++)
            {
                info.Frame[slot] = frame; info.FrameCount[slot] = count;
                info.Step[slot] = step; info.Flags[slot] = flags;
            }
            AnimationSampling.NextFrame predicted = AnimationSampling.Predict(frame, flags, step, count);
            model.UpdateAnimFrames();
            Check(info.Frame[0] == predicted.Frame && info.Flags[0] == predicted.Flags
                && info.Frame[1] == predicted.Frame && info.Flags[1] == predicted.Flags,
                "canonical transition parity count=" + count + " frame=" + frame + " flags=" + flags);
        }

        Check(Math.Abs(AnimationSampling.BlendChannel(10f, 20f, 0.25f) - 12.5f) < 0.000001f,
            "channel blend midpoint");
        Check(Math.Abs(AnimationSampling.BlendChannel(179f * MathF.PI / 180f,
            -179f * MathF.PI / 180f, 0.5f, isRotation: true) - MathF.PI) < 0.001f,
            "rotation shortest arc");
        Check(AnimationSampling.BlendChannel(1f, 7f, float.NaN) == 1f,
            "nonfinite blend refused");
        Check(Math.Abs(AnimationSampling.BlendChannel(0f, 31f, 0.5f) - 15.5f) < 0.000001f,
            "material RGB and alpha intermediate value");
        Check(Math.Abs(AnimationSampling.BlendChannel(-1f, 1f, 0.25f) + 0.5f) < 0.000001f,
            "UV scrolling interpolation preserves signed offsets");
        Check(AnimationSampling.BlendChannel(0f, 1f, -1f) == 0f
            && AnimationSampling.BlendChannel(0f, 1f, 2f) == 1f,
            "UV and material fractional sampling clamps unsafe time");
        Check(AnimationRollout.EnabledValues(null, null), "animation channels default on");
        Check(!AnimationRollout.EnabledValues("0", null), "global rollback switch");
        Check(!AnimationRollout.EnabledValues(null, "off"), "channel rollback switch");
        Check(!AnimationRollout.EnabledValues("FALSE", "1"), "case insensitive rollback");
        Check(AnimationRollout.EnabledValues("1", "1"), "explicit override");
        var persistentModel = new ModelInstance(null!);
        var persistent = persistentModel.AnimInfo;
        persistent.Frame[0] = 2; persistent.FrameCount[0] = 10; persistent.Index[0] = 5;
        persistentModel.PresentationSamples.Record(persistent, 0, 2);
        persistentModel.PresentationSamples.Record(persistent, 0, 4);
        Check(persistentModel.PresentationSamples.TryGet(persistent, 0, 5, out _),
            "active Hunter animation sample established");
        persistentModel.SetModel(persistentModel.Model);
        Check(persistentModel.PresentationSamples.TryGet(persistent, 0, 5, out _),
            "same LOD selection preserves Hunter interpolation clock");

        var sampler = new AnimationSamples();
        var state = new AnimationInfo();
        state.Frame[0] = 2; state.FrameCount[0] = 10; state.Step[0] = 1;
        state.Index[0] = 4;
        sampler.Record(state, 0, 2);
        Check(!sampler.TryGet(state, 0, 2.5, out _), "one tick has no cadence");
        sampler.Record(state, 0, 4);
        Check(sampler.TryGet(state, 0, 5, out var midpoint) && midpoint.FromFrame == 2
            && midpoint.ToFrame == 3 && Math.Abs(midpoint.Fraction - 0.5f) < 0.000001f,
            "mid-tick sampling");
        Check(!sampler.TryGet(state, 0, 6, out _), "no extrapolation after cadence ends");
        state.Index[0] = 5;
        Check(!sampler.TryGet(state, 0, 5, out _), "animation index change invalidates stale sample");
        state.Index[0] = 4;
        state.Flags[0] = AnimFlags.Paused;
        Check(!sampler.TryGet(state, 0, 5, out _), "pause invalidates sample");
        state.Flags[0] = AnimFlags.None;
        sampler.Reset();
        Check(!sampler.TryGet(state, 0, 5, out _), "reset invalidates sample");
        sampler.Record(state, 0, 10);
        sampler.Record(state, 0, 100);
        Check(!sampler.TryGet(state, 0, 101, out _), "long stall disables interpolation");

        Console.WriteLine("ANIMATION SAMPLING PASS " + checks + " checks");
        return 0;
    }
}
