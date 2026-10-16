# Community Hub CH-02 | Indexed catalog reads and bounded mirror checks

Base: `main` at `ea017489bfc1299af1c85dbb3f1aa185d32a32f8`. Independent of CH-01.

## Catalog
- Decorated public/private map listings now use indexed favorite counts and membership instead of scanning the entire favorites file for each map revision.
- JSON files and the existing owner/favorite visibility APIs remain authoritative; indexes are rebuilt at startup and updated only after a successful atomic favorite mutation.
- Purging a map removes all its index entries along with durable creator records.
- Worst-case list decoration drops from O(map revisions × favorites) to O(map revisions), excluding other existing catalog work and serialization.

## Public immutable mirror metadata
- Full object hashes remain mandatory for new promotions, operator integrity scans and stale metadata.
- Public CDN URL presentation may reuse a successful SHA-256 verification for up to 30 seconds when file size, creation and modification timestamps are unchanged. Changed or missing files cause a full digest check before a URL is returned.
- This is an advisory optimization for public metadata only. The client continues to verify the entire downloaded SHA-256 and exact MapId/content/package identity before Ready, with origin fallback on failure.
- A malicious process that alters bytes while spoofing all filesystem metadata could produce a briefly stale CDN URL, but cannot bypass the strict client download verifier. Do not use this shortcut to authorize draft/unlisted content or replace full integrity checks.

## Tests
- `tools/map-editor-check -- --community-only`: favorites add/remove/re-add and public counts.
- `tools/community-cdn-check`: modified same-size objects invalidate cached public-location metadata; repaired content requires fresh digest.
- Existing `community-service-check`, `map-multiplayer-check`, and historical immutable identity gates must remain green.

## Limitations
No live CDN has been deployed. Storage, network and actual p95 catalog latency must be measured in staging with large map/favorite counts before claiming faster public browsing. PR-03/04 and browser redesign remain follow-ups.
