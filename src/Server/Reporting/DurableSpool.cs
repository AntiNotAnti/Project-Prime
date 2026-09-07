using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace MphRead.Reporting;

internal static class DurableSpool
{
    public static void Write(string path, byte[] bytes)
    {
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) throw new IOException("Conflicting existing spool body");
            SyncDirectory(Path.GetDirectoryName(path)!); return;
        }
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.WriteThrough)) { file.Write(bytes); file.Flush(flushToDisk: true); }
            if (OperatingSystem.IsWindows())
            {
                if (!MoveFileEx(temporary, path, 8)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            else { File.Move(temporary, path, overwrite: false); SyncDirectory(Path.GetDirectoryName(path)!); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void SyncDirectory(string directory)
    {
        if (OperatingSystem.IsWindows()) return; // write-through rename above; a lost delete only causes an idempotent retry
        int fd = open(directory, 0);
        if (fd < 0) throw new IOException("Could not open outbox directory for durable synchronization.");
        try { if (fsync(fd) != 0) throw new IOException("Outbox directory synchronization failed."); }
        finally { close(fd); }
    }
    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int descriptor);
    [DllImport("libc")] private static extern int close(int descriptor);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string replacement, uint flags);
}
