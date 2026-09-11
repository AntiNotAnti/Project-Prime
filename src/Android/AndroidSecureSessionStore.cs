using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Security;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using MphRead.Mods.Accounts;

namespace MphRead.Droid;

/// <summary>Android Keystore-backed encrypted refresh-token storage. The
/// Keystore key never leaves the device; only an AES-GCM envelope is persisted
/// in the app's private files directory.</summary>
public sealed class AndroidKeyStoreSessionStore : ISecureSessionStore
{
    private const string KeyStoreName = "AndroidKeyStore";
    private const int NonceBytes = 12;
    private readonly Context _context;
    private readonly string _directory;

    public AndroidKeyStoreSessionStore(Context context)
    {
        _context = context.ApplicationContext ?? context;
        _directory = Path.Combine(_context.FilesDir!.AbsolutePath, "project-prime-session");
        Directory.CreateDirectory(_directory);
    }

    public ValueTask<byte[]?> ReadAsync(string backendScope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureSessionRecordCodec.ValidateScope(backendScope);
        string path = PathFor(backendScope);
        if (!File.Exists(path)) return ValueTask.FromResult<byte[]?>(null);
        byte[] envelope = File.ReadAllBytes(path);
        if (envelope.Length <= NonceBytes)
        {
            QuarantineCorruptRecord(path);
            CryptographicOperations.ZeroMemory(envelope);
            return ValueTask.FromResult<byte[]?>(null);
        }
        ISecretKey key = KeyFor(backendScope);
        try
        {
            using Cipher cipher = Cipher.GetInstance("AES/GCM/NoPadding");
            cipher.Init(Javax.Crypto.CipherMode.DecryptMode, key,
                new GCMParameterSpec(128, envelope.AsSpan(0, NonceBytes).ToArray()));
            byte[] plain = cipher.DoFinal(envelope.AsSpan(NonceBytes).ToArray());
            if (plain.Length > SecureSessionRecordCodec.MaximumBytes)
            {
                CryptographicOperations.ZeroMemory(plain);
                QuarantineCorruptRecord(path);
                return ValueTask.FromResult<byte[]?>(null);
            }
            return ValueTask.FromResult<byte[]?>(plain);
        }
        catch (Exception exception) when (exception is Java.Lang.Exception
            or CryptographicException or InvalidOperationException)
        {
            // GCM authentication failures and malformed envelopes are not
            // recoverable records. Quarantine first so a later Activity
            // recreation cannot repeatedly feed the same corrupt bytes to the
            // Keystore. Keystore/key-creation failures occur outside the
            // decryption catch and remain observable to the caller.
            QuarantineCorruptRecord(path);
            return ValueTask.FromResult<byte[]?>(null);
        }
        finally { CryptographicOperations.ZeroMemory(envelope); }
    }

    public ValueTask WriteAsync(string backendScope, ReadOnlyMemory<byte> record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureSessionRecordCodec.ValidateScope(backendScope);
        if (record.Length is 0 or > SecureSessionRecordCodec.MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(record));
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        byte[] encrypted;
        try
        {
            using Cipher cipher = Cipher.GetInstance("AES/GCM/NoPadding");
            cipher.Init(Javax.Crypto.CipherMode.EncryptMode, KeyFor(backendScope),
                new GCMParameterSpec(128, nonce));
            encrypted = cipher.DoFinal(record.ToArray());
        }
        catch
        {
            CryptographicOperations.ZeroMemory(nonce);
            throw;
        }

        byte[] envelope = new byte[NonceBytes + encrypted.Length];
        // The nonce is not secret, but is unique for every write.
        Buffer.BlockCopy(nonce, 0, envelope, 0, NonceBytes);
        Buffer.BlockCopy(encrypted, 0, envelope, NonceBytes, encrypted.Length);
        CryptographicOperations.ZeroMemory(nonce);
        string path = PathFor(backendScope);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, envelope);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            CryptographicOperations.ZeroMemory(encrypted);
            CryptographicOperations.ZeroMemory(envelope);
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
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(scope))).ToLowerInvariant() + ".bin");

    private static void QuarantineCorruptRecord(string path)
    {
        if (!File.Exists(path)) return;
        string quarantine = path + ".corrupt-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Move(path, quarantine);
        }
        catch (IOException)
        {
            // A failed rename must not leave a repeatedly failing record in
            // the active slot. The path is app-private and contains only an
            // encrypted refresh record, so deletion is the safe fallback.
            try { File.Delete(path); }
            catch (IOException) { }
        }
        catch (UnauthorizedAccessException)
        {
            try { File.Delete(path); }
            catch (IOException) { }
        }
    }

    private static ISecretKey KeyFor(string scope)
    {
        string alias = "project-prime-session-" + Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(scope))).ToLowerInvariant();
        using KeyStore keyStore = KeyStore.GetInstance(KeyStoreName);
        keyStore.Load(null);
        if (keyStore.GetKey(alias, null) is ISecretKey existing) return existing;

        using KeyGenerator generator = KeyGenerator.GetInstance(
            KeyProperties.KeyAlgorithmAes, KeyStoreName);
        var specification = new KeyGenParameterSpec.Builder(alias,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .Build();
        generator.Init(specification);
        return generator.GenerateKey();
    }
}
