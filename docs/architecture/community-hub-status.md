# Community Hub 3.0 program status

Reviewed main: `16b1d89bfb6ed7776b801b3e39d513e740ee5e58` (2026-10-13).

| Slice | Current status | Required next evidence |
|---|---|---|
| PR-00: baseline and invariants | In progress: branch `codex/community-hub-pr00-baseline-20261013` | GitHub CI, golden package hash, baseline JSON receipts |
| PR-01: shared Community facades | Stacked draft on `codex/community-hub-pr01-facades-20261013`; acceptance pending | PR-00 pass, head CI and facade auth/cancellation tests |
| PR-02 and later | Not started | PR-01 acceptance and separate release gates |

Do not mark PASS without an exact-head successful run. Distinguish NOT_RUN, FAILURE, and PASS. PR-00/PR-01 must not change package bytes, wire protocol, persistent storage contracts, HTTP routes or match readiness.
