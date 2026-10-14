# NET-03 source capability classification

Status: **implemented source; new pure/native gates NOT_RUN**. Base source is NET-02 `bf3c144178e70b5bdcaec334b79e2980cdb56405`. Runtime execution awaits the exclusive test slot. This change does not enable general timed Enforce or claim NET-03 source-family completeness.

`NetTimedSourceCapabilities` describes the evidence already present. It does not authorize damage or change source/grant/payment state. The four statuses are:

| Status | Meaning |
| --- | --- |
| `SUPPORTED` | An ordinary originating receipt contains verified source and known, consistent grant funding or known absence. A false Double hint cannot turn absence into funding. |
| `SHADOW_ONLY` | A dependent/special family remains observational; its pulse/child/turret/other source or impact contract is incomplete. |
| `UNAVAILABLE` | The required retained source, completed emission, funding or exact impact fact is absent or inconsistent. It is not known absence and not physical proof. |
| `NOT_APPLICABLE` | The timed proof scope is Off, or this native source is not a player source. No timed authorization is inferred. |

## Source-backed family matrix

| Actual family / path | Originating modifier declaration | Exact impact evidence | Scope |
| --- | --- | --- | --- |
| Ordinary straight/projectile | `SUPPORTED` only with completed verified source and known consistent funding/absence; otherwise `UNAVAILABLE` | `UNAVAILABLE` | Native channels are frozen after charge, row, scope, approved modifier and Balanced tuning. They are launch amounts, not collision witnesses. |
| Uncharged PowerBeam / VoltDriver head | Same ordinary modifier rule | General impact `UNAVAILABLE`; the existing NET-02 helper can separately prove the same-ACK first segment | Later flight/charged/child geometry is not extrapolated. Unknown head geometry uses the existing normalized body fallback. |
| Imperialist | Same ordinary modifier rule | General impact `UNAVAILABLE`; existing NET-02 head ray is a separate historical geometry check | ACK/subframe and captured ray drive head geometry, never claimed body origin. |
| Homing / ricochet origin | Known originating modifier may be `SUPPORTED` | `SHADOW_ONLY` for downstream impact | An origin grant does not prove its later path, bounce or target. |
| Child / secondary | `SHADOW_ONLY` | `SHADOW_ONLY` | Keep the existing inherited full launch key and hierarchical component ID; no new grant or current-life rebinding. |
| Continuous / turret | `SHADOW_ONLY` | `SHADOW_ONLY` | Existing tick payments/heal and native turret outcomes remain separate owners. No generic originating certificate is invented. |
| Judicator / Omega / non-shot | `SHADOW_ONLY` | `SHADOW_ONLY` | Preserve baseline behavior until a distinct native source/impact contract is validated. |
| Replica/non-player/Off | `NOT_APPLICABLE` to timed declaration | `NOT_APPLICABLE` | Instrumentation is inert outside the eligible authority telemetry path. |

`ConstantDirectBodyChannel` is an independent arithmetic fact: the completed body's native channel has no direct-distance falloff and no Balanced range curve. It neither validates the selected head/splash channel nor proves that an impact occurred. No payout is labeled exact merely because this flag is true. Actual paid native outcomes remain authoritative in the existing `NetCombatPayments`/`NetContinuousPayments` terminals, which this classifier does not read, mutate or replace.

## Native telemetry

`PRIME_NET2_EVIDENCE` already opts in to campaign telemetry. After native `AddEntity` terminals, the classifier copies the existing `beam.ModLaunchKey`, component ID and actual family into a fixed **8 × 256** ring. Parent, player, beam and scene objects are never retained. A completed originating certificate can annotate matching retained values with its exact source frame and modifier classification. No heap object is allocated by the per-emission recorder; report serialization allocates only when the campaign report is captured.

The ring is diagnostic, not authorization storage. Replaced entries are counted as `ReplacedTelemetry`; older samples cannot be treated as retained evidence. Counters distinguish observed native terminals from completed originating certificates. Effect kernels that do not reach `AddEntity`, failed spawns, non-shot contacts and unapplied claims are not represented as successful originating emissions. The serialized `TimedSourceCapabilities` field is a campaign report addition; packet and replay formats are unchanged.

## Existing dynamic pickup capability and new canary

The A2/A3 dynamic incarnation feature is already implemented:

- `NetTimedGrants.RegisterSpawn` assigns a monotonic full key even when native drops share `SpawnId = -1`.
- `NetPickupNetwork.PublishItem/Join/Pump` publish and bootstrap reliable retained item facts. `NetReliableChannel.IsReliable` includes item, claim and verdict explicitly.
- `TimedPickupProjection.ApplyDynamic` materializes one instance per full key. `OwnerContact` reads `item.ModPickupSpawn`; it does not match by position.
- `ItemInstanceEntity.Destroy -> NetPowerUpLedger.Remove` publishes exact-key removal. `PickupItemHistory` prevents consumed identity resurrection.

The extended `NativeTimedGrantPresentationTests` creates two real authority instances at an identical position, admits their actual publisher bytes, projects distinct client instances and invokes actual owner contact. It checks duplicate contact/discovery, exact claims, native destruction, sibling preservation and no resurrection. A deliberately newer contradictory birth is retained as an expected negative failure callback. The fixture explicitly switches in-process roles and uses retained-delivery admission; it does not prove authenticated UDP, natural player input, settlement, rendered twins or packet loss recovery. Its existing `DesktopGraphicsSession` requires the native GPU slot.

## Exact damage scope retained from NET-02

Client `HitPoint` is body origin (`NetHitPrediction.Declare`), while native direct falloff uses projectile Position and Balanced direct range uses actual collision position. Splash attenuation needs a distinct native blast origin/radius/type; Balanced splash range uses launch-to-blast distance. NET-02 therefore retains a conservative launch-channel ceiling rather than inventing those distances from a claim. Exact distance-dependent impact support remains `UNAVAILABLE`/`SHADOW_ONLY` here. `NetSelfSplash` supplies only its explicit self-impact scope and must not be relabeled as general enemy-splash proof.

## Qualification gates and rollback

New gates are pending: `--timed-source-capabilities` (constructed pure classification), the extended `--native-timed-grant-presentation <assets> [room]` canary, and current source build/appropriate regression checks. Pure cases do not establish a real native source or physical payout. Enforce remains fixture-controlled; environment parsing still accepts only Off/Shadow. Opt-in telemetry never selects gameplay policy.

Rollback removes the classifier, its telemetry hooks/report field, pure dispatch/CI route and native canary together. Existing NET-01/NET-02 source certificates, sanitized modifiers, grant/incarnation discovery, packet/replay bytes and exact-once payment machinery remain unchanged.
