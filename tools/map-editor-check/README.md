Run `dotnet run --project tools/map-editor-check -c Release -- --roadmap-only` for the engineering roadmap regressions.

Instance probes reflect the real game ClientInstanceGuard in separate processes. The primary game owns the guard; ordinary and legacy `-mapstudio` direct probes both report refusal and a nonzero exit while that owner remains alive; normal release permits reacquisition and another clean release. A legacy flag cannot create another game guard role.

Actual legacy startup dispatch is a separate route check: it forwards before game boot and acquires no game client guard, broker or UI ownership. Studio remains an independent process. See [studio-ipc-check](../studio-ipc-check) for authenticated forwarding (60 foundation checks) and [studio-lifecycle-check](../studio-lifecycle-check) for native process coexistence and lifecycle (118 checks), plus the game [client-instance-check](../client-instance-check) for real alias blocking and crash/release recovery. This game tool does not reference the Studio application assembly.

The remaining roadmap checks preserve the original map storage, upload admission/authentication, cache lease/eviction, cancellation, identity, decoder and package/export assertions.
