#if !MPHREAD_SERVER
using System;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using WgpuColor = Silk.NET.WebGPU.Color;
using WgpuTexture = Silk.NET.WebGPU.Texture;
using WgpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Small offscreen proof that a selected modern backend can execute the
    /// same explicit geometry the compatibility renderer produces.
    /// </summary>
    internal static unsafe class ModernGraphicsRenderCheck
    {
        private const uint Width = 64;
        private const uint Height = 64;
        private const uint BytesPerPixel = 4;
        private const uint BytesPerRow = 256; // WebGPU copy rows must be 256-byte aligned.
        private const string ShaderSource = @"
struct VsIn {
    @location(0) position: vec3<f32>,
    @location(1) color: vec4<f32>,
};

struct VsOut {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
};

@vertex
fn vs_main(input: VsIn) -> VsOut {
    var output: VsOut;
    output.position = vec4<f32>(input.position, 1.0);
    output.color = input.color;
    return output;
}

@fragment
fn fs_main(input: VsOut) -> @location(0) vec4<f32> {
    return input.color;
}";

        private static BufferMapAsyncStatus _mapStatus = BufferMapAsyncStatus.Unknown;

        internal static string Run(ModernGraphicsDevice device)
        {
            WebGPU api = device.Api;
            Device* gpu = device.Device;
            ShaderModule* shader = null;
            RenderPipeline* pipeline = null;
            WgpuBuffer* vertexBuffer = null;
            WgpuBuffer* indexBuffer = null;
            WgpuBuffer* readback = null;
            WgpuTexture* texture = null;
            TextureView* view = null;
            CommandEncoder* encoder = null;
            RenderPassEncoder* pass = null;
            CommandBuffer* commands = null;
            Queue* queue = null;

            nint shaderPtr = 0;
            nint vsPtr = 0;
            nint fsPtr = 0;

            try
            {
                var batch = new LegacyGeometryBatch();
                AddQuad(batch);
                float[] vertices = batch.Vertices.ToArray();
                int[] indices = batch.BuildIndexArray();
                if (indices.Length != 6)
                {
                    throw new InvalidOperationException("Shared legacy geometry did not produce a six-index quad.");
                }

                shaderPtr = SilkMarshal.StringToPtr(ShaderSource);
                var wgsl = new ShaderModuleWGSLDescriptor
                {
                    Code = (byte*)shaderPtr,
                    Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor }
                };
                var shaderDescriptor = new ShaderModuleDescriptor
                {
                    NextInChain = (ChainedStruct*)&wgsl
                };
                shader = api.DeviceCreateShaderModule(gpu, shaderDescriptor);
                if (shader == null) throw new InvalidOperationException("WebGPU shader creation failed.");

                var attributes = stackalloc VertexAttribute[2];
                attributes[0] = new VertexAttribute
                {
                    Format = VertexFormat.Float32x3,
                    Offset = 0,
                    ShaderLocation = 0
                };
                attributes[1] = new VertexAttribute
                {
                    Format = VertexFormat.Float32x4,
                    Offset = 3u * sizeof(float),
                    ShaderLocation = 1
                };
                var vertexLayout = new VertexBufferLayout
                {
                    Attributes = attributes,
                    AttributeCount = 2,
                    StepMode = VertexStepMode.Vertex,
                    ArrayStride = (ulong)(LegacyGeometryBatch.FloatsPerVertex * sizeof(float))
                };

                var colorTarget = new ColorTargetState
                {
                    Format = WgpuTextureFormat.Rgba8Unorm,
                    Blend = null,
                    WriteMask = ColorWriteMask.All
                };
                fsPtr = SilkMarshal.StringToPtr("fs_main");
                var fragment = new FragmentState
                {
                    Module = shader,
                    EntryPoint = (byte*)fsPtr,
                    TargetCount = 1,
                    Targets = &colorTarget
                };
                vsPtr = SilkMarshal.StringToPtr("vs_main");
                var pipelineDescriptor = new RenderPipelineDescriptor
                {
                    Vertex = new VertexState
                    {
                        Module = shader,
                        EntryPoint = (byte*)vsPtr,
                        Buffers = &vertexLayout,
                        BufferCount = 1
                    },
                    Primitive = new PrimitiveState
                    {
                        Topology = PrimitiveTopology.TriangleList,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        CullMode = CullMode.None
                    },
                    Multisample = new MultisampleState
                    {
                        Count = 1,
                        Mask = ~0u,
                        AlphaToCoverageEnabled = false
                    },
                    Fragment = &fragment,
                    DepthStencil = null
                };
                pipeline = api.DeviceCreateRenderPipeline(gpu, pipelineDescriptor);
                if (pipeline == null) throw new InvalidOperationException("WebGPU pipeline creation failed.");

                ulong vertexBytes = (ulong)(vertices.Length * sizeof(float));
                ulong indexBytes = (ulong)(indices.Length * sizeof(int));
                vertexBuffer = api.DeviceCreateBuffer(gpu, new BufferDescriptor
                {
                    Size = vertexBytes,
                    Usage = BufferUsage.Vertex | BufferUsage.CopyDst
                });
                indexBuffer = api.DeviceCreateBuffer(gpu, new BufferDescriptor
                {
                    Size = indexBytes,
                    Usage = BufferUsage.Index | BufferUsage.CopyDst
                });
                readback = api.DeviceCreateBuffer(gpu, new BufferDescriptor
                {
                    Size = BytesPerRow * Height,
                    Usage = BufferUsage.CopyDst | BufferUsage.MapRead
                });
                if (vertexBuffer == null || indexBuffer == null || readback == null)
                {
                    throw new InvalidOperationException("WebGPU buffer allocation failed.");
                }

                var textureDescriptor = new TextureDescriptor
                {
                    Size = new Extent3D(Width, Height, 1),
                    Format = WgpuTextureFormat.Rgba8Unorm,
                    Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
                    MipLevelCount = 1,
                    SampleCount = 1,
                    Dimension = TextureDimension.Dimension2D
                };
                texture = api.DeviceCreateTexture(gpu, textureDescriptor);
                if (texture == null) throw new InvalidOperationException("WebGPU render target allocation failed.");
                view = api.TextureCreateView(texture, null);
                if (view == null) throw new InvalidOperationException("WebGPU render-target view creation failed.");

                queue = api.DeviceGetQueue(gpu);
                fixed (float* vertexPtr = vertices)
                {
                    api.QueueWriteBuffer(queue, vertexBuffer, 0, vertexPtr, (nuint)vertexBytes);
                }
                fixed (int* indexPtr = indices)
                {
                    api.QueueWriteBuffer(queue, indexBuffer, 0, indexPtr, (nuint)indexBytes);
                }

                encoder = api.DeviceCreateCommandEncoder(gpu, new CommandEncoderDescriptor());
                if (encoder == null) throw new InvalidOperationException("WebGPU command encoder creation failed.");

                var attachment = new RenderPassColorAttachment
                {
                    DepthSlice = uint.MaxValue, // WGPU_DEPTH_SLICE_UNDEFINED: this is a 2D view.
                    View = view,
                    ResolveTarget = null,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearValue = new WgpuColor { R = 0, G = 0, B = 0, A = 1 }
                };
                var passDescriptor = new RenderPassDescriptor
                {
                    ColorAttachments = &attachment,
                    ColorAttachmentCount = 1,
                    DepthStencilAttachment = null
                };
                pass = api.CommandEncoderBeginRenderPass(encoder, passDescriptor);
                api.RenderPassEncoderSetPipeline(pass, pipeline);
                api.RenderPassEncoderSetVertexBuffer(pass, 0, vertexBuffer, 0, vertexBytes);
                api.RenderPassEncoderSetIndexBuffer(pass, indexBuffer, IndexFormat.Uint32, 0, indexBytes);
                api.RenderPassEncoderDrawIndexed(pass, (uint)indices.Length, 1, 0, 0, 0);
                api.RenderPassEncoderEnd(pass);

                var source = new ImageCopyTexture
                {
                    Texture = texture,
                    MipLevel = 0,
                    Aspect = TextureAspect.All
                };
                var destination = new ImageCopyBuffer
                {
                    Buffer = readback,
                    Layout = new TextureDataLayout
                    {
                        Offset = 0,
                        BytesPerRow = BytesPerRow,
                        RowsPerImage = Height
                    }
                };
                var extent = new Extent3D(Width, Height, 1);
                api.CommandEncoderCopyTextureToBuffer(encoder, &source, &destination, &extent);

                commands = api.CommandEncoderFinish(encoder, new CommandBufferDescriptor());
                if (commands == null) throw new InvalidOperationException("WebGPU command buffer creation failed.");
                api.QueueSubmit(queue, 1, &commands);

                _mapStatus = BufferMapAsyncStatus.Unknown;
                api.BufferMapAsync(readback, MapMode.Read, 0, (nuint)(BytesPerRow * Height),
                    new PfnBufferMapCallback((status, _) => _mapStatus = status), null);
                device.Native.DevicePoll(gpu, true, null);
                if (_mapStatus != BufferMapAsyncStatus.Success)
                {
                    throw new InvalidOperationException($"WebGPU readback map failed: {_mapStatus}.");
                }

                byte* pixels = (byte*)api.BufferGetConstMappedRange(readback, 0, (nuint)(BytesPerRow * Height));
                if (pixels == null) throw new InvalidOperationException("WebGPU returned a null mapped range.");

                // Do not hinge the backend smoke test on one exact sample. The old
                // center pixel lands on the quad's 0->2 triangle diagonal, where
                // Metal and Vulkan/MoltenVK can make different (but valid) edge
                // ownership/sample decisions. Sample a small interior block instead:
                // a real failed draw remains black, while rasterization convention
                // differences cannot create a false negative.
                const uint SampleRadius = 2;
                uint sampleCount = 0;
                uint redCount = 0;
                byte firstR = 0;
                byte firstG = 0;
                byte firstB = 0;
                byte firstA = 0;
                bool capturedFirst = false;
                for (uint y = Height / 2 - SampleRadius; y <= Height / 2 + SampleRadius; y++)
                {
                    for (uint x = Width / 2 - SampleRadius; x <= Width / 2 + SampleRadius; x++)
                    {
                        uint offset = y * BytesPerRow + x * BytesPerPixel;
                        byte r = pixels[offset + 0];
                        byte g = pixels[offset + 1];
                        byte b = pixels[offset + 2];
                        byte a = pixels[offset + 3];
                        if (!capturedFirst)
                        {
                            firstR = r;
                            firstG = g;
                            firstB = b;
                            firstA = a;
                            capturedFirst = true;
                        }
                        sampleCount++;
                        if (r >= 220 && g <= 24 && b <= 24 && a >= 220)
                        {
                            redCount++;
                        }
                    }
                }
                api.BufferUnmap(readback);

                // The 5x5 block is well inside the +/-0.65 NDC quad. Requiring
                // most samples to be red still detects missing draws/copies while
                // tolerating a backend-specific shared-edge sample or two.
                if (redCount < 20)
                {
                    throw new InvalidOperationException(
                        $"WebGPU offscreen draw read back only {redCount}/{sampleCount} expected red interior pixels; "
                        + $"first sample rgba({firstR},{firstG},{firstB},{firstA}).");
                }

                return $"offscreen=red({redCount}/{sampleCount}) indices={indices.Length}";
            }
            finally
            {
                if (commands != null) api.CommandBufferRelease(commands);
                if (pass != null) api.RenderPassEncoderRelease(pass);
                if (encoder != null) api.CommandEncoderRelease(encoder);
                if (view != null) api.TextureViewRelease(view);
                if (texture != null) api.TextureRelease(texture);
                if (readback != null) api.BufferRelease(readback);
                if (indexBuffer != null) api.BufferRelease(indexBuffer);
                if (vertexBuffer != null) api.BufferRelease(vertexBuffer);
                if (pipeline != null) api.RenderPipelineRelease(pipeline);
                if (shader != null) api.ShaderModuleRelease(shader);
                if (queue != null) api.QueueRelease(queue);
                if (vsPtr != 0) SilkMarshal.Free(vsPtr);
                if (fsPtr != 0) SilkMarshal.Free(fsPtr);
                if (shaderPtr != 0) SilkMarshal.Free(shaderPtr);
            }
        }

        private static void AddQuad(LegacyGeometryBatch batch)
        {
            var red = new OpenTK.Mathematics.Vector4(1, 0, 0, 1);
            var normal = OpenTK.Mathematics.Vector3.UnitZ;
            var uv = OpenTK.Mathematics.Vector3.Zero;
            batch.Begin(OpenTK.Graphics.OpenGL.PrimitiveType.Quads);
            batch.AddVertex(new OpenTK.Mathematics.Vector3(-0.65f, -0.65f, 0), red, normal, uv, true);
            batch.AddVertex(new OpenTK.Mathematics.Vector3( 0.65f, -0.65f, 0), red, normal, uv, true);
            batch.AddVertex(new OpenTK.Mathematics.Vector3( 0.65f,  0.65f, 0), red, normal, uv, true);
            batch.AddVertex(new OpenTK.Mathematics.Vector3(-0.65f,  0.65f, 0), red, normal, uv, true);
            batch.End();
        }
    }
}
#endif
