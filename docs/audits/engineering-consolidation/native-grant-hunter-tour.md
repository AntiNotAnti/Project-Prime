# Genuine native timed-pickup hunter tour

Hypothesis: the original-source timed-grant/certificate contracts continue to
hold for every supported hunter, and genuine Cloak/Death Alt contacts preserve
native presentation/timer behavior while never authorizing Double damage. The
test source is based on integrated `88f9d29e58d0cf6254b2d33e682955bd432d0bd2`.

Existing TimedShotNativeTests proves the real sampled input/contact/emission
path for Samus and checks full canonical intent and accepted replay bytes across
Off/Shadow. NativeTimedGrantPresentationTests exercises many client projections
using declared authority facts/predictions; those seeded presentations are not
genuine item-contact witnesses for all seven hunters. This slice reuses the
existing asset-dependent native fixture and adds the missing contact matrix.

## Exact command and workload

```sh
dotnet /absolute/qualified/bin/nettest.dll --native-grant-hunter-tour \
  /absolute/licensed/AMHE1/filesystem 'MP1 SANCTORUS' \
  /absolute/fresh/evidence/native-grant-hunter-tour.json
```

Run the qualified full physical client/nettest build in the exclusive native
campaign slot. The output must be fresh; retain all attempts and actual
source/binary/asset identity. The fixture emits the loaded client SHA-256/MVID
and completed arm receipts with full item incarnation, actor life, grant
sequence, contact/effective/expiry frames, native phase counters, native emitted
shot, canonical intent and production replay callback bytes. A caught failure
writes FAIL and the completed partial denominators. No missing arm is PASS.

The matrix has seven hunters × three genuine ItemTypes (DoubleDamage, Cloak,
Deathalt) × Off/Shadow, requiring 42 genuine native contacts and actual shots,
21 exact parity pairs and all C/C+1/1799/1800 observations. ServerSim builds the
real world. The roster and normal runtime bind the actual hunter; keyboard
sampling, PlayerProcess, item factory/registration/contact, weapon Spawn/Commit
and ReplayRecorder callbacks supply the evidence. No test calls TryGrant,
ObserveInput, resource/source Seed or presentation Predict/Accept to manufacture
positive grant proof.

The fixture explicitly places each real item at the admitted actor and holds
that body at its native spawn with zero speed for the bounded timer tour. Those
are declared fixture inputs. Health, form and timed effects are not assigned to
create a success. The fixture checks actual native frame advancement and zero
unexpected simulation failures; it does not claim natural locomotion, AI or
authenticated authored UDP contact/fire. The reused helper's default Samus
tests retain their original behavior; changes are test-only visibility, a hunter
parameter and narrowly scoped body placement/post-native-press observers.

## Required positive and negative evidence

Native contact must consume the real item and create exactly the full
incarnation/life record, with C+1 activation and C+1800 expiry. Read-only queries
must find the actual automatically recorded native input as NotGranted at C,
Granted at C+1 and C+1799, and NotGranted at C+1800. Wrong-life queries cannot
borrow that truth. The genuine Double timer is 1799 at C and 1798 at C+1; its
last authorized input sees ownership before the final native decrement, leaving
timer zero after C+1799. Cloak/Death Alt timers are 1800 at C, 1799 at C+1,
one after C+1799 and zero after C+1800. Expiry must close the actual record at
its original deadline, without skipped clocks or injected timer values.

Double/Cloak emit an actual ordinary PowerBeam at C+1. Double's Shadow
certificate must bind the exact native grant sequence/incarnation and completed
component. Cloak's genuine grant cannot supply Double funding. Death Alt
forces a native morph that prevents ordinary biped firing during the grant, so
its truthful emitted shot belongs to the C input phase before contact/morph.
That certificate must remain NotGranted for Double; the test never forces a
biped or mislabels an alt-form event as a PowerBeam. Wrong-generation
certificate lookups fail. Off emits normally and stores no timed certificate.

Cloak must set the real Cloaking flag and native translucent target alpha, then
clear that flag and restore its target at expiry. Death Alt must invoke native
form transition and create the actual effect owner, then unlink it during the
next ordinary phase after expiry. These are native state/effect-owner checks;
GL color/depth/pixels, audible sound and target-impact damage are NOT_RUN here.

Each Off/Shadow pair requires exact native damage, lifespan, ammo, fire/semantic
counts, full canonical intent bytes, all accepted replay callback bytes, item
identity and timer chronology. Positive WeaponFired replay facts are mandatory.
No-target firing can have zero impact ReplayShotFacts; that is explicitly not
impact/damage-family acceptance. Balanced/affinity/turret/child/continuous and
authored long UDP campaigns remain separate fixtures/gates. This tour does not
alter production protocol, save/replay schemas, proof defaults or enforcement
rollout policy.

## Validation and rollback

Source diff check PASS. Build and actual 42-arm campaign are NOT_RUN pending the
coordinated integrated qualified build. No new native success is claimed from
source inspection. Preserve earlier Samus/presentation results with their
original scope. Roll back this fixture/dispatch/test-helper/document slice
independently; no production behavior change needs rollback.

The first qualified integrated run at `09ada0ab153dd4c15f4ea3b55473772c64f75663`
failed the Samus/Double Damage Off/Shadow pair after two completed native arms
and zero accepted pairs. The attempted failure JSON then failed separately:
general serialization recursively followed OpenTK's computed vector properties.
Preserve both the primary parity failure and secondary reporting abort; this is
not a completed seven-hunter campaign or a diagnosis of gameplay divergence.

Schema 2 converts every shot vector/target/key/incarnation to explicit scalar
DTOs and retains exact canonical bytes. Failure reporting also records scalar
fire/outcome/ammo/timer/identity values, decoded canonical intent fields and
bounded first-difference offsets/24-byte contexts/hashes for intent and replay
records. Production replay record arrays are capped at the existing 32-record
capture budget. The original parity assertion remains unchanged pending real
diagnostic evidence. Cleanup/proof-mode restoration still runs if writing a
diagnostic file fails. New-head build, diagnostic run and corrected acceptance
remain required; no source-inspection hypothesis upgrades the failed pair.
