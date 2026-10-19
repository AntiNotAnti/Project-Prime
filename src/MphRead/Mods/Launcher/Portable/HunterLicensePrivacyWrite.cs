using System;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

/// <summary>
/// Fixed opt-out transaction. The existing authentication owner holds its gate
/// and supplies all calls with the same access token. No identity reaches RML.
/// </summary>
internal static class HunterLicensePrivacyWrite
{
    internal static Task<bool> HideAsync(Guid expectedUser,
        Func<CancellationToken, Task<Guid>> readAuthenticatedUser,
        Func<CancellationToken, Task<bool>> persistPrivate,
        Func<CancellationToken, Task<bool>> readPublicVisibility,
        CancellationToken cancellation)
        => WriteAsync(expectedUser, false, readAuthenticatedUser, persistPrivate, readPublicVisibility, cancellation);

    internal static async Task<bool> WriteAsync(Guid expectedUser, bool visible,
        Func<CancellationToken, Task<Guid>> readAuthenticatedUser,
        Func<CancellationToken, Task<bool>> persistVisibility,
        Func<CancellationToken, Task<bool>> readPublicVisibility,
        CancellationToken cancellation)
    {
        if (expectedUser == Guid.Empty) return false;
        cancellation.ThrowIfCancellationRequested();
        Guid actualUser = await readAuthenticatedUser(cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        if (actualUser != expectedUser) return false;
        bool writtenPublic = await persistVisibility(cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        if (writtenPublic != visible) return false;
        bool readPublic = await readPublicVisibility(cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        return readPublic == visible;
    }
}
