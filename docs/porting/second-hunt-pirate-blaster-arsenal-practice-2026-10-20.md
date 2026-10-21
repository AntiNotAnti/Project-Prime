# Private Trooper Arsenal practice: Pirate Blaster

The explicit **Trooper Arsenal** offline setup choice now adds a playable private
Blaster to the existing native-fallback wall-cling, drone and laser mechanics.
The human is native Weavel; opponents are ordinary native bots. Imported Trooper
actors/cannon, Overcharged runtime presentation, complete character selection,
multiplayer/replay and the full integration plan remain unfinished.

This implements the user’s requested gameplay work. The attached plan is a
specification, not an additional authority. The user authorized committing and
merging completed work without waiting for review or CI. Windows transport was
already unavailable at authentication; this increment uses the authorized Mac
fallback. The retained recovery receipt is
`artifacts/sh-integration/windows-trooper-wall-cling-20fa89349/recovery-attempt-01.json`.
No new Windows pass is claimed. The user subsequently requested skipping Android
testing; both package checks below had already completed, and no further Android
tests were started.

## Behavior and ownership

Adapted from SecondHunt `41452f2d053fcfa679e33c00a73d120a53f59e61` (MIT),
principally `Metadata/PirateBlaster.cs`, `PlayerPirateBlaster.cs` and
`PlayerSquadMark.cs`, through current native owners:

- The fifth setup choice cycles Native → Wall-cling → Drone → Drone combat →
  Arsenal → Native. Old modes remain distinct. Arsenal requires a fresh explicit
  Weavel/Battle playtest, native movement and standard weapon/ammo rules. It
  defaults to one native bot and admits zero through seven, with native difficulty
  zero through three. Raw invalid selections are refused before persistence;
  Android handoff preserves the exact captured mode. The human alone gets the
  private capability; native bots keep native actors, controls, AI and weapons.
- Each genuine new human life starts selected with **60 in the actual shared UA
  bank**. Native UA pickups fund that same bank. Hold Fire for three bolts at
  source steps **0, 6, 12**; the first spends **5 UA once**. Released Fire does
  not cancel paid tails. The next burst starts at **40**, with current native aim
  and power-up state sampled for each bolt. Native selection, successful morph,
  freeze, grab, unavailable source input or ownership loss cancel future tails;
  changing weapons/forms cannot erase the accepted cadence. Empty fallback waits
  for paid tails. Existing custom/affinity actions and normal weapon cycling can
  reselect the Blaster. The native wheel continues to select native weapons.
- Body/head damage is **12/16**, speed is two world units per 60-Hz step, radius
  is `409/4096`, lifetime is 510 steps, and headshots retain the native 15-unit
  travel limit. Native Power Beam effectiveness is an explicit mathematical
  policy, with native protection, modifiers, health split, collision and death
  handling. It is not a native Power Beam identity. No splash, piercing, homing,
  charge or spread is added. Double damage takes precedence over Prime.
- Three distinct positively paid bolt ordinals from the **same burst to the same
  exact target life** create a 240-step local mark without bonus damage. Other
  targets reset that burst’s target mask; duplicate ordinals cannot count again;
  old flights retain their own burst. Death, respawn, replacement or source/rule
  retirement invalidate marks. Native locator and enhanced-radar composition
  read this private predicate without setting global radar reveal. Positive mark
  lifetime is qualified below; positive locator/radar pixels are not yet qualified.
- A Scene-owned runtime freezes an independent custom definition and owns a
  private 40-flight pool. A first bolt requires room for the full burst; no live
  flight is evicted. Provenance is reserved before native callbacks, and the
  actual native stamp admits exactly one pool member. NoSpawn refunds an
  uncommitted first UA debit; later cancellation never refunds a paid burst.
  Ordinals/IDs cannot wrap or be reused. Native collision enters a consumed
  private contact/payment scope; arbitrary public damage or custom provenance
  alone cannot inherit admission. Existing offline spawn match/generation/life
  facts are read without advancing or duplicating the native lifecycle owner.
- Morph/weapon changes permit already emitted flights to finish under their
  original source life. Source death, Scene exit or owner/rule loss destroys and
  unregisters all private flights. Draws do not advance ammo, cadence, marks,
  aim, RNG or collision. Native Weavel gun/animations, orange native trail/particle
  and Power Beam sound/impact resources are honest fallback presentation. There
  is no M7 rifle, magazine, reload or optic fabrication.
- Actual emitted bolts/paid contacts update generic shots, hits and clamped match
  damage. Real native kills/deaths/points and generic weapon death/assist facts
  remain native; no native per-weapon kill bucket or native shot key is forged.
  The new private profile cannot enter M7’s precision inventory/state, CWM1
  manifest, source journal, launch/payment or network adapter. Public schema1
  parsing and existing M7 canonical goldens remain unchanged. Native Hunter and
  weapon values, weapon tables, wire format and historical replay readers are
  unchanged. Private replay exclusion stays latched after retirement, with no
  normal rematch/career publication.

The private content ID is `49f3ab6c-bdc0-40ed-bf14-eaaa5ad5aa9f`. Its independently
pinned schema2 mechanics digest is
`e36df634e3024ef31a4f96fd9831bcc17515ef61006d0fede358a30473c20186`.
The whole-burst capacity/headroom refusal is a deliberate safe local policy;
it does not copy the donor’s live-pool eviction hazard.

## Frozen verification

The initial feature freeze was `c3e39ac76ab72f36f94407ebc6e84e83d56ede90`,
including main through PR920. Subsequent acceptance fixes did not change its
production owners. Main PR921/922 were then integrated at
`67bf7cef968db50e890e9763a0ea717964a8deae`; that combined desktop binary was
rebuilt and requalified separately below. Source equality is independently
checked; no earlier binary is presented as qualifying later main changes. Receipts retain
commands, source projections, exact tool hashes, failures and temporary-context
cleanup. The original dirty checkout is untouched.

Initial desktop Release core:
`bf8186997fc99e625ea2adf690c85c1082251376e91d5f518fa3c8ff0a4a85ee`.
Build passed with one inherited CS0649 warning and zero errors. The following
table records that initial qualification; the final desktop rerun follows it.

| Qualification | Actual result |
| --- | --- |
| Custom-weapon CPU contracts | 36,971 assertions, including 1,199 timeline checks, independent schema/adapter literals, old M7 goldens and private precision/network admission refusals |
| Offline policy/draft/preflight/replay | 947 assertions; Native and all earlier practice choices retained |
| Initialized owned Blaster Scene | 3,473 assertions: 0/6/12/40, released tails, exactly 12 bursts/36 bolts from 60 UA, current raw-mouse tail aim, native fire/cycling, same-step morph cancellation/remaining39 wait, ownership retirement, real zero–seven native bot creation, native UA pickup contact, actual body12/near-head16 contacts, three-hit marks through239/off240, native respawn invalidation, per-bolt12/18/24, far high-band body12 beyond15, actual99-HP death with9shots/9hits/99damage/onekill/death/point and zero native BeamKills aliases |
| Genuine native-bot reciprocal combat | 571 assertions: one admitted native Samus bot; one explicitly synthetic floor/LOS/clearance-checked native Spawn placement, then untouched AI/controls/health. Native missile paid32 to human after362 steps; real keyboard/raw-mouse return fire killed bot after95 steps, 9Blaster shots/9hits/99paid damage, humanHP67/botHP0, one native kill/death. No target pin, health write, AI override or private accounting injection |
| Actual Mac GL | 572 assertions, Apple M4 Pro OpenGL2.1, framebuffer1280×960. Native gun13 meshes contributed142,781 pixels; accepted orange trails at0/6/12, matched trail controls changed2,002 pixels at6 and12; real UA60→55,40 cadence, switching/reselection, draw-only state and teardown. 287simulation steps/64in-memory pictures;40private flights retired and362Scene textures deleted |
| Existing mechanics regressions | Wall owner276; wall Scene1,403; laser geometry495; original Scout Drone Scene11,949, including original4,958 primary assertions |
| Native offline setup UI | 475 checks/50translated intents **at each** 960×600 and640×480; exact Arsenal capture and fifth Native cycle. Full live-menu suite passed. All159runtime assets matched tracked source individually |
| Portable/Android handoff and hosts | Handoff629; full touch suite including29Zoom ownership checks; capabilities735; runtime canvas197/scheduler28/IME31/Cocoa35/full owner100reinitializations. All builds zero warnings/errors, core unchanged |
| Dedicated Release | SDK10.0.401, zero errors/one inherited warning,46.079seconds. Exact6,395Git inputs verified unchanged. Core `f977132bcc1e57f117dd25b74bd4927d5f8fa68f5d6361fbdea6f2724ce02da9` |

### Main PR921/922 combined desktop verification

At `67bf7cef968db50e890e9763a0ea717964a8deae`, all **6,422 tracked inputs**
matched the frozen Git source and remained unchanged. Desktop Release passed
with zero errors/one inherited CS0649 warning. Its core SHA256 is
`115f14b814a884f85413b866d85ce7b39bd7d2efa1e8bbc9d819c6446b16e424`;
the nettest tool is
`bd60265cad70c040b06d327ca525fdb637c1afad514307fe4faea84099428acc`.

The exact combined binary passed 36,971 custom-weapon CPU assertions, 947 offline
policy assertions, 3,473 Blaster Scene assertions, 571 native-bot assertions,
1,403 wall Scene assertions, 11,949 Scout Drone assertions and 572 actual GL
assertions. The reciprocal bot outcome and visible GL deltas matched the
initial qualification above. Fresh contexts were removed and all owned-source
hashes remained unchanged. The full live-menu suite and native UI passed; each native size passed475 checks/50translated intents. All159runtime assets matched tracked source. Complete independently hashed outputs are retained as APFS clones (359live-menu/358native-UI files).

The exact 6,422-input dedicated Release build passed in 56.114 seconds with zero
errors/one inherited warning; dedicated core SHA256 is
`69f9a3c32c4c5e0e3fb4f9c1dbb516e5af7407dbdce731ae3f144a95b6eee6fd`.
Its evaluated project references, restored libraries and runtime dependencies
exclude the experimental `MphRead.CorpsePhysics` client dependency.
Receipts are under
`artifacts/sh-integration/pirate-blaster-final-67bf7cef9`.

Android was **not rerun** for the final integration, following the user's
instruction. Earlier package checks below do not qualify the new main PR921
Android dependency or this final combined source.

### Main PR923 integration and shipping desktop verification

Main PR923 subsequently added Parkour and protocol77. It merged cleanly at
`65f7ded55d8008ab0e2854e610816e4639f092ea`; test-only commit
`04ecacaca010d10b28402167efa004a821936a16` adds explicit Traversal/Parkour
refusals for the Native-only Trooper choices. All **6,430 tracked inputs**
matched that frozen Git source and remained unchanged during qualification.
No new native weapon/Hunter or protocol identity is introduced by Arsenal.

The combined desktop Release and nettest builds passed. Desktop core SHA256:
`f52e8f132d07bb270fb0035b9028a40a86de50f167c67ee7c855bfb6733ce0d9`;
nettest SHA256:
`24b578ed1863f1749b6a3dc9b7c0bdd0eef8510855b59e688cd6bd3975ca133f`.
It passed **36,962 CPU contracts**, **971 offline policy assertions**, the same
**3,473 Blaster**, **571 native-bot**, **1,403 wall**, **11,949 Scout Drone** and
**572 actual GL** checks. The CPU total changes with upstream protocol77's
recognized bits and new stance contracts; the private Blaster goldens/admission
checks remain present. Owned files and exact binaries stayed unchanged.

The exact dedicated Release build passed in56.086seconds with zero errors/one
inherited CS0649 warning, all6,430source hashes unchanged. Dedicated core:
`b128f2e045f5e0590e9067da5f49d1fbbae507ad94a1714d72ee91090f7fc481`.

The first combined live-menu run stopped at the old four-choice movement
fixture. Its raw failure is retained. Main PR923 adds a fifth Parkour choice;
test-only corrections explicitly visit Parkour before returning to Classic,
without weakening the assertions or changing production behavior. Final menu
results are recorded after that correction. Receipts are under
`artifacts/sh-integration/pirate-blaster-final-04ecacaca`.
Android testing remains skipped for this integration.

The owned Scene/GL runs read AMHE1 in place: **3,970 files / 845,958,857 bytes**,
aggregate hash `76aa905095567286e4c5f65603c7b4cacc16ba53ca7f9730d16c563be499728e`,
unchanged before/after. No new protected images, audio, model payloads, user saves
or media captures were retained or committed. Qualified complete nettest runtime
outputs are retained as independent APFS clones, not hardlinks. The first
exploratory source-free CPU invocation used the default generated-map cache root;
final/source-bound qualification used fresh isolated user-data/TMP contexts.
No untouched-default-user-data claim is made for that initial exploratory run.

Primary receipt roots:

- `artifacts/sh-integration/pirate-blaster-exploratory-03`: combined M7/private CPU and policy pass; initial target fixture refusal.
- `pirate-blaster-exploratory-05` and `-06`: strict3,473 Scene pass, exact native damage/pickup/mark/kill facts.
- `pirate-blaster-exploratory-gl-01`: exact572 GL pass and owned/source/binary integrity.
- `pirate-blaster-exploratory-08`: final571 reciprocal native-bot pass, actual rays/AI/positions and separate paid outcomes.
- `pirate-blaster-c3e39ac76/{regression,dedicated,live-menu,native-ui,android-platform,qualified-nettest-before-bot}` and `pirate-blaster-dbd129f37/{host,android-host}`: frozen production/source projections and platform receipts.

The Scene/GL test tool was
`e59b457cd8104ad755bfdbcecb4e2fb3865fb1fa1e831e99b1c368b101f4ba21`;
the final bot tool was
`4b820a56d05e39475be9afc8e7d1f821c38873d09e7157c401c2e8d0336e4b0b`.
Original assertions were preserved while separate bot diagnostics were added.
Initial fixture failures remain raw: ambiguous test numeric/GL enum names,
incorrect HealthMax-versus99 spawn expectation, unsafe identity JSON projection,
blocked fixed bot placement and a native respawn+1Y clearance mismatch. The final
bot setup uses `respawn:false` and validates actual post-Spawn coordinates; no
production AI/damage gate was changed to manufacture a pass.

## Android checks completed before testing was skipped

Both clean serial-AOT Release profiles compiled the exact independent6,395-file
`1badffbda2e3cc58dc1c49b74c603fabff9b9513` archive, whose production inputs equal
c3. No ROMs/owned extracted data were copied into the build. Both have119AOT
libraries **per ABI**, valid ELF64/little-endian/machine183/62 libraries, and
verified v2/v3 local AndroidDebugcert signatures. These are packaging checks,
not physical-device, Android gameplay/UI or production-signing acceptance.

- Default APK: `4884f32ab52c84cc1fcd843712ee3801dfa228147d18ee451edbcc35c268cdc2`,147,978,313bytes;3m38.22s,26existing warnings/zero errors. Core `66e4af7a2cd45ca52756c33b89cf279f29c7fa523db63d4a68bd0135dba0061c`.
- Optional Package+Experiment APK: `6e6c03bd50d9bf1a17ec861216bce5286fc59a92c8150e253ffa64ad54b5bf28`,180,121,623bytes;3m40.58s,27existing warnings/zero errors. Core `eba76fd7438c442c5bab056c6f06657f3ebd4349d204d71d5eb75f9675525148`.

Default bridge absence and optional exact embedded bridge manifests/payloads were
verified against unchanged source fingerprint
`65eb5d70c119715a3c941aa68792512d53adf760bee7bc8980fe227fd8403ec9`
(16source inputs, all three existing native targets). All tracked inputs,
untracked nonbuild/native files and managed desktop bytes stayed unchanged.
Minimum observed free space was4,456,136,704bytes, above the768MiB guard; no child
was stopped/retried for storage. Each completed own Release cache was retired
only after independent APK/core/signature/full-AOT/native inventory retention;
source inputs remain. Standalone Debug and device/emulator testing were not run.

## Remaining work

This is qualified **private native-fallback offline Arsenal gameplay**, not a
completed imported character. Imported actors/cannons and source animations,
source-original projectile/audio fidelity, positive mark locator/radar pixels,
exact15-unit equality and broader protection/effectiveness/half-body/handicap
edge cases, physical controller/audio acceptance, online/team-shared marks,
selection/authority/protocol/replay/career integration and Android runtime
acceptance remain. Overcharged runtime cosmetics and complete arena/material
rendering remain separate engineering work. Android testing is now skipped at
the user’s request. No CI or external-review pass is claimed.
