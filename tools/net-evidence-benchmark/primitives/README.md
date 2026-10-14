# Constructed primitive overhead benchmark

The separate friend assembly measures six production value/storage primitives against an explicitly qualified physical `ProjectPrime.dll`. It does not rebuild the engine. The ordinary full `nettest.dll` is identified but never executed by this entry. Source qualification must cover the exact clean full checkout commit supplied to the runner; qualify again after integrating this source-only harness.

| Case | Measured unit | Untimed preparation |
|---|---|---|
| Diagnostics disabled | Bundle of six public diagnostic hooks | Engine/static/DTO initialization; evidence environment disabled |
| Claim scope | Enter, exact request-key read, dispose | Constructed immutable full key; nested restoration falsifiers |
| Witness scope | Enter, valid capture, dispose | Constructed value, invalid-value/restoration falsifiers |
| In-page storage | Claim + payment pair, ordinal increments | 32 recorders, first pages and first rows |
| New-page storage | Claim + payment pair at a page boundary | 32 recorders, first 1024 rows per channel |
| Full budget refusal | One row-limit refusal + one shared byte-limit refusal | Actual recorder filled once to its 256 MiB payload cap; one-row recorder filled |

These constructed fixtures establish primitive costs only. In-page/new-page storage uses the opt-in production recorder directly, without starting an authority session or pretending to exercise active Shadow gameplay. The value-only witness scope establishes no native collision, emission, payment, geometry admission, replay, gameplay, frame-step or FPS coverage. The proof mode stays Shadow; the campaign recorder stays absent during disabled hooks. This benchmark does not substitute for an actual native Off/Shadow parity tour.

Each case runs eight full warmup batches and retains all 30 measured raw batches, including outliers. Each raw batch has wall milliseconds, process CPU milliseconds, caller-thread allocation bytes, operations, and separate per-batch setup wall/allocation counters. One-time capacity-fill setup has its own counters. Process CPU covers all process threads; caller allocation bytes do not. Array/list/object overhead counts in caller allocations but not in the production payload-byte budget. No forced collection or sample filtering occurs; prior setup or validation GC can affect subsequent timings.

The disabled/scopes/refusal cases use 262144 units per batch. In-page uses 32 × 1023 pairs; boundary uses 32 pairs. Units differ, so compare only equivalent cases and source revisions. No-inline wrappers, delegates, loops and consumption costs are included. Median/p95/p99/p99.9 are interpolated over batch totals, never per-operation or frame tails. Do not turn 30 batches into a per-frame p99.9 claim.

Assertions check exact full request keys, wrong slot/id rejection, nested restoration, invalid witness rejection, counter progression, old rows and exact ordinals, actual shared payload-cap refusal, no overwrite, absent disabled recorder, and expected zero warmed caller allocations versus positive boundary allocations. Assertions, reflection, enumeration and JSON reporting stay outside timed intervals. Startup-only reflection reads the recorder field; measured loops use direct production calls.

Run only after the shared execution slot is explicitly released:

```sh
python3 tools/net-evidence-benchmark/run_primitives.py \
  --repo "$SOURCE" --head "$FULL_QUALIFIED_COMMIT" \
  --runtime "$QUALIFIED_RUNTIME" --donor "$DEPENDENCY_DONOR" \
  --sdk "$DOTNET_SDK_EXECUTABLE" \
  --qualification-receipt "$PASSING_QUALIFICATION_JSON" \
  --build-receipt "$BUILD_PROVENANCE_JSON" \
  --client-sha "$QUALIFIED_ENGINE_SHA256" --client-mvid "$QUALIFIED_ENGINE_MVID" \
  --fixture-sha "$QUALIFIED_FULL_NETTEST_SHA256" --fixture-mvid "$QUALIFIED_FULL_NETTEST_MVID" \
  --out "$FRESH_EXTERNAL_EVIDENCE_DIRECTORY" --execution-slot-released
```

The qualification JSON must contain a PASS status, identical clean `sourceBefore`/`sourceAfter` snapshots with the full commit, and `actualLoadedClient` SHA/MVID matching the supplied engine. Existing qualification field aliases are supported. The separate build provenance JSON is hashed and disclosed as a supplied identity document; the runner does not claim to re-execute or audit that build. The qualification receipt provides the source/engine linkage. Full-fixture SHA/MVID must independently come from that qualification's build evidence.

The runtime must contain the qualified engine and full fixture; the dependency donor supplies shared DLLs. The SDK executable and all deployment paths come from CLI inputs. Fresh output must be outside the source checkout. The entry project uses DLL references, not `ProjectReference`. Compiler servers and node reuse are disabled. Only the launcher's newly owned process groups may be closed; every group must exit normally and close for PASS. Failures retain their fresh receipt and logs rather than overwriting earlier evidence.

Receipts identify source HEAD/tree, actual engine, full fixture, thin entry, shared dependency hashes/MVIDs, build/qualification document hashes, benchmark source digest, raw data and setup costs. Source and physical inputs are checked before/after. Logs become explicitly named sanitized text derivatives with separate original and derivative hashes. Generated deployment projects and runtime binaries are local execution products, not portable repository evidence; commit only the text source and appropriately scoped measured derivatives.

Light checks, with no build/native/performance execution:

```sh
python3 -m unittest discover -s tools/net-evidence-benchmark -p test_run_primitives.py
python3 tools/net-evidence-benchmark/run_primitives.py --help
```
