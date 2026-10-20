# Quake III advanced movement and missile jumps

Profile v2 extends the existing selectable Quake III rule. Classic remains the
default in normal Practice and multiplayer menus; Momentum v1 and Quake III v1
math are unchanged. See the [technique guide](quake3-movement.md).

## Runtime contract

Held Jump requests another native jump only on a new physical Standing contact.
It never uses the native Grounded grace period, adds a midair jump, or spends
Space Jump automatically. Fresh jump presses retain their original ability
handling. The accepted native jump still owns Y, sound, animation and flags;
the horizontal solver skips friction on that accepted tick. There is no new
persistent latch, queue, timer or replay appendix. Circle/strafe speed gain comes
from the existing projection solver and turning input, without a speed bonus.

An eligible Quake III biped's native self-Missile splash derives a bounded radial
impulse from finalized damage and explosion geometry before hit prediction.
The impulse adds to existing X/Y/Z velocity, including when already rising.
Self-damage, invulnerability, lethal outcomes, native weapon resources, contact
and line-of-sight handling remain in their original paths. Other weapons,
Classic, Momentum, alternate forms and special-state impulse ownership keep
their existing behavior. Full tuning units and bounds are in the technique guide.

Online movement still uses owner-reported positions. Authority combat validates
real emitted shot/component identity, witnessed explosion, life, geometry and
payment; the client cannot select its own blast center or authoritative impulse.
Only a finite bounded self-Missile candidate is admitted by the expanded claim
gate, then the authority replaces it with its independently derived vector.
The existing component payment ledger and exact self-event receipt settlement
remain responsible for duplicate prevention and authority-minus-prediction
impulse correction. No new trust in reported owner velocity is introduced.

## Protocol and recordings

Protocol 62 selects current Quake III profile v2 and refuses older live peers.
All packet IDs, widths and movement bits retain the protocol-61 values. Original
Quake III recordings identify profile v1 by protocol 60/61. The replay metadata
profile property derives from recorded protocol, without a new archive field.
Passive replay remains pose/damage presentation and never runs current movement
or blast physics. Checkpoint and timed-shot capability reviews explicitly
include 62 while retaining historical width handling.

## Verification

The original Windows cohort passes a normal Release build with zero warnings/errors
and all 22 routes on main `2d18ac05721366597afc1afdb57f061e73ee4423` plus a
38-file hashed overlay. Feature commit `48fee4a5d` preserves those feature sources.
[Sanitized evidence](evidence/quake3-advanced-selection.json) retains the full
source identities and exact binary/archive hashes.

| Original cohort | Result |
| --- | --- |
| Advanced movement | 171,054 assertions across 18 scenarios; all seven Hunters have six held landing hops matching manual contact-edge presses, while Classic hold produces one jump |
| Native air-jump ability | A fresh airborne press still spends Space Jump; held input never spends it automatically |
| Samus left/right circle | Peaks 11.700683 / 11.468597 world units/second, vs straight 7.1997066 |
| Samus left/right strafe-hop chain | Later flight peaks 13.546651 / 16.33115 exceed first-flight 12.774461 / 12.503201; actual held/manual input produces the same body trajectories |
| Asset-free v2 contracts | 165 checks, including numerical bounds/finite inputs, zero-allocation warmed helper loop and recorded profile identity |
| Real authority self-splash seams | 445 Q3 / 443 Classic assertions: native-vs-witness parity, rising/falling velocity, self-damage/kill, source/life/protection fences, exact terminal receipts and no duplicate payment |
| Self-prediction | Three full feedback orderings use 0.6-Y authority / 0.45-Y local vectors for Q3; the standalone 55-check prediction suite also passes |
| Live UDP match | Dedicated authority, two independent scripted human-owner processes and an authority bot; all processes exit 0; owners earn 12 / 17 actual held hops |
| New recording | Protocol 62/profile 2; 2,399 frames and 79,741 replay assertions with checkpoint restoration and seeking |
| Historical recordings | Genuine protocol-60 and protocol-61 archives pass current passive replay/seek and report profile v1 |
| Normal menus/browser | Three CPU fixture projects pass; Classic default, selection, save/cancel and current/old protocol admission remain covered |

The original online archive SHA-256 is
`721535ad7fd5743c194572362dbf8063dadff0897695bc0f15dd79cda96dcc58`.
Original Core/tool SHA-256 values are respectively
`b0735608dda492c1f1bbd4674a09e72a0b12357cffd5b29b26d1347b1aeac998` and
`a814b55e6c353fc8dd8fdb1ea3a67ffb49399b6e1efa4ebbde26fbd918dd999c`.

Main advanced during validation. Clean committed combined source
`f62676dce3ea698ed2f943097e863fe64c3359ac` integrates main `d51605175`, including
the fresh custom-input boundary and optional owner-presentation/seek work.
Only `NetLobbyTest.cs` and `Program.cs` differ among the original overlay files;
their independent upstream routes were retained. Combined verification passes independently: the clean committed checkout builds
with zero warnings/errors and passes all 26 checks (the prior 22 plus owner-track,
fidelity export, upstream baseline and actual custom-readiness UDP). Source and
binary identities remain unchanged throughout. The three CPU menu/browser
fixtures above remain attributed to the original build04 lease.

The combined build replays the unchanged genuine 60/61/old-62 archives as profiles
1/1/2. A fresh 40-second two-owner/one-authority-bot recording cohort exits 0 in
all three processes; owners earn 14 / 17 actual held hops. Its protocol-62/profile-2
recording passes 80,101 replay assertions over 2,399 frames, eight checkpoints
and nine seek targets. The fresh archive SHA-256 is
`83403812a6d4b6917761d7988d3d919da3f4ede7736bd15092602c8091556071`.
Combined Core/tool SHA-256 values are respectively
`8b8c0335a5ef082c9088dce988e38bad1defee96f43d7b213268046296d0c9e5` and
`ef115729c8a64bce88e4ef2e9c2c70b95e89a8adf1105dc4d8b8f62b19073c26`.
Exact committed source/manifest hashes and cases remain in the sanitized evidence.
No Mac build, simulation, UI/focus interaction or performance run is performed.

The new automated routes are:

```text
--quake3-advanced-rules
--quake3-advanced-scene <assets> [json]
--quake3-rocket-scene <assets> [room]
```

The first route is asset-free and runs in the three-platform movement workflow.
Scene and rocket routes require the operator's extracted game data. Movement
proof uses actual keyboard/mouse input and native collisions. Combat seam tests
explicitly set incoming velocities and claim schedules; they do not masquerade
as physical input or live UDP blast tests. Real UDP and recordings have separate
source/binary leases.

## Retained failures and diagnostics

Earlier logs remain preserved under ignored `artifacts/quake3-advanced/`.
The clean protocol-61 metadata baseline misclassified its current protocol as a
future protocol. New fixtures use the current version plus one. New fixture
failures involved a missing namespace import, using a current-only writer to
construct an old QueueHello, expecting a repeated terminal receipt to change
its original result, and trying to put NaN through an integer direction codec.
Corrections retain original byte-identical receipt proof and actual serialized
oversized-vector rejection; no product tolerance, physics threshold or gameplay
safeguard was weakened. Windows disk exhaustion was resolved by deleting only
reproducible owned build outputs, preserving recordings and evidence.

Review also fixed a policy boundary: private human tuning suppresses shared
techniques only for that human, while bots retain their selected shared profile.
The predicate fixture is explicitly distinguished from actual authority AI runs.

Original live owners retained 35 / 36 broad displacement diagnostic triggers,
without sample overflow. The trigger records post-step displacement/change over
0.25 units; it is not an acceptance threshold. Native contact, lifecycle and
combat can contribute. No controlled causal comparison or seamless-traversal
claim is made from those counts. The combined owners retain 0 / 11 triggers,
also without overflow; this is not a zero-anomaly claim for the cohort.

## Limits

Human movement feel, physical devices, rendered UI/focus, broad high-speed map
qualification, surfing, crouch/hitbox changes and a replacement collision solver
are outside this slice's acceptance. Existing native slope, step and collision
responses still apply. No new performance-budget pass is claimed; optimization
remains deferred under the previously accepted tradeoff. **Hosted CI is not verified at publication.** Local Windows checks do not
establish its outcome; publication does not wait on CI or review under the
operator's instruction.
