using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MphRead.Mods.MapGen;

/// <summary>Fence the staged path against atomic replacement while a verified read handle is held.</summary>
internal static class MapPublicationFileIdentity
{
    internal static bool Same(FileStream handle, string path)
    {
        using var candidate = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (OperatingSystem.IsWindows())
        {
            return GetInformation(handle.SafeFileHandle.DangerousGetHandle(), out var original)
                && GetInformation(candidate.SafeFileHandle.DangerousGetHandle(), out var current)
                && original.Volume == current.Volume && original.IndexHigh == current.IndexHigh
                && original.IndexLow == current.IndexLow;
        }
        // stat's device/inode pair occupies the first sixteen bytes on supported
        // 64-bit Linux/macOS/Android targets. macOS's first word also contains mode
        // and link count; comparing it makes permission/link mutation fail closed.
        byte[] originalStat = new byte[256], currentStat = new byte[256];
        int first = checked((int)handle.SafeFileHandle.DangerousGetHandle());
        int second = checked((int)candidate.SafeFileHandle.DangerousGetHandle());
        if (FStat(first, originalStat) != 0 || FStat(second, currentStat) != 0) return false;
        return originalStat.AsSpan(0, 16).SequenceEqual(currentStat.AsSpan(0, 16));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Information
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetInformation(IntPtr handle, out Information info);
    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static extern int FStat(int descriptor, [Out] byte[] stat);
}
