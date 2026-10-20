# CW-04m atomic native input and custom control carrier

Protocol 65 adds packet 93, an unreliable realtime input envelope. It contains a
two-byte little-endian native length, the existing 76-byte primary control, and
unchanged CompactIntentCodec bytes. The complete datagram stays within 1,200 bytes
including NetHeader. Native packet layouts, native shot identities, existing
control/result types and replay bodies are unchanged.

The client repeats its oldest pending primary control with ordinary fresh native
input until an application response consumes it. Reliable packet 91 continues to
retry independently. The existing native producer still fills and encodes the
entire native frame first. If the wrapper cannot fit, it sends that original
native frame; no fire events, input history or touch reports are removed to make
space. This avoids extending the native realtime MTU or changing native recovery.

The bounded codec validates complete lengths and the full request/native match,
epoch, generation and life join before mutation. Slot comes from the authenticated
peer. For an active custom owner, exact bootstrap, manifest, runtime and actual
connection must also match. A contradictory envelope is refused as a whole.
Native movement continues when the custom lane has retired or room capability
temporarily prevents custom admission; losing custom readiness cannot freeze the
ordinary native input path.

The server captures the current accepted-input serial before admitting the native
carrier. Both envelopes then use the same native acceptance, timing, recovery and
relay implementation. Only its existing new-current-carrier result may admit a
previously unseen custom nonce using that earlier serial fence. An old/duplicate
native carrier can recover ordinary native events but cannot retrofit a new custom
trigger into previously frozen history. Relay remains the ordinary SlotIntent
containing the same native presentation bytes, without a custom/native identity
conversion.

The existing CW-04l source callback freezes the first actually consumed source
after admission, with the full immutable custom request identity. If earlier
carriers were coalesced or lost, only the later row that actually reached the
engine supplies body, aim, ACK, eligibility and modifier evidence. There is no
claim to the skipped row's original tap-time geometry. First negative evidence is
terminal; repeats cannot promote it after thaw or respawn. Same-world geometry
still waits until the next world before processing. Positive pre-death source
facts remain subject to the existing canonical death/trade ordering.

An exact retained nonce on a fresh carrier does not consume another semantic
offer or refresh any fence/deadline. This matters because native input repeats
at 60Hz and must not starve the existing 64/s action and application-ACK quota.
Changed or previously unseen nonces still consume that quota and require reserved
result/contact capacity before effects. Native input admission runs first even
when that custom quota is exhausted. Native transport ACK is never an action
verdict; only the existing application result path retires the client request.

The source clock, fixed life grants, one-head-per-new-observation ordering,
source-wait/world/wall deadlines, resource journal, geometry/ACK policy, mandatory
trade and canonical one-use payment are unchanged. This is a developer-only
transport/ordering improvement; both activation flags still default off. It adds
neither a historical source hint nor a caller-provided proof witness.

Remaining CW-04 gates include full recipient-bootstrap state/event replication,
spectator/reconnect/late-join baselines, local/bot source parity and final host/
remote fairness acceptance. Original hardware tap timing is not reconstructed.
Public controls/HUD and custom replay remain later slices; physical/rendered
acceptance is not implied by codec, headless or UDP checks.
