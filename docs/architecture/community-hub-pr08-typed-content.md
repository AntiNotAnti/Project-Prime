# Project Prime Community Hub 3.0: PR-08 Typed Content Catalog & v3 Read-Through

**Base:** merged PR-07 #540 at `989709d58c745178860398c36953e83e88de7470`.  
**Historical contract:** the existing v2 map project/revision catalog and exact immutable `.ppmap` SHA-256 identities remain authoritative; historical replay and host Ready paths are unchanged.

## Decision

The new generic v3 catalog is **read-only over HTTP in PR-08**. It supports typed `map`, `texture`, `material`, `model` and `prefab` metadata, but creator-driven uploads of non-map bytes are explicitly deferred to PR-09 with subtype-specific validation. The `GET /v3/capabilities` document accurately advertises `SupportsGenericContentCatalog=true` and `SupportsGenericContentPublishing=false`.

No map revisions are copied to a second persistent catalog. V3 map responses are dynamically projected from the shipping `MapCommunityRevisionCatalog` and `MapCreatorCatalog`. Public maps include only permitted published revisions; owners/collaborators can see their current and unpublished latest pointers. A map's `ArchiveHash` means the exact ZIP SHA-256, while `ContentHash` retains the existing manifest's digest. Both are distinct and immutable.

Non-map metadata uses its own atomic, bounded `content_catalog_v3.json` plus immutable local content objects at `content-v3/<sha256>.blob`. This is a **new typed-metadata store**, not a replacement for `map_catalog_v2.json`. V3 metadata registration is a **trusted local server-side API**, never an HTTP write route in PR-08. It checks kind/MIME allowlists, creator-owned stable IDs, parent hash optimistic concurrency, file SHA-256, path containment by digest, byte budget, tags, description, dependency syntax, entry counts, and atomic JSON publication. Each accepted new content revision stores a distinct immutable file hash, number, visibility and parent pointer.

SQLite is **deferred**. Existing on-disk creator/revision metadata is JSON and small; introducing SQLite while also changing the content schema would create parallel writers and an unnecessary migration. After PR-08 load/scale tests establish a real bottleneck, a separately gated migration may add indexed database storage with read-through and rollback.

## V3 schema

| Type | Meaning |
|---|---|
| `CommunityContentProject` | Stable `Kind`, `ContentId`, owner-authorized presentation metadata, current/latest immutable identities and visible revision counts |
| `CommunityContentRevision` | Revision number, `ContentHash`, optional `ArchiveHash` (maps only), bytes, MIME, visibility, parent, release notes and optional pinned dependencies |
| `CommunityContentDependency` | `Kind`, `ContentId`, exact immutable SHA-256. PR-10 will implement full dependency graph and lockfile resolution |
| `CommunityContentPage` | `schemaVersion=3`, `total`, `page`, `pageSize`, bounded `items[]`. Never swaps existing v2 arrays for envelopes |

Valid kinds are `map`, `texture`, `material`, `model`, `prefab`. Valid generic visibility states are `published`, `unlisted`, `draft`. A generic raw file uses its own SHA-256 `ContentHash` and has no `ArchiveHash`; a map uses both established exact identities.

## Additive routes

- `GET /v3/content?kind=map|texture|material|model|prefab&mine=true|false&page=1&pageSize=24&sort=name|updated&query=...&tag=...`: paginated typed projects, `X-Total-Count` and viewer-scoped ETag. Anonymous listing shows only published/discoverable entries. Owner listing requires a Hunter License/creator ticket.
- `GET /v3/content/{kind}/{contentId}`: typed project details; private/draft/unlisted content cannot be read by an anonymous viewer.
- `GET /v3/content/{kind}/{contentId}/revisions`: permitted immutable revision metadata only.
- `GET /v3/capabilities`: lists content kinds and v3 read routes, with `SupportsGenericContentPublishing=false`. Old clients need not call it.
- `POST /v3/content` and generic bytes `GET /v3/content/{kind}/{id}/blob` are **not available**. No unvalidated file can be published or used as a gameplay dependency through this PR.

`MapCommunityClient` adds optional typed discovery/detail/revision reads and falls back to existing v2 map methods if a server does not implement v3. `CommunityMapOperations` forwards these through the established short-lived-client and 401 ticket-refresh mechanism.

## Compatibility and access boundaries

- `map_catalog_v2.json`, creator favorites, collaborators, map ownership, revision numbers, published/current/latest semantics, legacy/modern `/maps` routes, immutable `/packages/{sha}` Range, and historical replays are unchanged.
- Generic blobs are **metadata only** in PR-08. No download/install path uses them, so there is no new way to bypass `MapRuntimeUsage` or multiplayer Ready.
- Public content never exposes `OwnerId` or unpublished revision data. A project with zero public revisions is absent from anonymous results. Owner views use the same existing creator credential, not a weaker new authorization system.
- A generic content record cannot claim `kind=map` or mutate any v2 map history. MapId and generic content GUID share the same type but are **namespaced by kind**.
- Disallowed MIME, malformed hashes/IDs, oversized fields, duplicate revision hashes, mismatched parents, self-dependencies and conflicting creator ownership fail before persistent state changes.
- Generic content object SHA-256 is rechecked during local registration/promotion. The metadata-only HTTP routes do not serve unverified raw asset bytes.

## Acceptance

The new asset-free `tools/community-v3-check` exercises:

1. Real `HttpListener` and legacy v2 package upload, then public v3 map read-through with **exact same** `MapId`, `ContentHash`, `ArchiveHash`, and revision numbers.
2. A later draft map revision is visible only to its owner; public current pointer remains unchanged. V3 state file contains no map revision duplicate.
3. Trusted local texture/material/prefab metadata registration with exact blobs, MIME policy, immutable revisions, parent hash, dependencies, persisted metadata and restart; no public exposure of drafts/owner identity.
4. Public kind/search/paging plus creator `mine=true`, bounded responses, 401/404 permissions, ETag and 304.
5. A map-only legacy service compatibility fallback through existing v2 client methods where supported.
6. A service restart proves both v2 map and generic typed content remain discoverable without migrations.

Existing PR-00–07 checks (Linux/Windows/macOS baseline ZIP hashes, streaming package parity, map-community, transfer/cache, editor, historical replay, multiplayer, dedicated server, Android packaging) remain CI acceptance gates. New v3 metadata does not change rendering, network session protocol, or offline map installation.

## Rollback and follow-ups

Rollback PR-08 without modifying installed `.ppmap`, v2 catalog or creator account state. The new `content_catalog_v3.json` and `content-v3/` asset bytes are dormant in older versions and can be retained for PR-09 forward migration; never silently purge them.

PR-09 will introduce authenticated typed upload/publish/moderation with strict file validators and importable materials/models/prefabs. PR-10 handles shared dependencies and content-tier lockfiles. Database migration and CDN are later independently benchmarked slices, not assumed necessities.
