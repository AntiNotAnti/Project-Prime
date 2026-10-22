# Asset mechanics diagnostic

Build the main Debug assembly first, then build this small diagnostic without recompiling it:

```sh
dotnet build src/MphRead/MphRead.csproj
dotnet build tools/mechanicscheck/mechanicscheck.csproj -p:BuildProjectReferences=false -p:UseSharedCompilation=false
dotnet tools/mechanicscheck/bin/Debug/net10.0/mechanicscheck.dll "<directory containing paths.txt>" "MP1 SANCTORUS"
```

The asset directory must contain configured `paths.txt` and extracted game assets. The room argument defaults to `MP1 SANCTORUS`. Failures return a nonzero exit code; no window or device is required.

The diagnostic starts a fresh native `ServerSim` for every playable hunter and runs Vanilla, Balanced, and Enhanced rule variants. It exercises native `PlayerEntity.Spawn` twice per variant with a previous Missile selection, verifies default Power Beam state, then invokes the common direct-select input path to equip Missile.

A real floor trace supplies each hunter's contact plane. The fixture seeds contact position and velocity, then invokes production `HandleCollision`: below-threshold and threshold landing, exact 21-HP landing at velocity -1, exact repeat, horizontal knockback, standing and alt/morph/unmorph exclusions, jump-pad grace, deep 350-HP contact at velocity -4.75, and lethal fall. The deep fixture retains the existing scalar formula; it adds no damage cap. The lethal case must advance the authority damage sequence and publish the exact damage with no attacker or weapon identity in the latest snapshot receipt.

This is a contact-kernel and common-selection test. It does not simulate an entire falling trajectory, pad launch, knockback attack, or transformation animation. It does not certify mouse/controller/Android routing, bot behavior, recorded demo replay, or the Sylux muzzle owner/remote matrix.

Validated on 2026-10-11 against the main Debug assembly and extracted AMHE1 assets: **406 assertions passed**, exit 0. The strict 21-HP landing regression failed before the raw fall-damage scalar correction (actual debit 1 HP).
