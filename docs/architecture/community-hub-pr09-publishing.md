# Community Hub PR-09 | Sandboxed typed content publishing

Built on PR-08. Keeps immutable map packages and the v2 map catalog authoritative.

- Adds strict bounded asset intake for PNG/JPEG/KTX2/TGA textures, glTF2 GLB/JSON models (without external URIs/extensions), and inert material/prefab JSON descriptors. No scripts, shader source, plugins or external HTTP references may be published.
- Creator-authenticated `POST /v3/content/{kind}/{contentId}/revisions`, with a bounded base64 JSON metadata header and a separately supplied SHA-256. Content-Length and kind-specific budgets are checked before file transfer; the server validates byte format and digest prior to immutable registry publication.
- Adds `GET /v3/objects/{sha256}` with strict authorization for public or owner-only revisions, Range and ETag; raw blobs remain separate from `.ppmap` installation.
- Adds creator lifecycle archive/restore (revisions and historical immutable bytes remain retained) plus the existing optimistic parent-hash revision control.
- Adds client publish/download/archive methods, plus real local service tests for two original types, SHA parity, public/private visibility, restoration, and malicious data rejection. These are service/authoring resources only; there is no new gameplay dependency.

**Known limitations:** Generic uploads use one bounded request with 128 MiB maximum models and smaller type-specific budgets, not resumable multipart sessions. The existing durable queue continues to handle maps. No generic content is auto-installed into an active scene. More complete glTF, image decompression, provenance/licensing, moderation and import support require separate hardening prior to production enablement.

**Rollback:** Disable v3 publishing and retain the separate immutable content files/metadata for later migration. The server's v2 routes and all historical maps/replays remain unchanged. Full acceptance is the final PR-14 integration gate per user request.
