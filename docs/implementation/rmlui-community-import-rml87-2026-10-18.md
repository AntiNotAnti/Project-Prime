# RML-87 native local package import

Date: October 18, 2026.

RmlUi remains the exclusive workstream; Settings stays last. The qualified
RML-86 Community library/reporting and restored RML-84 Home/Lobby remain intact.

## Implementation

Community's Import Package action opens a native modal with a system picker
and manual path entry. Preparation validates and builds a modern `.ppmap`
privately through `MapPackageInstaller.PrepareAsync`. The form reviews the
runtime map name and exact archive hash before explicit confirmation. The
existing installer publishes its private snapshot, then the owner registers it
with the existing gameplay metadata and installed registry. Closing a successful
import opens the native Installed catalog.

The configured game-file root is captured before background work; the existing
configured-files capability spans preparation, review and commit. Root changes,
cancellation, page retirement and stale results cannot publish a package.
Retired preparation results dispose their private snapshot and capability,
including results arriving after the controller is disposed. Confirmation
rechecks the context and existing scene installation gate on the owner thread.
Caller-owned source files are retained. Legacy/invalid packages receive bounded
guidance rather than private filesystem/service exception text.

The old CommunityImport-to-legacy-workspace handoff has been removed. No new
map registry, transfer queue or gameplay owner is introduced.

## Qualification

Client/server and all six tools compile with zero warnings/errors. Frozen
client: `83682fd5b5ff12e7675f723d037fa9a8061432f2e3c896d4773ea2feb281f9a9`.
Native bridge: `84434fef4a5a36549bc8133c76852fa2174d6bdb0699aeba073a6af903c0ffbd`.
Final frozen evidence: `artifacts/rmlui-community-import-rml87-20261018/build-attempt3/`.

Five CPU suites pass. The actual installer test passes 24 checks against an
independent private game-file clone, including exact snapshot installation after
the caller's private input is changed, registration, five runtime outputs and
root retirement. All original 3,858 cloned files, the RML-83 source fixture and
the repository package remain unchanged. Actual service receipts are under
`artifacts/rmlui-community-import-rml87-20261018/acceptance/actual-service-fixturev2/`.

The first native modal's actions passed, but screenshot inspection found 8-pixel
control overlaps. This failure is preserved; an asset-only repair adds explicit
action layout and overlap assertions. Corrected import acceptance passes at
Retina density 2 under `build-attempt2/`. Those runs exposed a second existing
layout issue: library paging overlapped the final row by 12 physical pixels.
Explicit paging layout and separation assertions now pass under `build-attempt3/`.
All seven final native cases pass 1,239 checks with unchanged frozen guards:
Library at 640×360, 960×600 and 1280×720 client sizes; Import and Report at
640×360 and 1280×720. Retina framebuffers are twice those dimensions and host
density is explicitly 2. All 48 PNG/semantic snapshots were reviewed. This
qualifies responsive layout, scrolling, modal focus, action routing and bounded
synthetic account/picker/commit interactions. Live account actions and physical
picker/input acceptance remain separate.

The managed/native binaries match the actual-installer test exactly; the
production delta is two stylesheets, with additional test sources. Final CPU and
actual-service reuse proofs compare their exact assemblies and relevant sources.
No external service mutation or canonical game-file write is part of acceptance.

## Mac ARM delivery

Delivered `/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml87-20261018.zip`:
85,190,828 bytes; SHA-256
`9a5da6ef5999c0320760802f710ab9462cf32ab4dabf34bb31dd475c0c4ef5a2`.
Final package proofs are under
`artifacts/ProjectPrime-RmlUi-macOS-arm64-rml87-20261018-final/`.
Ad hoc signature verification, exact extracted inventory, bundled/extracted
startup, packaged Home/resize and real AD1 thumbnail checks pass. The read-only
source fixture remains unchanged. No game assets or player data are packaged.
The earlier signed build-attempt2 candidate is preserved and was not archived.

Install-and-host continuation, remaining Hunter customization parity, remaining
rich-screen handoffs and final Settings integration are still open. Full RmlUi
migration remains in progress.
