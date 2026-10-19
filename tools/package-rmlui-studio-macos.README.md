# Native Prime Studio sibling package

Given a frozen, signed RmlUi game bundle containing its own .NET runtime and
client, create its separate Studio sibling:

```sh
python3 tools/package-rmlui-studio-macos.py \
  '/absolute/delivery/Project Prime.app' \
  '/absolute/delivery/Project Prime Studio.app'
```

The output must not exist and must share the source app's parent directory.
The script requires macOS, Xcode command-line tools, and Python 3.9 or newer.
It never builds managed/game code, downloads dependencies, reads game data,
launches either app, or installs an application. The input app must already
contain the `--studio` startup role in `Resources/client/ProjectPrime.dll`.

Every regular input file is copied with `clonefile` on APFS. These copies have
independent inodes and copy-on-write storage; no hard links are used. Filesystems
without clone support receive independent ordinary copies. Internal symlinks are
preserved; links escaping the input app are refused. Runtime and client remain
inside the Studio bundle, independent of the game bundle.

The old outer resource seal and game bootstrap are removed from the new copy.
Unchanged nested native/runtime signatures are retained and individually verified.
A new native bootstrap with the input bootstrap's arm64/x86_64 architectures
executes the Studio bundle's own runtime and client, adding `--studio` before the
original arguments. It sets the same bundled-runtime and native-RmlUi environment
as the game bootstrap. Other inherited environment, including isolated user data
and the one-shot IPC launch capability, is preserved.

The Studio plist uses executable `ProjectPrimeStudio`, display/name
`Project Prime Studio`, and identifier `com.projectprime.studio`; version numbers
are preserved. Game document, URL and UTI associations are removed. No legacy
Studio ICNS was found in donor trees `31c3b20` or `d08107a`, so the existing icon is
retained. Supply `--icon /absolute/Studio.icns` to install a distinct existing icon.

The script ad-hoc signs the new bootstrap and outer app with the verified input
bootstrap's entitlement policy, checks each nested Mach-O signature, and verifies
both bundles with `codesign --verify --deep --strict`. This verifies local seals;
it does not notarize the output. A failure leaves the newly created output for
inspection; remove that output or choose a fresh path before retrying.

Standalone packaging fixtures:

```sh
python3 tools/test_package_rmlui_studio_macos.py
```

The fixture creates tiny temporary signed apps, checks a complete clone/sign cycle
and copy independence, and launches no app or runtime. This tool is separate from
`package-macos.sh` because that script packages the ordinary publish layout,
whereas this adapter specifically requires the RmlUi bundle's Resources runtime.
