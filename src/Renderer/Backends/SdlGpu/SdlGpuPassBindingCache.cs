using System;
using SDL;

namespace MphRead;

/// <summary>
/// Native binding identities retained only for one SDL render pass. The
/// storage is fixed and bounded because sampler binding pointers commonly
/// refer to stackalloc data owned by the caller.
/// </summary>
internal unsafe struct SdlGpuPassBindingCache
{
    public const int MaximumSamplerBindingCount = SdlGpuSamplerBindingAbi.D3D12BatchSize;

    private nint _pipeline;
    private uint _samplerFirstSlot;
    private uint _samplerCount;
    private bool _hasPipeline;
    private bool _hasSamplers;
    private bool _descriptorBearingDirty;
    private fixed long _textureHandles[MaximumSamplerBindingCount];
    private fixed long _samplerHandles[MaximumSamplerBindingCount];

    public void Reset()
    {
        _pipeline = 0;
        _samplerFirstSlot = 0;
        _samplerCount = 0;
        _hasPipeline = false;
        _hasSamplers = false;
        _descriptorBearingDirty = true;
    }

    public bool ShouldBindPipeline(SDL_GPUGraphicsPipeline* pipeline)
    {
        nint identity = (nint)pipeline;
        if (_hasPipeline && _pipeline == identity) return false;
        _pipeline = identity;
        _hasPipeline = true;
        _descriptorBearingDirty = true;
        return true;
    }

    /// <summary>
    /// Returns true when SDL must receive this complete sampler table. The
    /// caller owns the input pointer; every native handle is copied into the
    /// pass-local bounded storage before the method returns.
    /// </summary>
    public bool ShouldBindFragmentSamplers(uint firstSlot, uint count,
        SDL_GPUTextureSamplerBinding* bindings)
    {
        if (count > MaximumSamplerBindingCount)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count,
                $"A pass can cache at most {MaximumSamplerBindingCount} sampler bindings.");
        }
        if (count != 0 && bindings == null)
            throw new ArgumentNullException(nameof(bindings));
        int bindingCount = checked((int)count);

        if (_hasSamplers && _samplerFirstSlot == firstSlot
            && _samplerCount == count)
        {
            bool same = true;
            for (int index = 0; index < bindingCount; index++)
            {
                if (_textureHandles[index] != (long)(nint)bindings[index].texture
                    || _samplerHandles[index] != (long)(nint)bindings[index].sampler)
                {
                    same = false;
                    break;
                }
            }
            if (same) return false;
        }

        _samplerFirstSlot = firstSlot;
        _samplerCount = count;
        _hasSamplers = true;
        _descriptorBearingDirty = true;
        for (int index = 0; index < bindingCount; index++)
        {
            _textureHandles[index] = (long)(nint)bindings[index].texture;
            _samplerHandles[index] = (long)(nint)bindings[index].sampler;
        }
        return true;
    }

    /// <summary>
    /// Consumes the pass-local descriptor-table change marker for one indexed
    /// draw. A reset pass starts dirty, and each changed pipeline or sampler
    /// table makes the next draw descriptor-bearing.
    /// </summary>
    public bool ConsumeDescriptorBearing()
    {
        bool result = _descriptorBearingDirty;
        _descriptorBearingDirty = false;
        return result;
    }
}
