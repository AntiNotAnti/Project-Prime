# CW-05s — private knowledge carriage

This foundation carries private resource knowledge from original client input or
control creation through authenticated ingress, engine source observations and
retained terminal results. Live protocol advances to73. It does not activate
authoritative online world pickups, connect the pickup-knowledge admission fence
or perform its trusted reconnect/rebootstrap rebind. Public online lives still
start empty; developer fixtures retain their explicit4/16 loadout. CW-05 remains
incomplete.

Canonical `CustomWeaponAction` stays109 bytes and contains no private nonce.
Owner-only packet86 now uses `CustomPrivateAction`: that canonical action followed
by24-byte `CustomResourceKnowledge`. Knowledge is an opaque resource revision and
128-bit nonce, distinct from action sequence, source frame and native shot ID.
Empty knowledge remains valid for the developer lane. Partial revision/nonce
values refuse decoding. Possessing or transporting a syntactically valid stamp
does not establish a pickup, ownership, source eligibility or permission to spend.

The private wire shapes are:

| Shape | Bytes | Knowledge placement |
| --- | ---: | --- |
| Private action, packet86 | 133 | Canonical109, then24-byte knowledge |
| Receipt, packet87 | 233 | Existing209-byte result, then24-byte knowledge |
| State, packet90 | 146 | `HasWeapon` at113; knowledge114–137; grant world138–141; effective source142–145 |
| Control request, packet91 | 100 | Existing76-byte request, then24-byte knowledge |
| No-source denial, packet92 | 113 | Full100-byte request; token100–107; world108–111; reason112 |

Private state requires valid knowledge. Explicit absence requires empty knowledge,
zero funding floors and the normalized empty snapshot from05r. Owned developer
state may also have empty knowledge, with both floors zero. Present knowledge
requires ownership, a grant world no later than the published authority world,
and an effective source no later than the snapshot tick, using serial arithmetic
across uint wrap. These fields describe private state; carriage alone does not
apply the separate admission checks described in05q.

`CustomClientCombat` captures the current private-state stamp when creating a
control or accepting a raw action into its pending queue. Retries retain that
value even if a newer state rotates knowledge. New controls use the newer stamp.
An exact pending request can retry with its original stamp; changing the stamp on
the same request is a conflict. Receipt consumption checks the pending stamp as
well as the canonical request/action join.

Native source input has its own creation-time latch. `NetHooks` captures
`CustomCapturedKnowledge` beside `CaptureIntent`, before post-step fire history is
attached. The immutable value names the full owner, bootstrap, manifest, knowledge
and exact source frame. Encoding checks that scope and source against the intent,
and checks that the independently supplied compact native bytes name the same
match, epoch, generation, life and frame. It never substitutes the latest state
stamp at send time.

Packet93 carries source knowledge independently of an optional repeated control.
Its first65 bytes are the complete bootstrap/manifest header, byte65 is a strict
control-present boolean, bytes66–89 are source knowledge and bytes90–91 are the
little-endian native payload length. Without a control, native bytes begin at92.
With a control, its100 bytes occupy92–191 and native bytes begin at192. A fresh
source can therefore carry new knowledge while repeating an older control with
its original stamp; a source can also carry knowledge without a control.

The realtime payload ceiling remains1176 bytes. When source knowledge is present
and the native payload fits the92-byte prefix but not the192-byte prefix, encoding
omits only the repeated control. Reliable packet91 retains that request. Exact
native identity validation still runs before this fallback. Native fire history
is never trimmed, reconstructed or converted into a custom action. If an envelope
cannot be sent, the ordinary native Intent path remains available.

On the server, both envelopes enter the same native admission and relay path.
Only a genuine `NewCurrentCarrier` records its knowledge and the actual ingress
world in `CustomPeerSourceBoundary`; a duplicate cannot upgrade either value.
The engine constructs its usual metadata-free source candidate, then
`TryAcceptedInput` recovers the original accepted stamp/world only when its base
owner, source, ACK, pose, aim and buttons match. The immutable engine observation
retains that original evidence. Relayed SlotIntent contains only the original
compact native payload.

The source journal stores pending knowledge and a parallel bounded128-entry
terminal history, leaving canonical actions and receipts nonce-free. An exact
duplicate retains its original stamp; changing knowledge on the same pending or
retained terminal action returns Conflict. `TryKnowledge` requires the exact
canonical action. These terminal joins remain available after journal retirement,
preventing a retry from replacing an old verdict with a newly stamped denial.

Outbox reservations bind the same private value. Publication requires that exact
value explicitly; the legacy publication overload supplies empty knowledge rather
than inferring current inventory. Reliable retries preserve their original
private payload. After an acknowledged outbox obligation is freed, recovery uses
the journal's retained canonical terminal and knowledge. Retained control conflict
checks also run before reserving a replacement obligation. A terminal retry cannot
retag the old receipt, debit again or emit again.

Private nonces do not enter canonical public actions, contact identities,
application ACK identities, native CompactIntent/SlotIntent bytes or public custom
replica facts. The live handshake/version allowlists advance to73; native packet
bodies and replay payload shapes remain unchanged. Custom replay remains blocked
until CW-12.

The focused checks cover private codec boundaries, independent input/control
stamps, creation-time retention, source metadata recovery and terminal retry
binding. The dedicated ingress fixture uses actual UDP and owner-thread engine
observations, but its developer loadout, native ACK/body/aim setup and knowledge
tokens are explicit test seams. It is not evidence of online pickup funding or
knowledge-fence admission. Build/run results belong in the separate evidence
record; this implementation note makes no device or completion claim.

The earlier public-availability combat suite still has a known FAIL inherited
from05r: it assumes an automatic4/16 spawn grant. That fixture must acquire through
actual authoritative world contact before its public combat matrix can qualify
the corrected acquisition flow. It is not replaced by developer-loadout results.
Authoritative online contact/funding, reliable item publication, admission/rebind
integration and the remaining device/refresh acceptance are still outstanding.
