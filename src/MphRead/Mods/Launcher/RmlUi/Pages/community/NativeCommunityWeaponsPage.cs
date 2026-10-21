#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using MphRead.Mods.Platform;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

/// <summary>Weapon-only UI over the existing account owner, typed content service and shared durable queue.</summary>
internal sealed class NativeCommunityWeaponsPage : IDisposable
{
    private sealed record Update(Action Apply, string? Status = null, object? Lease = null);
    private sealed record Done(int Epoch, Update? Update, string? Error);
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly string _endpoint = NetworkMapIdentity.ConfiguredDownloadSource();
    private readonly ConcurrentQueue<Done> _done = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private CancellationTokenRegistration _retirement;
    private RmlUiDocumentToken _document;
    private CommunityContentProject[] _rows = Array.Empty<CommunityContentProject>();
    private CommunityContentProject? _selected;
    private CommunityContentRevision[] _revisions = Array.Empty<CommunityContentRevision>();
    private CommunityTransferJob[] _jobs = Array.Empty<CommunityTransferJob>();
    private HashSet<string> _installed = new(StringComparer.Ordinal);
    private object? _accountLease;
    private Guid? _accountSubject;
    private Guid? _requiredContentId;
    private string? _requiredHash, _requiredStore;
    private Guid? _selectedJobId;
    private int _page = 1, _total, _revisionIndex, _epoch;
    private long _viewRevision, _lastPoll;
    private bool _mine, _busy, _disposed, _closed;
    private string _status = "Browse exact Community weapon revisions. Download acceptance and installation are separate actions.";
    internal RmlUiDocumentToken Document => _document;
    public bool Closed => _closed || _disposed;
    public bool IsBusy => _busy;
    public NativeCommunityWeaponsPage(RmlUiHost host, RmlUiPageManager pages) { _host = host; _pages = pages; }
    private bool Foreground => !Closed && _pages.Top == _document && _host.CurrentInputDocument == _document;
    private string Field(string id, int cap = 4096) => _host.ReadField(_document, id, cap);
    private CommunityContentRevision? Revision => _revisions.Length == 0 ? null : _revisions[_revisionIndex];
    private CommunityTransferJob? Job => _selectedJobId is { } id
        ? _jobs.FirstOrDefault(row => row.Id == id) : _jobs.FirstOrDefault();
    private static string Absolute(string path) => Path.IsPathFullyQualified(path) ? Path.GetFullPath(path)
        : throw new IOException("An absolute local path is required.");
    private string Store => Absolute(Field("weapons_store"));
    public void Open()
    {
        _document = _pages.OpenPage(new("community-weapons", "pages/community/weapons.rml", "weapons_search"));
        _retirement = _pages.Lifetime(_document).Register(() => { _closed = true; _lifetime.Cancel(); });
        _host.SetField(_document, "weapons_store", Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE")
            is { Length: > 0 } store ? store : Path.Combine(AppPaths.UserDataDirectory, "weapons"));
        _host.SetField(_document, "weapons_visibility", CommunityContentVisibility.Published);
        _host.SetField(_document, "weapons_parent", "");
        try { Browse(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { _status = error.Message; }
        Refresh();
    }
    public void RequireRevision(Guid contentId, string archiveHash)
    {
        if (contentId == Guid.Empty || !MapCommunityClient.ValidHash(archiveHash))
            throw new ArgumentException("A valid exact required weapon identity is required.");
        _requiredContentId = contentId; _requiredHash = archiveHash;
        _requiredStore = NetSession.WeaponPackageStoreDirectory;
        _host.SetField(_document, "weapons_store", _requiredStore);
        _selected = null; _revisions = Array.Empty<CommunityContentRevision>();
        _status = "Lobby requires exact archive " + archiveHash + ". No latest-revision substitution is allowed.";
        Begin(async token =>
        {
            async Task<(CommunityContentProject Project, CommunityContentRevision[] Revisions, int Index)> Read(
                CommunityMapOperations api, bool owner, CancellationToken ct)
            {
                var project = await api.GetContentAsync(CommunityContentKinds.Weapon, contentId, ct, owner).ConfigureAwait(false)
                    ?? throw new FileNotFoundException("The required weapon project is not available to this account.");
                var revisions = await api.GetContentRevisionsAsync(CommunityContentKinds.Weapon, contentId, ct, owner).ConfigureAwait(false);
                if (project.Kind != CommunityContentKinds.Weapon || project.ContentId != contentId || revisions.Length > 16384)
                    throw new InvalidDataException("Unexpected required weapon metadata.");
                revisions = revisions.OrderByDescending(row => row.RevisionNumber).ToArray();
                int index = Array.FindIndex(revisions, row => row.ContentHash == archiveHash && row.WeaponPackage?.ArchiveHash == archiveHash);
                if (index < 0) throw new FileNotFoundException("The exact required archive is not available; no substitute was selected.");
                return (project, revisions, index);
            }
            object? lease = null; Guid? subject = null;
            (CommunityContentProject Project, CommunityContentRevision[] Revisions, int Index) selected;
            try { selected = await Read(new CommunityMapOperations(_endpoint), false, token).ConfigureAwait(false); }
            catch (FileNotFoundException)
            {
                selected = await HunterLicenseClient.WithNativeCommunityIdentityAsync(null, null,
                    async (user, accountLease, credentials, ct) =>
                    { lease = accountLease; subject = user; return await Read(new(_endpoint, credentials), true, ct).ConfigureAwait(false); }, token).ConfigureAwait(false);
            }
            string store = _requiredStore!;
            var installed = new HashSet<string>(new WeaponPackageStore(store).List().Select(row => row.PackageHash), StringComparer.Ordinal);
            return new Update(() =>
            {
                _rows = new[] { selected.Project }; _selected = selected.Project; _revisions = selected.Revisions;
                _revisionIndex = selected.Index; _installed = installed;
                if (lease != null) { _accountLease = lease; _accountSubject = subject; }
                _host.SetField(_document, "weapons_parent", selected.Project.LatestHash);
            }, "Required exact archive " + archiveHash + " selected. Accept its download, then install into the displayed lobby store.", lease);
        });
        Refresh();
    }
    private void Begin(Func<CancellationToken, Task<Update>> action)
    {
        _operation?.Cancel(); _operation?.Dispose();
        _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        CancellationToken token = _operation.Token; int epoch = ++_epoch; _busy = true;
        _ = Task.Run(async () =>
        {
            try { token.ThrowIfCancellationRequested(); _done.Enqueue(new(epoch, await action(token).ConfigureAwait(false), null)); }
            catch (OperationCanceledException) { _done.Enqueue(new(epoch, null, "Operation cancelled.")); }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            { _done.Enqueue(new(epoch, null, error.Message)); }
        });
    }
    private void Browse()
    {
        string query = Field("weapons_search", 512), store = Store;
        bool mine = _mine; int page = _page;
        Begin(async token =>
        {
            object? lease = null; Guid? subject = null;
            CommunityContentPage catalog;
            if (mine)
                catalog = await HunterLicenseClient.WithNativeCommunityIdentityAsync(null, null,
                    async (currentSubject, currentLease, credentials, ct) =>
                    { lease = currentLease; subject = currentSubject; return await new CommunityMapOperations(_endpoint, credentials)
                        .BrowseContentAsync(ct, CommunityContentKinds.Weapon, true, query, "newest", page, 8).ConfigureAwait(false); }, token).ConfigureAwait(false);
            else catalog = await new CommunityMapOperations(_endpoint).BrowseContentAsync(token,
                CommunityContentKinds.Weapon, false, query, "newest", page, 8).ConfigureAwait(false);
            if (catalog.Items.Length > 8 || catalog.Items.Any(row => row.Kind != CommunityContentKinds.Weapon))
                throw new InvalidDataException("Unexpected typed weapon catalog.");
            var installed = new HashSet<string>(new WeaponPackageStore(store).List().Select(row => row.PackageHash), StringComparer.Ordinal);
            return new Update(() => { _rows = catalog.Items; _total = catalog.Total; _selected = null;
                _revisions = Array.Empty<CommunityContentRevision>(); _installed = installed; _accountLease = lease; _accountSubject = subject; },
                "Loaded " + catalog.Items.Length + " exact weapon projects.", lease);
        });
    }
    private void Select(int index)
    {
        if ((uint)index >= (uint)_rows.Length) return;
        var selection = _rows[index]; bool mine = _mine; object? expectedLease = _accountLease;
        Begin(async token =>
        {
            object? lease = null; Guid? subject = null;
            async Task<(CommunityContentProject, CommunityContentRevision[])> Read(CommunityMapOperations api, CancellationToken ct)
            {
                var project = await api.GetContentAsync(CommunityContentKinds.Weapon, selection.ContentId, ct, mine).ConfigureAwait(false)
                    ?? throw new IOException("The selected weapon is no longer available.");
                var revisions = await api.GetContentRevisionsAsync(CommunityContentKinds.Weapon, selection.ContentId, ct, mine).ConfigureAwait(false);
                if (revisions.Length > 16384) throw new InvalidDataException("Weapon revision metadata exceeds the UI bound.");
                return (project, revisions.OrderByDescending(row => row.RevisionNumber).ToArray());
            }
            (CommunityContentProject Project, CommunityContentRevision[] Revisions) result;
            if (mine) result = await HunterLicenseClient.WithNativeCommunityIdentityAsync(expectedLease, null,
                async (user, currentLease, credentials, ct) => { lease = currentLease; subject = user; return await Read(new(_endpoint, credentials), ct).ConfigureAwait(false); }, token).ConfigureAwait(false);
            else result = await Read(new(_endpoint), token).ConfigureAwait(false);
            return new Update(() => { _selected = result.Project; _revisions = result.Revisions; _revisionIndex = 0;
                _accountLease = lease; _accountSubject = subject; _host.SetField(_document, "weapons_parent", result.Project.LatestHash); }, "Selected exact revision history.", lease);
        });
    }
    public bool Handle(in RmlUiIntent intent)
    {
        if (!Foreground || intent.Kind != RmlUiIntentKind.WeaponCommunityAction || intent.Document != _document || !_pages.Accept(intent)) return false;
        if (_busy && intent.Argument != 17) return true;
        try
        {
            if (intent.Argument is >= 18 and <= 25) Select(intent.Argument - 18);
            else if (intent.Argument is >= 26 and <= 29)
            {
                int index = intent.Argument - 26;
                if (index < _jobs.Length) _selectedJobId = _jobs[index].Id;
            }
            else switch (intent.Argument)
            {
                case 0:
                    if (_requiredContentId is { } required && _requiredHash is { } exact) RequireRevision(required, exact);
                    else Browse();
                    break;
                case 1: _mine = true; _page = 1; Browse(); break;
                case 2: _mine = false; _page = 1; Browse(); break;
                case 3: _page = 1; Browse(); break;
                case 4: if (_page > 1) { _page--; Browse(); } break;
                case 5: if (_page * 8 < _total) { _page++; Browse(); } break;
                case 6: _revisionIndex = Math.Max(0, _revisionIndex - 1); break;
                case 7: _revisionIndex = Math.Min(Math.Max(0, _revisions.Length - 1), _revisionIndex + 1); break;
                case 8: AcceptDownload(); break;
                case 9: Install(); break;
                case 10: Transfer("retry"); break;
                case 11: Transfer("pause"); break;
                case 12: Transfer("cancel"); break;
                case 13: Report(); break;
                case 14: Lifecycle(false); break;
                case 15: Lifecycle(true); break;
                case 16: Publish(); break;
                case 17: Back(); break;
                case 30: SetVisibility(); break;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { _status = error.Message; }
        Refresh(); return true;
    }
    private void AcceptDownload()
    {
        var project = _selected ?? throw new InvalidOperationException("Select a weapon first.");
        var revision = Revision ?? throw new InvalidOperationException("Select an exact revision first.");
        var selection = new CommunityWeaponDownload(project.ContentId, project.Name, revision);
        if (_requiredHash != null && (project.ContentId != _requiredContentId || revision.ContentHash != _requiredHash))
            throw new InvalidOperationException("This lobby request requires the displayed exact archive; no substitute revision may be accepted.");
        object? expectedLease = _accountLease;
        Begin(async token =>
        {
            Guid id; Guid? subject = null; object? lease = null;
            if (revision.Visibility == CommunityContentVisibility.Draft)
            {
                var identity = await HunterLicenseClient.WithNativeCommunityIdentityAsync(expectedLease, null,
                    (user, accountLease, _, _) => Task.FromResult((user, accountLease)), token).ConfigureAwait(false);
                subject = identity.user; lease = identity.accountLease;
                id = await CommunityTransferSession.Shared.EnqueueWeaponDownloadAsync(_endpoint, selection, token,
                    new CommunityTransferAccount(identity.user)).ConfigureAwait(false);
            }
            else id = CommunityTransferSession.Shared.EnqueueWeaponDownload(_endpoint, selection);
            return new Update(() => { _selectedJobId = id; if (lease != null) { _accountLease = lease; _accountSubject = subject; } },
                "Accepted exact download " + revision.ContentHash + ". Wait for verification, then explicitly Install.", lease);
        });
    }
    private void Install()
    {
        var job = Job ?? throw new InvalidOperationException("Select a completed weapon download.");
        if (job.WeaponDownload == null || job.State != CommunityTransferState.Completed) throw new InvalidOperationException("A verified completed weapon download is required.");
        if (_requiredHash != null && (job.WeaponDownload.ContentId != _requiredContentId || job.PackageHash != _requiredHash))
            throw new InvalidOperationException("Select the completed exact lobby-required archive.");
        string store = _requiredHash != null ? _requiredStore! : Store; object? expectedLease = _accountLease;
        Begin(async token =>
        {
            WeaponPackage package; object? lease = null; Guid? subject = null;
            if (job.Account is { } original)
            {
                var identity = await HunterLicenseClient.WithNativeCommunityIdentityAsync(expectedLease, original.Subject,
                    (user, accountLease, _, _) => Task.FromResult((user, accountLease)), token).ConfigureAwait(false);
                subject = identity.user; lease = identity.accountLease;
                package = await CommunityTransferSession.Shared.InstallWeaponDownloadAsync(job.Id, store, token,
                    new CommunityTransferAccount(identity.user)).ConfigureAwait(false);
            }
            else package = await Task.Run(() => CommunityTransferSession.Shared.InstallWeaponDownload(job.Id, store, token), token).ConfigureAwait(false);
            return new Update(() =>
            {
                _installed.Add(package.PackageHash);
                if (lease != null) { _accountLease = lease; _accountSubject = subject; }
                NetSession.RetryRequiredWeaponPackage(package.PackageHash);
            }, "Installed " + package.PackageHash + ". Matching failed lobby preparation may retry; inventory is never granted.", lease);
        });
    }    private void Transfer(string action)
    {
        var job = Job ?? throw new InvalidOperationException("Select a weapon transfer first.");
        Begin(async token =>
        {
            var queue = CommunityTransferSession.Shared;
            if (action == "cancel") queue.Cancel(job.Id);
            else if (action == "pause") queue.Pause(job.Id);
            else
            {
                if (job.Kind == CommunityTransferKind.Upload || job.Account != null)
                {
                    var account = await HunterLicenseClient.WithNativeCommunityIdentityAsync(_accountLease, job.Account?.Subject,
                        (subject, _, _, _) => Task.FromResult(new CommunityTransferAccount(subject)), token).ConfigureAwait(false);
                    if (job.State == CommunityTransferState.NeedsAuthorization)
                    {
                        if (job.WeaponDownload != null) await queue.ReauthorizeWeaponDownloadAsync(job.Id, account, token).ConfigureAwait(false);
                        else await queue.ReauthorizeUploadAsync(job.Id, account, token).ConfigureAwait(false);
                    }
                    else await queue.CaptureUploadAccountAsync(account, token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested(); queue.Resume(job.Id);
            }
            return new Update(() => { }, "Transfer " + action + " requested.");
        });
    }
    private void Report()
    {
        var selection = _selected ?? throw new InvalidOperationException("Select a weapon first.");
        var revision = Revision ?? throw new InvalidOperationException("Select a revision first.");
        string reason = Field("weapons_report_reason", 2048);
        Begin(async token => await HunterLicenseClient.WithNativeCommunityIdentityAsync(null, null,
            async (_, lease, credentials, ct) =>
            {
                await new CommunityMapOperations(_endpoint, credentials).UseClientAsync(true, async client =>
                { await client.ReportWeaponAsync(selection.ContentId, revision.ContentHash, reason, ct).ConfigureAwait(false); return true; }, ct).ConfigureAwait(false);
                return new Update(() => { }, "Weapon revision report submitted.", lease);
            }, token).ConfigureAwait(false));
    }
    private void SetVisibility()
    {
        var selection = _selected ?? throw new InvalidOperationException("Select one of your exact weapon revisions first.");
        var revision = Revision ?? throw new InvalidOperationException("Select an exact revision first.");
        if (!selection.CanManage) throw new InvalidOperationException("Only the verified creator can change revision visibility.");
        string visibility = CommunityContentVisibility.Normalize(Field("weapons_visibility", 64)); object? expectedLease = _accountLease;
        Begin(async token => await HunterLicenseClient.WithNativeCommunityIdentityAsync(expectedLease, null,
            async (subject, lease, credentials, ct) =>
            {
                var api = new CommunityMapOperations(_endpoint, credentials);
                var current = await api.GetContentAsync(CommunityContentKinds.Weapon, selection.ContentId, ct, true).ConfigureAwait(false);
                if (current?.CanManage != true || current.LatestHash != selection.LatestHash)
                    throw new InvalidOperationException("Creator state changed. Refresh before changing visibility.");
                var updated = await api.UseClientAsync(true, client => client.SetWeaponVisibilityAsync(
                    selection.ContentId, revision.ContentHash, visibility, ct), ct).ConfigureAwait(false);
                var history = await api.GetContentRevisionsAsync(CommunityContentKinds.Weapon, selection.ContentId, ct, true).ConfigureAwait(false);
                if (history.Length > 16384) throw new InvalidDataException("Weapon revision metadata exceeds the UI bound.");
                history = history.OrderByDescending(row => row.RevisionNumber).ToArray();
                int index = Array.FindIndex(history, row => row.ContentHash == revision.ContentHash);
                if (index < 0 || history[index].Visibility != visibility)
                    throw new InvalidDataException("Updated exact revision visibility was not confirmed by the Community service.");
                return new Update(() => { _selected = updated; _revisions = history; _revisionIndex = index;
                    _accountLease = lease; _accountSubject = subject; _host.SetField(_document, "weapons_parent", updated.LatestHash); },
                    "Updated exact revision visibility to " + visibility + ". Archive bytes were not changed.", lease);
            }, token).ConfigureAwait(false));
    }
    private void Lifecycle(bool restore)
    {
        var selection = _selected ?? throw new InvalidOperationException("Select one of your weapons first.");
        if (!selection.CanManage) throw new InvalidOperationException("Only the verified creator can archive or restore this weapon.");
        object? lease = _accountLease;
        Begin(async token => await HunterLicenseClient.WithNativeCommunityIdentityAsync(lease, null,
            async (_, currentLease, credentials, ct) =>
            {
                var api = new CommunityMapOperations(_endpoint, credentials);
                var current = await api.GetContentAsync(CommunityContentKinds.Weapon, selection.ContentId, ct, true).ConfigureAwait(false);
                if (current?.CanManage != true || current.LatestHash != selection.LatestHash) throw new InvalidOperationException("Creator state changed. Refresh first.");
                await api.UseClientAsync(true, async client => { await client.ArchiveContentAsync(CommunityContentKinds.Weapon,
                    selection.ContentId, restore, ct).ConfigureAwait(false); return true; }, ct).ConfigureAwait(false);
                return new Update(() => { _rows = Array.Empty<CommunityContentProject>(); _selected = null;
                    _revisions = Array.Empty<CommunityContentRevision>(); }, restore ? "Restored. Refresh Mine." : "Archived. Refresh Mine.", currentLease);
            }, token).ConfigureAwait(false));
    }
    private void Publish()
    {
        string workspacePath = Absolute(Field("weapons_workspace"));
        string parent = Field("weapons_parent", 128).Trim(), notes = Field("weapons_notes", 16004);
        string visibility = CommunityContentVisibility.Normalize(Field("weapons_visibility", 64));
        Begin(async token =>
        {
            var workspace = await Task.Run(() => WeaponAuthoringWorkspace.Open(workspacePath), token).ConfigureAwait(false);
            byte[] bytes = workspace.PreparePublish(); var package = WeaponPackage.Parse(bytes);
            var identity = await HunterLicenseClient.WithNativeCommunityIdentityAsync(null, null,
                async (subject, lease, credentials, ct) =>
                {
                    var project = await new CommunityMapOperations(_endpoint, credentials)
                        .GetContentAsync(CommunityContentKinds.Weapon, package.Project.ContentId, ct, true).ConfigureAwait(false);
                    if ((project != null && !project.CanManage) || (project?.LatestHash ?? "") != parent)
                        throw new InvalidOperationException("Expected parent differs from the verified current project. Refresh or use the correct parent hash.");
                    return (subject, lease);
                }, token).ConfigureAwait(false);
            string directory = Path.Combine(AppPaths.UserDataDirectory, "weapon-publication-staging");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".ppweapon"); bool owned = false;
            try
            {
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { owned = true; output.Write(bytes); output.Flush(true); }
                var metadata = new CommunityContentRegistration(CommunityContentKinds.Weapon, package.Project.ContentId,
                    identity.subject.ToString("D"), package.Project.Name, package.Project.Name, package.Project.Author,
                    "Creator-authored custom weapon package.", new[] { "weapon" }, CommunityAssetValidator.WeaponMimeType,
                    visibility, parent.Length == 0 ? null : parent, notes);
                Guid id = await CommunityTransferSession.Shared.EnqueueWeaponUploadAsync(_endpoint, path, metadata, token,
                    new CommunityTransferAccount(identity.subject)).ConfigureAwait(false);
                return new Update(() => { _selectedJobId = id; _accountLease = identity.lease; _accountSubject = identity.subject; }, "Queued exact attested archive " + package.PackageHash + ". Transfer receipt: " + id, identity.lease);
            }
            finally { if (owned && File.Exists(path)) File.Delete(path); }
        });
    }
    public void Refresh()
    {
        if (Closed) return;
        if (_accountLease != null && !HunterLicenseClient.IsNativeAccountLeaseCurrent(_accountLease))
        {
            _operation?.Cancel(); _epoch++; _busy = false; _accountLease = null; _accountSubject = null; _selected = null;
            _rows = Array.Empty<CommunityContentProject>(); _revisions = Array.Empty<CommunityContentRevision>();
            _status = "Account changed. Refresh the catalog to acquire a new verified account witness.";
        }
        while (_done.TryDequeue(out var completion))
        {
            if (completion.Epoch != _epoch) continue;
            _busy = false;
            if (completion.Update is { } update)
            {
                if (update.Lease != null && !HunterLicenseClient.IsNativeAccountLeaseCurrent(update.Lease))
                { _status = "Account changed; stale UI completion discarded."; continue; }
                update.Apply(); if (update.Status != null) _status = update.Status;
            }
            else _status = completion.Error ?? "Operation failed.";
        }
        if (!_busy && Environment.TickCount64 - _lastPoll > 1000)
        {
            _lastPoll = Environment.TickCount64;
            Guid? visibleSubject = _accountLease != null && HunterLicenseClient.IsNativeAccountLeaseCurrent(_accountLease) ? _accountSubject : null;
            Begin(token => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var jobs = CommunityTransferSession.Shared.ListJobs().Where(row => row.Endpoint == _endpoint
                    && (row.WeaponDownload != null || row.WeaponPublish != null)
                    && (row.Account == null || visibleSubject is { } subject && row.Account.Matches(subject))).OrderByDescending(row => row.UpdatedAt).Take(4).ToArray();
                return new Update(() => { _jobs = jobs; if (_selectedJobId == null) _selectedJobId = jobs.FirstOrDefault()?.Id; });
            }, token));
        }
        if (!Foreground) return;
        var revision = Revision; var selected = _selected; var job = Job;
        string detail = selected == null ? "Select a weapon to inspect exact revision identities." :
            selected.Name + " // " + (selected.Author ?? "Unknown creator") + "\nContent ID " + selected.ContentId
            + "\nCurrent " + (selected.CurrentHash ?? "none") + "\nLatest " + selected.LatestHash
            + "\n" + (selected.Description ?? "") + "\n" + (selected.Archived ? "ARCHIVED" : "ACTIVE") + (selected.CanManage ? " // VERIFIED CREATOR" : "")
            + (revision == null ? "" : "\nRevision " + revision.RevisionNumber + " // " + revision.Visibility
                + " // " + revision.Bytes + " bytes\nExact archive " + revision.ContentHash
                + (revision.WeaponPackage is { } identity ? "\nContent " + identity.ContentHash
                    + "\nDependencies " + identity.DependencyHash + "\nMechanics " + identity.MechanicsHash : "\nMissing typed weapon identities")
                + "\n" + (_installed.Contains(revision.ContentHash) ? "INSTALLED" : "NOT INSTALLED")
                + "\n" + (revision.ReleaseNotes ?? "No release notes."));
        var bindings = new Dictionary<string, RmlUiBindingValue>
        {
            ["weapons_status"] = RmlUiBindingValue.FromText(_status),
            ["weapons_detail"] = RmlUiBindingValue.FromText(detail),
            ["weapons_paging"] = RmlUiBindingValue.FromText((_mine ? "MINE" : "PUBLIC") + " // page " + _page + " // " + _total + " projects"),
            ["weapons_transfer_detail"] = RmlUiBindingValue.FromText(job == null ? "No typed weapon transfer selected." :
                job.Id + " // " + job.State + " // " + job.AcknowledgedBytes + "/" + job.TotalBytes + "\n" + job.PackageHash + "\n" + (job.Error ?? "")),
            ["disabled:weapons_download"] = RmlUiBindingValue.FromBoolean(_busy || revision == null),
            ["disabled:weapons_install"] = RmlUiBindingValue.FromBoolean(_busy || job?.WeaponDownload == null || job.State != CommunityTransferState.Completed),
            ["disabled:weapons_set_visibility"] = RmlUiBindingValue.FromBoolean(_busy || selected?.CanManage != true || revision == null),
            ["disabled:weapons_archive"] = RmlUiBindingValue.FromBoolean(_busy || selected?.CanManage != true || selected.Archived),
            ["disabled:weapons_restore"] = RmlUiBindingValue.FromBoolean(_busy || selected?.CanManage != true || !selected.Archived)
        };
        for (int i = 0; i < 8; i++)
        {
            string id = "weapons_row_" + i; bool visible = i < _rows.Length;
            bindings["visible:" + id] = RmlUiBindingValue.FromBoolean(visible);
            bindings[id] = RmlUiBindingValue.FromText(visible ? _rows[i].Name + " // " + (_rows[i].Author ?? "Unknown creator")
                + " // revisions " + _rows[i].RevisionCount : "");
            bindings["disabled:" + id] = RmlUiBindingValue.FromBoolean(_busy);
        }
        for (int i = 0; i < 4; i++)
        {
            string id = "weapons_transfer_" + i; bool visible = i < _jobs.Length;
            bindings["visible:" + id] = RmlUiBindingValue.FromBoolean(visible);
            bindings[id] = RmlUiBindingValue.FromText(visible ? _jobs[i].Kind + " // " + _jobs[i].State + " // " + _jobs[i].PackageHash[..12] : "");
        }
        foreach (string id in new[] { "weapons_refresh", "weapons_mine", "weapons_public", "weapons_search_button", "weapons_previous", "weapons_next",
            "weapons_revision_previous", "weapons_revision_next", "weapons_retry", "weapons_pause", "weapons_cancel", "weapons_report", "weapons_publish" })
            bindings["disabled:" + id] = RmlUiBindingValue.FromBoolean(_busy);
        foreach (string field in new[] { "weapons_search", "weapons_store", "weapons_report_reason", "weapons_workspace",
            "weapons_parent", "weapons_visibility", "weapons_notes" })
            bindings["disabled:" + field] = RmlUiBindingValue.FromBoolean(_busy || field == "weapons_store" && _requiredHash != null);
        _pages.Present(_document, ++_viewRevision, bindings);
    }
    public bool Back() { if (!Foreground) return false; _closed = true; _lifetime.Cancel(); return true; }
    public void Dispose()
    {
        _retirement.Dispose(); if (_disposed) return;
        _disposed = _closed = true; _lifetime.Cancel(); _operation?.Cancel(); _operation?.Dispose(); _lifetime.Dispose();
    }
}
#endif
