# CW-14 — separate-process Weapon Studio MVP

Enable `PROJECT_PRIME_WEAPON_CREATOR=1` and choose Weapon Creator from the Studio
hub. The game launches the paired Project Prime Studio application. Studio also
accepts `--weapon` for a new draft or `--weapon <absolute .ppweaponproject/.ppweapon>`.
The existing authenticated local broker carries a bounded document request, never
package bytes, arbitrary commands or a live game object. Unsupported extensions,
unsafe paths and replay-only recovery flags are rejected. Dirty/busy workspaces
retain ownership while later broker requests wait.

The CPU-only `WeaponAuthoringWorkspace` creates a new content ID from the original
reference assets, imports exact packages/source directories, validates edits and
builds deterministic archives. The UI exposes name, author, mechanics and original
rigid-box model JSON, with range guidance and validation errors. Whole candidates
are preflighted before replacing the last valid snapshot. It provides an isometric
static picture of the actual model/palette/muzzle and bounded model/audio details.
Picture generation and conversion run off the UI thread; native image publication
belongs to the page owner. The private preview cache has a 32-picture workspace
budget and waits for render/conversion retirement before cleaning its owned files.

Save writes one bounded `.ppweaponproject` JSON document containing exact package
bytes. Same-directory staging, flush and atomic promotion protect saves; a persistent
exclusive sibling lock and prior SHA reject concurrent stale overwrites. Save As and
archive export refuse existing destinations. Link/reparse/device/ADS paths are refused.

Creators explicitly declare ownership or distribution permission and identify the
attestor. The declaration records UTC time and exact archive SHA, and any package
edit invalidates it. Export and publishing require the declaration; save the workspace
after attesting to retain it. This is a recorded creator declaration, not independent
legal review or a cryptographic signature. Packaged license text/SPDX metadata remains
part of strict package validation.

Install uses the atomic local package store. Play installs the exact validated revision
and starts an owned **separate game process**, retaining a shared archive lease. The
offline game starts empty in an authored stock pickup room; players must collect the
world weapon before firing, reloading or changing optics. Stop/close retires only the
owned child and its lease; the test has a 120-second lifetime. Installation and editor
controls never grant/equip a gameplay weapon.

This is the developer JSON editor MVP. Supported authoring is the existing original
rigid-box model and precision mechanics schema. Arbitrary GLB/texture/audio conversion,
in-editor animated 3D/audio, drag handles and Hunter attachment editing are unsupported;
the UI says so. The actual pickup-test game is the firing/reload/optic and mounted-model
preview. These limits must remain visible in any release scope.

## Evidence and limits

Compilation receipts are under `artifacts/custom-weapons/CW-14/compile-fixed/`. They identify
their configuration and combined worktree; later Community UI drafts may be present.
Compilation is not visual or process-lifecycle qualification.

An initial plain Studio build failed with NETSDK1150 because the Studio head selects
self-contained locally while its executable project reference did not inherit that
property. The receipt is retained in `CW-14/compile/`. Build the paired heads with
explicit global properties, as the desktop packaging configuration requires:
`dotnet build src/ProjectPrime.Studio/ProjectPrime.Studio.csproj -c Release -r win-x64 -p:SelfContained=true -m:1 -p:UseSharedCompilation=false`.

That configured Studio/game build exited 0 with 0 errors and 2 existing warnings,
in 101.89 seconds, after integrating main `ad39e26abb1d76261872b6a6e60cca0272aa8376`.
The receipt includes log and both managed binary SHA-256 values. Subsequent Community
UI drafts were also compiled, but are committed separately as CW-15.

**NOT RUN per user instruction:** remaining tests; rendered Studio workflow; author,
save/reopen/export/install/play round trip; invalid-input/fault/stale-save/IPC attacks;
child crash/close/lease races; preview fidelity, controls and accessibility. Android
build/device work remains deferred, and Studio has no Android editor dependency.
