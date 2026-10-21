# CW-11c — actual local package gameplay

Base main `f4ea73c35` (CW-11b plus isolated Windows shader prerequisite
[PR 937](https://github.com/AntiNotAnti/Project-Prime/pull/937)); protocol 79.
Windows x64, NVIDIA GeForce RTX 5080, OpenGL 4.6/NVIDIA 617.42.
Final qualification uses SDK 10.0.401/runtime 10.0.12 installed locally for this
task, two .NET processors, one MSBuild worker and shared compilation disabled.
Earlier SDK 10.0.400 development runs remain labelled in their logs.

[Explicit developer launch](../11c-package-play.md) binds an installed exact
revision before the first simulation tick. The scene retains its archive lease,
validated mechanics, model and decoded PCM; subsequent ticks never open packages
or decode their resources. Native arms and the existing render/audio pipeline
remain in use. Installation and activation do not grant or equip. Normal world
contact grants the weapon and applies the existing next-tick selection policy.

The graphical fixture creates a new creator content ID
`11c011c0-1122-3344-5566-778899aabbcc`, changes the body/head damage to 37/83,
magazine to 6, reserve to 23, cooldown to 24 ticks, reload to 42 ticks, receiver
palette to magenta, and all four WAVs to half amplitude. This distinguishes
package data from built-in constants and from a same-lineage M7 revision.
All resources derive from the original CC0 reference; no retail assets are added
to Git. The fixture references the operator's existing extracted data through
an isolated `paths.txt`; it does not copy or modify those files.

Final evidence: an actual native GL scene passes 49 assertions, walks 20
ordinary W-input steps from declared native spawn 4 to acquire the weapon, shows
6/23, fires to 5, reloads to 6/22, and applies exactly 37 body damage to a native
Kanden target (declared target placement, HP=200 and expired protection; HP=163).
The victim never owns a custom weapon. Free-camera station framing and target
placement are explicit fixture setup; ownership and fire never use a grant or
direct state injection. Window/world images show the magenta station and raised
arm module. Draw-only checks preserve simulation/resource state. PCM equality
is CPU evidence; no claim of physical audio perception is made.
The new identity also produces actual shot kick/flash, rather than only a static
model. The standalone fixture explicitly shuts down native audio like the real
entry point; its observed process exit is zero with empty native stderr.

Uninstall during the active scene leaves prune at zero; installed-only acquisition
then refuses the revision, and scene cleanup permits exactly one archive removal.
False opt-in leaves package, registry, model and waves unbound. A zero-reserve
definition can have a zero-refill station, while positive-reserve definitions
still reject zero refill. Source checks widen cosmetic identity support to
validated precision weapons while preserving complete owner/weapon/bootstrap
fences and the private Pirate burst renderer.

| Final local check | Observed result |
| --- | --- |
| Forced desktop Release rebuild | PASS 124.60s; existing CS0649 only, zero errors |
| Dedicated Release | PASS 112.18s; same existing warning, zero errors |
| New-ID graphical gameplay | PASS 49; actual process exit 0 |
| Desktop / actual dedicated store suite | PASS 99 each |
| Historical package vectors/parser | PASS 1,000 |
| Custom CPU including creator-context reset | PASS 36,965 |
| Desktop / dedicated native replay format | PASS 3,050 each |
| Desktop cosmetics | PASS 16,226 |
| Desktop shader LF/CRLF contract | PASS 7 (isolated PR 937) |
| Actual production `-weaponplay` | PASS exact new-ID revision, native GL, 2s fixed-tick limit, exit 0 |
| Server `-weaponplay` | Refused with expected exit 2 |

Final desktop SHA-256:
`841681370f3f359336465c1ba724836ec4ad2edd52cef8916857f46207a11c08`.
Main/copied tool hashes match before each final test. The dedicated probe loads
no Avalonia, BEPU or CorpsePhysics. `gl-clean-final/` contains final images/JSON;
the production launcher uses its own isolated profile and store. Raw attempts,
source/binary guards and actual process exits are retained in
`artifacts/custom-weapons/CW-11c/`.

Recorded development failures: Windows CRLF shader specialization failed before
package activation and was fixed/tested separately in PR 937. A new context test
then detected an old M7-only guard in a stale incremental DLL after source was
edited during compilation; final qualification forces a rebuild from frozen
source. A shell exit 1 after a graphical PASS was separately checked using an
isolated process: its actual exit was zero with empty native stderr. These
attempts are preserved, not relabelled as final acceptance.
A subsequent standalone-fixture run failed natively after its assertions because
it omitted the application's native audio shutdown; the fixture now performs
that same cleanup. The final assertion-plus-cleanup process and the production
command both exit zero. No failing product check was suppressed.

Native CWM1, wire layouts, authoritative combat and existing online pickup
synchronization are unchanged. Native custom replay refusal stays intact.
Community/Studio, online package preparation and replay binding remain CW-12–15.
Other-Hunter package graphics, physical controls/audio/high-refresh/WAN and
broader performance are CW-16 gates; this narrow Windows fixture does not close
them. Android builds/emulators/devices remain deferred. CI is neither awaited
nor claimed passing. Rollback: omit `-weaponplay`; native sessions never consult
the package store and feature defaults remain unchanged.
