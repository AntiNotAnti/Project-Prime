# Proposed Android native local cycle and Adventure seams — October 17, 2026

This records the reviewed design. The subsequent Offline-only production implementation and source-fixture scope are recorded in [configured-local launcher](rmlui-android-configured-local-launcher-2026-10-17.md); Adventure and IME below remain proposals. No default, account, device or acceptance state is changed by this document. The next smallest complete slice is **configured-local Offline Home → match → fresh native Home → another match on the same GameView/EGL owner**. Adventure and editable text follow separately.

## Existing owners that constrain the cycle

- `GameView.RenderLoop.End` currently permanently disposes native Pause, runs `Scene.DoCleanup`, clears `Scene`, invokes `AndroidMatch.Finish`, and posts `_onEnd`. `MainActivity.EndMatchCore` then stops/removes the view; context destruction supplies the final GPU lifetime boundary.
- Desktop `RenderWindow.EndScene` explicitly calls both `DoCleanup` and `UnloadGl`. The latter releases scene textures, shared-model leases, pipelines, shadows, transient textures and map runtime usage and marks visual release. A retained Android context needs this existing release path; merely keeping the old View is insufficient.
- `MatchStart.PrepareBeforeTeardown` / `AfterMatch(PostMatchCommitContext)` own detached save bytes, retries, exact request versions, and publication of ship/ending receipts. `ForegroundSceneBinding.TryRelease` drops the old player/state/random compatibility graph only after committed payload, cleanup, visual release and callback/transition eligibility. Reuse these owners. `AndroidMatch.Finish()` currently captures through `GameState.Current` after cleanup; a cyclic implementation must retain the exact old state/commit until that ordering completes.
- Current `RmlUiAndroidLauncherSession`, its Admission, and `RmlUiAndroidPauseRoute.DisposeOwner` are terminal. They cannot be reopened for a second scene. Fresh route/controller/session objects are required, while the GameView/thread/context remain the same.
- `StartScreen.Reset` reloads current settings/preferences, clears its one-shot `_finished` plan and stale overlays/workspaces, refreshes room keys and preserves its existing lobby coordinator. Its attached `LobbySessionCoordinator` has no clock/pump when `Screen` is null. Do not add a second coordinator or native Home network pump.
- `BindSurface` currently calls `GlEs.Reset` when `Scene == null`; `GlEs.Reset` forgets context-owned names and is documented for context loss. The new cycle must track actual context creation, not use scene absence as a proxy. Preserve resources across ordinary surface replacement and do not reset the context helper simply because native Home has no Scene.

## First implementation slice: Offline only

Keep the current explicit build + Intent opt-in. Restrict native cyclic return to a plan admitted by this native local route, `LaunchKind.Offline`, with no live NetSession or replay. Network/replay/legacy-created GameViews retain their existing exit owner. Native errors or incomplete cleanup fall back through terminal Stop/context cleanup and the existing launcher.

Replace the `Scene == null` / `_nativeLauncherMode` decision with a small explicit host phase:

`Home → LaunchSubmitted → MatchLoading → Match → RetiringMatch → AwaitingHomeUiAck → Home`

`Stopped` and `Faulted` are terminal from every phase. In particular, scene absence in `AwaitingHomeUiAck` must never execute the previous `_build` delegate. A pending return is consumed at an EGL frame boundary; a Scene close callback only requests it and never cleans up recursively from inside scene processing.

Proposed narrow values/API:

- `RmlUiAndroidHostWitness(Guid ViewLifetime, long Cycle, long MatchGeneration)` and an immutable phase snapshot. UI snapshots never contain Scene, GameState, native document managers, EGL objects or controllers.
- `GameView.TryRequestNativeHome(witness)` validates the current phase/generation, immediately invalidates copied input, and queues the return. Duplicate/old requests are harmless. Pause Leave, the engine-close callback, and existing result/rematch callbacks must capture the exact match witness when wired; an old result callback cannot end the next match.
- `Action<GameView, ReturnWitness> SceneRetired` posts a value notification after EGL cleanup. `GameView.TryAcknowledgeNativeHome(witness, copiedHomeOptions)` supplies fresh room/rule snapshots from the existing UI admission owner. Destruction, a later phase, or a replacement Activity rejects the ack.
- A per-View holder atomically publishes the current fresh launcher/Pause references for UI input wrappers; only EGL replaces/disposes those references. An old retained route remains stopped. Do not revive its `_stopped` flag or reuse document/admission generations.

Return order on EGL:

1. Confirm exact old Scene generation and local eligibility; cancel old input/late aim/composition, release AndroidInput/contacts, and invalidate all Pause/effect notifications. Retire the Pause surface/controller/native host, recording native shutdown success.
2. Capture `MatchStart.PrepareBeforeTeardown(old.GameState)`, run `old.DoCleanup()` and `old.UnloadGl()` while its existing context is current. For an active room transition preserve the existing post-cancellation capture timing, using the exact old state after cancellation rather than introducing an early save snapshot.
3. Run the existing `AfterMatch(commit)` commit owner, then exact `ForegroundSceneBinding.TryRelease(commit)` only when eligible. Do not publish scene-free Home while a failed save, unreleased graph, active callback/transition or failed visual/native release remains. The first Offline-only slice has no Adventure payload; failed eligibility terminates to the existing fallback rather than inventing a save-recovery UI.
4. Drop Scene references/delegates that retain it, clear chat and per-match input latches, pending spectator/replay/menu commands, late aim, fixed-step/pacing and copied result pixels. `PauseMenu.Reset`, existing spectator/reset policy and Android overlay visibility are handled by their current owners; context-owned GLES resources are retained, not blindly forgotten.
5. Enter `AwaitingHomeUiAck`, with no Scene update or network pump. Post the exact return witness. If backgrounded, keep it pending without native focus; Stop still wins.

Return order on UI:

1. Validate exact View/cycle/phase. Clear existing pending-load/end-panel/replay/pause/deferred callbacks and keyboard state; remove only ordinary touch/result overlays. Do **not** stop/remove the GameView, release the process preview barrier, or reveal/restart the legacy Hunter preview.
2. Reload settings and run the existing `StartScreen.Reset` path, preserving the sole coordinator and authoritative launch admission. Capture fresh room keys/rules, then acknowledge that exact return witness. Failed or throwing reset/ack takes terminal fallback.
3. Transfer the same View reference from `_gameView` back to `_nativeLauncherView`, so `InMatch` remains truthful; keep legacy launcher hidden and the native input barrier held.
4. EGL creates a fresh launcher session/admission lifetime and, for the next accepted match, a fresh Pause route. Initialize Home only after prior native shutdown succeeded. Reuse the existing asset root and process native lease. The process preview barrier stays held until this View finally stops, including during Activity destruction and asynchronous cleanup.

The second launch follows the currently implemented StartScreen admission/orientation/window-settling checks and native-shutdown-before-plan handoff. This adds no Scene, window, context or NetSession pump.

## Tests before claiming the cyclic slice complete

Create a durable source-linked lifecycle fixture used by the real phase/handshake implementation, with explicit fake scene/native/UI boundaries. Verify two and many Home/match cycles reuse one owner/thread/context identity; at most one Scene is live; `DoCleanup`, `UnloadGl`, commit and native shutdown happen once in order before fresh Home; old generations/documents/IME/Leave/results/Loaded/Failed callbacks cannot affect later cycles; new Pause works after each return; old Held/Back/menu/aim and result pixels do not carry; Home never ticks simulation or NetSession; Stop/background/surface loss/resize at every phase cannot revive input or an old callback. Inject cleanup, visual release, shutdown, commit, UI-reset and ack failures and require terminal fallback without a second live owner.

Retain the existing 661 launcher, 54 input, 42 asset and 38 Pause checks, plus actual no-content scene/foreground weak-reference and archival hash checks in the established nettest fixture. Root then coordinates real server/client/Android compilation and package/native ABI verification. A physical device must demonstrate two local matches on the same View/context, real navigation, resize/background, no audio/resource accumulation and failure fallback. Compilation/source fixtures do not establish those runtime or performance gates.

## Adventure after the cyclic owner exists

The shared `RmlUiMenuController` already has `RmlUiAdventureDraft`, `pages/adventure/saves.rml`, refresh, resume, new-run and overwrite confirmation. Extend capabilities explicitly for Adventure; do not simply make LocalOnly unrestricted. Supply the existing three-slot `AdventureSave.Read` + exact `ContentFingerprint` projection. Its JSON/backup/new-game serializer remains authoritative. UI launch uses the current `StartScreen.RmlUiLaunchAdventure(expectedSlot,newGame,hunter)`, which rereads slot metadata and exact bytes before creating the existing plan. Generalize the copied request envelope by a discriminated local launch request; keep every request field and one-shot ack fenced. Invalid/changed/unreadable saves stay on a refreshed usable native page or explicit legacy fallback.

`AndroidMatch.BuildAdventure` already owns Begin ordering, intro movies, selected slot, room, landing spawn and exact dock validation. Keep it. Before enabling Adventure return, wire the detached commit/retry protocol above: publish `RecentShipDock`/`RecentCampaignEnding` only after successful commit, retain failed payload for retry, and block a new run until retry resolves. Do not capture another canonical save or clear a newer request version. Journal is already captured on EGL by `RmlUiAndroidPausePresentation` and rendered by the shared discovered-only Journal page; it truthfully says gameplay continues.

Ship/ending pages and `CurrentNativeCampaignShipBackend` are currently behind desktop-only guards, but their controllers/contracts are portable. Widen only the page and small backend guards, then compose them on the existing EGL native host; leave `RmlUiOpenGlSmoke`, `Shell.Window`, GLFW, desktop input and desktop services out. Ship Read/TryCreateLaunch already recheck exact dock/fingerprint, existing navigation unlocks and a freshly deserialized save. UI final admission is `StartScreen.RmlUiLaunchCampaignShip(plan,dock,fingerprint)`. Ending consumes only the receipt actually displayed. Menu `CampaignAudioSession` is epoch-fenced but must start/tick/dispose on its existing audio owner after scene SFX cleanup and before the next scene begins; audit Android music initialization before offering it, without another output device.

The reviewed Offline implementation now passes the existing admitted `LauncherPrefs.LastColor` to the human player, matching desktop Offline. Bots/network retain their prior colors. `BuildAdventure` human recolor 0 remains outside this Offline-only change.

## Editable fields / IME seam

The current GameView editor is chat-only `InputTypes.Null` and a non-editor `BaseInputConnection`; native foreground keys are consumed, and native routes offer no text fields. The queue already copies 4,096-unit bounded text plus document/focus/surface generations; the native bridge supports composition, text-focus/candidate bounds and UTF-16 selection queries. It does not currently expose arbitrary selection mutation, surrounding-text snapshots, composing-region replacement or range deletion, so a full Android editor cannot be claimed by forwarding character keys.

A later `GameView.RmlUiIme` partial should create a real InputConnection for the exact copied native focus witness, enqueue all mutations to EGL, and publish only bounded detached editor snapshots/acks back to UI. Each connection stays bound to its original field/epoch; it cannot borrow a fresh witness for delayed callbacks. EGL remains the sole text authority. Connection reads use the last acknowledged snapshot without blocking UI on EGL. Protected fields expose no surrounding plaintext or diagnostics. UI calls restartInput/updateSelection and positions CursorAnchorInfo using native framebuffer-to-View/screen conversion once; layout/focus/modal/background changes cancel the old connection and pending composition.

Before enabling text controls, add bounded scope-checked native editing operations for selection/range replacement/deletion and non-protected surrounding text, with a native ABI revision owned by the central native owner. Android distinguishes UTF-16-unit deletion from code-point deletion, and composition commits must suppress duplicate character delivery; use the platform's documented contracts, not repeated synthetic backspaces as a substitute. [Android InputConnection](https://developer.android.com/reference/android/view/inputmethod/InputConnection), [CursorAnchorInfo](https://developer.android.com/reference/android/view/inputmethod/CursorAnchorInfo).

Test real source adapters with stale connection/field/modal/surface generations, batch edits, supplementary characters, combining scripts, composition replacement/finish/cancel, cursor/selection, protected text, deletion boundaries, overflow, resize and rejected dispatch. Real Android compilation plus actual IMEs, candidate positioning, inset/rotation behavior and accessibility remain separate acceptance gates. Adventure save selection itself requires none of this editable-text work.
