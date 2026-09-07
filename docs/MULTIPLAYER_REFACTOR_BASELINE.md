# Multiplayer-only refactor: R0 / R1

This batch follows the supplied **Multiplayer-Only Major Refactor — Full
Implementation Plan**. Its first-pass contract is R0 characterization followed
by R1 toolchain migration only. Adventure/offline removal, match ownership,
lifecycle changes, campaign deletion and project splitting belong to R2–R12.
The current `MphRead` folders, namespaces and conditional server personality
remain transitional by design; this batch introduces no destination projects.

## R0: characterized source

Starting revision: `7e321b1`. Runtime/compiler: .NET SDK 9.0.317 on macOS ARM64.
The initial full suite passed 286 C# tests, and all 29 Python tooling tests passed.
The dedicated server/conformance harness built with zero warnings or errors.
The authoritative family remains 2, live protocol 6 and simulation rate 60 Hz.
After characterization, the complete .NET 9 suite passed 323 tests (37 added,
zero skipped). All 16 real-gameplay WAN cases passed: 2/4/8 clients at LAN and
50/100/150/250 ms RTT with jitter/loss, plus four asymmetric clients. The
data-enabled dedicated-server checks and the real rotation/late-join fixture
also passed.

`MatchBaselineTests` freezes the original enum values/team classification,
mode-specific ranking and tiebreaks, team aggregation, standings, score/time
end conditions, survival/Prime Hunter flow, clock expiry and rotation progress
reset. It intentionally characterizes existing quirks rather than changing
rules: BountyTeams currently compares teams as tied, and survival team-time
aggregation ignores the negative surviving-player sentinel.

`--match-baseline` supplements those isolated state tests with real entity
behavior using extracted multiplayer content: lethal damage in all 12 modes,
flag capture, node capture/scoring, contested Defender occupancy and the first
Prime Hunter kill. Flag capture invokes the real capture method after pickup;
it does not simulate navigation through a base. These are headless, controlled fixtures. The existing
`--match-lifecycle` fixture separately exercises real server-process rotation,
loading, late joins and score reset through impaired UDP connections. Existing
snapshot, world and reliable combat serialization tests remain part of the suite.

The asset guard now uses streaming shell reads supported by macOS Bash 3.2 and
CI Bash. The map guard drains ZIP listings to avoid a `grep -q` / `pipefail`
SIGPIPE false rejection. Five regression tests cover large listings, missing
levels/textures, paths containing spaces and unlisted images; all 34 Python
tests pass. The ten existing, original MIT-licensed Parallax source textures are
individually allowlisted using their authoring README as provenance. No broad
image exemption or game-data exception was added. Both repository guards pass.
No gameplay implementation or content generation is changed by R0.

### Reliable backpressure observation

The first isolated 60-second, eight-client mixed-combat sample (100 ms RTT,
20 ms jitter, 2% loss, seed 20260906) failed with three reliable admission
refusals. It recorded 3,601 ticks, zero dropped ticks and eight connected peers.
The last fact exposed a harness discrepancy: production disconnects a peer
whose reliable queue refuses a combat batch, whereas the old harness counted
the refusal and kept that peer connected. The underlying admission limit is
real; the failed run is not a passing capacity result.

The harness now follows the production disconnect policy and retains its
overflow counter and failure gate. A deterministic loopback self-test saturates
one peer, verifies its removal and checks that a healthy peer receives exactly
one admission of each subsequent batch with successive sequence IDs. It does
not impose FIFO delivery on the reliable sender's existing round-robin scheduler.
No production transport limit or disconnect behavior changed. This observation
must remain visible when comparing migration results; a later passing sample
does not prove that the intermittent admission limit has been eliminated.

One parity-corrected repeat of the same impaired workload also failed: one
admission refusal removed a peer, after which the client process timed out and
disposed its remaining connections. The server report survived; the orchestrator
then timed out waiting for its shutdown tail. The precise queue-count versus
outstanding-ID-window trigger was not instrumented. No further retries of that
workload were used to obtain a passing baseline.

A separate zero-impairment LAN reference passed all server/client checks with
eight peers for 60 seconds: 3,600 ticks, zero dropped ticks or admission refusals,
4.054 CPU seconds, 63,890 allocated bytes/tick and a 1.679 ms p99 tick. GC counts
were 27/2/1 (generations 0/1/2); traffic was 9,052,552 bytes received and
20,459,664 sent. Actual travel/trace/homing/continuous root-shot counts were
217/45/21/4,309. [Raw metrics](MULTIPLAYER_PERFORMANCE_BASELINE.json) retain both
failed stress samples and the successful LAN reference. R0 characterizes these
limits; it does not certify sustained impaired continuous-fire capacity.

## Multiplayer content inventory

`nettest --audit-multiplayer DATA OUTPUT_JSON [FH_DATA|-] [MAP_DIRECTORY|-]`
reads the multiplayer room catalog, all 16 retail entity masks and the layer
mapping for 12 modes × 8 player counts. It checks entity-table boundaries,
versions, lengths, type support and layer counts against the production parser.
First Hunt uses its separate version-1, layerless format. Self-tests cover valid
retail/FH records, multi-layer entries, malformed data and deletion-sensitive FH
types. The tool never generates content or overwrites a prior report.

[MULTIPLAYER_CONTENT_BASELINE.json](MULTIPLAYER_CONTENT_BASELINE.json) is a
compact developer inventory from the available AMHE1 data and existing custom
binaries. It groups identical layer counts and omits raw entity records and
machine-specific input roots. Detailed reports retain offsets, IDs, masks and
hashes for local inspection; no cartridge bytes belong in either artifact.

The inventory contains 42 catalog entries. All available entity files parsed:
27 retail rooms plus TEST ARENA, DUST2 and PARALLAX, totaling 1,451 records.
Six First Hunt entries lack supplied extracted data; six biodefense catalog
entries have no entity path. The report therefore deliberately says
`Complete: false` and the audit command exits 1. No parser errors occurred in the
available files. This is an incomplete inventory, **not a deletion allowlist**.
No Door/ForceField/Platform types were observed in the available subset; absent
FH coverage prevents any global deletion conclusion. The audit also does not
prove collision, objective, spawn, rendered-client or runtime-generated-entity
support. All existing parsers and content remain intact.

## R1: .NET 10 toolchain

`global.json` selects SDK 10.0 feature bands and excludes a future SDK 11.
Desktop, audio, tests and tools target `net10.0`; Android targets
`net10.0-android36.0` with minimum API 24 and JDK 21. CI and active development
commands use the same baseline. All direct package versions remain unchanged.
Validation used SDK 10.0.400 and Android workload 36.1.2 in temporary directories;
no system SDK, persistent environment configuration or user device was changed.

C# 14 exposed 107 ambiguous mutable-span `Slice(uint)` calls in the existing
tooling source. One exact `Span<T>, uint` overload forwards to the existing
mutable `long` overload. Thirteen regression cases verify aliasing, empty/end
slices and unchanged bounds/narrowing behavior. No caller rewrite, language
downgrade or parser policy change was required.

The development launcher now uses ordinary `dotnet build` from the repository
root so `global.json` governs SDK selection. The previous newer-SDK/manual-MSBuild
and runtime roll-forward bypasses are removed. Its managed launch path also now
invokes `dotnet` with the verified DLL, rather than trying to execute that DLL.
Published executable preference, Windows native executable selection, arguments,
working-directory restoration and child exit handling are preserved. A
PowerShell AST/function-only fixture passes 37 checks and is wired into the
Windows build job. This is script-routing evidence, not a Windows game run.

### Verified migration behavior

- All 336 C# tests passed, with zero skipped. The 323 R0 cases remain intact.
- Dedicated server and nettest builds passed with zero warnings or errors.
- All 16 actual-gameplay UDP WAN cases passed on .NET 10, including eight-player
  impaired links and the asymmetric four-player case.
- All 30 deterministic ON/OFF and ON/trace-only comparisons passed.
- Fifteen separate .NET 9 versus .NET 10 runs produced identical complete JSON
  bytes, including accepted inputs, shot spread seeds, hits and trajectories.
  [Comparison hashes](MULTIPLAYER_RUNTIME_COMPARISON.json) retain every case.
- Real-content dedicated-server checks, all-mode scoring and both audit and
  backpressure self-tests passed.
- All seven self-contained, single-file desktop/server publishes passed:
  Windows x64, Linux x64 and macOS x64/ARM64 clients; Windows x64 and Linux
  x64/ARM64 servers. PE subsystem/architecture, ELF/Mach-O architecture, asset
  guards and shipped-map guards passed for every package.
- The macOS ARM64 package ran the native headless server/content/network smoke
  checks successfully. Cross-publishing the other architectures is not evidence
  that those binaries ran on their target operating systems.
- The standard Android Release APK passed signing and ZIP alignment checks with
  both default ABIs (`arm64-v8a`, `x86_64`), target API 36 and minimum API 24.
  Mono remains enabled. An isolated API 36 ARM64 emulator installed the APK,
  rendered the startup activity and unpacked PARALLAX without a fatal exception;
  the activity remained resumed after 15 seconds. This was startup evidence,
  not a rendered multiplayer match. The temporary emulator was stopped.
- Scratch-only cooking produced DUST2 (21 textures) and PARALLAX (10 textures)
  bundles with recipe, BSP and texture payloads, no PK3 leakage, and passing ZIP
  and asset guards. Uncompressed payloads matched the preserved .NET 9 assembly
  run under the .NET 10 host; this particular check compares assemblies, not
  runtimes. TEST ARENA has no import and correctly produced no cooked bundle.

Android's packaged `libSkiaSharp.so` in both ABIs still has ELF LOAD alignment
`0x1000`; the other packaged native libraries use `0x4000`. Successful APK ZIP
alignment and emulator startup do not establish native 16 KB page compatibility.
That existing dependency limitation remains a release gate, alongside native
Windows/Linux execution and rendered multiplayer validation on target devices.

The isolated .NET 10 LAN cost sample passed with all eight peers for 60 seconds:
3,600 ticks, zero dropped ticks, queue drops or reliable admission refusals,
3.473 CPU seconds, 62,445 allocated bytes/tick and a 1.663 ms p99 tick. GC counts
were 28/2/1; traffic was 9,067,984 bytes received and 20,446,016 sent. Actual
travel/trace/homing/continuous root-shot counts were 225/45/16/4,242. The
[raw metrics](MULTIPLAYER_PERFORMANCE_BASELINE.json) preserve both runtimes.
This is a single sample per runtime with equal configuration, different combat
outcomes and no rendering; it supports a cost comparison, not a causal speedup
claim. The failed R0 impaired continuous-fire samples remain unresolved and were
not rerun to obtain a green capacity result during R1.

Desktop restores emit `NU1903` for the existing transitive
`Tmds.DBus.Protocol` 0.21.2 dependency. The .NET 9 assets already contained that
version; targeting .NET 10 enables transitive NuGet auditing by default. The
[upstream advisory](https://github.com/advisories/GHSA-xrw6-gwf8-vvr9) describes
malicious peers on the local D-Bus and identifies a 0.21.3 backport. This R1 pass
preserves the requested package versions and does not suppress the warning.
Dependency remediation remains separate from this toolchain migration.

Adventure/offline paths, campaign code, `GameState` ownership, the conditional
server build and Android's whole-desktop-source import remain unchanged. Their
removal belongs to R2–R12 after the R1 acceptance review; no later pass was
started and no release, deployment or push was performed.

## Reproduction

Use the SDK selected by `global.json`; use the recorded .NET 9 revision for a
pre-migration comparison. Keep report directories outside game data and choose
new output paths for each run.

```sh
dotnet test src/MphRead.Tests/MphRead.Tests.csproj -c Release -p:MphReadServer=true
dotnet build tools/nettest/nettest.csproj -c Release -p:MphReadServer=true -o /tmp/fruity-refactor-nettest
dotnet /tmp/fruity-refactor-nettest/nettest.dll --audit-multiplayer-self-test
dotnet /tmp/fruity-refactor-nettest/nettest.dll --audit-multiplayer /path/to/AMHE1 /tmp/multiplayer-audit.json - maps
dotnet /tmp/fruity-refactor-nettest/nettest.dll --match-baseline /path/to/AMHE1
dotnet /tmp/fruity-refactor-nettest/nettest.dll --match-lifecycle /path/to/AMHE1
python3 tools/run-network-baseline.py --nettest /tmp/fruity-refactor-nettest/nettest.dll \
  --server /tmp/fruity-refactor-nettest/FruityPrime.dll --simulation --data /path/to/AMHE1 \
  --seconds 20 --output /tmp/fruity-refactor-wan
python3 tools/run-mixed-combat-soak.py --nettest /tmp/fruity-refactor-nettest/nettest.dll \
  --data /path/to/AMHE1 --seconds 60 --modes on --rtt 0 --jitter 0 --loss 0 \
  --output /tmp/fruity-refactor-cost
```

The audit's incomplete exit is expected without FH data and for the catalog's
entity-less entries; do not suppress that exit in an automated completeness gate.
