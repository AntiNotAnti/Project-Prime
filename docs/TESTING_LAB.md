# Project Prime testing lab

The testing lab keeps content-free validation, local extracted-content checks,
and live client evidence separate. A green parser/unit test is not a retail,
rendered, Windows, Android, WAN, or deployed acceptance result.

## Evidence levels

| Level | What it proves | Public CI |
| --- | --- | --- |
| Schema/unit | bounded JSON, protocol, ownership, and deterministic model behavior | Yes |
| Extracted AMHE1 | the supplied extracted directory matches the frozen AMHE1 anchor/aggregate | Only when private content is supplied locally |
| Oracle record | an explicitly configured external adapter produced a normalized artifact | Manual/local only |
| Client diagnostics | one SDL host observed a device through the shipping event path | Manual/local only |
| Live E2E | Backend + Node + Worker and two free-running client processes traversed the online lifecycle | Manual/local/nightly only |

Public CI must not require a ROM, emulator, external adapter, physical
controller, display server, or deployed service.

## P0 commands

```bash
dotnet run --project src/Tools/Tools.csproj -c Release -- fidelity oracle list
dotnet test tests/Tests/Tests.csproj -c Release --filter Category=FidelitySchema
dotnet test tests/Tests/Tests.csproj -c Release --filter FullyQualifiedName~SemanticControlProtocolTests
dotnet run --project src/Client/Client.csproj -c Release -- -gamepad 10
```

`-gamepad` creates one `SdlGameHost`, pumps its existing tool event path, and
writes a privacy-safe report below the existing local diagnostics directory.
It does not create a second SDL event pump or claim physical acceptance when no
device is available.

## Local artifacts

Oracle and E2E output belongs under ignored `artifacts/` directories. Keep raw
captures and logs local. Checked-in scenario specifications and normalized
fixtures must not contain ROM memory dumps, proprietary state, serial numbers,
machine paths, user identity, or credentials.

Use the E2E coordinator only with explicit development commands and a private
token. It refuses to claim a live run when the required server/client commands
are absent.
