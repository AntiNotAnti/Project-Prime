# Native Community revision controller checks

`dotnet run --project tools/rmlui-community-revisions-check -c Release`

This links current portable controllers and policy and uses a synthetic authority. Its 183 checks exercise immutable revision identity, eight-row paging, historical public-package installation selection, owned promotion/visibility/deletion/restoration confirmation, account leases, stale-route cancellation, malformed responses, owner-thread fencing and generic JSON redaction. It also covers unknown-write read-only recovery, permission revocation and immediate private completion retirement. It performs no HTTP requests, backend writes, file installs or credential operations.

The production adapter additionally pins the existing authenticated session under its auth gate, re-reads owned project/current/latest/history before each action, and validates fresh readback. Existing service endpoints do not provide an atomic expected-revision compare-and-swap operation. Composed native lifecycle input/layout and real service integration require separate acceptance.
