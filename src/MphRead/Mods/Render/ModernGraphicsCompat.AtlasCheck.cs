#if !ANDROID && !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    // Native integration gate: two geometries share one page; partial release
    // must preserve offsets and final release must reclaim the two buffers.
    // A draw is encoded before final release, then submitted/read back after it.
    internal static void VerifyRetainedAtlasLifetimeForCheck()
    {
        var s = Current;
        s.FlushCommands();
        if (s._retainedMultiDrawPages.Count != 0 || s._retainedMultiDrawEntries.Count != 0)
            throw new InvalidOperationException("Atlas lifetime check requires an empty retained atlas.");
        var original = LiveResources;
        int texture = 0, framebuffer = 0;
        ShaderModule* shader = null;
        RenderPipeline* pipeline = null;
        int drawFramebuffer = s._resources.DrawFramebuffer, readFramebuffer = s._resources.ReadFramebuffer;
        int activeTextureUnit = s._resources.ActiveTextureUnit;
        int oldTexture = s._resources.BoundTexture(0);
        try
        {
            texture = GenTexture();
            ActiveTexture(TextureUnit.Texture0);
            BindTexture(TextureTarget.Texture2D, texture);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 4, 4, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            NativeTexture target = s.EnsureTexture(texture, allowProgressive: false);
            framebuffer = GenFramebuffer();
            BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, texture, 0);
            pipeline = s.CreateAtlasCheckPipeline(target.Format, out shader);
            var before = LiveResources;
            for (int cycle = 0; cycle < 16; cycle++)
            {
                var a = AtlasCheckGeometry();
                var b = AtlasCheckGeometry();
                var first = s.EnsureRetainedMultiDrawEntry(a);
                var second = s.EnsureRetainedMultiDrawEntry(b);
                var allocated = LiveResources;
                if (!ReferenceEquals(first.Page, second.Page)
                    || allocated.Buffers != before.Buffers + 2
                    || s._retainedMultiDrawPages.Count != 1
                    || first.Page.Ownership.LiveAllocations != 2
                    || s.RetainedAtlasReservedBytes != (long)(RetainedMultiDrawVertexPageBytes + RetainedMultiDrawIndexPageBytes)
                    || s.RetainedAtlasLiveBytes != (long)(first.AllocationBytes + second.AllocationBytes))
                    throw new InvalidOperationException("Two retained geometries did not share the expected bounded atlas page.");

                s.ReleaseRetainedMultiDrawGeometry(a);
                var survivor = s.EnsureRetainedMultiDrawEntry(b);
                if (survivor != second || second.Page.Vertex == null || second.Page.Index == null
                    || LiveResources.Buffers != allocated.Buffers || s._retainedMultiDrawPages.Count != 1
                    || second.Page.Ownership.LiveAllocations != 1
                    || s.RetainedAtlasLiveBytes != (long)second.AllocationBytes)
                    throw new InvalidOperationException("Partial atlas release moved offsets, grew the page or released live buffers.");

                var color = new RenderPassColorAttachment
                { View = target.View, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(0, 0, 0, 1) };
                var passDescriptor = new RenderPassDescriptor { ColorAttachments = &color, ColorAttachmentCount = 1 };
                RenderPassEncoder* pass = s._api.CommandEncoderBeginRenderPass(s.BeginCommands(), &passDescriptor);
                try
                {
                    s._api.RenderPassEncoderSetPipeline(pass, pipeline);
                    s._api.RenderPassEncoderSetVertexBuffer(pass, 0, second.Page.Vertex, 0, second.Page.VertexCapacityBytes);
                    s._api.RenderPassEncoderSetIndexBuffer(pass, second.Page.Index, IndexFormat.Uint32, 0, second.Page.IndexCapacityBytes);
                    s._api.RenderPassEncoderDrawIndexed(pass, second.IndexCount, 1, second.FirstIndex, second.BaseVertex, 0);
                    s._api.RenderPassEncoderEnd(pass);
                }
                finally { s._api.RenderPassEncoderRelease(pass); }

                s.ReleaseRetainedMultiDrawGeometry(b);
                s.ReleaseRetainedMultiDrawGeometry(b); // duplicate logical release is harmless
                if (s._retainedMultiDrawPages.Count != 0 || s._retainedMultiDrawEntries.Count != 0
                    || second.Page.Vertex != null || second.Page.Index != null
                    || s.RetainedAtlasReservedBytes != 0 || s.RetainedAtlasLiveBytes != 0
                    || LiveResources != before)
                    throw new InvalidOperationException("Final atlas owner did not restore native resource counts.");

                // ReadPixels submits the encoded draw after both managed atlas
                // handles were released. Native commands must retain those buffers.
                byte[] pixel = new byte[4];
                ReadPixels(2, 2, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
                s._device.ThrowIfFailed();
                if (pixel[0] > 2 || pixel[1] < 253 || pixel[2] > 2 || pixel[3] < 253)
                    throw new InvalidOperationException("Encoded atlas draw lost its buffers during final-owner release.");
            }
        }
        finally
        {
            try { s.FlushCommands(); }
            finally
            {
                s.DisposeRetainedMultiDraw();
                if (pipeline != null) s._api.RenderPipelineRelease(pipeline);
                if (shader != null) s._api.ShaderModuleRelease(shader);
                BindFramebuffer(FramebufferTarget.DrawFramebuffer, drawFramebuffer);
                BindFramebuffer(FramebufferTarget.ReadFramebuffer, readFramebuffer);
                if (framebuffer != 0) DeleteFramebuffer(framebuffer);
                if (texture != 0) DeleteTexture(texture);
                BindTexture(TextureTarget.Texture2D, oldTexture);
                ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + activeTextureUnit));
            }
        }
        if (LiveResources != original)
            throw new InvalidOperationException($"Atlas lifetime check leaked native resources: {original} -> {LiveResources}.");
        Console.WriteLine("[renderwindowcheck] 16-cycle shared atlas ownership, reclamation and encoded draw lifetime PASS");
    }

    private static GeometryList AtlasCheckGeometry()
    {
        var vertices = new float[LegacyGeometryBatch.FloatsPerVertex * 3];
        vertices[0] = -1; vertices[1] = -1;
        vertices[LegacyGeometryBatch.FloatsPerVertex] = 3;
        vertices[LegacyGeometryBatch.FloatsPerVertex + 1] = -1;
        vertices[LegacyGeometryBatch.FloatsPerVertex * 2] = -1;
        vertices[LegacyGeometryBatch.FloatsPerVertex * 2 + 1] = 3;
        return new GeometryList { Vertices = vertices, Triangles = new[] { 0, 1, 2 } };
    }

    private RenderPipeline* CreateAtlasCheckPipeline(WgpuTextureFormat format, out ShaderModule* shader)
    {
        const string source = "@vertex fn vs_main(@location(0) position: vec3<f32>) -> @builtin(position) vec4<f32> { return vec4<f32>(position, 1.0); }\n"
            + "@fragment fn fs_main() -> @location(0) vec4<f32> { return vec4<f32>(0.0, 1.0, 0.0, 1.0); }";
        nint code = SilkMarshal.StringToPtr(source), vertexEntry = SilkMarshal.StringToPtr("vs_main"), fragmentEntry = SilkMarshal.StringToPtr("fs_main");
        shader = null;
        try
        {
            var wgsl = new ShaderModuleWGSLDescriptor
            { Code = (byte*)code, Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor } };
            var shaderDescriptor = new ShaderModuleDescriptor { NextInChain = (ChainedStruct*)&wgsl };
            shader = _api.DeviceCreateShaderModule(_device.Device, &shaderDescriptor);
            if (shader == null) throw new InvalidOperationException("Atlas lifetime fixture shader creation failed.");
            var attribute = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
            var layout = new VertexBufferLayout
            { ArrayStride = LegacyGeometryBatch.FloatsPerVertex * sizeof(float), AttributeCount = 1, Attributes = &attribute, StepMode = VertexStepMode.Vertex };
            var target = new ColorTargetState { Format = format, WriteMask = ColorWriteMask.All };
            var fragment = new FragmentState { Module = shader, EntryPoint = (byte*)fragmentEntry, TargetCount = 1, Targets = &target };
            var descriptor = new RenderPipelineDescriptor
            {
                Vertex = new VertexState { Module = shader, EntryPoint = (byte*)vertexEntry, BufferCount = 1, Buffers = &layout },
                Fragment = &fragment,
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                Multisample = new MultisampleState { Count = 1, Mask = ~0u }
            };
            RenderPipeline* pipeline = _api.DeviceCreateRenderPipeline(_device.Device, &descriptor);
            if (pipeline == null) throw new InvalidOperationException("Atlas lifetime fixture pipeline creation failed.");
            return pipeline;
        }
        catch { if (shader != null) _api.ShaderModuleRelease(shader); shader = null; throw; }
        finally { SilkMarshal.Free(code); SilkMarshal.Free(vertexEntry); SilkMarshal.Free(fragmentEntry); }
    }
}
#endif
