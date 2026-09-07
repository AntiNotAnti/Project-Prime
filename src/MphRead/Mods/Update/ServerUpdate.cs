using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;

namespace MphRead.Mods.Update;

public sealed record ServerUpdateOptions(string Repository, string ManifestAsset, string Rid,
    Version InstalledVersion, string Installation, ServerRestart Restart);
public sealed record ServerUpdateFile(string Path, long Bytes, string Sha256);
public sealed record ServerUpdateManifest(int Format, NetWireFamily Family, byte Protocol, string Version,
    string Rid, string Package, long PackageBytes, string PackageSha256, string Executable, ServerUpdateFile[] Files);
public sealed record ServerUpdateStage(string Directory, ServerUpdateManifest Manifest);

internal sealed class ServerUpdateValidatorCleanupException(string message, Exception inner) : IOException(message, inner);

/// <summary>One installation's opt-in update. Only PollIdle calls owner-thread lifecycle callbacks.</summary>
public sealed class ServerUpdate : IDisposable
{
    internal const int MetadataLimit = 1024 * 1024;
    internal const long PackageLimit = 256L * 1024 * 1024;
    internal const long ExpandedLimit = 512L * 1024 * 1024;
    private readonly ServerUpdateOptions _options;
    private readonly HttpClient _http;
    private readonly TimeSpan _downloadTimeout;
    private readonly Func<ServerUpdateStage, CancellationToken, Task<string?>> _validate;
    private readonly FileStream _lock;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _stageRoot;
    private Task<ServerUpdateStage?>? _pending;
    private long _nextCheck;
    private bool _handoff;
    private bool _preserveStage;
    private bool _disposed;
    public string? LastError { get; private set; }
    public ServerUpdateStage? Ready { get; private set; }

    public static bool Enabled(ReadOnlySpan<string> arguments)
    {
        bool enabled = false;
        foreach (string argument in arguments)
        {
            if (argument.Equals("-noupdate", StringComparison.OrdinalIgnoreCase)) { return false; }
            enabled |= argument.Equals("-autoupdate", StringComparison.OrdinalIgnoreCase);
        }
        return enabled;
    }

    public ServerUpdate(ServerUpdateOptions options,
        Func<ServerUpdateStage, CancellationToken, Task<string?>> validate)
        : this(options, validate, new HttpClientHandler { AllowAutoRedirect = false }) { }

    internal ServerUpdate(ServerUpdateOptions options,
        Func<ServerUpdateStage, CancellationToken, Task<string?>> validate, HttpMessageHandler handler,
        TimeSpan? downloadTimeout = null)
    {
        if (!Regex.IsMatch(options.Repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
            || options.Repository.Equals("liveteklol/Fruity-Prime", StringComparison.OrdinalIgnoreCase))
        { throw new ArgumentException("Configure an explicit authoritative release repository; relay upstream is not an update channel."); }
        if (OperatingSystem.IsWindows() && options.Restart.Supervised)
        { throw new ArgumentException("Windows service supervisors need an explicit update handoff; only standalone Windows updating is supported."); }
        SafeRelative(options.ManifestAsset);
        if (options.ManifestAsset.Contains('/')) { throw new ArgumentException("Manifest asset must be a filename."); }
        string installation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Installation));
        RejectLinks(installation);
        if (!Directory.Exists(installation)) { throw new DirectoryNotFoundException(installation); }
        if (options.InstalledVersion == null || BuildVersion.Parse(options.InstalledVersion.ToString()) == null)
        { throw new ArgumentException("Unstamped local builds are not eligible for unattended updates."); }
        _options = options with
        {
            Installation = installation,
            InstalledVersion = BuildVersion.Normalise(options.InstalledVersion),
            Restart = options.Restart with { Arguments = (string[])options.Restart.Arguments.Clone() }
        };
        _validate = validate;
        _downloadTimeout = downloadTimeout ?? TimeSpan.FromMinutes(10);
        if (_downloadTimeout <= TimeSpan.Zero || _downloadTimeout > TimeSpan.FromMinutes(10))
        { throw new ArgumentOutOfRangeException(nameof(downloadTimeout)); }
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FruityPrime-Authoritative-Updater/1");
        _stageRoot = StageRoot(installation);
        Directory.CreateDirectory(_stageRoot);
        RejectLinks(_stageRoot);
        try { _lock = new FileStream(Path.Combine(_stageRoot, "installation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch { _http.Dispose(); _stop.Dispose(); throw; }
    }

    internal static string StageRoot(string installation)
    {
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(installation)))[..16];
        return Path.Combine(Path.GetDirectoryName(installation)!, ".fruity-server-updates-" + identity);
    }

    /// <summary>Starts at startup and every fifteen minutes. No network or validation runs on the owner thread.</summary>
    public void CheckWhenDue()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long now = Stopwatch.GetTimestamp();
        if (Ready != null || _pending != null || now < _nextCheck || _handoff) { return; }
        _nextCheck = now + 15 * 60 * Stopwatch.Frequency;
        _pending = Task.Run(() => CheckAsync(_stop.Token));
    }

    /// <summary>Counts must include admitted/loading players, or all owned/pending child matches.</summary>
    public bool PollIdle(Func<bool> idle, Action stopAdmission, Action resumeAdmission,
        Action<ServerRestart> restart, Func<ProcessStartInfo, bool>? launchHelper = null)
    {
        if (_pending is { IsCompleted: true }) { Ready = _pending.GetAwaiter().GetResult(); _pending = null; }
        if (Ready == null || _handoff || !idle()) { return false; }
        stopAdmission();
        try
        {
            // This owner thread must make idle inspection and admission closure indivisible.
            if (!idle()) { resumeAdmission(); return false; }
            VerifyStage(Ready);
            if (OperatingSystem.IsWindows())
            {
                string planPath = ServerUpdateInstall.WritePlan(Ready, _options.Installation, _options.Restart, Environment.ProcessId);
                ProcessStartInfo helper = ServerUpdateInstall.HelperStart(Ready, planPath);
                if (!(launchHelper?.Invoke(helper) ?? (Process.Start(helper) != null)))
                { throw new IOException("The staged update helper did not start."); }
            }
            else
            {
                // Unix rename replaces mapped binaries safely. Complete installation before
                // signalling exit: systemd Restart=always may restart immediately afterward.
                ServerUpdateInstall.Apply(Ready, _options.Installation);
            }
        }
        catch (Exception error)
        {
            LastError = error.Message;
            if (error is AggregateException) { _handoff = _preserveStage = true; throw; } // Incomplete rollback: keep admission closed.
            DiscardReady();
            resumeAdmission();
            return false;
        }
        _handoff = true;
        _preserveStage = OperatingSystem.IsWindows();
        // Installation is committed: an exit-callback failure must never reopen admission.
        restart(_options.Restart);
        return true;
    }

    internal async Task<ServerUpdateStage?> CheckAsync(CancellationToken cancel)
    {
        string? directory = null;
        try
        {
            LastError = null;
            byte[] metadata = await Download(new Uri($"https://api.github.com/repos/{_options.Repository}/releases/latest"), MetadataLimit, null, cancel);
            using JsonDocument release = JsonDocument.Parse(metadata);
            string tag = release.RootElement.GetProperty("tag_name").GetString() ?? "";
            Version? version = BuildVersion.Parse(tag);
            if (version == null || version <= _options.InstalledVersion) { return null; }
            if (release.RootElement.TryGetProperty("draft", out JsonElement draft) && draft.GetBoolean()
                || release.RootElement.TryGetProperty("prerelease", out JsonElement prerelease) && prerelease.GetBoolean()) { return null; }
            string? manifestUrl = null;
            foreach (JsonElement asset in release.RootElement.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() == _options.ManifestAsset)
                {
                    if (manifestUrl != null) { throw new IOException("Duplicate update manifest assets."); }
                    manifestUrl = asset.GetProperty("browser_download_url").GetString();
                }
            }
            if (manifestUrl == null) { throw new IOException("Release has no authoritative update manifest for this package."); }
            Uri manifestUri = AssetUri(manifestUrl, tag, _options.ManifestAsset);
            byte[] json = await Download(manifestUri, MetadataLimit, null, cancel);
            ServerUpdateManifest manifest = JsonSerializer.Deserialize<ServerUpdateManifest>(json)
                ?? throw new IOException("Invalid update manifest.");
            ValidateManifest(manifest, version);
            Uri packageUri = AssetUri($"https://github.com/{_options.Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(manifest.Package)}", tag, manifest.Package);
            int retained = 0;
            foreach (string _ in Directory.EnumerateDirectories(_stageRoot))
            { if (++retained >= 4) { throw new IOException("Four retained update stages need operator review before another download."); } }
            directory = Path.Combine(_stageRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string archive = Path.Combine(directory, ".package.zip");
            using (var package = new FileStream(archive, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                await DownloadTo(packageUri, package, manifest.PackageBytes, manifest.PackageBytes, cancel);
                package.Position = 0;
                if (!Convert.ToHexString(SHA256.HashData(package)).Equals(manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
                { throw new IOException("Update archive hash mismatch."); }
            }
            Extract(archive, directory, manifest);
            File.Delete(archive);
            var staged = new ServerUpdateStage(directory, manifest);
            VerifyStage(staged);
            using var validation = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            validation.CancelAfter(TimeSpan.FromMinutes(2));
            // The owned validator observes cancellation and reaps its process before returning.
            // Do not race its cleanup with stage deletion through an outer WaitAsync.
            string? failure = await _validate(staged, validation.Token);
            if (failure != null) { throw new IOException("Staged content validation failed: " + failure); }
            // Validation runs the new binary. Recheck every byte before handing it to the owner.
            VerifyStage(staged);
            return staged;
        }
        catch (Exception error)
        {
            LastError = error is OperationCanceledException ? "Update check cancelled." : error.Message;
            if (directory != null && error is ServerUpdateValidatorCleanupException)
            {
                LastError += " Validator exit was not confirmed; preserve stage for operator review: " + directory;
            }
            else if (directory != null)
            {
                try { Directory.Delete(directory, true); }
                catch (IOException cleanup) { LastError += " Stage cleanup failed: " + cleanup.Message; }
                catch (UnauthorizedAccessException cleanup) { LastError += " Stage cleanup failed: " + cleanup.Message; }
            }
            return null;
        }
    }

    private Uri AssetUri(string address, string tag, string asset)
    {
        SafeRelative(asset);
        if (asset.Contains('/')) { throw new IOException("Update asset must be a filename."); }
        var expected = new Uri($"https://github.com/{_options.Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(asset)}");
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) || uri != expected)
        { throw new IOException("Update asset does not belong to the configured repository and release."); }
        return uri;
    }

    private async Task<byte[]> Download(Uri uri, long limit, long? exact, CancellationToken cancel)
    {
        using var target = new MemoryStream();
        await DownloadTo(uri, target, limit, exact, cancel);
        return target.ToArray();
    }

    private async Task DownloadTo(Uri uri, Stream target, long limit, long? exact, CancellationToken cancel)
    {
        // ResponseHeadersRead ends HttpClient.Timeout at the response headers.
        // One deadline must also cover every redirect and the entire streamed body.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(_downloadTimeout);
        cancel = deadline.Token;
        for (int redirects = 0; ; redirects++)
        {
            using HttpResponseMessage response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                Uri? location = response.Headers.Location;
                if (redirects >= 3 || location == null) { throw new IOException("Too many or invalid update redirects."); }
                Uri next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (next.Scheme != "https" || next.Port != 443 || next.UserInfo.Length != 0
                    || next.Fragment.Length != 0 || !(next.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                { throw new IOException("Update redirect is outside trusted release download hosts."); }
                uri = next;
                continue;
            }
            response.EnsureSuccessStatusCode();
            long? advertised = response.Content.Headers.ContentLength;
            if (advertised > limit || (exact.HasValue && advertised.HasValue && advertised != exact))
            { throw new IOException("Update download length exceeds its manifest bound."); }
            using Stream source = await response.Content.ReadAsStreamAsync(cancel);
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                if (target.Length + read > limit) { throw new IOException("Update download exceeds its bound."); }
                await target.WriteAsync(buffer.AsMemory(0, read), cancel);
            }
            if (exact.HasValue && target.Length != exact) { throw new IOException("Update download is truncated."); }
            return;
        }
    }

    private void ValidateManifest(ServerUpdateManifest manifest, Version release)
    {
        if (manifest.Format != 1 || manifest.Family != NetWireFamily.Authoritative
            || manifest.Protocol != NetHeader.Version || BuildVersion.Parse(manifest.Version) != release
            || manifest.Rid != _options.Rid || !manifest.Package.EndsWith(".zip", StringComparison.Ordinal)
            || manifest.PackageBytes is <= 0 or > PackageLimit || !ValidHash(manifest.PackageSha256)
            || manifest.Files == null || manifest.Files.Length is < 1 or > 2048)
        { throw new IOException("Update manifest identity, version, or bounds are invalid."); }
        SafeRelative(manifest.Executable);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (ServerUpdateFile file in manifest.Files)
        {
            SafeRelative(file.Path);
            if (!names.Add(file.Path) || file.Bytes is < 0 or > ExpandedLimit || !ValidHash(file.Sha256)
                || (total += file.Bytes) > ExpandedLimit) { throw new IOException("Invalid or duplicate update file."); }
        }
        if (!names.Contains(manifest.Executable)) { throw new IOException("Manifest does not include its executable."); }
    }

    private static void Extract(string package, string directory, ServerUpdateManifest manifest)
    {
        using var archive = ZipFile.OpenRead(package);
        var expected = new Dictionary<string, ServerUpdateFile>(StringComparer.Ordinal);
        foreach (ServerUpdateFile file in manifest.Files) { expected.Add(file.Path, file); }
        if (archive.Entries.Count != expected.Count) { throw new IOException("Archive and manifest file counts differ."); }
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            SafeRelative(entry.FullName);
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType is not (0 or 0x8000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || !expected.Remove(entry.FullName, out ServerUpdateFile? file)
                || entry.Length != file.Bytes) { throw new IOException("Unexpected archive entry, link, or length."); }
            string path = Path.Combine(directory, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using Stream input = entry.Open();
            using Stream output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            byte[] buffer = new byte[64 * 1024];
            long total = 0;
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                if ((total += read) > file.Bytes) { throw new IOException("Expanded file exceeds its bound."); }
                output.Write(buffer, 0, read);
            }
            if (total != file.Bytes) { throw new IOException("Expanded file is truncated."); }
        }
        if (!OperatingSystem.IsWindows())
        {
            string executable = Path.Combine(directory, manifest.Executable);
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    internal static void VerifyStage(ServerUpdateStage stage)
    {
        if (stage.Manifest.Format != 1 || stage.Manifest.Family != NetWireFamily.Authoritative
            || stage.Manifest.Protocol != NetHeader.Version || stage.Manifest.Files == null
            || stage.Manifest.Files.Length is < 1 or > 2048) { throw new IOException("Invalid staged identity."); }
        SafeRelative(stage.Manifest.Executable);
        if (!Array.Exists(stage.Manifest.Files, file => file.Path == stage.Manifest.Executable))
        { throw new IOException("Staged executable is missing from the verified inventory."); }
        RejectLinks(stage.Directory);
        foreach (ServerUpdateFile file in stage.Manifest.Files)
        {
            SafeRelative(file.Path);
            string path = Path.Combine(stage.Directory, file.Path);
            RejectLinks(path);
            using var stream = File.OpenRead(path);
            if (stream.Length != file.Bytes || !Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            { throw new IOException("Staged file hash mismatch: " + file.Path); }
        }
    }

    internal static void SafeRelative(string path)
    {
        if (String.IsNullOrEmpty(path) || path.Length > 240 || path.Contains('\\') || path.Contains(':')
            || path.StartsWith('/') || path.Contains('\0')) { throw new IOException("Unsafe update path."); }
        foreach (char character in path)
        { if (character < 32 || character == 127) { throw new IOException("Unsafe update path."); } }
        foreach (string part in path.Split('/'))
        {
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.StartsWith(".", StringComparison.Ordinal)
                || Regex.IsMatch(part, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\\.|$)", RegexOptions.IgnoreCase)) { throw new IOException("Unsafe update path."); }
        }
    }

    internal static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { throw new IOException("Update paths cannot traverse links."); }
            }
        }
    }
    private static bool ValidHash(string hash) => hash != null && Regex.IsMatch(hash, "^[A-Fa-f0-9]{64}$");

    private void DiscardReady()
    {
        string? directory = Ready?.Directory;
        Ready = null;
        if (directory == null) { return; }
        try { Directory.Delete(directory, true); }
        catch (IOException cleanup) { LastError += " Stage cleanup failed: " + cleanup.Message; }
        catch (UnauthorizedAccessException cleanup) { LastError += " Stage cleanup failed: " + cleanup.Message; }
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _stop.Cancel();
        if (_pending != null) { Ready = _pending.GetAwaiter().GetResult(); }
        if (!_preserveStage) { DiscardReady(); }
        // Handoff artifacts remain recoverable; no startup-wide cleanup can delete
        // another process's validator/helper or a failed installation's backups.
        _lock.Dispose(); _http.Dispose(); _stop.Dispose();
    }
}
