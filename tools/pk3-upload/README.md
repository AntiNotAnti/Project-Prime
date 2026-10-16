# Project Prime PK3 web conversion service

Separate, authenticated service at a **loopback** port (8094 by default).
Accepts resumable creator-owned `.pk3` source uploads and invokes the
headless `tools/pk3-converter` binary in a separate child process. This
does **not** run the game, mutate gameplay state or change the existing
Community package catalog.

## Website to game flow

1. Browser creates a `ppm1` Hunter License ticket via the existing
   `/api/maps/ticket` endpoint. No backend secrets go to the browser.
2. `POST /v1/sources/{sha256}` with JSON
   `{"bytes":10000,"roomName":"Q3_ARENA","levelName":"q3dm17","rightsConfirmed":true}`
   reserves a private upload session.
3. `PUT /v1/sources/{sha256}?offset=0` sends sequential binary chunks no
   larger than 8 MiB, with exact `Content-Length`.
   `GET /v1/sources/{sha256}` recovers the server-confirmed offset.
   A session belongs exclusively to its verified Hunter identity, even when
   two players upload the exact same archive hash.
4. `POST /v1/sources/{sha256}/complete` verifies the whole input SHA-256,
   moves it atomically to a private job and queues conversion.
5. `GET /v1/conversions/{jobId}` polls queue progress or validated output.
   The worker output is checked for exact hash, size and map GUID.
6. `GET /v1/conversions/{jobId}/package` streams the `.ppmap` back to the
   same authenticated creator with Content-Disposition and X-Prime-SHA256.
7. The browser displays converter warnings and obtains a fresh Hunter ticket
   before publishing via the existing authoritative Community `.ppmap`
   resumable API. **No second catalog, map owner system or new protocol.**

All steps except public `/health` and approved-origin CORS preflight
require an authorized ticket. Tickets expire in 15 minutes; the website
refreshes through its authenticated Supabase session. Workers do **not**
retain ticket strings or call the map publisher on their own.

## Security and limits

- 128 MiB source upload (below the engine's 512 MiB archive ceiling),
  512 MiB converted output ceiling; single 8 MiB upload chunks.
- Bounded JSON metadata, 2 pending sessions + 2 converted jobs per creator,
  16 total retained jobs and a shared 2 GiB storage reservation budget.
- On completion, the full source SHA-256 is checked, then the original
  `Pk3DiscoveryService` validates ZIP paths, symlinks, duplicate entries,
  entry expansion and level geometry. The portable `.ppmap` is verified
  again by `MapPackageReader`.
- Sessions/jobs are stored under random or owner-hashed private directories.
  Data is removed after 24h; job queue can recover after service restart.
- The HTTP service returns generic errors, never a source absolute path or
  client-supplied filesystem entry. It runs independently of multiplayer.
- Never automatically publish material merely because it converted. The
  creator must inspect any missing textures/unsupported gameplay warnings
  and confirm they have redistribution rights.
- `PK3_ALLOWED_ORIGINS` is an exact HTTPS origin allowlist. The service
  does not allow browser credentials/cookies, cross-origin wildcard or
  arbitrary access via CORS. All state still requires an authenticated ticket.
- Production: run this container with read-only root FS, dedicated UID,
  dropped capabilities, memory/CPU/PID quotas and a private volume. The
  converter is a separate child process with a five-minute execution limit.
  For **strong kernel isolation of the converter itself**, use an additional
  OCI/bubblewrap namespace with no egress, writable staging mount only and
  a seccomp policy. The example container limits the whole service; it does
  not claim independent per-child network namespace isolation.

## Deployment

Build from repository root:

```bash
docker compose -f tools/pk3-upload/compose.example.yml build
docker compose -f tools/pk3-upload/compose.example.yml up -d
```

Example production proxy mapping under the **same map HTTPS host**:
```caddy
handle_path /pk3/* {
  request_body { max_size 9MB }
  reverse_proxy 127.0.0.1:8094
}
```

The real Community map origin must remain the handler for all other routes,
especially `/packages`, `/uploads` and `/v2/maps`. Add
`NEXT_PUBLIC_PK3_CONVERSION_URL=https://maps.projectprime.gg/pk3/`
to Vercel only **after** DNS/TLS, ticket validation and this sidecar are live.
Do not assume that domain is configured just because it is documented.

No conversion command accepts a source pack URL, shell input, external ZIP
dependency, custom exporter binary or editable file path. For larger/complex
maps creators can still use local Project Prime Studio, export `.ppmap`
and use the existing publisher.

## Acceptance

- Node 22 `--test tools/pk3-upload/service.test.mjs` for HTTP/CORS,
  resumability, digest, creator isolation and asynchronous job completion.
- `dotnet build tools/pk3-converter/pk3-converter.csproj -c Release
  -p:MphReadServer=true` plus map-editor Q3 fixture import and packaging.
- Browser test 2 different Hunter accounts, same-source hash, session
  recovery, expired tickets, revoked links and cross-origin attempts.
- Verify archive bombs, symlinks, duplicate entry names, too many levels,
  huge payloads and slow interrupted client requests are safely rejected.
- Verify resulting `.ppmap` installs in the actual game and preserves
  correct spawn, collision, textures and gameplay rules on Windows and Android.
- Check games joined via exact-hash content transfer, historical replay,
  separate map service uptime and conversion saturation.
