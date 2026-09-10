using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using MphRead.Mods.Update;

namespace MphRead.Droid
{
    /// <summary>
    /// The phone's half of <see cref="IUpdateInstaller"/>: everything real is
    /// in <see cref="ApkInstaller"/>, and this is the shape the front screen
    /// asks for it in.
    ///
    /// Installed in <c>MainActivity.CustomizeAppBuilder</c>, before the front
    /// screen is built, because the screen reads whether it exists while it is
    /// laying its update entry out.
    /// </summary>
    internal sealed class AndroidUpdateInstaller : IUpdateInstaller
    {
        private readonly Activity _activity;
        private string _staged = "";

        public AndroidUpdateInstaller(Activity activity)
        {
            _activity = activity;
        }

        public bool Allowed => ApkInstaller.Allowed(_activity);

        public bool RequestPermission() => ApkInstaller.RequestPermission(_activity);

        /// <summary>
        /// False: the system kills this app as it replaces it. Quitting on our
        /// own would take the screen away before the player has even answered
        /// the install dialog.
        /// </summary>
        public bool ExitAfterInstall => false;

        public Action<bool, string>? Finished
        {
            get => ApkInstaller.Finished;
            set => ApkInstaller.Finished = value;
        }

        public bool Prepare(UpdateInfo update, Action<float>? progress, out string error)
        {
            UpdatePrepareResult result = PrepareAsync(update,
                new Progress<UpdateProgress>(value => progress?.Invoke(
                    value.TotalBytes > 0 ? (float)value.Fraction : -1f)))
                .GetAwaiter().GetResult();
            error = result.Error ?? "";
            return result.Success;
        }

        public async Task<UpdatePrepareResult> PrepareAsync(UpdateInfo update,
            IProgress<UpdateProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _staged = ApkInstaller.StagingPath(_activity);
            if (update.Package is not UpdatePackage package || update.PackageUri == null)
                return new UpdatePrepareResult(false, "the signed Android package is unavailable");
            // Returning from the system permission/settings screen must not
            // download the same verified APK again. Re-hash the staged bytes
            // before trusting the cache, then repeat the signer check below.
            if (!IsVerifiedPackage(package))
            {
                DownloadResult downloaded = await UpdateDownload.DownloadAsync(package,
                    update.PackageUri, _staged, progress, cancellationToken).ConfigureAwait(false);
                if (!downloaded.Success)
                    return new UpdatePrepareResult(false, downloaded.Error ?? "the download failed");
            }
            if (!ApkInstaller.SameSigner(_activity, _staged, out string? mismatch))
                return new UpdatePrepareResult(false,
                    mismatch ?? "that package cannot be installed over this one");
            return new UpdatePrepareResult(true, null);
        }

        private bool IsVerifiedPackage(UpdatePackage package)
        {
            try
            {
                if (!File.Exists(_staged)) return false;
                FileInfo info = new(_staged);
                if (info.Length != package.Size) return false;
                using FileStream stream = File.OpenRead(_staged);
                string actual = Convert.ToHexString(SHA256.HashData(stream));
                bool matches = actual.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase);
                return matches;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool Install(out string error) =>
            ApkInstaller.Commit(_activity, _staged, out error);
    }
}
