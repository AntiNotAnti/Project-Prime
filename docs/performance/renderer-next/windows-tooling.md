# Windows qualification tooling — 2026-10-22

Scope: resumed worktree at baseline `46299080b`. All executed checks run on
Windows. No shared client build or native GPU campaign was run by this lane.

The four frozen-value display-pose checks now use explicit
`PROJECT_PRIME_POSE_REFERENCE_DLL`, then `DOTNET_EXE` or native PATH discovery
(`dotnet`/`dotnet.exe`). They no longer assume `~/.dotnet/dotnet` or silently
select historical artifact directories. Missing explicit reference means
NOT_RUN; an explicitly supplied missing DLL/executable fails. Existing source
product predicates, pinned simulation hashes and warning checks remain intact.

`qualify_windows_renderer.ps1` preserves the predeclared numeric gates: mean
within 2% of the cap, p99 no greater than twice the period, and zero simulation
drops/stalls. It additionally checks expected room, roster size, cap, scale,
shadow mode, framebuffer, software clock/VSync, focus/window/visibility state,
observed DLL hashes and raw-capture identity. The initial hidden-to-visible
reveal is allowed; extra visibility transitions fail. Optional periodic/GPU
profiling modes are rejected. Display and Unlimited have no invented scanout
budget. Required metrics must be finite and positive; missing/null/NaN/Infinity
fail. Matrix/Soak measurement must cover requested seconds minus four warmup
seconds, allowing only 100 ms at the boundary. Native chronological intervals
must sum to the measured window; raw Present intervals must cover it and stay
within total wall duration. These are different interval endpoints, so the fixed
100 ms tolerance is explicit. Summary records requested measured seconds, native
and raw interval seconds, and raw coverage fraction. Scale remains capture-only
and does not receive a normal wall-duration acceptance claim.

The runner resolves executables before native work, uses a fresh directory,
records preflight/settings, detects frozen DLL mutation, retains malformed and
failed receipts, continues subsequent cases, and exits 1 if any gate fails.
Functional completion is reported separately from cadence acceptance. Unlimited
raw capacity is 131,072 for the 24-second recipe, exceeding the native harness's
20×4096 measured-interval envelope; the 240-cap soak reserves 524,288 rows.
The runner restores its three environment overrides in `finally`.

The manifest writer retains schema 1 and existing fields while adding full
tracked/nonignored-untracked inventories, missing-file markers, explicit
build-binding metadata and exclusive output creation. `--snapshot-source`
computes its inventory before opening the output and explicitly excludes that
evidence path, fixing review P2 self-inclusion. Before/after snapshots can be
stored outside the worktree:

```powershell
python tools/native-render-check/write_renderer_manifest.py --snapshot-source --output C:\fresh\before.json
```

Native manifests accept `--source-snapshot`, `--build-binding`, `--frozen-build`
and optionally `--end-source-snapshot`. Binding JSON must supply
`sourceSnapshotSha256`, `clientSha256` and `harnessSha256` matching the explicit
snapshot and observed binaries. Explicit `buildInputMatch=false` is rejected.
With an end snapshot, `endSnapshotSha256` must match and src/tools/native/root
build-input rows must be unchanged; other tracked-file changes remain visible
in the retained inventories. Digest agreement does not manufacture compiler
attestation. Missing build metadata stays UNVERIFIED.

Actual focused validation:

```powershell
python -m unittest discover -s tools/native-render-check -p test_qualification_evidence.py -v
```

**PASS, 8 tests**, latest retained in `windows-tooling-duration.log`: executable/reference
resolution, full inventory and deletions, fresh in-worktree CLI output without
self-inclusion, explicit build bindings and mutation, before/after source checks,
and Windows fake-process campaign checks. The fake campaign exercises success,
nonzero native exit, corrupt JSON, raw-record overflow, focus failure, fresh
output refusal, failed build metadata, binary mutation, capacity and aggregate
failure exit. Duration/metric negative cases reject a 20-second receipt for the
1800-second soak, missing/null/NaN p99, infinite mean, short raw coverage and
native interval/window disagreement. It runs no renderer and proves no native
performance result.

The explicit frozen client at
`C:\Users\Jarrett\AppData\Local\ProjectPrime\renderer-qualification-20261022\native-final\ProjectPrime.dll`
has SHA256 `f22d19de70681de8e7a1dfa48ec514e96969904a83a3e2cf4c99ba8c19d12e74`
and MVID `5d14b5fe-80be-41a1-838a-b3c59a02d46f`. Initial Windows execution exposed
two stale C# fixture compile failures, retained in `windows-pose-boundary-current.log`
and `windows-pose-owner-current.log`. These were fixture failures, not evidence
of a product failure. Narrow source-linked scaffold repairs account for current
local/crouch isolation, replica draw visibility, online third-person ownership
and the alternate display muzzle. Existing canonical transform, simulation hash,
schema and cleanup checks remain; new positive/negative cases cover the changed
conditions. Both files now pass all six tests, including all four formerly
skipped compiled cases (`windows-pose-boundary-repaired.log`,
`windows-pose-owner-repaired.log`). The fixtures build only isolated temporary
projects and never build the shared application.

The full Windows native Python suite passes **133 tests, one skipped** in 30.036s
(`windows-native-python-final.log`). The remaining skip is Windows symlink
privilege, not a frozen-value test. This is offline regression evidence; native
FPS and sustained acceptance remain the parent's separate campaign. No historical
removed evidence has been restored.

## Final integrated tooling result

Against the final v4 client (same complete bundle as v5), the Windows native Python suite passes134 tests with one symlink-privilege skip: [log](windows-native-python-v4.log). This includes the new actual-source readback-region regression. The later executable-resolution repair is separately covered by the focused eight-test suite: [log](windows-tooling-python-resolution.log). A duplicate Python basename on PATH now selects one executable instead of joining several matches. The earlier real Scale preflight failure remains in `windows-scale-v4.log`; it started no native scale run. Final matrix/scale/soak gates and identities are in [status](status.md) and [final receipt](final-receipt.json).
