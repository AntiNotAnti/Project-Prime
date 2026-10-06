using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MphRead.Mods.Update;

/// <summary>Prevents a paired update from replacing assemblies used by an open Studio.
/// Kernel ownership ends on dispose or process death. The coordination inode is never deleted.</summary>
public sealed class InstallationLifetime : IDisposable
{
    private FileStream? _stream;
    private InstallationLifetime(FileStream stream) => _stream = stream;

    public static InstallationLifetime AcquireApplication(string installationDirectory, string? coordinationDirectory = null)
        => Acquire(installationDirectory, coordinationDirectory, exclusive: false);

    public static InstallationLifetime AcquireUpdate(string installationDirectory, string? coordinationDirectory = null)
        => Acquire(installationDirectory, coordinationDirectory, exclusive: true);

    private static InstallationLifetime Acquire(string installationDirectory, string? coordinationDirectory, bool exclusive)
    {
        string root = CanonicalInstallation(installationDirectory);
        string? isolated = Environment.GetEnvironmentVariable("PROJECT_PRIME_INSTALLATION_LIFETIME_DATA");
        if (isolated is { Length: > 0 } && !Path.IsPathFullyQualified(isolated))
            throw new ArgumentException("PROJECT_PRIME_INSTALLATION_LIFETIME_DATA must be an absolute directory.");
        // Application profiles must not create independent locks for the same installed binaries.
        string state = coordinationDirectory ?? (isolated is { Length: > 0 } ? Path.GetFullPath(isolated) : Path.Combine(
                OperatingSystem.IsMacOS()
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Project Prime")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Project Prime"),
            "installation-lifetime"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(state);
        else Directory.CreateDirectory(state, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root))).ToLowerInvariant();
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite,
            Share = FileShare.ReadWrite, BufferSize = 1 };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        FileStream stream;
        try { stream = new FileStream(Path.Combine(state, key + ".lock"), options); }
        catch (IOException ex) when (IsBusy(ex.HResult & 0xffff)) { throw Busy(exclusive); }
        try
        {
            bool acquired;
            if (OperatingSystem.IsWindows())
            {
                var overlap = new Overlapped();
                acquired = LockFileEx(stream.SafeFileHandle, exclusive ? 3u : 1u, 0, 1, 0, ref overlap);
            }
            else
            {
                int result;
                do { result = Flock(checked((int)stream.SafeFileHandle.DangerousGetHandle()), exclusive ? 6 : 5); }
                while (result != 0 && Marshal.GetLastPInvokeError() == 4);
                acquired = result == 0;
            }
            if (!acquired)
            {
                int error = Marshal.GetLastPInvokeError();
                if (IsBusy(error)) throw Busy(exclusive);
                throw new IOException("Could not coordinate the desktop installation.", new Win32Exception(error));
            }
            return new(stream);
        }
        catch { stream.Dispose(); throw; }
    }

    private static bool IsBusy(int error) => OperatingSystem.IsWindows() ? error is 32 or 33 : error is 11 or 35;
    private static IOException Busy(bool update) => new(update
        ? "Project Prime or Project Prime Studio is still open. Close both applications, then retry the update; no installed files were changed."
        : "This desktop installation is being updated. Start Project Prime or Studio after the update completes.");

    /// <summary>Two sibling macOS bundles form one installation; directory aliases share ownership.</summary>
    public static string CanonicalInstallation(string directory)
        => ProjectPrime.DesktopShared.DesktopInstallationIdentity.CanonicalInstallation(directory);

    public void Dispose() => Interlocked.Exchange(ref _stream, null)?.Dispose();
    [StructLayout(LayoutKind.Sequential)]
    private struct Overlapped { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockFileEx(SafeFileHandle handle, uint flags, uint reserved, uint low, uint high, ref Overlapped overlap);
    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(int descriptor, int operation);
}
