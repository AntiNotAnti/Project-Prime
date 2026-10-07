#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    internal sealed record ShaderDiagnosticSnapshot(ModernGraphicsCompat Owner, int Program,
        nint Buffer, ulong Offset, int Size, uint[] Words, ModernUniformLayout[] Members,
        int[] Textures, string[] Samplers, nint[] BindingResources, nint BindGroup,
        long TextureRevision, string CpuValues);

    private static readonly PfnBufferMapCallback ShaderDiagnosticMapCallback = new((status, userdata) =>
    {
        // Opaque tokens, not GCHandle pointers: a callback after cancellation is harmless.
        ShaderDiagnosticMapRegistry.Complete((nint)userdata, (int)status);
    });

    internal static void WriteShaderDiagnosticForCheck(string message)
        => ShaderDiagnosticPolicy.Write(Console.Error, message);

    internal static ShaderDiagnosticSnapshot? CapturePostProcessDiagnosticForCheck(int program)
    {
        if (!ShaderDiagnosticPolicy.Enabled(Environment.GetEnvironmentVariable(ShaderDiagnosticPolicy.EnvironmentVariable),
            Environment.GetEnvironmentVariable("PRIME_WGPU_VALIDATION"), Environment.GetEnvironmentVariable("PRIME_WGPU_GPU_VALIDATION")))
            return null;
        try
        {
            ModernGraphicsCompat? owner = _current;
            if (owner == null || owner._programs.CurrentProgram != program
                || !owner._generatedPrograms.TryGetValue(ModernProgramKind.PostProcess, out var generated)
                || generated.BindGroupCursor == 0)
                throw new InvalidOperationException("Original PostProcess draw has no generated binding snapshot.");
            ShaderDiagnosticPolicy.ValidateBinding(generated.UniformOffset, generated.Layout.Size, generated.Words.Length);
            var originalGroup = generated.BindGroups[generated.BindGroupCursor - 1];
            var bindings = originalGroup.Resources.ToArray();
            if (bindings[1] != (nint)generated.UniformBuffer || (ulong)bindings[2] != generated.UniformOffset)
                throw new InvalidOperationException("Original draw cache differs from its generated buffer/offset.");
            var record = owner._programs.Program(program);
            string cpu = string.Join(";", generated.Layout.Uniforms.Select(member =>
                record.Uniforms.TryGetValue(member.Name, out var value)
                ? member.Integer ? $"{member.Name}=int:{value.IntValue}"
                    : $"{member.Name}=float:[{string.Join(",", value.Data ?? Array.Empty<float>())}]"
                : $"{member.Name}=unset"));
            return new(owner, program, (nint)generated.UniformBuffer, generated.UniformOffset, generated.Layout.Size,
                generated.Words.ToArray(), generated.Layout.Uniforms.ToArray(), generated.Textures.ToArray(),
                generated.Layout.Samplers.ToArray(), bindings, (nint)originalGroup.Group, originalGroup.TextureRevision, cpu);
        }
        catch (Exception ex)
        {
            WriteShaderDiagnosticForCheck("DIAGNOSTIC postprocess snapshot UNAVAILABLE: " + ex.Message);
            return null;
        }
    }

    internal static void ObservePostProcessFailureForCheck(ShaderDiagnosticSnapshot? snapshot)
    {
        if (snapshot == null) return;
        try
        {
            WriteShaderDiagnosticForCheck($"DIAGNOSTIC postprocess original-binding program={snapshot.Program} group=0x{snapshot.BindGroup:x} buffer=0x{snapshot.Buffer:x} staticOffset={snapshot.Offset} dynamicOffsetCount=0 size={snapshot.Size} textureRevision={snapshot.TextureRevision}");
            WriteShaderDiagnosticForCheck("DIAGNOSTIC postprocess CPU program: " + snapshot.CpuValues);
            WriteShaderDiagnosticForCheck("DIAGNOSTIC postprocess CPU packed words: " + string.Join(",", snapshot.Words.Select(w => $"{w:x8}")));
            WriteShaderDiagnosticForCheck("DIAGNOSTIC postprocess binding resources: " + string.Join(",", snapshot.BindingResources.Select(p => $"0x{p:x}")));
            WriteShaderDiagnosticForCheck("DIAGNOSTIC postprocess sampler IDs: " + string.Join(",", snapshot.Samplers.Zip(snapshot.Textures, (name, id) => $"{name}={id}")));
            if (!ReferenceEquals(_current, snapshot.Owner) || snapshot.Owner._disposed || snapshot.Owner._device.IsLost)
                throw new InvalidOperationException("Original draw owner is no longer healthy/current.");
            uint[] gpu = snapshot.Owner.ReadDiagnosticUniformWords(snapshot);
            WriteShaderDiagnosticForCheck("DIAGNOSTIC postprocess GPU uniform words: " + string.Join(",", gpu.Select(w => $"{w:x8}")));
            foreach (var field in snapshot.Members)
            {
                int at = field.Offset / 4;
                WriteShaderDiagnosticForCheck($"DIAGNOSTIC postprocess field={field.Name} offset={field.Offset} cpuBits=0x{snapshot.Words[at]:x8} gpuBits=0x{gpu[at]:x8} integer={field.Integer}");
            }
            WriteShaderDiagnosticForCheck($"DIAGNOSTIC postprocess GPU backing-buffer observation DONE matchesCpu={gpu.AsSpan().SequenceEqual(snapshot.Words)}; fresh compute CBV does not prove original fragment CBV interpretation; original pixel failure remains required");
        }
        catch (Exception ex)
        {
            WriteShaderDiagnosticForCheck("DIAGNOSTIC postprocess GPU observation UNAVAILABLE: " + ex.Message + "; original pixel failure remains required");
        }
    }

    private uint[] ReadDiagnosticUniformWords(ShaderDiagnosticSnapshot snapshot)
    {
        ShaderDiagnosticPolicy.ValidateBinding(snapshot.Offset, snapshot.Size, snapshot.Words.Length);
        const string source = """
            struct Original { words: array<vec4<u32>, 85>, }
            struct Observed { words: array<vec4<u32>, 85>, }
            @group(0) @binding(0) var<uniform> original: Original;
            @group(0) @binding(1) var<storage, read_write> observed: Observed;
            @compute @workgroup_size(64)
            fn main(@builtin(global_invocation_id) id: vec3<u32>) {
                if (id.x < 85u) { observed.words[id.x] = original.words[id.x]; }
            }
            """;
        WgpuBuffer* storage = null;
        WgpuBuffer* readback = null;
        ShaderModule* module = null;
        ComputePipeline* pipeline = null;
        BindGroupLayout* layout = null;
        BindGroup* group = null;
        CommandEncoder* encoder = null;
        CommandBuffer* commands = null;
        ComputePassEncoder* pass = null;
        nint entry = 0, token = 0;
        bool mapAccepted = false;
        var watch = Stopwatch.StartNew();
        void Budget()
        {
            if (watch.ElapsedMilliseconds > ShaderDiagnosticPolicy.ProbeBudgetMilliseconds)
                throw new TimeoutException("PostProcess uniform observation exceeded its 10-second budget.");
        }
        try
        {
            // Independent diagnostic resources/encoder; do not rent, rewrite or reset an original arena.
            storage = _api.DeviceCreateBuffer(_device.Device, new BufferDescriptor
                { Size = (ulong)snapshot.Size, Usage = BufferUsage.Storage | BufferUsage.CopySrc });
            readback = _api.DeviceCreateBuffer(_device.Device, new BufferDescriptor
                { Size = (ulong)snapshot.Size, Usage = BufferUsage.CopyDst | BufferUsage.MapRead });
            if (storage == null || readback == null) throw new InvalidOperationException("Diagnostic buffer creation failed.");
            module = CreateWgslModule(source);
            entry = SilkMarshal.StringToPtr("main");
            pipeline = _api.DeviceCreateComputePipeline(_device.Device, new ComputePipelineDescriptor
                { Compute = new ProgrammableStageDescriptor { Module = module, EntryPoint = (byte*)entry } });
            Budget();
            if (pipeline == null) throw new InvalidOperationException("Diagnostic compute pipeline creation failed.");
            layout = _api.ComputePipelineGetBindGroupLayout(pipeline, 0);
            var entries = stackalloc BindGroupEntry[2];
            entries[0] = new() { Binding = 0, Buffer = (WgpuBuffer*)snapshot.Buffer, Offset = snapshot.Offset, Size = (ulong)snapshot.Size };
            entries[1] = new() { Binding = 1, Buffer = storage, Size = (ulong)snapshot.Size };
            group = _api.DeviceCreateBindGroup(_device.Device, new BindGroupDescriptor { Layout = layout, Entries = entries, EntryCount = 2 });
            encoder = _api.DeviceCreateCommandEncoder(_device.Device, new CommandEncoderDescriptor());
            if (group == null || encoder == null) throw new InvalidOperationException("Diagnostic binding/encoder creation failed.");
            pass = _api.CommandEncoderBeginComputePass(encoder, new ComputePassDescriptor());
            if (pass == null) throw new InvalidOperationException("Diagnostic compute pass creation failed.");
            _api.ComputePassEncoderSetPipeline(pass, pipeline);
            _api.ComputePassEncoderSetBindGroup(pass, 0, group, 0, null);
            _api.ComputePassEncoderDispatchWorkgroups(pass, 2, 1, 1);
            _api.ComputePassEncoderEnd(pass);
            _api.ComputePassEncoderRelease(pass); pass = null;
            _api.CommandEncoderCopyBufferToBuffer(encoder, storage, 0, readback, 0, (ulong)snapshot.Size);
            commands = _api.CommandEncoderFinish(encoder, new CommandBufferDescriptor());
            if (commands == null) throw new InvalidOperationException("Diagnostic command buffer creation failed.");
            CommandBuffer* submitted = commands;
            _device.ObserveNativeResult(ModernGraphicsNativeBridge.Submit(_queue, 1, &submitted), "diagnostic uniform submission");
            var lease = new ShaderDiagnosticMapLease();
            token = ShaderDiagnosticMapRegistry.Register(lease);
            mapAccepted = true;
            _api.BufferMapAsync(readback, MapMode.Read, 0, (nuint)snapshot.Size, ShaderDiagnosticMapCallback, (void*)token);
            while (lease.Status == -1)
            {
                Budget();
                _device.Native.DevicePoll(_device.Device, false, null);
                _device.ThrowIfFailed();
                if (lease.Status == -1) Thread.Sleep(1);
            }
            if (lease.Status != (int)BufferMapAsyncStatus.Success)
                throw new InvalidOperationException($"Diagnostic map failed: {(BufferMapAsyncStatus)lease.Status}.");
            uint* data = (uint*)_api.BufferGetConstMappedRange(readback, 0, (nuint)snapshot.Size);
            if (data == null) throw new InvalidOperationException("Diagnostic mapped range is null.");
            uint[] words = new uint[snapshot.Size / 4];
            for (int i = 0; i < words.Length; i++) words[i] = data[i];
            Budget();
            return words;
        }
        finally
        {
            // Native callbacks stay rooted for the process lifetime. Removing the
            // token before unmapping also makes a deferred abort callback harmless.
            ShaderDiagnosticMapRegistry.Cancel(token);
            void Release(Action action)
            {
                try { action(); }
                catch (Exception ex) { WriteShaderDiagnosticForCheck("DIAGNOSTIC postprocess cleanup: " + ex.Message); }
            }
            if (mapAccepted && readback != null) Release(() => _api.BufferUnmap(readback));
            if (commands != null) Release(() => _api.CommandBufferRelease(commands));
            if (pass != null) Release(() => _api.ComputePassEncoderRelease(pass));
            if (encoder != null) Release(() => _api.CommandEncoderRelease(encoder));
            if (group != null) Release(() => _api.BindGroupRelease(group));
            if (layout != null) Release(() => _api.BindGroupLayoutRelease(layout));
            if (pipeline != null) Release(() => _api.ComputePipelineRelease(pipeline));
            if (module != null) Release(() => _api.ShaderModuleRelease(module));
            if (storage != null) Release(() => _api.BufferRelease(storage));
            if (readback != null) Release(() => _api.BufferRelease(readback));
            if (entry != 0) Release(() => SilkMarshal.Free(entry));
        }
    }
}
#endif
