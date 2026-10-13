# Native timed grant acceptance diagnostic

This independent tool compiles against an explicitly supplied frozen client. It
does not build ProjectPrime, restore its packages, copy game assets or duplicate
the dependency directory. The client and tool sources must be committed and the
client rebuilt before immutable acceptance evidence is claimed.

```sh
dotnet build tools/timed-grant-acceptance/timed-grant-acceptance.csproj -c Release \
  -p:AcceptanceBinaryDirectory=/absolute/frozen/candidate
```

Stage only the small tool DLL, runtimeconfig and deps files alongside the reused
candidate libraries, or supply the candidate directory to the built tool's
resolver. Native dependencies must remain available exactly as for the client.

```sh
dotnet timed-grant-acceptance.dll \
  --binary-dir /absolute/frozen/candidate \
  --expected-client-sha256 EXACT_PROJECTPRIME_DLL_SHA256 \
  --assets /absolute/licensed/profile \
  --room 'MP5 FUEL SLUICE' --output /absolute/new/evidence-directory \
  --replay /absolute/genuine-a3-recording.ppdemo --clip /absolute/genuine-exported-clip.ppdemo
```

This launches an actual RenderWindow and client Scene with a real GL context,
loaded native HUD, decoded assets and an actual SfxInstance. Headless mode and
silent audio fallback fail the check. A bounded backend decorator delegates
every operation to the real sound backend while recording value-only call IDs;
OpenAL channel states are read back. It does not fabricate handles or sounds.
World/HUD PNGs and raw world depth are read from the actual framebuffer. These
are inspectable stage artifacts, not a claim of golden-image parity.

The fixture deliberately injects authority scope, item birth and grant/verdict
facts through the existing APIs. The owner materializes a real dynamic item and
uses actual OwnerContact, native simulation, effect/HUD/audio stages and firing.
The client role stays a client during provisional-versus-confirmed damage checks.
No socket or live UDP campaign is started. The actual DeathAlt collision invokes
CheckPlayerCollision against overlapping loaded player volumes, then records
native/client-predicted victim debit; it is not authority combat payment proof.
Health, life, position, protection release and clocks are explicit fixture inputs.

All three effects exercise initial native contact, positive projection,
contact-frame versus next-input confirmation, delta/verdict ordering, pending
renewal/refusal, confirmed renewal, 420/240/1/0 remaining stages, quiet resume,
replicated death, new slot/life and discovery room fencing. The room-fence case
changes the decoder scope while keeping the loaded map; it is not a rendered
room-load/rotation campaign. Discovery must clear old effects without inventing
new grant coverage. Real beam/bomb emission tests Double Damage denominators and
actual emitted damage. Repeated confirmation, verdict and quiet resume must not
restart the native pickup flash or gain cue.

The narrow PlayerEntityTimedGrants fix restores `_timeSincePickup=0` at the
existing first-gain cue gates. An unpatched compatible A3 client fails the first
provisional HUD assertion after saving its baseline framebuffer/depth evidence.
Matching confirmation/verdict/Keep and quiet Resume are negative assertions;
they do not reset the flash. Existing A3 Retime/renewal pickup-cue policy is
preserved and must not be described as baseline audiovisual renewal parity.

`result.json` always retains checks completed before failure, client path/MVID,
expected and before/after client hashes, timestamps, stage/audio evidence and
the exception. Evidence directories must be fresh; old results are never replaced.
Exit 0 means every implemented required gate passed, 1 means a failed assertion,
and 2 means implemented checks passed but required coverage remains incomplete.
Native calls must run serially with other native campaigns.

When genuine `--replay` and `--clip` files are supplied, the optional probe opens
them through the real asset-backed PassiveReplayScene/PassiveReplayPlayer path.
It compares native timers with their actual private decoder views, requires
positive confirmed coverage for all three types, keeps live facade/presentation
identities unchanged, and compares genuine native seek gameplay hashes plus
exact captured world/HUD PNG and depth bytes against independent linear playback.
It does not seed invented replay facts. A file without a positive effect leaves
that type NOT_RUN; missing paths do not turn into synthetic success. It retains
the private replay camera/audio policy, so it does not claim player-perspective
pickup/loop audio from a silent private presentation.

Current broader exclusions are explicit: replay player-perspective stage audio,
historical capsule audiovisual parity, rendered room transitions, live UDP
owner/observer settlement, authority damage payment, authored-spawner quota and
the full profile matrix. Those are NOT_RUN; sampled timers or injected facts
cannot close them. In particular, ServerSim's headless/silent backend cannot
substitute for this client audio test. Images/assets/binaries belong in external
evidence directories and are not checked into Git.
