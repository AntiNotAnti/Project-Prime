# Community Hub CH-05B1: connection pooling and storage limits

Standalone PR from `main`, independent of open CH-01 through CH-05A.

## Changes

- Two process-wide `SocketsHttpHandler` pools replace the per-operation socket-handler creation. Separate proxy/direct pools preserve the existing TLS-frame fallback behavior and automatic certificate validation. A private `HttpClient` per operation keeps the bearer ticket isolated in instance request headers. Handler lifetime and idle lifetime are bounded; no auth token is stored in static state.
- Archive CAS default quota: 512 MiB on Android, 4 GiB on desktop. Operators may override with `PROJECT_PRIME_COMMUNITY_CACHE_MIB=512..1048576`. Explicit constructor budgets used by existing tests/embedders still take precedence. Eviction continues to skip leased archives and never removes installed/replay archives.
- Optional SHA-256 public object mirror defaults to a 50 GiB byte budget, with `PROJECT_PRIME_MAP_MIRROR_MAX_GIB=1..1000` and 64 MiB disk headroom. Over-capacity mirror copies fail closed, leaving publishing and the authoritative origin unaffected. The mirror does **not** delete immutable public objects or revoke previously distributed CDN URLs.

## Testing

```bash
dotnet run --project tools/map-community-check -c Release
dotnet run --project tools/community-cache-check -c Release
dotnet run --project tools/community-cdn-check -c Release
dotnet run --project tools/community-service-check -c Release
dotnet run --project tools/community-transfer-check -c Release
```

`map-community-check` exercises short-lived authenticated and anonymous clients using the shared transport. `community-cdn-check` rejects over-budget mirror staging. Existing cache lease/eviction tests remain the protection against over-eager quota cleanup.

## Limits

- Quota checks enumerate stored immutable mirror objects at publication time, not during normal catalog/download requests. Large multi-node storage deployments may need a transactionally maintained ledger and distributed lease, with measurement before replacement.
- No external CDN, low-storage Android acceptance, end-to-end network throughput or TLS-proxy certification was performed here. This PR must pass build and device gates before release.
