# RmlUi Mac qualification: RML-58 through RML-68

The user requested macOS validation only for this continuation. Windows, Linux
and physical Android runtime qualification are deferred. CPP-01 remains
**COMPLETE — SKIPPED**. Neither choice certifies the unresolved AGX findings.

## Closed RML-58 fixture slice

The immutable composed receipt is
`artifacts/rmlui-checkpoint-rml58-final-20261017/composed-final58-acceptance.json`
(SHA-256 `3488b7f66a72c98393d4f16bfd652d4bf83ef892680a25cef16156d3a63066f8`).
It records 15/15 native GL fixture passes at actual 640×360, 1280×720 and
1920×1080: Studio editor, Party, queue, replay playback and Adventure saves.
Headless Studio ownership passed 259 checks; replay files passed 38. Studio's
transition notices and full file paths were visually inspected. These are
bounded page/owner fixtures, not full application, performance or service
acceptance.

All runs use production client
`b64c20891de93a6db4e9bbd0ed0b045c96029796ef030c1cdc3e02d4e1961aa3`,
native bridge
`7eca5c7e375012a27f49e8156ca04480c813ca9a53e970c12c6c2359437937bf`
and the same 110 authored UI assets. The original failed Adventure receipt is
preserved. Its deferred-focus production fix was followed by a separate,
bounded fixture correction accepting the known label child inside the exact
slot button. Action, document, slot, duplicate and save assertions remain.

Nine actual compiled-engine/network checks also passed, including lifecycle,
rotation, real UDP load, display-pose and Android scene ownership on the Mac
headless engine. The actual Party control-plane loopback passed 650 checks
against the compiled transport and dedicated-server owners, with closed HTTP
services. This does not establish live backend deployment or a fully applied
remote client world.

## RML-59 candidate and ongoing qualification

`artifacts/rmlui-checkpoint-rml59-qualified-20261017` freezes 1,646 production
source inputs, the server/client builds, all compiled companions and 114 UI
assets. Both builds passed with zero warnings/errors. The client is
`4cac5d8c2ed5b34706999be99998ebdcaaae4593c7e189a6d5172542c1416019`;
the native bridge is
`9a1b53697ccd9e849b9a5caf4d274b26960b2b9a2a7374ac660fd23ed9ffd0fe`.
The frozen GL fixture executable is
`f0a64754ea59deb4c4c03bd5be6416f07408d10f8d805f17dda8014f3ce553d7`.

This adds native replay event/highlight/reel/analytics editing through the
existing player, explicit library-to-editor launch, Studio material/asset
editing through its existing document/history, and cancellable Adventure save
work through the existing owners. Historical action prefixes, the 40-byte
native intent ABI and protocol 57 remain unchanged.

Headless Studio ownership passed 347 checks and replay files passed 38. The
first 640×360 Studio GL run failed its final combined save assertion; its log
and screenshots remain intact. The resource fixture declared image assets
without creating their bytes. The unchanged actual writer reproduced that
failure and saved successfully after those private fixture files were supplied.
The corrected Studio run passed 968 checks at each of the three sizes.

The composed RML-59 receipt records 15 passing GL runs and one local-capability
Adventure failure, with its two larger cases unrun. That failure exposed a real
page-routing bug: Home-only visibility controls were applied to other documents.
The explicit per-document correction passed the subsequent RML-60 asynchronous
Adventure fixture at all three sizes (120 checks each). RML-60 playback,
including the native Editor, and synchronous Adventure also passed all three.
RML-60 Studio modeling remains unqualified because its initial semantic fixture
assertion depended on compact-layout-hidden text; the next candidate tests the
always-visible project heading by exact ID and records diagnostics first.

## Full-world findings remain separate

Actual asset-backed dedicated-server runs completed six consecutive maps for
2/4/8 human cohorts, bots and a spectator using the real 60 Hz loop and normal
intermission. They exposed missing scene cleanup during authority retirement.
The original evidence remains under `artifacts/cpp05-actual-world-rml58`. The
separate correction passed 83 actual-asset regressions, including authority
publication failure before Scene construction. A subsequent normal six-map run
passed 158 checks: all seven loaded world generations completed cleanup, visual
release and eligible facade release, and all six replay archives finalized
healthy. This remains distinct from fully applied remote client/League checks.

The actual world Draw pilot passed Samus at 60 output frames per second. At
144, a zero-simulation-step draw changed an existing muzzle-effect transform
in the established replay presentation hash. The first 379 simulation steps
and prepared/consumed inputs match the 60-rate prefix. This is a failing
presentation-ownership gate, not a tolerance adjustment or accepted result.
The original-build comparison reproduced the same muzzle-anchor mutation.
RML-60's narrow animation-aware isolation correction passed both 60 and 144
strict per-run gates and produced identical 1,440-step canonical streams. The
paired-input gate initially found a one-field first-focus warmup difference.
The fixture's neutral first warmup input then passed all 1,440 exact input and
canonical-step comparisons at 60/144, without exclusions or normalization.
Cross-camera transactions also retain a separately recorded emitter-ownership
gap; no full seven-Hunter or camera parity claim follows from the pilot.

The actual map-cache/maintenance regression passed 12 checks in separate default
and isolated processes. An explicit user-data override now controls the shared
map scheduler's cache, and maintenance uses that owner's captured root even if
the environment changes later. Default install paths remain the same; alternate
cache and save sentinels were preserved. This makes later native System cleanup
validation possible in a private profile; full Settings integration remains last.

## Later focused qualification

RML-61's native Studio and Replay Camera Tools passed all three framebuffer
sizes, but Studio's UV labels were clipped in compact layouts. Its failure
receipt remains intact. RML-62 fixes that layout and passes 1,986 Studio checks
at each size, including all thirteen UV actions, complete button labels and
contained, reachable controls. Playback, Editor, Camera Tools and export also
pass all three sizes. The exact RML-62 UI receipt is
`artifacts/rmlui-checkpoint-rml62-candidate-20261017/acceptance.json`, SHA-256
`924df6ef74508ea04200f344867910c25569ca873082df28a3830a8e3a95fe59`.
It deliberately remains conditional on its failed strict world-camera gate.

RML-63 corrects a main-actor test which accidentally depended on camera mode.
The strict Samus 60/144 pair then passes every prepared/consumed input and both
canonical hashes across all 1,440 steps, plus all thirteen camera transactions
with no named entry/exit difference. The complete streams also match the
preceding neutral RML-60 pilot. Its scoped receipt is
`artifacts/cpp03-mac-world-pose-pilot-20261017/strict-camera-rml63-pass-receipt.json`,
SHA-256 `9d4fa14466374b0f8c978c7973921e8953fcfe72faa77ba4799636d5141bb457`.
This is two rates and one Hunter; the broader suite and positive native pixel
controls remain separate. The 69-check compiled display-pose regression also
preserves the exact historical replay schema and field stream.

An actual RML-63 single-player scene loads Celestial Archives with 26 entities,
steps 90 times, then saves and reloads 38,460 identical bytes through the real
save owners. Source asset identity remains unchanged. The receipt is under
`artifacts/rmlui-checkpoint-rml63-candidate-20261017/root-headless-runs/campaign-native`.
Discoveries, ship boarding, movies, audio, boss completion and a full campaign
playthrough are not established by that room/save check.

The fully applied native-client RML-62 pilot exposed a delayed hit claim dropped
after InMatch ended. RML-64 admits only terminal acknowledgments or refusals
through the existing claim owner during PostMatch. Its 252-check actual compiled
boundary regression passes, including paid/cached outcomes, lifecycle/conflict
guards and no post-finalization damage, head correction or score changes.
The original failed UDP/world pilot remains preserved; its fresh successor is
still being qualified. RML-64 also adds strict collision triangle closure and
compiler-authored route reports for private Echoes imports. These are local
validation improvements, not proof of real imported arena or online readiness.

Private native text-editing work has now passed 65 actual CPU bridge/owner
checks after a focus-dispatch ordering correction. The original failed run and
numeric diagnosis remain intact. Production promotion, actual AppKit messages
and physical IME/candidate behavior remain separate input gates.

## RML-65 Studio, input and real replay-window findings

RML-65 client
`91c74fec1ed146d14a87d7bb05e40d3b59b2eb65a04aa4e5ae258f34e7123d2e`
and server build with zero warnings/errors. The native bridge is
`830b5ffdbdfbe7559b6ab97197403059d34d34b8ec7817fd77b688fa7f580be5`.
Studio's ten asset IO actions pass 648 retained-owner checks and
2,530/2,558/2,546 native GL checks at actual 640×360, 1280×720 and 1920×1080.
All 316 frozen companions remain unchanged, and 159 captures are hashed.
`artifacts/rmlui-checkpoint-rml65-candidate-20261017/studio-guid-corrected-runs/acceptance.json`
records exact IDs, private generated files, overwrite review/recovery, cancel,
retirement, undo, save and reopen. The original fixture failure using the last
material after GUID-sorted save is preserved; its correction changes only the
fixture's imported-material lookup. OS file dialogs, physical input, full editor
parity and performance are not established by these checks.

The production native text-edit bridge passes 65 CPU checks, 20 native/Host
lifecycle checks and 49 actual macOS AppKit programmatic callback checks. The
AppKit receipt is under the candidate's `text-input-runs`. Physical IME and
candidate-server interaction remain unqualified. The optional text request and
state layouts are 80/88 bytes; existing native action and legacy composition
layouts remain unchanged. Studio actions total 200, Replay actions 135, and the
candidate contains 121 authored UI assets.

The real replay-window harness passes normal launcher startup and reaches the
actual playing world at client 640×360 / framebuffer 1280×720. It then fails:
the native playback page closes when `RenderWindow.Scene` changes from the
canonical shell to the actual replay replica. The same exact-scene guard would
also reject a seek-driven replica replacement. The failure and earlier harness
bootstrap/dependency failures remain in `runtime-runs`; the exact diagnosis is
`replay-window-packed-first/integration-diagnosis.json`. Timeline source checks
do not convert this failed application integration into a pass. A repair must
retain the exact window and replay request, accept only that request's own
presentation replicas, and preserve stale context/transport retirement.

The explicit private Mac source-rig path passes 904 assertions in the actual
compiled character asset check plus the existing suite. GPU capture, real
imported animation/cannon/form readiness and network/replay identity are
separate gates. Full Settings remains private and last; its four review fixes
are undergoing independent regression review.

## RML-66 reliable ACK retention and applied clients

RML-66 client
`40c32596c4ab007045d51e79dfcdb7148e8db74bb5d8d70fb10b2541334f6dc2`
and server
`6a3f6bd1c80ce28293305b92fa3edad7aa5f0ca0c861444d69d68f264d8a6124`
build with zero warnings/errors. Only three production network sources change
from RML-65; the native bridge and UI assets remain identical. The reliable
event retains bounded ACK correlations independently of the packet telemetry
ring, whose 512-slot reuse caused the original 750 ms failure. Existing pending
capacity, timeout, protocol and transport owners remain unchanged.

`artifacts/cpp05-reliable-attempt-owner/acceptance66.json` records 514 actual
compiled-owner checks and 135 additional physical UDP checks. The 65th retained
receipt is suppressed before dispatch and reports one existing connection
failure. The fresh eight-client, 750 ms native-AI run strictly passes with
61,168 health comparisons and zero mismatches, all nine process exits zero,
unchanged assets and twelve finalized archives fully read by the production
reader. Its receipt is under
`artifacts/cpp05-applied-client-world-rml66/prime-cpp05-applied-matrix66-c8-r750-86takgxu`.
The original RML-64 failure remains preserved. The subsequent four-client
150 ms and eight-client 250/350 ms regression arms also strictly pass. The
composed `artifacts/cpp05-applied-client-world-rml66/matrix-acceptance.json`
records 199,574 health comparisons with zero mismatches, 28 client worlds plus
four authorities, 44 healthy finalized/full-read archives, zero nonzero process
exits and no automatic retries. Seventeen accepted turret sources retire before
emission under the existing exact authority-lifetime policy; the receipt lists
each exclusion rather than treating it as an emitted shot. Targeted alternate
forms remain separate: the first Weavel scenario passes base parity but fails
the strict form auditor because recording replicas append observer diagnostics
to live evidence. That original failure is preserved while a live-only evidence
guard is prepared. Neither matrix certifies League or physical-client rendering.

The frozen RML-66 integration tool also passes 69 display-pose/schema checks,
architecture and historical byte fixtures, compact intent/replay compatibility,
43 replay-form history assertions and the existing impaired reliable suite.
All 415 frozen tool companions remain unchanged. Logs and exact command arrays
are in the candidate's `root-headless-runs/results.json`; no full Draw or device
acceptance follows from these content-free checks.

No source assets, credentials, user settings or saves are included in these
receipts or the delivered RML-57 Mac application. Full Settings integration,
remaining Studio/Replay tools, input parity, imported-character/map readiness
and final release qualification remain open. Original-game numerical accuracy
also still requires genuine original-game traces.

## RML-67 and RML-68 actual replay-window continuation

RML-67 client
`c00c2ae7668df69157332bf8c51de014c3154411ca86b041d1dd5d47812ceeba`
fixes replay-overlay ownership using the existing window, canonical scene and
replay request. Exact old transport/camera contexts still retire on seek.
The same checkpoint adds the live-only form-diagnostic guard; 27 enabled and
23 disabled actual-owner checks pass. Client and server build with zero
warnings/errors. Protocol 57 and the historical schema remain unchanged.

Strict native preflight first rejected stale inherited bridge copies before
launch. Those outputs and their manifests remain preserved. New qualified
client/tool directories explicitly compose native bridge
`830b5ffdbdfbe7559b6ab97197403059d34d34b8ec7817fd77b688fa7f580be5`;
`native-composition-correction.json` records the sole-file correction. A
tool-only macOS dyld check now attests the actual loaded bridge path and its
independently pinned frozen file hash, both after startup and before shutdown.
This hashes the loaded path's file, not mapped memory. It makes no historical
loader claim for earlier processes.

The first attested RML-67 window passes 23 steps, including actual playback,
bookmark persistence, camera-key creation and Camera Tools. Timeline opening
then fails: the managed rectangle formatter emits spaces while the native
parser requires commas. The original mock parser accepted spaces and concealed
that integration defect. Original exit 1 and all runtime artifacts are retained.

RML-68 changes only that managed formatter. Client
`654f2a4674153d2926df2889730491cc92971c5fb4ab8ac4712c89546d271966`
and GL tool
`f4297d4402cad495e033f3eed699dbb314bb31884dc57493ea173b708005693e`
build cleanly and explicitly compose the same bridge. The unchanged RML-67
server is reused with an explicit source-exclusion receipt, not reported as a
new server build. Twenty-eight actual native CPU/DOM checks cover the compiled
formatter, culture invariance, exact bounds, old-format rejection and malformed
rectangle rejection; 65 presenter and 11 root checks also pass.

The actual RML-68 compact window (640×360 client, 1280×720 framebuffer) passes
28 steps and attests the exact bridge. Timeline opening and a successful world
picture now pass. The first exact seek then fails: native focus scrolls the Go
button into view and the camera lane offscreen, while the common render ticket
requires the entire camera lane onscreen for every action. Input was sent while
OS focus was true; later focus loss during the timeout is not the initial cause.
The process exits 1, closes the window, removes observers and preserves all
316 companion hashes, source archive and assets. Larger sizes were not run.
See `artifacts/rmlui-checkpoint-rml68-candidate-20261018/runtime-runs/replay-window-attested-first`.
A private correction separates visible semantic-control bounds from the stronger
camera pointer/drag bounds within the same existing successful-presentation
ticket; it is not yet promoted or runtime-qualified.

Separately, all 24 actual synthetic source-rig GPU captures and the existing
native character acceptance suite pass against the frozen RML-65 client with
bridge 830b. The source-rig receipt and exact private-entry composition are in
`artifacts/rmlui-checkpoint-rml65-candidate-20261017/character-source-rig-gpu-run`.
This does not qualify real imported art or animation. Game Files/setup remains
a legacy route boundary. Full Settings remains private and last; its reviewed
262-check baseline is preserved while HUD input, unconditional video deadline
and queued window-mode recovery wiring are repaired before final integration.

## RML-69 and RML-70 continuation (October 18)

RML-69 client `54f2ec60caca3573e876b9183942e3689d1ed89fe34c7ff160c97b1325047e40`
separates semantic-control bounds from camera pointer/drag bounds within the
existing successful-presentation ticket. Its actual attested replay window
passes 72 steps, including exact world seeks, saved camera keys, 120 paused
swaps and native marker actions. Drag then fails because focusing the canvas
scrolls its geometry. Exit 1 and all original evidence remain preserved.

RML-70 client `f696116d4d541ded5228c9af31a2148948bbe14f3a5cd1483131f1c7adb07e93`
adds the established `data-focus-scroll="none"` policy to that canvas. Native
bridge `9174d430969ac3ed937e2e491e756e39cd3c00d935b88f287212acbf4a700f97`
also projects accessibility bounds and effective ancestor clips into the same
framebuffer coordinates. The unchanged strict fixtures pass 402 checks against
the actual central GL2 bridge; these are native CPU/DOM checks, not GL draws.
Client, server and GL tool build cleanly after one retained MSBuild child-node
failure. The successful client retry disables shared compilation and uses one
build node; no source workaround was introduced.

The first actual RML-70 replay run stops after 58 completed steps when OS focus
is lost during the paused-picture gate. A separately hashed tool-only variant
allows one genuine Focus call plus explicit Timeline Refresh only inside that
gate, resets its counter and still requires 120 uninterrupted current swaps.
Its single actual run instead loses focus as replay opens, before recovery is
eligible, and stops at Timeline readiness after 23 completed steps. Those
Host-injected actions occurred while the OS window was backgrounded and do not
qualify foreground interaction. Neither run passes the whole scenario. Both
attest the exact loaded bridge, exit 1, close normally, remove handlers and
preserve the archive, assets and all 316 companions. The causes of OS focus
loss are under separate foreground/window diagnostics; guards remain intact.

RML-70's frozen headless tools pass all eight import, display-pose/schema,
architecture, bomb protocol, six-transition match-rotation, compact-intent,
replay-form and impaired-reliable cases. Exact commands, logs and unchanged
companion receipts are under `artifacts/rmlui-checkpoint-rml70-candidate-20261018/root-headless-runs`.
These checks do not close the actual replay-window failure.

Separately, original ScanLog data identified nine off-by-one starter equipment
IDs. RML-70 corrects only fresh StorySave initialization. Its actual DLL passes
41 three-slot lifecycle checks with zero starting scans/nine equipment entries,
byte-exact load/resave and guarded copy/archive/cancel/stale-command behavior.
That CPU lifecycle result does not establish visible save UI or a full campaign.

Native Game Files v2 and full Settings remain private. The Settings draft passes
379 checks across 24 fresh processes; composed production-owner compilation and
actual window/input/rollback acceptance remain required, with Settings last.
