# Build & Deploy — servers and deployment

Deployment notes and commands.

Deploy script (server and directory)

```bash
# server and directory (rebuilds ARM64, installs both units, restarts them)
MPH_SERVER_HOST=rebooty.xyz MPH_SERVER_USER=projectprime \
  MPH_SERVER_PASS="$(read -rsp 'pi password: ' p; echo "$p")" ./deploy-server.sh
# MPH_DEPLOY_MASTER=0 to leave the directory alone
```

Publish commands (Windows client and server)

```bash
# Windows client
dotnet publish src/Client/Client.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -o publish/win-x64

# Windows dedicated server
tools/package-server.sh --rid win-x64 \
  --map-artifacts artifacts/maps/current --output publish/server-win-x64
```

Notes

- The client and Node/Worker are separate projects and packages; there is no
  server build personality inside Client.
- Protocol compatibility is enforced by the current protocol constants and
  refusal handshake. Do not copy a historical numeric version from this guide;
  inspect `src/Game/Protocol` and `docs/CURRENT_PROTOCOL.md`.
