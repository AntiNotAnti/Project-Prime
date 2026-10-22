# Qualified NET04 campaign recipes

**PREPARED, NOT_RUN.** Root must explicitly dispatch each case after the new C# and asset qualification finishes and the serial heavy slot is released. These scripts prepare or execute one bounded case; they do not silently launch all six or reuse an old frozen identity. Only reusable tools/docs and lightweight tests changed during preparation; no build, native or campaign execution occurred.

All cases use genuine native tactical AI, authored MP12 SIC TRANSIT powerups and the existing independent-process runner. No grant/contact/source/payment seed, HitRig, forced weapon, authority bot or soak rule is added. The eight-client roster contains all seven Hunters (with Samus repeated), as assigned by the source-qualified runner.

| Case | Clients | Gameplay | Drain budget | Default port |
| --- | ---: | ---: | ---: | ---: |
| `smoke2` | 2 | 90 s | 35 s | 29250 |
| `long2` | 2 | 1200 s | 35 s | 29252 |
| `long8` | 8 | 1200 s | 35 s | 29254 |
| `smoke-lan2` | 2 | 90 s | 35 s | 29256 |
| `smoke-moderate4` | 4 | 90 s | 35 s | 29258 |
| `smoke-worst8` | 8 | 90 s | 35 s | 29260 |

Common parameters: tactical AI level 2, AI seed 1729 plus launch index and transport seed 8128 plus launch index. Poor `smoke2`/`long2`/`long8` use RTT 250 ms, jitter 40 ms, loss 5%, reorder 5%, duplication 2%. LAN uses 0/0/0/0/0; moderate four clients use 100/20/1/3/1; worst eight clients use 400/80/5/5/2, in the same RTT/jitter/loss/reorder/duplication order. Timed proof Shadow and legacy shot-proof Shadow remain independent. Authority archival timed facts and client recordings are requested. Explicit evidence row limit 1048576 expands diagnostic budgets without enabling soak. The join recorder still has its hard shared 256 MiB value-page cap; any observed drop or legacy overflow remains incomplete evidence.

Use a newly qualified clean source checkout and matching physical runtime. The required build receipt has exact source-before/source-after and physical hashes-before/hashes-after, plus loaded client SHA/MVID/protocol; the separate qualification receipt must also bind the same clean source/tree and be PASS. Root verifies native/benchmark prerequisites before dispatch. The caller must supply an explicit physical `--dotnet` executable (the qualified workspace SDK). The wrapper rejects a symlink, queries SDK version in the source checkout, records its streaming hash/version and validates unchanged identity afterward. No SDK location is inferred from the wrapper directory. Protocol is read from the unique source declaration and must match both qualified metadata and build constants; it is never guessed. Input asset paths are references only; the wrapper does not copy ROM assets or licensed game files. Campaign profiles/export directories and staging are owned by the existing runner.

From the workspace root, set the task-specific variables to the final identities supplied by root, then prepare:

```sh
python3 tools/nettest/run_plan_campaign.py \
  --source-worktree "$NET_PLAN_QUALIFIED_SOURCE_WORKTREE" \
  --source "$NET_PLAN_QUALIFIED_SOURCE_SHA" \
  --build "$NET_PLAN_QUALIFIED_RUNTIME" \
  --build-receipt "$NET_PLAN_BUILD_RECEIPT" \
  --qualification "$NET_PLAN_QUALIFICATION_RECEIPT" \
  --paths "$NET_PLAN_LICENSED_PATHS_FILE" \
  --dotnet "$NET_PLAN_QUALIFIED_DOTNET" \
  --case smoke2 \
  --out work/net-plan-campaigns/smoke2-final-preflight1
```

Preparation launches no game or UDP process; it performs the read-only SDK `--version` query. Use a **different new output directory** and append `--execute` only after root's explicit dispatch. Use the other five explicit case names only when subsequently authorized, again with new output directories. This runner supports 2/3/4/8 clients; it does not support solo UDP. Controlled 400 ms pump-stall and rate/capacity exhaustion remain NOT_RUN pending an authentic application fixture, and are stated explicitly in each receipt. The wrapper does not synthesize those events. An optional explicit `--port` is validated and probed for availability before actual execution. A failed introduced runner/runtime smoke must be diagnosed before the longer cases; natural absence of positive grants or incomplete source proof must stay labeled INCOMPLETE.

The wrapper clears inherited game environment flags, then the runner sets and records only its declared child flags. Receipts capture exact SDK/source/tree/runtime/loader identities, command/environment manifests, PID/group observations, process exits, transport/AI/drain outputs, clocks and source report identities, strict audit logs and replay file inventories before/after. A wrapper-created process session owns only its runner and descendants; timeout, interruption, residual processes or disk guard may signal only that exact owned group. No broad process-name kill, output rewrite or automatic campaign retry exists. Untruncated logs and finalized/partial recordings remain in the immutable case directory.

The disk target is approximately 6 GiB free; actual starting and minimum observed free bytes are recorded. Free disk below 1.5 GiB stops only the owned process group and preserves partial evidence. Each campaign wall-clock deadline is gameplay duration + 35 s drain + 240 s bounded startup/shutdown allowance. The separate strict auditor is limited to 240 s and shares the disk guard. Source/runtime changes are a failed identity gate.

After the campaign, `net04_join_evidence.py` audits surviving reports, including failed or partial attempts when possible. Result precedence preserves **FAIL**, then **INCOMPLETE**, then `PASS_BOUNDED_OBSERVED_JOINS`. A generic transport PASS with missing/nonpositive funded-source or full component/payment joins is INCOMPLETE; retained source/identity/payment contradictions are FAIL. The source-verified fraction is descriptive. This wrapper never concludes 99.9% canary readiness from zero, a small sample or incomplete rows, and never enables Enforce.

Replay inventory reads only the six-byte header and streams file hashes; no entire `.ppdemo` or checkpoint is decompressed into memory. Header format/protocol is an observation, not semantic acceptance. The actual production replay consumer and native/Studio playback/export gates remain separate. Historical exit 0 setup failures and genuine semantic exit 1 failures remain separate evidence and are not relabeled by a new campaign.

Lightweight checks completed:

```sh
python3 -m unittest discover -s tools/nettest -p test_run_plan_campaign.py -v
python3 tools/nettest/run_plan_campaign.py --help
```

Eight recipe/guard/disposition/inventory/SDK/protocol portability tests PASS. They launch no game process and contribute no native pickup, shot, claim, payment or campaign denominator. The portable code and this recipe contain no licensed assets, credentials or fixed host paths.
