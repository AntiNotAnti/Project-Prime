# RML-13: Native read-only Hunter License overview

A native authenticated overview now displays the **current** Hunter License
service's server-accepted career totals. The native view does not create a new
identity, duplicate Supabase sessions, mint tokens, or read credentials into
its RML data model.

- The existing `HunterLicenseClient.LoadAsync` performs authentication and
  authoritative stats fetch off the render thread, as in the legacy UI.
- A privacy-safe DTO projects only Hunter name, favorite Hunter, rating,
  stats counts, recent-match count and confirmed public/private visibility.
  The email, account UUID, provider IDs, tokens, recovery input and credentials
  are never passed to native markup.
- Null/unavailable public visibility stays **UNKNOWN/UNAVAILABLE**, never
  inferred as public or private.
- The refresh operation is cancellable and generation-fenced. RML-39 supersedes
  the original stale-record retention policy: each refresh clears the previous
  identity and career record until authentication succeeds again. Service
  failures show a generic unavailable status without private response bodies.
- The local native Hunter picker opens this overview through Full Hunter
  License, and Account actions fall back to the current secured account page.
- The default Avalonia launcher remains intact. This page is opt-in desktop.
- No client may submit career scores or mutate server reputation.

The no-game-assets `tools/rmlui-license-check` fixture validates current
owner-thread updates, cancellation, stale replies, account retirement,
and verified-only presentation. [RML-39](rmlui-license-career-rml39.md) adds native
Stats, paginated History, Achievements and Benchmark. It does not claim native full License tabs,
secure-account/verification parity, public opt-out mutation, or Android IME.
