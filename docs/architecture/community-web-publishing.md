# Website Community API browser access

The Project Prime website uses the existing Community service as the sole
source of truth for published immutable `.ppmap` archives, creator ownership,
version history, browsing, and multiplayer preparation.

## Browser origin allowlist

`MapCommunityServer` now handles `OPTIONS` preflight for exact allowlisted
origins before identity verification. Default allowed origins:

- `https://projectprime.gg`
- `https://www.projectprime.gg`

Set `PROJECT_PRIME_MAP_CORS_ORIGINS` to a comma-separated list of **full
origins**, not wildcard hosts. For example, staging or local development:

```sh
PROJECT_PRIME_MAP_CORS_ORIGINS=https://projectprime.gg,https://staging.projectprime.gg,http://localhost:3000
```

Only HTTPS origins are supported except loopback HTTP for local development.
A configured origin must have no path, query, fragment, or credentials.
If the setting is present it replaces, rather than appends to, the defaults.
Do not add arbitrary Vercel preview hosts or untrusted domains.

Allowed preflight methods: GET, POST, PUT, DELETE, OPTIONS.
Allowed request headers: Authorization, Content-Type, Accept, Range,
If-None-Match. Exposed response headers: ETag, X-Total-Count, Accept-Ranges,
Content-Range, Retry-After, Content-Disposition. No `Access-Control-Allow-Credentials` is emitted
because the website sends explicit short-lived Bearer tickets, not cookies.
Responses include `Vary: Origin`; rejected origins are never reflected.

CORS is **not** authorization. Existing creator ticket validation, quota
reservation, package validation, publication locks, and revision conflicts
remain in force. The game client and server-to-server callers without an
`Origin` header behave exactly as before.

## Deployment

- Update the map service executable using the normal isolated map service
  deploy procedure. Restarting the website alone does not enable preflight
  on an old origin service.
- Verify `OPTIONS /uploads/{sha}` from the approved website returns 204 with
  allow-origin and the expected methods/headers.
- Verify unapproved origins do not get allow-origin, while native clients
  without Origin continue to browse, publish, and download as before.
- Browser chunks upload directly to the map service. Do not proxy large
  map packages through Vercel Functions.
- HTTPS reverse proxies must forward `OPTIONS` and `Origin`; avoid replacing
  the allowlist with a permissive `*` at the reverse proxy.
- The website's **server-only** public catalog proxy should use the same map
  service host as the browser uploader. The old `maps.rebooty.xyz` address is
  still a game default until an explicitly configured domain is deployed.

## CI

`tools/community-service-check` exercises real `HttpListener` preflights,
denied origins, and public catalog origin variation, alongside its existing
upload, quota, ETag, and service admission checks.
