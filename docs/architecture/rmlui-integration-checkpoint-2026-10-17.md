# RmlUi integration checkpoint — October 17, 2026

This is local implementation evidence on a shared dirty checkout based on
`dc7f097067d755ec76d145fc7cc333068566863d`. It is not clean-head, live-service,
physical-device, full-world rendering or release acceptance. CPP-01 remains
**COMPLETE — SKIPPED by user direction**, without a graphics/device pass.
The user's parallel-work instruction permits independent implementation;
new product UI uses RmlUi. Full Settings remains the final migration slice.

## Compiled checkpoint

The central server build passed with zero warnings/errors. The restored opt-in
desktop client passed with zero errors and four existing nullable warnings.
The Android experiment build, including native Pause/Journal route source,
passed with zero errors and 39 existing SDK/JDK/XML warnings. The Android
package flag was omitted, so that result establishes host/API/AOT compilation,
not native resource packaging or runtime operation. No device was connected.

| Artifact | SHA-256 / evidence |
| --- | --- |
| Desktop client | `2b09acc5738eb4f16c4c544fae129eaf3fcf30f9312fa47c416ec60bdc7d7d23` |
| macOS arm64 GL2 native bridge | `c262a76907a4bf5ff987df317c4a6364b47fff6e912d13af92ce42dd3a07e633` |
| Immutable copied tool, dependencies and 101 RML/RCSS assets | `artifacts/rmlui-checkpoint-20261017-security-studio/manifest.json` |
| Build logs | `/tmp/prime-rml56-server-build.log`, `/tmp/prime-rml56-client-build.log`, `/tmp/prime-rml56-android-pause-build.log`, `/tmp/prime-rml56-native-build.log` |

This native bridge includes Replay actions 0–47 and Studio actions 0–30.
Later source additions are outside this compiled checkpoint.

## Native fixture results and visual failure

All 18 synthetic native runs passed their functional assertions. Each mode ran
at actual framebuffers 640×360, 1280×720 and 1920×1080; macOS requested client
dimensions were half those values. Headless replay-file acceptance passed
38 checks. The copied tool used fake authenticated services and synthetic
recordings/projects, without real accounts, HTTP, backend writes or user saves.

| Mode | Checks per size, excluding common host checks |
| --- | --- |
| License | 31 production-adapter checks and 325 native checks |
| Party | 23 Presence adapter, 8 Party adapter, 8 Party lease adapter and 200 native checks |
| Playback | 41 transport, 72 bookmark, 75 range/materialization and 146 camera checks |
| Community history/lifecycle | 430 native checks |
| Studio project workspace | 86 native checks |
| In-game/Journal | 258 native checks; new local/replay/fullscreen capability branches await the next fixture build |

**Visual acceptance failed at 640×360:** the new Community lifecycle button
rows overlap and clip horizontally. Functional assertions did not detect this.
The exact failed capture is
`artifacts/rmlui-checkpoint-20261017-security-studio/artifacts/rmlui-community-history-rml51/synthetic-lifecycle-last-active-expired-restore-640x360.png`.
The following source cycle fixes the layout and adds containment, disjoint
button bounds, real wheel reachability and visible-footer assertions. Its
result remains pending until the new binary/assets run.

The aggregate explicitly reports `FUNCTIONAL_PASS_VISUAL_FAIL_PENDING_FIX`.
Its 527 capture/evidence files have recorded dimensions and hashes. The first
three License runs reused 102 older fixed-path capture/evidence files before
the runner moved into its isolated checkpoint directory. The exact replacements
are listed in `capture-provenance-notes.json`; prior logs/summaries and the older
copied tool remain, but no restoration of those older images is claimed.

## Additional regression evidence

The retained editor suite passed 486 checks in isolated temporary user data.
Display-pose integration passed 69 checks and preserved archival schema
`A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A` and exact
legacy field stream SHA-256
`64a16aac610f36c686ae6055fdfde3d2c94819fd1e1cddd7457cabf9138ae4f1`.
The source-linked camera suite passed 34 controller and 33 actual matrix/cache
checks. Its presentation cache uses external weak storage and promotes a pose
only after successful buffer swap, without new archived Scene fields.

Subsequent source-linked Party connection coordination passed 16 assertions,
including stale/cancelled admission disposal and validation → existing-owner
acceptance → joined acknowledgment ordering. These compile the changed
controller/contracts against stable dependency binaries; they do not replace
the next full client/server build or actual UDP/backend acceptance.

## Following source cycle

Independent owners are implementing native editable Studio, Community publish
with account-bound durable upload retries, verified Party membership/travel,
configured-local Android Home/Offline, and native video export through existing
owners. These are distinct from the compiled evidence above. Backend deployment,
group seat allocation, advanced editor parity, full Android routes, full Settings,
hardware/input qualification and clean-head CI remain open.
