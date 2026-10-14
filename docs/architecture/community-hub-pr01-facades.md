# Community Hub PR-01: UI-neutral map application facade

**Base:** stacked on PR-00 branch `codex/community-hub-pr00-baseline-20261013`.  
**Backend baseline:** legacy + v2 Community transport, no v3 routes or new package format.

## Implemented boundaries

- `CommunityMapOperations` is a small map-specific facade under `Mods/MapGen/Sharing/`. It exposes typed anonymous catalog, exact-package metadata, map install, authenticated upload and revision publication operations. Additional existing lifecycle operations continue to use `UseClientAsync` so they can be migrated without a competing REST client.
- `ICommunityCredentialSource` has two asynchronous methods: obtain and refresh a short-lived ticket. `DelegateCommunityCredentialSource` allows the shipping Hunter License client to supply tickets without a MapGen -> UI dependency.
- The facade owns short-lived `MapCommunityClient` instances for each operation. It does not persist tickets, record local paths, hold editable project references, use Avalonia or change the HTTP contract. 401 retries refresh exactly once; the underlying package-hash publication is idempotent.
- `MapStudioSharing` now only supplies the Hunter License callbacks and forwards Host/Publish through the facade. `MapStudioCommunityDashboard` uses it for anonymous/owner catalog browsing, installation, both publish paths, and existing authenticated operations.
- All Community UI still resides in Map Studio, intentionally, until PR-02. The facade is reusable by a future Game tab and standalone Studio UI.

## Contracts explicitly unchanged

- `MapId`, `ContentHash`, `PackageHash`, package ZIP structure, version history, draft/unlisted/current semantics.
- `MapCommunityClient` HTTP routes, TLS trust/proxy fallback, upload offsets, content hashing, range downloads and error/status codes.
- Map generation, `MapRuntimeUsage` leases, install commit, lobby readiness, historical replay and host rotation.
- The `WithCommunityAuthentication` editor entry point remains as a shim; calls route through the same 401 recovery and owned-HTTP operation.
- Game shell navigation, asset packer, server/database configuration and existing credentials remain unchanged.

## Tests

Focused `map-community-check` exercises:

1. Public `BrowseProjectsAsync` through the facade with no credential source.
2. Authenticated `mine=true` browse using a rejected first token, one refresh, and exact v2 identity.
3. Repeated immutable upload idempotence through the facade and fresh ticket operation.
4. Pre-cancelled operation does not contact the service or mint/refresh a ticket.

Regression gates must also run map-editor Community revision/history tests, runtime-only custom map installation, full multiplayer acceptance including historical replays, cross-platform game/server build, and normal CI. No PASS claim before check completion.

## Known follow-ups

- This PR does not transfer page state, modals, job scheduling or mutable editor snapshot ownership out of the existing dashboard. That belongs to PR-02 and PR-03.
- No durable queue/download cache, v3 content schema, new map format or CDN is introduced.
- No HTTP client pooling is attempted while preserving old request behavior.
- Test credentials are synthetic loopback secrets, not Supabase sessions.

## Rollback

Revert facade file and adapter call sites; the legacy MapCommunityClient APIs are unchanged. No migration required. Do not merge until PR-00's CI and golden fixture are accepted; do not start PR-02 until PR-01 facade checks pass.
