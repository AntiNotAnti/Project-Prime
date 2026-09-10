using System;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Update;

public sealed record UpdatePrepareResult(bool Success, string? Error);
public sealed record UpdateInstallResult(bool Started, bool ExitAfterInstall, string? Error);

/// <summary>
/// Platform installation seam. The legacy synchronous methods remain as a
/// compatibility facade; orchestration uses the cancellation-aware async API.
/// </summary>
public interface IUpdateInstaller
{
    bool Allowed { get; }
    bool RequestPermission();
    bool Prepare(UpdateInfo update, Action<float>? progress, out string error);
    bool Install(out string error);
    bool ExitAfterInstall { get; }
    Action<bool, string>? Finished { get; set; }

    Task<UpdatePrepareResult> PrepareAsync(UpdateInfo update,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            bool ok = Prepare(update,
                fraction => progress?.Report(new UpdateProgress(
                    fraction < 0 ? 0 : Math.Max(0, (long)(fraction * 1000)),
                    fraction < 0 ? 0 : 1000)), out string error);
            return new UpdatePrepareResult(ok, ok ? null : error);
        }, cancellationToken);
    }

    Task<UpdateInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool started = Install(out string error);
        return Task.FromResult(new UpdateInstallResult(started, ExitAfterInstall,
            started ? null : error));
    }
}

/// <summary>The single platform installer registration point.</summary>
public static class UpdateInstall
{
    private static IUpdateInstaller? _current;
    public static IUpdateInstaller? Current
    {
        get => _current;
        set
        {
            _current = value;
            if (UpdateCoordinator.IsCreated)
                UpdateCoordinator.Shared.Installer = value;
        }
    }

    public static bool CanInstall(UpdateInfo update) =>
        Current != null && (update.PackageUri != null || update.AssetUrl.Length > 0);

    public static void UseDesktopIfPossible()
    {
        if (Current == null && DesktopUpdate.Supported)
            Current = new DesktopUpdateInstaller();
    }
}

internal sealed class DesktopUpdateInstaller : IUpdateInstaller
{
    public bool Allowed => true;
    public bool RequestPermission() => true;
    public bool ExitAfterInstall => true;
    public Action<bool, string>? Finished { get; set; }

    public bool Prepare(UpdateInfo update, Action<float>? progress, out string error)
    {
        bool ok = DesktopUpdate.Stage(update, progress);
        error = ok ? "" : DesktopUpdate.LastError ?? "the package could not be staged";
        return ok;
    }

    public async Task<UpdatePrepareResult> PrepareAsync(UpdateInfo update,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        bool ok = await DesktopUpdate.StageAsync(update, progress, cancellationToken)
            .ConfigureAwait(false);
        return new UpdatePrepareResult(ok,
            ok ? null : DesktopUpdate.LastError ?? "the package could not be staged");
    }

    public bool Install(out string error)
    {
        bool ok = DesktopUpdate.Launch();
        error = ok ? "" : DesktopUpdate.LastError ?? "the update could not be started";
        return ok;
    }

    public Task<UpdateInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool ok = Install(out string error);
        return Task.FromResult(new UpdateInstallResult(ok, ExitAfterInstall,
            ok ? null : error));
    }
}
