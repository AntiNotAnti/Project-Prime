using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Accounts;

/// <summary>Selects an approved refresh-token store for the current desktop
/// platform. Linux deliberately remains process-local until an approved Secret
/// Service integration is available.</summary>
public static class SecureSessionStoreFactory
{
    public static ISecureSessionStore CreateDefault()
    {
        if (OperatingSystem.IsWindows()) return new WindowsDpapiSessionStore();
        if (OperatingSystem.IsMacOS()) return new MacKeychainSessionStore();
        return new MemoryOnlySessionStore();
    }
}

/// <summary>DPAPI user-scoped storage for Windows. The file contains only
/// CryptProtectData output and is never used as a plaintext fallback.</summary>
public sealed class WindowsDpapiSessionStore : ISecureSessionStore
{
    private readonly string _directory;

    public WindowsDpapiSessionStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProjectPrime", "session");
        if (string.IsNullOrWhiteSpace(_directory))
            throw new InvalidOperationException("Windows protected-session storage has no local application directory.");
        Directory.CreateDirectory(_directory);
    }

    public ValueTask<byte[]?> ReadAsync(string backendScope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureSessionRecordCodec.ValidateScope(backendScope);
        string path = PathFor(backendScope);
        if (!File.Exists(path)) return ValueTask.FromResult<byte[]?>(null);
        byte[] protectedRecord = File.ReadAllBytes(path);
        if (protectedRecord.Length is 0 or > SecureSessionRecordCodec.MaximumBytes + 1024)
            return ValueTask.FromResult<byte[]?>(null);
        try { return ValueTask.FromResult<byte[]?>(WindowsDpapi.Unprotect(protectedRecord)); }
        catch (CryptographicException) { return ValueTask.FromResult<byte[]?>(null); }
    }

    public ValueTask WriteAsync(string backendScope, ReadOnlyMemory<byte> record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureSessionRecordCodec.ValidateScope(backendScope);
        if (record.Length is 0 or > SecureSessionRecordCodec.MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(record));
        byte[] protectedRecord = WindowsDpapi.Protect(record.ToArray());
        string path = PathFor(backendScope);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, protectedRecord);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            CryptographicOperations.ZeroMemory(protectedRecord);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string backendScope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureSessionRecordCodec.ValidateScope(backendScope);
        string path = PathFor(backendScope);
        if (File.Exists(path)) File.Delete(path);
        return ValueTask.CompletedTask;
    }

    private string PathFor(string scope)
        => Path.Combine(_directory, Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(scope))).ToLowerInvariant() + ".bin");

    private static class WindowsDpapi
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Length;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptProtectData(ref DataBlob dataIn, string? description,
            IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);
        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref DataBlob dataIn, StringBuilder? description,
            IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr handle);

        public static byte[] Protect(byte[] value) => Transform(value, protect: true);
        public static byte[] Unprotect(byte[] value) => Transform(value, protect: false);

        private static byte[] Transform(byte[] value, bool protect)
        {
            IntPtr inputMemory = Marshal.AllocHGlobal(value.Length);
            try
            {
                Marshal.Copy(value, 0, inputMemory, value.Length);
                var input = new DataBlob { Length = value.Length, Data = inputMemory };
                var output = new DataBlob();
                bool success = protect
                    ? CryptProtectData(ref input, "Project Prime refresh token", IntPtr.Zero,
                        IntPtr.Zero, IntPtr.Zero, 0, ref output)
                    : CryptUnprotectData(ref input, null, IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, 0, ref output);
                if (!success) throw new CryptographicException(Marshal.GetLastWin32Error());
                try
                {
                    byte[] result = new byte[output.Length];
                    Marshal.Copy(output.Data, result, 0, output.Length);
                    return result;
                }
                finally { if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
            }
            finally { Marshal.FreeHGlobal(inputMemory); }
        }
    }
}

/// <summary>The narrow native seam used by the macOS Keychain store. Keeping
/// this at the P/Invoke boundary makes replacement failures testable without
/// introducing a general platform-security abstraction.</summary>
internal interface IMacKeychainApi
{
    int FindGenericPassword(byte[] service, byte[] account, out uint length,
        out IntPtr data, out IntPtr item);
    int AddGenericPassword(byte[] service, byte[] account, byte[] value);
    int ModifyContent(IntPtr item, byte[] value);
    int DeleteItem(IntPtr item);
    void Release(IntPtr item);
    int FreeContent(IntPtr data);
}

/// <summary>macOS Keychain generic-password storage. Keychain errors are
/// surfaced rather than silently writing a plaintext file.</summary>
public sealed class MacKeychainSessionStore : ISecureSessionStore
{
    internal const int ItemNotFound = -25300;
    internal const int DuplicateItem = -25299;
    internal const int Success = 0;
    private const string ServicePrefix = "ProjectPrime.AccountSession.";
    private readonly IMacKeychainApi _api;

    public MacKeychainSessionStore() : this(new NativeMacKeychainApi()) { }

    internal MacKeychainSessionStore(IMacKeychainApi api)
        => _api = api ?? throw new ArgumentNullException(nameof(api));

    public ValueTask<byte[]?> ReadAsync(string backendScope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureSessionRecordCodec.ValidateScope(backendScope);
        byte[] service = Service(backendScope);
        byte[] account = Account();
        try
        {
            int status = _api.FindGenericPassword(service, account,
                out uint length, out IntPtr data, out IntPtr item);
            if (status == ItemNotFound) return ValueTask.FromResult<byte[]?>(null);
            if (status != Success) throw new IOException($"macOS Keychain read failed ({status}).");
            try
            {
                if (length is 0 or > SecureSessionRecordCodec.MaximumBytes)
                    return ValueTask.FromResult<byte[]?>(null);
                byte[] result = new byte[length];
                Marshal.Copy(data, result, 0, result.Length);
                return ValueTask.FromResult<byte[]?>(result);
            }
            finally
            {
                if (data != IntPtr.Zero) _api.FreeContent(data);
                if (item != IntPtr.Zero) _api.Release(item);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(service);
            CryptographicOperations.ZeroMemory(account);
        }
    }

    public ValueTask WriteAsync(string backendScope, ReadOnlyMemory<byte> record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureSessionRecordCodec.ValidateScope(backendScope);
        if (record.Length is 0 or > SecureSessionRecordCodec.MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(record));
        byte[] service = Service(backendScope);
        byte[] account = Account();
        byte[] value = record.ToArray();
        try
        {
            int status = FindItem(service, account, out IntPtr item);
            if (status == Success)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    status = _api.ModifyContent(item, value);
                }
                finally { _api.Release(item); }
                if (status != Success)
                    throw new IOException($"macOS Keychain update failed ({status}).");
                return ValueTask.CompletedTask;
            }
            if (status != ItemNotFound)
                throw new IOException($"macOS Keychain lookup failed ({status}).");

            cancellationToken.ThrowIfCancellationRequested();
            status = _api.AddGenericPassword(service, account, value);
            if (status == DuplicateItem)
            {
                // Another writer won the add race. Update that item in place;
                // never delete first, because a failed add/update must not
                // destroy the last recoverable refresh record.
                status = FindItem(service, account, out item);
                if (status == Success)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        status = _api.ModifyContent(item, value);
                    }
                    finally { _api.Release(item); }
                }
            }
            if (status != Success) throw new IOException($"macOS Keychain write failed ({status}).");
            return ValueTask.CompletedTask;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(value);
            CryptographicOperations.ZeroMemory(service);
            CryptographicOperations.ZeroMemory(account);
        }
    }

    public ValueTask DeleteAsync(string backendScope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureSessionRecordCodec.ValidateScope(backendScope);
        byte[] service = Service(backendScope);
        byte[] account = Account();
        try
        {
            int status = _api.FindGenericPassword(service, account,
                out _, out IntPtr data, out IntPtr item);
            if (status == ItemNotFound) return ValueTask.CompletedTask;
            if (status != Success) throw new IOException($"macOS Keychain lookup failed ({status}).");
            try
            {
                if (data != IntPtr.Zero) _api.FreeContent(data);
                cancellationToken.ThrowIfCancellationRequested();
                status = _api.DeleteItem(item);
                if (status != Success) throw new IOException($"macOS Keychain delete failed ({status}).");
                return ValueTask.CompletedTask;
            }
            finally { if (item != IntPtr.Zero) _api.Release(item); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(service);
            CryptographicOperations.ZeroMemory(account);
        }
    }

    private int FindItem(byte[] service, byte[] account, out IntPtr item)
    {
        int status = _api.FindGenericPassword(service, account, out _,
            out IntPtr data, out item);
        if (data != IntPtr.Zero) _api.FreeContent(data);
        return status;
    }

    private static byte[] Account() => Encoding.UTF8.GetBytes("refresh");

    private static byte[] Service(string scope)
        => Encoding.UTF8.GetBytes(ServicePrefix + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(scope))).ToLowerInvariant());

}

/// <summary>Production implementation of the deliberately narrow Keychain
/// adapter. It contains no policy and is never used on Windows or Linux.</summary>
internal sealed class NativeMacKeychainApi : IMacKeychainApi
{
    public int FindGenericPassword(byte[] service, byte[] account, out uint length,
        out IntPtr data, out IntPtr item)
        => MacKeychain.SecKeychainFindGenericPassword(IntPtr.Zero,
            (uint)service.Length, service, (uint)account.Length, account,
            out length, out data, out item);

    public int AddGenericPassword(byte[] service, byte[] account, byte[] value)
        => MacKeychain.SecKeychainAddGenericPassword(IntPtr.Zero,
            (uint)service.Length, service, (uint)account.Length, account,
            (uint)value.Length, value, IntPtr.Zero);

    public int ModifyContent(IntPtr item, byte[] value)
        => MacKeychain.SecKeychainItemModifyContent(item, IntPtr.Zero,
            (uint)value.Length, value);

    public int DeleteItem(IntPtr item) => MacKeychain.SecKeychainItemDelete(item);

    public void Release(IntPtr item) => MacKeychain.CFRelease(item);

    public int FreeContent(IntPtr data)
        => MacKeychain.SecKeychainItemFreeContent(IntPtr.Zero, data);

    private static class MacKeychain
    {
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        public static extern int SecKeychainFindGenericPassword(IntPtr keychainList,
            uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
            out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        public static extern int SecKeychainAddGenericPassword(IntPtr keychain,
            uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
            uint passwordLength, byte[] passwordData, IntPtr itemRef);
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        public static extern int SecKeychainItemDelete(IntPtr itemRef);
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        public static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr itemContent);
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        public static extern int SecKeychainItemModifyContent(IntPtr itemRef, IntPtr attrList,
            uint length, byte[] data);
        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        public static extern void CFRelease(IntPtr cf);
    }
}
