# Metroid Prime Hunters Recomp reference

This document records the external reference boundary used by Project Prime's
P0 tooling. It is a behavioral reference only; Project Prime remains the
authoritative implementation for its modern SDL client and Backend + Node +
Worker multiplayer architecture.

| Field | Frozen value |
| --- | --- |
| Repository | `mstan/MetroidPrimeHuntersRecomp` |
| Reviewed commit | `a92c401af2552989421668364c63ddc9b94f0765` |
| Reviewed | 2026-09-14 |
| Recomp target | USA Revision 0 (`AMHE0`) |
| Project Prime target | USA Revision 1 (`AMHE1`) |
| Project Prime review boundary | HEAD `8a501aa8846915fa6b6d9b315934bead565e9637`; concurrent dirty edits are outside this reference review and are preserved |
| SDL runtime | 3.4.16 (the existing shipping decision) |
| Code copied | No |

## Ideas used

The P0 work independently implements bounded scenario/checkpoint artifacts,
first-divergence comparison, free-running multi-client control, and SDL-owned
controller diagnostics. These are engineering patterns, not a port of the
Recomp runtime. No ARM9/ARM7 recompilation banks, melonDS/BIOS/firmware code,
DS rendering, Nintendo WFC/local-wireless code, savestate code, or Recomp
runner code is included.

The Recomp source is MIT-licensed. Its documented distributed binaries are
combined works with the linked `ndsrecomp`/melonDS runner and carry
GPL-3.0-or-later implications. Project Prime therefore keeps this work
independent and does not copy runner/framework implementation. Any future
adaptation of title-local source requires a separate attribution and license
review.

## AMHE1 evidence boundary

Project Prime's existing fidelity baseline freezes the AMHE1 extracted-content
identity used by the oracle parser:

```text
reference revision: AMHE1
anchor: _bin/arm9.bin
anchor SHA-256: 1b70b078ec1b026004c89272acf619e7510e60fd294aa776b7bda48733ae850b
aggregate SHA-256: f64b99e2f976b30e5a667156bd7878f8a43c741bb1a5e02be1c6f40adc513226
```

`OracleHost.VerifyReference` verifies the extracted AMHE1 directory and its
aggregate. `OracleHost.VerifyRom` separately accepts an explicit private ROM
path only when the header identifies game code `AMHE`, ROM revision `1`, the
image is exactly 67,108,864 bytes, and its complete-image SHA-256 is
`bcd9c2d408825589c35c6754c0efb547cbae78fbda9ce7f69500a9cab8e70b8f`. The
path is never written to artifacts or committed. Without `--rom`, adapters
must emit `romIdentity: "unverified:<reason>"`; with it, artifacts bind to
`romIdentity: "sha256:<digest>"`. A deterministic external adapter is not
present here, so no retail run is claimed by this repository.

See [the oracle contract](../fidelity/ORACLE.md) for the schema and
[the testing lab](../TESTING_LAB.md) for evidence levels.
