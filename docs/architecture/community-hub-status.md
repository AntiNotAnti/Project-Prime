# Community Hub 3.0 program status

Current integrated `main`: `4e428a0547d74cd0485638c8dbe1f2486602cdba` (following PR #536, October 13, 2026 US Central / October 14 UTC).

| Slice | Repository state | Evidence at merge / remaining gates |
|---|---|---|
| PR-00 #533: baseline and invariants | **Merged** `1f06d8d9` | Linux/Windows/macOS Community and map-editor focused checks passed. Separate Windows and Linux/macOS archive SHA-256 goldens are pinned; cross-platform archive bytes differ and remain an explicit unresolved optimization concern. |
| PR-01 #534: UI-neutral services | **Merged** `5f3c80a9` | Shared authenticated Community facade, compatible v2/legacy routes and tests; prior broad CI included a Windows golden mismatch since corrected in PR-00. |
| PR-02 #535: independent Game Community tab | **Merged** `acfe5c25` | Discover, offline Installed, Favorites, active Transfers and map details. 26 CI jobs had passed, no failures, with three macOS jobs pending at merge; hardware UI acceptance remains separate. |
| PR-03 #536: durable transfers | **Merged** `4e428a05` | Immutable upload snapshots, resumable exact download receipts and pause/resume/cancel. Focused Linux/Windows Community/transfer tests and historic map regressions passed. 22 broad CI jobs had passed, no failures, seven macOS/Android/release jobs pending at merge. |
| PR-04 #537: verified immutable archive CAS | **Draft / in progress** | Cross-process archive read leases and download gate, quota-LRU, corrupted-object quarantine, strict cached identity, and completed receipt revival on cache miss. Latest exact-head CI and real multiprocess/Android acceptance still required. |
| PR-05 onward | **Not started** | Installer I/O reduction, streamed packaging, service load isolation, generic v3 content and CDN remain separate acceptance slices. |

## Hard invariants

1. Do not mark a check as PASS without its exact-head result; distinguish PASS, FAILURE, PENDING and NOT_RUN.
2. Never mutate a historical `.ppmap` archive or silently swap its package hash. Current multiplayer Ready and historical replay references use exact content identities.
3. Keep legacy/v2 Community routes and authenticated visibility. PR-03/04 do not create generic v3 content uploads.
4. PR-04 cross-process archive locks do **not** make the existing `jobs-v1.json` receipt journal safe for concurrent multiwriter Game/Studio access. A future Studio integration must use separate journal roots or implement explicit transactional synchronization.
5. Do not claim measured cache-hit speedups from loopback fixtures alone. Run real size-tier, disk pressure and join/Ready benchmarks before a performance claim.

## Next gated delivery

Complete PR-04 test matrix, inspect actual CI failures, verify offline/online exact archive recovery under LRU with an active lease and test a real second process. Only then consider merging PR-04 and starting PR-05.
