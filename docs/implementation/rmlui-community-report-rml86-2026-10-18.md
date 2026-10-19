# RML-86 native Community reporting and accessible details

Date: October 18, 2026.

RmlUi remains the exclusive implementation workstream. Preserve the RML-84
Home/Lobby composition and four-seat Party projection. Full Settings stays last
and must follow the user's working SettingsView layout.

## Implementation

Report Map now opens a native modal for the selected published map. It uses the
existing six report reasons and optional details up to 4000 characters. Opening
the form verifies and pins the current account; only explicit Submit calls the
existing Community report service. The fixed selection retains map ID, package
hash and version. Account changes retire the form and clear its details.
The existing report protocol sends map ID and version, reason and details;
it has no separate hash field. Displaying/capturing the hash does not add a new
backend identity guarantee.

The owner-thread controller permits one operation, rejects duplicate/pending
submissions, sanitizes failures and ignores canceled or retired completions.
Successful submission is shown only after the service returns. Modal Back,
focus and command routing stay with the existing page manager.

Installed descriptions, exact hashes, release notes and the Hunter cosmetic
visibility explanation have explicit paragraph roles so native accessibility
readback can identify their visible labels reliably.

## Qualification

Client/server and six acceptance tools compile with zero warnings/errors.
Final frozen client: `dbcf21f048bac3de5243bf7bdbd2f71d92b0e7206b918b11ad2318db70370e15`.
Native bridge: `84434fef4a5a36549bc8133c76852fa2174d6bdb0699aeba073a6af903c0ffbd`.
Evidence: `artifacts/rmlui-community-report-rml86-20261018/build-attempt3/`.

All five CPU suites and real-RGB headless preview acceptance pass with unchanged
frozen source/runtime guards. Native Library passes at 640×360, 960×600 and
1280×720; Report passes at 640×360 and 1280×720, totaling 788 checks. Captures
include raw semantic snapshots and framebuffer evidence. The preceding RML-85
larger-window semantic failures remain preserved and are resolved by these runs.
These generic GL runs requested the listed client sizes on a Retina framebuffer
twice as large and used host density 1. They qualify native behavior/readback at
those captured dimensions; RML-87 adds explicit density 2 coverage for responsive
layout at the matching Mac logical window sizes.

Report acceptance uses fake authorities and never submits an external report.
It exercises verification, all six reasons, exact data capture, pending/success
gates, account change, Back/focus, canceled work and late completions. Valid
multibyte details exceeding 4001 UTF-8 bytes survive native input and submission;
emoji glyph rendering is limited and full Unicode visual support is not claimed.
The composed root router forwards Report and ReportReason as well as the
existing Community commands. Original tool compilation failures and the bounded
input/routing repair builds remain preserved.

Delivered Mac ARM archive:
`/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml86-20261018.zip`.
85,185,828 bytes; SHA-256
`e02b08b67ccd2be4e28adf6d50dd8b122ef8d5e0e2c95078d782b57cf94f60b2`.
Ad hoc signing, exact extracted inventory, bundled/extracted startup, packaged
Home resize and actual AD1 thumbnail pass. The private game-file fixture remains
unchanged and no game assets or player data are included in the archive.

Full Community parity remains open: local package import and install/host
continuation are not implemented by this slice. Live backend/account and
physical input acceptance are distinct from synthetic native UI qualification.
Full RmlUi migration remains in progress.
