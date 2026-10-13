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
The fixture admits its two slots through ApplyRoster and NetSlotManager.Sync
before invoking actual native Spawn at authored respawn points. This matters:
Sync calls Initialize on first admission, so manually incrementing a life before
admission lets the next full frame clear native health after the setup check.
The new slot/life case also uses actual Spawn, rather than counters alone.
Value-only `Phases` receipts record health, native in-play/load/death/form flags,
life identity, native/model timers, prediction/confirmation state and fixture,
network and scene clocks before and after simulation/contact/projection. A
failed provisional assertion therefore retains its exact state. These fixture
changes leave the production alive and exact-scope gates intact. The corrected
fixture can run against compatible original A3 31cdd APIs; its first HUD-flash
assertion precedes the later, new room-fence API. Native rerun is required; source
review alone does not validate this correction.

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
aggregate positive confirmed coverage for all three types across the supplied
files, keeps live facade/presentation
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


Optional controlled authored-contact recordings use a separate route:

```sh
dotnet timed-grant-acceptance.dll \
  --binary-dir /absolute/frozen/candidate \
  --expected-client-sha256 EXACT_PROJECTPRIME_DLL_SHA256 \
  --assets /absolute/licensed/profile --room 'MP5 FUEL SLUICE' \
  --record-authored-effect DoubleDamage --output /absolute/fresh/dd-evidence
```

Run separate Cloak and DeathAlt campaigns on maps that actually contain those
authored items (for example MP2 HARVESTER and MP6 HEADSHOT). The route registers
only loaded authored ItemInstanceEntity actors with real spawner owners. It
places the native owner at the selected item's actual position, then steps full
host-authority native simulation. The actual PickUpItems contact must consume
that item and create a matching NetPowerUpLedger history entry. That production
ledger publishes its real facts to ReplayCapture and DemoRecorder; this route
never constructs TimedGrantFact or calls presentation.Accept. The receipt keeps
loaded room/content identity, profile path, spawner, grant sequence/spawn/life,
source/contact clocks and explicit fixture positioning/host authority labels.
It is controlled native contact, not normal AI or live UDP acceptance.

For snapshot capture only, the tool calls the existing dedicated-authority
BroadcastSnapshot serializer under its required Server role and restores Host
immediately. Its real bytes go through production RecordOwnSnapshot and a
value-only diagnostic sink. Actual host simulation is unchanged. Recorder
bootstrap world and nonzero serialized snapshot count must exist, or the route
retains NOT_RUN. Production full recording and instant clip writers must finish
without error; genuine files are copied into the fresh evidence directory and
hashed. The generated recording and clip are then opened by the same native
linear/seek probe. Missing native contact fails; missing authored item,
bootstrap, or finalized files remains explicit incomplete coverage.

`--replay` and `--clip` may each be repeated. Per-file positive counters remain
in each receipt; final aggregate counters require all three positive native
types across genuine supplied/generated files. A DD-only genuine file is never
reported as having Cloak or DeathAlt. To close aggregate recording coverage,
supply the prior genuine Cloak/DeathAlt files along with a DD run (and the prior
clips through repeated --clip arguments), or run the normal client fixture with
all three recordings and clips. Exact native seek image/depth requirements are
unchanged. Player-perspective replay audio, old-format compatibility, live UDP
and authority combat payment remain separate unclosed gates. The controlled
recording route also explicitly leaves the injected client prediction/HUD
fixture NOT_RUN; it cannot substitute for that separate run.

Source review of this optional route is not native acceptance. Compile and run
against the exact committed candidate serially, retaining all failures, before
claiming contact, recording, clip or seek evidence.


Native replay readbacks retain value-only actor, form, camera, model-animation,
material, effect-particle and RNG state before drawing, after drawing and after
an immediate repeat draw. Float receipts include raw IEEE bits. Full actual
native checkpoint bytes are also captured and hashed at those boundaries; they
are diagnostic captures only and are never restored or adjusted by the tool.
The immediate repeat readback must be exact, independently of the separate
linear-versus-seek comparison. This counterfactual can falsify same-state render
stability while preserving the original exact seek failure. It does not introduce
pixel tolerance, normalize camera/effects or change production checkpoints.


Additional native diagnostics retain prepared draw-item matrices/material state
and read actual uploaded RGBA texture/mipmap bytes by logical model/texture/
palette/recolor source, with hashes and actual GL sampler parameters. The tool
restores texture unit/binding state after those read-only queries. Allocation-
local GL handles are recorded for inspection but are not treated as differing
asset content. Dither and multisample enablement are observed, not changed.
These receipts do not disable rendering features or soften exact image parity.


`--replay-original-opaque-order` is an explicit COUNTERFACTUAL_ONLY diagnostic.
After ordinary native draw preparation it restores the opaque retained packets'
original Sequence order and calls the existing production adjacent-batch builder
on that order. Prepared batch plans are discarded so they cannot describe the
old permutation. No texture/camera/checkpoint/effect state is normalized. The
receipt preserves before/after actual retained packet sequences and marks this
route ineligible for production rendering acceptance even if exact readbacks
match. Without the flag, submission remains untouched. This isolates allocation-
local state-key sorting from exact native restore/texture content evidence.
Actual retained opaque/decal/translucent packet sequences and full item state are
also retained in ordinary diagnostics to compare the submitted permutation.
