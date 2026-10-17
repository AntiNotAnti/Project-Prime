# RML-37: native Community verified installer

Date: 2026-10-17
Stacks on RML-36 / #699.

## Implementation
- The native Community Discover page can install the selected **exact immutable published revision** without leaving RmlUi. `community:detail:0` is handled by a foreground-document intent, with a cancellation control.
- The installer resolves the selected hash from the current Community backend, checks the returned Map ID and hash, then reuses `CommunityTransferSession.Shared` and `CommunityTransferManager.EnqueueDownload/WaitAsync/LeaseVerifiedArchive`. No new HTTP download/receipt/cache pipeline is introduced.
- The existing `MapPackageInstaller.InstallLeased` verifies archive/content/package identities before install and compilation. Gameplay scene usage is checked before starting and again before commit/registration.
- UI-thread `Metadata.RegisterDownloadedMap` updates the already-existing local map picker index. Installation happens off the UI thread to avoid the prior Community tab stall.
- Every operation captures the exact Map ID/hash, uses a generation fence and a cancellation token, and rejects malformed or mismatching results. Closing the page cancels the foreground handoff, not the durable download queue. It never directly changes network Ready state.
- Native page bindings report progress state, disable duplicate installs, and show a meaningful cancellation action. The existing Full Community entry remains available for creator and advanced management.

## Regression acceptance
`tools/rmlui-community-check` now covers malformed selected hashes, duplicate install commands, mismatch rejection, exact result registration, stale completion on cancellation and owner-thread registration. The existing no-game-assets workflow already runs this fixture.

## Known limitations / real-device acceptance
The transfer progress widget is currently a coarse busy/status state, not per-byte queue progress. Advanced transfer management, favorites and creator publishing remain in Full Community / Studio. Device and live backend verification with genuine user-owned map assets is still outstanding. Existing Avalonia remains default, Settings remains last.
