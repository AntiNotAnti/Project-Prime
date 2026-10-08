# Runtime UI selection and startup rollback

```sh
dotnet run --project tools/launcher-ui-policy-check -c Release
```

The source-linked check exercises actual bounded JSON persistence, private file
permissions, interrupted startup, explicit retry, successful presentation,
rollback UI/renderer pairing, target acceptance and unavailable compile paths.
It opens no UI, account service, or gameplay transport.
Capture and performance diagnostics retain the actual selection parser and
recovery decisions while leaving the user's startup record untouched, including
interrupted-startup detection during record loading.

`process-check.py --dotnet /path/to/dotnet --native /path/to/native/ProjectPrime.dll
--transitional /path/to/transitional/ProjectPrime.dll --default-client
/path/to/default/ProjectPrime.dll` checks the actual build variants. It creates
fresh diagnostic user-data fixtures, requires nonzero rejection before any
window is created, checks explicit legacy precedence over the old native alias,
and verifies that these diagnostic runs leave no startup recovery record.

`-ui=auto|rmlui|legacy` also accepts separated values. Existing native development
flags remain aliases when no explicit UI option is supplied. The accepted RID
catalog is intentionally empty until target acceptance, so the ordinary
transitional build keeps its legacy default. A native-only compile uses its
available native presentation. A failed or interrupted native attempt blocks
future automatic canary selection; an explicit native retry clears that block
only after a successful physical surface presentation. Explicit renderer flags
take priority over automatic renderer rollback.
