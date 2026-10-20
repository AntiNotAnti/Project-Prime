# CW-05g: frozen online availability and protocol70

`DedicatedServer.RiftlineM7Available` defaults to false. It seeds the initial
match definition; an authenticated lobby owner may change the typed option via
UpdateMatch. Only the frozen non-lobby definition activates public combat and
readiness. Mutating the server default does not toggle an active round. Every
configured rotation entry must support the option before the server starts.
Public launcher controls and offline availability follow this API/network slice.

Protocol70 recognizes availability bit18 in the existing32-bit modifier word.
SessionState, UpdateMatch and discovery retain native payload widths and packet
IDs. Live69 envelopes are refused with the existing protocol reason. Reviewed
native69 archive/status layouts remain supported; historical bit18 is refused.
Current custom archives are also refused until CW-12 can reconstruct their
manifest, actions and authoritative outcomes. Native protocol70 replay support
retains all native69 bytes, stance semantics and movement version thresholds.

The shared policy admits Battle FFA and rejects unsupported loadout modifiers.
Online custom matches reject bots, both when enabling availability around an
existing roster and when adding/configuring bots in an enabled match. Native-only
bot matches retain their behavior. The availability flag freezes with the match
in both SessionState/MatchState arrival orders, including a new control packet
arriving while an old lobby session remains. A mutable lobby's provisional
control does not freeze the next round's choice.

The client derives its capability from the exact accepted frozen session/control
stream, rather than setting a sticky developer flag. Existing connection,
bootstrap, slot-generation, source-life, schema, definition digest and replica
application checks remain required. Native world admission precedes custom
capability. Native source input continues while custom readiness is pending:
freezing that input would prevent the first trusted source and create a cycle.
Empty replica baselines can commit before a source inventory exists.

Only a fresh accepted, engine-consumed source after readiness binds a life. It
receives4/16 resources and native selection. First private state for each exact
owner life waits until every current recipient has acknowledged its baseline;
availability, Equip requests and action timestamps cannot mint grants.

Late join can close the execution barrier after an existing action was bound.
For public matches, existing published lives may reserve bounded result delivery
while that barrier is closed. Unbound controls receive the existing source-
unavailable denial. Bound actions drain through the existing terminal source-
denied path before consulting the source oracle, without creating a certificate,
advancing resources, preparing a new audience or emitting damage. This prevents
an old head from waiting through client-result or journal-gap deadlines. A denied
nonce cannot revive when readiness returns; the next fresh request may progress.
Previously accepted flights retain their contact obligations.

Invalid or timed-out public readiness removes the peer with typed refusal7;
already admitted clients accept that reason on the authenticated link and expose
it to the existing launcher teardown owner. Invalid readiness is not recreated
under the same bootstrap to reset poison or grants. Native-only default matches
create no custom runtime, registry, manifest or capability. Explicit developer
fixture overrides retain their independent behavior.

Frozen client availability blocks native recording before the first replica page.
Custom replay, packages, custom bots and full platform release qualification remain
later gates; none is implied by online activation.
