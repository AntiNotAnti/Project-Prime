# CW-05h evidence — launcher and offline availability

Base `c2fdfdd5602593ab97a7e054a7ab5be0040d0086`, latest fetched main at
qualification. [Contract](../05h-launcher-offline-availability.md).

macOS arm64, .NET10.0.401, `DOTNET_PROCESSOR_COUNT=2`. Final Release build
PASS78.573s, zero warnings/errors. Main SHA256:
`78061fc3d18ea25fd039608fc2bc4251092292e7ad2af4606ddf419430ebfbf0`.
Sources were frozen during compilation; tools use `BuildProjectReferences=false`.
Main/copied runtime digests match before/after each final command. No process
mapped a DLL while that DLL was replaced. Commands/raw exits/hashes/logs are in
`artifacts/custom-weapons/CW-05h/`.

| Final acceptance | Result | Metadata |
| --- | --- | --- |
| Pure custom, settings JSON, rule DTO/CAS, admission | PASS14807 | 05h-final-custom-results.json |
| Actual offline availability + keyboard/bot contact/respawn | PASS18 | same |
| Actual hidden RenderWindow + MatchStart.Begin + LoadScene | PASS17 | same |
| Scope/local/offline/public client scenes | PASS37/28/46/24 | same |
| Native selection/client projection/replica | PASS180/145/173 | same |
| Readiness/admission/source/authority/rewind | PASS127/249/318/1014/156 | same |
| Focused synthetic controller | PASS75 | same |
| Actual NetSession/native isolation UDP | PASS75/680 | 05h-final-nettest-results.json |
| Admitted native timed shot/Imperialist impact | PASS343/949 | same |
| Architecture, protocol, replay, movement, pickups, target, retirement, chat, reliable | All14 commands PASS | same |
| RmlUi runtime/offline rules/live menu | All3 build/test pairs PASS | ui-reviewed-results.json |
| Actual Avalonia Rules and refused Start callbacks | PASS20;9 PNGs and geometry records | ui-reviewed-results.json, final-loadout-ui/loadout-ui-evidence.json |
| Packet registry/native weapon tables | PASS78 unchanged old names/IDs; native tuning unchanged | final-ancillary-ancillary-results.json |
| Native fidelity | Known46/47; raw exit1 | same |

All17 custom commands and19 nettest commands pass. Pure persistence uses an
isolated actual settings file and checks old/default settings, successful Create,
frozen rematch, stale drafts, failed-write rule rollback, unrelated fields, and
invalid rules/recording refusing before preferences change.

The actual window fixture calls the production Begin path, observes LoadScene
completion, waits for real native spawn/fixed steps, and checks4/16 unselected
inventory. A frozen false plan stays entirely native even when mutable settings
are true. Unsupported flagged Adventure/demo/training/none and active recording
refuse before touching a null window. A declared preseeded bot-slot0 fixture
loads and then fails activation; lifetime diagnostics show Loaded, CleanupStarted,
CleanupComplete and VisualResourcesReleased, with window/runtime/registry empty.
The same window subsequently launches a fresh native scene successfully.
This is a real hidden desktop GL window with headless simulation, not a rendered
gameplay or launcher-click test.

The availability scene uses real assets, native spawn/death/respawn, physical
input sampling fed synthetic keyboard state, and a declared collision target.
It grants only the captured human, refuses another live human and the bot,
spends one M7 round and pays65 through canonical damage. Bot AI qualification is
not implied. Respawn restores4/16 unselected without reviving old state/flights.

The UI harness constructs actual production controls with declared staged
settings/session facts and invokes their actual open/start callbacks. Both
off/on Rules states are captured at940x528 and360x800. Desktop M7 toggles and
help are visible and contained. The inherited full-shell scaling makes the
portrait captures too small for practical touch reading; containment is not
phone usability acceptance. This remains a CW-05 platform gate. The real saved-
M7/Survival Start callback displays the specific Battle requirement, emits no
launch event, and preserves isolated preference bytes and in-memory preferences.
No native RmlUi rendering, UDP or physical click/device claim is made.

Review caught and corrected Begin's Adventure/demo early return and the internal
public Grant(otherHuman) gap before final qualification. The launcher callers
now catch refused Preview/Create and validate before persistence. Two earlier
menu test failures were stale fixtures: index14 became a valid M7 option, and
Traversal's integrated profile is V2 while the old assertion expectedV1. Raw
failed logs remain; final results above use the corrected tests and final DLL.
The initial candidate DLL `6a2c148b0dad4145de6e7c25844be7667da841886e1f33bd954158aaebd1cc96`
also passed16 custom and19 nettest commands; it is not relabeled as final.
The reviewed f552 candidate passed all17 custom and19 nettest commands before a
final presentation-only change removed an internal argument name from M7 refusal
text. All listed checks and9 captures were rerun with the final78061 DLL. The
first final UI orchestration script had a bracket typo and ran no test; its
corrected run passes all4 build/test pairs, including capture with zero warnings.

Native fidelity retains exactly `weapon.missile.splash`, tick177,
`victim.position4096.z` expected52814/actual52815. The Imperialist headshot F2 hash
is still `6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
No gameplay tolerance or baseline changed.

Experimental Android RmlUi asynchronous settings, accessible in-game selection,
physical Android/controller, WAN and custom replay acceptance remain pending.
CW-05 and the full CW-16 program are not complete. CI/review are neither awaited
nor reported passing, as requested by the user.
