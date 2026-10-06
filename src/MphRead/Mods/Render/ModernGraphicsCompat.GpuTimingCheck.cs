#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    // Opt-in real-device acceptance. Bounded polling belongs to this check only;
    // gameplay timing never waits for a query or overwrites an occupied slot.
    internal static void VerifyGpuFrameTimingForCheck()
    {
        var s = Current;
        if (!GraphicsTimingPolicy.Enabled)
            throw new InvalidOperationException("Enable GPU timing before creating the acceptance device.");
        if (!s._device.SupportsTimestampQueries)
        {
            Console.WriteLine($"[renderwindowcheck] GPU timestamps SKIP unsupported enabled feature: {s._device.Backend} / {s._device.AdapterName}");
            return;
        }
        if (s._gpuTimingSlots.Count != 0)
            throw new InvalidOperationException("GPU timing acceptance requires an unused timing ring.");

        GraphicsApi.PushAttrib(AttribMask.AllAttribBits);
        int previousProgram = GraphicsApi.GetInteger(GetPName.CurrentProgram);
        int previousFramebuffer = GraphicsApi.GetInteger(GetPName.DrawFramebufferBinding);
        try
        {
            GraphicsApi.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            GraphicsApi.UseProgram(0);
            GraphicsApi.Viewport(0, 0, (int)s._width, (int)s._height);
            foreach (var cap in new[] { EnableCap.DepthTest, EnableCap.CullFace, EnableCap.Blend,
                EnableCap.Texture2D, EnableCap.ScissorTest, EnableCap.StencilTest, EnableCap.AlphaTest })
                GraphicsApi.Disable(cap);
            GraphicsApi.ColorMask(true, true, true, true);
            GraphicsApi.PolygonMode(TriangleFace.FrontAndBack, OpenTK.Graphics.OpenGL.PolygonMode.Fill);

            // Warm the geometry/uniform arenas before isolating ring storage.
            DrawGpuTimingCheckFrame();
            Present();
            var before = LiveResources;
            long bytesBefore = EndPerformanceSample().PooledBufferStorageBytes;
            const long firstFrame = 7000;
            BeginGpuFrameTiming(firstFrame);
            var allocated = LiveResources;
            long bytesAllocated = EndPerformanceSample().PooledBufferStorageBytes;
            if (allocated.QuerySets - before.QuerySets != 8
                || allocated.Buffers - before.Buffers != 16
                || bytesAllocated - bytesBefore != 8 * (256 + 16))
                throw new InvalidOperationException("GPU timestamp ring resource/storage accounting failed.");

            DrawGpuTimingCheckFrame();
            Present();
            for (long frame = firstFrame + 1; frame < firstFrame + 4; frame++)
            {
                BeginGpuFrameTiming(frame);
                DrawGpuTimingCheckFrame();
                Present();
            }
            var received = new HashSet<long>();
            var deadline = Stopwatch.StartNew();
            while (received.Count < 4 && deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                while (TryTakeGpuFrameSample(out var sample))
                {
                    if (sample.FrameId < firstFrame || sample.FrameId >= firstFrame + 4
                        || !received.Add(sample.FrameId)
                        || !double.IsFinite(sample.Milliseconds) || sample.Milliseconds < 0)
                        throw new InvalidOperationException("GPU timestamp readback lost frame identity or produced an invalid duration.");
                }
                if (received.Count < 4) Thread.Sleep(1);
            }
            if (received.Count != 4 || s._gpuTimingFailed != 0 || s._gpuTimingDropped != 0)
                throw new InvalidOperationException($"GPU timestamp acceptance received {received.Count}/4 frames; failed={s._gpuTimingFailed}, dropped={s._gpuTimingDropped}.");

            // Submit one more mapping, then retire its buffers before polling
            // the readback. A native callback may race retirement; its opaque
            // token must already be invalidated before buffers are released.
            BeginGpuFrameTiming(firstFrame + 4);
            DrawGpuTimingCheckFrame();
            Present();
            var tokens = new List<nint>();
            var leases = new List<GpuTimingReadbackLease>();
            foreach (var slot in s._gpuTimingSlots)
            {
                if (slot.Lease.MapToken == 0) continue;
                tokens.Add(slot.Lease.MapToken);
                leases.Add(slot.Lease);
            }
            if (tokens.Count == 0)
                throw new InvalidOperationException("GPU timestamp retirement fixture did not queue a readback.");
            var retiring = LiveResources;
            long retiringBytes = EndPerformanceSample().PooledBufferStorageBytes;
            s.DisposeGpuTiming();
            var retired = LiveResources;
            long retiredBytes = EndPerformanceSample().PooledBufferStorageBytes;
            if (retiring.QuerySets - retired.QuerySets != 8
                || retiring.Buffers - retired.Buffers != 16
                || retiringBytes - retiredBytes != 8 * (256 + 16)
                || s._gpuTimingSlots.Count != 0 || s._gpuTimingResults.Count != 0)
                throw new InvalidOperationException("GPU timestamp retirement retained owned resources or storage.");

            deadline.Restart();
            do
            {
                s._device.Native.DevicePoll(s._device.Device, false, null);
                for (int i = 0; i < tokens.Count; i++)
                    if (GpuTimingCallbacks.ContainsKey(tokens[i]) || leases[i].MapToken != 0
                        || leases[i].TryGetCompletion(out _, out _))
                        throw new InvalidOperationException("A late GPU timestamp callback revived a retired lease.");
                Thread.Sleep(1);
            } while (deadline.Elapsed < TimeSpan.FromMilliseconds(100));
            s._device.ThrowIfFailed();
            Console.WriteLine("[renderwindowcheck] GPU timestamps real clear/draw/present frames, identity, bounded asynchronous readback and pending-map retirement PASS");
        }
        finally
        {
            s.DisposeGpuTiming();
            GraphicsApi.UseProgram(previousProgram);
            GraphicsApi.BindFramebuffer(FramebufferTarget.DrawFramebuffer, previousFramebuffer);
            GraphicsApi.PopAttrib();
        }
    }

    private static void DrawGpuTimingCheckFrame()
    {
        GraphicsApi.ClearColor(.1f, .2f, .3f, 1);
        GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
        GraphicsApi.Color4(1, .5f, .25f, 1);
        GraphicsApi.Begin(PrimitiveType.Triangles);
        GraphicsApi.Vertex3(-.75f, -.75f, 0);
        GraphicsApi.Vertex3(.75f, -.75f, 0);
        GraphicsApi.Vertex3(0, .75f, 0);
        GraphicsApi.End();
    }
}
#endif
