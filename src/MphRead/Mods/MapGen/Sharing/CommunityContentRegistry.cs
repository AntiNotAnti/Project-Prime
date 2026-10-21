using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.CustomWeapons.Packages;

namespace MphRead.Mods.MapGen;

internal sealed record CommunityContentStoredProject(
    string Kind, Guid ContentId, string OwnerId,
    string Name, string? DisplayName, string? Author,
    string? Description, string[] Tags,
    string? CurrentHash, string LatestHash,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    CommunityContentRevision[] Revisions, bool Archived = false, bool ModerationHidden = false);

internal sealed record CommunityContentStoredState(
    int SchemaVersion, CommunityContentStoredProject[] Projects, CommunityContentReport[]? Reports = null);

/// <summary>
/// Private, atomically persisted metadata for non-map Community content kinds.
/// The existing v2 map revision catalog remains the one authority for maps.
/// All writers must supply verified immutable local bytes; PR-09 will expose
/// creator upload endpoints after content-type-specific validation.
/// </summary>
public sealed class CommunityContentRegistry
{
    private const int Format = 1;
    private const int MaximumProjects = 4096;
    private const int MaximumRevisions = 16384;
    private const int MaximumTags = 16;
    private const int MaximumDependencies = 32;
    // Distinct raw objects are a separate storage pool from historical
    // .ppmap packages. Cap both the service and a single creator so valid
    // credentials cannot exhaust origin disk by accumulating revisions.
    private const long DefaultObjectBudget = 50L * 1024 * 1024 * 1024;
    private const long DefaultCreatorBudget = 10L * 1024 * 1024 * 1024;
    private readonly long _maximumStoredBytes;
    private readonly long _maximumOwnerBytes;
    private readonly string _root;
    private readonly string _objects;
    private readonly string _index;
    private readonly object _gate = new();
    private readonly List<CommunityContentReport> _reports = new();
    private sealed record WeaponProof(long WriteTicks, long CreationTicks, FileAttributes Attributes,
        WeaponPackageRevisionIdentity Identity);
    private readonly Dictionary<(string Hash, Guid Id, string Name, long Bytes), WeaponProof> _weaponProofs = new();
    private readonly Dictionary<(string Kind, Guid Id), CommunityContentStoredProject> _projects
        = new();

    public CommunityContentRegistry(string storage,
        long maximumStoredBytes = DefaultObjectBudget,
        long maximumOwnerBytes = DefaultCreatorBudget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storage);
        if (maximumStoredBytes < 1 || maximumOwnerBytes < 1
            || maximumOwnerBytes > maximumStoredBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumStoredBytes),
                "V3 content pool and creator quotas must be positive and ordered.");
        _maximumStoredBytes = maximumStoredBytes;
        _maximumOwnerBytes = maximumOwnerBytes;
        _root = Path.GetFullPath(storage);
        _objects = Path.Combine(_root, "content-v3");
        _index = Path.Combine(_root, "content_catalog_v3.json");
        Directory.CreateDirectory(_objects);

        if (!File.Exists(_index)) return;
        if (new FileInfo(_index).Length > 32L * 1024 * 1024)
            throw new InvalidDataException("V3 content catalog exceeds its metadata budget.");
        var saved = JsonSerializer.Deserialize<CommunityContentStoredState>(
            File.ReadAllBytes(_index), MapPackageReader.JsonOptions)
            ?? throw new InvalidDataException("Missing v3 catalog state.");
        if (saved.SchemaVersion != Format || saved.Projects == null
            || saved.Projects.Length > MaximumProjects
            || saved.Projects.Sum(x => x?.Revisions?.Length ?? 0) > MaximumRevisions)
            throw new InvalidDataException("Invalid v3 catalog schema or bounds.");
        foreach (CommunityContentStoredProject project in saved.Projects)
        {
            ValidateProject(project);
            if (!_projects.TryAdd((project.Kind, project.ContentId), project))
                throw new InvalidDataException("Duplicate v3 content project identity.");
        }
        if (saved.Reports is { } reports)
        {
            if (reports.Length > 4096) throw new InvalidDataException("Typed report budget exceeded.");
            var ids = new HashSet<Guid>(); var keys = new HashSet<(string, string, Guid, string)>();
            foreach (var report in reports)
            {
                ValidateReport(report);
                if (!ids.Add(report.ReportId) || !keys.Add((report.ReporterId, report.Kind, report.ContentId, report.Hash))
                    || !_projects.TryGetValue((report.Kind, report.ContentId), out var project)
                    || !project.Revisions.Any(r => r.ContentHash == report.Hash))
                    throw new InvalidDataException("Invalid persisted typed report identity.");
                _reports.Add(report);
            }
        }
    }

    /// <summary>
    /// Trusted, local service-side registration only. This method deliberately
    /// does not authenticate remote users and MUST NOT be exposed as an HTTP
    /// route until PR-09 validates type-specific files and creator permissions.
    /// No code path can register a map here.
    /// </summary>
    public CommunityContentProject RegisterVerifiedBlob(string stagedFile,
        CommunityContentRegistration metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateRegistration(metadata);
        if (metadata.Kind == CommunityContentKinds.Weapon
            && Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_COMMUNITY_PUBLISH") != "1")
            throw new InvalidDataException("Community weapon publication is disabled.");
        if (string.IsNullOrWhiteSpace(stagedFile))
            throw new ArgumentException("A verified local asset path is required.", nameof(stagedFile));
        using (var source = new FileStream(stagedFile, FileMode.Open,
            FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
        {
            if (source.Length is <= 0 or > MapPackageReader.MaxEntryBytes)
                throw new InvalidDataException("V3 asset exceeds the 256 MiB file budget.");
        }
        long bytes = new FileInfo(stagedFile).Length;
        string hash = MapBuildFingerprint.HashFile(stagedFile);
        if (!MapCommunityClient.ValidHash(hash))
            throw new InvalidDataException("V3 immutable file digest is invalid.");
        if (metadata.Kind == CommunityContentKinds.Weapon)
            ValidateWeaponBlob(stagedFile, metadata.ContentId, metadata.Name, hash, bytes);

        lock (_gate)
        {
            var key = (metadata.Kind, metadata.ContentId);
            bool existed = _projects.TryGetValue(key, out var current);
            if (!existed && _projects.Count >= MaximumProjects)
                throw new InvalidDataException("V3 catalog project budget exceeded.");
            if (current != null && current.OwnerId != metadata.OwnerId)
                throw new InvalidDataException("Cannot change v3 content owner.");
            if (current != null && metadata.Kind == CommunityContentKinds.Weapon
                && !StringComparer.Ordinal.Equals(current.Name, metadata.Name))
                throw new InvalidDataException("Weapon package names are immutable within a Community project.");
            if (current?.Revisions.FirstOrDefault(r => r.ContentHash == hash) is { } existingRevision)
            {
                if (metadata.Kind == CommunityContentKinds.Weapon && existingRevision.Visibility != metadata.Visibility)
                    throw new InvalidDataException("This exact weapon revision already exists with different visibility. Use the explicit weapon visibility action.");
                return Project(current, owner: true)
                    ?? throw new InvalidDataException("Existing asset metadata is invalid.");
            }

            int count = _projects.Values.Sum(p => p.Revisions.Length);
            if (count >= MaximumRevisions)
                throw new InvalidDataException("V3 catalog revision budget exceeded.");
            if (current == null && metadata.ExpectedParentHash != null)
                throw new InvalidDataException("New v3 content cannot have a parent.");
            if (current != null
                && metadata.ExpectedParentHash != current.LatestHash)
                throw new InvalidDataException("Stale v3 content parent; reload before publishing.");

            // Pool/account quotas count each exact SHA only once even when
            // several materials or creators reference the same immutable file.
            // The bounded revision catalog has at most 16,384 entries; only
            // creator publication takes this quota snapshot under _gate.
            var allRevisions = _projects.Values
                .SelectMany(p => p.Revisions).ToArray();
            bool alreadyStored = allRevisions.Any(revision =>
                revision.ContentHash == hash);
            long globalBytes = allRevisions
                .GroupBy(revision => revision.ContentHash, StringComparer.Ordinal)
                .Sum(group => group.First().Bytes);
            if (!alreadyStored && bytes > _maximumStoredBytes - globalBytes)
                throw new InvalidDataException(
                    "Community generic content pool storage quota exceeded.");

            var ownerRevisions = _projects.Values
                .Where(p => p.OwnerId == metadata.OwnerId)
                .SelectMany(p => p.Revisions).ToArray();
            bool ownerAlreadyUses = ownerRevisions.Any(revision =>
                revision.ContentHash == hash);
            long ownerBytes = ownerRevisions
                .GroupBy(revision => revision.ContentHash, StringComparer.Ordinal)
                .Sum(group => group.First().Bytes);
            if (!ownerAlreadyUses && bytes > _maximumOwnerBytes - ownerBytes)
                throw new InvalidDataException(
                    "Community creator generic content storage quota exceeded.");

            // A published typed item cannot advertise a private or unresolved
            // dependency hash. PR-10 will generalize this into a lockfile DAG;
            // PR-08 safely permits only already-published generic dependencies.
            if (metadata.Visibility == CommunityContentVisibility.Published)
                foreach (var dependency in metadata.Dependencies ?? Array.Empty<CommunityContentDependency>())
                {
                    if (dependency.Kind == CommunityContentKinds.Map
                        || !_projects.TryGetValue(
                            (dependency.Kind, dependency.ContentId), out var upstream)
                        || !upstream.Revisions.Any(r => r.ContentHash == dependency.Hash
                            && r.Visibility == CommunityContentVisibility.Published))
                        throw new InvalidDataException(
                            "Published v3 content cannot expose a private or unresolved dependency.");
                }

            // A published asset may reference only the already-approved
            // optional/editor dependency graph. Reject cycles before writing
            // either immutable bytes or revision metadata.
            if (metadata.Visibility == CommunityContentVisibility.Published)
                EnsureAcyclic(metadata.Kind, metadata.ContentId,
                    metadata.Dependencies ?? Array.Empty<CommunityContentDependency>());

            // Immutable objects are persisted before the metadata pointer.
            // If metadata persistence fails, an unreferenced blob is harmless,
            // while no catalog revision ever points to an unwritten object.
            PutVerifiedBlob(stagedFile, hash, bytes);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            int revisionNumber = (current?.Revisions.LastOrDefault()?.RevisionNumber ?? 0) + 1;
            var revision = new CommunityContentRevision(
                revisionNumber, hash, ArchiveHash: null, bytes,
                metadata.MimeType, metadata.Visibility, now,
                ParentHash: current?.LatestHash,
                ReleaseNotes: metadata.ReleaseNotes,
                Dependencies: metadata.Dependencies?.ToArray());
            // The project-level presentation is public when CurrentHash is
            // published. A later draft must not overwrite and leak unpublished
            // title, description or tags through that public current pointer.
            bool publishPresentation = current == null
                || metadata.Visibility == CommunityContentVisibility.Published;
            var next = new CommunityContentStoredProject(
                metadata.Kind, metadata.ContentId, metadata.OwnerId,
                publishPresentation ? metadata.Name : current!.Name,
                publishPresentation ? metadata.DisplayName : current!.DisplayName,
                publishPresentation ? metadata.Author : current!.Author,
                publishPresentation ? metadata.Description : current!.Description,
                publishPresentation ? metadata.Tags.ToArray() : current!.Tags.ToArray(),
                CurrentHash: metadata.Visibility == CommunityContentVisibility.Published
                    ? hash : current?.CurrentHash,
                LatestHash: hash,
                CreatedAt: current?.CreatedAt ?? now,
                UpdatedAt: metadata.Visibility == CommunityContentVisibility.Published
                    || current == null ? now : current.UpdatedAt,
                Revisions: current == null
                    ? new[] { revision } : current.Revisions.Append(revision).ToArray(),
                Archived: current?.Archived ?? false, ModerationHidden: current?.ModerationHidden ?? false);

            CommunityContentStoredProject[] updated = _projects.Values
                .Where(p => !(p.Kind == metadata.Kind && p.ContentId == metadata.ContentId))
                .Append(next)
                .OrderBy(p => p.Kind, StringComparer.Ordinal)
                .ThenBy(p => p.ContentId).ToArray();
            AtomicFile.Write(_index, JsonSerializer.SerializeToUtf8Bytes(
                new CommunityContentStoredState(Format, updated, _reports.ToArray()),
                MapPackageReader.JsonOptions));
            _projects[key] = next;
            return Project(next, owner: true)
                ?? throw new InvalidDataException("New v3 content could not be projected.");
        }
    }

    public CommunityContentProject[] ListVisible(string? viewerId)
    {
        lock (_gate)
            return _projects.Values
                .OrderBy(p => p.Kind, StringComparer.Ordinal)
                .ThenBy(p => p.ContentId)
                .Select(p => Project(p, viewerId == p.OwnerId))
                .OfType<CommunityContentProject>().ToArray();
    }

    public CommunityContentProject? GetVisible(string kind, Guid contentId, string? viewerId)
    {
        string normalized = CommunityContentKinds.Normalize(kind);
        if (normalized == CommunityContentKinds.Map) return null;
        lock (_gate)
            return _projects.TryGetValue((normalized, contentId), out var value)
                ? Project(value, viewerId == value.OwnerId, allowUnlisted: normalized == CommunityContentKinds.Weapon) : null;
    }

    public CommunityContentRevision[] RevisionsVisible(
        string kind, Guid contentId, string? viewerId)
    {
        string normalized = CommunityContentKinds.Normalize(kind);
        if (normalized == CommunityContentKinds.Map) return Array.Empty<CommunityContentRevision>();
        lock (_gate)
        {
            if (!_projects.TryGetValue((normalized, contentId), out var value))
                return Array.Empty<CommunityContentRevision>();
            bool owner = value.OwnerId == viewerId;
            if ((value.Archived || value.ModerationHidden) && !owner) return Array.Empty<CommunityContentRevision>();
            return VisibleRevisions(value, owner, allowUnlisted: normalized == CommunityContentKinds.Weapon).Select(revision => ProjectRevision(value, revision))
                .OrderByDescending(r => r.RevisionNumber).ToArray();
        }
    }

    public string? BlobPath(string contentHash)
    {
        if (!MapCommunityClient.ValidHash(contentHash)) return null;
        string path = Path.Combine(_objects, contentHash + ".blob");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Owner-only lifecycle. Retain immutable bytes and revisions
    /// for historical creator references and safe rollback.</summary>
    public bool Archive(string kind, Guid id, string owner, bool archived)
    {
        kind = CommunityContentKinds.Normalize(kind);
        if (kind == CommunityContentKinds.Map) return false;
        lock (_gate)
        {
            if (!_projects.TryGetValue((kind, id), out var project)
                || project.OwnerId != owner) return false;
            if (project.Archived == archived) return true;
            var changed = project with { Archived = archived, UpdatedAt = DateTimeOffset.UtcNow };
            SaveReplacing(changed);
            return true;
        }
    }

    public CommunityContentProject? Promote(string kind, Guid id, string owner,
        string revisionHash)
    {
        kind = CommunityContentKinds.Normalize(kind);
        if (kind == CommunityContentKinds.Weapon
            && Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_COMMUNITY_PUBLISH") != "1")
            throw new InvalidDataException("Community weapon publication is disabled.");
        if (!MapCommunityClient.ValidHash(revisionHash)
            || kind == CommunityContentKinds.Map) return null;
        lock (_gate)
        {
            if (!_projects.TryGetValue((kind, id), out var project)
                || project.OwnerId != owner) return null;
            var revision = project.Revisions.FirstOrDefault(r => r.ContentHash == revisionHash
                && r.Visibility == CommunityContentVisibility.Published);
            if (revision == null) return null;
            var changed = project with
            {
                CurrentHash = revisionHash,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            SaveReplacing(changed);
            return Project(changed, owner: true);
        }
    }

    // Owner-only lifecycle: changes visibility, never immutable archive identity.
    public CommunityContentProject? SetWeaponVisibility(Guid id, string owner, string hash, string visibility)
    {
        visibility = CommunityContentVisibility.Normalize(visibility);
        if (!MapCommunityClient.ValidHash(hash) || string.IsNullOrWhiteSpace(owner))
            throw new InvalidDataException("Invalid exact weapon visibility request.");
        if (visibility != CommunityContentVisibility.Draft
            && Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_COMMUNITY_PUBLISH") != "1")
            throw new InvalidDataException("Community weapon publication is disabled.");
        lock (_gate)
        {
            if (!_projects.TryGetValue((CommunityContentKinds.Weapon, id), out var project) || project.OwnerId != owner)
                return null;
            int at = Array.FindIndex(project.Revisions, r => r.ContentHash == hash);
            if (at < 0) return null;
            var selected = project.Revisions[at];
            var path = BlobPath(hash) ?? throw new InvalidDataException("The exact weapon archive is unavailable.");
            ValidateWeaponBlob(path, id, project.Name, hash, selected.Bytes);
            if (selected.Visibility == visibility) return Project(project, owner: true);
            var revisions = project.Revisions.ToArray();
            revisions[at] = selected with { Visibility = visibility };
            string? current = project.CurrentHash;
            if (visibility == CommunityContentVisibility.Published) current = hash;
            else if (current == hash) current = revisions.LastOrDefault(r => r.Visibility == CommunityContentVisibility.Published)?.ContentHash;
            // Project presentation is not versioned. Reconstruct safe visible
            // metadata rather than publishing fields inherited from a draft.
            var visible = current != null ? revisions.First(r => r.ContentHash == current)
                : revisions.LastOrDefault(r => r.Visibility == CommunityContentVisibility.Unlisted);
            WeaponPackage? display = visible == null ? null : ValidateWeaponBlob(
                BlobPath(visible.ContentHash) ?? throw new InvalidDataException("Visible weapon archive is unavailable."),
                id, project.Name, visible.ContentHash, visible.Bytes);
            var changed = project with
            {
                Revisions = revisions, CurrentHash = current, UpdatedAt = DateTimeOffset.UtcNow,
                DisplayName = display?.Project.Name, Author = display?.Project.Author,
                Description = null, Tags = Array.Empty<string>()
            };
            SaveReplacing(changed);
            return Project(changed, owner: true);
        }
    }

    /// <summary>Authorize a raw immutable asset by exact revision hash. The
    /// requested hash is not itself proof of public visibility.</summary>
    public string? AuthorizedBlob(string hash, string? viewerId)
    {
        if (!MapCommunityClient.ValidHash(hash)) return null;
        long declared = -1;
        CommunityContentStoredProject? weapon = null;
        lock (_gate)
        {
            foreach (var project in _projects.Values)
            {
                if ((project.Archived || project.ModerationHidden) && project.OwnerId != viewerId) continue;
                bool owner = viewerId != null && project.OwnerId == viewerId;
                foreach (var revision in project.Revisions)
                {
                    if (revision.ContentHash != hash) continue;
                    // Every published immutable revision is discoverable
                    // through the public revisions API, including versions
                    // superseded by a newer published CurrentHash. Retain
                    // exact historical blob fetches until explicit lifecycle
                    // deletion, without exposing drafts or archived projects.
                    if (!owner && revision.Visibility != CommunityContentVisibility.Published
                        && !(project.Kind == CommunityContentKinds.Weapon && revision.Visibility == CommunityContentVisibility.Unlisted))
                        continue;
                    declared = revision.Bytes;
                    if (project.Kind == CommunityContentKinds.Weapon) weapon = project;
                    break;
                }
                if (declared >= 0) break;
            }
        }
        if (declared < 0) return null;
        string path = Path.Combine(_objects, hash + ".blob");
        if (!File.Exists(path) || new FileInfo(path).Length != declared
            || MapBuildFingerprint.HashFile(path) != hash)
            throw new InvalidDataException("Referenced v3 asset is missing or corrupted.");
        if (weapon != null) ValidateWeaponBlob(path, weapon.ContentId, weapon.Name, hash, declared);
        return path;
    }

    public CommunityContentReport SubmitReport(string kind, Guid id, string hash, string reporter, string reason)
    {
        if (kind != CommunityContentKinds.Weapon || id == Guid.Empty || !MapCommunityClient.ValidHash(hash)
            || !SafeText(reporter, 128) || !SafeText(reason, 512))
            throw new InvalidDataException("Invalid typed weapon report.");
        lock (_gate)
        {
            var existing = _reports.FirstOrDefault(r => r.ReporterId == reporter && r.Kind == kind && r.ContentId == id && r.Hash == hash);
            if (existing != null) return CopyReport(existing);
            if (!_projects.TryGetValue((kind, id), out var project)
                || project.OwnerId != reporter && (project.Archived || project.ModerationHidden)
                || !project.Revisions.Any(r => r.ContentHash == hash
                    && (project.OwnerId == reporter || r.Visibility is CommunityContentVisibility.Published or CommunityContentVisibility.Unlisted)))
                throw new InvalidDataException("The reported revision is unavailable.");
            var now = DateTimeOffset.UtcNow;
            if (_reports.Count >= 4096 || _reports.Count(r => r.ReporterId == reporter && r.CreatedAt >= now.AddDays(-1)) >= 20)
                throw new InvalidDataException("Typed report rate or storage budget exceeded.");
            var report = new CommunityContentReport(Guid.NewGuid(), kind, id, hash, reporter, reason, now);
            var reports = _reports.Append(report).ToArray();
            PersistReports(reports, null); _reports.Add(report); return CopyReport(report);
        }
    }

    // Service-only seam: callers must authenticate and authorize moderator tickets.
    internal CommunityContentReport ResolveReport(Guid reportId, string moderatorId, bool hide, string reason)
    {
        if (!SafeText(moderatorId, 128) || !SafeText(reason, 512)) throw new InvalidDataException("Invalid moderation decision.");
        lock (_gate)
        {
            int at = _reports.FindIndex(r => r.ReportId == reportId);
            if (at < 0) throw new InvalidDataException("Typed report not found.");
            var original = _reports[at];
            var project = _projects[(original.Kind, original.ContentId)];
            var history = original.DecisionHistory ?? (original.ResolvedAt is { } previous
                ? new[] { new CommunityContentModerationDecision(original.ModeratorId!, previous, original.Hide!.Value, original.DecisionReason!) }
                : Array.Empty<CommunityContentModerationDecision>());
            if (history.Length >= 32) throw new InvalidDataException("Moderation decision history budget exceeded.");
            var now = DateTimeOffset.UtcNow;
            var minimum = history.Length == 0 ? original.CreatedAt : history[^1].DecidedAt;
            if (now < minimum) now = minimum;
            var decision = original with { ModeratorId = moderatorId, ResolvedAt = now, Hide = hide, DecisionReason = reason,
                DecisionHistory = history.Append(new CommunityContentModerationDecision(moderatorId, now, hide, reason)).ToArray() };
            var changed = project with { ModerationHidden = hide };
            var reports = _reports.ToArray(); reports[at] = decision;
            PersistReports(reports, changed);
            _reports[at] = decision; _projects[(changed.Kind, changed.ContentId)] = changed;
            return CopyReport(decision);
        }
    }

    internal CommunityContentReport[] ListReports()
    { lock (_gate) return _reports.Select(CopyReport).ToArray(); }

    private static CommunityContentReport CopyReport(CommunityContentReport report)
        => report with { DecisionHistory = report.DecisionHistory?.ToArray() };

    private void PersistReports(CommunityContentReport[] reports, CommunityContentStoredProject? replacement)
    {
        var projects = _projects.Values.Select(p => replacement != null && p.Kind == replacement.Kind && p.ContentId == replacement.ContentId ? replacement : p)
            .OrderBy(p => p.Kind, StringComparer.Ordinal).ThenBy(p => p.ContentId).ToArray();
        AtomicFile.Write(_index, JsonSerializer.SerializeToUtf8Bytes(new CommunityContentStoredState(Format, projects, reports), MapPackageReader.JsonOptions));
    }

    private static void ValidateReport(CommunityContentReport report)
    {
        if (report == null || report.ReportId == Guid.Empty || report.Kind != CommunityContentKinds.Weapon
            || report.ContentId == Guid.Empty || !MapCommunityClient.ValidHash(report.Hash)
            || !SafeText(report.ReporterId, 128) || !SafeText(report.Reason, 512) || report.CreatedAt == default
            || report.ResolvedAt == null && (report.ModeratorId != null || report.Hide != null || report.DecisionReason != null)
            || report.ResolvedAt != null && (!SafeText(report.ModeratorId, 128) || !SafeText(report.DecisionReason, 512)
                || report.Hide == null || report.ResolvedAt < report.CreatedAt))
            throw new InvalidDataException("Invalid persisted typed moderation report.");
        if (report.DecisionHistory is { } history)
        {
            if (history.Length is < 1 or > 32 || report.ResolvedAt == null)
                throw new InvalidDataException("Invalid moderation history budget.");
            DateTimeOffset previous = report.CreatedAt;
            foreach (var decision in history)
            {
                if (decision == null || !SafeText(decision.ModeratorId, 128) || !SafeText(decision.Reason, 512)
                    || decision.DecidedAt < previous)
                    throw new InvalidDataException("Invalid immutable moderation history.");
                previous = decision.DecidedAt;
            }
            var last = history[^1];
            if (last.ModeratorId != report.ModeratorId || last.DecidedAt != report.ResolvedAt
                || last.Hide != report.Hide || last.Reason != report.DecisionReason)
                throw new InvalidDataException("Moderation history differs from its latest decision.");
        }
    }

    private void SaveReplacing(CommunityContentStoredProject replacement)
    {
        CommunityContentStoredProject[] updated = _projects.Values
            .Where(p => !(p.Kind == replacement.Kind && p.ContentId == replacement.ContentId))
            .Append(replacement)
            .OrderBy(p => p.Kind, StringComparer.Ordinal)
            .ThenBy(p => p.ContentId).ToArray();
        AtomicFile.Write(_index, JsonSerializer.SerializeToUtf8Bytes(
            new CommunityContentStoredState(Format, updated, _reports.ToArray()),
            MapPackageReader.JsonOptions));
        _projects[(replacement.Kind, replacement.ContentId)] = replacement;
    }

    private void PutVerifiedBlob(string source, string hash, long bytes)
    {
        string output = Path.Combine(_objects, hash + ".blob");
        if (File.Exists(output))
        {
            if (new FileInfo(output).Length != bytes
                || MapBuildFingerprint.HashFile(output) != hash)
                throw new InvalidDataException(
                    "Existing immutable v3 content object is corrupted; operator repair required.");
            return;
        }
        string staging = Path.Combine(_objects,
            hash + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var input = new FileStream(source, FileMode.Open,
                FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
            using (var target = new FileStream(staging, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
            {
                input.CopyTo(target, 65536);
                target.Flush(flushToDisk: true);
            }
            if (new FileInfo(staging).Length != bytes
                || MapBuildFingerprint.HashFile(staging) != hash)
                throw new InvalidDataException("V3 source changed during verified copy.");
            File.Move(staging, output);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    private CommunityContentProject? Project(CommunityContentStoredProject data, bool owner, bool allowUnlisted = false)
    {
        if ((data.Archived || data.ModerationHidden) && !owner) return null;
        CommunityContentRevision[] visible = VisibleRevisions(data, owner, allowUnlisted)
            .Select(revision => ProjectRevision(data, revision)).ToArray();
        if (visible.Length == 0) return null;
        CommunityContentRevision latest = visible[^1];
        CommunityContentRevision? current = data.CurrentHash != null
            ? visible.FirstOrDefault(r => r.ContentHash == data.CurrentHash)
            : null;
        if (!owner && current == null && !allowUnlisted) return null;
        CommunityContentRevision selected = current ?? latest;
        // A missing referenced object is unavailable, not a downloadable
        // catalog entry. PR-09 will add exact verified download endpoints.
        if (BlobPath(selected.ContentHash) is not { } blob
            || new FileInfo(blob).Length != selected.Bytes) return null;
        // Direct weapon links expose only visible immutable package metadata,
        // never project-level presentation inherited from an earlier draft.
        WeaponPackage? directWeapon = !owner && current == null && allowUnlisted && data.Kind == CommunityContentKinds.Weapon
            ? ValidateWeaponBlob(BlobPath(latest.ContentHash) ?? throw new InvalidDataException("Visible weapon blob is unavailable."),
                data.ContentId, data.Name, latest.ContentHash, latest.Bytes) : null;
        return new CommunityContentProject(
            data.Kind, data.ContentId, data.Name, directWeapon != null ? directWeapon.Project.Name : data.DisplayName,
            directWeapon != null ? directWeapon.Project.Author : data.Author,
            owner ? data.OwnerId : null,
            directWeapon != null ? null : data.Description, directWeapon != null ? Array.Empty<string>() : data.Tags.ToArray(),
            current?.ContentHash,
            latest.ContentHash,
            visible.Length,
            directWeapon != null ? visible[0].CreatedAt : data.CreatedAt,
            directWeapon != null ? latest.CreatedAt : owner ? data.Revisions[^1].CreatedAt : data.UpdatedAt,
            current,
            latest,
            CanManage: owner, Archived: data.Archived, ModerationHidden: data.ModerationHidden);
    }

    private static CommunityContentRevision[] VisibleRevisions(
        CommunityContentStoredProject data, bool owner, bool allowUnlisted = false)
    {
        if (owner) return data.Revisions.ToArray();
        CommunityContentRevision[] published = data.Revisions
            .Where(r => r.Visibility == CommunityContentVisibility.Published
                || allowUnlisted && data.Kind == CommunityContentKinds.Weapon && r.Visibility == CommunityContentVisibility.Unlisted).ToArray();
        // The true parent may be an unpublished draft. Public revision edges
        // must reference only hashes already visible to an anonymous reader,
        // while the full immutable parent lineage remains owner-visible.
        for (int i = 0; i < published.Length; i++)
            published[i] = published[i] with
            {
                ParentHash = i == 0 ? null : published[i - 1].ContentHash
            };
        return published;
    }

    private static void ValidateRegistration(CommunityContentRegistration m)
    {
        string normalized = CommunityContentKinds.Normalize(m.Kind);
        if (normalized == CommunityContentKinds.Map)
            throw new InvalidDataException("Map registrations must use the v2 map revision catalog.");
        if (m.Kind != normalized || m.ContentId == Guid.Empty
            || !SafeText(m.OwnerId, 128)
            || !SafeText(m.Name, 128) || !SafeText(m.DisplayName, 128, optional: true)
            || !SafeText(m.Author, 128, optional: true)
            || !SafeText(m.Description, 2000, optional: true)
            || m.Tags == null || m.Tags.Length > MaximumTags
            || m.Tags.Any(t => !SafeText(t, 32))
            || m.Tags.Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Tags.Length
            || !SafeText(m.ReleaseNotes, 4000, optional: true)
            || m.ExpectedParentHash != null
                && !MapCommunityClient.ValidHash(m.ExpectedParentHash))
            throw new InvalidDataException("Invalid v3 creator metadata.");
        _ = CommunityContentVisibility.Normalize(m.Visibility);
        if (!SupportedMime(m.Kind, m.MimeType))
            throw new InvalidDataException("Unsupported v3 kind/MIME combination.");
        ValidateDependencies(m.Dependencies, m.Kind, m.ContentId);
    }

    private void ValidateProject(CommunityContentStoredProject p)
    {
        if (p == null || p.Revisions == null || p.Revisions.Length == 0
            || p.Revisions.Length > MaximumRevisions
            || p.ContentId == Guid.Empty || !SafeText(p.OwnerId, 128)
            || !SafeText(p.Name, 128) || !SafeText(p.DisplayName, 128, true)
            || !SafeText(p.Author, 128, true) || !SafeText(p.Description, 2000, true)
            || p.Tags == null || p.Tags.Length > MaximumTags
            || p.Tags.Any(t => !SafeText(t, 32))
            || !MapCommunityClient.ValidHash(p.LatestHash)
            || p.CurrentHash != null && !MapCommunityClient.ValidHash(p.CurrentHash))
            throw new InvalidDataException("Invalid stored v3 content project.");
        string kind = CommunityContentKinds.Normalize(p.Kind);
        if (kind == CommunityContentKinds.Map || kind != p.Kind)
            throw new InvalidDataException("Map content cannot be duplicated in the v3 metadata store.");
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < p.Revisions.Length; i++)
        {
            var rev = p.Revisions[i];
            if (rev == null || rev.RevisionNumber != i + 1
                || !MapCommunityClient.ValidHash(rev.ContentHash)
                || rev.ArchiveHash != null || rev.Bytes is <= 0 or > MapPackageReader.MaxEntryBytes
                || !SupportedMime(kind, rev.MimeType)
                || !hashes.Add(rev.ContentHash)
                || rev.ParentHash != (i == 0 ? null : p.Revisions[i - 1].ContentHash)
                || !SafeText(rev.ReleaseNotes, 4000, true))
                throw new InvalidDataException("Invalid stored v3 content revision.");
            _ = CommunityContentVisibility.Normalize(rev.Visibility);
            ValidateDependencies(rev.Dependencies, kind, p.ContentId);
            if (kind == CommunityContentKinds.Weapon)
                WeaponRevisionProof(p.ContentId, p.Name, rev);
        }
        if (p.Revisions[^1].ContentHash != p.LatestHash
            || p.CurrentHash != null && !p.Revisions.Any(r =>
                r.ContentHash == p.CurrentHash
                && r.Visibility == CommunityContentVisibility.Published))
            throw new InvalidDataException("V3 content pointers differ from immutable revisions.");
    }

    private CommunityContentRevision ProjectRevision(CommunityContentStoredProject project, CommunityContentRevision revision)
    {
        if (project.Kind != CommunityContentKinds.Weapon) return revision;
        return revision with { WeaponPackage = WeaponRevisionProof(project.ContentId, project.Name, revision) };
    }

    // Called only for admitted catalog revisions (or fully validated persisted
    // catalog entries). Upload staging paths never populate this bounded cache.
    private WeaponPackageRevisionIdentity WeaponRevisionProof(Guid id, string name, CommunityContentRevision revision)
    {
        var key = (revision.ContentHash, id, name, revision.Bytes);
        string path = Path.Combine(_objects, revision.ContentHash + ".blob");
        var info = new FileInfo(path); info.Refresh();
        if (!info.Exists || info.LinkTarget != null || (info.Attributes & FileAttributes.ReparsePoint) != 0
            || info.Length != revision.Bytes || info.Length is <= 0 or > WeaponPackageLimits.MaximumArchiveBytes)
        {
            _weaponProofs.Remove(key);
            throw new InvalidDataException("Registered weapon blob is missing, linked or has changed length.");
        }
        long write = info.LastWriteTimeUtc.Ticks, creation = info.CreationTimeUtc.Ticks;
        var attributes = info.Attributes;
        if (_weaponProofs.TryGetValue(key, out var proof) && proof.WriteTicks == write
            && proof.CreationTicks == creation && proof.Attributes == attributes) return proof.Identity;
        _weaponProofs.Remove(key);
        var package = ValidateWeaponBlob(path, id, name, revision.ContentHash, revision.Bytes);
        info.Refresh();
        if (!info.Exists || info.LinkTarget != null || info.Length != revision.Bytes
            || info.LastWriteTimeUtc.Ticks != write || info.CreationTimeUtc.Ticks != creation || info.Attributes != attributes)
            throw new InvalidDataException("Registered weapon blob changed during metadata validation.");
        if (_weaponProofs.Count >= MaximumRevisions) throw new InvalidDataException("Weapon revision proof budget exceeded.");
        var identity = new WeaponPackageRevisionIdentity(package.PackageHash, package.ContentHash,
            package.DependencyHash, package.Definition.Mechanics.DefinitionHash);
        _weaponProofs.Add(key, new(write, creation, attributes, identity));
        return identity;
    }

    private static WeaponPackage ValidateWeaponBlob(string path, Guid contentId, string name, string hash, long bytes)
    {
        if (new FileInfo(path).LinkTarget != null)
            throw new InvalidDataException("Weapon package blobs cannot be links.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is <= 0 or > WeaponPackageLimits.MaximumArchiveBytes || input.Length != bytes)
            throw new InvalidDataException("Weapon package blob exceeds its exact byte budget.");
        var archive = new byte[checked((int)input.Length)]; input.ReadExactly(archive);
        var package = WeaponPackage.Parse(archive);
        if (package.Project.ContentId != contentId || !StringComparer.Ordinal.Equals(package.Project.Name, name)
            || !StringComparer.Ordinal.Equals(package.PackageHash, hash))
            throw new InvalidDataException("Weapon package identity or name differs from its immutable Community revision.");
        return package;
    }

    private static bool SafeText(string? text, int maximum, bool optional = false)
        => optional && text == null || text is { Length: > 0 }
            && text.Length <= maximum && !string.IsNullOrWhiteSpace(text)
            && !text.Any(char.IsControl);

    private static bool SupportedMime(string kind, string mime) => kind switch
    {
        CommunityContentKinds.Weapon => mime == CommunityAssetValidator.WeaponMimeType,
        CommunityContentKinds.Texture =>
            mime is "image/png" or "image/jpeg" or "image/ktx2" or "image/x-tga",
        CommunityContentKinds.Material =>
            mime is "application/vnd.projectprime.material+json",
        CommunityContentKinds.Model =>
            mime is "model/gltf-binary" or "model/gltf+json",
        CommunityContentKinds.Prefab =>
            mime is "application/vnd.projectprime.prefab+json",
        _ => false
    };

    private void EnsureAcyclic(string rootKind, Guid rootId,
        CommunityContentDependency[] roots)
    {
        var explored = new HashSet<(string Kind, Guid Id)>();
        var visiting = new HashSet<(string Kind, Guid Id)> { (rootKind, rootId) };
        void Walk(CommunityContentDependency dependency, int depth)
        {
            if (depth > 12 || explored.Count > 128)
                throw new InvalidDataException("Optional content graph exceeds its depth or item limit.");
            var next = (dependency.Kind, dependency.ContentId);
            if (!visiting.Add(next))
                throw new InvalidDataException("Cyclic generic content dependencies are prohibited.");
            try
            {
                if (_projects.TryGetValue(next, out var p))
                {
                    var rev = p.Revisions.FirstOrDefault(r =>
                        r.ContentHash == dependency.Hash
                        && r.Visibility == CommunityContentVisibility.Published);
                    if (rev != null)
                        foreach (var child in rev.Dependencies
                            ?? Array.Empty<CommunityContentDependency>())
                            Walk(child, depth + 1);
                }
                explored.Add(next);
            }
            finally { visiting.Remove(next); }
        }
        foreach (var dependency in roots) Walk(dependency, 0);
    }

    private static void ValidateDependencies(CommunityContentDependency[]? items,
        string kind, Guid id)
    {
        if (items == null) return;
        if (kind == CommunityContentKinds.Weapon && items.Length != 0)
            throw new InvalidDataException("Weapon packages are self-contained and cannot declare external Community dependencies.");
        if (items.Length > MaximumDependencies) throw new InvalidDataException("Too many v3 dependencies.");
        foreach (CommunityContentDependency item in items)
        {
            if (item == null || item.ContentId == Guid.Empty
                || !CommunityDependencyRoles.IsAllowed(item.Role)
                || !MapCommunityClient.ValidHash(item.Hash)
                || (item.Kind == kind && item.ContentId == id))
                throw new InvalidDataException("Invalid or self-referencing v3 content dependency.");
            _ = CommunityContentKinds.Normalize(item.Kind);
            if (item.Kind == CommunityContentKinds.Weapon)
                throw new InvalidDataException("Weapon packages cannot be optional visual dependencies.");
        }
        if (items.Select(x => (x.Kind, x.ContentId))
            .Distinct().Count() != items.Length)
            throw new InvalidDataException("Duplicate v3 content dependency.");
    }
}
