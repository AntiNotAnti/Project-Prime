# Map publication, cache and career fixture

Build the dedicated production assembly first, then point this fixture to that
assembly. It links the actual BCL-only publication/cache source so it can directly
exercise cross-process file leases. Stock hashing and career tracking execute the
production assembly methods through reflection. No game assets, network server,
remote backend or production credential is used.

```sh
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true --artifacts-path /tmp/prime-map-build -m:1
dotnet run --project tools/maps-career-check/MapsCareerCheck.csproj -c Release -p:PrimeAssembly=/tmp/prime-map-build/bin/MphRead/release/ProjectPrime.dll --artifacts-path /tmp/prime-map-check -m:1
```

The fixture creates four child copies of its own executable and kills one to
verify orphaned file-lease recovery. All files belong to a unique temporary
fixture directory, which is removed afterward. The budget is scaled to 256 bytes
with 64-byte reservations. The separate 16 MiB publication sample checks bounded
owner allocations and prints local elapsed time. This is not a storage latency
benchmark or a full 2 GiB stress test.

Backend handler tests and the disposable PostgreSQL acceptance runner are under
`tools/edge-check`. Run `node tools/edge-check/check-career-sql.mjs` for the pinned
PGlite check of all migrations and both SQL gates against a compatible fixture.
The full Supabase relay/JWT and deployment-schema checks remain separate.
Implementation, results and limits are in
`docs/network/network-server-slices-2026-10-06.md`.
