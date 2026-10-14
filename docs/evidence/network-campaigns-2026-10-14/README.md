# Completed network campaign evidence

Each subdirectory preserves a closed workload or an explicitly interrupted/failed attempt and its exact source/client identity. An INCOMPLETE or FAIL attempt remains such when later attempts are added. The interrupted predecessor's original RUNNING receipt is retained as partial evidence; it is never published as a final successful result. Full NET acceptance remains NOT_ACCEPTED; production Enforce is unavailable.

| Attempt | Exact source | Recorded outcome |
| --- | --- | --- |
| [smoke2](smoke2-7588-attempt1/README.md) | `7588d98689899c9868e8a0105b78653c7a1db50d` | Transport/AI/drain PASS; strict joins INCOMPLETE; seven simulation dropped steps and one stall retained |
| [long2 interrupted](long2-7588-attempt1/README.md) | `7588d98689899c9868e8a0105b78653c7a1db50d` | Original RUNNING receipt; UNKNOWN interruption; no final drain/report/20-minute or replay success |
| [long2 retry](long2-7588-attempt2/README.md) | `7588d98689899c9868e8a0105b78653c7a1db50d` | Transport/AI/drain PASS; strict joins INCOMPLETE; two funded DD sources unverified; nine simulation drops and one stall retained |

Portable verifier: `python3 tools/network-evidence/verify.py --evidence <completed-attempt-directory>`. Original-file verification additionally takes `--original-workspace-root "$TASK_WORKSPACE"`. Verifier falsifiers run with `python3 -m unittest discover -s tools/network-evidence -p 'test_*.py' -v`; all twelve pass, including refusal of forged original indices/reordered metric columns, refusal to package running/missing-original attempts, exact deterministic Unicode recomposition, missing/altered/reordered chunk refusal and refusal of a falsely rewritten PASS even when derivative hashes are updated.

See [current758 native acceptance](../native-acceptance-2026-10-14/README.md) for its separate engine/rendered fixture scope. Native correctness and transport activity alone establish neither missing authority/payment joins nor a performance rollout. The separately corrected audits report their actual bounded joins.

The reusable `tools/network-evidence/pack.py` requires finalized process closure or a separate recovered interrupted-attempt receipt, refuses existing immutable output directories and missing declared inputs before writing, preserves complete selected originals, and automatically uses bounded lossless chunks. New source/case metrics require explicit review; packing is not acceptance.

Completed remaining cases: [long8 failed wrapper/recovery](long8-7588-attempt1/README.md), [LAN2](smoke-lan2-7588-attempt1/README.md), [moderate4](smoke-moderate4-7588-attempt1/README.md), [worst8 original FAIL](smoke-worst8-7588-attempt1/README.md), and [separate corrected read-only audits](corrected-audits-a74d1575/README.md). Corrected outcomes are one bounded PASS and five INCOMPLETE; producer 7588 and auditor a74 identities remain distinct. No original interruption/guard/overflow/strict failure is overwritten. Full NET remains NOT_ACCEPTED.
