# RML-12: Native Community Discover

Opt-in native RmlUi Community browsing is now backed by the current
CommunityMapOperations / MapCommunityClient v2 paged API, **not** by the
historical pre-rollback CommunityController.

- Eight visible immutable map projects per page, server-provided total,
  filter query, sort, page selection and exact package-hash detail.
- HTTP client setup, proxy choice, TLS request, page decoding and metadata
  projection execute on a worker. UI-thread Tick accepts immutable results
  only from the current cancellation epoch.
- Entry/exit/cancellation never blocks the OpenGL frame, acquires the
  Community transfer-index lock, hashes installed packages, or creates an
  additional map service. Long-lived asset and transfer data are untouched.
- The native Full Community action switches to the existing Community UI,
  retaining authenticated downloads, revision installation, reporting,
  favorites, Creator Studio, and Community Hub 3.0 permissions.
- Native page is opt-in desktop only and does not alter the default launcher.

CI: `tools/rmlui-community-check` simulates delayed HTTP completions,
stale result rejection, search and page bounds, selection, sort and
owner-thread enforcement. Real catalog/TLS and device rendering still
require online and physical-device acceptance.
