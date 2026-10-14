# Community Hub PR-09 | Sandboxed typed content publishing

Built on PR-08. Keeps immutable map packages and the v2 map catalog authoritative.

- Adds strict bounded asset intake for PNG/JPEG/KTX2/TGA textures, glTF2 GLB/JSON models (without external URIs/extensions), and inert material/prefab JSON descriptors. No scripts, shader source, plugins or external HTTP references may be published.
- Creator-authenticated `POST /v3/content/{kind}/{contentId}/revisions`, with a bounded base64 JSON metadata header and a separately supplied SHA-256. Content-Length and kind-specific budgets are checked before file transfer; the server validates byte format and digest prior to immutable registry publication.
- Adds `GET /v3/objects/{sha256}` with strict authorization for public or owner-only revisions, Range and ETag; raw blobs remain separate from `.ppmap` installation.
- Adds creator lifecycle archive/restore (revisions and historical immutable bytes remain retained) plus the existing optimistic parent-hash revision control.
- Adds client publish/download/archive methods, plus real local service tests for two original types, SHA parity, public/private visibility, restoration, and malicious data rejection. These are service/authoring resources only; there is no new gameplay dependency.

**Known limitations:** Generic uploads use one bounded request with 128 MiB maximum models and smaller type-specific budgets, not resumable multipart sessions. The existing durable queue continues to handle maps. No generic content is auto-installed into an active scene. More complete glTF, image decompression, provenance/licensing, moderation and import support require separate hardening prior to production enablement.

**Rollback:** Disable v3 publishing and retain the separate immutable content files/metadata for later migration. The server's v2 routes and all historical maps/replays remain unchanged. Full acceptance is the final PR-14 integration gate per user request.

## Follow-up hardening of the publication boundary

- Large `POST /v3/content/{kind}/{id}/revisions` streams now consume the **creator-upload lane**, never the metadata/catalog lane. `GET /v3/objects/{sha256}` uses the independent download lane. The existing 16-way multiplayer package download capacity is not reduced.
- All publicly listed immutable revisions, including previously published revisions superseded by a new current pointer, remain available by exact raw-object SHA-256. Draft and archived objects remain private.
- Generic v3 content has separately bounded origin storage: 50 GiB total and 10 GiB per owner by default. The single-writer metadata registry enforces actual distinct SHA-256 bytes before writing a new revision. Shared identical hashes count once within each scope. These budgets are intentionally independent of v2 `.ppmap` storage.
- CI adds a real three-stalled-v3-upload campaign, verifying that typed browsing and published raw downloads remain responsive while a fourth upload receives 503/Retry-After. Tests also exercise exact historical blob retrieval, storage budget rejection, deduplication, and restart recovery.
- These checks do not imply full deep image decoder safety: the format validators remain a bounded preliminary intake gate. Actual engine rendering/decoding, licensing, moderation and advanced glTF semantics require later independently reviewed integration.
