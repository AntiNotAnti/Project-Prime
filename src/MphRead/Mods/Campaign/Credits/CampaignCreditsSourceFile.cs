using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MphRead.Mods.Campaign;

// Classifies the opened object before any payload read/allocation. The loader
// separately owns ancestry policy and exact size/hash identity. This is not a
// deadline for ordinary filesystem IO or a race-free ancestor traversal claim.
internal static class CampaignCreditsSourceFile
{
    // Reviewed 64-bit ABI profiles only. The unused tail is deliberately larger
    // than each supported stat; an unknown architecture is refused before open.
    [StructLayout(LayoutKind.Explicit, Size = 512)]
    private struct PosixStat
    {
        [FieldOffset(0)] internal ulong Alignment;
        [FieldOffset(4)] internal ushort DarwinMode;
        [FieldOffset(16)] internal uint LinuxArm64Mode;
        [FieldOffset(24)] internal uint LinuxX64Mode;
    }
    [DllImport("libc", EntryPoint = "open", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    private static extern int OpenPosix([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", EntryPoint = "fstat", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    private static extern int StatPosix(SafeFileHandle handle, out PosixStat stat);
    [DllImport("libc", EntryPoint = "fstat$INODE64", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    private static extern int StatDarwinX64(SafeFileHandle handle, out PosixStat stat);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle OpenWindows(string path, uint access, uint share, nint security,
        uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(SafeFileHandle handle);

    internal static FileStream Open(string path, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (String.IsNullOrEmpty(path) || path.Contains('\0'))
            throw new InvalidDataException("Original credits source path is invalid.");
        SafeFileHandle handle = OperatingSystem.IsWindows() ? Windows(path) : Posix(path);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            // Stream takes ownership of this same classified handle. Constructor
            // failure is still this method's responsibility; no descriptor leaks.
            return new FileStream(handle, FileAccess.Read, 65536, isAsync: false);
        }
        catch { handle.Dispose(); throw; }
    }
    private static SafeFileHandle Windows(string path)
    {
        // GENERIC_READ, SHARE_READ, OPEN_EXISTING, OPEN_REPARSE_POINT|SEQUENTIAL_SCAN.
        var handle = OpenWindows(path, 0x80000000, 1, 0, 3, 0x08200000, 0);
        try
        {
            if (handle.IsInvalid) throw Failure();
            if (GetFileType(handle) != 1 || (File.GetAttributes(handle)
                & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new InvalidDataException("Original credits input must be a regular file.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }
    private static SafeFileHandle Posix(string path)
    {
        Architecture architecture = RuntimeInformation.ProcessArchitecture;
        bool darwin = OperatingSystem.IsMacOS();
        bool linux = OperatingSystem.IsLinux() || OperatingSystem.IsAndroid();
        if ((!darwin && !linux) || !BitConverter.IsLittleEndian || IntPtr.Size != 8
            || architecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("Original credits source file profile is unsupported.");
        // NONBLOCK prevents a FIFO peer handshake; NOFOLLOW refuses a leaf link.
        // CLOEXEC keeps source handles out of child processes. No create/write.
        int descriptor = OpenPosix(path, darwin ? 0x1000104 : 0xA0800);
        if (descriptor < 0) throw Failure();
        var handle = new SafeFileHandle((nint)descriptor, ownsHandle: true); // fd0 is valid.
        try
        {
            int status = darwin && architecture == Architecture.X64
                ? StatDarwinX64(handle, out var stat) : StatPosix(handle, out stat);
            if (status != 0) throw Failure();
            uint mode = darwin ? stat.DarwinMode : architecture == Architecture.X64 ? stat.LinuxX64Mode : stat.LinuxArm64Mode;
            if ((mode & 0xF000) != 0x8000)
                throw new InvalidDataException("Original credits input must be a regular file.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }
    private static IOException Failure() => new("Original credits source handle is unavailable.",
        new Win32Exception(Marshal.GetLastPInvokeError()));
}
