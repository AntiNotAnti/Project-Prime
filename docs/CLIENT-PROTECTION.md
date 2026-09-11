# Project Prime client protection

## Boundary and policy

Protection is enabled only by `PrimeProtectClient=true` on a Release client
publish. Desktop additionally requires `PublishSingleFile=true`. Normal `build`,
`test`, `run`, server, Worker, Backend, editor, and tool flows remain unchanged.

Exactly these Project Prime-owned modules are processed in one Obfuscar invocation:

- `ProjectPrime.dll`
- `ProjectPrime.Game.dll`
- `Server.Shared.dll` (the copied client payload only)
- `ProjectPrime.Replay.dll`
- `Audio.Ncsf.dll`

Third-party modules are dependency search inputs only. The pinned provider is
`Obfuscar.GlobalTool` `3.0.0-beta.20`, policy `client-v1`. P0 keeps public APIs,
properties, events, special-name members, compiler-generated types, and strings;
it renames private/internal types, methods, and fields. It does not add DRM,
anti-debugging, anti-tamper, a runtime framework, secret storage, or NativeAOT.

## Preserve inventory

The source inventory was reviewed before enabling the pipeline:

| Surface | Name dependency | P0 handling |
| --- | --- | --- |
| Avalonia `*.axaml` | compiled `x:Class`, custom CLR element types, and their members | generator resolves `clr-namespace`/`using` elements and preserves each first-party type plus all members |
| `InputSettings` | reflects and persists `ClientPlayerBindings` property names | explicit full-type preserve |
| `NetLaunch` | reflects `Cheats` properties | explicit full-type preserve |
| `System.Text.Json` contracts | property/field contract names and generated contexts | global property/event preservation plus generated-code preservation |
| Desktop P/Invoke | default native entry point can derive from managed method name | explicit interop-type preserves |
| Android activities/services/JNI | Java-visible types and registration metadata | entire `MphRead.Droid.*` namespace preserved |
| Android `EsBindings` | native EGL entry point | explicit full-type preserve |
| OpenTK keyboard/mouse bridge | reflects third-party OpenTK members | OpenTK is not obfuscated; covered by protected APK runtime regression testing |
| `Audio.Ncsf.Common` fast list access | reflects private `List<T>`/`Memory<T>` framework members | framework assemblies are not obfuscated; no first-party exclusion |
| External interface contracts on internal types | Obfuscar can rename `IDisposable.Dispose`/`IAsyncDisposable.DisposeAsync` even though the runtime vtable contract cannot change | wildcard contract-method preserves in every protected module; equality-contract types are explicitly preserved |

Rules are reviewed XML under `build/protection`. They do not exclude an entire
first-party assembly merely because a narrow reflection or binding surface exists.
Property/event renaming and stronger public API renaming remain P2 work and require
package/runtime evidence before any policy change.

## Desktop build path

The desktop target runs after `PrepareForBundle` and before the SDK bundle input
cache and `GenerateSingleFileBundle`. It selects exactly one bundle item for every
expected module, clears its intermediate staging directories, copies all five,
generates an absolute-path configuration, runs Obfuscar once, validates the result,
then replaces only those five `FilesToBundle` entries while retaining their bundle
metadata. The supported .NET single-file bundler performs the final packaging.

Protected Linux CI and official releases run the resulting single-file executable
through `tools/protection/linux-client-smoke.py`. The bounded black-box check starts
`ProjectPrime -launcher -text`, feeds `q`, requires exit status zero, and requires
the stable versioned Project Prime text-launcher heading on stdout. It proves that
the protected Linux bundle reaches and exits its launcher; Windows x64 and both
macOS architecture launch checks remain external release-machine gates.

## Android build path

The insertion point was verified against the pinned .NET 10 Android SDK pack
`36.1.69`. Protection runs after `_PrepareAssemblies` and before
`_GenerateJavaStubs`: the five modules are already linked, while Java/JNI generation
and `_GenerateCompressedAssembliesNativeSourceFiles` have not yet consumed the
resolved assembly graph. A later insertion produced compressed-assembly native size
metadata from the original IL and was rejected by the runtime when the larger
protected assembly store loaded.

The target rejects untrimmed and NativeAOT builds and stages the five post-link
assemblies independently for `android-arm64` and `android-x64`. Each RID gets one
Obfuscar invocation over its complete five-module graph because post-link assemblies
can differ by RID. One protected `package/<rid>/<module>` identity is then substituted
into `_ResolvedUserAssemblies`, with metadata cloned from the original items. This
is the input used for compressed-native size metadata. `_ResolvedAssemblies`,
`_ResolvedUserMonoAndroidAssemblies`, and both shrunk sets remain canonical while
`_GenerateJavaStubs` consumes adjacent SDK-private `.jlo.xml` sidecars. After
`_RemoveRegisterAttribute` finishes its canonical rewrite, a second target swaps
only `_ShrunkAssemblies` and `_ShrunkUserAssemblies` to the already-created protected
identities. Canonical linked/shrunk files are never overwritten.
The aggregate staging root is resolved at target execution time beneath the
effective `obj/Release/<TFM>/prime-protection` directory rather than beside the
project. Protected RID processing is serialized for deterministic access to that
root.

The protected resolved-user graph is hash-checked immediately after its early
substitution. Immediately before `_CollectAssembliesToCompress`, a second
fail-closed target verifies that the late-selected
five selected `_ShrunkUserAssemblies` for every active RID still live under the
current `prime-protection/package/<rid>/` root and have the same hashes as the
corresponding Obfuscar outputs. Protected builds always invalidate Java/JNI stamps,
generated compressed-assembly native source/object/shared-library files, SDK LZ4
compression, and APK intermediates; the first unprotected build after a protected
build does the same. This prevents either mode from reusing size metadata or package
bytes produced by the other. Android AOT is explicitly disabled only for protected
clients until an equally verified protected-input AOT boundary exists.

After signing, `check-obfuscation.py verify-android-apk` opens each ABI-specific
`lib/<abi>/libassembly-store.so`, uses the installed .NET Android SDK's
`llvm-objcopy` to extract its `payload` section, parses the Mono XABA store, expands
XALZ/LZ4 assembly entries, and compares the embedded five modules to the matching
per-RID Obfuscar output hashes. It also requires the APK's assembly-store ABI set to
match the protected RID set exactly and rejects any non-empty store debug/config
payload descriptor. `unzip -t`, leakage and signing checks remain additional
final-package gates.

The executable device gate is `tools/protection/android-smoke.py`. It installs the
exact signed APK twice with `adb install -r` (the second install proves update
replacement), launches the manifest launcher for `com.antinotanti.projectprime`,
suppresses or dismisses Android's first-run immersive-mode confirmation, and uses a
bounded wait to require a live process, a resumed activity/window, and a
`uiautomator` marker from the Avalonia front shell. When Settings is available it
taps that control, requires a Settings-page marker, and injects Android back input.
On a clean no-content installation it instead requires the explicit game-file setup
surface, opens the accessible More destinations menu, requires its explicit route/
account/connection-details marker plus Settings and Close controls, then taps Close
and verifies that the overlay disappears while the no-content shell remains. (The
launcher intentionally gates every route, including Settings, until game files are
ready.) More and Close taps are retried only while their accessibility controls
remain visible, with a fixed interval and attempt cap; the explicit overlay state
changes remain mandatory. The process must remain alive and foreground after either
path.
Fatal AndroidRuntime, Java/JNI, native-signal, managed startup, OpenTK keyboard/
mouse bridge, and Avalonia renderer signatures fail the gate. Logcat, activity/
window dumps, UI XML, and a screenshot are captured for diagnosis. Visible
Avalonia UI plus direct Settings navigation or More-overlay open/close is the renderer/touch signal
available without proprietary content; gameplay and full renderer/audio/controller
initialization remain content-aware device gates.

The additive protected Android build job and official release both provision a
deterministic API 35 Google APIs x86_64 emulator with
`reactivecircus/android-emulator-runner` v2.38.0. The smoke is fatal in both paths;
official release no longer assumes an externally connected adb serial.

## Release operations

`tools/build-all.sh` enables client protection by default. Its diagnostic
`--no-client-protection` escape hatch marks the output unsuitable for public
distribution. Official release automation has no disable switch and never passes
the property to server commands.

For every official version retain the commit SHA, tool version, policy/config hash,
and mapping as a private CI artifact named `ProjectPrime-protection-map-vX.Y.Z`.
Mappings and generated XML must not enter public packages.

Runtime acceptance remains platform-specific: launch all four desktop executables;
on Android the automated front-shell startup gate runs first, then the content-aware
device matrix must exercise touch/controller navigation and the OpenTK reflection
bridge, initialize renderer/audio, and—with developer-owned content—run a local
gameplay smoke. Static success must not be reported as device proof.

## Upgrade checklist

An Obfuscar upgrade is one intentional commit. Rerun normal tests, protected Linux,
Windows, macOS x64, macOS ARM64, protected Android, Android device smoke, rename
statistics comparison, startup/size measurements, and representative ILSpy review.
