# Native Party reservation control-owner fixture

This isolated executable links the actual server reservation, waitlist and Social membership partials, the existing FIFO/group allocator, both HTTP relays, the membership pump, and generated reservation/queue codecs. Packet IDs come from `../group-reservation-check/Registry.generated.cs`; its existing Python registry check runs before compilation. The installed stable packet-generator DLL is an analyzer. Production sources are not copied or rewritten.

Run from the repository root with SDK 10.0.401:

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/party-reservation-owner-check/party-reservation-owner-check.csproj -c Release
```

All waitlist/control-owner operations run synchronously on the executable's original thread. Worker HTTP responses use held task completions. Bounded polling sleeps one millisecond and never transfers waitlist ownership to an asynchronous continuation.

The fixture currently checks 75 assertions covering:

- Tentative all-member capacity holds and no `Reserved` publication before activation acknowledgement.
- A remote activation committed before its response is released; cancellation uses that exact real reservation ID, persists while eight workers are occupied, retries a refusal, and prevents delayed acknowledgement from restoring local capacity.
- Explicit Accept retains its original claim nonce when worker dispatch is saturated, retries within the original two-second budget, and expires visibly when capacity never becomes available.
- Self-decline after a claim retains the exact queue peer and short hold until the claim settles; pending self-decline preserves the frozen leader-owned cohort.
- Actual peer acknowledgement can be blocked by worker saturation, refused, and retried without another ordinary admission callback, peer, session, or group cancellation.
- Match/epoch rollover, connection replacement, and slot reuse reject stale worker completions.
- The actual occupancy refresh excludes existing humans and bots, and a full lobby allocates no partial cohort.
- An actually verified Waiting attachment survives the unproven five-second cap. Verified attachments do not consume the separate 16-unproven classification allowance; a fresh unproven peer expires at five seconds.

`Facades.cs` explicitly supplies the missing outer server fields, peer objects, transport connection/publication boundary, reporter configuration, and ordinary `HandleHello` boundary. The `HandleHello` facade records one admission and emits the expected Welcome shape; gameplay construction and real transport/session ownership are not simulated beyond that boundary. The token-bucket facade permits calls, so this fixture does **not** validate the independent eight-per-second bootstrap bucket algorithm. The 17-already-verified-peer classification test seeds that boundary state directly; the separate Waiting attachment case executes actual validation and attachment logic.

`ClosedBackend` accepts only the `closed.invalid` HTTP handler, records bounded JSON requests, preserves fixed activation/claim response deadlines, and models a committed reservation ID and cancellation tombstone. There are no sockets, real HTTP requests, credentials, HMAC authentication, SQL execution, or physical-device claims here. The independent Edge and actual saved-SQL suites cover authentication and database gates.

Latest local log: `/tmp/prime-party-reservation-owner-check.log`.
