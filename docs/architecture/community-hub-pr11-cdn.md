# Community Hub PR-11 | Immutable Object Mirror & Trusted CDN Reads

The Community control plane remains the existing authenticated server. This PR adds an **opt-in, content-addressed filesystem object mirror** suitable for a mounted external object store, not a claim that a public CDN has been deployed.

- `CommunityObjectStore` uses immutable `sha256/{prefix}/{sha}.ppmap` object keys, streaming staged copy, byte-length + SHA-256 verification and immutable no-overwrite policy.
- `GET /v3/distribution/maps/{sha}` advertises the current exact origin path and, only for published/discoverable content with a verified mirror and configured HTTPS operator CDN base, an optional public CDN URL.
- Creator private/draft/unlisted content has **no globally readable CDN URL**. Such bytes stay on the existing authenticated origin route; no signed private CDN tokens are implemented in this phase.
- `MapCommunityClient.DownloadViaTrustedCdnOrOriginAsync` uses a **client-side configured host allowlist**, requires HTTPS/no redirect query/credentials, confirms full SHA-256 and strict `MapId`+content identity, and falls back to the existing origin Range downloader.
- Opt-in settings: server `PROJECT_PRIME_MAP_OBJECT_MIRROR_ENABLED=1`, `PROJECT_PRIME_MAP_CDN_BASE=https://.../`, client `PROJECT_PRIME_TRUSTED_CDN_HOSTS=host1,host2`. None are on by default.
- `CommunityObjectStore.Inspect` reports verified/missing/corrupt objects for operator reconciliation. Mirroring is explicit and does not rewrite historical map packages or remove origin copies.
- `tools/community-cdn-check` verifies digest-checked mirroring, shared SHA, public/private URL policy, corruption rejection and origin fallback under a local service.

**Deployment limitations:** A filesystem mirror and mock HTTP downloader are not an external object storage deployment. Production requires CDN storage mapping, signed URL provenance/expiry design for private assets, staging verification, region tests and a load campaign. These flags are intentionally disabled until those gates pass. This is a reversible additive data plane with no v2 protocol, historical replay or multiplayer Ready change. CI verification is consolidated at final PR-14 by user request.
