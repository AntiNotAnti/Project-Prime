# Normal match startup regression

Uses real configured owned assets, an isolated profile and the production
`RenderWindow(shell:true)` / `MatchStart.Begin` offline CTF path with seven bots,
local team zero and 4v4 rules. Two matches reuse the same OS window. Every match
first draws through `RenderWindow.OnRenderFrame(FrameEventArgs(0))` after
`FrameTiming.Reset`, without a preceding simulation update or forced spawn.
Only after that draw succeeds are zero fixed steps and camera inverse checked.
It then runs at least 120 normal fixed steps, checks enabled bot navigation and
objective bindings, and tears down the scene before the next match.

Set `PROJECT_PRIME_USER_DATA` to a private absolute profile. Its `paths.txt` must
point to cloned owned assets; optional copied launcher/settings files must also
be private. No account/license/session files are needed. The tool never edits
camera pose, actor state, or source geometry. Linear color defaults to true to
exercise the material-camera inversion that failed before the first fixed step.

Build against the intended prebuilt client with `BuildProjectReferences=false`
and the same configuration, RuntimeIdentifier and artifacts root as that client.
Stage its normal native/content dependencies beside the tool as for other GL
harnesses. Do not rebuild the client when obtaining the failing baseline.

```powershell
dotnet build tools/match-startup-check/match-startup-check.csproj -c Release -p:RuntimeIdentifier=win-x64 -p:BuildProjectReferences=false -p:UseSharedCompilation=false
dotnet tools/match-startup-check/bin/Release/net10.0/win-x64/match-startup-check.dll --room "AD2 ALINOS PERCH" --output C:/absolute/private/result.json
```

Use the exact runtime room key for Fault Line in a separate run. Reports retain
the loaded client hash/MVID/version, harness hash, original exception stack,
first-draw camera data and match/readiness/cleanup state. This checks content
startup and rematch, not physical route traversal or Internet multiplayer.
