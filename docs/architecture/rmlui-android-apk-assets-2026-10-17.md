# Android RmlUi APK asset extraction — October 17, 2026

## Scope and owner

`RmlUiAndroidApkAssets` is an isolated foundation behind `PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT`. It reads only the package's `rmlui` namespace through `IRmlUiAndroidPackageAssets`; the Android adapter wraps the existing `AssetManager`. It creates no Activity, View, EGL context, scene, native UI, launcher route or network session. No activation or default selection is added.

The APK remains the authoritative inventory. The existing package items include the shared RML97 documents, components, RCSS themes, four fonts and their licenses, native license files, and the arm64-v8a/x86_64 native manifests. This helper copies their bytes without regenerating those assets or trusting a build-generated extraction manifest. It requires `prime_home.rml`, then fingerprints every packaged file under `rmlui`. The native `.so` remains an Android native library rather than an extracted document asset.

The caller supplies an absolute **app-private** cache directory, such as a child of the application's private cache directory. Managed containment and link checks assume another writer cannot modify that directory during extraction. They reject ordinary traversal and existing symbolic links; they are not an `openat`/`O_NOFOLLOW` guarantee against an adversarial process racing file operations.

## Bounded inventory and verified publication

Default limits are 4,096 files, 8,192 entries, directory depth 16, 32 MiB per file and 128 MiB total. Callers can lower these limits. Entry names are restricted to ASCII letters, digits, dot, underscore and hyphen; empty names, `.`/`..`, separators, case collisions, overlong names/paths and the reserved extraction receipt name are rejected. Every destination is resolved beneath a fresh `.rmlui-stage-<guid>` directory owned by this invocation.

The extractor counts bytes actually streamed, copies with exclusive create, flushes the file, and compares its SHA-256 with a disk readback. It then sorts the inventory by ordinal relative path. The inventory fingerprint is SHA-256 over the versioned `PP-RMLUI-APK-INVENTORY-1` binary framing of path, byte length and file SHA-256. A deterministic JSON receipt records that inventory and total bytes. A full staging-tree verification checks all file hashes, receipt bytes, directories and absence of unexpected entries **before publication**.

The same-parent `Directory.Move` publishes the verified tree as `rmlui-<fingerprint>`. A matching existing root is reused only after exact tree, size, SHA-256 and receipt verification. A concurrent publisher's destination is verified by the same rule. Corrupt existing roots fail closed; the helper never overwrites or deletes a published root. A package content change produces another root while preserving any root an existing native host may still read. Published contents are immutable by caller policy; this helper does not change filesystem permissions.

Cancellation is cooperative between synchronous package/disk reads; it cannot interrupt an APK read blocked inside the platform. Cancellation or failure before the atomic publication point retires this invocation's staging tree. Cancellation detected after that point may leave a complete verified root without returning it. It is deliberately retained because another host may already be reading it; a subsequent invocation verifies it before reuse. Cache pruning is the caller's responsibility after all native sessions using a root have shut down.

## Integration seam

```csharp
var extracted = RmlUiAndroidApkAssets.Extract(
    new RmlUiAndroidPackageAssets(assetManager),
    appPrivateCacheDirectory,
    cancellation: cancellationToken);
// On the existing current EGL owner, initialize the existing session once.
bool initialized = session.Initialize(width, height, density, extracted.Root);
```

The returned root has no trailing directory separator, matching the shared host's document containment checks. Extraction performs file I/O only and may precede EGL startup. The existing EGL owner must initialize and retire the session; this helper must not start another loop or scene. See the [input and surface foundation](rmlui-android-input-foundation-2026-10-17.md) for the owner-thread and lifecycle seams.

## Verification and limits

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/rmlui-android-assets-check/rmlui-android-assets-check.csproj -c Release
```

The source-only fixture links the production extractor and uses a fake package reader plus a real temporary local filesystem. **42 checks passed**: sorted byte-exact RML/RCSS/font/license/manifest copy, repeat reuse and package changes, SHA corruption, unexpected directories, receipt tampering, path/case rejection, all budgets, missing home, pre-cancellation, mid-stream cancellation after a partial copy, interrupted streams, staging cleanup and directory/file/dangling symbolic links. The frozen synthetic inventory fingerprint is `158ea4965bc16474c0af9b3bb1c276ce94401d567f2973bb010a5eb7e37b85b3`; it describes the ten fake fixture files, not a real APK.

The fixture output is preserved in [asset-source-fixture.txt](../evidence/rmlui-android-foundation-2026-10-17/asset-source-fixture.txt). Independent read-only review found no further containment/hash/ownership defect under the app-private writer assumption and requested the staged receipt verification and mid-stream cancellation coverage included here.

This result does not establish an Android compile, real APK inventory, installed asset extraction, native ABI compatibility, startup, input method behavior, GLES rendering, surface recovery or physical-device acceptance. The root owner coordinates the actual Android compile and later opt-in launcher integration separately.
