# Opt-in native desktop client

The desktop client can compile with `MphReadAvalonia=false` and
`MphReadRmlUi=true`. Its engine-window adapter reuses the current native page
presenters, Core controllers, match loader, lobby controller and Studio broker.
The default build and the independent Studio application retain their existing
toolkit choice until migration acceptance.

Run the actual client project in a snapshot of the current working tree:

```sh
python3 tools/native-client-check/check.py --dotnet /path/to/dotnet
```

Add `--server` to check a separate dedicated-server snapshot with
`MphReadServer=true`, RmlUi disabled and no native UI/font payload.
Add `--legacy` instead to check the transitional client with both presentation
implementations available for runtime selection and rollback.
Use `--default-client` to check the unchanged ordinary desktop build with the
native feature disabled, including its explicit unavailable-presentation error.

This copies current source, including uncommitted changes, into a temporary
directory outside the checkout. The actual project references build there with
their own `bin` and `obj` directories. The check rejects Avalonia packages in
the restored graph, runtime dependency entries and client output assemblies.
It also requires the real Studio protocol and the independent Skia HUD bitmap
dependency. Shared native artifacts and maps remain read-only build inputs.
Each snapshot writes `source-manifest.json` with the copied source hashes and
`boundary-check.json` with the built assembly hash and dependency result.

The equivalent opt-in build is:

```sh
dotnet build src/MphRead/MphRead.csproj -c Release \
  -p:MphReadAvalonia=false -p:MphReadRmlUi=true
```

Runtime captures use the resulting client and the same `-rmluipocshot`,
`-rmluipage` and `-rmluisize` diagnostics as the transitional native presenter.
Platform acceptance and physical input checks remain separate requirements;
the compile boundary alone does not establish them.
