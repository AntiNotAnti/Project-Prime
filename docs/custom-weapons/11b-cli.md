# CW-11b offline package commands

The desktop and dedicated-server binaries dispatch `-weapon` before game-file
setup. These commands run bounded CPU validators without graphics, audio devices
or extracted game files. Installing a package does not activate or grant a weapon.

```text
ProjectPrimeServer -weapon export ./my-rifle
ProjectPrimeServer -weapon build ./my-rifle ./my-rifle.ppweapon
ProjectPrimeServer -weapon validate ./my-rifle.ppweapon
ProjectPrimeServer -weapon install ./my-rifle.ppweapon ./weapon-store
ProjectPrimeServer -weapon list ./weapon-store
ProjectPrimeServer -weapon uninstall <package-sha256> ./weapon-store
ProjectPrimeServer -weapon prune ./weapon-store
```

Export creates an editable project containing the original bundled reference
assets and `weapon.json`. It excludes the generated package manifest. Build reads
only the project's declared resources and uses the shared deterministic builder;
unrelated files are ignored. Validation decodes the actual final archive, model
and audio and verifies resource hashes and the self-contained dependency lock.
Successful build/validate/install reports archive, content, dependency and
mechanics identities. List identifies installed revisions by archive digest.

Output parents must already exist; export destinations and build output files
must be new. Sibling staging and non-overwriting promotion prevent partial final
outputs and accidental replacement. Input reads have per-resource and aggregate
budgets; archive input is limited to 8 MiB. Symlinks/reparse points anywhere in
an input/output ancestor chain are rejected. Use privately owned authoring and
store directories: managed path checks do not prevent a different process with
write access from concurrently renaming an ancestor. Terminal diagnostics are
ASCII sanitized and bounded. Exit codes: 0 success, 1 operation failure, 2 usage.

Android validation remains deferred by user scope. Runtime package readiness,
match activation and replay binding belong to CW-12.
