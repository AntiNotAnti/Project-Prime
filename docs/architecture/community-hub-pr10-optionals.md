# Community Hub PR-10 | Optional Visual Assets and Exact Dependency Lockfiles

- Defines explicit dependency roles `optional-visual` and `editor-only`. `required-gameplay` dependencies are rejected: no network protocol, collision, map Ready or historical replay change.
- Publishing enforces already-published dependency identities, a bounded acyclic dependency graph and exact immutable SHA-256 edges.
- Adds deterministic canonical optional-resource lockfiles keyed to an **exact map package SHA-256**, with separate quality tiers (Standard/Hd/Ultra), a missing-optional result, bounded depth/count/bytes, and no mutable latest-version resolution.
- Adds independent content-addressed visual blob cache. Two maps requesting the same SHA-256 reuse one archive, and corruption triggers fresh integrity-checked download rather than silent use.
- This remains authoring/optional display metadata. The engine's core gameplay assets remain self-contained in `.ppmap`; maps do not require HD content to enter multiplayer. Real shader/texture replacement must be separately staged and verified to avoid rendering regressions.
- `tools/community-dependency-check` tests exact role enforcement, deterministic lockfile, cache dedup, corruption repair, absent-pack nonblocking semantics.

**Limits:** Full live shader/LOD application and Android GPU-budget tuning are deferred, because the PR cannot legitimately claim renderer integration without hardware tests. Cache admission is per-process; object hashes enforce byte identity. CI only on final PR-14 per user instruction. Rollback removes optional metadata/cache without touching installed maps or replays.