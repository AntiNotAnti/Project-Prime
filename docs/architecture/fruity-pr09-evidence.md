# Fruity PR09: dedicated release archive evidence

Date: 2026-10-11. Source baseline: `ec9d63f7afa7ddd1220cf9441f619c3576294014`.
Host: macOS ARM64. SDK used: `/Users/jarrett/.dotnet/dotnet`.

## Proven gaps and changes

The release workflow's dedicated startup gate ran against assembled publish folders before tarball creation. It did not verify the delivered archive's executable permissions, architecture, complete manifest or startup from an isolated extracted installation. ARM64 packaging did not explicitly copy the project LICENSE, while x64 copies tolerated failure.

`tools/check-server-archive.py` now validates Linux server tarballs before the release artifact upload. It rejects absolute/traversal paths, links, special entries and duplicate normalized entries before extraction. It requires ProjectPrime, LICENSE, SERVER.md, SERVER.txt and the ownership manifest; checks executable bits and ELF64 little-endian machine identity; verifies exact manifest coverage and every SHA-256; rejects updater runtime state. It extracts only reviewed regular files/directories into a disposable directory. The x64 workflow invocation then runs the existing startup/directory smoke from that directory as its working directory, without a developer tree fallback. ARM64 gets the same structural/integrity gate without attempting cross-architecture execution.

Server LICENSE copies now fail when absent. The ARM64 package explicitly receives LICENSE. The existing Linux publish-folder smoke remains an earlier diagnostic.

## Verification actually run

- PASS: `python3 -m unittest discover -s tools -p 'test_server_archive.py'` — 9 synthetic content-free archive checks: accepted archive, wrong architecture, missing execute bits, missing LICENSE, tampered hash, unowned extra file, traversal, duplicate normalized name, symlink.
- PASS: `python3 -m unittest discover -s tools -p 'test_release_manifest_writer.py'` — 4 existing manifest checks.
- PASS: `/Users/jarrett/.dotnet/dotnet run --project tools/updatecheck -c Release --no-restore -p:UseSharedCompilation=false` — compiled and ran existing version selection, digest, transaction rollback at six publication faults, idempotent recovery, abrupt worker termination at six mutation points, user-file preservation, tampered payload rejection, parent timeout and known-good helper/handoff checks. Also PASS: nine new direct server cases (six rollback faults, tamper refusal, retained old-image handle bytes, successful commit preserving operator data).
- PASS: `bash -n tools/check-dedicated-server.sh`.
- PASS: serial `dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true --no-restore -p:UseSharedCompilation=false -m:1` using the absolute SDK: 0 errors, 21 existing warnings. This is a source build, not a Linux published-package startup.

Synthetic ELF headers test policy only; they are not executable package evidence.

## Remaining PR09 acceptance and limits

- NOT RUN: real Linux x64 unpacked candidate archive startup, missing-game-data refusal, directory query on this macOS host. The new release workflow gate invokes the existing executable smoke for those behaviors on Linux x64.
- NOT RUN: ARM64 Linux device startup, oldest supported Linux distribution/glibc/runtime compatibility. The repository does not define an oldest supported OS contract in SERVER.md; no glibc library bundling or guessed minimum was added.
- NOT RUN: extracted proprietary game-data-backed match/advertisement, coordinated client/server deployment, clean shutdown exit-status acceptance, supervisor/service restart, staging deployment and full GitHub Actions release jobs.
- PASS: local Linux x64 and ARM64 self-contained single-file candidate publishes and real tarball architecture/permission/license/exact-manifest validation. Both package asset guards also pass. This locally built candidate was not published as a release and was not executed on Linux. Candidate archives and hashes are retained under `artifacts/fruity-plan/`.

`ServerUpdate.ReplaceInPlace` previously removed obsolete files then copied/renamed one file at a time without rollback. It now uses the existing shared `ReleaseInstallation.Apply` transaction: complete incoming payload verification/staging precedes changes, each original is renamed into a backup, the manifest commits last, and failure restores prior release-owned files. Startup runs transaction recovery even with automatic updates disabled. Publication does not write into a running image's existing bytes. The existing empty-server and supervisor restart decisions are preserved.

`tools/updatecheck` now links the actual ServerUpdate implementation and adds direct server publication rollback at six mutation boundaries, paths.txt preservation, successful release ownership replacement, tampered staging rejection, and byte preservation through an old image's open handle. A retained file handle exercises the rename boundary; it is not certification of every Windows mapped native dependency or an OS service. Real running native image, service recovery and sudden host power-loss acceptance remain NOT RUN. If recovery itself fails, the server logs the transaction exception and does not claim successful replacement.

The tarball validator checks manifest consistency, not independent release authenticity; downloaded package authenticity remains the updater's GitHub asset digest check. No protocol or server restart-policy changes were made. No deployment, publishing or commit occurred. Rollback is reverting the tools/workflow and ServerUpdate changes; `-noautoupdate` disables automatic server installation.
