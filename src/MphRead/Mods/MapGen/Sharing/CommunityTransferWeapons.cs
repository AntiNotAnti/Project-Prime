using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.CustomWeapons.Packages;

namespace MphRead.Mods.MapGen;

public sealed record CommunityWeaponDownload(Guid ContentId, string Name, CommunityContentRevision Revision);

public sealed partial class CommunityTransferManager
{
    private string WeaponDownloadPath(Guid id) => Path.Combine(_root, "archives", id.ToString("N") + ".ppweapon");
    private void DeletePrivateWeaponDownload(Guid id)
    {
        string path = WeaponDownloadPath(id);
        if (File.Exists(path)) File.Delete(path);
    }
    private static T FreezeWeaponMetadata<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, MapPackageReader.JsonOptions), MapPackageReader.JsonOptions)!;
    private static void ValidateWeaponDownload(CommunityWeaponDownload value)
    {
        var revision = value.Revision;
        if (value.ContentId == Guid.Empty || string.IsNullOrWhiteSpace(value.Name) || value.Name.Length > 128
            || revision == null || revision.Visibility is not (CommunityContentVisibility.Published or CommunityContentVisibility.Unlisted or CommunityContentVisibility.Draft) || revision.RevisionNumber < 1 || revision.MimeType != CommunityAssetValidator.WeaponMimeType
            || revision.Bytes is <= 0 or > WeaponPackageLimits.MaximumArchiveBytes
            || !MapCommunityClient.ValidHash(revision.ContentHash) || revision.ArchiveHash != null && revision.ArchiveHash != revision.ContentHash
            || revision.WeaponPackage is not { } identity || identity.ArchiveHash != revision.ContentHash
            || !MapCommunityClient.ValidHash(identity.ContentHash) || !MapCommunityClient.ValidHash(identity.DependencyHash)
            || !MapCommunityClient.ValidHash(identity.MechanicsHash))
            throw new InvalidDataException("Invalid exact typed weapon revision.");
    }
    private static WeaponPackage ReadTransferWeapon(string path)
    {
        if (new FileInfo(path).LinkTarget != null) throw new InvalidDataException("Weapon transfer inputs cannot be links.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is <= 0 or > WeaponPackageLimits.MaximumArchiveBytes) throw new InvalidDataException("Weapon package exceeds transfer limits.");
        byte[] bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
        return WeaponPackage.Parse(bytes);
    }
    private static void ValidateDownloadedWeapon(CommunityWeaponDownload selection, WeaponPackage package)
    {
        ValidateWeaponDownload(selection);
        var expected = new WeaponPackageRevisionIdentity(package.PackageHash, package.ContentHash, package.DependencyHash, package.Definition.Mechanics.DefinitionHash);
        if (package.Project.ContentId != selection.ContentId || package.Project.Name != selection.Name
            || package.PackageHash != selection.Revision.ContentHash || package.CopyArchive().Length != selection.Revision.Bytes
            || selection.Revision.WeaponPackage != expected)
            throw new InvalidDataException("Weapon bytes differ from the selected exact revision.");
    }
    private void ValidateWeaponRecord(CommunityTransferJob job)
    {
        if (job.TotalBytes > WeaponPackageLimits.MaximumArchiveBytes || job.Download != null || job.Publish != null
            || job.WeaponDownload != null && job.WeaponPublish != null)
            throw new InvalidDataException("Typed weapon transfer cannot carry map metadata.");
        if (job.Kind == CommunityTransferKind.Download)
        {
            if (job.WeaponDownload is not { } selection || job.WeaponPublish != null || job.SnapshotPath != null
                || selection.Revision.Visibility == CommunityContentVisibility.Published && job.Account != null
                || selection.Revision.Visibility == CommunityContentVisibility.Draft && job.Account == null)
                throw new InvalidDataException("Invalid typed weapon download receipt.");
            ValidateWeaponDownload(selection);
            if (selection.Revision.ContentHash != job.PackageHash || selection.Revision.Bytes != job.TotalBytes)
                throw new InvalidDataException("Stored weapon revision differs from its transfer identity.");
        }
        else
        {
            if (job.WeaponPublish is not { } publish || job.WeaponDownload != null || publish.Kind != CommunityContentKinds.Weapon
                || publish.ContentId == Guid.Empty || string.IsNullOrWhiteSpace(publish.Name)
                || publish.MimeType != CommunityAssetValidator.WeaponMimeType
                || JsonSerializer.SerializeToUtf8Bytes(publish, MapPackageReader.JsonOptions).Length > 6144
                || job.SnapshotPath != UploadPath(job.Id)
                || job.Account == null && job.State is not (CommunityTransferState.Completed or CommunityTransferState.Cancelled or CommunityTransferState.NeedsAuthorization))
                throw new InvalidDataException("Invalid typed weapon upload receipt.");
        }
    }
    public Guid EnqueueWeaponDownload(string endpoint, CommunityWeaponDownload selection)
    {
        ArgumentNullException.ThrowIfNull(selection); ValidateWeaponDownload(selection);
        if (selection.Revision.Visibility == CommunityContentVisibility.Draft) throw new CommunityTransferAuthorizationException();
        return EnqueueWeaponDownloadCore(endpoint, selection, null);
    }
    public async Task<Guid> EnqueueWeaponDownloadAsync(string endpoint, CommunityWeaponDownload selection,
        CancellationToken cancellation = default, CommunityTransferAccount? expectedAccount = null)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(selection);
        selection = FreezeWeaponMetadata(selection); ValidateWeaponDownload(selection); ValidateEndpoint(endpoint);
        if (selection.Revision.Visibility != CommunityContentVisibility.Draft) return EnqueueWeaponDownload(endpoint, selection);
        var account = await CaptureUploadAccountAsync(expectedAccount, cancellation).ConfigureAwait(false);
        Guid id = default;
        await ((ICommunityAccountCredentialSource)_credentials!).CommitForAccountAsync(account,
            () => id = EnqueueWeaponDownloadCore(endpoint, selection, account), cancellation).ConfigureAwait(false);
        return id;
    }
    private Guid EnqueueWeaponDownloadCore(string endpoint, CommunityWeaponDownload selection, CommunityTransferAccount? account)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(selection); ValidateEndpoint(endpoint);
        selection = FreezeWeaponMetadata(selection); ValidateWeaponDownload(selection);
        CommunityTransferJob job;
        lock (_gate)
        {
            using var index = LockAndReloadLocked();
            var existing = _jobs.Values.FirstOrDefault(j => j.Kind == CommunityTransferKind.Download && j.WeaponDownload != null
                && j.Endpoint == endpoint && j.PackageHash == selection.Revision.ContentHash && j.State != CommunityTransferState.Cancelled
                && (account == null ? j.Account == null : account.Matches(j.Account)));
            if (existing != null)
            {
                if (JsonSerializer.Serialize(existing.WeaponDownload, MapPackageReader.JsonOptions) != JsonSerializer.Serialize(selection, MapPackageReader.JsonOptions))
                    throw new InvalidDataException("Stored transfer conflicts with the exact weapon revision.");
                job = existing;
                if (job.State == CommunityTransferState.Completed)
                {
                    try { ValidateDownloadedWeapon(selection, ReadTransferWeapon(WeaponDownloadPath(job.Id))); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        job = job with { State = CommunityTransferState.Retryable, Error = "Exact private weapon archive is missing or invalid.", UpdatedAt = DateTimeOffset.UtcNow };
                        _jobs[job.Id] = job; PersistLocked();
                    }
                }
            }
            else
            {
                RequireCapacityLocked();
                job = new(Guid.NewGuid(), CommunityTransferKind.Download, CommunityTransferState.Queued, endpoint,
                    selection.Revision.ContentHash, selection.Revision.Bytes, 0, DateTimeOffset.UtcNow, WeaponDownload: selection) { Account = account };
                _jobs.Add(job.Id, job); PersistLocked();
            }
        }
        NotifyChanged(job);
        if (job.State is CommunityTransferState.Queued or CommunityTransferState.Retryable or CommunityTransferState.Failed) Schedule(job.Id);
        return job.Id;
    }
    public async Task<Guid> EnqueueWeaponUploadAsync(string endpoint, string packagePath, CommunityContentRegistration publication,
        CancellationToken cancellation = default, CommunityTransferAccount? expectedAccount = null)
    {
        ThrowIfDisposed(); ValidateEndpoint(endpoint); ArgumentNullException.ThrowIfNull(publication);
        publication = FreezeWeaponMetadata(publication);
        var account = await CaptureUploadAccountAsync(expectedAccount, cancellation).ConfigureAwait(false);
        var credentials = (ICommunityAccountCredentialSource)_credentials!;
        Guid id = Guid.NewGuid(); string snapshot = UploadPath(id); bool admitted = false;
        lock (_gate) { using var index = LockAndReloadLocked(); RequireCapacityLocked(); }
        try
        {
            var package = await Task.Run(() => ReadTransferWeapon(packagePath), cancellation).ConfigureAwait(false);
            if (publication.Kind != CommunityContentKinds.Weapon || publication.MimeType != CommunityAssetValidator.WeaponMimeType
                || publication.ContentId != package.Project.ContentId || publication.Name != package.Project.Name)
                throw new InvalidDataException("Weapon publication metadata differs from its exact archive.");
            await using (var output = new FileStream(snapshot, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await output.WriteAsync(package.CopyArchive(), cancellation).ConfigureAwait(false);
                await output.FlushAsync(cancellation).ConfigureAwait(false);
            }
            var job = new CommunityTransferJob(id, CommunityTransferKind.Upload, CommunityTransferState.Queued, endpoint,
                package.PackageHash, new FileInfo(snapshot).Length, 0, DateTimeOffset.UtcNow, SnapshotPath: snapshot,
                WeaponPublish: publication) { Account = account };
            ValidateRecord(job);
            await credentials.CommitForAccountAsync(account, () => { AddJob(job); admitted = true; }, cancellation).ConfigureAwait(false);
            NotifyChanged(job); Schedule(id); return id;
        }
        catch { if (!admitted && File.Exists(snapshot)) File.Delete(snapshot); throw; }
    }
    private async Task DownloadWeaponAsync(CommunityTransferJob job, CancellationToken cancellation)
    {
        var selection = job.WeaponDownload!; ValidateWeaponDownload(selection);
        UpdateAttempt(job.Id, job.Attempt, j => j with { State = CommunityTransferState.Downloading });
        bool authenticated = job.Account != null;
        var operations = new CommunityMapOperations(job.Endpoint, authenticated
            ? AccountCredentials(job.Account ?? throw new CommunityTransferAuthorizationException()) : _credentials);
        await operations.UseClientAsync(authenticated, client => client.DownloadWeaponPackageAsync(selection.ContentId, selection.Name,
            selection.Revision, WeaponDownloadPath(job.Id), cancellation,
            (offset, total) => Progress(job.Id, job.Attempt, offset, total)), cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        UpdateAttempt(job.Id, job.Attempt, j => j with { State = CommunityTransferState.Verifying });
        ValidateDownloadedWeapon(selection, ReadTransferWeapon(WeaponDownloadPath(job.Id)));
        if (authenticated)
        {
            try
            {
                await ((ICommunityAccountCredentialSource)_credentials!).CommitForAccountAsync(job.Account!,
                    () => UpdateAttempt(job.Id, job.Attempt, j => j with { State = CommunityTransferState.Completed, AcknowledgedBytes = j.TotalBytes, Error = null }), cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            { throw new CommunityTransferAuthorizationException(); }
        }
        else UpdateAttempt(job.Id, job.Attempt, j => j with { State = CommunityTransferState.Completed, AcknowledgedBytes = j.TotalBytes, Error = null });
    }
    public async Task ReauthorizeWeaponDownloadAsync(Guid id, CommunityTransferAccount expected, CancellationToken cancellation = default)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(expected);
        var captured = GetJob(id);
        if (captured.WeaponDownload == null || captured.Account == null || captured.State != CommunityTransferState.NeedsAuthorization
            || !expected.Matches(captured.Account)) throw new CommunityTransferAuthorizationException();
        await CaptureUploadAccountAsync(expected, cancellation).ConfigureAwait(false);
        await ((ICommunityAccountCredentialSource)_credentials!).CommitForAccountAsync(expected, () =>
        {
            lock (_gate)
            {
                using var index = LockAndReloadLocked();
                var latest = _jobs[id];
                if (latest.State != CommunityTransferState.NeedsAuthorization || latest.Attempt != captured.Attempt
                    || latest.PackageHash != captured.PackageHash || !expected.Matches(latest.Account)) throw new CommunityTransferAuthorizationException();
                _jobs[id] = latest with { State = CommunityTransferState.Paused, Error = null, UpdatedAt = DateTimeOffset.UtcNow };
                PersistLocked();
            }
        }, cancellation).ConfigureAwait(false);
        NotifyChanged(GetJob(id));
    }
    public WeaponPackage InstallWeaponDownload(Guid id, string store, CancellationToken cancellation = default)
    {
        if (GetJob(id).Account != null) throw new CommunityTransferAuthorizationException();
        return InstallWeaponDownloadCore(id, store, cancellation);
    }
    public async Task<WeaponPackage> InstallWeaponDownloadAsync(Guid id, string store, CancellationToken cancellation = default,
        CommunityTransferAccount? expectedAccount = null)
    {
        var captured = GetJob(id);
        if (captured.Account == null) return InstallWeaponDownload(id, store, cancellation);
        var account = await CaptureUploadAccountAsync(expectedAccount ?? captured.Account, cancellation).ConfigureAwait(false);
        if (!account.Matches(captured.Account)) throw new CommunityTransferAuthorizationException();
        WeaponPackage? installed = null;
        await ((ICommunityAccountCredentialSource)_credentials!).CommitForAccountAsync(account,
            () => installed = InstallWeaponDownloadCore(id, store, cancellation), cancellation).ConfigureAwait(false);
        return installed!;
    }
    private WeaponPackage InstallWeaponDownloadCore(Guid id, string store, CancellationToken cancellation)
    {
        ThrowIfDisposed(); cancellation.ThrowIfCancellationRequested();
        using var lease = _coordination.TryLeaseJob(id)
            ?? throw new InvalidOperationException("The weapon transfer is still owned by a worker; retry installation after it finishes.");
        var job = GetJob(id);
        if (job.Kind != CommunityTransferKind.Download || job.State != CommunityTransferState.Completed || job.WeaponDownload is not { } selection)
            throw new InvalidOperationException("Complete and verify the selected weapon download before installing it.");
        var package = ReadTransferWeapon(WeaponDownloadPath(id)); ValidateDownloadedWeapon(selection, package);
        cancellation.ThrowIfCancellationRequested();
        return new WeaponPackageStore(store).Install(package.CopyArchive(), cancellation);
    }
}
