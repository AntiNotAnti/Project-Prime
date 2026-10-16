# Community Hub CH-05A | Bounded browsing and debounced search

## Changes
- Game Community Discover/Favorites now request 24 projects per page from the existing `GET /v2/maps` endpoint. The server already implements filtering, sorting, page/pageSize and `X-Total-Count`; no new route or database was introduced.
- Player browser gains compact Previous/Next and name/updated sort controls with controller navigation identities. Search is debounced by 300 ms to avoid requests on every keystroke and resets to page 1 after editing.
- Installed and Downloads remain completely offline/local. Existing fixture mode stays free from live Community requests.
- Older servers without v2 are handled by the existing legacy map-grouping fallback; intermediate v2 implementations that ignore pagination are bounded in the client.
- No `.ppmap`, replay, Ready, upload or creator flow changes.

## Acceptance
- `tools/community-service-check` checks paged facade search with authoritative totals and empty results.
- Run the full shell UI/controller/touch suite and inspect 830x390 as well as standard desktop layouts.
- Benchmark 100/500/2,000 map catalogs, search latency, memory allocation and cancellation under slow network.

### Deferred CH-05 work
Lazy thumbnail cards, mode/player-count filters, trending/featured metadata, richer progress displays and visual regression acceptance remain separate. They must not be inferred from this pagination slice.
