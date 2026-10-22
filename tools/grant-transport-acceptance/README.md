# Grant transport acceptance diagnostic

This standalone, device-free control exercises the supplied frozen client's
production `NetTimedGrants`, `NetGrantNetwork`, `NetRetainedDelivery`,
`NetFactBatch`, `NetTransport`, `NetConnection` and reliable channels. It opens
one server and three client UDP sockets on ephemeral loopback ports. Socket
relays deliberately reorder two grant datagrams, duplicate one, lose ACKs,
reject an unknown sender/wrong connection incarnation, and cut one peer's link.

The seed consists of explicitly synthetic value inputs: eight complete lives,
three timed types per life, room 3, source/contact frame 100, expiry 1900, and
bootstrap authority frames 700/900. Reservations use the production pure ledger;
there is no asset-loaded scene, native contact geometry, normal AI pickup,
movement proof, GPU/audio device or accepted-shot damage authorization. A PASS
qualifies grant codec, identity-bound UDP delivery, batching and finite recovery
only. Natural native three-type coverage remains NOT_RUN. High-level hosted and
dedicated gameplay admission are separate from this transport control.

The control requires 24 unique states in exactly two application datagrams,
complete reliable wire lengths 1194/134 bytes within the 1200-byte bound, positive
wire fault counters and actual retransmission, an independent late-join baseline,
and real 15-second application/reliable failure signals for a deliberately
backpressured peer. Healthy peers continue their ordinary production pump during
that wait, retain identical confirmed state, and receive a subsequent terminal.
No private production clock or failure watermark is backdated. A transport
admission transfers bytes into reliability; it is distinct from receiver delivery.

The assembly uses the existing diagnostic friend name `nettest`. Build against
one supplied physical candidate; `Private=true` copies only its client assembly.
Managed dependencies resolve from that candidate directory, so this tool neither
rebuilds the client nor copies its full runtime tree. Startup checks the loaded
physical client SHA-256 against the supplied file and records both MVIDs/build IDs.
Each invocation uses a fresh `PROJECT_PRIME_USER_DATA` directory and process cwd.

```sh
dotnet build tools/grant-transport-acceptance/grant-transport-acceptance.csproj -c Release \
  -p:AcceptanceBinaryDirectory=/absolute/frozen/candidate -p:OutDir=/absolute/tool-output/
dotnet /absolute/tool-output/nettest.dll \
  --binary-dir /absolute/frozen/candidate --receipt /absolute/receipt.json
```

The receipt is preserved on a fixture failure and the process exits nonzero.
The expected stalled-peer transport failure is part of the positive control.

An earlier development fixture omitted healthy-peer servicing during the stall
and consequently failed at the next terminal; its receipt is retained externally
as `work/grant-transport-first.json`. It is excluded from acceptance. The second
run fixed that omission and passed, but its bootstrap lengths list continued to
mutate with later phases; `work/grant-transport-corrected.json` is retained as
instrumentation-negative evidence. The final implementation snapshots that list.
Use the committed result receipt's source/client/tool hashes and scope, and
rerun after any client or diagnostic change. These controls do not establish
full A3 acceptance or a natural native gameplay campaign.

The committed [result receipt](result.json) qualifies the code source in its
`diagnosticSourceCommit` field against frozen client `0c2eefb5`: 303 passing
checks, 24 states in two bootstrap application datagrams, eight observed
retransmissions after four lost ACKs, one positive reorder/duplicate control,
and explicit stalled-peer failure at approximately 15 seconds with unchanged
healthy state. The receipt remains limited to that exact frozen client.
