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

}
