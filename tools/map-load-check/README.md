# Real-map load campaign

`run.py` compares two already-built `ProjectPrime.dll` files on the same machine and map inputs. It runs a real eight-player bot map audit and reads load time from the game's `-maploadreport` receipt. The measurement starts at the runtime's map-load request and ends at the first successful actual presentation after all eight players are spawned and alive. Process startup is excluded; the driver does not infer readiness from log text.

This driver is for standalone published-package load campaigns only. `--maps-dir` names a bounded catalog of regular `.ppmap` package files. The driver copies only those files into private catalogs passed as `-mapdir`; loose JSON recipes, `.pk3` archives, and other files are excluded so runtime loads cannot follow recipe paths back into the source catalog. Use the existing `tools/pk3-conversion-bench` workflow for Quake `.pk3` conversion measurements. The source catalog remains untouched.

Each sample launches a fresh process with `-maptest <ROOM> -players 8 -seconds 1 -bots -mapdir <PRIVATE_PACKAGE_CATALOG> -maploadreport <RECEIPT> -noupdate`. A sample is accepted only when the process exits with code 0 and its schema 1 receipt has `ready: true`, `outcome: "passed"`, `exitCode: 0`, the requested room, one loaded room, and all eight requested players in the scene. The receipt's `readyElapsedMilliseconds` must match the `firstSuccessfulPresentWithAllPlayersLive` stage. Any process timeout, missing or malformed receipt, readiness failure, audit failure, or nonzero exit stops the campaign. Failed run metadata and logs remain in the output; failed attempts never become timing samples.

The campaign reports `application-cold` and `persistent-cache-warm` separately. It inventories and copies every configured, non-empty native asset root once into a private temporary `assets/` tree before launching any game process. Each private profile's `paths.txt` points every configured root at those copies, while `Export` points inside that profile. The native source roots are never passed to the runtime, so generated `CustomRooms` outputs cannot be written into operator-owned extracted data. Root inventories and copy hashes are captured outside the timed interval, and the original roots are re-inventoried at campaign completion. Inputs are bounded to 32 configured roots, 200,000 files / 4 GiB per root, and 400,000 files / 8 GiB total; disk roots, links, and source/target overlap are rejected.

The map catalog scan is bounded to 50,000 filesystem entries and 8 GiB of regular-file data. At most 20,000 `.ppmap` packages are admitted, with a 512 MiB limit per package and 8 GiB total package bytes. Every application-cold sample gets a fresh independent package-copy directory, which is deleted after its owned game process exits, including timeout and interruption paths. Persistent-cache-warm uses one private package catalog per arm and room, shared by that arm/room's preseed and samples. Copies are regular file copies, never hard links; source and destination overlap, symlinks, non-regular files, and package paths that escape the catalog are rejected. Private copies and profiles live outside the evidence output and are removed when the campaign exits.

Every application-cold sample also receives fresh private `PROJECT_PRIME_USER_DATA` and `PROJECT_PRIME_Q3_CACHE` directories and a unique `-customruntimenamespace` per arm, room, and trial. The Q3 cache override is always set, so ambient/global import caches are not used. Persistent-cache-warm runs use stable private user-data, Q3 cache, runtime namespace, and package catalog per arm and room: one successful preseed process populates them, preseed timings are excluded, and later fresh processes reuse them. Baseline and candidate state remains separate even though both read the same private native asset copies. These runs do not measure an in-memory/model warm load, and the driver does not flush the operating system's file cache.

Samples are paired sequentially. Even-numbered trials run baseline then candidate; odd-numbered trials reverse that order. The sequence is deterministic, not randomized. The runner records all raw load milliseconds and reports P50, P95, and P99 with the nearest-rank method. At fewer than 100 samples, P99 is exploratory; with the default 20 repeats, nearest-rank P99 is the maximum observed sample. Shared OS cache state, thermal/power state, driver scheduling, and other system activity can still affect the comparison.

Input hashing happens outside each timed map-load interval. `inputs.json` records SHA256 digests for both DLLs, the supplied `paths.txt`, every regular `.ppmap` package included in the map catalog, and a bounded per-file inventory digest for each configured native root. Loose JSON and `.pk3` files are not hashed or copied because they are not campaign load sources. After a campaign finishes or is interrupted, the driver rehashes these inputs and marks the result failed if any benchmark input changed; interruption is recorded before private temporary data is removed. Each child receives a copy of `paths.txt` rewritten to private native-root copies and a private `Export` directory. Native asset file names and contents are not written to the evidence output; it records only root keys, file counts, byte counts, and inventory hashes.

## Run

Use a new, empty output directory for each campaign. Supply paths to the baseline and candidate assemblies, the operator-owned `paths.txt`, the published-package catalog, and one or more room names:

```powershell
python tools/map-load-check/run.py `
  --baseline C:\builds\baseline\ProjectPrime.dll `
  --candidate C:\builds\candidate\ProjectPrime.dll `
  --dotnet 'C:\Program Files\dotnet\dotnet.exe' `
  --game-paths C:\game-data\paths.txt `
  --maps-dir C:\published-map-packages `
  --rooms 'custom-arena-a' `
  --rooms 'custom-arena-b' `
  --repeat 20 `
  --output C:\benchmarks\map-load-2026-10-21
```

`--dotnet` is optional when `dotnet` is available on `PATH`; when supplied, it must be an absolute SDK executable path. `--repeat` defaults to 20 and is limited to 100. Each child is bounded by `--timeout-seconds` (default 180, maximum 3600); the driver terminates and waits for only the process it launched on timeout or interruption. A campaign is limited to 1,000 subprocess launches.

The output contains `campaign.json`, `inputs.json`, `summary.json`, and a `runs/` tree with separate stdout, stderr, runtime receipts, and per-process metadata. `summary.json` has raw accepted samples, the preseed list, and per-room/arm/cache-kind aggregates. Review the full run receipts and logs alongside the summary before drawing a performance conclusion.

The Python unit tests exercise receipt rejection, timeout and interruption handling with fake processes, native-root copy/rewriting and source preservation, package-only private catalog copies and cleanup, Q3 cache isolation, runtime namespace isolation, alternating order, and nearest-rank calculations. They use small synthetic files/processes, are driver guard tests, and make no performance claim:

```powershell
python tools/map-load-check/test_run.py
```
