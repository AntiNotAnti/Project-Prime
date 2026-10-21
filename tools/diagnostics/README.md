# Windows crash and frame-pacing diagnostics

The game captures its own regular and native-stderr logs when **Debug Logs**
is enabled. A native or abrupt termination may not leave a managed exception,
so full .NET crash dumps are an **opt-in** troubleshooting tool.

From PowerShell in the release installation directory:

```powershell
& "C:\path\to\Project-Prime\tools\diagnostics\Run-ProjectPrimeWithCrashDumps.ps1" -Executable ".\ProjectPrime.exe"
```

The script starts the Windows game with `DOTNET_DbgEnableMiniDump=1`,
`DOTNET_DbgMiniDumpType=4` (required for single-file builds) and a dump name
under the installation's `logs` folder, and waits for the game to exit.
Remove old full dumps when done: they may be **hundreds of megabytes** each
and can contain private information from process memory.

To attribute an unexplained FPS collapse, also start the game with
`PROJECT_PRIME_GL_PROFILE=1` (or the `-glprofile` argument). Its
`[glframe]` records split CPU rendering, present wait and frame intervals.
A sudden drop from 1000+ FPS to 9 FPS by itself does not tell which component
blocked. Compare the `[frametiming!]` and `[glframe]` windows at the same time.

This script does not install a global Windows crash dump handler or change
release defaults. Fatal native faults that bypass the .NET dumper may still
require Windows Error Reporting or an external debugger.
