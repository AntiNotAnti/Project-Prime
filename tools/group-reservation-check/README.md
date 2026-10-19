# Native Party group codec and allocator checks

Run with the repository's .NET10 SDK after the central Release build has produced the packet generator:

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/group-reservation-check/group-reservation-check.csproj -c Release
```

The pre-build registry check compares the actual production enum against the frozen pre-change78-name baseline. It permits only IDs80–82 and optional status flag2, checking the current protocol59. The test compiles actual packet schema, queue client, shared allocator, HTTP relay and ticket cache publication helper sources. It checks generated maximum sizes, malformed/reordered messages, exact socket ownership transfer through a closed transport, fixed FIFO deadlines, six rotations for virtual2/4/8-member cohorts, and held old-account publication.

The transport, auth environment and HTTP handler are closed facades; no socket, credentials, real backend, gameplay world or physical-device admission is exercised. `party-reservation-owner-check` separately executes the actual dedicated control partial sources. `server-browser-party-source-check` uses the centrally built application DLL to verify actual registry, codecs, status bytes, transport registration and the admission initialization fence.
