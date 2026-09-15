# Android device acceptance

This document separates what the repository can prove in CI from what needs a
real Android device and authorized game content. A status marked `AUTOMATED`
is reproducible from the repository. `EMULATOR` means the protected or release
APK was installed and exercised on an Android emulator. `PHYSICAL` requires a
phone or tablet. `OPEN` means that no current evidence exists yet.

## Required run metadata

Record this with every acceptance run:

| Field | Value |
| --- | --- |
| Date, commit, and build variant | UTC timestamp, exact Git SHA, Debug/Release/protected |
| Device | manufacturer, model, serial alias, Android version, API level, ABI |
| Display | resolution, density, refresh rate, orientation, cutout/insets |
| Graphics | GPU, driver/version, renderer/backend, Vulkan/OpenGL ES version |
| Runtime | .NET SDK/workload, Android SDK/build-tools, host OS and architecture |
| Artifact | APK filename, SHA-256, signer/keystore identity, package version |
| Content | `paths.txt` and authorized content identity/hash, or `content-free` |
| Map/replay input | map identity/fingerprint, replay identity/version, if exercised |
| Harness | adb/emulator options, frame-rate cap, renderer settings, log artifact paths |

Never put proprietary game files or credentials in the repository or CI
artifacts. Redact serial numbers and account/session data in shared reports.

## Evidence labels

Use one evidence label for each artifact. A stronger label does not erase the
limits of a weaker one, and no label below is an automatic release decision.

| Label | Establishes | Does not establish |
| --- | --- | --- |
| `SOURCE` | Current call paths, ownership, responsive-layout rules, and explicit exclusions in the tree | That a native UI, GPU, device, or Internet path renders correctly |
| `UNIT` | Focused assertions over input, state, codec, and presentation seams | Physical touch/controller/stylus behavior, GPU output, or WAN delivery |
| `HEADLESS` | Deterministic content/simulation or server/client harness behavior without a native window | Native rendering, Android lifecycle, human ergonomics, or a public Internet path |
| `RENDERED` | A native renderer produced the captured frame on the host named in the run metadata | A physical Android device, high-refresh display, or real-WAN path |
| `DEVICE` | The exact Android APK, hardware, display, input peripherals, and lifecycle run listed in the metadata | Another device, a deployed service, or geographic WAN behavior |
| `WAN` | Two independent endpoints, verified geography/path, Node admission, direct Worker UDP, and the recorded network metrics | Human visual quality unless separately reviewed |
| `HUMAN` | A reviewer inspected the exact captures and recorded visual conclusions | Source, protocol, authority, or transport facts that were not captured |

An emulator remains `EMULATOR` evidence inside the device workflow, not
`DEVICE` evidence. A local rendered capture remains `RENDERED` even when the
same code is later installed on Android. Keep source, unit, headless, rendered,
device, WAN, and human results in separate rows of the run record.

## Viewport and route capture matrix

Capture the same route set at every supported size before adding a new
height-specific breakpoint. The existing responsive classes remain
`Mobile`, `Compact`, `Medium`, and `Wide`; a short landscape height is a
measured modifier, not a replacement classification.

| Fixture | Viewport and orientation | Required device variation | Required capture set |
| --- | --- | --- | --- |
| `phone-short` | `830 x 390`, landscape | Phone with a display cutout and gesture/navigation inset where available | Every route below; include the keyboard-open state on text routes |
| `phone-standard` | `960 x 540`, landscape | 60 Hz and 120 Hz phone runs | Every route below plus frame-pacing metadata |
| `tablet-landscape` | `1280 x 720`, landscape | Android tablet at its native density | Every route below plus map preview and scroll states |
| `tablet-tall` | `1280 x 800`, landscape; portrait only when supported | Tablet with system bars visible at least once | Every route below plus rotation/surface recreation |
| `large-display` | `1920 x 1080`, landscape | External/high-density display only when the device supports it | Every route below; retain the actual display mode in metadata |

For each fixture, record the effective content rectangle after system-bar and
cutout insets, the responsive class, orientation, density, and whether a
software keyboard is present. A capture is not complete if a footer, dialog, or
selected row is hidden outside that rectangle.

Required routes and route-specific checks:

| Route | Interaction to exercise | Acceptance evidence |
| --- | --- | --- |
| Gateway | Sign-in/guest entry, focus a text field, submit and cancel | Active field and actions remain visible with the keyboard; no implicit guest fallback or lost selection |
| Home | Navigate to Play, Profile, Maps, and Settings | Primary actions remain reachable at short height and after returning from a child route |
| Play | Open lobby browsing and host entry | Touch focus/pressed state is visible without hover; no clipped action row |
| Lobby browser | Scroll, select a lobby, change selection, open detail | The selected row remains visibly selected; scrolling works without a mouse hover; a useful selected-map preview is retained where practical |
| Lobby detail | Read roster/rules, open map picker, continue/back | Critical actions stay above the safe-area boundary; selected map and preview agree |
| Host lobby | Choose mode/map, ready, start, and back out | Controls are reachable with touch and keyboard; map preview never displaces start/back controls |
| Settings | Change, reset, cancel, and save a setting | Focus/selection is explicit; reduced-height and keyboard states restore after dismissal |
| Profile | Edit a text field, scroll, save/cancel | Active field, submit, and cancel remain visible while the keyboard is open |
| Hunter License | Select a Hunter/cosmetic and navigate back | Selected card/row remains visible and does not depend on hover |
| Maps | Scroll and select a map | Selection and preview update together; list remains usable when preview is compacted |
| Results | Read the terminal result and interact with the existing ballot | Score/result stays readable; offered option selection is visible and submits only the existing option ID/revisions |
| Theatre | Open a replay, pause/step/seek, change perspective, exit | Transport controls remain reachable; playback resumes or exits without stale touch input |
| Map picker | Scroll, select, preview, confirm/cancel | The selected map is visibly distinct; list > critical actions > preview if space is constrained |
| Hunter picker | Scroll, select, confirm/cancel | Touch selection is deterministic and survives redraw/rotation; no hover-only state |

Map previews are subordinate to list usability and critical actions. If a
short phone cannot show all three at once, use a compact or secondary preview,
then preserve the ordering `list > critical actions > map preview`. Do not
silently remove preview state from the selected map.

## Safe-area, keyboard, and touch acceptance

For each route that can display system UI, repeat the capture with:

- gesture navigation and three-button/navigation-bar insets when available;
- a left/right/top display cutout, rounded corners, and system bars visible;
- a software keyboard opened and dismissed on Gateway, Profile, lobby chat,
  and any configuration/text-entry surface.

Acceptance requires the active field to remain visible, submit/cancel to remain
reachable, the page to scroll when usable height is reduced, and dismissal to
restore the pre-keyboard layout without changing the selected route or row.
Touch controls must have a finite, explicit pressed/selected state and a
minimum target appropriate to the device density. Selection may not require a
pointer hover event. A pending touch must be consumed once by its owning
surface and discarded when navigation, pause, scene handoff, or input ownership
changes.

## Physical device and peripheral matrix

Run the matrix on the exact APK/content/build recorded above. `OPEN` is the
required initial state for a not-yet-recorded physical run; it is not a pass.

| Profile | Required configuration | Measure | Status |
| --- | --- | --- | --- |
| 60 Hz phone | Landscape; `830 x 390` and `960 x 540` where supported; cutout/inset variant | Touch navigation, aim/control feel, frame pacing, keyboard, suspend/resume | `OPEN` |
| 120 Hz phone | Landscape at the device's native 120 Hz mode; retain actual mode if Android falls back | Refresh-rate negotiation, frame pacing, touch aim, scene transition, thermal trend | `OPEN` |
| Android tablet | Landscape `1280 x 720` or `1280 x 800`; portrait if supported | Larger touch targets, list/preview composition, rotation/surface loss, sustained rendering | `OPEN` |
| Samsung/S-Pen | Pen identity and pressure enabled only where supported; finger fallback | Stylus selection/aim, pressure threshold, pen-to-finger handoff, no generic pen-jump behavior | `OPEN` |
| Bluetooth controller | Pair before launch and disconnect/reconnect during shell and match | Discovery, button/axis mapping, focus handoff, reconnect, no stuck input | `OPEN` |
| USB controller | Attach through the device-supported adapter/hub | Same mapping and handoff checks as Bluetooth, including attach after launch | `OPEN` |

Record a separate row when the same physical device changes refresh rate,
orientation, controller, or renderer. Do not collapse 60 Hz and 120 Hz into a
single result.

## Suspend, resume, transitions, Results, and replay

Exercise suspend/resume at each checkpoint below. Capture the foreground route,
scene/match identity, input ownership, and logcat around the transition.

| Checkpoint | Action | Required result |
| --- | --- | --- |
| Shell | Background and resume at Gateway, Home, Play, Settings, and Profile | The same route/selection returns; keyboard and focus are either restored coherently or dismissed without clipping |
| Lobby | Suspend in browser, detail, and host lobby; resume before and after a directory refresh | Node/control state is not duplicated; selected lobby/map and critical actions remain valid |
| Handoff | Suspend during launch and just after Worker handoff | At most one scene is created for the admitted match; stale touch/input does not reach the new scene |
| In-match | Lock/unlock or background during Playing, then resume | Renderer/surface recovers; no duplicate input, stale frame, or client-authored gameplay state appears |
| Terminal | Let the match reach Results, suspend, and resume before choosing an option | Immutable result remains readable and the existing ballot state/revision is preserved |
| Continuation | Choose Rematch/Next/Return through the Results surface and observe the next lobby or match | One fresh continuation/handoff occurs; Node control session remains the same where the lifecycle contract requires it |
| Replay | Open Theatre from the shell or Results, pause/step/seek, change camera, suspend/resume, and exit | Replay transport state is deterministic, no live gameplay input leaks into replay, and the shell returns cleanly |
| Surface/rotation | Rotate where supported or force surface loss/recreation | Safe-area recomputation and renderer reinitialization preserve the route or fail with a captured actionable error |

The Results surface is the existing Node-owned post-match ballot described by
`docs/G5_VOTING.md` and `docs/SEAMLESS_ONLINE_FLOW.md`; this acceptance work
does not alter its options, revisions, authority, or wire contract. In-match
voting remains explicitly **DEFERRED** until repeated live
`match -> Results -> ballot -> continuation -> next match` cycles demonstrate
stable handoff, result retention, and reconnect behavior. Do not use an
Android capture or a focused test to advance that decision.

`LagCompensationPolicy.MaxRewindTicks` remains `15`. Revisit it only after the
real geographic WAN matrix has recorded material clamping; device or loopback
results alone are insufficient.

## Critical scenarios and evidence

| Scenario | Required evidence | Current status |
| --- | --- | --- |
| Build the shared Android head and Release APK | `dotnet build/publish` succeeds; signed APK is structurally valid | `AUTOMATED` |
| Install, replace, launch, and collect fatal logs | `tools/protection/android-smoke.py` output, install/replace exit codes, logcat | `EMULATOR` in scheduled/protected workflow |
| Missing-content shell | front screen, Settings/More/back navigation, screenshot, no fatal exception | `EMULATOR` for the content-free shell |
| Content discovery and map cook/cache | authorized content is found; map fingerprint and cooked `.fpmap` are recorded | `OPEN` / content-dependent |
| Offline match and replay playback | enter a map, complete a deterministic match, save/load/replay, record logs and hashes | `OPEN` / content-dependent |
| Touch, controller, and stylus input | tap navigation, hardware/controller mapping, pen identity/pressure where supported | `OPEN` on physical device |
| Lifecycle and display recovery | background/resume, rotation or surface loss, lock/unlock, renderer reinitialization | `OPEN` |
| Audio and sustained rendering | device audio output, no repeated backend errors, target refresh-rate run | `OPEN` |

The emulator run is a launch/shell gate, not evidence that a match, map, replay,
audio device, or physical input path works. Keep those results separate rather
than marking the whole APK accepted from the shell check alone.

## Reproduction outline

1. Publish the exact commit with the Android workload and the declared SDK.
2. Verify the APK signer and SHA-256 before installation.
3. Install with `adb install -r`, capture `adb logcat`, and record the metadata
   table above.
4. Run the content-free shell scenarios, then repeat with authorized content
   copied under `Android/data/com.antinotanti.projectprime/files`.
5. Store screenshots, logs, map/replay identities, and pass/fail status per
   scenario. A missing content or unavailable physical peripheral is `OPEN`,
   not a pass and not an APK build failure.
