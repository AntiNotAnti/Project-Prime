using System;
using SDL;

namespace MphRead
{
    /// <summary>
    /// Reference-counted ownership of an SDL fence. Frame-resource rotation
    /// and capture readback tickets may observe the same submission, so a raw
    /// fence pointer cannot be released by one owner while the other still
    /// needs to query it.
    ///
    /// All operations are intentionally called on the SDL graphics thread;
    /// this is a lifetime lease, not a cross-thread synchronization primitive.
    /// </summary>
    internal unsafe sealed class SdlGpuFenceLease
    {
        private readonly SDL_GPUDevice* _device;
        private SDL_GPUFence* _fence;
        private int _references = 1;

        public SdlGpuFenceLease(SDL_GPUDevice* device, SDL_GPUFence* fence)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (fence == null) throw new ArgumentNullException(nameof(fence));
            _device = device;
            _fence = fence;
        }

        public SDL_GPUFence* Handle => _fence;

        public bool IsComplete
            => _fence == null || SDL3.SDL_QueryGPUFence(_device, _fence);

        public void Wait()
        {
            if (_fence == null) return;
            SDL_GPUFence* fence = _fence;
            if (!SDL3.SDL_WaitForGPUFences(_device, true, &fence, 1))
            {
                throw new InvalidOperationException($"SDL GPU fence wait failed: {SDL3.SDL_GetError()}");
            }
        }

        public void Retain()
        {
            if (_fence == null || _references <= 0)
            {
                throw new ObjectDisposedException(nameof(SdlGpuFenceLease));
            }
            checked { _references++; }
        }

        public void Release()
        {
            if (_references <= 0) return;
            _references--;
            if (_references != 0) return;
            SDL_GPUFence* fence = _fence;
            _fence = null;
            if (fence != null) SDL3.SDL_ReleaseGPUFence(_device, fence);
        }
    }

    /// <summary>
    /// Bounded dynamic-upload slots. Rotation is only storage management; a
    /// slot is reused after SDL reports its fence complete (or after waiting
    /// for that fence), never merely because an integer frame counter wrapped.
    /// </summary>
    internal unsafe sealed class SdlGpuFrameResources : IDisposable
    {
        private sealed class Slot
        {
            public SdlGpuFenceLease? Fence;
        }

        private readonly SDL_GPUDevice* _device;
        private readonly Slot[] _slots;
        private int _next;
        private bool _disposed;

        public SdlGpuFrameResources(SDL_GPUDevice* device, int slotCount = 3)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (slotCount < 2) throw new ArgumentOutOfRangeException(nameof(slotCount));
            _device = device;
            _slots = new Slot[slotCount];
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new Slot();
        }

        public int SlotCount => _slots.Length;

        /// <summary>
        /// The slot selected by the most recent <see cref="BeginFrame"/>.
        /// Per-frame upload storage uses the same fence rotation as command
        /// buffers, so a dynamic buffer is never reused while the GPU still
        /// reads it.
        /// </summary>
        public int CurrentSlotIndex => _next;

        public void BeginFrame()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Slot slot = _slots[_next];
            if (slot.Fence != null)
            {
                if (!slot.Fence.IsComplete) slot.Fence.Wait();
                slot.Fence.Release();
                slot.Fence = null;
            }
        }

        /// <summary>
        /// Commits a native fence and returns a caller-owned lease. The frame
        /// rotation retains one additional reference; callers must release the
        /// returned reference after handing any required references to other
        /// owners such as readback tickets.
        /// </summary>
        public SdlGpuFenceLease CommitFence(SDL_GPUFence* fence)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (fence == null) throw new ArgumentNullException(nameof(fence));
            SdlGpuFenceLease lease = new SdlGpuFenceLease(_device, fence);
            Slot slot = _slots[_next];
            if (slot.Fence != null)
            {
                slot.Fence.Release();
            }
            lease.Retain();
            slot.Fence = lease;
            _next = (_next + 1) % _slots.Length;
            return lease;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _slots.Length; i++)
            {
                SdlGpuFenceLease? lease = _slots[i].Fence;
                if (lease != null)
                {
                    lease.Release();
                    _slots[i].Fence = null;
                }
            }
        }
    }
}
