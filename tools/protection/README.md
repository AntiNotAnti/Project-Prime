# Client protection tools

Project Prime protects only the five first-party managed assemblies distributed
inside client packages. Obfuscation is a release build step, not an application
dependency. The input is always a fresh copy and canonical `bin`/linked output is
never overwritten.

The tool version is pinned in `.config/dotnet-tools.json`. Restore it once with:

```bash
dotnet tool restore
```

Protected desktop publish example:

```bash
dotnet publish src/Client/Client.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishSingleFile=true -p:PrimeProtectClient=true
```

`generate-obfuscar-config.py` accepts only explicit absolute build paths, validates
the checked-in rule fragments, scans `src/**/*.axaml` for `x:Class` and first-party
custom CLR elements, preserves those types and all members, and writes the ephemeral
Obfuscar configuration under the project intermediate directory. Common rules also
preserve external disposal contract method names on internal types and the reviewed
equality-contract types.

Obfuscar 3.0.0-beta.20 currently omits ECMA-335 `FieldMarshal` rows when it rewrites
an assembly. It can also rewrite the framework `System.ValueType` constraint used by
`unmanaged` generic parameters as a reference scoped to the protected assembly itself,
and it emits zero-instruction bodies for C# default interface implementations.
Immediately after each Obfuscar run, the release-only `MetadataRepair` tool copies
field, parameter, and return-value marshal descriptors from the staged input and
repairs only that known bad generic-constraint scope. It also restores each stripped
default-interface body while remapping its IL references to the jointly obfuscated
five-module graph. Repairs use matching metadata tokens and generic-parameter positions.
The tool validates parameter attributes, constraint counts, framework scope, the complete
marshal-descriptor count, and stable default-interface IL profiles before atomically
replacing the protected output. This keeps binary structures, P/Invoke parameters, ROM
extraction, constrained generic methods, and optional interface hooks functional without
mutating canonical build outputs. A missing module, changed token/signature, unexpected
constraint/body shape, or incomplete repair is fatal. The repair runs for all five
modules even when a module needs no repair.

`check-obfuscation.py protected` checks the five staged inputs, outputs, mapping,
rename totals, preserve rules, hashes, and PE debug directories.
`check-obfuscation.py public PATH...` rejects mapping/config/staging files and
PDB/MDB data in a public directory, ZIP, or APK. `stage-android` groups post-link
assemblies by RID and copies a fresh five-module input graph for each RID without
altering the linker's files. Each RID is obfuscated independently because its
post-link bytes can differ. `package-android` reads that staging inventory and
copies each exact `output/<rid>/<module>` into its matching protected package path,
verifying every hash before MSBuild substitutes the metadata-preserving items.
Android protection runs after `_PrepareAssemblies` and before `_GenerateJavaStubs`,
replacing only `_ResolvedUserAssemblies` so compressed-native size metadata uses
protected byte sizes while Java/JNI generation retains canonical assemblies and
their adjacent `.jlo.xml` sidecars. After `_RemoveRegisterAttribute`, a second target
replaces `_ShrunkAssemblies` and `_ShrunkUserAssemblies` with the same protected
`package/<rid>/<module>` identities. `verify-android-routing` checks the early
resolved-user boundary and the late final-shrunk boundary. For every active RID it
requires exactly the five
first-party `_ShrunkUserAssemblies`, requires every identity to be under the
current `prime-protection/package/<rid>/` root, and compares it byte-for-byte with
the corresponding Obfuscar output. `verify-android-apk` extracts each packaged
`lib/<abi>/libassembly-store.so` XABA payload with the installed .NET Android SDK's
`llvm-objcopy`, expands XALZ entries, and compares the actual embedded five
assemblies with their per-RID protected output hashes. It rejects unexpected ABI
stores and any store entry carrying debug/config payload descriptors. Protected
Android builds disable AOT until it can consume the post-protection graph safely.

`android-smoke.py` is the black-box gate for an exact signed protected APK. It
requires one ready adb device (or an explicit `--serial`), installs the exact APK
twice with `adb install -r` to prove update replacement, resolves and launches
`com.antinotanti.projectprime`, and waits at most 45 seconds for a live PID,
resumed activity/window, and an accessibility-tree marker from the Avalonia front
shell. It taps Settings, requires a Settings-page marker, and injects back input.
On a clean installation with no developer-owned game content, it instead requires
the explicit game-file setup surface, opens More destinations, verifies its labeled
route/account/connection overlay and accessible controls, then closes it by touch and
requires the no-content shell again. More/Close taps use bounded, count-capped
accessibility-driven retries so an input dropped during initial UI stabilization does
not become a false failure. The process must remain alive and foreground.
Android's
first-run immersive confirmation is suppressed and dismissed if it still appears.
Fatal Java, JNI, native, managed, OpenTK bridge, and Avalonia renderer signatures
fail the run.
Logcat, activity/window state, UI XML, and a screenshot are retained in the
requested artifact directory on success or failure:

```bash
python3 tools/protection/android-smoke.py \
  --apk /absolute/path/ProjectPrime-Signed.apk \
  --serial emulator-5554 \
  --artifacts /tmp/project-prime-android-smoke
```

The smoke intentionally stops at the launcher when developer-owned game content
is unavailable. Its visible Avalonia shell and direct Settings or More-overlay touch
navigation cover the safe UI/renderer surface available there; gameplay, audio and
full controller/OpenTK interaction remain separate content-aware device tests.

`--serial` selects an already-connected entry whose state is `device` in
`adb devices`; it does not connect or provision one. The protected build and
release workflows provision their own API 35 x86_64 emulator; local callers must
still provide an adb-visible device themselves.

`linux-client-smoke.py` launches the actual protected `linux-x64/ProjectPrime`
single-file executable with `-launcher -text`, feeds `q`, requires a clean exit
within 20 seconds, and checks stdout for the versioned Project Prime text-launcher
heading. Protected Linux CI and official releases run this black-box package check.

`Mapping.txt`, generated XML, routing data, and configuration hashes are private
release diagnostics. Never copy them into a client directory, ZIP, APK, update
payload, or public release repository.

There is deliberately no unprotected fallback. A restore, generator, Obfuscar, or
verification failure fails the publish.
