# Hosted package cache lifetime checks

Run from the repository root with .NET 10:

```sh
dotnet run --project tools/hosted-cache-check -c Release
```

This BCL-only fixture links the exact production hosted cache and canonical
process-incarnation helper; it does not build either application or use game
assets. An independent child owns a private library while separate reaper
processes apply real cache-budget pressure. The checks preserve library and
original archive bytes for exact live, unverified, malformed and legacy owner
records; reject a genuinely different incarnation; retain the startup handoff
window; and reclaim an exited owner's library and pins.

Linux/Android owner identities use the kernel boot UUID, PID and procfs field22.
An unavailable identity retains a live owner conservatively. No UTC tolerance
or PID-only ownership match permits deletion.
