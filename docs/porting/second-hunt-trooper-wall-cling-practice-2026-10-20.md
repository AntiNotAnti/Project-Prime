# Explicit offline Trooper wall-cling practice

The existing local wall-cling mechanics were unreachable: no launch owner
called their one-shot rule binder. Offline setup now offers an explicit
MECHANICS choice, TROOPER WALL-CLING. Selecting it captures Weavel, Battle and
zero bots in the existing immutable offline draft/LaunchPlan. It defaults to
Native on each new draft and never derives gameplay from a skin, imported
file, character catalog or source-preview state. Native Hunter values stay
unchanged. The presentation is explicitly native Weavel model/weapons, with
no drone or blaster claim.

The shared OfflineLaunch preview validates before preference writes. Existing
offline preflight also refuses unknown variants, other launch kinds, online or
replay owners, non-Weavel/Battle, bots, Balanced/Enhanced/LowTier/M7 and non-Native
movement. The native page projects current-rule refusal reasons before its
map job; launch revalidates again. After actual local-player creation and before
room initialization, MatchStart binds that exact current player once. A failed
bind disposes the candidate Scene and reports the refusal. Ordinary plans
retain Native behavior. The configured local Android launcher admits the same
explicit control through its existing local launch service. This native Android
owner is conditional on the experimental build; default Android packages
continue to use Avalonia. Packaging/resources and experiment compilation are
qualified separately from runtime activation.

The binder preserves foreground Scene/registry/slot/lifetime/thread and
preinitialization guards, rejects conflicting rules/recorders and never binds
network, replicas, sideScenes or replay labs. Retirement cannot revive an old
capability after facade restoration. A one-way Scene latch excludes recording
and killcams even after the ability retires, because replay setup/state schemas
do not yet carry this mechanics identity. Practice is a playtest, excluding the
ordinary automatic offline rematch. It cannot enter the server career path.

Existing native movement and collision hooks own the mechanics: jump or at
least2 units of fall, inward speed>.02, actual near-vertical face contact and
three height probes;150-frame hold, at most3 clings between standing contacts,
20-frame release wait, .3 outward wall jump and14-frame into-wall steering
suppression. Native jump pads, locks, freeze, forms, death, bots, spectators,
inactive slots and retired owners release/reset the hold. No second movement
controller or external collision representation is added.

## Qualification

The first production freeze492571f48 passed276 source-free actual owner/collision
checks,92 offline plan/draft/intent/replay checks and1,403 initialized AMHE1
Proving Ground checks on Mac. The combined-main20fa89349 core repeats these
results and passes the complete RmlUi runtime suite,732 configured-local
capability/admission checks and the Android local launch-witness suite.

The main608-tick keyboard schedule produces two clings, one wall kick, one
exact150-step timeout and zero hold drift. Native Weavel replays the same
schedule with exact trajectory parity before the first cling transition and
never clings. The separate no-jump fall arm uses an explicitly synthetic native
Spawn placement at an observed real wall+2.5Y; both rules fall2.0856323 units
in22 native steps, and only Trooper clings. This is CPU simulation acceptance,
without drawing or imported-model playability. Actual native AMHE1 input data
remains unchanged across these runs (3,970 files/845,958,857bytes, inventory
`ef956fba92e6e2d4f2f3ae1d85d02b5b750592daad6dc82f35f1ba5968db0a92`).

Actual native UI acceptance found and fixed the C++ action parser's old16-choice
limit: choice16 now emits the same literal kind140/argument16 as managed
validation. Native ABI1 stays unchanged. All three bridges are freshly rebuilt
from pinned RmlUi6.3/FreeType sources, verified against the16-file closure
fingerprint `65eb5d70c119715a3c941aa68792512d53adf760bee7bc8980fe227fd8403ec9`.
The UI run also found Training off-screen beside the full-width Battle panel;
two visibility bindings now project only the active activity panel.

Final desktop source d4b382431 passes Release with one inherited CS0649 warning
and zero errors. Actual native GL UI passes156 checks/17 translated intents
at **each**960x600 and640x480 against core
`eec218e34399a8451919bf2100fc539061676855f0312a83465d54a80c12e98d`.
It covers real selection/labels/notice/start, captured practice requests,
current-rule and missing-snapshot refusals before jobs, Native return,
training13/14/15, modal/retired/duplicate input fences, unchanged prefs/Scene
and GL-clean teardown. The source-linked Host runtime and all732 configured-local
Android capability checks also pass at this exact d4 source. Services supply
invented local values and do not launch
a real match; the separate owned-arena run exercises the actual plan binder
and initialized simulation. No protected captures are retained.

Dedicated Mac fallback passes at01ae65cc3 with one inherited CS0649/zero errors,
core `da3f36b010c3c4867c6502ffc0a534df6b53e560468f8b265acdc9ec98a2dcf2`.
The sole later managed source change is RmlUiMenuPages.cs, explicitly removed
by MphReadServer=true; this is not a claim of a newly built d4 server binary.

Windows primary testing at492571f48 passes desktop/dedicated/tool builds and
276 owner assertions. Its wrapper then encounters a diagnostic-log Unicode
decoding error; raw logs remain intact. The new20fa11-command run verifies
both complete6,292-file source trees before execution, but SSH transport later
fails with exit255/port22 timeouts. A later single bounded recovery reaches SSH
but authentication is denied before any remote command. Its child results remain
unknown. These
transport/wrapper failures are not counted as game failures or successful
final Windows tests. The authorized Mac fallback completes the checks above;
Windows native bridge/GL qualification is not established.

Retained test/setup failures also include the initial aggregate capability
counts (calibrated+4 with all actual assertions retained), a missing command
in the focused canvas harness, and an ambiguous test-only OpenGL ErrorCode
reference. The first20fa Android Debug build passes26 inherited warnings/zero
errors but contains the earlier native parser; it is compile/package evidence
only and is superseded by final packaging below. All attempts and their exact
source/binary identities are retained under ignored artifacts/sh-integration.

Completed own reproducible build caches were retired as space required, with
exact key DLLs/APKs, hashes, logs and tracked sources retained. Duplicate plain
git archives were also retired after recording hashes/recreation commands;
exact source directories and committed history remain available. No other
QA outputs, user files, settings or backups were removed.

Final Android default Release passes26 inherited XML warnings/zero errors,
with119 AOT libraries per ABI, correct ARM64/x64 ELF identities and valid
v2/v3 signatures. Its APK SHA256 is
`d60b832fba849d3afa6f6b3332a2691c6943dc1bce00caa1bde2bda73a465c76`.
The optional Package=true + Experiment=true Release also passes the same
AOT/ELF/signature checks; APK SHA256 is
`23822254bfdb2a35df01be9f29474852a7dfabc2c90a092ba49444f81f1c6fcf`.
Both packaged native libraries exactly match the newly rebuilt65eb5d70 closure;
the default package intentionally omits this optional bridge. Independent APK
and managed-core copies, inventories, commands and source manifests are retained
in trooper-wall-cling-final-d4b382431/android-platform. The optional standalone
Debug package also passes27 warnings/zero errors, signing and both native
payload checks; APK SHA256 is
`eb40e318c679c734b35bf2ffe9436ab77990d493463ae87b8d8d03322697cc85`.
Its355 ELF libraries per ABI have correct64-bit identities and no AOT is requested.
All6,292 tracked source files remain unchanged, as does the final desktop core
hash. These are compile/package checks, without Android UI activation, touch
or device gameplay qualification.

Drone
hover/burst/laser, Pirate Blaster/affinity, source Trooper actor/gun materials
and animations, online authority, replay setup/state and physical
Android/touch/gameplay qualification remain engineering/acceptance work.
This feature is labelled wall-cling practice, not a completed Trooper.
