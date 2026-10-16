# Isolated Quake .pk3 converter

This is a standalone **conversion-only** executable. It never starts the
map server or writes to its catalog. It never accepts a creator token and has
no upload, moderation, publication or auth logic.

It accepts a vetted `.pk3` archive with one or more Quake III
`maps/*.bsp` levels and writes exactly one portable `.ppmap` package.
The conversion uses the existing `Q3ImportService` and
`MapPackageBuilder`; immutable multiplayer map identities and v2/v3
package format remain unchanged.

Build for the server host (Linux example):

```bash
dotnet publish tools/pk3-converter/pk3-converter.csproj -c Release \
  -r linux-x64 -p:MphReadServer=true --self-contained true \
  -p:PublishSingleFile=true -o ./artifacts/pk3-converter
```

Run in a private, ephemeral working directory, ideally a dedicated
container or a restricted `systemd` service with:

- Unprivileged service UID with no access to private game files
- Read-only executable/runtime, writable ephemeral source/output only
- `MemoryMax`, `CPUQuota`, `TasksMax`, `RuntimeMaxSec`,
  `ProtectSystem=strict`, `NoNewPrivileges`, network isolation
- No Supabase service role, creator token, production map storage secrets
- Per-request archive size and decompression budget, CPU/memory/time limits

CLI:

```sh
pk3-converter --source input.pk3 --output converted.ppmap --room Q3_ARENA
pk3-converter --source input.pk3 --output converted.ppmap --room Q3_ARENA --level q3dm17
```

Standard output: one JSON object with the new MapId, exact package SHA-256,
content hash, package byte length, BSP level, and bounded warnings. Progress
and generic errors go to standard error.

The server-side web queue and browser UI are separate slices; do not let
conversion bypass the existing authenticated Community publisher.
Use only maps and textures the uploader has the rights to redistribute.
