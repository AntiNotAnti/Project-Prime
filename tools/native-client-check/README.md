# Native desktop client

The ordinary game client uses RmlUi and has no Avalonia dependencies. The independent Studio application retains its toolkit. An explicit `MphReadAvalonia=true` compatibility build includes the legacy client for rollback.

Check actual current source in separate snapshots:

```sh
python3 tools/native-client-check/check.py --default-client --dotnet /path/to/dotnet
python3 tools/native-client-check/check.py --legacy --dotnet /path/to/dotnet
python3 tools/native-client-check/check.py --server --dotnet /path/to/dotnet
python3 tools/default-client-check/check.py --dotnet /path/to/dotnet
```

`--default-client` builds the actual project without presentation properties and rejects Avalonia packages, dependency entries and output assemblies. `--legacy` explicitly enables the compatibility property; `--server` checks the UI-free dedicated server. Snapshots contain current uncommitted source and own their bin/obj directories. Actual native artifacts/maps remain read-only inputs.

Ordinary publication requires a strict RmlUi bridge for its RID before publishing. Runtime `-ui legacy` requires the compatibility package. If native-only automatic startup is blocked after a prior failure, install the previous version or transitional package; an explicit `-ui rmlui` trial preserves the failure witness until a real surface presentation succeeds. Compile/package checks alone do not establish graphics, input or performance acceptance.
