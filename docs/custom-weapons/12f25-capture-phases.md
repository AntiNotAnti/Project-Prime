# CW-12f25 — shared capture phases and asynchronous format8 storage

This slice adds explicit live capture APIs for the phase contract qualified in
12f22–24. It does not activate a dedicated authority recorder. Validation and
limits are recorded in [the evidence](evidence/CW-12f25.md).

`ReplayCaptureRecord` carries an immutable accepted timeline fact and its ordinary
or post-world disposition. Each queued owner retains its own payload reference;
the synchronous Scene/session step borrows its batch. `ReplayCapturePhaseClock`
classifies only completed frame N or next frame N+1 and completes contiguously.
An eventual producer must use that single decision for live reconstruction and
disk enqueue. Source recording and authority clocks remain unchanged.

`CreatePostWorldInspection`, `RestorePostWorldInspection` and
`StepPostWorldInspection` are separate explicit APIs. Before step N+1, the session
prevalidates the entire batch, then accepts deferred N facts in order followed by
ordinary N+1 facts. The capture owner retains post-world N+1 for the following
step. Both record count and byte budgets apply across the combined batch. A fresh
seed permits one empty initialization at its own frame; a restored completed
world can only advance to its next frame. Malformed phase/source/order or an
undeclared timed-shot capability closes the private session before accepting a
batch prefix. Semantic markers remain metadata.

`ReplayWritePump` explicitly supports format8 and caches its exact custom scope
and timed-shot declaration. Producer admission uses the same strict canonical
payload whitelist and archive capability validation as live reconstruction.
Ordinary records belong to the next queued world; post-world records belong to
the successfully queued completed world and precede next-world ordinary records.
Only a successful frame enqueue advances that frontier. Complete refuses accepted
future work without a completed frame. Retained asynchronous payloads include the
post-world envelope in their queue cost and are released on success or failure.
The worker writes one final quiet marker, including an empty recording at zero.

Metadata explicitly selects custom format8 from a phase Scene or an exact custom
world without lead-in. Default custom metadata and existing client capture remain
format7. Native wire, checkpoint17 and World12/13 bytes are unchanged. Production
format8 activation, authority resource ownership, ordinary player/theatre admission
and bots remain subsequent CW-12 work. Installation and recording do not grant or
equip a weapon. Android remains deferred.
