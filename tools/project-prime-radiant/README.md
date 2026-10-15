# Project Prime Radiant source candidate

This separate fork recipe pins NetRadiant-custom
[`8216133984031afaa9a857b56ea66dd9c3d54b26`](https://github.com/Garux/netradiant-custom/tree/8216133984031afaa9a857b56ea66dd9c3d54b26).
It is never linked into Game/Studio and never downloads gamepacks or binaries.

```sh
python3 tools/project-prime-radiant/build.py checkout --source /absolute/new/radiant-source
python3 tools/project-prime-radiant/build.py build --source /absolute/new/radiant-source --jobs 2
python3 tools/project-prime-radiant/real_tool_check.py --q3map2 /absolute/local/q3map2 --output /absolute/new/acceptance
python3 tools/project-prime-radiant/build.py bundle --source /absolute/new/radiant-source --output /absolute/new/candidate
```

On macOS the full build is a native Cocoa Qt5 editor plus Q3Map2, with no X11
requirement. Install Qt5, glib, libxml2, JPEG, PNG, Assimp and build tools first.
The build uses `/opt/homebrew/opt/qt@5` on ARM64 or `/usr/local/opt/qt@5` on Intel;
set `PROJECT_PRIME_QT_PREFIX` to another absolute Qt5 prefix when needed.
`--compiler-only` remains available for Q3Map2 alone.

Package an existing full ARM64 candidate on an ARM64 Mac with Apple's command
line tools and Qt5's `macdeployqt`:

```sh
python3 tools/project-prime-radiant/package_macos.py --candidate /absolute/new/candidate --output "/absolute/new/Project Prime Radiant.app" --qt-prefix /opt/homebrew/opt/qt@5
python3 tools/project-prime-radiant/package_macos.py --validate-app "/absolute/new/Project Prime Radiant.app" --rid osx-arm64
```

The packager closes non-system dependencies inside the app, includes Cocoa/SVG
plugins, flattens framework symlinks, records provenance and verifies ad hoc
signatures. The result is a symlink-free qualification candidate; it is not
notarized. Packaging refuses existing output paths and never builds or downloads.
To include that prebuilt app as the third sibling in a paired Mac archive, pass
it as the optional sixth argument after the Game and Studio publish directories:

```sh
tools/package-macos.sh /absolute/game-publish /absolute/dist osx-arm64 0.1.0 /absolute/studio-publish "/absolute/new/Project Prime Radiant.app"
```

The paired packager validates the app before copying and after archive extraction.
Keep all three apps and hidden paired metadata together. Native app resources
live in `Contents/Resources/netradiant`; the original game descriptor is at
`gamepacks/games/projectprime.game` beneath that resource root.

The explicit checkout refuses existing destinations. Build refuses another HEAD
or altered patch input. The recorded portability patch restores `mesh_t` copy
assignment, needed by `entity_t` with current C++ standard libraries, and directly
constructs zero vectors in the draw-vertex initializer for GCC 13's explicit-
constructor rules. It also replaces temporary C arrays in VFS, brush tools and
editor dialogs with named arrays, preserving the suffix guard before indexing.
Remote Linux candidates exposed these issues; failed runs remain recorded.
Build
receipts pin source, patch bytes, dependency versions and output hashes. Archive
reproduction also requires retaining the exact compiler and dependency packages;
the workflow saves their version inventory but **does not claim identical future
package-manager resolutions or byte-reproducible binaries**.

Linux requires Qt5, glib, libxml2, JPEG, PNG, Assimp, OpenGL and build tools.
Windows uses the upstream MSYS2/MINGW64 toolchain. The candidate workflow builds
these platforms and runs the actual compiler; that evidence is separate from
GUI acceptance. Studio presents **Edit Geometry in Radiant** as the primary
geometry action with **Review / Apply** on return, and an explicit **Native
geometry fallback**.

October 15 source revision `b3339620` passed full editor/compiler builds and real
BSP checks on Windows and Linux CI. Linux uploaded its candidate; Windows found
an upload-only C:/D: path error, now corrected with separate platform/receipt
uploads. GUI usability, signing and dependency/license qualification are still
pending. Current job results are linked from the implementation ledger.

A bounded native Mac open/edit/save/quit loop passed on October 15, including
existing geometry, new brushes, Duplicate, Save As and Undo/Redo. Strict Studio
review, metadata preservation and package build/reload passed on the actual
saved files. Broader focus/DPI, UV/angled/patch authoring and content coverage,
Windows/Linux GUI checks and release qualification remain separate. See the
[acceptance ledger](../../docs/architecture/studio-radiant-implementation.md).

The real-tool check creates original graybox source and checkerboard pixels and
requires a real BSP46 with collision brushes and draw surfaces. It records
compiler/source/output SHA-256 and logs. A successful process exit producing an
empty BSP fails. It does not assert GUI, gameplay, performance or device parity.

Candidate bundles include the upstream source archive, exact patches/recipe,
original gamepack and upstream LICENSE/GPL/LGPL/CONTRIBUTORS. Dependency license
and corresponding-source review, signing and platform GUI checks remain required
before redistribution. The `QUALIFICATION-PENDING.txt` is intentional. Root MIT
licensing does not relicense the external fork. No commercial game content or
cartridge files may enter this bundle.
