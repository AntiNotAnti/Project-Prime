# Focused NET03 and NET04 benchmarks

These small C# programs measure actual internal classifier and archival APIs through the production `nettest` friend name. They compile a fresh benchmark entry against an explicitly qualified physical `ProjectPrime.dll`; there is no ProjectReference and no client source rebuild in this tool. A passing full source qualification receipt binds the measured client's full commit, SHA256 and MVID. The benchmark entry and full native fixture identities remain separate.

NET03 measures six pure `Classify`/`NativeValue` DTO cases: funded, NotGranted and unknown ordinary sources; Child; Homing; and ordinary native DTO creation. NET04 measures funded/unfunded fact216 codecs; full2048 duplicate/read/miss/capacity; dense authority advance; fresh/cached Begin; and state Write/CaptureCheckpoint with1/2048 facts. Constructed values establish no native admission, production CurrentWorld, emission, impact coverage, payment, gameplay or FPS result.

Prerequisites: Python3, Git, .NET10 SDK, the separately qualified actual client/full nettest runtime, and a compatible dependency directory containing OpenTK.Mathematics plus the client's shared libraries. No SDK, DLL, game assets or demos are included here. Run serially while other native/benchmark campaigns are idle.

```text
python3 tools/net-evidence-benchmark/run.py \
  --kind net04 \
  --repo <clean-source-checkout> \
  --head <full-source-commit> \
  --qualified-revision <same-full-source-commit> \
  --qualification-receipt <passing-full-source-qualification-receipt.json> \
  --runtime <qualified-runtime-directory> \
  --donor <shared-dependency-directory> \
  --sdk <dotnet-executable> \
  --client-sha <qualified-ProjectPrime-SHA256> \
  --client-mvid <qualified-ProjectPrime-MVID> \
  --out <fresh-output-directory-outside-source-checkout> \
  --execution-slot-released
```

For NET03, use `--kind net03` and add `--classifier-revision <qualified-full-commit>`. Its current checkout may include later unrelated merges, but the classifier file must hash identically to the measured revision before and after. NET04 requires the checkout HEAD to equal the qualified revision. Neither path is an implicit qualification of a newer complete checkout.

Each case has eight warmup batches and30 measured batches. Preserve every raw row and outlier. CPU includes all process threads; allocated bytes count only the caller thread. DTO/fixture/delegate setup is excluded except the explicit fresh Begin case. NET04's dense scan rebuilds2048 retained cells outside each batch, then advances512 consecutive frames before expiry. Checkpoint captures allocate normally on each call.

Median/p95/p99/p99.9 describe batch totals. Dividing a batch total by operations yields a batch average; it does not establish per-call/frame tails. NET03 includes no-inline wrapper, delegate and consumption overhead. The programs check expected DTO/codec behavior against the actual assembly before timing.

The runner creates a new directory and refuses overwrite, requires clean stable HEAD/tree/status, checks physical client and full fixture digests before/after, disables compiler servers, and closes only process groups it created. Passing runtime receipts include startup client SHA/MVID validation. Logs are sanitized derivatives with separate original and derivative hashes; CLI input paths become placeholders. Preserve failed output directories separately. Generated source projects and runtime binaries are execution artifacts and should not be committed. The runner currently targets POSIX process-group APIs on macOS/Linux.

Validate the committed historical evidence without starting a benchmark or loading assets:

```text
python3 tools/net-evidence-benchmark/verify_evidence.py \
  --evidence tools/net-evidence-benchmark/fixtures/primitive-overhead-7588d986
```

This checks every derivative digest, all measured rows against the retained RAW lines, batch indices/counts, source/binary closure guards, entry/fixture identity separation, and excluded private paths/binary types.
