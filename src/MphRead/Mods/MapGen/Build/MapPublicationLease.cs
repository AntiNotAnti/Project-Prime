using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MphRead.Mods.MapGen;

/// <summary>An installation is busy in another game process. Retrying after its readers close is safe.</summary>
public sealed class MapPublicationBusyException(string message) : IOException(message);

/// <summary>
/// Cross-process reader/publication fence for one installed runtime room. Lease files live in private
/// user data and are never deleted: all processes must keep locking the same kernel object.
/// This complements the game's owner-thread and in-process runtime usage checks.
/// </summary>
public sealed class MapPublicationLease : IDisposable
{
    private FileStream? _stream;

    private MapPublicationLease(FileStream stream) => _stream = stream;

    public static string CoordinationDirectory
    {
        get
        {
            string root = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") is { Length: > 0 } supplied
                ? Path.GetFullPath(supplied)
                : OperatingSystem.IsMacOS()
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Library", "Application Support", "Project Prime")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Project Prime");
            return Path.Combine(root, "map-publication");
        }
    }

    public static MapPublicationLease AcquireReader(string runtimeRoot, string runtimeNamespace, string room,
        string? coordinationDirectory = null, CancellationToken cancellation = default)
        => Acquire(runtimeRoot, runtimeNamespace, room, publication: false, coordinationDirectory, cancellation);

    /// <summary>Acquire immediately or throw; the game/network owner must never wait for a live scene.</summary>
    public static MapPublicationLease AcquirePublication(string runtimeRoot, string runtimeNamespace, string room,
        string? coordinationDirectory = null, CancellationToken cancellation = default)
        => Acquire(runtimeRoot, runtimeNamespace, room, publication: true, coordinationDirectory, cancellation);

    private static MapPublicationLease Acquire(string runtimeRoot, string runtimeNamespace, string room,
        bool publication, string? coordinationDirectory, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(runtimeNamespace);
        string root = Path.GetFullPath(coordinationDirectory ?? CoordinationDirectory);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(root);
        else Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        // Runtime names share files regardless of casing. On desktop macOS/Windows, fold root
        // casing too; conservative contention is safer on an optional case-sensitive volume.
        string installation = CanonicalizeRuntimeDirectory(runtimeRoot);
        string identity = installation + "\0" + runtimeNamespace.ToUpperInvariant() + "\0" + room.ToUpperInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        string path = Path.Combine(root, key + ".lock");
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.ReadWrite,
            BufferSize = 1
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        FileStream stream;
        try { stream = new FileStream(path, options); }
        catch (IOException ex) when (OperatingSystem.IsWindows()
            ? (ex.HResult & 0xffff) is 32 or 33 : ex.HResult is 11 or 35 || (ex.HResult & 0xffff) is 32 or 33)
        {
            // .NET's Unix FileStream itself requests a shared flock while opening.
            // An existing exclusive publication may reject the open before our native call.
            throw Busy(publication);
        }
        try
        {
            bool acquired;
            int error;
            if (OperatingSystem.IsWindows())
            {
                var overlap = new NativeOverlapped();
                acquired = LockFileEx(stream.SafeFileHandle, publication ? 3u : 1u, 0, 1, 0, ref overlap);
                error = acquired ? 0 : Marshal.GetLastPInvokeError();
            }
            else
            {
                // flock locks are associated with this open file description, so each reader
                // has independent ownership. Closing/crashing releases its lease automatically.
                int result;
                do { result = Flock(checked((int)stream.SafeFileHandle.DangerousGetHandle()), publication ? 6 : 5); }
                while (result != 0 && Marshal.GetLastPInvokeError() == 4); // EINTR
                acquired = result == 0;
                error = acquired ? 0 : Marshal.GetLastPInvokeError();
            }
            if (!acquired)
            {
                bool busy = OperatingSystem.IsWindows() ? error == 33 : error is 11 or 35;
                if (busy) throw Busy(publication);
                throw new IOException("Could not acquire the map publication lease.", new Win32Exception(error));
            }
            cancellation.ThrowIfCancellationRequested();
            return new(stream);
        }
        catch { stream.Dispose(); throw; }
    }

    private static MapPublicationBusyException Busy(bool publication) => new(publication
        ? "Another game process is using this map. Close its scene and retry installation."
        : "Another process is publishing this map. Retry preparation after publication completes.");

    /// <summary>Resolve existing directory aliases for publication ownership and private-output guards.</summary>
    public static string CanonicalizeRuntimeDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string result = CanonicalDirectory(directory);
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? result.ToUpperInvariant() : result;
    }

    /// <summary>Resolve directory aliases for physical containment without folding casing or installation roles.</summary>
    public static string ResolveRuntimeDirectoryAliases(string directory)
        => ProjectPrime.DesktopShared.DesktopInstallationIdentity.ResolveDirectoryAliases(directory);

    private static string CanonicalDirectory(string directory, int depth = 0)
    {
        if (depth > 32) throw new IOException("Map runtime directory has too many symbolic links.");
        string full = Path.GetFullPath(directory);
        string root = Path.GetPathRoot(full)!;
        string current = root;
        foreach (string component in full[root.Length..].Split(Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            var info = new DirectoryInfo(current);
            if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                current = CanonicalDirectory(info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new IOException("Cannot resolve the map runtime directory."), depth + 1);
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    public void Dispose() => Interlocked.Exchange(ref _stream, null)?.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeOverlapped
    {
        public IntPtr Internal, InternalHigh;
        public uint Offset, OffsetHigh;
        public IntPtr Event;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockFileEx(SafeFileHandle handle, uint flags, uint reserved,
        uint lengthLow, uint lengthHigh, ref NativeOverlapped overlapped);

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(int descriptor, int operation);
}
