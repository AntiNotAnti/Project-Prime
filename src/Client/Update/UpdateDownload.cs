using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Update;

public sealed record UpdateProgress(long BytesReceived, long TotalBytes)
{
    public double Fraction => TotalBytes > 0
        ? Math.Min(1d, BytesReceived / (double)TotalBytes) : -1d;
}

public sealed record DownloadResult(
    bool Success,
    string Destination,
    long BytesReceived,
    string? Error)
{
    public bool Cancelled => String.Equals(Error, "cancelled", StringComparison.Ordinal);
}

/// <summary>
/// Bounded, hash-verifying streaming download for signed release packages.
/// </summary>
public static class UpdateDownload
{
    public const long MaxPackageBytes = UpdateManifestValidator.MaxPackageBytes;

    /// <summary>Compatibility diagnostic for old launcher callers.</summary>
    public static string? LastError { get; private set; }

    public static Task<DownloadResult> DownloadAsync(UpdatePackage package, Uri source,
        string destination, IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default, HttpMessageHandler? handler = null)
    {
        return DownloadCoreAsync(package, source, destination, progress,
            cancellationToken, handler);
    }

    public static Task<DownloadResult> DownloadAsync(UpdatePackage package, string source,
        string destination, IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default, HttpMessageHandler? handler = null)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri))
        {
            return Task.FromResult(new DownloadResult(false, destination, 0,
                "the download address is invalid"));
        }
        return DownloadAsync(package, uri, destination, progress, cancellationToken, handler);
    }

    /// <summary>
    /// Legacy facade retained for existing Android/launcher code. New paths
    /// use the asynchronous manifest overload.
    /// </summary>
    public static bool Fetch(string url, string path, long expectedBytes = 0,
        Action<float>? progress = null, CancellationToken cancel = default)
    {
        LastError = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? source))
        {
            LastError = "the download address is invalid";
            return false;
        }
        var package = new UpdatePackage(RuntimePlatform.Rid(),
            Path.GetFileName(source.AbsolutePath), expectedBytes > 0 ? expectedBytes : MaxPackageBytes,
            new string('0', 64));
        try
        {
            DownloadResult result = DownloadCoreAsync(package, source, path,
                new Progress<UpdateProgress>(value => progress?.Invoke(
                    value.TotalBytes > 0 ? (float)value.Fraction : -1f)),
                cancel, handler: null, verifyHash: false).GetAwaiter().GetResult();
            LastError = result.Error;
            return result.Success;
        }
        catch (OperationCanceledException)
        {
            LastError = "cancelled";
            return false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static async Task<DownloadResult> DownloadCoreAsync(UpdatePackage package,
        Uri source, string destination, IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken, HttpMessageHandler? handler,
        bool verifyHash = true)
    {
        string partial = destination + ".part";
        long received = 0;
        try
        {
            UpdateManifestValidator.Validate(new UpdateManifest(1, "stable", "1.0.0",
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), [package]));
            if (!UpdateTransport.IsAllowedUri(source))
                return Failure(destination, "the download address is not an allowed HTTPS host");
            if (package.Size <= 0 || package.Size > MaxPackageBytes)
                return Failure(destination, "package size is outside the allowed bounds");
            bool expectedLength = verifyHash || package.Size != MaxPackageBytes;
            string? parent = Path.GetDirectoryName(destination);
            if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            TryDeletePartial(partial);

            using HttpClient client = UpdateTransport.CreateClient(handler, out _);
            using HttpResponseMessage response = await UpdateTransport.SendFollowingRedirectsAsync(
                client, source, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Failure(destination, $"update host returned {(int)response.StatusCode}");
            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength is <= 0 or > MaxPackageBytes)
                return Failure(destination, "download length is outside the allowed bounds");
            if (expectedLength && contentLength.HasValue && contentLength.Value != package.Size)
                return Failure(destination, "download length does not match the signed package");

            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var output = new FileStream(partial, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[64 * 1024];
            while (true)
            {
                int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                cancellationToken.ThrowIfCancellationRequested();
                received += read;
                if (received > MaxPackageBytes || (expectedLength && received > package.Size))
                    return Failure(destination, "download exceeded the signed package size", received);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                progress?.Report(new UpdateProgress(received,
                    expectedLength ? package.Size : contentLength ?? 0));
            }
            if (expectedLength && received != package.Size)
                return Failure(destination, "the download ended before the signed length", received);
            if (verifyHash)
            {
                byte[] actual = hash.GetHashAndReset();
                if (!CryptographicOperations.FixedTimeEquals(actual,
                    Convert.FromHexString(package.Sha256)))
                    return Failure(destination, "download SHA-256 does not match the signed package", received);
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            File.Move(partial, destination, overwrite: true);
            progress?.Report(new UpdateProgress(received,
                expectedLength ? package.Size : contentLength ?? received));
            LastError = null;
            return new DownloadResult(true, destination, received, null);
        }
        catch (OperationCanceledException)
        {
            return Failure(destination, "cancelled", received);
        }
        catch (Exception ex)
        {
            return Failure(destination, ex.Message, received);
        }
        finally
        {
            TryDeletePartial(partial);
        }
    }

    private static DownloadResult Failure(string destination, string message, long received = 0)
    {
        LastError = message;
        return new DownloadResult(false, destination, received, message);
    }

    private static void TryDeletePartial(string partial)
    {
        try
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
