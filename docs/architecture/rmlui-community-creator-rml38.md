# RML-38: native Community My Uploads and lifecycle

Date: 2026-10-17
Stacks on RML-37 / #700.

## Scope
- Add a native **MY UPLOADS** tab alongside Discover. It reads the authenticated creator's project catalog through the existing `CommunityMapOperations.BrowseProjectsPageAsync(mine: true)` facade and the existing Hunter License Community credential supplier.
- Display owned map title, latest immutable package hash, revision count, and archived/deleted/active state. Only service-authorized manageable projects enable lifecycle buttons.
- Archive/restore requires explicit in-page confirmation pinned to the exact current selected project and controller epoch. A fresh authenticated `GetOwnedProjectAsync` read must match the prior entire project snapshot before invoking the existing archive/restore service. On success, a fresh owned-project catalog read determines the UI. No optimistic mutation or fake success.
- A cancelled/retired owner rejects stale network results; authentication failures clear the private owned-map list and redact service errors. All network work stays off the rendering thread.
- Preserve Full Community/Studio for advanced revisions and publishing; there is no competing creator upload queue or filesystem path handling.

## Automated acceptance
The existing `tools/rmlui-community-check` fixture covers delayed/stale owned project reads, confirmation and dismissal, exact ownership, verified post-action catalog reread, stale project rejection and owner-thread constraints.

## Limitations
Creator lifecycle endpoints currently do not accept an atomic expected-revision argument. The native client performs a fresh preflight, but the map server **must** still enforce authorization, permissions and races independently. Advanced revision selection, visibility settings, local project publishing, and file chooser integration remain in Studio. Live two-account, backend authorization, and real-device visual testing are required before default cutover.

Settings stays last.