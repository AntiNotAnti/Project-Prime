#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using MphRead;
using OpenTK.Mathematics;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using WgpuTexture = Silk.NET.WebGPU.Texture;
using WgpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    // The CPU portal walk remains authoritative. This compute stage receives
    // only room packets that already survived portal/frustum traversal and may
    // conservatively reject more work with current-frustum and temporal Hi-Z.
    private const int GpuVisibilityCandidateWords = 12; // 48 bytes
    private const int GpuVisibilityUniformWords = 72;   // 288 bytes
    private const float GpuVisibilityDepthBias = 0.0025f;
    private const float GpuVisibilityMotionPixels = 2.0f;
    private const float GpuVisibilityExtentPixels = 4.0f;

    private readonly Dictionary<RenderItem, int> _gpuVisibilitySlots = new();
    private uint[] _gpuVisibilityCandidateWords = Array.Empty<uint>();
    private readonly uint[] _gpuVisibilityUniformWords =
        new uint[GpuVisibilityUniformWords];
    private WgpuBuffer* _gpuVisibilityCandidateBuffer;
    private ulong _gpuVisibilityCandidateCapacity;
    private WgpuBuffer* _gpuVisibilityIndirectBuffer;
    private ulong _gpuVisibilityIndirectCapacity;
    private WgpuBuffer* _gpuVisibilityCompactBuffer;
    private ulong _gpuVisibilityCompactCapacity;
    private WgpuBuffer* _gpuVisibilityCountersBuffer;
    private ulong _gpuVisibilityCountersCapacity;
    private WgpuBuffer* _gpuVisibilityUniformBuffer;
    private ulong _gpuVisibilityUniformCapacity;

    private ComputePipeline* _gpuVisibilityPipeline;
    private BindGroupLayout* _gpuVisibilityLayout;
    private nint _gpuVisibilityBindGroup;
    private nint[] _gpuVisibilityBindResources = Array.Empty<nint>();

    private ComputePipeline* _gpuHiZDepthPipeline;
    private BindGroupLayout* _gpuHiZDepthLayout;
    private ComputePipeline* _gpuHiZReducePipeline;
    private BindGroupLayout* _gpuHiZReduceLayout;
    private WgpuTexture* _gpuHiZTexture;
    private nint _gpuHiZFullView;
    private readonly List<nint> _gpuHiZMipViews = new();
    private nint _gpuHiZDepthBindGroup;
    private nint _gpuHiZDepthSourceView;
    private readonly List<nint> _gpuHiZReduceBindGroups = new();
    private int _gpuHiZWidth;
    private int _gpuHiZHeight;
    private int _gpuHiZMipCount;

    private Matrix4 _gpuVisibilityPreviousProjection = Matrix4.Identity;
    private Matrix4 _gpuVisibilityPreviousView = Matrix4.Identity;
    private bool _gpuVisibilityPreviousMatricesValid;
    private int _gpuVisibilityPreviousDepthTexture;
    private int _gpuVisibilityPreviousWidth;
    private int _gpuVisibilityPreviousHeight;
    private bool _gpuVisibilityPrepared;
    private bool _gpuVisibilityRefused;
    private long _gpuVisibilityIndirectDraws;

    private long _gpuVisibilityDispatches;
    private long _gpuVisibilityCandidates;
    private long _gpuVisibilityHiZBuilds;
    private int _gpuVisibilityCandidateHighWater;

    internal static bool GpuVisibilityEnabled =>
        _current?.UseGpuVisibility ?? false;
    internal static long GpuVisibilityDispatches =>
        _current?._gpuVisibilityDispatches ?? 0;
    internal static long GpuVisibilityCandidates =>
        _current?._gpuVisibilityCandidates ?? 0;
    internal static long GpuVisibilityHiZBuilds =>
        _current?._gpuVisibilityHiZBuilds ?? 0;
    internal static int GpuVisibilityCandidateHighWater =>
        _current?._gpuVisibilityCandidateHighWater ?? 0;
    internal static long GpuVisibilityIndirectDraws =>
        _current?._gpuVisibilityIndirectDraws ?? 0;
    internal static bool GpuVisibilityRefused =>
        _current?._gpuVisibilityRefused ?? false;

    private bool UseGpuVisibility
    {
        get
        {
#if ANDROID
            // Temporal Hi-Z forces readable depth and extra bandwidth on a tile
            // renderer. Keep Android on the CPU portal path until physical-device
            // benchmarks show this wins there.
            return false;
#else
            // This stage consumes the indirect argument buffer directly. Metal
            // stays on direct draws until its indirect path clears the benchmark.
            return !_gpuVisibilityRefused
                && UseRetainedIndirectDraws
                && (_device.Backend is GraphicsBackend.DirectX12
                    or GraphicsBackend.Vulkan);
#endif
        }
    }

    internal static void PrepareRetainedGpuVisibility(
        IReadOnlyList<RetainedDrawPacket> packets,
        Matrix4 projection, Matrix4 view,
        int depthTexture, int width, int height, bool historyValid)
    {
        if (_current == null)
            return;
        Current.PrepareRetainedGpuVisibilityCore(
            packets, projection, view,
            depthTexture, width, height, historyValid);
    }

    internal static bool CaptureRetainedGpuVisibilityHistory(
        Matrix4 projection, Matrix4 view,
        int depthTexture, int width, int height)
    {
        if (_current == null)
            return false;
        return Current.CaptureRetainedGpuVisibilityHistoryCore(
            projection, view, depthTexture, width, height);
    }

    private void PrepareRetainedGpuVisibilityCore(
        IReadOnlyList<RetainedDrawPacket> packets,
        Matrix4 projection, Matrix4 view,
        int depthTexture, int width, int height, bool historyValid)
    {
        try
        {
            PrepareRetainedGpuVisibilityUnsafe(
                packets, projection, view,
                depthTexture, width, height, historyValid);
        }
        catch (Exception ex) when (
            ex is not OutOfMemoryException and not StackOverflowException)
        {
            _gpuVisibilityRefused = true;
            _gpuVisibilityPrepared = false;
            _gpuVisibilitySlots.Clear();
            Console.WriteLine(
                $"[render] GPU visibility unavailable; using retained CPU visibility: {ex.Message}");
        }
    }

    private void PrepareRetainedGpuVisibilityUnsafe(
        IReadOnlyList<RetainedDrawPacket> packets,
        Matrix4 projection, Matrix4 view,
        int depthTexture, int width, int height, bool historyValid)
    {
        _gpuVisibilitySlots.Clear();
        _gpuVisibilityPrepared = false;
        if (!UseGpuVisibility || width <= 0 || height <= 0)
            return;

        EndActiveCorePass();
        EnsureGpuVisibilityPipelines();

        int candidateCount = 0;
        for (int i = 0; i < packets.Count; i++)
        {
            RetainedDrawPacket packet = packets[i];
            RenderItem item = packet.Item;
            if (!packet.ReorderableOpaque
                || !item.RetainedGpuVisibilityEligible
                || !RetainedWorldPacketEligibleForPass(
                    item, WorldRenderPassKind.Opaque)
                || !ValidGpuBounds(item.RetainedBoundsMin, item.RetainedBoundsMax)
                || !_lists.TryGetValue(packet.Mesh.ListId, out GeometryList? geometry)
                || geometry.Triangles.Length == 0
                || geometry.Lines.Length != 0)
            {
                continue;
            }
            candidateCount++;
        }

        if (candidateCount == 0)
            return;

        _gpuVisibilityCandidateHighWater = Math.Max(
            _gpuVisibilityCandidateHighWater, candidateCount);
        int requiredWords = checked(candidateCount * GpuVisibilityCandidateWords);
        if (_gpuVisibilityCandidateWords.Length < requiredWords)
        {
            int capacity = Math.Max(requiredWords,
                Math.Max(256, _gpuVisibilityCandidateWords.Length * 2));
            _gpuVisibilityCandidateWords = new uint[capacity];
        }

        int candidate = 0;
        for (int i = 0; i < packets.Count; i++)
        {
            RetainedDrawPacket packet = packets[i];
            RenderItem item = packet.Item;
            if (!packet.ReorderableOpaque
                || !item.RetainedGpuVisibilityEligible
                || !RetainedWorldPacketEligibleForPass(
                    item, WorldRenderPassKind.Opaque)
                || !ValidGpuBounds(item.RetainedBoundsMin, item.RetainedBoundsMax)
                || !_lists.TryGetValue(packet.Mesh.ListId, out GeometryList? geometry)
                || geometry.Triangles.Length == 0
                || geometry.Lines.Length != 0)
            {
                continue;
            }

            int at = candidate * GpuVisibilityCandidateWords;
            WriteGpuVec3(_gpuVisibilityCandidateWords, at,
                item.RetainedBoundsMin);
            WriteGpuVec3(_gpuVisibilityCandidateWords, at + 4,
                item.RetainedBoundsMax);
            _gpuVisibilityCandidateWords[at + 8] =
                checked((uint)geometry.Triangles.Length);
            _gpuVisibilityCandidateWords[at + 9] =
                checked((uint)packet.Sequence);
            _gpuVisibilityCandidateWords[at + 10] = 0;
            _gpuVisibilityCandidateWords[at + 11] = 0;
            _gpuVisibilitySlots[item] = candidate;
            candidate++;
        }

        ulong candidateBytes = checked(
            (ulong)candidateCount * GpuVisibilityCandidateWords * sizeof(uint));
        ulong indirectBytes = checked(
            (ulong)candidateCount * RetainedIndexedIndirectBytes);
        ulong compactBytes = checked((ulong)candidateCount * sizeof(uint));
        GrowBuffer(ref _gpuVisibilityCandidateBuffer,
            ref _gpuVisibilityCandidateCapacity,
            candidateBytes, BufferUsage.Storage);
        GrowBuffer(ref _gpuVisibilityIndirectBuffer,
            ref _gpuVisibilityIndirectCapacity,
            indirectBytes, BufferUsage.Storage | BufferUsage.Indirect);
        GrowBuffer(ref _gpuVisibilityCompactBuffer,
            ref _gpuVisibilityCompactCapacity,
            compactBytes, BufferUsage.Storage | BufferUsage.CopySrc);
        GrowBuffer(ref _gpuVisibilityCountersBuffer,
            ref _gpuVisibilityCountersCapacity,
            16, BufferUsage.Storage | BufferUsage.CopySrc);
        GrowBuffer(ref _gpuVisibilityUniformBuffer,
            ref _gpuVisibilityUniformCapacity,
            GpuVisibilityUniformWords * sizeof(uint), BufferUsage.Uniform);

        fixed (uint* candidatePtr = _gpuVisibilityCandidateWords)
        {
            WriteProfiledBuffer(_gpuVisibilityCandidateBuffer, 0,
                candidatePtr, checked((nuint)candidateBytes));
        }
        uint* counters = stackalloc uint[4];
        counters[0] = counters[1] = counters[2] = counters[3] = 0;
        WriteProfiledBuffer(_gpuVisibilityCountersBuffer, 0,
            counters, checked((nuint)(4 * sizeof(uint))));

        bool useHistory = historyValid
            && _gpuVisibilityPreviousMatricesValid
            && _gpuHiZFullView != 0
            && depthTexture != 0
            && depthTexture == _gpuVisibilityPreviousDepthTexture
            && width == _gpuVisibilityPreviousWidth
            && height == _gpuVisibilityPreviousHeight
            && width == _gpuHiZWidth
            && height == _gpuHiZHeight;

        TextureView* hiZView = useHistory
            ? (TextureView*)_gpuHiZFullView
            : _whiteView;

        uint[] uniforms = _gpuVisibilityUniformWords;
        WriteGpuMatrix(uniforms, 0, projection);
        WriteGpuMatrix(uniforms, 16, view);
        WriteGpuMatrix(uniforms, 32, _gpuVisibilityPreviousProjection);
        WriteGpuMatrix(uniforms, 48, _gpuVisibilityPreviousView);
        WriteGpuFloat(uniforms, 64, width);
        WriteGpuFloat(uniforms, 65, height);
        WriteGpuFloat(uniforms, 66, useHistory ? _gpuHiZMipCount : 1);
        WriteGpuFloat(uniforms, 67, useHistory ? 1 : 0);
        WriteGpuFloat(uniforms, 68, candidateCount);
        WriteGpuFloat(uniforms, 69, GpuVisibilityDepthBias);
        WriteGpuFloat(uniforms, 70, GpuVisibilityMotionPixels);
        WriteGpuFloat(uniforms, 71, GpuVisibilityExtentPixels);
        fixed (uint* uniformPtr = uniforms)
        {
            WriteProfiledBuffer(_gpuVisibilityUniformBuffer, 0,
                uniformPtr,
                checked((nuint)(GpuVisibilityUniformWords * sizeof(uint))));
        }

        BindGroup* group = GpuVisibilityBindGroup(hiZView);
        DispatchGpuCompute(_gpuVisibilityPipeline, group,
            checked((uint)((candidateCount + 63) / 64)), 1);
        _gpuVisibilityPrepared = true;
        _gpuVisibilityDispatches++;
        _gpuVisibilityCandidates += candidateCount;
    }

    private bool CaptureRetainedGpuVisibilityHistoryCore(
        Matrix4 projection, Matrix4 view,
        int depthTexture, int width, int height)
    {
        if (!UseGpuVisibility || depthTexture == 0
            || width <= 0 || height <= 0)
        {
            _gpuVisibilityPreviousMatricesValid = false;
            _gpuVisibilityPreviousDepthTexture = 0;
            return false;
        }

        try
        {
            EndActiveCorePass();
            EnsureGpuVisibilityPipelines();
            EnsureGpuHiZ(width, height);
            NativeTexture depth = EnsureTexture(depthTexture);
            BuildGpuHiZ(depth.SampleView, width, height);
            _gpuVisibilityPreviousProjection = projection;
            _gpuVisibilityPreviousView = view;
            _gpuVisibilityPreviousDepthTexture = depthTexture;
            _gpuVisibilityPreviousWidth = width;
            _gpuVisibilityPreviousHeight = height;
            _gpuVisibilityPreviousMatricesValid = true;
            return true;
        }
        catch (Exception ex) when (
            ex is not OutOfMemoryException and not StackOverflowException)
        {
            _gpuVisibilityRefused = true;
            _gpuVisibilityPreviousMatricesValid = false;
            _gpuVisibilityPreviousDepthTexture = 0;
            Console.WriteLine(
                $"[render] Hi-Z history unavailable; disabling GPU visibility: {ex.Message}");
            return false;
        }
    }

    private bool TryGpuVisibilityIndirect(
        RenderPassEncoder* pass, RenderItem item)
    {
        if (!_gpuVisibilityPrepared
            || _gpuVisibilityIndirectBuffer == null
            || !_gpuVisibilitySlots.TryGetValue(item, out int slot))
        {
            return false;
        }
        _api.RenderPassEncoderDrawIndexedIndirect(
            pass, _gpuVisibilityIndirectBuffer,
            checked((ulong)slot * RetainedIndexedIndirectBytes));
        _retainedIndirectDraws++;
        _gpuVisibilityIndirectDraws++;
        return true;
    }

    private static bool ValidGpuBounds(Vector3 min, Vector3 max) =>
        float.IsFinite(min.X) && float.IsFinite(min.Y) && float.IsFinite(min.Z)
        && float.IsFinite(max.X) && float.IsFinite(max.Y) && float.IsFinite(max.Z)
        && min.X <= max.X && min.Y <= max.Y && min.Z <= max.Z;

    private void EnsureGpuVisibilityPipelines()
    {
        if (_gpuVisibilityPipeline == null)
        {
            CreateGpuComputePipeline(GpuVisibilityShader,
                out _gpuVisibilityPipeline, out _gpuVisibilityLayout);
        }
        if (_gpuHiZDepthPipeline == null)
        {
            CreateGpuComputePipeline(GpuHiZDepthShader,
                out _gpuHiZDepthPipeline, out _gpuHiZDepthLayout);
        }
        if (_gpuHiZReducePipeline == null)
        {
            CreateGpuComputePipeline(GpuHiZReduceShader,
                out _gpuHiZReducePipeline, out _gpuHiZReduceLayout);
        }
    }

    private void CreateGpuComputePipeline(string source,
        out ComputePipeline* pipeline, out BindGroupLayout* layout)
    {
        ShaderModule* module = CreateWgslModule(source);
        nint entry = SilkMarshal.StringToPtr("main");
        try
        {
            var descriptor = new ComputePipelineDescriptor
            {
                Layout = null,
                Compute = new ProgrammableStageDescriptor
                {
                    Module = module,
                    EntryPoint = (byte*)entry
                }
            };
            pipeline = _api.DeviceCreateComputePipeline(
                _device.Device, descriptor);
            if (pipeline == null)
                throw new InvalidOperationException(
                    "Could not create retained GPU visibility compute pipeline.");
            layout = _api.ComputePipelineGetBindGroupLayout(pipeline, 0);
            if (layout == null)
                throw new InvalidOperationException(
                    "GPU visibility compute pipeline has no bind-group layout.");
        }
        finally
        {
            SilkMarshal.Free(entry);
            _api.ShaderModuleRelease(module);
        }
    }

    private void EnsureGpuHiZ(int width, int height)
    {
        int mipCount = 1 + (int)Math.Floor(
            Math.Log2(Math.Max(width, height)));
        if (_gpuHiZTexture != null
            && _gpuHiZWidth == width
            && _gpuHiZHeight == height
            && _gpuHiZMipCount == mipCount)
        {
            return;
        }

        ReleaseGpuHiZResources();
        _gpuHiZWidth = width;
        _gpuHiZHeight = height;
        _gpuHiZMipCount = mipCount;
        _gpuHiZTexture = _api.DeviceCreateTexture(
            _device.Device, new TextureDescriptor
            {
                Size = new Extent3D(
                    checked((uint)width), checked((uint)height), 1),
                Format = WgpuTextureFormat.R32float,
                Usage = TextureUsage.TextureBinding
                    | TextureUsage.StorageBinding,
                MipLevelCount = checked((uint)mipCount),
                SampleCount = 1,
                Dimension = TextureDimension.Dimension2D
            });
        if (_gpuHiZTexture == null)
            throw new InvalidOperationException(
                "Could not allocate retained Hi-Z texture.");

        _gpuHiZFullView = (nint)_api.TextureCreateView(
            _gpuHiZTexture, null);
        if (_gpuHiZFullView == 0)
            throw new InvalidOperationException(
                "Could not create retained Hi-Z sampled view.");

        for (int mip = 0; mip < mipCount; mip++)
        {
            var descriptor = new TextureViewDescriptor
            {
                Format = WgpuTextureFormat.R32float,
                Dimension = TextureViewDimension.Dimension2D,
                Aspect = TextureAspect.All,
                BaseMipLevel = checked((uint)mip),
                MipLevelCount = 1,
                BaseArrayLayer = 0,
                ArrayLayerCount = 1
            };
            TextureView* view = _api.TextureCreateView(
                _gpuHiZTexture, descriptor);
            if (view == null)
                throw new InvalidOperationException(
                    $"Could not create retained Hi-Z mip {mip} view.");
            _gpuHiZMipViews.Add((nint)view);
        }

        for (int mip = 1; mip < mipCount; mip++)
        {
            var entries = stackalloc BindGroupEntry[2];
            entries[0] = new BindGroupEntry
            {
                Binding = 0,
                TextureView = (TextureView*)_gpuHiZMipViews[mip - 1]
            };
            entries[1] = new BindGroupEntry
            {
                Binding = 1,
                TextureView = (TextureView*)_gpuHiZMipViews[mip]
            };
            BindGroup* group = CreateTrackedBindGroup(
                new BindGroupDescriptor
                {
                    Layout = _gpuHiZReduceLayout,
                    Entries = entries,
                    EntryCount = 2
                });
            _gpuHiZReduceBindGroups.Add((nint)group);
        }

        ReleaseGpuVisibilityBindGroup();
    }

    private void BuildGpuHiZ(
        TextureView* depthView, int width, int height)
    {
        if (depthView == null || _gpuHiZTexture == null)
            return;

        if (_gpuHiZDepthBindGroup == 0
            || _gpuHiZDepthSourceView != (nint)depthView)
        {
            if (_gpuHiZDepthBindGroup != 0)
                ReleaseTrackedBindGroup(
                    (BindGroup*)_gpuHiZDepthBindGroup);
            var entries = stackalloc BindGroupEntry[2];
            entries[0] = new BindGroupEntry
            {
                Binding = 0, TextureView = depthView
            };
            entries[1] = new BindGroupEntry
            {
                Binding = 1,
                TextureView = (TextureView*)_gpuHiZMipViews[0]
            };
            _gpuHiZDepthBindGroup = (nint)CreateTrackedBindGroup(
                new BindGroupDescriptor
                {
                    Layout = _gpuHiZDepthLayout,
                    Entries = entries,
                    EntryCount = 2
                });
            _gpuHiZDepthSourceView = (nint)depthView;
        }

        DispatchGpuCompute(
            _gpuHiZDepthPipeline,
            (BindGroup*)_gpuHiZDepthBindGroup,
            checked((uint)((width + 7) / 8)),
            checked((uint)((height + 7) / 8)));

        int mipWidth = width;
        int mipHeight = height;
        for (int mip = 1; mip < _gpuHiZMipCount; mip++)
        {
            mipWidth = Math.Max(1, (mipWidth + 1) / 2);
            mipHeight = Math.Max(1, (mipHeight + 1) / 2);
            DispatchGpuCompute(
                _gpuHiZReducePipeline,
                (BindGroup*)_gpuHiZReduceBindGroups[mip - 1],
                checked((uint)((mipWidth + 7) / 8)),
                checked((uint)((mipHeight + 7) / 8)));
        }
        _gpuVisibilityHiZBuilds++;
    }

    private void DispatchGpuCompute(
        ComputePipeline* pipeline, BindGroup* group,
        uint groupsX, uint groupsY)
    {
        if (groupsX == 0 || groupsY == 0)
            return;
        CommandEncoder* encoder = BeginCommands();
        ComputePassEncoder* pass =
            _api.CommandEncoderBeginComputePass(
                encoder, new ComputePassDescriptor());
        if (pass == null)
            throw new InvalidOperationException(
                "Could not begin retained GPU visibility compute pass.");
        _api.ComputePassEncoderSetPipeline(pass, pipeline);
        _api.ComputePassEncoderSetBindGroup(
            pass, 0, group, 0, null);
        _api.ComputePassEncoderDispatchWorkgroups(
            pass, groupsX, groupsY, 1);
        _api.ComputePassEncoderEnd(pass);
        _api.ComputePassEncoderRelease(pass);
        EndCommands();
    }

    private BindGroup* GpuVisibilityBindGroup(
        TextureView* hiZView)
    {
        nint[] resources =
        {
            (nint)_gpuVisibilityLayout,
            (nint)_gpuVisibilityCandidateBuffer,
            (nint)_gpuVisibilityIndirectBuffer,
            (nint)_gpuVisibilityCompactBuffer,
            (nint)_gpuVisibilityCountersBuffer,
            (nint)_gpuVisibilityUniformBuffer,
            (nint)hiZView
        };
        if (_gpuVisibilityBindGroup != 0
            && _gpuVisibilityBindResources.AsSpan()
                .SequenceEqual(resources))
        {
            return (BindGroup*)_gpuVisibilityBindGroup;
        }

        ReleaseGpuVisibilityBindGroup();
        var entries = stackalloc BindGroupEntry[6];
        entries[0] = new BindGroupEntry
        {
            Binding = 0,
            Buffer = _gpuVisibilityCandidateBuffer,
            Offset = 0,
            Size = _gpuVisibilityCandidateCapacity
        };
        entries[1] = new BindGroupEntry
        {
            Binding = 1,
            Buffer = _gpuVisibilityIndirectBuffer,
            Offset = 0,
            Size = _gpuVisibilityIndirectCapacity
        };
        entries[2] = new BindGroupEntry
        {
            Binding = 2,
            Buffer = _gpuVisibilityCompactBuffer,
            Offset = 0,
            Size = _gpuVisibilityCompactCapacity
        };
        entries[3] = new BindGroupEntry
        {
            Binding = 3,
            Buffer = _gpuVisibilityCountersBuffer,
            Offset = 0,
            Size = 16
        };
        entries[4] = new BindGroupEntry
        {
            Binding = 4,
            Buffer = _gpuVisibilityUniformBuffer,
            Offset = 0,
            Size = checked((ulong)(GpuVisibilityUniformWords * sizeof(uint)))
        };
        entries[5] = new BindGroupEntry
        {
            Binding = 5,
            TextureView = hiZView
        };
        _gpuVisibilityBindGroup = (nint)CreateTrackedBindGroup(
            new BindGroupDescriptor
            {
                Layout = _gpuVisibilityLayout,
                Entries = entries,
                EntryCount = 6
            });
        _gpuVisibilityBindResources = resources;
        return (BindGroup*)_gpuVisibilityBindGroup;
    }

    internal static void BeginRetainedPreVisibilityPass()
    {
        if (_current == null)
            return;
        Current.ResetGpuVisibilityFrameState();
    }

    private void ResetGpuVisibilityFrameState()
    {
        _gpuVisibilityPrepared = false;
        _gpuVisibilitySlots.Clear();
    }

    private void ReleaseGpuVisibilityBindGroup()
    {
        if (_gpuVisibilityBindGroup != 0)
        {
            ReleaseTrackedBindGroup(
                (BindGroup*)_gpuVisibilityBindGroup);
            _gpuVisibilityBindGroup = 0;
        }
        _gpuVisibilityBindResources = Array.Empty<nint>();
    }

    private void ReleaseGpuHiZResources()
    {
        ReleaseGpuVisibilityBindGroup();
        if (_gpuHiZDepthBindGroup != 0)
        {
            ReleaseTrackedBindGroup(
                (BindGroup*)_gpuHiZDepthBindGroup);
            _gpuHiZDepthBindGroup = 0;
            _gpuHiZDepthSourceView = 0;
        }
        foreach (nint group in _gpuHiZReduceBindGroups)
        {
            if (group != 0)
                ReleaseTrackedBindGroup((BindGroup*)group);
        }
        _gpuHiZReduceBindGroups.Clear();
        foreach (nint view in _gpuHiZMipViews)
        {
            if (view != 0)
                _api.TextureViewRelease((TextureView*)view);
        }
        _gpuHiZMipViews.Clear();
        if (_gpuHiZFullView != 0)
        {
            _api.TextureViewRelease(
                (TextureView*)_gpuHiZFullView);
            _gpuHiZFullView = 0;
        }
        if (_gpuHiZTexture != null)
        {
            _api.TextureRelease(_gpuHiZTexture);
            _gpuHiZTexture = null;
        }
        _gpuHiZWidth = _gpuHiZHeight = _gpuHiZMipCount = 0;
    }

    private void DisposeGpuVisibility()
    {
        EndActiveCorePass();
        ReleaseGpuHiZResources();
        if (_gpuVisibilityCandidateBuffer != null)
            _api.BufferRelease(_gpuVisibilityCandidateBuffer);
        if (_gpuVisibilityIndirectBuffer != null)
            _api.BufferRelease(_gpuVisibilityIndirectBuffer);
        if (_gpuVisibilityCompactBuffer != null)
            _api.BufferRelease(_gpuVisibilityCompactBuffer);
        if (_gpuVisibilityCountersBuffer != null)
            _api.BufferRelease(_gpuVisibilityCountersBuffer);
        if (_gpuVisibilityUniformBuffer != null)
            _api.BufferRelease(_gpuVisibilityUniformBuffer);
        _gpuVisibilityCandidateBuffer = null;
        _gpuVisibilityIndirectBuffer = null;
        _gpuVisibilityCompactBuffer = null;
        _gpuVisibilityCountersBuffer = null;
        _gpuVisibilityUniformBuffer = null;
        _gpuVisibilityCandidateCapacity = 0;
        _gpuVisibilityIndirectCapacity = 0;
        _gpuVisibilityCompactCapacity = 0;
        _gpuVisibilityCountersCapacity = 0;
        _gpuVisibilityUniformCapacity = 0;

        if (_gpuVisibilityLayout != null)
            _api.BindGroupLayoutRelease(_gpuVisibilityLayout);
        if (_gpuVisibilityPipeline != null)
            _api.ComputePipelineRelease(_gpuVisibilityPipeline);
        if (_gpuHiZDepthLayout != null)
            _api.BindGroupLayoutRelease(_gpuHiZDepthLayout);
        if (_gpuHiZDepthPipeline != null)
            _api.ComputePipelineRelease(_gpuHiZDepthPipeline);
        if (_gpuHiZReduceLayout != null)
            _api.BindGroupLayoutRelease(_gpuHiZReduceLayout);
        if (_gpuHiZReducePipeline != null)
            _api.ComputePipelineRelease(_gpuHiZReducePipeline);
        _gpuVisibilityLayout = null;
        _gpuVisibilityPipeline = null;
        _gpuHiZDepthLayout = null;
        _gpuHiZDepthPipeline = null;
        _gpuHiZReduceLayout = null;
        _gpuHiZReducePipeline = null;
        _gpuVisibilitySlots.Clear();
    }

    private static void WriteGpuVec3(
        uint[] words, int at, Vector3 value)
    {
        WriteGpuFloat(words, at, value.X);
        WriteGpuFloat(words, at + 1, value.Y);
        WriteGpuFloat(words, at + 2, value.Z);
        words[at + 3] = 0;
    }

    private static void WriteGpuMatrix(
        uint[] words, int at, Matrix4 value)
    {
        WriteGpuFloat(words, at, value.M11);
        WriteGpuFloat(words, at + 1, value.M12);
        WriteGpuFloat(words, at + 2, value.M13);
        WriteGpuFloat(words, at + 3, value.M14);
        WriteGpuFloat(words, at + 4, value.M21);
        WriteGpuFloat(words, at + 5, value.M22);
        WriteGpuFloat(words, at + 6, value.M23);
        WriteGpuFloat(words, at + 7, value.M24);
        WriteGpuFloat(words, at + 8, value.M31);
        WriteGpuFloat(words, at + 9, value.M32);
        WriteGpuFloat(words, at + 10, value.M33);
        WriteGpuFloat(words, at + 11, value.M34);
        WriteGpuFloat(words, at + 12, value.M41);
        WriteGpuFloat(words, at + 13, value.M42);
        WriteGpuFloat(words, at + 14, value.M43);
        WriteGpuFloat(words, at + 15, value.M44);
    }

    private static void WriteGpuFloat(
        uint[] words, int at, float value) =>
        words[at] = BitConverter.SingleToUInt32Bits(value);

    private const string GpuHiZDepthShader = @"
@group(0) @binding(0) var source_depth: texture_depth_2d;
@group(0) @binding(1) var destination: texture_storage_2d<r32float, write>;

@compute @workgroup_size(8, 8, 1)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let size = textureDimensions(destination);
    if (id.x >= size.x || id.y >= size.y) {
        return;
    }
    let p = vec2<i32>(i32(id.x), i32(id.y));
    let depth = textureLoad(source_depth, p, 0);
    textureStore(destination, p, vec4<f32>(depth, 0.0, 0.0, 0.0));
}
";

    private const string GpuHiZReduceShader = @"
@group(0) @binding(0) var source: texture_2d<f32>;
@group(0) @binding(1) var destination: texture_storage_2d<r32float, write>;

@compute @workgroup_size(8, 8, 1)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let dst_size = textureDimensions(destination);
    if (id.x >= dst_size.x || id.y >= dst_size.y) {
        return;
    }
    let src_size = textureDimensions(source);
    let base = vec2<i32>(i32(id.x * 2u), i32(id.y * 2u));
    let hi = vec2<i32>(i32(src_size.x) - 1, i32(src_size.y) - 1);
    let p0 = clamp(base, vec2<i32>(0), hi);
    let p1 = clamp(base + vec2<i32>(1, 0), vec2<i32>(0), hi);
    let p2 = clamp(base + vec2<i32>(0, 1), vec2<i32>(0), hi);
    let p3 = clamp(base + vec2<i32>(1, 1), vec2<i32>(0), hi);
    let d = max(max(textureLoad(source, p0, 0).x,
                    textureLoad(source, p1, 0).x),
                max(textureLoad(source, p2, 0).x,
                    textureLoad(source, p3, 0).x));
    textureStore(destination, vec2<i32>(i32(id.x), i32(id.y)),
        vec4<f32>(d, 0.0, 0.0, 0.0));
}
";

    private const string GpuVisibilityShader = @"
struct Candidate {
    bounds_min: vec4<f32>,
    bounds_max: vec4<f32>,
    index_count: u32,
    packet_id: u32,
    pad0: u32,
    pad1: u32,
};

struct DrawIndexedArgs {
    index_count: u32,
    instance_count: u32,
    first_index: u32,
    base_vertex: i32,
    first_instance: u32,
};

struct Counters {
    visible: atomic<u32>,
    frustum_rejected: atomic<u32>,
    hiz_rejected: atomic<u32>,
    reserved: atomic<u32>,
};

struct VisibilityUniforms {
    current_projection: mat4x4<f32>,
    current_view: mat4x4<f32>,
    previous_projection: mat4x4<f32>,
    previous_view: mat4x4<f32>,
    viewport: vec4<f32>,
    params: vec4<f32>,
};

@group(0) @binding(0) var<storage, read> candidates: array<Candidate>;
@group(0) @binding(1) var<storage, read_write> draw_args: array<DrawIndexedArgs>;
@group(0) @binding(2) var<storage, read_write> compact_ids: array<u32>;
@group(0) @binding(3) var<storage, read_write> counters: Counters;
@group(0) @binding(4) var<uniform> uniforms: VisibilityUniforms;
@group(0) @binding(5) var hiz: texture_2d<f32>;

fn corner(c: Candidate, index: u32) -> vec3<f32> {
    return vec3<f32>(
        select(c.bounds_min.x, c.bounds_max.x, (index & 1u) != 0u),
        select(c.bounds_min.y, c.bounds_max.y, (index & 2u) != 0u),
        select(c.bounds_min.z, c.bounds_max.z, (index & 4u) != 0u));
}

fn clip_point(projection: mat4x4<f32>, view: mat4x4<f32>,
              point: vec3<f32>) -> vec4<f32> {
    return projection * view * vec4<f32>(point, 1.0);
}

fn pixel_from_clip(clip: vec4<f32>) -> vec2<f32> {
    let ndc = clip.xy / clip.w;
    return vec2<f32>(
        (ndc.x * 0.5 + 0.5) * uniforms.viewport.x,
        (0.5 - ndc.y * 0.5) * uniforms.viewport.y);
}

fn hiz_at(pixel: vec2<f32>, mip: u32) -> f32 {
    let size = textureDimensions(hiz, i32(mip));
    let scale = exp2(f32(mip));
    let p = vec2<i32>(pixel / scale);
    let hi = vec2<i32>(i32(size.x) - 1, i32(size.y) - 1);
    return textureLoad(hiz, clamp(p, vec2<i32>(0), hi), i32(mip)).x;
}

@compute @workgroup_size(64, 1, 1)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let count = u32(uniforms.params.x);
    if (id.x >= count) {
        return;
    }

    let c = candidates[id.x];
    draw_args[id.x].index_count = c.index_count;
    draw_args[id.x].instance_count = 0u;
    draw_args[id.x].first_index = 0u;
    draw_args[id.x].base_vertex = 0;
    draw_args[id.x].first_instance = 0u;

    var left = 0u;
    var right = 0u;
    var bottom = 0u;
    var top = 0u;
    var near_count = 0u;
    var far_count = 0u;
    var current_min = vec2<f32>(1e30);
    var current_max = vec2<f32>(-1e30);
    var current_all_front = true;

    for (var i = 0u; i < 8u; i = i + 1u) {
        let clip = clip_point(
            uniforms.current_projection, uniforms.current_view,
            corner(c, i));
        if (clip.x < -clip.w) { left = left + 1u; }
        if (clip.x > clip.w) { right = right + 1u; }
        if (clip.y < -clip.w) { bottom = bottom + 1u; }
        if (clip.y > clip.w) { top = top + 1u; }
        if (clip.z < -clip.w) { near_count = near_count + 1u; }
        if (clip.z > clip.w) { far_count = far_count + 1u; }
        if (clip.w <= 1e-5) {
            current_all_front = false;
        } else {
            let pixel = pixel_from_clip(clip);
            current_min = min(current_min, pixel);
            current_max = max(current_max, pixel);
        }
    }

    if (left == 8u || right == 8u || bottom == 8u || top == 8u
        || near_count == 8u || far_count == 8u) {
        atomicAdd(&counters.frustum_rejected, 1u);
        return;
    }

    var occluded = false;
    if (uniforms.viewport.w > 0.5 && current_all_front) {
        var previous_min = vec2<f32>(1e30);
        var previous_max = vec2<f32>(-1e30);
        var previous_near_depth = 1.0;
        var previous_all_front = true;

        for (var i = 0u; i < 8u; i = i + 1u) {
            let clip = clip_point(
                uniforms.previous_projection, uniforms.previous_view,
                corner(c, i));
            if (clip.w <= 1e-5) {
                previous_all_front = false;
            } else {
                let pixel = pixel_from_clip(clip);
                previous_min = min(previous_min, pixel);
                previous_max = max(previous_max, pixel);
                let depth = clamp(
                    (clip.z / clip.w + 1.0) * 0.5, 0.0, 1.0);
                previous_near_depth =
                    min(previous_near_depth, depth);
            }
        }

        let fully_in_view = current_min.x >= 0.0
            && current_min.y >= 0.0
            && current_max.x < uniforms.viewport.x
            && current_max.y < uniforms.viewport.y
            && previous_min.x >= 0.0
            && previous_min.y >= 0.0
            && previous_max.x < uniforms.viewport.x
            && previous_max.y < uniforms.viewport.y;

        if (previous_all_front && fully_in_view) {
            let current_center = (current_min + current_max) * 0.5;
            let previous_center = (previous_min + previous_max) * 0.5;
            let current_extent = max(
                current_max - current_min, vec2<f32>(1.0));
            let previous_extent = max(
                previous_max - previous_min, vec2<f32>(1.0));
            let center_motion = max(
                abs(current_center.x - previous_center.x),
                abs(current_center.y - previous_center.y));
            let extent_motion = max(
                abs(current_extent.x - previous_extent.x),
                abs(current_extent.y - previous_extent.y));

            if (center_motion <= uniforms.params.z
                && extent_motion <= uniforms.params.w) {
                let max_dimension = max(
                    previous_extent.x, previous_extent.y);
                let requested_mip = u32(max(
                    0.0, ceil(log2(max(max_dimension, 1.0)))));
                let mip_count = u32(uniforms.viewport.z);
                let mip = min(requested_mip, mip_count - 1u);
                let p0 = previous_min;
                let p1 = vec2<f32>(previous_max.x, previous_min.y);
                let p2 = vec2<f32>(previous_min.x, previous_max.y);
                let p3 = previous_max;
                let pc = (previous_min + previous_max) * 0.5;
                let farthest = max(
                    max(hiz_at(p0, mip), hiz_at(p1, mip)),
                    max(max(hiz_at(p2, mip), hiz_at(p3, mip)),
                        hiz_at(pc, mip)));
                occluded = previous_near_depth
                    > farthest + uniforms.params.y;
            }
        }
    }

    if (occluded) {
        atomicAdd(&counters.hiz_rejected, 1u);
        return;
    }

    draw_args[id.x].instance_count = 1u;
    let compact_index = atomicAdd(&counters.visible, 1u);
    compact_ids[compact_index] = c.packet_id;
}
";
}
#endif
