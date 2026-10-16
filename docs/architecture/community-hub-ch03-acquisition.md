# Community Hub CH-03 | Shared exact archive acquisition

**Stack:** CH-01 PR #600 -> CH-03. CH-02 PR #602 is independent.

- `CommunityExactArchiveAcquisition` unifies exact package acquisition for multiplayer synchronization, dedicated lobby prep, the legacy map picker, and historical replay recovery.
- It shares the Game/Studio transfer CAS (`LauncherPrefs.Directory/community-transfers`), cross-process download gate, resumable partials and strict SHA-256 + MapId/content/room validation.
- The first consumer downloads and verifies; subsequent consumers may reuse the exact cached immutable bytes without HTTP. If modern metadata is absent, it uses the older exact-hash origin download route.
- `MapPackageInstaller.PrepareLeasedAsync` keeps the archive read lease alive across private compilation and owner-thread Commit. The prepared-installation Dispose releases the lease without deleting the shared CAS object.
- Server-authoritative Ready, scene ownership, existing installation restrictions, package identity, replay history and network protocol are unchanged.
- The hosted-rotation private namespace path is intentionally left intact until its process-lifecycle tests are expanded.

### Regression acceptance

```bash
dotnet run --project tools/map-community-check -c Release
dotnet run --project tools/community-transfer-check -c Release
dotnet run --project tools/community-cache-check -c Release
dotnet run --project tools/map-multiplayer-check -c Release
dotnet run --project tools/map-editor-check -c Release -- --runtime-only
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true
```

Added loopback tests prove cold verified acquire, offline warm reuse and MapId mismatch rejection. Hardware joins, live 1v1/8-player, Android low-storage, replay playback/export and historical server compatibility are **not yet certified**.
