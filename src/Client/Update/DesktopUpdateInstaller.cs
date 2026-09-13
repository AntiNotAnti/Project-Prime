using System;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Update;

/// <summary>Registers the desktop updater without leaking it into shared presentation.</summary>
internal static class DesktopUpdateRegistration
{
    public static void UseIfPossible()
    {
        if (UpdateInstall.Current == null && DesktopUpdate.Supported)
            UpdateInstall.Current = new DesktopUpdateInstaller();
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
