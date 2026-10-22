# Native Party actual loopback control integration

This Mac executable references an existing compiled application DLL. It contains no replacement server, Peer, allocator, packet codec, transport or queue client. The original test thread drives the actual `DedicatedServer` control methods in the same control order as `Run`, and real `NetTransport` workers exchange UDP on assigned ephemeral ports.

The only closed boundary is an injected `HttpMessageHandler`: it accepts only `https://closed.invalid/reservation`, uses an explicitly synthetic reporter through the existing credential owner, and checks exact request/Party/travel/match/epoch/member/ticket/slot/claim witnesses. Synthetic commitment is recorded before held activation/claim replies can be released. Real Edge HMAC and SQL acceptance are separate suites.

The production `_controlPlaneOnlyForTests` seam suppresses asset prewarm in packet handling. Only the actual reservation-maintenance invocation temporarily sees the normal flag so its original capability worker can verify the closed backend. The flag is restored synchronously on the same owner thread. This fixture does not run `DedicatedServer.Run`, construct a gameplay world, or test real backend/account/device admission.

The final run passed650 assertions, including six requests each for2/4/8 local participants, held activation acknowledgement, explicit Accept, actual ordinary Welcome/Peer/occupant generation, exact `TakeAdmission` socket transfer, cancellation before and after the short claim, a new actual Peer occupying released capacity before an old claim completes, malformed/stale wire refusal, the actual bootstrap/unproven bound and absolute5second lifetime, and a blocked head group ahead of a younger solo queue.

Build independently with SDK10; the project has no ProjectReference and cannot build the application:

```sh
/Users/jarrett/.dotnet/dotnet build tools/party-reservation-loopback-check/party-reservation-loopback-check.csproj -c Release -p:BuildProjectReferences=false
```

Run only with fresh private cwd/userdata and the explicit synthetic `PROJECT_PRIME_CAREER_SERVER_KEY` literal checked by the fixture. The durable run receipts/logs are in `artifacts/rmlui-party-reservations-v1-rml58/actual-loopback-attempt*.{json,log}`. Attempt1 passed639; attempt2 preserved a fixture-only asynchronous acknowledgement timing failure; attempt3 passed650 after bounded waiting for the actual acknowledgement. All use the recorded immutable Mac DLL SHA. No NetLaunch/client-world/GL/performance or physical-device claim follows from this control test.
