# Original-ROM biped recorder

This runs the original AMHE revision 1 ROM in the melonPrimeDS interpreter. Python schedules inputs and converts captured fixed-point values; it does **not** reimplement movement. No ROM, firmware, save, or state is distributed. Cartridge save writes are volatile, network backends are disabled, and the user's emulator installation is untouched.

## Build and capture (macOS/Linux)

The recorder is pinned to [melonPrimeDS commit 41b0878](https://github.com/makinori/melonPrimeDS/tree/41b087835978cb99e0d1b862411b702c5d1648c8). The external emulator core is GPL-3.0; honor its license when distributing a linked executable. CMake, a C++17 compiler, Git, and Python 3 are needed. Qt, SDL, a graphics window, and external BIOS files are not required.

```sh
git clone https://github.com/makinori/melonPrimeDS.git /absolute/melon-source
git -C /absolute/melon-source checkout 41b087835978cb99e0d1b862411b702c5d1648c8
cmake -S tools/native-recorder -B /absolute/native-build -DMELON_SOURCE=/absolute/melon-source
cmake --build /absolute/native-build -j 6
python3 tools/native-recorder/prepare.py \
  --bridge /absolute/native-build/prime-native --rom /absolute/AMHE1.nds \
  --output /absolute/new-start-directory
python3 tools/native-recorder/record.py \
  --bridge /absolute/native-build/prime-native --rom /absolute/AMHE1.nds \
  --state /absolute/new-start-directory/native-biped.state \
  --scenario tools/fps-scenarios/biped-forward-1s.json \
  --output /absolute/new-capture-directory
```

`prepare.py` drives original-game menus into Data Shrine Battle with Samus and one bot. Pass `--room "MP3 PROVING GROUND"` to prepare Combat Hall for the jump-pad fixture. It checks live room/mode/hunter/health, captures a screenshot, and writes a state plus hash-bound metadata. This was executed successfully with the supplied ROM. `record.py` requires that metadata, checks the exact ROM hash and live room, restores that state, establishes shared position/velocity, input-idle timer, initial grounded flags and RNG seeds, settles, applies any prescribed initial impulse, and runs the script. It refuses unsupported facing, forms, unresolved placements, nonrepresentable spawn coordinates, damage, cadence errors, missing probe observations, and reused output directories. Bot damage makes a fixture fail rather than contaminate the reference silently.

The same input JSON drives Prime's `-fpsscenario`. Directions hold until the next input row; a jump event is sent for one native tick. The supplied fixtures have isolated jump edges. Adjacent native-tick jump events are rejected by both runners because native button edges require an intervening release tick.

Output includes `native-reference.jsonl`, `manifest.json`, a snapshot of `profile.json`, emulator log, and final PPM screenshot. Native elapsed tick N maps to Prime frame `2*N`; the recorder checks the native game counter advances exactly once per two NDS video frames. Position/velocity use signed fixed point /4096. The native ground timer is multiplied by two for Prime's counter units. Header frame numbers are elapsed scenario boundaries, not the original match's absolute counter (which is retained separately).

## Verified AMHE1 observations

| Location | Meaning / verification |
|---|---|
| `0x020DAF94` | Local CPlayer; entity type 25, linked player stride `0xF30`, observed active Samus |
| `+0x1C / +0x28` | Position / previous position |
| `+0x34 / +0x40` | Velocity / previous velocity |
| `+0x4C` | Facing; observed +Z and native drift retained in output |
| `+0xCC / +0xD8` | Timed acceleration vector / uint16 duration; movement instructions `02020CA8–02020CCC`; adjacent health is preserved when setting duration |
| `+0x408` | uint32 timeSinceInput; native reset/increment at `02011090–020110A4`, reset to match Prime Spawn before settling |
| `+0xDA / +0x400` | Health / hunter ID |
| `+0x436` | uint16 timeSinceGrounded; native instructions `0x02021A88–0x02021ADC` increment, cap at 90, reset on standing, clear grounded at 8 |
| `+0x4C4` | Flags: standing 1, grounded `0x100000`, lateral/entity collision `0x90` |
| `0x020D945C` | Native game tick counter; verified one increment per two video frames |
| `0x020E21E8 / 0x020E21EC` | RNG1/RNG2; native setters/wrappers at `0x020433A8–0x02043450` |
| `0x020E83BC` | Game mode, room ID, area, player count; Data Shrine Battle = `03 5D 08 02`; Combat Hall = `03 5F 08 02` |

Offsets start from the repository's `MemoryClasses.CPlayer` layout and were checked against the running ROM. The older European address table is **not** used unchanged.

CMake instruments an out-of-tree copy of the pinned ARM interpreter; the external checkout stays pristine. `probe.cpp` observes native ARM9 instructions at movement entry, after acceleration, damping, gravity, integration and collision. Collision depth comes from the native computed register; plane and pushout come from the native collision result and player state. Stack-backed planes are read through ARM9 DTCM mapping without changing emulated cycles. Instruction signatures guard the profile. Forward trajectories with probes on/off were identical.

`nativeStages` are whole **30 Hz** stages. Prime `stages` are individual **60 Hz** stages. They must not be declared equivalent by comparing one native stage to just the second Prime substep. The comparator accepts direct internal-stage comparison only when the offline Prime kernel experiment declares 30 Hz stages. Ordinary 60 Hz stages remain UNAVAILABLE for that comparison. Native requested keys plus raw consumed control/input memory are retained; analog scales and internal traction/cap values are not claimed decoded.

All 17 fixtures now have executable setups, including eight geometry/impulse coverage checks. The offline native-cadence experiment matches movement state/stages/contacts throughout this corpus; two full comparisons still fail on idle-sway heading. The live movement kernel remains unchanged. See [findings](../../docs/physics/NATIVE-MOVEMENT-PARITY.md).

The manifest hashes the trace itself, recorder script, profile, executable, ROM, starting state, and scenario. Setup clears only the initial grounded bits, not collision responses during simulation. For `initialImpulse`, the scenario supplies velocity and a timed acceleration after settling; this is a knockback-response test, not a weapon-hit test. Native health is checked every tick, and partial/contaminated captures are never published as references.
