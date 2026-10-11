# Fruity PR08 mechanics audit evidence

Date: 2026-10-11. This is a conditional mechanics audit, not certification of PR08a–d acceptance. Three reproduced defects were corrected: native Sylux muzzle obstruction, fall-damage scalar conversion and Android touch steering after release. No weapon-selection or network-protocol change was imported.

## PR08a: Sylux muzzle obstruction

Inspected `BeamProjectileEntity.CheckCollision`: surface collision checks `BackPosition` → `Position` and establishes the nearest collision before player-hit selection. Inspected `NetShotProof.Admit` / `EmissionReason`: historical body-to-muzzle obstruction produces `ShotProofReason.BlockedMuzzle`, independently of projectile collision. These are different guarantees. The authority guard does not prove that an owner-predicted projectile born beyond a wall is visually correct.

The actual native fixture reproduced owner and accepted remote shots each dealing
6 HP through a real wall at the muzzle, with the shooter body safely in front.
The finite Sylux muzzle interval now resolves through the shared native obstacle
and impact handling before damage. Fire/ammo semantics and protocol policy stay
intact. Before: 27 assertions with two blocked-shot failures. After: **27 PASS**,
including open and lateral-cover positive controls and actual Lockjaw bomb
placement. The same build also passes **324 shot-proof** and **94 combat-arbitration**
assertions. See `sylux-native-muzzle-regression.md` for geometry, source reference,
commands and limits. This controlled native fixture does not certify natural
gun-model animation, hardware, live owner/client transport, or the full bomb
radius/snare parity matrix.

## PR08b: fall damage

Inspected and exercised `PlayerEntity.HandleCollision` in `PlayerCollision.cs`. The fall-speed threshold, standing exclusion, alt/morph/unmorph exclusions, and jump-pad grace precede the damage calculation. Fall damage enters `TakeDamage` with `DamageFlags.NoDmgInvuln`, null direction, and null source.

Reproduced the same dimensional error corrected by [Fruity PR120](https://github.com/liveteklol/Fruity-Prime/pull/120): `FallDamageMax` is the raw HP scalar **70**, but Project Prime converted it with `Fixed.ToFloat`, yielding **0.017089844**. The actual authored-floor contact at vertical velocity **-1**, threshold **0.75**, should debit **21 HP** (existing truncation of 70 × 0.25 / 0.8). It debited **1 HP** instead. Tightened regression failed before the production change with `expected 21 HP, actual=1`, exit 1, recorded in `/tmp/fruity-mechanics-before.log`.

The production correction uses `(float)Values.FallDamageMax`; the speed threshold remains fixed-point. It preserves the existing integer truncation, one-HP minimum, eligibility gates, native `TakeDamage`, and authority receipt format. No additional damage cap was introduced.

`tools/mechanicscheck` starts fresh asset-backed `ServerSim` scenes for all seven hunters. Its matrix covers Vanilla/Balanced/Enhanced rules, actual authored floor planes, below-threshold contact, threshold minimum, exact repeated landing debit, horizontal knockback, standing/alt/morph/unmorph exclusions, jump-pad grace at 10 ticks and admission at 11, deep contact (350 HP at velocity -4.75 against 500 HP), and lethal health clamping. Lethal contact must advance the damage sequence and place the exact debit in `PlayerState.Damage3`, the newest receipt, with no attacker generation/slot or beam attribution.

The initial audit-only version passed **364 assertions** against the old behavior; those results exposed the scalar error and do not certify the correction. After the root agent rebuilt the main Debug assembly, the stricter post-fix matrix **passed 406 assertions**, exit 0, recorded in `/tmp/fruity-mechanics-after.log`.

**NOT RUN:** full falling/transform/jump-pad/knockback trajectories, physical controller/touch routing, bots, 1v1, native terminal-velocity behavior, and recorded replay comparisons. The contact kernel fixture explicitly seeds contact position/velocity; it does not pretend to simulate those trajectories.

## PR08c: missile equip after spawn

Inspected `PlayerEntity.Spawn`, `InitializeWeapon`, `TryEquipWeapon`, and `ProcessTouchInput`. Multiplayer initialization enables Power Beam and Missile and seeds missile ammunition as ten missile costs. Spawn silently equips Power Beam (Imperialist in InstaGib), then applies the established game-mode spawn loadouts. Direct missile selection calls the existing equip gate, which synchronizes `CurrentWeapon` and `WeaponSelection`; ammo, availability, gun animation, and mode restrictions remain authoritative.

`tools/mechanicscheck` exercises native `Spawn` twice after a previous Missile selection for each of all seven hunters × Vanilla/Balanced/Enhanced. It verifies `CurrentWeapon` and `WeaponSelection` reset to Power Beam, then invokes the actual common `ProcessTouchInput` direct-select branch and verifies both become Missile immediately after spawn. These cases passed in the final **406-assertion** native matrix. No Project Prime missile-selection defect was reproduced; no aim/zoom option or weapon-selection fix was imported.

**NOT RUN:** physical mouse/controller/Android touch routing, all game-mode modifiers, and network respawn orchestration. Calling the common direct-select branch tests native selection semantics, not the external input device.

## PR08d: touch sample consumption

Added `TouchSamplingChecks.Run`, called by the existing `PointerCheck.Run` diagnostic. An integer timeline replays thirty 30 Hz touch moves over one second. Presentation runs at 30/60/120/144 Hz while simulation runs at 60 Hz; this includes pictures with no simulation and pictures with two simulation steps. The test exercises production `PointerDevice.Update`, `AdvanceSimulationStep`, `TakeDelta`, and `AltFormGesture.StockRollDrive`. It asserts 120/-60 pixels of integrated motion, 5/-2.5 integrated unsaturated Samus steering, exactly 60 simulation steps, and zero motion on a second consume after every simulation step. Failure detects discarded high-refresh deltas and duplicate catch-up consumption.

Existing `PointerCheck.CheckZone` additionally verifies paused/focus-blocked contact quarantine, pointer identity churn, release/rearm, accumulated drag, and zero movement on a second consume. These are desktop synthetic tests, not Android device tests.

Android reproduction: `tools/touchcheck` links the actual platform-free `TouchControls.cs` production source. `PointerDown` → `PointerMove(4, -2)` → `PointerUp`, all before the next simulation step, produced `SamusAltMoveDrive == (False, 0, 0)` while `TakeAimDelta() == (4, -2)`. The final drag reached aim but was silently discarded for Morph Ball steering. The pre-fix harness exited 1 with that exact evidence.

The minimal correction in `TouchControls.SamusAltMoveDrive` treats nonzero pending raw motion as eligible even after contact release. `TakeAimDelta` remains the sole consumer, so the next step sees zero steering. The initial post-fix regression exited 0. The expanded harness exercises thirty 30 Hz native touch samples across 30/60/120/144 Hz presentation and fixed 60 Hz simulation, including release before the final simulation and `ReleaseEverything` lifecycle cancellation. It verifies exact aim and roll totals, zero duplicate roll, and cleared raw/presentation state after cancellation.

`GameView.DrawFrame` calls `ApplyInput` inside each fixed simulation step. `CollectInput` reads Samus steering before `TakeAimDelta`; pictures without a simulation step do not collect gameplay input. The other rolling hunters retain their anchored precision stick, and Samus retains stock motion steering and configurable swipe sensitivity. No gesture threshold, boost timing, wire format, replay state, or authority receipt was changed.

**NOT RUN:** Android touch hardware, pause/resume/focus action tapes, native boost charge cadence, and gameplay/replay/network parity. The deterministic desktop tape does not prove those device paths.

## Validation record

- Existing pre-change October 7 binary: `~/.dotnet/dotnet src/MphRead/bin/Debug/net10.0/ProjectPrime.dll -pointercheck` → `POINTERCHECK PASS (1788 assertions)`. This result excludes the new sampling tape.
- `~/.dotnet/dotnet build tools/touchcheck --no-restore -p:BuildProjectReferences=false -p:UseSharedCompilation=false -v:q` → build succeeded, zero warnings/errors for the released-drag regression. `~/.dotnet/dotnet tools/touchcheck/bin/Debug/net10.0/touchcheck.dll` → pre-fix exit 1, post-fix exit 0.
- Expanded Android tape harness: build succeeded with zero warnings/errors; executable exited 0 with `TOUCHCHECK PASS` for released-drag consumption and all four presentation frequencies plus lifecycle reset.
- Root agent rebuilt the main source serially and ran `-pointercheck`: **PASS, 1788 existing counted assertions**, plus the new `TouchSamplingChecks.Run` action tape (its assertions are independently enforced and do not increment the existing counter). Root also reran `touchcheck`: **PASS**.
- Focused native mechanics: `~/.dotnet/dotnet build tools/mechanicscheck --no-restore -p:BuildProjectReferences=false -p:UseSharedCompilation=false -v:q` succeeded with zero warnings/errors. `~/.dotnet/dotnet tools/mechanicscheck/bin/Debug/net10.0/mechanicscheck.dll '/Users/jarrett/Library/Application Support/Project Prime'` → **PASS, 406 assertions**, exit 0. The stricter expected-21-HP regression failed before the scalar fix. Before/after logs: `/tmp/fruity-mechanics-before.log`, `/tmp/fruity-mechanics-after.log`. Build/run scope and limits: `tools/mechanicscheck/README.md`.

Native extracted assets were located at `/Users/jarrett/Library/Application Support/Project Prime/files/AMHE1`; the unexecuted native cases are not waived because assets exist. Full PR08 acceptance remains open until their concrete fixtures and hardware/replay results are recorded.
