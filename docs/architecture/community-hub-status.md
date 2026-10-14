# Community Hub 3.0 program status

Current integration base: [PR #533](https://github.com/AntiNotAnti/Project-Prime/pull/533) merged into main as `1f06d8d9df9996360725c24fe168cc80f4540890`. The frozen package fixture has separate observed Windows and Linux/macOS golden hashes; preserve exact bytes, do not silently normalize.

| Slice | Status | Evidence / next gate |
|---|---|---|
| PR-00: baseline and invariants | **Merged** #533 | Focused map-editor/community acceptance passed on Windows, Linux and macOS, server compile and no-game-assets passed; broader build/release jobs were still running at merge time |
| PR-01: shared Community facades | **Draft / CI pending** #534, now targets main | UI-neutral auth refresh, catalog, install and publish shim; new exact-head build after this status change, check full matrix |
| PR-02: independent Game Community UI | **Draft** #535, stacked on #534 | Independent route, Discover/Installed/Favorites/Downloads and exact package install; requires its own UI regressions |
| PR-03 and later | Not started | PR-02 acceptance |

Do not mark a gate PASS unless the exact SHA and named checks completed. Keep NOT_RUN, FAILURE and PASS distinct. Existing network protocol, immutably published `.ppmap` bytes, v2 routes, history/replay sources and live Ready barrier cannot silently change.