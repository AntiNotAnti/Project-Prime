# Interrupted long2 predecessor: completion UNKNOWN

The original wrapper receipt remains **RUNNING**. A separate recovery receipt records **INCOMPLETE_EXTERNALLY_INTERRUPTED**, with no surviving owned group members and interruption cause **UNKNOWN**. There is no final process exit receipt, no completed final role reports, and no reached final drain. This attempt is **not** a completed20-minute campaign and establishes no strict join, final pacing/transport or replay-consumer success.

The last observed authority frame was45840 (approximately764 simulation seconds). That partial observation does not establish actual campaign wall duration or a completed producer history. Recovery reports no observed fatal log match; absence of such a match does not identify the interruption cause. Source/client metadata in the RUNNING receipt is the originally planned/qualified source758 identity, not a fabricated source/runtime-after qualification.

All13 selected originals are immutable projections: original RUNNING receipt, separate recovery receipt, complete monitor-checkpoint JSONL, partial file inventory, original setup manifest, untruncated small runner/peer logs and an explicitly selected large-server-log projection, rotation and the available partial drain snapshots. Monitor/recovery/original-receipt SHA256/bytes are retained independently. The inventory records actual hashes/byte counts for **three incomplete `.ppdemo.part` files**; no recording payload is copied and no recording is asserted complete or replayable. Recovery's earlier inventory listed two peer files; that original list is preserved unchanged alongside the later complete three-file inventory.

Small logs use exact complete escaped text. The large server log has a declared first20/last30/all diagnostic-matching line projection with exact zero-based original indices, total original line count, original whole-file identity and kept-text digest/bytes. Every selected line is complete; the omitted middle is explicit, never silently truncated. All original/derivative SHA256/bytes, chunk contiguous UTF-8 ranges and recomposed projection digests are in [manifest.json](manifest.json). Private workspace/home prefixes are sanitized; no Paths.txt, profiles, binaries/assets/media are included. This is finalized recovery provenance for an interrupted attempt, not publication of a running campaign as accepted.

```sh
python3 tools/network-evidence/verify.py --evidence docs/evidence/network-campaigns-2026-10-14/long2-7588-attempt1 --original-workspace-root "$TASK_WORKSPACE"
```

Full NET/program remains NOT_ACCEPTED. The independently completed [retry](../long2-7588-attempt2/README.md) preserves its own source/timestamps and does not replace this interruption.
