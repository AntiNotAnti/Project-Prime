# CTF bot startup crash — 2026-10-22

The delivered Windows build reproduced a `NullReferenceException` in
`PlayerAiData.Func3_213BAF4`: eight-player CTF with bots selected the native
Capture personality tree, but `InitializeMain` initialized its flag/base
references only for ordinary Capture. The same map without bots loaded normally.
The reported Alinos Perch and Fault Line failures share this missing mode guard.

The repair adds CaptureTheFlag to the existing Capture objective initialization
and Capture-only selector branch, and routes bot flag interaction through
`CtfRules.Active` rather than the CTZ/Payload `HeroRules.Active` check. Native
personality trees, navigation, pickup authority and incompatible-map safeguards
are retained.

`-ctfcheck` now also creates actual CTF flag/base entities and eight player AI
objects in a controlled asset-free scene. It executes production objective
initialization and the formerly crashing selector for both teams, normal Zoom
pickup/release input, the Capture versus Bounty selector behavior, and the
existing missing-objective disable/early-exit behavior. This does not claim a
complete personality, simulated match, render or online acceptance run.

The existing live regression commands, with isolated `PROJECT_PRIME_USER_DATA`
pointing to a profile containing the operator's own compatible game data, are:

```text
ProjectPrime.exe -ctfcheck
ProjectPrime.exe -maptest "CTF1 FAULT LINE - EXPANDED" -mode CaptureTheFlag -players 8 -seconds 8 -bots -debuglog
ProjectPrime.exe -maptest "AD2 ALINOS PERCH" -mode CaptureTheFlag -players 8 -seconds 8 -bots -debuglog
ProjectPrime.exe -maptest "CTF1 FAULT LINE - EXPANDED" -mode Capture -players 8 -seconds 8 -bots -debuglog
```

Fault Line's unexpanded key and Combat Hall lack the required CTF objectives;
they are not substitutes for the expanded-map reproduction. Fresh build and
actual bot-match results belong in the associated validation receipts.

Validation completed: Windows Game/Studio, server and Android builds passed; CTF checks 213/213 and arena catalog checks 18/18 passed. Final native runs used balanced teams and required enabled objective bindings for every bot: Alinos Perch and expanded Fault Line each completed 1,800 frames with seven of seven native bots ready, moving and firing. Ordinary Capture passed with three of three bots ready; DUST2 Battle passed with all eight players spawned, moved and fired. Both packaged application heads passed compatibility and native window checks. The tested ctf1 package was installed into Desktop/PP; all 3,730 unowned user files were preserved byte-for-byte. Full original installation backup and validation receipts remain in the ignored local task folders.
