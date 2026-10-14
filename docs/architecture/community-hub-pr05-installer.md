# Community Hub 3.0 | PR-05 Streamlined Map Installation

**Source:** AntiNotAnti/Project-Prime main `7a51490e9242f91607eeb40cb78d8bc320fb0fdd` (after merged PR-04 #537).  
**Change scope:** validated `.ppmap` install/archive I/O and bounded runtime output publication.  
**Protocol / service / package bytes:** unchanged.

## Current-path diagnosis

Prior to PR-05, `MapCommunityClient.PrepareExactAsync` downloaded and verified a complete private archive, then `MapPackageInstaller.PrepareAsync` copied those bytes into *another* private snapshot, hashed/validated, and built runtime output. `PreparedMapInstallation.Commit` then ran `AtomicFile.Write(destination, File.ReadAllBytes(_snapshot))` under `MapRuntimeUsage.Gate`, requiring a package-sized managed array. Existing destination overwrites also duplicated a rollback backup. The runtime output install loop used another `File.ReadAllBytes` for each of five compiled output files.

Important: not all initial copies were redundant. Arbitrary mutable author files must still be isolated into a private snapshot, while the owned download and a valid, leased CAS archive already have the required private/immutable lifetime.

## Changes and ownership

1. **Owned HTTP download:** `MapCommunityClient.PrepareExactAsync` transfers ownership of its unique, already verified temporary archive to `MapPackageInstaller.PrepareOwnedAsync`. No second full download snapshot; a failed preparation removes it; a successful `PreparedMapInstallation.Dispose` removes it. The owner still validates exact `MapId`, content and package SHA-256 and runs the same private compiler. The observable Verifying → Building stage order remains.
2. **Leased CAS archive:** `MapPackageInstaller.InstallLeased` consumes an exact `CommunityArchiveCache.ArchiveLease` for the full private build and commit. The Community game workspace now uses this path instead of forcing a duplicate archive snapshot. Disposal **never deletes** a caller-owned cache archive; the read lease prevents compliant CAS eviction.
3. **Mutable source:** Existing `PrepareAsync` retains an isolated private snapshot. After the call, the caller can alter or remove its original source; verification and commit continue from captured bytes. This is an explicit compatibility guarantee.
4. **Bounded archive commit:** stage a single copy through a 64 KiB buffer into a private file on the destination volume, verify its full SHA-256, then atomically replace the archive. No `File.ReadAllBytes(_snapshot)` allocation. Preserve the existing scene/prewarm fence, conflict validation, and rollback for overwriting a **different** installed archive.
5. **Exact-idempotent reinstall:** if an installed archive's complete SHA-256 matches the required immutable package, skip package overwrite and rollback backup entirely. Runtime output validation/publication remains under the original compiler and scene ownership rules. This is an archive I/O skip, not a shortcut around Ready.
6. **Runtime output streaming:** `AtomicFile.Copy` replaces `File.ReadAllBytes` + `AtomicFile.Write` for the compiler's five output files. Each file is privately staged on its destination volume and atomically moved into place. Hash validation of the cached input is retained before copying, and the manifest still comes last.
7. **Metrics:** `PreparedMapInstallation.ArchiveIo` reports preparation/commit/backup bytes actually copied and whether an exact installed archive was reused. The Community baseline report emits cold/warm archive copy receipts and timing; no network/Ready claims are inferred.

## Safety and compatibility

- Existing package bytes, SHA-256 identities, map IDs, supported mode/room names, v2 service URLs, and protocol 53+ live identity contracts remain untouched.
- `MapRuntimeUsage.RequireInstallationAllowed` and `RequireNoConflict` execute **before** any publication directory or staging file is created. The earlier active-scene and prewarm refusal semantics are preserved.
- Compiling remains detached from the active room. `MapBuildScheduler.Install` validates its cached fingerprint before streaming and writes its manifest last. Runtime readiness still requires normal scene build/prewarm and server-authoritative Ready.
- Atomic rename and rollback protect the installed package against exceptions. Archive bytes can be unmodified even if runtime output compilation/install fails; the existing separate runtime output failure policy is not silently changed.
- No new hardlinks/reflinks, assumptions about cross-volume atomic rename, or mutable cache references. CAS lease ownership remains managed through the existing `ArchiveLease`.
- Historical replay resolution and dedicated host map rotation continue using their original exact map identities. Those callers gain the owned-download optimization without changing their protocols.
- The broad `PrepareAsync` API is intentionally *not* rewritten to skip source isolation; only explicit trusted ownership types qualify.

## Acceptance tests and measurement

| Check | Contract |
|---|---|
| `tools/map-editor-check` | Prepared prewarm refusal does not create library; mutable source snapshot counts archive bytes; first commit writes exactly one package; repeated exact commit writes zero package/backup bytes; source deletion after Prepare remains safe; mutated snapshot rejects before publish; 12 MiB binary runtime copy preserves SHA-256 and cleans failed atomic stage |
| `tools/map-multiplayer-check` | Authenticated client/host/replay paths prepare the exact `.ppmap` with zero redundant copy; private owned source survives until Dispose and is cleaned afterward; old hash mismatch/cancellation/rotation scenarios remain passing |
| `tools/community-baseline-check` | Versioned unchanged platform package golden; new actual archive byte counters and warm/cold preparation times |
| `tools/map-install-io-benchmark` | 32 MiB × 3 runs per CI OS. Compare the previous **simulated old copy algorithm** with the new real `AtomicFile.Copy` method, alternated order; record elapsed and same-thread managed allocations; emit JSON. This is **copy-stage evidence only**, not a valid large map or full live time-to-Ready |
| Existing CI | Map runtime, Community publishing, downloaded map acceptance, historical replay, server-only build, no commercial game asset guards, Windows/Linux/macOS and Android package gates |

### Reference commands

```bash
dotnet build src/MphRead/MphRead.csproj -c Release
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true
dotnet run --project tools/map-editor-check -c Release
dotnet run --project tools/map-community-check -c Release
dotnet run --project tools/map-editor-check -c Release -- --community-only
dotnet run --project tools/map-editor-check -c Release -- --runtime-only
dotnet run --project tools/community-transfer-check -c Release
dotnet run --project tools/community-cache-check -c Release
dotnet run --project tools/map-multiplayer-check -c Release
dotnet run --project tools/community-baseline-check -c Release -- --output artifacts/community-baseline-pr05.json
dotnet run --project tools/map-install-io-benchmark -c Release -- --mib 32 --repeat 3 --output artifacts/install-io-pr05.json
```

## Benchmark interpretation

For a first-time owned archive of compressed size `S`, the previous happy-path preparation wrote a duplicate private snapshot (`S`) and commit wrote the installed package (`S`): **2S logical archive writes**, ignoring the already downloaded private source, compressed data read and runtime compiler artifacts. This PR's owned fast path writes only installed archive bytes (`S`): **1S**, a modeled 50% reduction in extra archive writes. Existing destination overwrite of a different revision still needs a rollback backup; identical exact installed revision writes **zero** additional archive/backup bytes. These are **mechanistic counts**, not measured 50% time-to-Ready improvements.

The old/new A/B benchmark simulates the precise old copying algorithm and invokes the actual new atomic streaming method on a local 32 MiB synthetic file. Use those cross-platform JSON results to confirm time/memory effects. Cross-machine and OS timings cannot be directly compared without hardware controls. Actual 1v1/8-player joins with large verified maps and live frametime are a separate acceptance item.

## Rollback

Revert this PR's method additions, caller handoff, output-copy helper and tests. Existing map archives and server-side metadata are not migrated. Runtime and online map identities remain unchanged, so old clients and replay packages do not require conversion. Do not remove any previously installed package or historical replay archive.

## Later work

PR-06 moves the map package *builder* itself from all-assets-in-memory staging to deterministic streamed packaging; PR-07 improves service transfer admission and quota accounting. Those are intentionally separate from installation I/O so regressions remain attributable.
