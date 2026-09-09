using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SDL;

namespace MphRead
{
    /// <summary>
    /// Device-local proof resources. The cache owns this object; all native
    /// pointers are released with the device that created them, and the
    /// upload is encoded once into the first command buffer that draws it.
    /// </summary>
    internal unsafe sealed class SdlGpuTriangleResources : IDisposable
    {
        private readonly SdlGpuDevice _device;
        private SDL_GPUShader* _vertexShader;
        private SDL_GPUShader* _fragmentShader;
        private SDL_GPUGraphicsPipeline* _pipeline;
        private SDL_GPUBuffer* _vertexBuffer;
        private SDL_GPUBuffer* _indexBuffer;
        private SDL_GPUTransferBuffer* _transferBuffer;
        private bool _uploaded;
        private bool _disposed;

        private SdlGpuTriangleResources(SdlGpuDevice device)
        {
            _device = device;
            ShaderArtifactManifest.ValidateFresh();
            (SDL_GPUShaderFormat format, string vertexPath, string fragmentPath) = SelectShaderArtifacts(device);
            _vertexShader = CreateShader(device.Handle, format, vertexPath, "main_vs", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX);
            try
            {
                _fragmentShader = CreateShader(device.Handle, format, fragmentPath, "main_ps", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT);
                _pipeline = CreatePipeline(device, _vertexShader, _fragmentShader);
                CreateBuffers(device);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static SdlGpuTriangleResources Create(SdlGpuDevice device)
            => new(device ?? throw new ArgumentNullException(nameof(device)));

        public void Encode(SDL_GPUCommandBuffer* commandBuffer, SDL_GPUTexture* target,
            uint width, uint height)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (commandBuffer == null || target == null || _pipeline == null
                || _vertexBuffer == null || _indexBuffer == null)
            {
                throw new InvalidOperationException("SDL triangle resources are incomplete.");
            }

            if (!_uploaded)
            {
                Upload(commandBuffer);
                _uploaded = true;
            }

            SDL_GPUColorTargetInfo targetInfo = new SDL_GPUColorTargetInfo
            {
                texture = target,
                mip_level = 0,
                layer_or_depth_plane = 0,
                clear_color = new SDL_FColor { r = 0.025f, g = 0.035f, b = 0.055f, a = 1f },
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = true
            };
            SDL_GPURenderPass* pass = SDL3.SDL_BeginGPURenderPass(commandBuffer, &targetInfo, 1, null);
            if (pass == null)
            {
                throw new InvalidOperationException($"SDL render pass creation failed: {SDL3.SDL_GetError()}");
            }

            SDL3.SDL_BindGPUGraphicsPipeline(pass, _pipeline);
            SDL_GPUBufferBinding vertexBinding = new SDL_GPUBufferBinding
            {
                buffer = _vertexBuffer,
                offset = 0
            };
            SDL3.SDL_BindGPUVertexBuffers(pass, 0, &vertexBinding, 1);
            SDL_GPUBufferBinding indexBinding = new SDL_GPUBufferBinding
            {
                buffer = _indexBuffer,
                offset = 0
            };
            SDL3.SDL_BindGPUIndexBuffer(pass, &indexBinding,
                SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
            SDL3.SDL_DrawGPUIndexedPrimitives(pass, 3, 1, 0, 0, 0);
            SDL3.SDL_EndGPURenderPass(pass);
        }

        private void Upload(SDL_GPUCommandBuffer* commandBuffer)
        {
            if (_transferBuffer == null) throw new InvalidOperationException("SDL triangle upload buffer is missing.");
            IntPtr mapped = SDL3.SDL_MapGPUTransferBuffer(_device.Handle, _transferBuffer, false);
            if (mapped == IntPtr.Zero)
            {
                throw new InvalidOperationException($"SDL triangle upload mapping failed: {SDL3.SDL_GetError()}");
            }
            try
            {
                float[] vertices =
                {
                    -0.65f, -0.55f, 0f, 1f, 0.18f, 0.12f, 1f,
                     0.65f, -0.55f, 0f, 0.12f, 1f, 0.2f, 1f,
                     0f,  0.65f, 0f, 0.18f, 0.4f, 1f, 1f
                };
                int[] indices = { 0, 1, 2 };
                Marshal.Copy(vertices, 0, mapped, vertices.Length);
                Marshal.Copy(indices, 0, mapped + vertices.Length * sizeof(float), indices.Length);
            }
            finally
            {
                SDL3.SDL_UnmapGPUTransferBuffer(_device.Handle, _transferBuffer);
            }

            SDL_GPUCopyPass* copyPass = SDL3.SDL_BeginGPUCopyPass(commandBuffer);
            if (copyPass == null)
            {
                throw new InvalidOperationException($"SDL triangle copy pass creation failed: {SDL3.SDL_GetError()}");
            }
            SDL_GPUTransferBufferLocation vertexSource = new SDL_GPUTransferBufferLocation
            {
                transfer_buffer = _transferBuffer,
                offset = 0
            };
            SDL_GPUBufferRegion vertexDestination = new SDL_GPUBufferRegion
            {
                buffer = _vertexBuffer,
                offset = 0,
                size = 3 * 7 * sizeof(float)
            };
            SDL3.SDL_UploadToGPUBuffer(copyPass, &vertexSource, &vertexDestination, false);
            SDL_GPUTransferBufferLocation indexSource = new SDL_GPUTransferBufferLocation
            {
                transfer_buffer = _transferBuffer,
                offset = vertexDestination.size
            };
            SDL_GPUBufferRegion indexDestination = new SDL_GPUBufferRegion
            {
                buffer = _indexBuffer,
                offset = 0,
                size = 3 * sizeof(uint)
            };
            SDL3.SDL_UploadToGPUBuffer(copyPass, &indexSource, &indexDestination, false);
            SDL3.SDL_EndGPUCopyPass(copyPass);
        }

        private static SDL_GPUShader* CreateShader(SDL_GPUDevice* device, SDL_GPUShaderFormat format,
            string path, string entrypoint, SDL_GPUShaderStage stage)
        {
            byte[] code = File.ReadAllBytes(path);
            byte[] name = Encoding.UTF8.GetBytes(entrypoint + "\0");
            fixed (byte* codePtr = code)
            fixed (byte* namePtr = name)
            {
                SDL_GPUShaderCreateInfo info = new SDL_GPUShaderCreateInfo
                {
                    code_size = (UIntPtr)code.Length,
                    code = codePtr,
                    entrypoint = namePtr,
                    format = format,
                    stage = stage,
                    num_samplers = 0,
                    num_storage_textures = 0,
                    num_storage_buffers = 0,
                    num_uniform_buffers = 0
                };
                SDL_GPUShader* shader = SDL3.SDL_CreateGPUShader(device, &info);
                if (shader == null)
                {
                    throw new InvalidOperationException($"SDL shader creation failed for {path}: {SDL3.SDL_GetError()}");
                }
                return shader;
            }
        }

        private static SDL_GPUGraphicsPipeline* CreatePipeline(SdlGpuDevice device,
            SDL_GPUShader* vertexShader, SDL_GPUShader* fragmentShader)
        {
            SDL_GPUVertexBufferDescription vertexDescription = new SDL_GPUVertexBufferDescription
            {
                slot = 0,
                pitch = 7 * sizeof(float),
                input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX,
                instance_step_rate = 0
            };
            SDL_GPUVertexAttribute* attributes = stackalloc SDL_GPUVertexAttribute[2];
            attributes[0] = new SDL_GPUVertexAttribute
            {
                location = 0,
                buffer_slot = 0,
                format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3,
                offset = 0
            };
            attributes[1] = new SDL_GPUVertexAttribute
            {
                location = 1,
                buffer_slot = 0,
                format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4,
                offset = 3 * sizeof(float)
            };
            SDL_GPUVertexInputState vertexInput = new SDL_GPUVertexInputState
            {
                vertex_buffer_descriptions = &vertexDescription,
                num_vertex_buffers = 1,
                vertex_attributes = attributes,
                num_vertex_attributes = 2
            };
            SDL_GPUColorTargetDescription colorTarget = new SDL_GPUColorTargetDescription
            {
                format = device.SwapchainFormat,
                blend_state = new SDL_GPUColorTargetBlendState
                {
                    src_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    dst_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
                    color_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    src_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    dst_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
                    alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    color_write_mask = SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_R
                        | SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_G
                        | SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_B
                        | SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_A,
                    enable_blend = false,
                    enable_color_write_mask = false
                }
            };
            SDL_GPUGraphicsPipelineCreateInfo info = new SDL_GPUGraphicsPipelineCreateInfo
            {
                vertex_shader = vertexShader,
                fragment_shader = fragmentShader,
                vertex_input_state = vertexInput,
                primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
                rasterizer_state = new SDL_GPURasterizerState
                {
                    fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                    cull_mode = SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE,
                    front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE,
                    enable_depth_clip = true
                },
                multisample_state = new SDL_GPUMultisampleState
                {
                    sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1,
                    // SDL requires zero here for the default mask.
                    sample_mask = 0
                },
                depth_stencil_state = default,
                target_info = new SDL_GPUGraphicsPipelineTargetInfo
                {
                    color_target_descriptions = &colorTarget,
                    num_color_targets = 1,
                    depth_stencil_format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID,
                    has_depth_stencil_target = false
                }
            };
            SDL_GPUGraphicsPipeline* pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(device.Handle, &info);
            if (pipeline == null)
            {
                throw new InvalidOperationException($"SDL triangle pipeline creation failed: {SDL3.SDL_GetError()}");
            }
            return pipeline;
        }

        private void CreateBuffers(SdlGpuDevice device)
        {
            SDL_GPUBufferCreateInfo vertexInfo = new SDL_GPUBufferCreateInfo
            {
                usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX,
                size = 3 * 7 * sizeof(float)
            };
            _vertexBuffer = SDL3.SDL_CreateGPUBuffer(device.Handle, &vertexInfo);
            SDL_GPUBufferCreateInfo indexInfo = new SDL_GPUBufferCreateInfo
            {
                usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX,
                size = 3 * sizeof(uint)
            };
            _indexBuffer = SDL3.SDL_CreateGPUBuffer(device.Handle, &indexInfo);
            SDL_GPUTransferBufferCreateInfo transferInfo = new SDL_GPUTransferBufferCreateInfo
            {
                usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                size = 3 * 7 * sizeof(float) + 3 * sizeof(uint)
            };
            _transferBuffer = SDL3.SDL_CreateGPUTransferBuffer(device.Handle, &transferInfo);
            if (_vertexBuffer == null || _indexBuffer == null || _transferBuffer == null)
            {
                throw new InvalidOperationException($"SDL triangle buffer creation failed: {SDL3.SDL_GetError()}");
            }
        }

        private static (SDL_GPUShaderFormat Format, string Vertex, string Fragment) SelectShaderArtifacts(SdlGpuDevice device)
        {
            string directory = ShaderArtifactManifest.Directory;
            // SDL's D3D12 backend consumes DXIL directly. Prefer it whenever
            // the device advertises that format; MSL is selected for Metal,
            // and SPIR-V remains the Vulkan fallback.
            if ((device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL) != 0)
            {
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL,
                    Path.Combine(directory, "scene_triangle.vert.dxil"),
                    Path.Combine(directory, "scene_triangle.frag.dxil"));
            }
            if ((device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL) != 0)
            {
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL,
                    Path.Combine(directory, "scene_triangle.vert.msl"),
                    Path.Combine(directory, "scene_triangle.frag.msl"));
            }
            if ((device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV) != 0)
            {
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV,
                    Path.Combine(directory, "scene_triangle.vert.spv"),
                    Path.Combine(directory, "scene_triangle.frag.spv"));
            }
            throw new PlatformNotSupportedException($"SDL GPU shader formats {SdlGpuDevice.DescribeShaderFormats(device.ShaderFormats)} have no checked-in proof artifact.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SDL3.SDL_WaitForGPUIdle(_device.Handle);
            if (_transferBuffer != null) SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, _transferBuffer);
            if (_indexBuffer != null) SDL3.SDL_ReleaseGPUBuffer(_device.Handle, _indexBuffer);
            if (_vertexBuffer != null) SDL3.SDL_ReleaseGPUBuffer(_device.Handle, _vertexBuffer);
            if (_pipeline != null) SDL3.SDL_ReleaseGPUGraphicsPipeline(_device.Handle, _pipeline);
            if (_fragmentShader != null) SDL3.SDL_ReleaseGPUShader(_device.Handle, _fragmentShader);
            if (_vertexShader != null) SDL3.SDL_ReleaseGPUShader(_device.Handle, _vertexShader);
            _transferBuffer = null;
            _indexBuffer = null;
            _vertexBuffer = null;
            _pipeline = null;
            _fragmentShader = null;
            _vertexShader = null;
        }
    }

    internal static class ShaderArtifactManifest
    {
        public static string Directory
        {
            get
            {
                string published = Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders", "Generated");
                if (File.Exists(Path.Combine(published, "manifest.json"))) return published;
                string working = Path.Combine(Environment.CurrentDirectory, "src", "Client", "Rendering", "Shaders", "Generated");
                if (File.Exists(Path.Combine(working, "manifest.json"))) return working;
                throw new FileNotFoundException("Published SDL renderer shader manifest is missing.", Path.Combine(published, "manifest.json"));
            }
        }

        public static void ValidateFresh()
        {
            ValidateFresh("scene_triangle.hlsl", "manifest.json");
        }

        public static void ValidateSceneFresh()
        {
            ValidateFresh("scene.hlsl", "scene_manifest.json");
        }

        public static void ValidatePostFresh()
        {
            ValidateFresh("fullscreen.hlsl", "fullscreen_manifest.json");
            ValidateFresh("hud.hlsl", "hud_manifest.json");
            ValidateFresh("disruption.hlsl", "disruption_manifest.json");
            ValidateFresh("cel.hlsl", "cel_manifest.json");
            ValidateFresh("bloom.hlsl", "bloom_manifest.json");
        }

        private static void ValidateFresh(string sourceName, string manifestName)
        {
            string directory = Directory;
            string manifestPath = Path.Combine(directory, manifestName);
            string sourcePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(directory))!, sourceName);
            string manifest = File.ReadAllText(manifestPath);
            string sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))).ToLowerInvariant();
            if (!manifest.Contains($"\"source_sha256\": \"{sourceHash}\"", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"SDL renderer shader manifest is stale for {sourceName}.");
            }
        }
    }
}
