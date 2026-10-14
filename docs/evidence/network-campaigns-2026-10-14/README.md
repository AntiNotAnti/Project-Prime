# Completed network campaign evidence

Each subdirectory preserves one completed attempt and its exact source/client identity. An INCOMPLETE or FAIL attempt remains such when later attempts are added. Running artifacts are not published as final results. Full NET acceptance remains NOT_ACCEPTED; production Enforce is unavailable.

| Attempt | Exact source | Recorded outcome |
| --- | --- | --- |
| [smoke2](smoke2-7588-attempt1/README.md) | `7588d98689899c9868e8a0105b78653c7a1db50d` | Transport/AI/drain PASS; strict joins INCOMPLETE; seven simulation dropped steps and one stall retained |

Portable verifier: `python3 tools/network-evidence/verify.py --evidence <completed-attempt-directory>`. Original-file verification additionally takes `--original-workspace-root "$TASK_WORKSPACE"`. Verifier falsifiers run with `python3 -m unittest discover -s tools/network-evidence -p 'test_*.py' -v`; all three pass, including refusal of a falsely rewritten PASS even when derivative hashes are updated.

See [current758 native acceptance](../native-acceptance-2026-10-14/README.md) for its separate engine/rendered fixture scope. Neither native correctness nor this bounded transport smoke establishes a joined live authority/payment claim or a performance rollout.
