# CH-06 | Creator revision and visibility workflow

**Stack:** CH-01 PR #600 -> CH-06. Relies on CH-01's transfer-row stability fix. Independent of the CH-05A visual browser branch.

## Implemented
- My Uploads provides current vs latest immutable hashes, project lifecycle status, author-only Archive/Restore, and a selectable full immutable revision history.
- A revision detail view exposes its authored parent, release notes, draft/unlisted/published state, and explicit visibility, restore and current-revision promotion actions when permitted. Existing `MapCommunityClient` v2 endpoints are wrapped in `CommunityMapOperations` to retain authenticated ticket refresh.
- All mutations use a two-step confirmation and refresh the owner-side project snapshot to refuse stale latest/current hashes, lifecycle state or lost management permissions.
- The studio's Publish pane rejects double-click duplicate queuing; after successful frozen publication it opens the durable Transfers tab.
- Permanent deletion/purge, reports, moderation and collaborator settings stay in Advanced Revisions, which remains reachable as rollback.

## Invariants and caveats
- Visibility is catalog metadata; neither `.ppmap` bytes nor MapId/content/package hashes change.
- Previously published public CDN URLs may remain reachable after setting Unlisted or Draft. The UI warns about this limitation.
- A client-side optimistic read before mutation reduces accidental stale edits but does not replace server-side transaction/permission checks; the existing service still authorizes each change.
- Do not interpret a headless API test as proof of actual Windows/Linux/macOS creator GUI, Android Game view, NetRadiant handoff or replay compatibility.

## Gates
```bash
dotnet run --project tools/map-editor-check -c Release -- --community-only
dotnet run --project tools/community-transfer-check -c Release
dotnet run --project tools/map-community-check -c Release
dotnet build src/MphRead/MphRead.csproj -c Release
```
Manual UI acceptance must include draft -> unlisted -> published, revision promotion, archive/restore, stale conflict and keyboard/controller navigation with concurrent Game/Studio transfer ownership.
