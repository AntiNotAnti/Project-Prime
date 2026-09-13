using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Update;

/// <summary>
/// Compatibility facade for existing launcher surfaces. All stateful work is
/// delegated to the process-global <see cref="UpdateCoordinator"/>.
/// </summary>
public static class Updater
{
    private static readonly object Gate = new();
    private static UpdateInfo? _available;
    private static bool _checked;

    public static UpdateCoordinator Coordinator => UpdateCoordinator.Shared;
    public static bool Configured => !String.IsNullOrWhiteSpace(Branding.UpdateRepository);

    /// <summary>Set by -noupdate; it is absolute for this process.</summary>
    public static bool Disabled
    {
        get => Coordinator.Disabled;
        set => Coordinator.Disabled = value;
    }

    public static UpdateInfo? Available
    {
        get { lock (Gate) return _available; }
        private set { lock (Gate) _available = value; }
    }

    public static bool Checked
    {
        get { lock (Gate) return _checked; }
        private set { lock (Gate) _checked = value; }
    }

    public static UpdateInfo? Check(CancellationToken cancel = default)
    {
        if (Disabled || !Configured)
        {
            Checked = true;
            return null;
        }
        UpdateCheckResult result = Coordinator.CheckAsync(cancel, force: true)
            .GetAwaiter().GetResult();
        Checked = true;
        UpdateInfo? info = ToInfo(result);
        Available = info;
        return info;
    }

    public static void CheckInBackground(Action<UpdateInfo> found, Action? done = null)
    {
        if (Disabled || !Configured)
        {
            done?.Invoke();
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                Coordinator.SetPolicy(Launcher.LauncherPrefs.UpdatePolicy);
                // This method is called only by launcher surfaces. A game
                // match never checks here, so the launcher is an explicit
                // safe point for a staged install.
                Coordinator.SetSafeToRestart(true);
                UpdateCheckResult result = await Coordinator.CheckAsync(
                    CancellationToken.None, force: false).ConfigureAwait(false);
                Checked = true;
                UpdateInfo? info = ToInfo(result);
                Available = info;
                if (info is UpdateInfo available)
                {
                    found(available);
                    if (Launcher.LauncherPrefs.UpdatePolicy == UpdatePolicy.Automatic)
                    {
                        Coordinator.SetPolicy(UpdatePolicy.Automatic);
                        await Coordinator.DownloadAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[update] check failed: {ex.GetType().Name}");
            }
            finally
            {
                done?.Invoke();
            }
        });
    }

    /// <summary>
    /// Apply a package already verified and staged by the coordinator. This
    /// keeps launcher buttons from downloading the same package a second time
    /// after an automatic check.
    /// </summary>
    public static async Task<bool> InstallStagedAsync(
        CancellationToken cancellationToken = default)
    {
        IUpdateInstaller? installer = Coordinator.Installer;
        if (installer == null)
            return false;
        if (!installer.Allowed)
        {
            installer.RequestPermission();
            return false;
        }
        return await Coordinator.InstallIfReadyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Explicit launcher action for NotifyOnly (and a retry for Automatic).
    /// Downloading and installing remain coordinator operations so a manual
    /// click cannot race an automatic check or create a second package cache.
    /// </summary>
    public static async Task<bool> DownloadAndInstallAsync(
        CancellationToken cancellationToken = default)
    {
        if (Disabled || !Configured) return false;
        Coordinator.SetSafeToRestart(true);
        if (!await Coordinator.DownloadAsync(cancellationToken).ConfigureAwait(false))
            return false;
        return await InstallStagedAsync(cancellationToken).ConfigureAwait(false);
    }

    public static void WaitForCheck(TimeSpan limit)
    {
        if (Disabled || !Configured) return;
        DateTime until = DateTime.UtcNow + limit;
        while (!Checked && DateTime.UtcNow < until)
            Thread.Sleep(25);
    }

    public static bool OpenPage(UpdateInfo update) => OpenUrl(
        update.PageUrl.Length > 0 ? update.PageUrl : UpdateCheck.ReleasesPage);

    public static bool OpenLink(string url) => OpenUrl(url);

    public static string Describe(UpdateInfo update)
    {
        string which = update.AssetName.Length > 0 ? $" -- you want {update.AssetName}" : "";
        return $"{update.Tag} is available (this is {BuildVersion.Display}){which}";
    }

    private static UpdateInfo? ToInfo(UpdateCheckResult result)
    {
        if (result is not UpdateCheckResult.Available available) return null;
        Version version = Version.Parse(available.Manifest.Version);
        return new UpdateInfo
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
    }

    private static bool OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps)
            return false;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
                return true;
            }
            if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", new[] { uri.ToString() });
                return true;
            }
            if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
                && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                return false;
            Process.Start("xdg-open", new[] { uri.ToString() });
            return true;
        }
        catch (Exception) { return false; }
    }
}
