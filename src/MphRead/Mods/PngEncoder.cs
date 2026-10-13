using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using ReFuel.Stb;
using ReFuel.Stb.Native;

namespace MphRead.Mods;

// Same STB encoder/options as the package convenience method, with an explicit
// owner for the managed delegate behind its unmanaged callback pointer.
internal static class PngEncoder
{
    internal static unsafe void Write<T>(ReadOnlySpan<T> pixels, int width, int height,
        StbiImageFormat format, Stream destination) where T : unmanaged
    {
        int components = format switch
        {
            StbiImageFormat.Grey => 1, StbiImageFormat.GreyAlpha => 2,
            StbiImageFormat.Rgb => 3, StbiImageFormat.Rgba => 4,
            _ => throw new ArgumentException("Expected a fully qualified byte format.", nameof(format))
        };
        ArgumentNullException.ThrowIfNull(destination);
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(pixels);
        int stride = checked(width * components);
        if (width <= 0 || height <= 0 || bytes.Length < checked(stride * height))
            throw new ArgumentException("PNG dimensions exceed the pixel buffer.", nameof(pixels));
        var output = new CallbackStream(destination);
        var wrapper = new StbiWriteStreamWrapper(output);
        try
        {
            int written;
            fixed (byte* data = bytes)
                written = Stbi.write_png_to_func(wrapper.Callback, null, width, height, components, data, stride);
            output.ThrowIfFailed();
            if (written == 0) throw new IOException("STB could not encode the PNG.");
        }
        finally
        {
            // wrapper owns the actual callback delegate, which owns output and
            // the destination. A native IntPtr alone does not keep it alive.
            GC.KeepAlive(wrapper);
        }
    }

    // Never unwind a stream failure through the native callback. Report the
    // original exception after the synchronous encoder has returned instead.
    private sealed class CallbackStream(Stream destination) : Stream
    {
        private Exception? _failure;
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_failure != null) return;
            try { destination.Write(buffer); }
            catch (Exception error) { _failure = error; }
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        internal void ThrowIfFailed() { if (_failure != null) ExceptionDispatchInfo.Capture(_failure).Throw(); }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => destination.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => destination.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
