using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Presentation;

namespace MphRead.Mods.Update;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    NotApplicable,
    Available,
    Downloading,
    Verifying,
    Staged,
    WaitingForSafePoint,
    Installing,
    Restarting,
    Failed
}

public sealed record UpdateStatus(
    UpdateState State,
    Version? InstalledVersion,
    Version? AvailableVersion,
    long BytesReceived,
    long TotalBytes,
    string? Message);

/// <summary>
/// Application-wide owner of update state, transport, staging and install
/// claims. Launcher views only observe this object; they do not issue their own
/// network requests or make safe-point decisions.
/// </summary>
public sealed class UpdateCoordinator
{
    private static readonly Lazy<UpdateCoordinator> SharedInstance = new(() =>
        new UpdateCoordinator(new UpdateManifestClient(), UpdateInstall.Current));

    public static UpdateCoordinator Shared => SharedInstance.Value;
    internal static bool IsCreated => SharedInstance.IsValueCreated;

    private readonly object _gate = new();
    private readonly UpdateManifestClient _client;
    private IUpdateInstaller? _installer;
    private UpdateStatus _status;
    private UpdateCheckResult.Available? _available;
    private Task<UpdateCheckResult>? _checkTask;
    private UpdateCheckResult? _lastCheckResult;
    private bool _hasCompletedCheck;
    private Task<bool>? _downloadTask;
    private Task<bool>? _installTask;
    private bool _safeToRestart;
    private bool _disabled;
    private int _playLeases;
    private bool _installClaim;
    private UpdatePolicy _policy = UpdatePolicy.Automatic;

    public UpdateCoordinator(UpdateManifestClient client, IUpdateInstaller? installer = null,
        Version? installedVersion = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _installer = installer;
        _status = new UpdateStatus(UpdateState.Idle,
            installedVersion ?? BuildVersion.Current, null, 0, 0, null);
    }

    public event EventHandler<UpdateStatus>? StatusChanged;

    public UpdateStatus Status
    {
        get { lock (_gate) return _status; }
    }

    public UpdatePolicy Policy
    {
        get { lock (_gate) return _policy; }
    }

    public IUpdateInstaller? Installer
    {
        get { lock (_gate) return _installer; }
        set { lock (_gate) _installer = value; }
    }

    public bool Disabled
    {
        get { lock (_gate) return _disabled; }
        set
        {
            UpdateState state;
            lock (_gate)
            {
                _disabled = value;
                state = _status.State;
            }
            if (value && state is not (UpdateState.Staged or UpdateState.WaitingForSafePoint
                or UpdateState.Installing or UpdateState.Restarting))
                SetStatus(UpdateState.Idle, "updates disabled for this run");
        }
    }

    public void SetPolicy(UpdatePolicy policy)
    {
        UpdateState state;
        lock (_gate)
        {
            _policy = policy;
            state = _status.State;
        }
        if (policy == UpdatePolicy.Off && state is not (UpdateState.Staged
            or UpdateState.WaitingForSafePoint or UpdateState.Installing or UpdateState.Restarting))
            SetStatus(UpdateState.Idle, "automatic updates are off");
    }

    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default,
        bool force = false)
    {
        lock (_gate)
        {
            if (_disabled || (!force && _policy == UpdatePolicy.Off))
                return Task.FromResult<UpdateCheckResult>(
                    new UpdateCheckResult.Failed("updates are disabled"));
            if (_checkTask != null) return _checkTask;
            if (force && (_downloadTask != null || _installTask != null
                || _status.State is UpdateState.Downloading or UpdateState.Verifying
                or UpdateState.Staged or UpdateState.WaitingForSafePoint
                or UpdateState.Installing or UpdateState.Restarting))
            {
                return Task.FromResult<UpdateCheckResult>(
                    new UpdateCheckResult.Failed("an update operation is already in progress"));
            }
            if (!force && _hasCompletedCheck && _lastCheckResult != null)
                return Task.FromResult(_lastCheckResult);
            // Start outside the lock so the first synchronous state event from
            // CheckCoreAsync cannot run while a caller owns coordinator state.
            _checkTask = Task.Run(() => CheckCoreAsync(cancellationToken));
            return _checkTask;
        }
    }

    public Task<bool> DownloadAsync(CancellationToken cancellationToken = default)
    {
        bool noUpdate;
        lock (_gate)
        {
            if (_downloadTask != null) return _downloadTask;
            if (_status.State is UpdateState.Staged or UpdateState.WaitingForSafePoint
                or UpdateState.Installing or UpdateState.Restarting)
                return Task.FromResult(true);
            UpdateCheckResult.Available? available = _available;
            noUpdate = available == null;
            if (!noUpdate)
                _downloadTask = Task.Run(() => DownloadCoreAsync(available!, cancellationToken));
        }
        if (noUpdate)
        {
            SetStatus(UpdateState.Failed, "there is no verified update to download");
            return Task.FromResult(false);
        }
        return _downloadTask!;
    }

    public Task<bool> InstallIfReadyAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_disabled || _policy == UpdatePolicy.Off)
                return Task.FromResult(false);
            if (_installTask != null) return _installTask;
            _installTask = Task.Run(() => InstallIfReadyCoreAsync(cancellationToken));
            return _installTask;
        }
    }

    private async Task<bool> InstallIfReadyCoreAsync(CancellationToken cancellationToken)
    {
        bool launched = false;
        try
        {
            lock (_gate)
            {
                if (_disabled || _policy == UpdatePolicy.Off)
                    return false;
            }
            IUpdateInstaller? installer;
            UpdateState state;
            bool shouldWait;
            lock (_gate)
            {
                state = _status.State;
                shouldWait = (state is UpdateState.Staged or UpdateState.WaitingForSafePoint)
                    && (!_safeToRestart || _playLeases != 0);
                if (_installClaim || state is UpdateState.Installing or UpdateState.Restarting
                    || !_safeToRestart || _playLeases != 0
                    || state is not (UpdateState.Staged or UpdateState.WaitingForSafePoint))
                {
                    installer = null;
                }
                else
                {
                    installer = _installer;
                    if (installer != null) _installClaim = true;
                }
            }

            if (shouldWait)
                SetStatus(UpdateState.WaitingForSafePoint, "update ready; waiting for a safe restart point");
            if (installer == null)
            {
                if (!shouldWait && state is (UpdateState.Staged or UpdateState.WaitingForSafePoint))
                    SetStatus(UpdateState.Failed, "this platform cannot install updates automatically");
                return false;
            }

            SetStatus(UpdateState.Installing, "installing update");
            if (!installer.ExitAfterInstall)
            {
                // Android completes asynchronously through PackageInstaller.
                // A cancellation or failure must release the install claim so
                // the still-running, still-valid player can retry or play.
                installer.Finished = InstallerFinished;
            }
            UpdateInstallResult result = await installer.InstallAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!result.Started)
            {
                SetStatus(UpdateState.Failed, result.Error ?? "the update could not be started");
                return false;
            }
            SetStatus(UpdateState.Restarting, result.ExitAfterInstall
                ? "restarting Project Prime" : "waiting for the system installer");
            // The claim remains held after the updater has launched. The
            // current process must not begin a new match while the staged
            // process owns installation/restart responsibility.
            launched = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            SetStatus(UpdateState.Failed, "update install cancelled");
            return false;
        }
        catch (Exception ex)
        {
            SetStatus(UpdateState.Failed, $"update install failed ({ex.GetType().Name})");
            return false;
        }
        finally
        {
            lock (_gate)
            {
                if (!launched) _installClaim = false;
                _installTask = null;
            }
        }
    }

    private void InstallerFinished(bool success, string message)
    {
        if (success)
        {
            SetStatus(UpdateState.Restarting,
                String.IsNullOrWhiteSpace(message) ? "update installed" : message);
            return;
        }
        lock (_gate) _installClaim = false;
        SetStatus(UpdateState.Failed, String.IsNullOrWhiteSpace(message)
            ? "the system installer did not install the update" : message);
    }

    public void SetSafeToRestart(bool safe)
    {
        bool waiting;
        bool becameReady;
        lock (_gate)
        {
            _safeToRestart = safe;
            waiting = !safe && _status.State == UpdateState.Staged;
            becameReady = safe && _playLeases == 0
                && _status.State == UpdateState.WaitingForSafePoint;
        }
        if (waiting)
            SetStatus(UpdateState.WaitingForSafePoint, "update ready; waiting for a safe restart point");
        else if (becameReady)
            SetStatus(UpdateState.Staged, "update staged; ready to restart");
    }

    public IDisposable? AcquirePlayLease() => AcquireLease();
    public IDisposable? AcquireCriticalOperationLease() => AcquireLease();

    public bool HasInstallClaim
    {
        get { lock (_gate) return _installClaim; }
    }

    private IDisposable? AcquireLease()
    {
        lock (_gate)
        {
            if (_installClaim || _status.State is UpdateState.Installing or UpdateState.Restarting)
                return null;
            _playLeases++;
            return new Lease(ReleaseLease);
        }
    }

    private void ReleaseLease()
    {
        lock (_gate)
        {
            if (_playLeases > 0) _playLeases--;
        }
    }

    private async Task<UpdateCheckResult> CheckCoreAsync(CancellationToken cancellationToken)
    {
        SetStatus(UpdateState.Checking, "checking stable updates");
        UpdateCheckResult result;
        try
        {
            result = await _client.CheckAsync(UpdateChannel.Stable,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = new UpdateCheckResult.Failed("update check cancelled");
        }
        catch (Exception ex)
        {
            result = new UpdateCheckResult.Failed(
                $"could not check for updates ({ex.GetType().Name})");
        }
        lock (_gate)
        {
            _lastCheckResult = result;
            _hasCompletedCheck = true;
            switch (result)
            {
                case UpdateCheckResult.UpToDate:
                    _available = null;
                    break;
                case UpdateCheckResult.Available available:
                    _available = SnapshotAvailable(available);
                    break;
                default:
                    _available = null;
                    break;
            }
        }
        switch (result)
        {
            case UpdateCheckResult.UpToDate:
                SetStatus(UpdateState.UpToDate, "Project Prime is up to date");
                break;
            case UpdateCheckResult.Available available:
                SetStatus(UpdateState.Available, $"update {available.Manifest.Version} is available");
                break;
            case UpdateCheckResult.Failed failed:
                // SetStatus owns the one translation boundary. Keeping the
                // low-level diagnostic here also lets the logger retain its
                // context without ever sending it to the route views.
                DebugLog.Line("update", $"Update check failed: {failed.Message}");
                SetStatus(UpdateState.Failed, failed.Message);
                break;
            case UpdateCheckResult.NotApplicable notApplicable:
                SetStatus(UpdateState.NotApplicable,
                    PrimeUserMessage.ForUpdate(notApplicable).Text);
                DebugLog.Line("update", $"Update check skipped: {notApplicable.Reason}");
                break;
        }
        // Keep the completed task published until all state/event work for the
        // check has finished. A caller racing the final status notification
        // must coalesce onto this task rather than start a second request.
        lock (_gate) _checkTask = null;
        return result;
    }

    private static UpdateCheckResult.Available SnapshotAvailable(
        UpdateCheckResult.Available available)
    {
        UpdateManifest manifest = available.Manifest with
        {
            Packages = available.Manifest.Packages.ToArray()
        };
        UpdatePackage package = available.Package with { };
        return available with { Manifest = manifest, Package = package };
    }

    private async Task<bool> DownloadCoreAsync(UpdateCheckResult.Available available,
        CancellationToken cancellationToken)
    {
        SetStatus(UpdateState.Downloading, "downloading update");
        try
        {
            IUpdateInstaller? installer;
            lock (_gate) installer = _installer;
            if (installer == null)
            {
                SetStatus(UpdateState.Failed, "this platform cannot install updates automatically");
                return false;
            }
            Version version = Version.Parse(available.Manifest.Version);
            var update = new UpdateInfo
            {
                Tag = "v" + available.Manifest.Version,
                Version = version,
                AssetName = available.Package.FileName,
                AssetUrl = available.PackageUri.ToString(),
                AssetSize = available.Package.Size,
                PageUrl = "https://github.com/" + Branding.UpdateRepository + "/releases",
                Notes = "",
                Manifest = available.Manifest,
                Package = available.Package,
                PackageUri = available.PackageUri
            };
            var progress = new Progress<UpdateProgress>(value => SetProgress(value));
            UpdatePrepareResult prepared = await installer.PrepareAsync(update, progress,
                cancellationToken).ConfigureAwait(false);
            if (!prepared.Success)
            {
                SetStatus(UpdateState.Failed, prepared.Error ?? "the update could not be staged");
                return false;
            }
            SetStatus(UpdateState.Verifying, "verifying and staging update");
            bool safe;
            lock (_gate) safe = _safeToRestart && _playLeases == 0;
            SetStatus(safe
                ? UpdateState.Staged : UpdateState.WaitingForSafePoint,
                safe
                    ? "update staged" : "update ready; waiting for a safe restart point");
            return true;
        }
        catch (OperationCanceledException)
        {
            SetStatus(UpdateState.Failed, "update download cancelled");
            return false;
        }
        catch (Exception ex)
        {
            SetStatus(UpdateState.Failed, $"update download failed ({ex.GetType().Name})");
            return false;
        }
        finally
        {
            lock (_gate) _downloadTask = null;
        }
    }

    private void SetProgress(UpdateProgress progress)
    {
        UpdateStatus status;
        lock (_gate)
        {
            status = _status with
            {
                BytesReceived = progress.BytesReceived,
                TotalBytes = progress.TotalBytes
            };
            _status = status;
        }
        RaiseStatusChanged(status);
    }

    private void SetStatus(UpdateState state, string? message)
    {
        if (state == UpdateState.Failed && message != null)
            message = PrimeUserMessage.Translate(message,
                PrimeUserMessageSeverity.Error).Text;
        UpdateStatus status;
        lock (_gate)
        {
            status = _status with
            {
                State = state,
                AvailableVersion = _available == null
                    ? null : Version.Parse(_available.Manifest.Version),
                Message = message
            };
            _status = status;
        }
        RaiseStatusChanged(status);
    }

    private void RaiseStatusChanged(UpdateStatus status)
    {
        try { StatusChanged?.Invoke(this, status); }
        catch (Exception) { }
    }

    private sealed class Lease : IDisposable
    {
        private Action? _release;
        public Lease(Action release) => _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
