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

The explicit checkout refuses existing destinations. Build refuses another HEAD
or altered patch input. The one fork patch restores `mesh_t` copy assignment,
needed by `entity_t` copy assignment with current C++ standard libraries. Build
receipts pin source, patch bytes, dependency versions and output hashes. Archive
reproduction also requires retaining the exact compiler and dependency packages;
the workflow saves their version inventory but **does not claim identical future
package-manager resolutions or byte-reproducible binaries**.

Linux requires Qt5, glib, libxml2, JPEG, PNG, Assimp, OpenGL and build tools.
Windows uses the upstream MSYS2/MINGW64 toolchain. The candidate workflow builds
these platforms and runs the actual compiler; its future execution is separate
from local source acceptance. On macOS `--compiler-only` builds native Q3Map2;
the upstream editor's X11/Qt5 GUI remains unqualified. Native JSON Studio is the
supported fallback pending an actual native-editor test.

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
