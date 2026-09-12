using System;
using System.Collections.Generic;
using SDL;

namespace MphRead
{
    /// <summary>
    /// Bounded SDL GPU readback scheduler. A slot owns one reusable download
    /// transfer buffer and retains the submission fence until the transfer is
    /// mapped. The host drains completed results on the SDL graphics thread;
    /// no worker thread touches SDL GPU objects.
    /// </summary>
    internal unsafe sealed class SdlGpuReadback : IDisposable
    {
        // RenderFrame bounds requests to eight entries. Keeping one slot per
        // possible request makes a burst bounded without making the normal
        // screenshot-plus-recording case wait for a previous ticket.
        private const int MaximumFailures = RenderFrame.DefaultMaximumCaptureRequests * 2;
        private const uint RowPitchAlignment = 256;
        private const uint TransferOffsetAlignment = 512;

        private sealed class Slot
        {
            public SDL_GPUTransferBuffer* Transfer;
            public uint TransferCapacity;
            public uint TransferOffset;
            public uint RowPitch;
            public uint Width;
            public uint Height;
            public CapturePixelFormat SourceFormat;
            public RenderCaptureRequest? Request;
            public SdlGpuFenceLease? Fence;
        }

        private readonly SdlGpuDevice _device;
        private readonly Slot[] _slots;
        private readonly Queue<RenderCaptureResult> _completed = new();
        private readonly Queue<RenderCaptureFailure> _failures = new();
        private SDL_GPUCopyPass* _copyPass;
        private bool _disposed;

        public SdlGpuReadback(SdlGpuDevice device, int slotCount = RenderFrame.DefaultMaximumCaptureRequests)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            if (slotCount < 1) throw new ArgumentOutOfRangeException(nameof(slotCount));
            _slots = new Slot[slotCount];
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new Slot();
        }

        public int SlotCount => _slots.Length;
        public int PendingCount
        {
            get
            {
                int count = 0;
                foreach (Slot slot in _slots)
                {
                    if (slot.Request != null) count++;
                }
                return count;
            }
        }

        /// <summary>Polls tickets without blocking for an unfinished fence.</summary>
        public void DrainCompleted() => DrainCompleted(waitForCompletion: false);

        /// <summary>Begins the one copy pass used by this frame's requests.</summary>
        public bool TryBeginSubmission(SDL_GPUCommandBuffer* commandBuffer, out string error)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (commandBuffer == null)
            {
                error = "SDL GPU returned a null command buffer for capture readback.";
                return false;
            }
            if (_copyPass != null)
            {
                error = "SDL GPU capture readback already has an open copy pass.";
                return false;
            }

            _copyPass = SDL3.SDL_BeginGPUCopyPass(commandBuffer);
            if (_copyPass == null)
            {
                error = $"SDL GPU capture copy pass creation failed: {SDL3.SDL_GetError()}";
                return false;
            }
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Encodes one texture-to-download-buffer operation. No CPU mapping
        /// occurs here; the transfer is readable only after CommitSubmission
        /// associates the slot with the command buffer's fence.
        /// </summary>
        public bool TrySchedule(SDL_GPUTexture* texture, uint sourceWidth, uint sourceHeight,
            SDL_GPUTextureFormat sourceTextureFormat, RenderCaptureRequest request,
            out string error)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_copyPass == null)
            {
                error = "SDL GPU capture readback has no open copy pass.";
                return false;
            }
            if (texture == null)
            {
                error = "SDL GPU capture source texture is null.";
                return false;
            }
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (sourceWidth == 0 || sourceHeight == 0)
            {
                error = "SDL GPU capture source has no drawable pixels.";
                return false;
            }
            if (request.Width != sourceWidth || request.Height != sourceHeight)
            {
                error = $"Capture request is {request.Width}x{request.Height}, but its {request.Target} source is {sourceWidth}x{sourceHeight}.";
                return false;
            }
            if (!TryGetSourceFormat(sourceTextureFormat, out CapturePixelFormat sourceFormat))
            {
                error = $"SDL GPU capture does not support source texture format {sourceTextureFormat}.";
                return false;
            }

            Slot? slot = FindFreeSlot();
            if (slot == null)
            {
                error = $"SDL GPU capture readback queue is full ({_slots.Length} transfer slots).";
                return false;
            }

            try
            {
                uint sourceBytesPerPixel = checked((uint)RenderCaptureResult.BytesPerPixel(sourceFormat));
                uint rowBytes = checked(sourceWidth * sourceBytesPerPixel);
                uint rowPitch = Align(rowBytes, RowPitchAlignment);
                uint transferOffset = Align(0, TransferOffsetAlignment);
                uint transferBytes = checked(transferOffset + rowPitch * sourceHeight);
                EnsureTransferBuffer(slot, transferBytes);

                SDL_GPUTextureRegion region = new()
                {
                    texture = texture,
                    mip_level = 0,
                    layer = 0,
                    x = 0,
                    y = 0,
                    z = 0,
                    w = sourceWidth,
                    h = sourceHeight,
                    d = 1
                };
                SDL_GPUTextureTransferInfo destination = new()
                {
                    transfer_buffer = slot.Transfer,
                    offset = transferOffset,
                    pixels_per_row = checked(rowPitch / sourceBytesPerPixel),
                    rows_per_layer = sourceHeight
                };
                SDL3.SDL_DownloadFromGPUTexture(_copyPass, &region, &destination);

                slot.TransferOffset = transferOffset;
                slot.RowPitch = rowPitch;
                slot.Width = sourceWidth;
                slot.Height = sourceHeight;
                slot.SourceFormat = sourceFormat;
                slot.Request = request;
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void EndSubmission()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_copyPass == null) return;
            SDL3.SDL_EndGPUCopyPass(_copyPass);
            _copyPass = null;
        }

        /// <summary>
        /// Associates all slots encoded since the last failed/successful
        /// submission with the same fence. Each slot retains its own reference
        /// so frame-resource rotation may release its reference independently.
        /// </summary>
        public void CommitSubmission(SdlGpuFenceLease fence)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (fence == null) throw new ArgumentNullException(nameof(fence));
            foreach (Slot slot in _slots)
            {
                if (slot.Request == null || slot.Fence != null) continue;
                fence.Retain();
                slot.Fence = fence;
            }
        }

        /// <summary>
        /// A failed submit did not put any capture transfer on the GPU. Clear
        /// only those uncommitted slots so the immutable request remains owned
        /// by Renderer and is attached to the next frame with the same ID/name.
        /// </summary>
        public void DiscardUnsubmitted()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (Slot slot in _slots)
            {
                if (slot.Fence == null && slot.Request != null) ClearRequest(slot);
            }
        }

        public void ReportFailure(RenderCaptureFailure failure)
        {
            if (failure == null) throw new ArgumentNullException(nameof(failure));
            EnqueueFailure(failure);
        }

        private void EnqueueFailure(RenderCaptureFailure failure)
        {
            if (_failures.Count >= MaximumFailures)
            {
                Console.Error.WriteLine($"[capture] failure queue full; dropping {failure.RequestId}: {failure.Error}");
                return;
            }
            _failures.Enqueue(failure);
        }

        public bool TryDequeueCapture(out RenderCaptureResult? result)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DrainCompleted(waitForCompletion: false);
            if (_completed.Count == 0)
            {
                result = null;
                return false;
            }
            result = _completed.Dequeue();
            return true;
        }

        public bool TryDequeueFailure(out RenderCaptureFailure? failure)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DrainCompleted(waitForCompletion: false);
            if (_failures.Count == 0)
            {
                failure = null;
                return false;
            }
            failure = _failures.Dequeue();
            return true;
        }

        /// <summary>
        /// Waits and drains all tickets during controlled host shutdown. The
        /// transfer buffers remain reusable between frames and are released by
        /// Dispose only after their associated fence has been handled.
        /// </summary>
        public void Flush()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DrainCompleted(waitForCompletion: true);
        }

        private void DrainCompleted(bool waitForCompletion)
        {
            foreach (Slot slot in _slots)
            {
                if (slot.Request == null || slot.Fence == null) continue;

                try
                {
                    if (!slot.Fence.IsComplete)
                    {
                        if (!waitForCompletion) continue;
                        slot.Fence.Wait();
                    }

                    RenderCaptureRequest request = slot.Request;
                    RenderCaptureResult result = MapResult(slot, request);
                    if (_completed.Count >= _slots.Length)
                    {
                        EnqueueFailure(new RenderCaptureFailure(request,
                            $"SDL GPU capture completion queue is full ({_slots.Length} results)."));
                    }
                    else
                    {
                        _completed.Enqueue(result);
                    }
                }
                catch (Exception ex)
                {
                    EnqueueFailure(new RenderCaptureFailure(slot.Request,
                        $"SDL GPU capture readback failed: {ex.Message}"));
                }
                finally
                {
                    slot.Fence.Release();
                    slot.Fence = null;
                    ClearRequest(slot);
                }
            }
        }

        private RenderCaptureResult MapResult(Slot slot, RenderCaptureRequest request)
        {
            if (slot.Transfer == null) throw new InvalidOperationException("SDL GPU capture transfer buffer is missing.");
            IntPtr mapped = SDL3.SDL_MapGPUTransferBuffer(_device.Handle, slot.Transfer, false);
            if (mapped == IntPtr.Zero)
            {
                throw new InvalidOperationException($"SDL GPU capture transfer map failed: {SDL3.SDL_GetError()}");
            }
            try
            {
                int mappedBytes = checked((int)(slot.RowPitch * slot.Height));
                IntPtr firstRow = IntPtr.Add(mapped, checked((int)slot.TransferOffset));
                ReadOnlySpan<byte> source = new ReadOnlySpan<byte>((void*)firstRow, mappedBytes);
                byte[] normalized = RenderCapturePixels.Normalize(source,
                    checked((int)slot.Width), checked((int)slot.Height), slot.SourceFormat,
                    CaptureRowOrientation.TopDown, request.PixelFormat, request.RowOrientation,
                    checked((int)slot.RowPitch));
                return new RenderCaptureResult(request.RequestId, request.OriginatingFrame,
                    request.Target, request.Width, request.Height, request.PixelFormat,
                    request.RowOrientation, normalized, request.OutputName, request.Delivery);
            }
            finally
            {
                SDL3.SDL_UnmapGPUTransferBuffer(_device.Handle, slot.Transfer);
            }
        }

        private Slot? FindFreeSlot()
        {
            foreach (Slot slot in _slots)
            {
                if (slot.Request == null && slot.Fence == null) return slot;
            }
            return null;
        }

        private void EnsureTransferBuffer(Slot slot, uint required)
        {
            if (slot.Transfer != null && slot.TransferCapacity >= required) return;
            SDL_GPUTransferBufferCreateInfo description = new()
            {
                usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_DOWNLOAD,
                size = required
            };
            SDL_GPUTransferBuffer* replacement = SDL3.SDL_CreateGPUTransferBuffer(_device.Handle, &description);
            if (replacement == null)
            {
                throw new InvalidOperationException($"SDL GPU capture transfer allocation failed: {SDL3.SDL_GetError()}");
            }
            if (slot.Transfer != null) SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, slot.Transfer);
            slot.Transfer = replacement;
            slot.TransferCapacity = required;
        }

        private static uint Align(uint value, uint alignment)
        {
            uint remainder = value % alignment;
            return remainder == 0 ? value : checked(value + alignment - remainder);
        }

        private static bool TryGetSourceFormat(SDL_GPUTextureFormat format, out CapturePixelFormat sourceFormat)
        {
            switch (format)
            {
                case SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM:
                case SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM_SRGB:
                    sourceFormat = CapturePixelFormat.Rgba8;
                    return true;
                case SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM:
                case SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM_SRGB:
                    sourceFormat = CapturePixelFormat.Bgra8;
                    return true;
                default:
                    sourceFormat = default;
                    return false;
            }
        }

        private static void ClearRequest(Slot slot)
        {
            slot.Request = null;
            slot.TransferOffset = 0;
            slot.RowPitch = 0;
            slot.Width = 0;
            slot.Height = 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            DrainCompleted(waitForCompletion: true);
            _disposed = true;
            foreach (Slot slot in _slots)
            {
                slot.Fence?.Release();
                slot.Fence = null;
                if (slot.Transfer != null)
                {
                    SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, slot.Transfer);
                    slot.Transfer = null;
                }
                slot.TransferCapacity = 0;
                ClearRequest(slot);
            }
        }
    }
}
