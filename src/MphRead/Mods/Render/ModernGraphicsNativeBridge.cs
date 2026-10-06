#if !MPHREAD_SERVER
using System;
using System.Text;
using Silk.NET.WebGPU;
using WgpuTexture = Silk.NET.WebGPU.Texture;

namespace MphRead.Mods.Render;

internal enum NativeGraphicsOutcome : uint
{
    Success, Timeout, Outdated, SurfaceLost, DeviceLost, OutOfMemory, ValidationError, InternalError
}

internal readonly record struct NativeGraphicsResult(NativeGraphicsOutcome Outcome, string Detail)
{
    internal bool Succeeded => Outcome == NativeGraphicsOutcome.Success;
}

// Resolve additive entry points from the exact native context Silk loaded. A
// second DllImport resolver could accidentally bind another runtime/library.
internal static unsafe class ModernGraphicsNativeBridge
{
    private const int MessageCapacity = 512;
    private static delegate* unmanaged[Cdecl]<Surface*, SurfaceConfiguration*, byte*, nuint, uint> _configure;
    private static delegate* unmanaged[Cdecl]<Surface*, SurfaceTexture*, byte*, nuint, uint> _acquire;
    private static delegate* unmanaged[Cdecl]<Surface*, byte*, nuint, uint> _present;
    private static delegate* unmanaged[Cdecl]<WgpuTexture*, byte*, nuint, uint> _discard;
    private static delegate* unmanaged[Cdecl]<Queue*, nuint, CommandBuffer**, byte*, nuint, uint> _submit;
    private static delegate* unmanaged[Cdecl]<Queue*, float> _timestampPeriod;

    internal static void Initialize(WebGPU api)
    {
        nint Resolve(string name)
        {
            if (!api.Context.TryGetProcAddress(name, out nint address) || address == 0)
                throw new DllNotFoundException($"The loaded wgpu-native runtime lacks {name}. "
                    + "Build and package the Project Prime patched runtime with tools/wgpu/build-native.sh; "
                    + "an upstream NuGet runtime cannot safely handle renderer device/surface loss.");
            return address;
        }
        var version = (delegate* unmanaged[Cdecl]<uint>)Resolve("primeWgpuBridgeVersion");
        if (version() != 1)
            throw new DllNotFoundException("The loaded Project Prime wgpu-native bridge ABI is incompatible; rebuild the paired native runtime.");
        _configure = (delegate* unmanaged[Cdecl]<Surface*, SurfaceConfiguration*, byte*, nuint, uint>)Resolve("primeWgpuSurfaceConfigure");
        _acquire = (delegate* unmanaged[Cdecl]<Surface*, SurfaceTexture*, byte*, nuint, uint>)Resolve("primeWgpuSurfaceAcquire");
        _present = (delegate* unmanaged[Cdecl]<Surface*, byte*, nuint, uint>)Resolve("primeWgpuSurfacePresent");
        _discard = (delegate* unmanaged[Cdecl]<WgpuTexture*, byte*, nuint, uint>)Resolve("primeWgpuSurfaceDiscard");
        _submit = (delegate* unmanaged[Cdecl]<Queue*, nuint, CommandBuffer**, byte*, nuint, uint>)Resolve("primeWgpuQueueSubmit");
        _timestampPeriod = (delegate* unmanaged[Cdecl]<Queue*, float>)Resolve("primeWgpuQueueGetTimestampPeriod");
    }

    private static NativeGraphicsResult Result(uint status, byte* message)
    {
        NativeGraphicsOutcome outcome = status <= (uint)NativeGraphicsOutcome.InternalError
            ? (NativeGraphicsOutcome)status : NativeGraphicsOutcome.InternalError;
        int length = 0;
        while (length < MessageCapacity && message[length] != 0) length++;
        return new(outcome, length == 0 ? string.Empty : Encoding.UTF8.GetString(new ReadOnlySpan<byte>(message, length)));
    }

    internal static NativeGraphicsResult Configure(Surface* surface, in SurfaceConfiguration configuration)
    {
        byte* message = stackalloc byte[MessageCapacity];
        message[0] = 0;
        fixed (SurfaceConfiguration* config = &configuration)
            return Result(_configure(surface, config, message, MessageCapacity), message);
    }
    internal static NativeGraphicsResult Acquire(Surface* surface, out SurfaceTexture texture)
    {
        byte* message = stackalloc byte[MessageCapacity];
        message[0] = 0;
        texture = default;
        fixed (SurfaceTexture* output = &texture)
            return Result(_acquire(surface, output, message, MessageCapacity), message);
    }
    internal static NativeGraphicsResult Present(Surface* surface)
    {
        byte* message = stackalloc byte[MessageCapacity];
        message[0] = 0;
        return Result(_present(surface, message, MessageCapacity), message);
    }
    internal static NativeGraphicsResult Discard(WgpuTexture* texture)
    {
        byte* message = stackalloc byte[MessageCapacity];
        message[0] = 0;
        return Result(_discard(texture, message, MessageCapacity), message);
    }
    internal static NativeGraphicsResult Submit(Queue* queue, uint count, CommandBuffer** commands)
    {
        byte* message = stackalloc byte[MessageCapacity];
        message[0] = 0;
        return Result(_submit(queue, count, commands, message, MessageCapacity), message);
    }
    internal static float TimestampPeriod(Queue* queue) => _timestampPeriod(queue);
}
#endif
