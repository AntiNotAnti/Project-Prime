# Android access to owned character source documents

This slice makes the existing private character-bundle importer reachable from Android Settings. It adds selected-document access, bounded temporary staging and import lifecycle ownership. It does not add playable characters, an Android source-model renderer, public character selection, multiplayer/replay identities, or a new wire format.

Frozen source `d098819dcf19ea5977052b3963914f4b41364f1b` includes main through PR863 / protocol 73 and passed all 19 qualification commands. That protocol change is inherited; this source-access slice introduces no wire or replay fields. Follow-up `78aa831ee5814c83a8faac240bcf39760d041e3a` changes only two Android view files to prevent a closed view from restarting its progress timer on reattachment. Its portable importer and test sources are byte-identical to the qualified baseline. All six Android follow-up qualification commands passed, followed by the isolated-package emulator checks below.

## User flow

The existing Android Settings view has an **Additional character sources** group on **Profile**. Choose an appearance, then:

- **Import / Update Selected** opens the system document chooser for the matching owned Prime or Echoes ISO, GCM or RVZ.
- **Import Same-Source Group** requests both Echoes appearances or the four Prime Troopers, using the existing canonical source list.
- **Check Selected** reports the existing bundle and per-part component states without applying or discarding the settings draft.
- **Remove Selected** opens a confirmation naming the selected appearance. Confirming removes its current discovery receipt; retained immutable generations and the original source document are not deleted.
- **Cancel Import** requests cancellation. If the system chooser remains open, close it too. Already committed bundles remain installed; use Check to inspect them.

Import/removal controls require launcher context and an applied or discarded settings draft. Check is read-only. While a source operation is active, the shared gate prevents another copy/import/check/remove from replacing it. Opening the chooser normally pauses the Activity; that normal pause is not treated as cancellation. Closing or detaching the requesting Settings view retires its result/progress ownership.

The chooser accepts a selected readable document without broad storage permission. It uses one-shot read access; there is no remembered source URI or persisted grant. Reimport asks the user to select the source again. The provider display name, URI and extension are not trusted filesystem paths or format authority.

## Storage and parser boundary

`OwnedSourceSnapshot` opens and copies the chosen provider stream on a worker. It supports nonseekable streams and unknown lengths, using a fixed 128 KiB buffer and a **2 GiB physical source-copy cap**. It recognizes the existing GameCube ISO/GCM or supported RVZ header, then gives the existing path-based importer one complete ordinary seekable file. Header recognition does not qualify the edition or source archives: the existing importer still verifies those identities and hashes. Existing RVZ compression restrictions remain unchanged.

Scratch lives under the app's canonical internal `FilesDir/source-import-scratch`, outside log-sharing storage. Each attempt gets its own random directory, exclusive owner lock and application-chosen `source.iso` or `source.rvz` name. The snapshot remains owned through import and is deleted on completion, error or cancellation. Recovery considers only bounded, recognizable abandoned scratch directories with claimable owner locks and the expected files; active or unrelated directories are not deleted. Cleanup failure is not evidence that a payload was removed.

Published inventory has a different owner. `CharacterImportPaths` captures the absolute, existing, canonical app-writable Android data root once. Repeating the same configuration is idempotent; a different root is refused. Android initialization resolves platform path aliases before assigning the import root, preferences and game-file roots. Default import/inspection/selection consumers agree on `source-character-previews` under that authority. A job captures its destination and canonical appearance IDs before work starts. Desktop `AppPaths` behavior is unchanged.

The existing `RetroCharacterPreviewExporter` and `LocalImportBundle` retain source verification, per-appearance publication, immutable generations, writer leases and atomic current-receipt replacement. The new document bridge does not implement another disc parser, importer, inventory pointer or rollback protocol. Cancellation before a commit preserves the previous bundle; cancellation after a completed publication does not remove that committed appearance or compensate by deleting its generation.

## View, Activity and provider ownership

`MainActivity.SourceImport.cs` supplies an `ACTION_OPEN_DOCUMENT`/`CATEGORY_OPENABLE` bridge. The Activity result captures an opaque selection and application-scoped resolver; potentially blocking provider open/read/dispose work does not run on the UI thread. Provider errors are converted to bounded source-independent status text, rather than exposing URI/account details.

`SourceDocumentPickerSession` assigns monotonic bounded request codes. A cancelled launched chooser remains a tombstone until its result is drained, so an old result cannot satisfy a newer request. The high-water mark is retained across Activity state restoration, and stale source-picker results are drained. Activity destruction cancels its picker/registration lifetime; exact registration identity prevents an old Activity's disposal from clearing a newer registration.

`SourceCharacterImportWork` holds one process slot across selection, copying, import and resource retirement. Its cancellation links the requesting view and Activity. A late completion/progress callback cannot update a replacement or retired view. A provider may ignore cancellation or block during read/disposal; the slot remains occupied until resources actually retire. This is cancellation and publication fencing, not a promise that every remote provider stops instantly. Durable committed bundles survive view/Activity retirement.

## Frozen build and source evidence

Windows authentication remained unavailable, so qualification used the authorized Mac fallback. Every command used isolated user-data and temporary directories; those contexts were deleted. The retained local receipts contain commands, environments, exits, source manifests and binary hashes.

| Frozen source | Archive SHA256 | Local archive root and receipt directory |
|---|---|---|
| `d098819dcf19ea5977052b3963914f4b41364f1b` | `9a4968e427d716628ebbf81f73f2c613d6d1c9177a2cb53863d8b7507ebe6623` | `/private/tmp/sh-source-access-final-d098819dc-qu2qw9rr`; `artifacts/sh-integration/source-document-access-final-d098819dc/` |
| `78aa831ee5814c83a8faac240bcf39760d041e3a` | `5d7d002ada047e703084f08b99a0a2817080f419c8078f787fb4af5b02b56c24` | `/private/tmp/sh-source-access-followup-78aa831ee-3z7004ai`; `artifacts/sh-integration/source-document-access-followup-78aa831ee/` |

Each archive has separate `source` and `server-source` trees. All **6,144 tracked files in each tree** remained unchanged in both runs, as recorded by each `source-integrity-after-tests.json`.

| Evidence | Result and scope |
|---|---|
| Portable synthetic access checks | 196 passed: 109 snapshot, 71 picker/session/worker and 16 root-authority checks |
| Portable owned-source composition | 9 checks passed using a nonseekable, unknown-length provider and the default importer: 1,287,435,724 copied bytes, normal recipe 5 Ready, 19 required components / 23 files; original source hash unchanged and temporary derivatives deleted |
| Frozen baseline `d098819dc` | **19/19 commands passed**: desktop/dedicated Release with zero warnings/errors, source-free importer/character/settings/game-files suites, owned document import, RML assets, native runtime verification, standalone Android Debug and signed Android Release/AOT |
| Android follow-up `78aa831ee` | **6/6 commands passed**: native verification for Mac arm64 and both Android ABIs, standalone Debug, Release/AOT and APK signature verification. Android builds reported 25 existing warnings and zero errors. |
| Android AOT inventory | Both frozen Release APKs contain 119 AOT libraries for each of `arm64-v8a` and `x86_64`, including ProjectPrime; signature verification passed |
| Initial exploratory emulator package | Fast Deployment assemblies were absent from the first standalone APK; native startup aborted before application code. This packaging failure remains recorded separately and qualifies no UI/provider behavior. |

The final Release APK SHA256 is `f4a26f5a9eba49ecef83a5e62c4a6e5ac616fb477944e7a2ced6a952678de95e`. The final standalone Debug APK used for emulator testing is `516aa5a405b0c52bb6905f1321202248ba9f93c9fc394e40e34e0c9b5f9e6f8a`, package `com.projectprime.sourceaccesscheck`, version 1.0 / code 1. The Release APK was built and inspected; the isolated Debug APK was installed and exercised.

## Actual emulator workflow and recovery

The existing API 36 arm64 `emulator-5580` ran only the isolated test package. Its configuration and the main application were untouched. Baseline `d098819dc` directly exercised Profile controls, selected and same-source-group chooser opening, chooser background/return/cancel, Check NotInstalled, named Remove cancel/confirmation, and refusal of an invented invalid ISO with empty scratch afterward.

Final `78aa831ee` exercised chooser cancellation, leaving and reopening Settings/Profile, and Check after reattachment. The actual system document chooser then selected the local owned Echoes RVZ. ContentResolver copied all **1,287,435,724 bytes**, the existing importer reported one published private appearance bundle, and Check reported **Ready, body v4 Ready and ball v3 Ready**, with all local components verified. No broad storage permission or persistent URI grant was requested.

One bounded process-death test interrupted reimport of that same document. A partial internal scratch file of **4,325,376 bytes** was observed before force-stopping only the isolated package; the abandoned file was **17,301,504 bytes** when inspected after termination. The previous receipt hash remained unchanged after restarting the app. The next import removed the abandoned scratch directory, returned Ready and left scratch empty. It reused the same published generation and receipt hash. This proves that one actual mid-copy force-stop/recovery path; it is not a general crash suite or an Activity-recreation test.

Remove Cancel preserved the receipt hash. Confirm Remove unpublished the current selection while retaining its immutable generation. The source document remained unchanged.

The Android generation was `291c88a543f9a0773a1625f9d9b51286e27e17f218c43c0f56904f5f3fdc57d5`, with current-receipt SHA256 `060e865c82b87e12b0024ab1398041be031806d63fd5dc818284bc8f614cfff0`. It differs from the desktop owned-document generation `e49d6ec63b18b3d19cc3b24c300c34b40dfa5a574b1b3f743d59787b7356dab9`. Only the Android receipt digest, generation and all 23 component names/lengths were retained before cleanup; per-component digests were not retained. The cause of the difference is unknown. **Cross-platform component byte parity and Android runtime-preview admission are unproven.** Ready here verifies the local authoring container; it does not qualify playable characters, materials, first-person presentation or animation/morph gameplay.

Original and emulator-copy SHA256 checks matched `ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`. After testing, the unique emulator RVZ and its MediaStore row, isolated package, internal scratch and generated app data were deleted and absence verified. The original host source and installed-test APK hashes remained unchanged. No owned-source or derived-media screenshots were taken; all preliminary product-UI captures were deleted. No source or generated payload is committed.

Local emulator metadata is retained under `artifacts/sh-integration/android-source-import-access-exploratory/standalone-d098819dc/` and `followup-78aa831ee/`; the latter includes `summary.json`, 136 logged ADB commands and partial-copy observations. The initial packaging failure remains in the same parent evidence directory.

Broader acceptance still requires physical devices, cloud/stream-only and SD-card providers, denied or revoked access, low storage and blocked-provider behavior, Activity recreation while choosing, broader process-death/crash cases, actual same-source-group import and late cancellation around publication. Windows execution, physical rendering/input/performance, playable integration and Android runtime-preview parity remain unverified. Rollback removes this Android access path; existing desktop import and retained bundle generations remain governed by their prior owners.
